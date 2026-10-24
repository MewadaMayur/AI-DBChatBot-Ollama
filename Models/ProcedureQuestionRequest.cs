namespace DBChatbot.Models;

public sealed class ProcedureQuestionRequest
{
    public string DatabaseName { get; set; } = "";
    public string SchemaName { get; set; } = "dbo";
    public string ProcedureName { get; set; } = "";
    public string Question { get; set; } = "";
}