using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jev.Client;

/// <summary>
/// A minimal .NET client for the Jev / OpenJev wire API (research.md §2, §5.5). The whole
/// surface is one endpoint: <c>POST {base}/v1/systemone</c>.
/// </summary>
public sealed class JevClient
{
    /// <summary>Registered in ServiceDefaults so every call shows up as a trace span, enriched
    /// with the OpenTelemetry GenAI semantic conventions (github.com/open-telemetry/semantic-
    /// conventions-genai) - the same attribute family the OpenAI chat calls already carry, so
    /// both show up the same way in the dashboard's GenAI trace view.</summary>
    private static readonly ActivitySource ActivitySource = new("Jev.Client");


    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly JevOptions _options;

    public JevClient(HttpClient httpClient, JevOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    /// <summary>
    /// Ask Jev one or more bounded questions about <paramref name="state"/> in a single request.
    /// </summary>
    /// <param name="state">Anything JSON-serializable: a string, an object, or an array.</param>
    /// <param name="questions">Question id -> question. The ids are never sent to the model.</param>
    /// <exception cref="JevException">The server returned a non-2xx status.</exception>
    public async Task<JevResponse> AskAsync(
        object state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity($"chat {_options.Model}", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.provider.name", "openjev");
        activity?.SetTag("gen_ai.request.model", _options.Model);
        if (_httpClient.BaseAddress is { } baseAddress)
        {
            activity?.SetTag("server.address", baseAddress.Host);
            activity?.SetTag("server.port", baseAddress.Port);
        }

        var questionsNode = new JsonObject();
        foreach (var (id, question) in questions)
        {
            questionsNode[id] = question.ToJson();
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["state"] = JsonSerializer.SerializeToNode(state, StateJson),
            ["questions"] = questionsNode,
        };

        if (_options.CaptureContent)
        {
            // Jev has no chat-turn/role concept (research.md §2: choice/score/noul primitives over
            // one opaque "state"), so the state+questions body is carried as the "content" of a
            // single synthetic user message - the dashboard's GenAI viewer requires the
            // role/parts array shape even when there is only one turn.
            activity?.SetTag("gen_ai.input.messages", AsMessagesJson("user", body.ToJsonString()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var httpError = $"{(int)response.StatusCode}";
                activity?.SetTag("error.type", httpError);
                activity?.SetStatus(ActivityStatusCode.Error, httpError);
                throw new JevException((int)response.StatusCode, TryReadDetail(text), $"Jev request failed with HTTP {(int)response.StatusCode}.");
            }

            var result = JsonSerializer.Deserialize<JevResponse>(text, ResponseJson)
                ?? throw new JevException((int)response.StatusCode, null, "Jev returned an empty response body.");

            activity?.SetTag("gen_ai.response.model", result.Model);
            if (result.Usage is { } usage)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
                activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            }

            if (_options.CaptureContent)
            {
                activity?.SetTag("gen_ai.output.messages", AsMessagesJson("assistant", text));
            }

            return result;
        }
        catch (Exception ex) when (ex is not JevException)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>Wraps raw JSON text as the single gen_ai.*.messages array entry the GenAI spec
    /// (and the Aspire dashboard's GenAI viewer) expects: [{"role":..,"parts":[{"type":"text","content":..}]}].</summary>
    private static string AsMessagesJson(string role, string content) =>
        new JsonArray(new JsonObject
        {
            ["role"] = role,
            ["parts"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["content"] = content,
            }),
        }).ToJsonString();

    private static JsonElement? TryReadDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var detail) ? detail.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
