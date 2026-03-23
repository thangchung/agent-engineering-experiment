using TestWeb.Components;
using TestWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

int configuredApiTimeoutSeconds = builder.Configuration.GetValue<int?>("Copilot:ApiTimeoutSeconds") ?? 300;
if (configuredApiTimeoutSeconds <= 0)
{
    configuredApiTimeoutSeconds = 300;
}

TimeSpan apiTimeout = TimeSpan.FromSeconds(configuredApiTimeoutSeconds);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<CopilotChatApiClient>(httpClient =>
{
    httpClient.Timeout = apiTimeout;
});
builder.Services.AddScoped<CopilotChatApiClient>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
