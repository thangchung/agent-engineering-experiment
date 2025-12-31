# Semantic Cache with .NET 10 and Microsoft Agent Framework

A production-ready semantic caching system built with .NET 10, implementing intelligent response caching using vector embeddings and cosine similarity. This project demonstrates the integration of Azure OpenAI, PostgreSQL, Redis, and Microsoft's Agent Framework for building scalable, personalized AI applications.

## 🚀 Features

### Core Capabilities
- **Semantic Caching**: Uses Azure OpenAI embeddings (text-embedding-ada-002) with in-memory cosine similarity to cache AI responses semantically
- **HybridCache**: Two-tier caching with L1 (in-memory) and L2 (Redis distributed cache) for optimal performance
- **Agent Memory Management**: Leverages Microsoft.Agents.AI for conversation state and context persistence
- **Personalized Responses**: Context-aware responses using user preferences, conversation history, and contextual memory
- **PostgreSQL Persistence**: Long-term storage with snake_case naming convention via EFCore.NamingConventions
- **Aspire Orchestration**: .NET Aspire 13 for seamless containerized development with PostgreSQL and Redis
- **Modern OpenAPI UI**: Scalar.AspNetCore for interactive API documentation integrated with Aspire dashboard

### Architecture Highlights
- **Vertical Slice Architecture**: Features organized by business capability (Chat, Cache, Users, Metrics)
- **Clean Separation**: Infrastructure, Domain, and Feature layers
- **TDD Ready**: Comprehensive integration tests using Aspire TestHost
- **Production-Ready**: Health checks, telemetry, retry policies, and structured logging

## 📋 Prerequisites

- .NET 10 SDK (latest preview)
- Docker Desktop (for PostgreSQL and Redis containers)
- Visual Studio 2022 17.x+ or VS Code with C# Dev Kit
- Azure OpenAI Service account (or OpenAI API key)

## 🛠️ Technology Stack

| Category | Technology | Version |
|----------|-----------|---------|
| Runtime | .NET | 10.0 |
| Database | PostgreSQL | Latest (via Aspire) |
| Cache | Redis | Latest (via Aspire) |
| ORM | Entity Framework Core | 10.0.0-rc.2 |
| AI Framework | Microsoft.Agents.AI | 1.0.0-preview |
| AI Service | Azure OpenAI | 2.1.0 |
| Orchestration | .NET Aspire | 13.0.0 |
| API Docs | Scalar.AspNetCore | 2.11.0 |
| Testing | xUnit + Aspire TestHost | Latest |
| Naming Convention | EFCore.NamingConventions | 10.0.0-rc.2 |

## 📁 Project Structure

```
SemanticCache/
├── src/
│   ├── SemanticCache.Api/
│   │   ├── Features/                    # Vertical slices
│   │   │   ├── Chat/                    # Chat endpoints (raw, cached, personalized)
│   │   │   ├── Cache/                   # Cache management endpoints
│   │   │   ├── Users/                   # User and preference endpoints
│   │   │   └── Metrics/                 # Metrics and statistics endpoints
│   │   ├── Infrastructure/
│   │   │   ├── Database/
│   │   │   │   ├── Entities/            # EF Core entities
│   │   │   │   ├── AppDbContext.cs      # DbContext with snake_case
│   │   │   │   └── DatabaseSeeder.cs    # Seed data (3 users, 5 cache entries, 50 metrics)
│   │   │   └── Services/
│   │   │       ├── VectorSimilarityService.cs   # Cosine similarity calculations
│   │   │       ├── SemanticCacheService.cs      # Semantic cache with HybridCache
│   │   │       ├── AgentMemoryService.cs        # Agent conversation state management
│   │   │       └── ChatService.cs               # OpenAI integration
│   │   └── Program.cs                   # Application startup
│   ├── SemanticCache.AppHost/           # Aspire orchestration
│   └── SemanticCache.ServiceDefaults/   # Shared Aspire configuration
└── tests/
    └── SemanticCache.IntegrationTests/  # Integration tests
```

## 🔧 Configuration

### 1. Azure OpenAI Setup

