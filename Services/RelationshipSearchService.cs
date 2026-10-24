
using DBChatbot.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace DBChatbot.Services;

public sealed class RelationshipSearchService
{
    private readonly IConfiguration _configuration;

    private readonly HashSet<string> _allowedDatabases;
  

    public RelationshipSearchService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<List<DatabaseObjectRelationship>> SearchAsync(
        string databaseName,
        string question,
        CancellationToken cancellationToken)
    {
        if (!_allowedDatabases.Contains(databaseName))
            throw new ArgumentException("Database is not approved.");

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "SQL Server connection string is missing.");

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName
        };

        var relationships = new List<DatabaseObjectRelationship>();

        const string sql = """
        -- SQL object dependencies
        SELECT
            'SQL_DEPENDENCY' AS RelationshipType,
            ss.name AS SourceSchema,
            so.name AS SourceObject,
            CAST(NULL AS nvarchar(128)) AS SourceColumn,
            COALESCE(sed.referenced_database_name, DB_NAME())
                AS TargetDatabase,
            sed.referenced_schema_name AS TargetSchema,
            sed.referenced_entity_name AS TargetObject,
            rc.name AS TargetColumn,
            'sys.sql_expression_dependencies' AS EvidenceSource,
            CAST(so.type_desc AS nvarchar(4000))
        COLLATE DATABASE_DEFAULT AS Evidence
        FROM sys.sql_expression_dependencies sed
        INNER JOIN sys.objects so
            ON so.object_id = sed.referencing_id
        INNER JOIN sys.schemas ss
            ON ss.schema_id = so.schema_id
        LEFT JOIN sys.columns rc
            ON rc.object_id = sed.referenced_id
           AND rc.column_id = sed.referenced_minor_id
           AND sed.referenced_database_name IS NULL
           AND sed.referenced_server_name IS NULL
        WHERE so.is_ms_shipped = 0
          AND sed.referenced_entity_name IS NOT NULL

        UNION ALL

        -- Foreign-key column relationships
        SELECT
            'FOREIGN_KEY',
            ps.name,
            pt.name,
            pc.name,
            DB_NAME(),
            rs.name,
            rt.name,
            rc.name,
            'sys.foreign_key_columns',
            CAST(fk.name AS nvarchar(4000))
        COLLATE DATABASE_DEFAULT
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc
            ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.tables pt
            ON pt.object_id = fkc.parent_object_id
        INNER JOIN sys.schemas ps
            ON ps.schema_id = pt.schema_id
        INNER JOIN sys.columns pc
            ON pc.object_id = pt.object_id
           AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.tables rt
            ON rt.object_id = fkc.referenced_object_id
        INNER JOIN sys.schemas rs
            ON rs.schema_id = rt.schema_id
        INNER JOIN sys.columns rc
            ON rc.object_id = rt.object_id
           AND rc.column_id = fkc.referenced_column_id

        UNION ALL

        -- Synonyms
        SELECT
            'SYNONYM',
            ss.name,
            sy.name,
            CAST(NULL AS nvarchar(128)),
            PARSENAME(sy.base_object_name, 3),
            PARSENAME(sy.base_object_name, 2),
            PARSENAME(sy.base_object_name, 1),
            CAST(NULL AS nvarchar(128)),
            'sys.synonyms',
            CAST(sy.base_object_name AS nvarchar(4000))
        COLLATE DATABASE_DEFAULT
        FROM sys.synonyms sy
        INNER JOIN sys.schemas ss
            ON ss.schema_id = sy.schema_id

        ORDER BY SourceSchema, SourceObject;
        """;

        await using var connection =
            new SqlConnection(builder.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            relationships.Add(new DatabaseObjectRelationship
            {
                DatabaseName = databaseName,
                RelationshipType = reader.GetString(0),
                SourceSchema = reader.GetString(1),
                SourceObject = reader.GetString(2),
                SourceColumn = ReadNullableString(reader, 3),
                TargetDatabase = ReadNullableString(reader, 4),
                TargetSchema = ReadNullableString(reader, 5),
                TargetObject = ReadNullableString(reader, 6),
                TargetColumn = ReadNullableString(reader, 7),
                EvidenceSource = reader.GetString(8),
                Evidence = ReadNullableString(reader, 9)
            });
        }

        // Rank relationships using terms from the user's question.
        var terms = Regex.Matches(
                question,
                @"[A-Za-z_][A-Za-z0-9_]*")
            .Select(m => m.Value)
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return relationships
            .Select(r => new
            {
                Relationship = r,
                Score = terms.Sum(term =>
                    ScoreField(r.SourceObject, term, 5) +
                    ScoreField(r.SourceSchema, term, 1) +
                    ScoreField(r.SourceColumn, term, 4) +
                    ScoreField(r.TargetObject, term, 5) +
                    ScoreField(r.TargetSchema, term, 1) +
                    ScoreField(r.TargetColumn, term, 4) +
                    ScoreField(r.Evidence, term, 1))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(40)
            .Select(x => x.Relationship)
            .ToList();
    }

    private static int ScoreField(
        string? value,
        string term,
        int weight)
    {
        return !string.IsNullOrEmpty(value) &&
               value.Contains(term, StringComparison.OrdinalIgnoreCase)
            ? weight
            : 0;
    }

    private static string? ReadNullableString(
        SqlDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }
}
