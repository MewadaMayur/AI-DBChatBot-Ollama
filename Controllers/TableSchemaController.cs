
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/schema")]
public class TableSchemaController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public TableSchemaController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("tables")]
    public async Task<IActionResult> GetTables(
        [FromQuery] string databaseName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            return BadRequest("databaseName is required.");

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
            return Problem("SQL Server connection string is missing.");

        const string sql = """
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            ORDER BY s.name, t.name;
            """;

        try
        {
            var tables = new List<object>();

            var builder =
                new SqlConnectionStringBuilder(connectionString);

            // Set the database separately; never concatenate user input into SQL.
            builder.InitialCatalog = databaseName;

            await using var connection =
                new SqlConnection(builder.ConnectionString);

            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);

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
        catch (ArgumentException)
        {
            return BadRequest("Invalid database connection settings.");
        }
        catch (SqlException)
        {
            return Problem(
                title: "Unable to inspect database",
                detail: "Check the database name and SQL Server permissions.",
                statusCode: 500);
        }
    }

    [HttpGet("columns")]
    public async Task<IActionResult> GetColumns(
        [FromQuery] string databaseName,
        [FromQuery] string tableName,
        [FromQuery] string schemaName = "dbo",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName) ||
            string.IsNullOrWhiteSpace(tableName) ||
            string.IsNullOrWhiteSpace(schemaName))
        {
            return BadRequest(
                "databaseName, tableName and schemaName are required.");
        }

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
            return Problem("SQL Server connection string is missing.");

        const string sql = """
            SELECT
                c.name AS ColumnName,
                ty.name AS DataType,
                CASE
                    WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
                        THEN c.max_length
                    WHEN ty.name IN ('nvarchar', 'nchar')
                        THEN c.max_length / 2
                    ELSE NULL
                END AS MaxLength,
                c.precision AS NumericPrecision,
                c.scale AS NumericScale,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                c.is_computed AS IsComputed,
                CASE WHEN pk.column_id IS NOT NULL
                     THEN CAST(1 AS bit)
                     ELSE CAST(0 AS bit)
                END AS IsPrimaryKey,
                fk.ReferencedTable,
                fk.ReferencedColumn
            FROM sys.tables t
            INNER JOIN sys.schemas s
                ON s.schema_id = t.schema_id
            INNER JOIN sys.columns c
                ON c.object_id = t.object_id
            INNER JOIN sys.types ty
                ON ty.user_type_id = c.user_type_id
            LEFT JOIN (
                SELECT ic.object_id, ic.column_id
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON ic.object_id = i.object_id
                   AND ic.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) pk ON pk.object_id = c.object_id
                AND pk.column_id = c.column_id
            LEFT JOIN (
                SELECT
                    fkc.parent_object_id,
                    fkc.parent_column_id,
                    OBJECT_SCHEMA_NAME(fkc.referenced_object_id)
                        + '.' +
                    OBJECT_NAME(fkc.referenced_object_id)
                        AS ReferencedTable,
                    COL_NAME(
                        fkc.referenced_object_id,
                        fkc.referenced_column_id
                    ) AS ReferencedColumn
                FROM sys.foreign_key_columns fkc
            ) fk ON fk.parent_object_id = c.object_id
                AND fk.parent_column_id = c.column_id
            WHERE s.name = @SchemaName
              AND t.name = @TableName
            ORDER BY c.column_id;
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

            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<object>();

            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new
                {
                    ColumnName = reader.GetString(0),
                    DataType = reader.GetString(1),
                    MaxLength = reader.IsDBNull(2)
    ? (int?)null
    : reader.GetInt32(2),
                    NumericPrecision = reader.GetByte(3),
                    NumericScale = reader.GetByte(4),
                    IsNullable = reader.GetBoolean(5),
                    IsIdentity = reader.GetBoolean(6),
                    IsComputed = reader.GetBoolean(7),
                    IsPrimaryKey = reader.GetBoolean(8),
                    ReferencedTable = reader.IsDBNull(9)
                        ? null
                        : reader.GetString(9),
                    ReferencedColumn = reader.IsDBNull(10)
                        ? null
                        : reader.GetString(10)
                });
            }

            if (columns.Count == 0)
            {
                return NotFound(
                    $"Table '{schemaName}.{tableName}' was not found " +
                    $"in database '{databaseName}', or its metadata " +
                    "is not visible to this login.");
            }

            return Ok(new
            {
                DatabaseName = databaseName,
                SchemaName = schemaName,
                TableName = tableName,
                ColumnCount = columns.Count,
                Columns = columns
            });
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid database connection settings.");
        }
        catch (SqlException)
        {
            return Problem(
                title: "Unable to inspect table",
                detail: "Check the database name and SQL Server permissions.",
                statusCode: 500);
        }
    }



    [HttpGet("procedure-definition")]
    public async Task<IActionResult> GetProcedureDefinition(
        [FromQuery] string databaseName,
        [FromQuery] string procedureName,
        [FromQuery] string schemaName = "dbo",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName) ||
            string.IsNullOrWhiteSpace(procedureName) ||
            string.IsNullOrWhiteSpace(schemaName))
        {
            return BadRequest(
                "databaseName, procedureName and schemaName are required.");
        }

        var connectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(connectionString))
            return Problem("SQL Server connection string is missing.");

        const string sql = """
        SELECT
            s.name AS SchemaName,
            p.name AS ProcedureName,
            m.definition AS Definition,
            p.create_date AS CreatedDate,
            p.modify_date AS ModifiedDate
        FROM sys.procedures p
        INNER JOIN sys.schemas s
            ON s.schema_id = p.schema_id
        LEFT JOIN sys.sql_modules m
            ON m.object_id = p.object_id
        WHERE s.name = @SchemaName
          AND p.name = @ProcedureName
          AND p.is_ms_shipped = 0;
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

            command.Parameters.Add(
                "@SchemaName", System.Data.SqlDbType.NVarChar, 128
            ).Value = schemaName;

            command.Parameters.Add(
                "@ProcedureName", System.Data.SqlDbType.NVarChar, 128
            ).Value = procedureName;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return NotFound(
                    "Procedure not found, or its metadata is not visible.");
            }

            string? definition = reader.IsDBNull(2)
                ? null
                : reader.GetString(2);

            return Ok(new
            {
                DatabaseName = databaseName,
                SchemaName = reader.GetString(0),
                ProcedureName = reader.GetString(1),
                CreatedDate = reader.GetDateTime(3),
                ModifiedDate = reader.GetDateTime(4),
                DefinitionAvailable = definition is not null,
                Definition = definition,
                Message = definition is null
                    ? "Definition unavailable. The procedure may be encrypted " +
                      "or your login may lack metadata permissions."
                    : "Definition retrieved. The procedure was not executed."
            });
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid database connection settings.");
        }
        catch (SqlException)
        {
            return Problem(
                title: "Unable to retrieve procedure definition",
                detail: "Check the database name and SQL Server permissions.",
                statusCode: 500);
        }
    }

}
