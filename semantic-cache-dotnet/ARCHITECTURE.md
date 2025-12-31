# Architecture Overview

## System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Aspire Dashboard                          │
│              (Monitoring, Logs, Traces, Health)                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Aspire AppHost                              │
│              (Orchestration & Service Discovery)                 │
└─────────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌──────────────┐     ┌──────────────┐    ┌──────────────┐
│  PostgreSQL  │     │    Redis     │    │  API Service │
│   Container  │     │  Container   │    │   (Net 10)   │
│              │     │              │    │              │
│ - pgAdmin    │     │ - Commander  │    │ - Scalar UI  │
│ - snake_case │     │ - L2 Cache   │    │ - Health     │
└──────────────┘     └──────────────┘    └──────────────┘
```

## API Service Architecture (Vertical Slices)

```
SemanticCache.Api/
│
├── Features/                          # Vertical Slices
│   ├── Chat/
│   │   └── ChatEndpoints.cs          # /api/chat/{raw,cached,personalized}
│   ├── Cache/
│   │   └── CacheEndpoints.cs         # /api/cache
│   ├── Users/
│   │   └── UserEndpoints.cs          # /api/users
│   └── Metrics/
│       └── MetricsEndpoints.cs       # /api/metrics
│
├── Infrastructure/                    # Shared Infrastructure
│   ├── Database/
│   │   ├── Entities/                 # EF Core Models
│   │   ├── AppDbContext.cs           # DbContext
│   │   └── DatabaseSeeder.cs         # Initial Data
│   └── Services/
│       ├── VectorSimilarityService.cs   # Cosine Similarity
│       ├── SemanticCacheService.cs      # Semantic Caching Logic
│       ├── AgentMemoryService.cs        # Conversation State
│       └── ChatService.cs               # OpenAI Integration
│
└── Program.cs                         # Startup Configuration
```

## Data Flow - Semantic Caching

### Cache Hit Scenario

```
User Request
     │
     ▼
┌─────────────────┐
│  Chat Endpoint  │
└─────────────────┘
     │
     ▼
┌─────────────────────────────┐
│    ChatService              │
│  GenerateEmbedding()        │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│  SemanticCacheService       │
│  GetSemanticCacheAsync()    │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│     HybridCache (L1)        │
│   Check Memory Cache        │
└─────────────────────────────┘
     │ Hit!
     ▼
┌─────────────────────────────┐
│  VectorSimilarityService    │
│  CalculateCosineSimilarity()│
└─────────────────────────────┘
     │ Similarity ≥ 0.85
     ▼
┌─────────────────────────────┐
│   Return Cached Response    │
│   Response Time: ~50ms      │
└─────────────────────────────┘
```

### Cache Miss Scenario

```
User Request
     │
     ▼
┌─────────────────┐
│  Chat Endpoint  │
└─────────────────┘
     │
     ▼
┌─────────────────────────────┐
│    ChatService              │
│  GenerateEmbedding()        │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│  SemanticCacheService       │
│  GetSemanticCacheAsync()    │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│  VectorSimilarityService    │
│  No Match (< 0.85)          │
└─────────────────────────────┘
     │ Miss!
     ▼
┌─────────────────────────────┐
│    Azure OpenAI             │
│  Chat Completion API        │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│  SemanticCacheService       │
│  SetSemanticCacheAsync()    │
│  Store: Query + Response    │
│         + Embedding         │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│     PostgreSQL              │
│  Persist to cache_entries   │
└─────────────────────────────┘
     │
     ▼
┌─────────────────────────────┐
│   Return Fresh Response     │
│   Response Time: ~1800ms    │
└─────────────────────────────┘
```

## Database Schema (PostgreSQL with snake_case)

```sql
-- User Management
user_profiles (
    id VARCHAR PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    created_at TIMESTAMP,
    updated_at TIMESTAMP
)

user_preferences (
    id VARCHAR PRIMARY KEY,
    user_id VARCHAR FK → user_profiles.id,
    preference_key VARCHAR(100),
    preference_value VARCHAR(500),
    created_at TIMESTAMP,
    updated_at TIMESTAMP
)

-- Conversation Management
conversation_entries (
    id VARCHAR PRIMARY KEY,
    user_id VARCHAR FK → user_profiles.id,
    thread_id VARCHAR,
    role VARCHAR(50),  -- 'user' or 'assistant'
    content TEXT,
    timestamp TIMESTAMP
)

