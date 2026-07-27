using AgenticTodo.ServiceDefaults;
using AgenticTodo.TodoApi;
using AgenticTodo.TodoApi.Adapters;
using AgenticTodo.TodoApi.Features.CreateTodo;
using AgenticTodo.TodoApi.Features.ListTodos;
using AgenticTodo.TodoApi.Ports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();
builder.AddGroupAuthorization();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>());

builder.Services.AddHttpClient("agent", client =>
    client.BaseAddress = new Uri(builder.Configuration["Agent:GatewayBaseUrl"]!));

var hop1Mode = builder.Configuration["Hop1:Mode"] ?? "gateway";
if (hop1Mode == "dotnet")
{
    builder.Services.AddScoped<IAgentGateway, OboAgentGateway>();
}
else
{
    builder.Services.AddScoped<IAgentGateway, PassthroughAgentGateway>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();
app.UseTokenDiagnostics();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

// Scalar's OAuth2 flow object has no client_id field -- without WithClientId, Entra
// rejects the /token request with AADSTS900144.
var todoApiClientId = builder.Configuration["AzureAd:ClientId"];
app.MapScalarApiReference(options => options
    .AddAuthorizationCodeFlow("oauth2", flow => flow
        .WithClientId(todoApiClientId)
        .WithPkce(Pkce.Sha256)));

var todos = app.MapGroup("/todos").RequireAuthorization();
todos.MapListTodos();
todos.MapCreateTodo();

app.Run();
