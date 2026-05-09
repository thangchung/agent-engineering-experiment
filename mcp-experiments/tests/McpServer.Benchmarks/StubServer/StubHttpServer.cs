using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace McpServer.Benchmarks.StubServer;

public class Brewery
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string BreweryType { get; init; } = string.Empty;
}

public static class StubBreweryBuilder
{
    public static List<Brewery> GenerateSanDiegoBreweries(int count = 250)
    {
        var types = new[] { "micro", "regional", "large", "planning", "closed" };
        var cities = new[] { "San Diego", "La Jolla", "Carlsbad", "Oceanside", "Encinitas" };
        var result = new List<Brewery>();
        for (int i = 0; i < count; i++)
        {
            result.Add(new Brewery
            {
                Id = $"sd-{i:D4}",
                Name = $"Brewery {i:D3}",
                City = cities[i % cities.Length],
                BreweryType = types[i % types.Length]
            });
        }
        return result;
    }

    public static List<Brewery> GenerateMoonBreweries()
    {
        return new List<Brewery>
        {
            new() { Id = "moon-1", Name = "Blue Moon Brewing", City = "Denver", BreweryType = "large" },
            new() { Id = "moon-2", Name = "Moon River Ales", City = "Portland", BreweryType = "micro" },
            new() { Id = "moon-3", Name = "Full Moon Taphouse", City = "Austin", BreweryType = "regional" },
            new() { Id = "moon-4", Name = "Harvest Moon Pub", City = "Boulder", BreweryType = "micro" },
            new() { Id = "moon-5", Name = "Crescent Moon Brewery", City = "San Francisco", BreweryType = "large" },
            new() { Id = "moon-6", Name = "Moonlight Brewing Co", City = "Oakland", BreweryType = "regional" },
        };
    }
}

public static class StubHttpServerFactory
{
    public static WebApplication BuildStubServer(int port = 5555)
    {
        var builder = WebApplication.CreateBuilder(new[] { "--urls", $"http://127.0.0.1:{port}" });
        builder.Services.AddCors();
        var app = builder.Build();

        var sdBreweries = StubBreweryBuilder.GenerateSanDiegoBreweries();
        var moonBreweries = StubBreweryBuilder.GenerateMoonBreweries();

        app.MapGet("/breweries/random", () =>
        {
            var random = new Random();
            return Results.Ok(sdBreweries[random.Next(sdBreweries.Count)]);
        });

        app.MapGet("/breweries", (string? byCity, int perPage = 200, int page = 1) =>
        {
            if (string.Equals(byCity, "san_diego", StringComparison.OrdinalIgnoreCase))
            {
                var startIdx = (page - 1) * perPage;
                var endIdx = Math.Min(startIdx + perPage, sdBreweries.Count);
                if (startIdx >= sdBreweries.Count)
                    return Results.Ok(Array.Empty<Brewery>());
                return Results.Ok(sdBreweries.GetRange(startIdx, endIdx - startIdx));
            }
            return Results.Ok(Array.Empty<Brewery>());
        });

        app.MapGet("/breweries/search", (string? query, int perPage = 200) =>
        {
            if (string.Equals(query, "moon", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(moonBreweries);
            return Results.Ok(Array.Empty<Brewery>());
        });

        return app;
    }
}