context_memories (
    id VARCHAR PRIMARY KEY,
    user_id VARCHAR FK → user_profiles.id,
    memory_key VARCHAR(100),
    memory_value VARCHAR(1000),
    created_at TIMESTAMP,
    updated_at TIMESTAMP
)

-- Semantic Cache
cache_entries (
    id VARCHAR PRIMARY KEY,
    query VARCHAR(1000) NOT NULL,
    response TEXT NOT NULL,
    embedding FLOAT[] NOT NULL,  -- 1536 dimensions
    metadata VARCHAR,
    hit_count INT DEFAULT 0,
    created_at TIMESTAMP,
    last_accessed_at TIMESTAMP
)

-- Metrics
metrics_logs (
    id VARCHAR PRIMARY KEY,
    endpoint VARCHAR(200),
    method VARCHAR(10),
    status_code INT,
    response_time_ms BIGINT,
    cache_hit BOOLEAN,
    user_id VARCHAR,
    timestamp TIMESTAMP
)
```

## HybridCache Architecture

```
Request
   │
   ▼
┌─────────────────────────────────┐
│         HybridCache             │
│                                 │
│  ┌───────────────────────────┐ │
│  │   L1: In-Memory Cache     │ │
│  │   - Ultra Fast (< 1ms)    │ │
│  │   - Process-local         │ │
│  │   - TTL: 5 minutes        │ │
│  └───────────────────────────┘ │
│             │                   │
│             │ Miss              │
│             ▼                   │
│  ┌───────────────────────────┐ │
│  │   L2: Redis Cache         │ │
│  │   - Fast (5-20ms)         │ │
│  │   - Distributed           │ │
│  │   - TTL: 30 minutes       │ │
│  └───────────────────────────┘ │
│             │                   │
└─────────────┼───────────────────┘
              │ Miss
              ▼
    ┌────────────────────┐
    │    PostgreSQL      │
    │  (Source of Truth) │
    └────────────────────┘
```

## Agent Memory Flow (Personalized Chat)

```
User Request (Personalized)
     │
     ▼
┌─────────────────────────────────┐
│   AgentMemoryService            │
│   GetOrCreateUserAsync()        │
└─────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────┐
│   Load User Context             │
│   - Preferences                 │
│   - Conversation History        │
│   - Context Memories            │
└─────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────┐
│   ChatService                   │
│   GetPersonalizedResponseAsync()│
└─────────────────────────────────┘
     │
     ├─→ Check SemanticCache
     │   (with user context)
     │
     └─→ If Miss:
         ┌─────────────────────────┐
         │  Build Context Prompt:  │
         │  - System: User info    │
         │  - History: Last 10 msg │
         │  - Current: Query       │
         └─────────────────────────┘
              │
              ▼
         ┌─────────────────────────┐
         │   Azure OpenAI          │
         │   (Context-Aware)       │
         └─────────────────────────┘
              │
              ▼
         ┌─────────────────────────┐
         │  Save Conversation      │
         │  - User message         │
         │  - Assistant response   │
         └─────────────────────────┘
```

## Technology Integration Points

```
┌──────────────────────────────────────────────────────────┐
│                     .NET 10 Web API                       │
└──────────────────────────────────────────────────────────┘
                           │
       ┌───────────────────┼───────────────────┐
       ▼                   ▼                   ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ EF Core 10   │   │   Aspire 13  │   │ Azure.AI.    │
│              │   │              │   │ OpenAI 2.1   │
│ - Npgsql     │   │ - Hosting    │   │              │
│ - Migrations │   │ - Dashboard  │   │ - Chat       │
│ - snake_case │   │ - Telemetry  │   │ - Embeddings │
└──────────────┘   └──────────────┘   └──────────────┘
       │                   │                   │
       ▼                   ▼                   ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────┐
│ PostgreSQL   │   │ Redis + PG   │   │ Azure OpenAI │
│              │   │ Containers   │   │   Service    │
└──────────────┘   └──────────────┘   └──────────────┘
```

## Service Dependencies

```
ChatService
    ├─→ AzureOpenAIClient (Azure.AI.OpenAI)
    ├─→ SemanticCacheService
    │       ├─→ HybridCache (L1 + L2 Redis)
    │       ├─→ VectorSimilarityService
    │       └─→ AppDbContext (PostgreSQL)
    ├─→ AgentMemoryService
    │       └─→ AppDbContext (PostgreSQL)
    └─→ AppDbContext (PostgreSQL)

