# MCP Experiments

This repository implements a .NET MCP server experiment with:
- tool search (`search_tools`, `call_tool` model)
- code-mode discovery + constrained execution flow
- local orchestration baseline with Aspire AppHost

## Prerequisites

- .NET SDK 10+
- Docker Desktop (for container flow)
- Optional: Azure CLI and `kubectl` for AKS

## Local Build and Test

```bash
dotnet build McpExperiments.slnx
dotnet test McpExperiments.slnx
```

## Run MCP Server Locally

```bash
dotnet run --project src/McpServer/McpServer.csproj
```

The current server prints a startup smoke output for registry/search/execute wiring.

### CLI Query Mode

In CLI mode, `query` is explicit tool invocation only. Natural-language intent routing inside CLI is disabled.

```bash
dotnet run --no-launch-profile --project src/McpServer/McpServer.csproj -- query --tool brewery_search --args '{"query":"moon","per_page":5,"page":1}'
```

If `--tool` is omitted or a free-text intent is provided, CLI returns an error and does not call GitHub Copilot chat orchestration.

## Run Blazor MCP Test Website

This repo includes a simple Blazor UI to test OpenBrewery use cases through the MCP server.

1. Start MCP server:

```bash
dotnet run --project src/McpServer/McpServer.csproj
```

2. In another terminal, start the web UI:

```bash
dotnet run --project src/TestWeb/TestWeb.csproj
```

3. Open the shown local URL and run cases:
- Single Brewery
- List Breweries (filters and pagination)
- Random Brewery
- Search Breweries
- Metadata

The UI uses `call_tool` with the OpenBrewery compatibility aliases (`brewery_get`, `brewery_list`, `brewery_random`, `brewery_search`, `brewery_meta`).

## MCP Tools Reference

### Pinned Meta-Tools (Always Visible)

These tools are pinned and appear immediately in `list_tools` without requiring a search:

| Tool | Parameters | Description |
|------|-----------|-------------|
| `status` | None | Returns MCP server health, version, and current timestamp |

### Discovery & Execution Tools

Tools for discovering tool schemas and executing multi-step workflows:

| Tool | Parameters | Description |
|------|-----------|-------------|
| `search_tools` | `query` (string, required) | Search tool catalog by keyword. Returns matching tool names and brief descriptions. |
| `call_tool` | `toolName` (string), `arguments` (object) | Invoke a real tool by exact name with JSON parameters. |
| `search` | `query` (string, required), `detail` (string), `tags` (array) | Discover tools with optional schema detail level and tag filtering. |
| `get_schema` | `toolNames` (array, required), `detail` (string) | Retrieve input schemas for specific tool names at Brief/Detailed/Full detail levels. |
| `execute` | `code` (string, required) | Execute a Python or C# script that chains multiple tool calls in a sandbox. |
| `get_execute_syntax` | None | Returns code syntax guide and available runner environment info. |

**Schema Detail Levels:**
- `Brief` (0): Tool name only
- `Detailed` (1): Parameter names, types, and required flags (compact markdown)
- `Full` (2): Complete JSON Schema with descriptions

### OpenBrewery Tools (Loaded from `contracts/openbrewerydb.v1.openapi.yaml`)

Real tools accessible via `call_tool`, `search_tools`, or `execute`:

| Tool Name | Parameters | Description |
|-----------|-----------|-------------|
| `getSingleBrewery` | `obdb-id` (string, required) | Fetch a brewery by ID. |
| `listBreweries` | `by_city` (string), `by_name` (string), `by_state` (string), `by_postal` (string), `by_type` (string), `sort` (string), `page` (integer) | List breweries with optional filters and pagination. |
| `brewery_random` | None | Get a random brewery. |

**Example Parameters for `listBreweries`:**
```json
{
  "by_city": "San Diego",
  "by_type": "microbrewery",
  "page": 1
}
```

### Petstore Tools (Loaded from `https://petstore3.swagger.io/api/v3/openapi.json`)

Additional tools auto-loaded from the public Petstore API for demonstration.

---

## Sample Prompts for TestWeb

