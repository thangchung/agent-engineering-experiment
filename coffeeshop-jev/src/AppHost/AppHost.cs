var builder = DistributedApplication.CreateBuilder(args);

// AddParameter(name, string value) never reads configuration - it always returns that literal
// value, ignoring appsettings.json's "Parameters" section entirely (confirmed in Aspire's own
// source, ParameterResourceBuilderExtensions.cs: only the no-default AddParameter(name, secret)
// overload calls GetParameterValue(builder.Configuration, ...); the string-value overload just
// wraps the value in a plain valueGetter). Reading "Parameters:{name}" ourselves first, with ""
// as the fallback, gets both: appsettings.json/appsettings.{env}.json/user-secrets/env vars are
// honored, AND `aspire run` never blocks or throws when nothing is configured (research.md G4
// fail-closed-at-call-site design, not fail-at-apphost-startup).
static string FromConfig(IDistributedApplicationBuilder b, string name) =>
    b.Configuration[$"Parameters:{name}"] ?? "";

// research.md §5.6 / §14 (Q1/Q2/Q12): the Jev host is on the user's own LAN, local demo only,
// so no external endpoints and no TLS/auth work here - that is the LAN owner's responsibility.
var openjevUrl = builder.AddParameter("openjev-url", FromConfig(builder, "openjev-url"));
var jevApiKey = builder.AddParameter("jev-api-key", FromConfig(builder, "jev-api-key"), secret: true);

var openjev = builder.AddExternalService("openjev", openjevUrl)
    .WithHttpHealthCheck("/health");

// research.md §2.1 / Q12: the 3 agents' LLM is any OpenAI-compatible endpoint (Microsoft
// Foundry's v1 endpoint, a local proxy like LiteLLM, vLLM, etc.) - no Entra needed
// (requirement 4 still holds). Defaults are empty so `aspire run` never blocks on them; a
// missing/blank value fails gracefully at the call site (Common/AiSetup.cs), matching the G4
// fail-closed design.
var openAiBaseUrl = builder.AddParameter("openai-base-url", FromConfig(builder, "openai-base-url"));
var openAiApiKey = builder.AddParameter("openai-api-key", FromConfig(builder, "openai-api-key"), secret: true);
var openAiModel = builder.AddParameter("openai-model", FromConfig(builder, "openai-model"));

var catalog = builder.AddProject<Projects.ProductCatalogService>("catalog");

builder.AddProject<Projects.CounterService>("counter")
    .WithReference(catalog)
    .WaitFor(catalog) // research.md B23: the menu must exist before the first order
    .WithReference(openjev) // no WaitFor: the external service may be down, G4 handles it
    .WithEnvironment("Jev__BaseUrl", openjevUrl)
    .WithEnvironment("Jev__ApiKey", jevApiKey)
    .WithEnvironment("OPENAI_BASE_URL", openAiBaseUrl)
    .WithEnvironment("OPENAI_API_KEY", openAiApiKey)
    .WithEnvironment("OPENAI_MODEL", openAiModel);
// No WithExternalHttpEndpoints(): local-only demo (Q2).

builder.Build().Run();
