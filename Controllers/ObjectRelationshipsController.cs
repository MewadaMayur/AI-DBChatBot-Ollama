
using DBChatbot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DBChatbot.Controllers;

[ApiController]
[Route("api/objects")]
public class ObjectRelationshipsController : ControllerBase 
{
    private readonly IConfiguration _configuration;
    private readonly HashSet<string> _approvedDatabases;

    public ObjectRelationshipsController(IConfiguration configuration)
    {
        _configuration = configuration;
        _approvedDatabases = new HashSet<string>(
            configuration
                .GetSection("AllowedDatabases")
                .Get<string[]>() ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }
    

    [HttpGet("relationships")]
    public async Task<IActionResult> GetRelationships(
        [FromQuery] string? databaseName,
        CancellationToken cancellationToken)
    {
        var baseConnectionString =
            _configuration.GetConnectionString("SqlServer");

        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return StatusCode(500, new
            {
                error = "ConnectionStrings:SqlServer is not configured."
            });
        }

        string[] databases;

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            databases = _approvedDatabases.ToArray();
        }
        else
        {
            var approved = _approvedDatabases.FirstOrDefault(
                x => string.Equals(
                    x, databaseName, StringComparison.OrdinalIgnoreCase));

            if (approved is null)
            {
                return BadRequest(new
                {
                    error = "Database is not approved for chatbot searches.",
                    approvedDatabases = _approvedDatabases
                });
            }

            databases = [approved];
        }

        var relationships = new List<DatabaseObjectRelationship>();
        var errors = new List<object>();

