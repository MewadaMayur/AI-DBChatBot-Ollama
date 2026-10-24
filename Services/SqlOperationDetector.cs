
using System.Text.RegularExpressions;

namespace DBChatbot.Services;

public sealed class SqlOperationDetector
{
    public bool HasOperationOnTable(
        string definition,
        string tableName,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(definition) ||
            string.IsNullOrWhiteSpace(tableName) ||
            string.IsNullOrWhiteSpace(operation))
        {
            return false;
        }

        var sql = Regex.Replace(
            definition,
            @"\[(?<name>[^\]]+)\]",
            "${name}");

        var table = Regex.Escape(tableName);

        string pattern = operation.ToUpperInvariant() switch
        {
            "INSERT" =>
                $@"\bINSERT\s+(?:INTO\s+)?(?:\w+\s*\.\s*){{0,2}}{table}\b",

            "UPDATE" =>
                $@"\bUPDATE\s+(?:\w+\s*\.\s*){{0,2}}{table}\b\s+(?:AS\s+\w+\s+)?SET\b",

            "DELETE" =>
                $@"\bDELETE\s+(?:FROM\s+)?(?:\w+\s*\.\s*){{0,2}}{table}\b",

            "SELECT" =>
                $@"\bFROM\s+(?:\w+\s*\.\s*){{0,2}}{table}\b|\bJOIN\s+(?:\w+\s*\.\s*){{0,2}}{table}\b",

            _ => throw new ArgumentException(
                "Supported operations: INSERT, UPDATE, DELETE, SELECT.")
        };

        return Regex.IsMatch(
            sql,
            pattern,
            RegexOptions.IgnoreCase);
    }

    public List<string> FindUpdateEvidence(
    string definition,
    string tableName)
    {
        var evidence = new List<string>();

        if (string.IsNullOrWhiteSpace(definition) ||
            string.IsNullOrWhiteSpace(tableName))
        {
            return evidence;
        }

        var sql = Regex.Replace(
            definition,
            @"\[([^\]]+)\]",
            "$1");

        var table = Regex.Escape(tableName);

        var pattern =
            $@"\bUPDATE\s+(?:(?:\w+)\s*\.\s*){{0,2}}{table}\b" +
            @"\s+(?:AS\s+\w+\s+)?SET\b" +
            @"[\s\S]*?" +
            @"(?=;|\bUPDATE\b|\bINSERT\b|\bDELETE\b|" +
            @"\bSELECT\b|\bIF\b|\bELSE\b|\bEND\b|" +
            @"\bCOMMIT\b|\bROLLBACK\b|--|$)";

        foreach (Match match in Regex.Matches(
            sql,
            pattern,
            RegexOptions.IgnoreCase))
        {
            var statement = match.Value.Trim();

            if (statement.Length > 1500)
            {
                statement = statement[..1500] + "...";
            }

            evidence.Add(statement);
        }

        return evidence;
    }

    public List<string> ExtractUpdatedColumns(string statement)
{
    var columns = new List<string>();

    if (string.IsNullOrWhiteSpace(statement))
        return columns;

    var setMatch = Regex.Match(
        statement,
        @"\bSET\b(?<setClause>[\s\S]*?)(?=\bWHERE\b|$)",
        RegexOptions.IgnoreCase);

    if (!setMatch.Success)
        return columns;

    var assignments = SplitSqlAssignments(
        setMatch.Groups["setClause"].Value);

    foreach (var assignment in assignments)
    {
        // Only inspect the left side of the assignment.
        var equalsIndex = assignment.IndexOf('=');

        if (equalsIndex < 0)
            continue;

        var leftSide = assignment[..equalsIndex].Trim();

        // Supports column names with optional table aliases.
        var columnMatch = Regex.Match(
            leftSide,
            @"(?:\w+\s*\.\s*)?(?<column>\w+)$");

        if (columnMatch.Success)
        {
            var column = columnMatch.Groups["column"].Value;

            if (!columns.Contains(
                column,
                StringComparer.OrdinalIgnoreCase))
            {
                columns.Add(column);
            }
        }
    }

    return columns;
}

private static List<string> SplitSqlAssignments(string setClause)
{
    var assignments = new List<string>();
    var current = new System.Text.StringBuilder();

    var parentheses = 0;
    var insideString = false;

    for (int i = 0; i < setClause.Length; i++)
    {
        char c = setClause[i];

        if (c == '\'')
        {
            current.Append(c);

            // SQL escapes a single quote as two single quotes.
            if (insideString &&
                i + 1 < setClause.Length &&
                setClause[i + 1] == '\'')
            {
                current.Append(setClause[++i]);
                continue;
            }

            insideString = !insideString;
            continue;
        }

        if (!insideString)
        {
            if (c == '(')
                parentheses++;
            else if (c == ')')
                parentheses--;

            if (c == ',' && parentheses == 0)
            {
                assignments.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
        }

        current.Append(c);
    }

    if (current.Length > 0)
        assignments.Add(current.ToString().Trim());

    return assignments;
}

}