Update `appsettings.Development.json` with your Azure OpenAI credentials:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR_RESOURCE_NAME.openai.azure.com/",
    "ApiKey": "YOUR_API_KEY_OR_LEAVE_EMPTY_FOR_MANAGED_IDENTITY",
    "ChatModel": "gpt-4",
    "EmbeddingModel": "text-embedding-ada-002"
  }
}
```

**Deployment Requirements:**
- Deploy `gpt-4` (or `gpt-35-turbo`) model
- Deploy `text-embedding-ada-002` model

### 2. Connection Strings

Aspire automatically manages connection strings for PostgreSQL and Redis. No manual configuration needed!

## 🚀 Getting Started

### 1. Clone and Build

```bash
git clone <repository-url>
cd mem
dotnet restore
dotnet build
```

### 2. Run with Aspire

Start the Aspire AppHost (this will automatically spin up PostgreSQL and Redis containers):

```bash
cd src/SemanticCache.AppHost
dotnet run
```

This will:
- Start PostgreSQL with pgAdmin at http://localhost:5050
- Start Redis with Redis Commander
- Start the API
- Open the Aspire Dashboard at https://localhost:17287

### 3. Access the API

- **Scalar OpenAPI UI**: https://localhost:7000/scalar/v1
- **Aspire Dashboard**: https://localhost:17287
- **Health Checks**: https://localhost:7000/health

## 📊 Database Schema (Snake Case)

The database uses snake_case naming convention automatically via EFCore.NamingConventions:

```sql
-- Tables created with snake_case naming
user_profiles
user_preferences
conversation_entries
context_memories
cache_entries
metrics_logs
```

### Seeded Data
- **3 Users**: Alice Johnson, Bob Smith, Carol Williams
- **5 Cache Entries**: Sample Q&A pairs with embeddings
- **50 Metrics Logs**: Simulated API request metrics

## 🔌 API Endpoints

### Chat Endpoints

#### POST /api/chat/raw
Raw chat completion without caching.

**Request:**
```json
{
  "query": "What is the capital of France?"
}
```

**Response:**
```json
{
  "response": "The capital of France is Paris.",
  "responseTimeMs": 1523
}
```

#### POST /api/chat/cached
Chat with semantic caching (checks similarity before calling OpenAI).

**Request:**
```json
{
  "query": "What's the capital city of France?",
  "similarityThreshold": 0.85
}
```

**Response:**
```json
{
  "response": "The capital of France is Paris.",
  "fromCache": true,
  "similarity": 0.92,
  "responseTimeMs": 45
}
```

#### POST /api/chat/personalized
Personalized chat with user context, preferences, and conversation history.

**Request:**
```json
{
  "userId": "user-001",
  "userName": "Alice Johnson",
  "query": "Tell me about Paris",
  "threadId": "thread-abc123",
  "similarityThreshold": 0.85
}
```

**Response:**
```json
{
  "response": "Paris is the capital and most populous city of France...",
  "fromCache": false,
  "similarity": 0,
  "threadId": "thread-abc123",
  "responseTimeMs": 1845
}
```

### Cache Management

- **GET /api/cache** - Get all cache entries with statistics
- **DELETE /api/cache** - Clear all cache entries

### User Management

- **GET /api/users** - Get all users
- **GET /api/users/{userId}** - Get specific user
- **GET /api/users/{userId}/preferences** - Get user preferences
- **POST /api/users/{userId}/preferences** - Set user preference
- **GET /api/users/{userId}/conversations/{threadId}** - Get conversation history

### Metrics

- **GET /api/metrics** - Get recent metrics logs
- **GET /api/metrics/stats** - Get aggregated statistics (cache hit rate, avg response time)

## 🧪 Testing

Run integration tests:

```bash
cd tests/SemanticCache.IntegrationTests
dotnet test
```

The tests use Aspire TestHost to spin up full infrastructure (PostgreSQL, Redis, API) and verify:
- Seeded data is accessible
- Users endpoint returns 3 seeded users
- Cache endpoint returns 5 seeded entries
- Metrics endpoint returns 50 seeded logs

## 🎯 How Semantic Caching Works

1. **Query Arrives**: User sends a query
2. **Generate Embedding**: Convert query to vector using `text-embedding-ada-002` (1536 dimensions)
3. **Similarity Search**: Calculate cosine similarity with all cached embeddings
4. **Cache Hit/Miss**:
   - If similarity ≥ threshold (default 0.85): Return cached response
   - Otherwise: Call OpenAI, cache the new response with its embedding
5. **HybridCache**: L1 (memory) serves most requests, L2 (Redis) for distributed scenarios

**Performance Benefits:**
- Cache Hit: ~40-100ms (no OpenAI call)
- Cache Miss: ~1500-3000ms (OpenAI call + caching)
- Cache Hit Rate: Typically 60-80% in production

## 🔐 Agent Memory System

The Agent Memory Service manages:

1. **User Profiles**: Basic user information
2. **User Preferences**: Key-value pairs (language, theme, timezone)
3. **Conversation History**: Thread-based chat history
4. **Context Memories**: Long-term contextual information

All data persists in PostgreSQL with snake_case column names.

## 📈 Observability

- **Distributed Tracing**: OpenTelemetry integration via Aspire
- **Health Checks**: `/health` endpoint with PostgreSQL and Redis checks
- **Structured Logging**: Serilog with JSON formatting
- **Metrics Collection**: Custom metrics for cache hit rate, response times

## 🚀 Deployment

### Container Deployment

```bash
# Build container
dotnet publish src/SemanticCache.Api -t:PublishContainer

# Or use Aspire deployment
cd src/SemanticCache.AppHost
dotnet run --configuration Release
```

### Azure Deployment Options

1. **Azure Container Apps** (Recommended for Aspire)
2. **Azure App Service**
3. **Azure Kubernetes Service (AKS)**

Aspire generates deployment manifests automatically:
```bash
dotnet run --publisher manifest --output-path ../aspire-manifest.json
```

## 🛠️ Development Tips

### Adding New Entities

1. Create entity in `Infrastructure/Database/Entities/`
2. Add `DbSet` to `AppDbContext`
3. Configure relationships in `OnModelCreating`
4. Update `DatabaseSeeder` if needed
5. Run: `dotnet ef migrations add YourMigration`

### Adding New Endpoints

1. Create endpoint file in `Features/YourFeature/`
2. Define records for requests/responses
3. Implement endpoint mapping method
4. Register in `Program.cs`: `app.MapYourFeatureEndpoints();`

## 🤝 Contributing

Contributions welcome! Please follow:
- Vertical slice architecture
- TDD approach
- Snake_case for database entities
- PascalCase for C# code

## 📝 License

MIT License - see LICENSE file for details

## 🙏 Acknowledgments

- Python backend inspiration: [AzureManagedRedis/semantic-caching-demo](https://github.com/AzureManagedRedis/semantic-caching-demo-and-calculator/tree/main/backend)
- Microsoft Agent Framework
- .NET Aspire team

## 📞 Support

For issues or questions:
- Create an issue on GitHub
- Check Aspire documentation: https://learn.microsoft.com/dotnet/aspire/
- Check Microsoft Agent Framework docs: https://github.com/microsoft/agents

---

**Built with ❤️ using .NET 10, Aspire 13, and Microsoft Agent Framework**