        foreach (var dbName in databases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var builder =
                new SqlConnectionStringBuilder(baseConnectionString)
                {
                    InitialCatalog = dbName
                };

            try
            {
                await using var connection =
                    new SqlConnection(builder.ConnectionString);

                await connection.OpenAsync(cancellationToken);

                // 1. Dependencies recorded by SQL Server.
                const string dependencySql = """
                    SELECT
                        rs.name AS SourceSchema,
                        ro.name AS SourceObject,
                        ro.type_desc AS SourceType,
                        sed.referenced_server_name,
                        sed.referenced_database_name,
                        sed.referenced_schema_name,
                        sed.referenced_entity_name,
                        sed.referenced_id,
                        sed.referenced_minor_id,
                        sed.is_caller_dependent,
                        sed.is_ambiguous,
                        referencedColumn.name AS ReferencedColumnName
                    FROM sys.sql_expression_dependencies AS sed
                    INNER JOIN sys.objects AS ro
                        ON ro.object_id = sed.referencing_id
                    INNER JOIN sys.schemas AS rs
                        ON rs.schema_id = ro.schema_id
                    LEFT JOIN sys.columns AS referencedColumn
                        ON referencedColumn.object_id = sed.referenced_id
                       AND referencedColumn.column_id = sed.referenced_minor_id
                    WHERE ro.is_ms_shipped = 0
                    ORDER BY rs.name, ro.name;
                    """;

                await using (var command =
                    new SqlCommand(dependencySql, connection))
                {
                    command.CommandTimeout = 60;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);



                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var targetDatabase = reader.IsDBNull(4)
                            ? dbName
                            : reader.GetString(4);

                        var targetSchema = reader.IsDBNull(5)
                            ? null
                            : reader.GetString(5);

                        var targetObject = reader.IsDBNull(6)
                            ? null
                            : reader.GetString(6);

                        var targetServer = reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3);

                        var targetColumn = reader.IsDBNull(11)
                            ? null
                            : reader.GetString(11);

                        var callerDependent = reader.GetBoolean(9);
                        var ambiguous = reader.GetBoolean(10);

                        var evidence = $"Source type: {reader.GetString(2)}";

                        if (!string.IsNullOrWhiteSpace(targetServer))
                            evidence += $"; Target server: {targetServer}";

                        if (callerDependent)
                            evidence += "; Caller-dependent reference";

                        if (ambiguous)
                            evidence += "; Ambiguous reference";

                        relationships.Add(new DatabaseObjectRelationship
                        {
                            DatabaseName = dbName,
                            RelationshipType = "SQL_DEPENDENCY",
                            SourceSchema = reader.GetString(0),
                            SourceObject = reader.GetString(1),
                            TargetDatabase = targetDatabase,
                            TargetSchema = targetSchema,
                            TargetObject = targetObject,
                            TargetColumn = targetColumn,
                            EvidenceSource = "sys.sql_expression_dependencies",
                            Evidence = evidence
                        });
                    }


                }

                // 2. Declared foreign-key relationships.
                const string foreignKeySql = """
                    SELECT
                        childSchema.name AS ChildSchema,
                        childTable.name AS ChildTable,
                        childColumn.name AS ChildColumn,
                        parentSchema.name AS ParentSchema,
                        parentTable.name AS ParentTable,
                        parentColumn.name AS ParentColumn,
                        fk.name AS ForeignKeyName
                    FROM sys.foreign_keys AS fk
                    INNER JOIN sys.foreign_key_columns AS fkc
                        ON fkc.constraint_object_id = fk.object_id
                    INNER JOIN sys.tables AS childTable
                        ON childTable.object_id = fkc.parent_object_id
                    INNER JOIN sys.schemas AS childSchema
                        ON childSchema.schema_id = childTable.schema_id
                    INNER JOIN sys.columns AS childColumn
                        ON childColumn.object_id = childTable.object_id
                       AND childColumn.column_id = fkc.parent_column_id
                    INNER JOIN sys.tables AS parentTable
                        ON parentTable.object_id = fkc.referenced_object_id
                    INNER JOIN sys.schemas AS parentSchema
                        ON parentSchema.schema_id = parentTable.schema_id
                    INNER JOIN sys.columns AS parentColumn
                        ON parentColumn.object_id = parentTable.object_id
                       AND parentColumn.column_id = fkc.referenced_column_id
                    ORDER BY childSchema.name, childTable.name, fk.name;
                    """;

                await using (var command =
                    new SqlCommand(foreignKeySql, connection))
                {
                    command.CommandTimeout = 60;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        relationships.Add(new DatabaseObjectRelationship
                        {
                            DatabaseName = dbName,
                            RelationshipType = "FOREIGN_KEY",
                            SourceSchema = reader.GetString(0),
                            SourceObject = reader.GetString(1),
                            SourceColumn = reader.GetString(2),
                            TargetDatabase = dbName,
                            TargetSchema = reader.GetString(3),
                            TargetObject = reader.GetString(4),
                            TargetColumn = reader.GetString(5),
                            EvidenceSource = "sys.foreign_keys",
                            Evidence = reader.GetString(6)
                        });
                    }
                }

                // 3. Synonym target names.
                const string synonymSql = """
                    SELECT
                        s.name AS SourceSchema,
                        sy.name AS SynonymName,
                        sy.base_object_name AS TargetName
                    FROM sys.synonyms AS sy
                    INNER JOIN sys.schemas AS s
                        ON s.schema_id = sy.schema_id
                    ORDER BY s.name, sy.name;
                    """;

                await using (var command =
                    new SqlCommand(synonymSql, connection))
                {
                    command.CommandTimeout = 30;

                    await using var reader =
                        await command.ExecuteReaderAsync(cancellationToken);

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        relationships.Add(new DatabaseObjectRelationship
                        {
                            DatabaseName = dbName,
                            RelationshipType = "SYNONYM",
                            SourceSchema = reader.GetString(0),
                            SourceObject = reader.GetString(1),
                            TargetDatabase = null,
                            TargetSchema = null,
                            TargetObject = null,
                            EvidenceSource = "sys.synonyms",
                            Evidence = reader.GetString(2)
                        });
                    }
                }
            }
            catch (SqlException ex)
            {
                errors.Add(new
                {
                    databaseName = dbName,
                    error = ex.Message
                });
            }
        }

        return Ok(new
        {
            searchedDatabaseCount = databases.Length,
            successfulDatabaseCount = databases.Length - errors.Count,
            relationshipCount = relationships.Count,
            relationships,
            errors
        });
    }
}
