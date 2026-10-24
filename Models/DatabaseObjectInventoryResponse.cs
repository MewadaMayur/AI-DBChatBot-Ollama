
namespace DBChatbot.Models;

public sealed class DatabaseObjectInventoryResponse
{
    public string DatabaseName { get; set; } = "";
    public List<DatabaseObjectInfo> Objects { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class DatabaseObjectInfo
{
    public string DatabaseName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string? SynonymTarget { get; set; }
    public List<DatabaseColumnInfo> Columns { get; set; } = [];
}

public sealed class DatabaseColumnInfo
{
    public string ColumnName { get; set; } = "";
    public string DataType { get; set; } = "";
    public int OrdinalPosition { get; set; }
    public bool IsNullable { get; set; }
}
