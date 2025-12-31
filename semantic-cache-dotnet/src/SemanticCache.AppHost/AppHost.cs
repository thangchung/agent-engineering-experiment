var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL with persistent volume
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("semanticcachedb");

// Add Redis for HybridCache L2 storage
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisCommander();

// Add the API project with references to PostgreSQL and Redis
var api = builder.AddProject<Projects.SemanticCache_Api>("api")
    .WithReference(postgresDb)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithUrls(context =>
    {
        // Configure URL display for Scalar (OpenAPI)
        context.Urls.Add(new()
        {
            Url = "/scalar",
            DisplayText = "UI URL",
            Endpoint = context.GetEndpoint("https")
        });
    });

builder.Build().Run();
