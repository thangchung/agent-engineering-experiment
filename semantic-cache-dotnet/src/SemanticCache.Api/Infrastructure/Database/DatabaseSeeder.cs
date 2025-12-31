using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database.Entities;

namespace SemanticCache.Api.Infrastructure.Database;

public class DatabaseSeeder(AppDbContext context)
{
    private readonly AppDbContext _context = context;

    public async Task SeedAsync()
    {
        // Ensure database is created
        await _context.Database.EnsureCreatedAsync();
        
        // Check if already seeded
        if (await _context.UserProfiles.AnyAsync())
        {
            return;
        }

        // Seed 3 users
        var users = new[]
        {
            new UserProfile 
            { 
                Id = "user-001", 
                Name = "Alice Johnson",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                UpdatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new UserProfile 
            { 
                Id = "user-002", 
                Name = "Bob Smith",
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                UpdatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new UserProfile 
            { 
                Id = "user-003", 
                Name = "Carol Williams",
                CreatedAt = DateTime.UtcNow.AddDays(-15),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            }
        };
        await _context.UserProfiles.AddRangeAsync(users);
        
        // Seed user preferences
        var preferences = new[]
        {
            new UserPreference { Id = "pref-001", UserId = "user-001", PreferenceKey = "language", PreferenceValue = "English" },
            new UserPreference { Id = "pref-002", UserId = "user-001", PreferenceKey = "theme", PreferenceValue = "dark" },
            new UserPreference { Id = "pref-003", UserId = "user-002", PreferenceKey = "language", PreferenceValue = "Spanish" },
            new UserPreference { Id = "pref-004", UserId = "user-002", PreferenceKey = "timezone", PreferenceValue = "America/New_York" },
            new UserPreference { Id = "pref-005", UserId = "user-003", PreferenceKey = "language", PreferenceValue = "French" }
        };
        await _context.UserPreferences.AddRangeAsync(preferences);
        
        // Seed 5 cache entries with sample embeddings (1536 dimensions for text-embedding-ada-002)
        var cacheEntries = new[]
        {
            new CacheEntry
            {
                Id = "cache-001",
                Query = "What is the capital of France?",
                Response = "The capital of France is Paris.",
                Embedding = GenerateRandomEmbedding(),
                HitCount = 15,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                LastAccessedAt = DateTime.UtcNow.AddHours(-2)
            },
            new CacheEntry
            {
                Id = "cache-002",
                Query = "How does photosynthesis work?",
                Response = "Photosynthesis is the process by which plants convert light energy into chemical energy, using carbon dioxide and water to produce glucose and oxygen.",
                Embedding = GenerateRandomEmbedding(),
                HitCount = 8,
                CreatedAt = DateTime.UtcNow.AddDays(-7),
                LastAccessedAt = DateTime.UtcNow.AddHours(-5)
            },
            new CacheEntry
            {
                Id = "cache-003",
                Query = "What is machine learning?",
                Response = "Machine learning is a subset of artificial intelligence that enables systems to learn and improve from experience without being explicitly programmed.",
                Embedding = GenerateRandomEmbedding(),
                HitCount = 22,
                CreatedAt = DateTime.UtcNow.AddDays(-12),
                LastAccessedAt = DateTime.UtcNow.AddMinutes(-30)
            },
            new CacheEntry
            {
                Id = "cache-004",
                Query = "Explain quantum computing",
                Response = "Quantum computing uses quantum bits (qubits) that can exist in superposition states, allowing quantum computers to process information in fundamentally different ways than classical computers.",
                Embedding = GenerateRandomEmbedding(),
                HitCount = 5,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                LastAccessedAt = DateTime.UtcNow.AddHours(-12)
            },
            new CacheEntry
            {
                Id = "cache-005",
                Query = "What are the benefits of exercise?",
                Response = "Regular exercise improves cardiovascular health, strengthens muscles and bones, enhances mental well-being, helps manage weight, and reduces the risk of chronic diseases.",
                Embedding = GenerateRandomEmbedding(),
                HitCount = 12,
                CreatedAt = DateTime.UtcNow.AddDays(-8),
                LastAccessedAt = DateTime.UtcNow.AddHours(-1)
            }
        };
        await _context.CacheEntries.AddRangeAsync(cacheEntries);
        
        // Seed 50 metrics logs
        var metricsLogs = new List<MetricsLog>();
        var endpoints = new[] { "/api/chat/raw", "/api/chat/cached", "/api/chat/personalized", "/api/cache", "/api/users" };
        var methods = new[] { "GET", "POST" };
        var random = new Random(42);
        
        for (int i = 0; i < 50; i++)
        {
            metricsLogs.Add(new MetricsLog
            {
                Id = $"metrics-{i + 1:D3}",
                Endpoint = endpoints[random.Next(endpoints.Length)],
                Method = methods[random.Next(methods.Length)],
                StatusCode = random.Next(10) < 9 ? 200 : (random.Next(2) == 0 ? 404 : 500),
                ResponseTimeMs = random.Next(50, 2000),
                CacheHit = random.Next(2) == 0,
                UserId = random.Next(2) == 0 ? users[random.Next(users.Length)].Id : null,
                Timestamp = DateTime.UtcNow.AddHours(-random.Next(1, 72))
            });
        }
        await _context.MetricsLogs.AddRangeAsync(metricsLogs);
        
        await _context.SaveChangesAsync();
    }
    
    private static float[] GenerateRandomEmbedding()
    {
        // Generate a normalized random embedding (1536 dimensions for text-embedding-ada-002)
        var random = new Random();
        var embedding = new float[1536];
        var sumSquares = 0.0;
        
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
            sumSquares += embedding[i] * embedding[i];
        }
        
        // Normalize to unit length
        var magnitude = Math.Sqrt(sumSquares);
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= (float)magnitude;
        }
        
        return embedding;
    }
}
