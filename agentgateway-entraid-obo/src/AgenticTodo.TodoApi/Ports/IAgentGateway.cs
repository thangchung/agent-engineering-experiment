namespace AgenticTodo.TodoApi.Ports;

public interface IAgentGateway
{
    Task<HttpResponseMessage> ForwardAsync(HttpMethod method, string path, HttpContent? content, HttpContext httpContext, CancellationToken cancellationToken);
}
