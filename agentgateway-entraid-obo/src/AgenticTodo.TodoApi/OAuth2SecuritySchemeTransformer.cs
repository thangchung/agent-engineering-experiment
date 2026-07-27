using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AgenticTodo.TodoApi;

public sealed class OAuth2SecuritySchemeTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var tenantId = configuration["AzureAd:TenantId"] ?? "common";
        var clientId = configuration["AzureAd:ClientId"] ?? string.Empty;
        var scope = $"api://{clientId}/access_as_user";

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"),
                    TokenUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"),
                    Scopes = new Dictionary<string, string> { [scope] = "Access TodoApi as the signed-in user" },
                },
            },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["oauth2"] = scheme;

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("oauth2", document)] = [scope],
        });

        // Relative server: ASP.NET Core otherwise auto-populates Servers from the request's
        // Host header, which the gateway rewrites to an upstream target the browser can't resolve.
        document.Servers = [new OpenApiServer { Url = "/" }];

        return Task.CompletedTask;
    }
}
