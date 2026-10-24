
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/databases")]
public class DatabaseController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseController> _logger;

    public DatabaseController(
        IConfiguration configuration,
        ILogger<DatabaseController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetDatabases(
        CancellationToken cancellationToken)
    {
        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Problem(
                title: "Missing SQL Server configuration",
                detail: "Configure ConnectionStrings:SqlServer.");
        }

        const string sql = """
            SELECT name, state_desc
            FROM sys.databases
            WHERE state = 0
              AND HAS_DBACCESS(name) = 1
            ORDER BY name;
            """;

        try
        {
            var databases = new List<object>();

            await using var connection =
                new SqlConnection(connectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command =
                new SqlCommand(sql, connection);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(new
                {
                    DatabaseName = reader.GetString(0),
                    State = reader.GetString(1)
                });
            }

            return Ok(new
            {
                Count = databases.Count,
                Databases = databases
            });
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex, "Failed to discover SQL Server databases.");

            return Problem(
                title: "SQL Server connection failed",
                detail: "Verify your connection settings and permissions.",
                statusCode: 500);
        }
    }
}
