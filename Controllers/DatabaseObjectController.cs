
using DBChatbot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/objects")]
public class DatabaseObjectController : ControllerBase
{
    private readonly IConfiguration _configuration;

    // Only databases explicitly approved for chatbot searches.
    private readonly HashSet<string> _approvedDatabases;

    public DatabaseObjectController(IConfiguration configuration)
    {
        _configuration = configuration;
        _approvedDatabases = new HashSet<string>(
            configuration
                .GetSection("AllowedDatabases")
                .Get<string[]>() ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> GetInventory(
        CancellationToken cancellationToken)
    {
        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(500, new
            {
                error = "ConnectionStrings:SqlServer is not configured."
            });
        }

        var results = new List<DatabaseObjectInventoryResponse>();

        foreach (var databaseName in _approvedDatabases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var databaseResult = new DatabaseObjectInventoryResponse
            {
                DatabaseName = databaseName
            };

            try
            {
                var builder =
                    new SqlConnectionStringBuilder(connectionString)
                    {
                        InitialCatalog = databaseName
                    };

                await using var connection =
                    new SqlConnection(builder.ConnectionString);

                await connection.OpenAsync(cancellationToken);

                const string objectSql = """
                    SELECT
                        s.name AS SchemaName,
                        o.name AS ObjectName,
                        CASE o.type
                            WHEN 'U'  THEN 'TABLE'
                            WHEN 'V'  THEN 'VIEW'
                            WHEN 'P'  THEN 'STORED_PROCEDURE'
                            WHEN 'FN' THEN 'SQL_SCALAR_FUNCTION'
                            WHEN 'IF' THEN 'SQL_INLINE_TABLE_VALUED_FUNCTION'
                            WHEN 'TF' THEN 'SQL_TABLE_VALUED_FUNCTION'
                            WHEN 'FS' THEN 'CLR_SCALAR_FUNCTION'
                            WHEN 'FT' THEN 'CLR_TABLE_VALUED_FUNCTION'
                            WHEN 'SN' THEN 'SYNONYM'
                            ELSE o.type_desc
                        END AS ObjectType,
                        sy.base_object_name AS SynonymTarget
                    FROM sys.objects AS o
                    INNER JOIN sys.schemas AS s
                        ON s.schema_id = o.schema_id
                    LEFT JOIN sys.synonyms AS sy
                        ON sy.object_id = o.object_id
                    WHERE o.is_ms_shipped = 0
                      AND o.type IN
                          ('U', 'V', 'P', 'FN', 'IF', 'TF', 'FS', 'FT', 'SN')
                    ORDER BY s.name, o.name;
                    """;

                await using (var command =
                    new SqlCommand(objectSql, connection))
                {
                    command.CommandTimeout = 30;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        databaseResult.Objects.Add(new DatabaseObjectInfo
                        {
                            DatabaseName = databaseName,
                            SchemaName = reader.GetString(0),
                            ObjectName = reader.GetString(1),
                            ObjectType = reader.GetString(2),
                            SynonymTarget = reader.IsDBNull(3)
                                ? null
                                : reader.GetString(3)
                        });
                    }
                }

                const string columnSql = """
                    SELECT
                        s.name AS SchemaName,
                        o.name AS ObjectName,
                        c.name AS ColumnName,
                        TYPE_NAME(c.user_type_id) AS DataType,
                        c.column_id AS OrdinalPosition,
                        c.is_nullable AS IsNullable
                    FROM sys.columns AS c
                    INNER JOIN sys.objects AS o
                        ON o.object_id = c.object_id
                    INNER JOIN sys.schemas AS s
                        ON s.schema_id = o.schema_id
                    WHERE o.is_ms_shipped = 0
                      AND o.type IN ('U', 'V')
                    ORDER BY s.name, o.name, c.column_id;
                    """;

                var objectLookup = databaseResult.Objects.ToDictionary(
                    x => $"{x.SchemaName}.{x.ObjectName}",
                    StringComparer.OrdinalIgnoreCase);

                await using (var command =
                    new SqlCommand(columnSql, connection))
                {
                    command.CommandTimeout = 30;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var schemaName = reader.GetString(0);
                        var objectName = reader.GetString(1);
                        var key = $"{schemaName}.{objectName}";

                        if (!objectLookup.TryGetValue(key, out var item))
                            continue;

                        item.Columns.Add(new DatabaseColumnInfo
                        {
                            ColumnName = reader.GetString(2),
                            DataType = reader.IsDBNull(3)
                                ? "unknown"
                                : reader.GetString(3),
                            OrdinalPosition = reader.GetInt32(4),
                            IsNullable = reader.GetBoolean(5)
                        });
                    }
                }
            }
            catch (SqlException ex)
            {
                // Return partial inventory instead of failing all databases.
                databaseResult.Error = $"SQL metadata query failed: {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                databaseResult.Error = $"Database inspection failed: {ex.Message}";
            }

            results.Add(databaseResult);
        }

        return Ok(new
        {
            databaseCount = results.Count,
            successfulDatabaseCount = results.Count(x => x.Error is null),
            objectCount = results.Sum(x => x.Objects.Count),
            databases = results
        });
    }
}
