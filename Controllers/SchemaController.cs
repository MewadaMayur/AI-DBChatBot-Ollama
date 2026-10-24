
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/schema")]
public class SchemaController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchemaController> _logger;

    public SchemaController(
        IConfiguration configuration,
        ILogger<SchemaController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("{databaseName}/tables")]
    public async Task<IActionResult> GetTables(
        string databaseName,
        CancellationToken cancellationToken)
    {
        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem(
                title: "Missing SQL Server connection string",
                statusCode: 500);
        }

        try
        {
            var builder =
                new SqlConnectionStringBuilder(connectionString);

            // Select the requested database safely.
            builder.InitialCatalog = databaseName;

            const string sql = """
                SELECT
                    s.name AS SchemaName,
                    t.name AS TableName
                FROM sys.tables AS t
                INNER JOIN sys.schemas AS s
                    ON t.schema_id = s.schema_id
                ORDER BY s.name, t.name;
                """;

            var tables = new List<object>();

            await using var connection =
                new SqlConnection(builder.ConnectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command =
                new SqlCommand(sql, connection);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1)
                });
            }

            return Ok(new
            {
                DatabaseName = databaseName,
                TableCount = tables.Count,
                Tables = tables
            });
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve tables for database {DatabaseName}",
                databaseName);

            return Problem(
                title: "Unable to retrieve database tables",
                detail: "Check the database name, connection settings, and permissions.",
                statusCode: 500);
        }
        catch (ArgumentException)
        {
            return BadRequest(new
            {
                Message = "Invalid database name or connection configuration."
            });
        }
    }



    [HttpGet("procedures")]
    public async Task<IActionResult> GetProcedures(
        [FromQuery] string databaseName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return BadRequest("databaseName is required.");
        }

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem("SQL Server connection string is missing.");
        }

        const string sql = """
        SELECT
            s.name AS SchemaName,
            p.name AS ProcedureName,
            p.create_date AS CreatedDate,
            p.modify_date AS ModifiedDate,
            (
                SELECT COUNT(*)
                FROM sys.parameters prm
                WHERE prm.object_id = p.object_id
                  AND prm.parameter_id > 0
            ) AS ParameterCount,
            CAST(
                CASE WHEN m.definition IS NULL THEN 1 ELSE 0 END
                AS bit
            ) AS DefinitionUnavailable
        FROM sys.procedures p
        INNER JOIN sys.schemas s
            ON s.schema_id = p.schema_id
        LEFT JOIN sys.sql_modules m
            ON m.object_id = p.object_id
        WHERE p.is_ms_shipped = 0
        ORDER BY s.name, p.name;
        """;

        try
        {
            var builder =
                new SqlConnectionStringBuilder(connectionString);

            builder.InitialCatalog = databaseName;

            await using var connection =
                new SqlConnection(builder.ConnectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var procedures = new List<object>();

            while (await reader.ReadAsync(cancellationToken))
            {
                procedures.Add(new
                {
                    SchemaName = reader.GetString(0),
                    ProcedureName = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    ModifiedDate = reader.GetDateTime(3),
                    ParameterCount = reader.GetInt32(4),
                    DefinitionUnavailable = reader.GetBoolean(5)
                });
            }

            return Ok(new
            {
                DatabaseName = databaseName,
                ProcedureCount = procedures.Count,
                Procedures = procedures
            });
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid database connection settings.");
        }
        catch (SqlException)
        {
            return Problem(
                title: "Unable to discover stored procedures",
                detail: "Check the database name and SQL Server permissions.",
                statusCode: 500);
        }
    }

}