The Blazor test website at [src/TestWeb/Components/Pages/Chat.razor](src/TestWeb/Components/Pages/Chat.razor#L12) includes sample prompts:

| Category | Example Prompt | Expected Workflow |
|----------|----------------|--------------------|
| Discovery | "what tools are available?" | Call `search_tools` to list all tools |
| Search | "show me tools for brewery filtering by city" | Call `search` with keyword "brewery city" |
| Schema (Concise) | "show concise parameter schema for getSingleBrewery and listBreweries" | Call `get_schema` with `detail="Detailed"` |
| Schema (Full) | "show full json schema for brewery_random" | Call `get_schema` with `detail="Full"` |
| Single Call | "get random brewery" | Call `brewery_random` directly |
| Multi-step | "find breweries in San Diego and return only their names and types" | Call `listBreweries` with `by_city="San Diego"`, format results |
| Aggregation | "how many breweries are there across all pages" | Call `listBreweries` multiple times, accumulate results |
| Filtering | "find breweries with 'moon' in the name, return top 5 as name and city" | Call `listBreweries` with `by_name="moon"`, limit results |
| Error Handling | "try calling getSingleBrewery with an invalid ID and explain the error" | Call `getSingleBrewery` with invalid ID, handle error gracefully |
| Summary | "summarize all successful brewery queries and list the tools used with token counts" | Generate summary of session (now tracks cumulative tokens across all attempts) |

### Token Accounting

Starting in the latest build, `ChatTurnMetrics` now accumulates tokens across all retry attempts when a session becomes stale:

- **Prompt Tokens**: Sum of tokens from each attempt at a prompt (e.g., 2 attempts × 7 tokens = 14 tokens)
- **Completion Tokens**: Sum of all completion tokens returned (across all attempts)
- **Total Tokens**: Prompt tokens + Completion tokens
- **Elapsed Milliseconds**: Total wall-clock time from first attempt through final retry

**Log Example:**
```
[warn] Session stale after 1 attempt(s), tokens so far: prompt=7, completion=0
[warn] Chat turn recovered. Total attempts: 2, total tokens: prompt=14, completion=79, total=93
```



## Run Aspire AppHost Locally

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

AppHost starts:
- `mcp-server`
- `test-web`
- `opensandbox-server` container (`opensandbox/server:v0.1.8`)

The OpenSandbox container is configured for Docker runtime mode via a mounted config file ([`deploy/opensandbox.config.toml`](deploy/opensandbox.config.toml)). This ensures the server uses Docker execution instead of Kubernetes, which requires no kube-config setup for local development.

If Docker/OrbStack is unavailable, AppHost cannot start the OpenSandbox resource.

## Docker Runbook

1. Build image:

```bash
docker build -t mcp-experiments:local .
```

2. Run container:

```bash
docker run --rm -it mcp-experiments:local
```

3. Build all images:

```bash
docker compose down && docker compose up --build
```

4. Verify:
- Container starts without crash.
- Console output shows tool-list/search/schema/execute smoke values.

## AKS Runbook

1. Build and push image:

```bash
docker build -t YOUR_REGISTRY/mcp-experiments:latest .
docker push YOUR_REGISTRY/mcp-experiments:latest
```

2. Set image in [deploy/aks/deployment.yaml](deploy/aks/deployment.yaml).

3. Apply manifests:

```bash
kubectl apply -f deploy/aks/namespace.yaml
kubectl apply -f deploy/aks/deployment.yaml
kubectl apply -f deploy/aks/service.yaml
```

4. Verify deployment:

```bash
kubectl -n mcp-experiments get pods
kubectl -n mcp-experiments get svc
kubectl -n mcp-experiments logs deploy/mcp-server
```

## Troubleshooting

### Petstore (or other remote OpenAPI specs) not loading
- MCP server now logs bootstrap information about which sources load successfully:
  ```
  [Bootstrap] Loading 2 OpenAPI source(s): 'brewery', 'petstore'
  [Bootstrap] Total tools registered: 38 (status + OpenAPI-generated)
  [Bootstrap]   - brewery: 10 tool(s)
  [Bootstrap]   - pet: 11 tool(s)
  ```
- **If a remote source fails** (times out, unreachable), it is skipped with a warning; local sources continue to load normally
- Remote specs have a **10-second timeout**. If petstore.swagger.io is slow, it will be skipped
- To force petstore loading: configure a faster mirror or host the spec locally and use `Path` instead of `Url` in `OpenApi:Sources`
- To disable petstore: remove it from [appsettings.json](src/McpServer/appsettings.json) `OpenApi:Sources` array

 Build errors: run `dotnet restore` and re-run build.
- Test failures: run filtered tests to isolate (`dotnet test --filter ...`).
- Container exits quickly: run without detach and inspect logs.
- AKS image pull error: verify registry auth and image tag.
- Aspire shows `Cannot connect to the Docker daemon ... .orbstack/run/docker.sock`:
	- Start Docker Desktop or OrbStack.
	- Verify daemon access: `docker info`.
	- Check current context: `docker context ls`.
	- If OrbStack context is selected but OrbStack is stopped, switch context (`docker context use default`) or start OrbStack.
- Aspire/OpenSandbox pull error for `ghcr.io/alibaba/opensandbox/server:*: denied`:
	- Use Docker Hub image `opensandbox/server:v0.1.8` (already configured in AppHost).