Registered as:
- Singleton: AzureOpenAIClient
- Scoped: All Services, AppDbContext
```

## Performance Characteristics

| Operation | Latency | Storage |
|-----------|---------|---------|
| L1 Cache Hit | < 1ms | Memory (per instance) |
| L2 Cache Hit | 5-20ms | Redis (distributed) |
| Vector Similarity | 1-5ms | In-memory computation |
| PostgreSQL Query | 10-50ms | Persistent storage |
| Azure OpenAI Chat | 1500-3000ms | External API |
| Azure OpenAI Embedding | 100-300ms | External API |

## Scaling Considerations

### Horizontal Scaling
- **API Instances**: Stateless, can scale indefinitely
- **HybridCache L1**: Independent per instance
- **HybridCache L2**: Shared Redis (scales with Redis cluster)
- **PostgreSQL**: Connection pooling, read replicas

### Vertical Scaling
- **Vector Similarity**: CPU-bound, benefits from more cores
- **Memory**: L1 cache size depends on available RAM
- **Database**: Larger instance for more concurrent connections

### Optimization Tips
1. Increase similarity threshold to reduce OpenAI calls
2. Tune HybridCache TTL for your use case
3. Use connection pooling for PostgreSQL
4. Monitor cache hit rate and adjust strategy
5. Consider pgvector extension for native vector search (future enhancement)

## Security Architecture

```
┌─────────────────────────────────────────┐
│          HTTPS/TLS (443/7000)           │
└─────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│         ASP.NET Core Middleware         │
│  - Authentication (if enabled)          │
│  - Authorization                        │
│  - Rate Limiting                        │
└─────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────┐
│        Service Layer (Business Logic)   │
└─────────────────────────────────────────┘
                  │
      ┌───────────┼───────────┐
      ▼           ▼           ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│PostgreSQL│ │  Redis   │ │ Azure    │
│          │ │          │ │ OpenAI   │
│ SSL/TLS  │ │ SSL/TLS  │ │ HTTPS +  │
│          │ │          │ │ API Key/ │
│          │ │          │ │ Identity │
└──────────┘ └──────────┘ └──────────┘
```

## Deployment Architecture (Production)

```
┌─────────────────────────────────────────────────────┐
│              Azure Container Apps                    │
│  ┌───────────────────────────────────────────────┐  │
│  │  API Service (Multiple Replicas)              │  │
│  │  - Auto-scaling (CPU/Memory/HTTP)             │  │
│  │  - Health Probes                              │  │
│  │  - Rolling Updates                            │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
                          │
         ┌────────────────┼────────────────┐
         ▼                ▼                ▼
┌─────────────────┐ ┌─────────────┐ ┌─────────────────┐
│ Azure Database  │ │Azure Cache  │ │ Azure OpenAI    │
│ for PostgreSQL  │ │ for Redis   │ │    Service      │
│                 │ │             │ │                 │
│ - Flexible      │ │ - Premium   │ │ - Managed       │
│ - Backups       │ │ - Geo-Rep   │ │ - Rate Limits   │
│ - Read Replicas │ │ - Cluster   │ │ - Quotas        │
└─────────────────┘ └─────────────┘ └─────────────────┘
```

---

## Key Design Decisions

1. **Vertical Slice Architecture**: Features are self-contained, making the codebase easier to navigate and maintain

2. **Snake Case for PostgreSQL**: Using EFCore.NamingConventions for automatic PascalCase → snake_case conversion

3. **HybridCache**: Two-tier caching strategy balances performance (L1) with distributed consistency (L2)

4. **In-Memory Vector Similarity**: Avoids external dependencies like Redis Stack while maintaining fast similarity search

5. **Aspire Orchestration**: Simplifies local development and provides production-ready deployment manifests

6. **Agent Framework Integration**: Prepares for future agent-based workflows with proper conversation state management

---

**This architecture supports:**
- ✅ High throughput (L1 cache)
- ✅ Distributed deployments (L2 cache)
- ✅ Data persistence (PostgreSQL)
- ✅ Semantic search (Vector similarity)
- ✅ Context-aware responses (Agent memory)
- ✅ Production monitoring (Aspire + OpenTelemetry)
- ✅ Easy local development (Aspire containers)
