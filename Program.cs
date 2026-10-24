using DBChatbot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHttpClient("Ollama", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Ollama:BaseUrl"]!
    );

    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddScoped<DBChatbot.Services.OllamaService>();
builder.Services.AddScoped<DBChatbot.Services.ProcedurePromptBuilder>();
builder.Services.AddSingleton<DBChatbot.Services.SqlOperationDetector>();
builder.Services.AddScoped<RelationshipSearchService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.UseStaticFiles();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
