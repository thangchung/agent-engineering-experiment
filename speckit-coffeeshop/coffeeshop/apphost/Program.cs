var builder = DistributedApplication.CreateBuilder(args);

// product-catalog MCP server — no external references
// Generated Aspire type alias: Projects.product_catalog (matches csproj name)
var catalog = builder.AddProject<Projects.product_catalog>("product-catalog");

// counter — references product-catalog for service discovery + MCP calls
// Generated Aspire type alias: Projects.counter
var counter = builder.AddProject<Projects.counter>("counter")
    .WithReference(catalog)
    .WithExternalHttpEndpoints();

// frontend Next.js dev server
// COUNTER_URL is injected by Aspire so route.ts can forward AG-UI requests to counter.
// PORT is read by Next.js (next dev --port $PORT) so Aspire controls the port.
// BaristaWorker and KitchenWorker are IHostedService classes compiled inside counter —
// they share counter's DI container and Channel<T> singletons; no separate AddProject.
_ = builder.AddNpmApp("frontend", "../frontend", "dev")
    .WithReference(counter)
    .WithEnvironment("COUNTER_URL", counter.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT");

builder.Build().Run();
