using AgenticTodo.TodoApi.Ports;
using Microsoft.Identity.Abstractions;

namespace AgenticTodo.TodoApi.Adapters;

// Fallback hop-1 path (Hop1:Mode=dotnet): TodoApi performs the OBO exchange itself and
// forwards the exchanged token, with the gateway route switched to backendAuth.passthrough.
public sealed class OboAgentGateway(IHttpClientFactory httpClientFactory, IAuthorizationHeaderProvider authorizationHeaderProvider, IConfiguration configuration) : IAgentGateway
{
    public async Task<HttpResponseMessage> ForwardAsync(HttpMethod method, string path, HttpContent? content, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var todoAgentClientId = configuration["Agent:ClientId"]
            ?? throw new InvalidOperationException("Agent:ClientId is required.");

        var header = await authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
            [$"api://{todoAgentClientId}/access_as_user"],
            authorizationHeaderProviderOptions: null,
            httpContext.User,
            cancellationToken);

        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", header);

        var client = httpClientFactory.CreateClient("agent");
        return await client.SendAsync(request, cancellationToken);
    }
}
