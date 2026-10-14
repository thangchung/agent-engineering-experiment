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

// research.md §5.6 / §14 (Q1/Q2/Q12): the DecisionModel host is on the user's own LAN, local demo only,
// so no external endpoints and no TLS/auth work here - that is the LAN owner's responsibility.
//
// Provider switch: ONE value (decision-model-provider = typesafe | laya | custom) picks the whole
// endpoint. Each provider keeps its own decision-model-{provider}-url / -api-key, so the URL and the
// model name (which the counter derives from the provider) can never disagree. The plain
// decision-model-url / decision-model-api-key stay as a fallback for a single-provider setup.
var provider = FromConfig(builder, "decision-model-provider").Trim().ToLowerInvariant();
provider = provider.Length == 0 ? "typesafe" : provider;
string Pick(string suffix) =>
    FromConfig(builder, $"decision-model-{provider}-{suffix}") is { Length: > 0 } own ? own : FromConfig(builder, $"decision-model-{suffix}");

var decisionModelUrl = builder.AddParameter("decision-model-url", Pick("url"));
var decisionModelProvider = builder.AddParameter("decision-model-provider", provider);
var decisionModelModel = builder.AddParameter("decision-model-model", Pick("model"));
var decisionModelApiKey = builder.AddParameter("decision-model-api-key", Pick("api-key"), secret: true);

var decisionModel = builder.AddExternalService("decision-model", decisionModelUrl)
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

var counter = builder.AddProject<Projects.CounterService>("counter")
    .WithReference(catalog)
    .WaitFor(catalog) // research.md B23: the menu must exist before the first order
    .WithReference(decisionModel) // no WaitFor: the external service may be down, G4 handles it
    .WithEnvironment("DecisionModel__BaseUrl", decisionModelUrl)
    .WithEnvironment("DecisionModel__ApiKey", decisionModelApiKey)
    .WithEnvironment("DecisionModel__Provider", decisionModelProvider)
    .WithEnvironment("DecisionModel__Model", decisionModelModel)
    .WithEnvironment("OPENAI_BASE_URL", openAiBaseUrl)
    .WithEnvironment("OPENAI_API_KEY", openAiApiKey)
    .WithEnvironment("OPENAI_MODEL", openAiModel);
// Counter has no UI of its own any more (Q2: local-only, so no external endpoint); the browser talks to WebApp.

// research.md §17: the AG-UI front end. The browser reaches only this project.
builder.AddProject<Projects.WebApp>("web")
    .WithReference(counter)
    .WaitFor(counter)
    .WithExternalHttpEndpoints();

builder.Build().Run();
