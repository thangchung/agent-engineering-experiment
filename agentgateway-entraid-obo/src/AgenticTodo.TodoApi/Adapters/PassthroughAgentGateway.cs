using AgenticTodo.TodoApi.Ports;

namespace AgenticTodo.TodoApi.Adapters;

// Primary hop-1 path: forward the inbound bearer unchanged. The gateway performs the
// Entra OBO exchange (backendAuth.oauthTokenExchange) before this reaches TodoAgent.
public sealed class PassthroughAgentGateway(IHttpClientFactory httpClientFactory) : IAgentGateway
{
    public async Task<HttpResponseMessage> ForwardAsync(HttpMethod method, string path, HttpContent? content, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        var inboundAuthorization = httpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(inboundAuthorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", inboundAuthorization);
        }

        var client = httpClientFactory.CreateClient("agent");
        return await client.SendAsync(request, cancellationToken);
    }
}
