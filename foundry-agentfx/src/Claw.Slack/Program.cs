using Claw.Slack;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient<FoundryAgentClient>(client =>
{
    var baseUrl = builder.Configuration["Agent:BaseUrl"]
        ?? "http://localhost:5000";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddSlackChannel(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapSlack();

app.Run();
