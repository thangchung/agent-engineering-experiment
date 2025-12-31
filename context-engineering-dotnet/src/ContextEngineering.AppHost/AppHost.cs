var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume("context-eng-pgdata");

var contextDb = postgres.AddDatabase("contextdb");

// Add the API project (owns the database directly, uses plugins instead of MCP)
var api = builder.AddProject<Projects.ContextEngineering_Api>("api")
    .WithReference(contextDb)
    .WaitFor(contextDb)
    .WithExternalHttpEndpoints();

builder.Build().Run();
