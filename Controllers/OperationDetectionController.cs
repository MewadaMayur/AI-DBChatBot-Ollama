
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DBChatbot.Services;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/operation-detection")]
public class OperationDetectionController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly SqlOperationDetector _detector;

    public OperationDetectionController(
        IConfiguration configuration,
        SqlOperationDetector detector)
    {
        _configuration = configuration;
        _detector = detector;
    }

    [HttpGet("procedures")]
    public async Task<IActionResult> FindProcedures(
        [FromQuery] string databaseName,
        [FromQuery] string tableName,
        [FromQuery] string operation)
    {
        var allowedOperations =
            new[] { "INSERT", "UPDATE", "DELETE", "SELECT" };

        if (string.IsNullOrWhiteSpace(databaseName) ||
            string.IsNullOrWhiteSpace(tableName) ||
            !allowedOperations.Contains(
                operation, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Provide databaseName, tableName, and a valid operation."
            });
        }

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(500, new
            {
                message = "ConnectionStrings:SqlServer is missing."
            });
        }

        var connectionBuilder =
            new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName
            };

        const string sql = """
            SELECT
                s.name AS SchemaName,
                p.name AS ProcedureName,
                m.definition AS Definition
            FROM sys.procedures AS p
            INNER JOIN sys.schemas AS s
                ON p.schema_id = s.schema_id
            INNER JOIN sys.sql_modules AS m
                ON p.object_id = m.object_id
            ORDER BY s.name, p.name;
            """;

        var matches = new List<object>();
        var scanned = 0;

        await using var connection =
            new SqlConnection(connectionBuilder.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            scanned++;

            var schema = reader.GetString(0);
            var procedure = reader.GetString(1);
            var definition = reader.IsDBNull(2)
                ? string.Empty
                : reader.GetString(2);

            if (_detector.HasOperationOnTable(
         definition, tableName, operation))
            {
                var evidence = new List<object>();

                if (operation.Equals(
                        "UPDATE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var statements = _detector.FindUpdateEvidence(
                        definition, tableName);

                    foreach (var statement in statements)
                    {
                        evidence.Add(new
                        {
                            statement,
                            updatedColumns =
                                _detector.ExtractUpdatedColumns(statement)
                        });
                    }
                }

                matches.Add(new
                {
                    procedure = $"{schema}.{procedure}",
                    operation = operation.ToUpperInvariant(),
                    tableName,
                    sqlEvidence = evidence
                });
            }
        }

        return Ok(new
        {
            databaseName,
            tableName,
            operation = operation.ToUpperInvariant(),
            proceduresScanned = scanned,
            matchCount = matches.Count,
            procedures = matches
        });
    }
}
