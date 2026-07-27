using AgenticTodo.ServiceDefaults;
using AgenticTodo.TodoAgent;
using AgenticTodo.TodoAgent.Adapters.Llm;
using AgenticTodo.TodoAgent.Adapters.Mcp;
using AgenticTodo.TodoAgent.Domain.Ports;
using AgenticTodo.TodoAgent.Features.CreateTodo;
using AgenticTodo.TodoAgent.Features.ListTodos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();
builder.AddGroupAuthorization();
builder.Services.AddAgentIdentities();
UseScopedOidcFicSignedAssertionProvider(builder.Services);

builder.Services.AddChatClient(builder.Configuration);

builder.Services.AddScoped<ICreateTodo, CreateTodoHandler>();
builder.Services.AddScoped<IListTodos, ListTodosHandler>();
builder.Services.AddScoped<IDescriptionGenerator, ChatClientDescriptionGenerator>();
builder.Services.AddScoped<ITodoSink, McpTodoSink>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseTokenDiagnostics();

app.UseAuthentication();
app.UseAuthorization();

app.MapCreateTodo();
app.MapListTodos();

app.Run();

// FIC needs these Scoped, not the default Singleton, so each user's OBO exchange gets its
// own signed assertion. No-ops on a plain client secret (local dev) -- FIC provider never registered then.
static void UseScopedOidcFicSignedAssertionProvider(IServiceCollection services)
{
    ReplaceServiceLifetime(services, typeof(ICredentialsLoader), "Microsoft.Identity.Web.DefaultCertificateLoader", ServiceLifetime.Scoped);
    ReplaceServiceLifetime(services, typeof(ICustomSignedAssertionProvider), "Microsoft.Identity.Web.OidcFic.OidcIdpSignedAssertionLoader", ServiceLifetime.Scoped);
}

static void ReplaceServiceLifetime(IServiceCollection services, Type serviceType, string implementationTypeName, ServiceLifetime lifetime)
{
    var descriptor = services.FirstOrDefault(service =>
        service.ServiceType == serviceType &&
        service.ImplementationType?.FullName == implementationTypeName);
    if (descriptor?.ImplementationType is null)
    {
        return;
    }

    services.Remove(descriptor);
    services.Add(new ServiceDescriptor(serviceType, descriptor.ImplementationType, lifetime));
}
