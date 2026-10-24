
namespace DBChatbot.Models;

public sealed class DatabaseObjectRelationship
{
    public string DatabaseName { get; set; } = "";

    public string RelationshipType { get; set; } = "";

    public string SourceSchema { get; set; } = "";
    public string SourceObject { get; set; } = "";
    public string? SourceColumn { get; set; }

    public string? TargetDatabase { get; set; }
    public string? TargetSchema { get; set; }
    public string? TargetObject { get; set; }
    public string? TargetColumn { get; set; }

    public string EvidenceSource { get; set; } = "";
    public string? Evidence { get; set; }
}
