using WebApp;
using WebApp.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// An AG-UI run is one long SSE response; the default 100s HttpClient timeout would cut a slow order.
builder.Services.AddHttpClient("agui", c =>
{
    c.BaseAddress = new Uri("https+http://counter");
    c.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient("counter-api", c => c.BaseAddress = new Uri("https+http://counter"));
builder.Services.AddScoped<OrderSession>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
