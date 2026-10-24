namespace DBChatbot.Models;

public sealed class DatabaseQuestionRequest
{
    public string DatabaseName { get; set; } = "";
    public string Question { get; set; } = "";
}