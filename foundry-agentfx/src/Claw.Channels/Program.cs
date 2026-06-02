using Claw.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var isLocal = string.Equals(
    builder.Configuration["Agent:Provider"], "local",
    StringComparison.OrdinalIgnoreCase);

if (isLocal)
{
    builder.Services.AddHttpClient<IAgentClient, LocalAgentClient>(client =>
    {
        var baseUrl = (builder.Configuration["Agent:BaseUrl"] ?? "http://localhost:5000").TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);
    });
}
else
{
    builder.Services.AddHttpClient<IAgentClient, FoundryAgentClient>(client =>
    {
        var baseUrl = (builder.Configuration["Agent:BaseUrl"] ?? "http://localhost:5000").TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);
    });
}

builder.Services.AddSlackChannel(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapSlack();

app.MapWebChannel();

app.Run();
