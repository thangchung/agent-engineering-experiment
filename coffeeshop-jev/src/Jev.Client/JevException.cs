using System.Text.Json;

namespace Jev.Client;

/// <summary>
/// A non-2xx response from Jev/OpenJev. research.md §2: <c>detail</c> can be a plain string
/// (400), an object (401/403 <c>{error_type,message}</c>), or an array (422 validation errors),
/// so <see cref="Detail"/> is a raw <see cref="JsonElement"/> rather than a fixed shape.
/// </summary>
public sealed class JevException : Exception
{
    public JevException(int statusCode, JsonElement? detail, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    /// <summary>The HTTP status code (401/403 auth, 400/422 validation, 429/529 rate limit/overload, 503).</summary>
    public int StatusCode { get; }

    /// <summary>The response body's <c>detail</c> field, or null if the body had none / wasn't JSON.</summary>
    public JsonElement? Detail { get; }
}
