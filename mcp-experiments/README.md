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

- Build errors: run `dotnet restore` and re-run build.
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
