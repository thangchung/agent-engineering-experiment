# Quick Start Guide

## Prerequisites Setup

### 1. Install .NET 10 SDK
Download from: https://dotnet.microsoft.com/download/dotnet/10.0

Verify installation:
```bash
dotnet --version
```

### 2. Install Docker Desktop
Download from: https://www.docker.com/products/docker-desktop

Verify installation:
```bash
docker --version
```

### 3. Azure OpenAI Setup

#### Option A: Azure Portal
1. Create Azure OpenAI resource
2. Deploy models:
   - `gpt-4` (or `gpt-35-turbo`)
   - `text-embedding-ada-002`
3. Copy endpoint and API key

#### Option B: Using Azure CLI
```bash
# Login
az login

# Create resource group
az group create --name rg-semantic-cache --location eastus

# Create Azure OpenAI resource
az cognitiveservices account create \
  --name my-openai-resource \
  --resource-group rg-semantic-cache \
  --kind OpenAI \
  --sku S0 \
  --location eastus

# Deploy models
az cognitiveservices account deployment create \
  --name my-openai-resource \
  --resource-group rg-semantic-cache \
  --deployment-name gpt-4 \
  --model-name gpt-4 \
  --model-version "0613" \
  --model-format OpenAI

az cognitiveservices account deployment create \
  --name my-openai-resource \
  --resource-group rg-semantic-cache \
  --deployment-name text-embedding-ada-002 \
  --model-name text-embedding-ada-002 \
  --model-version "2" \
  --model-format OpenAI
```

## Configuration

### 1. Update Configuration

Edit `src/SemanticCache.Api/appsettings.Development.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR_RESOURCE_NAME.openai.azure.com/",
    "ApiKey": "YOUR_API_KEY_HERE",
    "ChatModel": "gpt-4",
    "EmbeddingModel": "text-embedding-ada-002"
  }
}
```

**Using Managed Identity (Production):**
Leave `ApiKey` empty and ensure your deployment has Managed Identity enabled with "Cognitive Services OpenAI User" role.

## Running the Application

### Method 1: Using Aspire (Recommended)

```bash
# Navigate to AppHost
cd src/SemanticCache.AppHost

# Run Aspire
dotnet run
```

This will:
- Start PostgreSQL container
- Start Redis container
- Start the API
- Open Aspire Dashboard at https://localhost:17287

### Method 2: Using Visual Studio

1. Open `SemanticCache.sln`
2. Set `SemanticCache.AppHost` as startup project
3. Press F5

### Method 3: Manual (without Aspire)

**Start PostgreSQL:**
```bash
docker run -d \
  --name postgres-semantic-cache \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=semanticcachedb \
  -p 5432:5432 \
  postgres:latest
```

**Start Redis:**
```bash
docker run -d \
  --name redis-semantic-cache \
  -p 6379:6379 \
  redis:latest
```

**Update connection strings in `appsettings.Development.json`:**
```json
{
  "ConnectionStrings": {
    "semanticcachedb": "Host=localhost;Port=5432;Database=semanticcachedb;Username=postgres;Password=postgres",
    "redis": "localhost:6379"
  }
}
```

**Run API:**
```bash
cd src/SemanticCache.Api
dotnet run
```

## Accessing the Application

### Aspire Dashboard
- URL: https://localhost:17287
- Features:
  - View all running resources
  - Monitor logs
  - Check health status
  - View traces and metrics

### Scalar OpenAPI UI
- URL: https://localhost:7000/scalar/v1
- Features:
  - Interactive API documentation
  - Test endpoints directly
  - View request/response examples

### API Endpoints
Base URL: https://localhost:7000

### pgAdmin (PostgreSQL Management)
- URL: http://localhost:5050
- Login with Aspire-provided credentials from dashboard

### Redis Commander
- URL: http://localhost:8081
- View cached data in real-time

## Testing the API

### 1. Test Raw Chat (No Caching)

```bash
curl -X POST https://localhost:7000/api/chat/raw \
  -H "Content-Type: application/json" \
  -d '{"query": "What is the capital of France?"}'
```

Expected response time: ~1500-3000ms

### 2. Test Cached Chat (First Call)

```bash
curl -X POST https://localhost:7000/api/chat/cached \
  -H "Content-Type: application/json" \
  -d '{"query": "What is machine learning?"}'
```

Expected: `"fromCache": false` (cache miss)

### 3. Test Cached Chat (Second Call - Similar Query)

```bash
curl -X POST https://localhost:7000/api/chat/cached \
  -H "Content-Type: application/json" \
  -d '{"query": "Explain machine learning"}'
```

Expected: `"fromCache": true, "similarity": 0.92` (cache hit, ~40-100ms)

### 4. Test Personalized Chat

```bash
curl -X POST https://localhost:7000/api/chat/personalized \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user-001",
    "userName": "Alice Johnson",
    "query": "What are my preferences?",
    "threadId": "thread-001"
  }'
```

### 5. View Cache Statistics

```bash
curl https://localhost:7000/api/cache
```

### 6. View Metrics

```bash
curl https://localhost:7000/api/metrics/stats
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "GetAllUsers_ReturnsSeededUsers"
```

## Troubleshooting

### Issue: Port already in use
**Solution:** Stop conflicting containers
```bash
docker ps
docker stop <container-id>
```

### Issue: Database not seeded
**Solution:** Delete and recreate database
```bash
# In Aspire, containers are ephemeral
# Just restart the AppHost
```

### Issue: Azure OpenAI quota exceeded
**Solution:** Check quota in Azure Portal or use rate limiting

### Issue: Connection refused to Redis/PostgreSQL
**Solution:** Ensure Docker Desktop is running
```bash
docker ps  # Should show running containers
```

### Issue: OpenAPI deprecation warnings
These are safe to ignore - they're warnings about upcoming API changes in .NET 10.

## Next Steps

1. **Explore the Aspire Dashboard**: Monitor logs and traces
2. **Try Different Queries**: Test semantic similarity matching
3. **Check Database**: Use pgAdmin to view snake_case tables
4. **Monitor Cache**: Use Redis Commander to see cached embeddings
5. **Run Integration Tests**: Verify everything works end-to-end

## Advanced Configuration

### Adjusting Similarity Threshold
Lower threshold = more cache hits (less accurate)
Higher threshold = fewer cache hits (more accurate)

```json
{
  "query": "What is AI?",
  "similarityThreshold": 0.90  // 0.85 is default
}
```

### Configuring HybridCache
In `Program.cs`:
```csharp
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(60),          // Increase for longer caching
        LocalCacheExpiration = TimeSpan.FromMinutes(10)  // L1 cache duration
    };
});
```

### Using Different OpenAI Models

Update `appsettings.Development.json`:
```json
{
  "AzureOpenAI": {
    "ChatModel": "gpt-35-turbo",  // Cheaper, faster
    "EmbeddingModel": "text-embedding-3-small"  // Newer model
  }
}
```

## Production Deployment

See [README.md](README.md#-deployment) for detailed deployment instructions.

## Support

- **Issues**: Create a GitHub issue
- **Documentation**: See [README.md](README.md)
- **Aspire Docs**: https://learn.microsoft.com/dotnet/aspire/
- **Azure OpenAI Docs**: https://learn.microsoft.com/azure/ai-services/openai/

---

Happy Coding! 🚀
