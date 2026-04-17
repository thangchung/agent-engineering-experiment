# Azure Foundry Deployment Options

This repository supports Azure AI Foundry through `DefaultAzureCredential` in [DotNetClaw/Program.cs](DotNetClaw/Program.cs). The Foundry provider is created with:

```csharp
var aiProjectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
```

That means container authentication is controlled entirely by environment and platform identity configuration.

This document explains three deployment options:

1. Option A: local Docker or CI using a service principal
2. Option B: local Docker using Azure CLI credentials
3. Option C: Azure-hosted deployment using managed identity

## Before you start

You need these values regardless of auth option:

```text
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
FOUNDRY_MODEL=<your-foundry-model-deployment-name>
```

Important notes:

- Use the Foundry project endpoint, not an Azure OpenAI `/openai/v1/` endpoint.
- The endpoint format should be `https://<resource-name>.services.ai.azure.com/api/projects/<project-name>`.
- `FOUNDRY_MODEL` must match the exact deployment name in your Foundry project.
- The calling identity must have access to the Foundry project. If auth succeeds but model calls fail, verify RBAC on the Foundry project or backing AI resource.

You can verify the endpoint and model from the Foundry portal or by checking your deployment configuration.

## Current docker-compose wiring

The current [docker-compose.yaml](docker-compose.yaml) already passes the Foundry settings and Azure identity variables into the `dotnetclaw` container:

```yaml
environment:
  Agent__Provider: ${AGENT_PROVIDER:-copilot}
  Foundry__Endpoint: ${FOUNDRY_ENDPOINT:-}
  Foundry__Model: ${FOUNDRY_MODEL:-gpt-5.4-mini}
  AZURE_CLIENT_ID: ${AZURE_CLIENT_ID:-}
  AZURE_TENANT_ID: ${AZURE_TENANT_ID:-}
  AZURE_CLIENT_SECRET: ${AZURE_CLIENT_SECRET:-}
```

So Option A works immediately. Option B and Option C depend on how the runtime environment provides credentials.

## Option A: Service principal in Docker

Use this for:

- local `docker compose`
- CI/CD pipelines
- environments where managed identity is not available

This is the simplest and most reliable local setup.

### Step 1: Create or choose an app registration / service principal

If you already have one, collect:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_CLIENT_SECRET`

If you need to create one:

```bash
az ad sp create-for-rbac --name dotnetclaw-foundry-sp
```

That command returns values similar to:

```json
{
  "appId": "<client-id>",
  "displayName": "dotnetclaw-foundry-sp",
  "password": "<client-secret>",
  "tenant": "<tenant-id>"
}
```

Map them like this:

- `appId` -> `AZURE_CLIENT_ID`
- `password` -> `AZURE_CLIENT_SECRET`
- `tenant` -> `AZURE_TENANT_ID`

### Step 2: Grant access to Azure AI Foundry

Assign the service principal the role needed to use the Foundry project. At minimum, make sure it can call the Foundry project and underlying model deployment.

If requests fail after auth, verify role assignment in the Azure portal on:

- the Foundry project
- the AI Services / Azure OpenAI resource backing that project

### Step 3: Create a local `.env`

For `docker compose`, create a `.env` file in the repo root:

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
FOUNDRY_MODEL=<deployment-name>
AZURE_CLIENT_ID=<service-principal-client-id>
AZURE_TENANT_ID=<tenant-id>
AZURE_CLIENT_SECRET=<service-principal-secret>
SLACK_BOT_TOKEN=<optional>
SLACK_APP_TOKEN=<optional>
SLACK_BOT_USER_ID=<optional>
```

### Step 4: Run the stack

```bash
docker compose up --build
```

### Why this works

`DefaultAzureCredential` checks environment-based credentials first. When `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_CLIENT_SECRET` are present, the SDK authenticates as that service principal.

### Verification

Look for startup logs showing:

```text
[Agent] Provider: foundry
[Agent] Foundry mode: model=<model> endpoint=<endpoint>
```

If the endpoint is wrong, you typically see `404` or `Resource not found`.

## Option B: Azure CLI credentials in local Docker

Use this for:

- local development when you do not want to create a service principal
- short-lived developer workflows

This option needs one correction: mounting `~/.azure` by itself is not enough.

`AzureCliCredential` requires the `az` executable to exist inside the container because it obtains a token by invoking Azure CLI.

The current runtime image in [DotNetClaw/Dockerfile](DotNetClaw/Dockerfile) is based on `mcr.microsoft.com/dotnet/aspnet:10.0` and does not install Azure CLI.

### Step 1: Log in on the host

```bash
az login
az account show
```

### Step 2: Make Azure CLI available inside the container

You have two ways to do this.

#### Approach 2A: Build a custom runtime image with Azure CLI installed

Update the runtime stage in [DotNetClaw/Dockerfile](DotNetClaw/Dockerfile) to install Azure CLI, then mount the host Azure config directory.

Example runtime additions:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y curl ca-certificates apt-transport-https lsb-release gnupg \
    && curl -sL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > /etc/apt/trusted.gpg.d/microsoft.gpg \
    && AZ_REPO=$(lsb_release -cs) \
    && echo "deb [arch=amd64] https://packages.microsoft.com/repos/azure-cli/ ${AZ_REPO} main" > /etc/apt/sources.list.d/azure-cli.list \
    && apt-get update \
    && apt-get install -y azure-cli \
    && rm -rf /var/lib/apt/lists/*
```

Then in [docker-compose.yaml](docker-compose.yaml):

```yaml
services:
  dotnetclaw:
    environment:
      Agent__Provider: ${AGENT_PROVIDER:-foundry}
      Foundry__Endpoint: ${FOUNDRY_ENDPOINT:-}
      Foundry__Model: ${FOUNDRY_MODEL:-gpt-5.4-mini}
    volumes:
      - ${HOME}/.azure:/root/.azure:ro
```

#### Approach 2B: Use a developer-only image that already contains Azure CLI

If you have a standard internal base image with `az` preinstalled, use that as the runtime image instead.

### Step 3: Provide Foundry settings

Use a `.env` file like this:

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
FOUNDRY_MODEL=<deployment-name>
```

Do not set `AZURE_CLIENT_SECRET` for this option.

### Step 4: Run the container

```bash
docker compose up --build
```

### Why this works

After environment credentials are skipped, `DefaultAzureCredential` can fall through to `AzureCliCredential`. That credential uses the logged-in Azure CLI identity, but only if `az` is installed and the CLI profile is accessible in the container.

### When not to use this option

- production deployments
- automated pipelines
- shared environments

For those cases, use Option A or Option C.

## Option C: Managed identity in Azure

Use this for:

- AKS
- Azure Container Apps
- App Service
- VMs or other Azure-hosted environments with managed identity

This is the preferred production pattern because there is no secret in config.

### System-assigned vs user-assigned

- System-assigned managed identity: usually no `AZURE_CLIENT_ID` is needed.
- User-assigned managed identity: set `AZURE_CLIENT_ID` so `DefaultAzureCredential` chooses the intended identity.

### Step 1: Enable managed identity on the hosting resource

Examples:

- AKS with workload identity
- Azure Container Apps managed identity
- App Service managed identity

For a user-assigned identity, you create the identity first and attach it to the resource.

### Step 2: Find the user-assigned identity client ID

Azure Portal:

- Open the managed identity resource.
- Copy `Client ID`.

Azure CLI:

```bash
az identity show \
  --name <identity-name> \
  --resource-group <resource-group> \
  --query clientId -o tsv
```

That value is what you place in `AZURE_CLIENT_ID`.

### Step 3: Grant the identity access to the Foundry project

Assign the managed identity the required role on the Foundry project or backing AI resource.

If auth works but model calls fail, check RBAC first.

### Step 4: Configure environment for the app

For system-assigned identity:

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
FOUNDRY_MODEL=<deployment-name>
```

For user-assigned identity:

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
FOUNDRY_MODEL=<deployment-name>
AZURE_CLIENT_ID=<user-assigned-managed-identity-client-id>
```

Do not set `AZURE_CLIENT_SECRET` for managed identity.

### Step 5: Azure-specific notes

#### AKS with workload identity

In AKS, workload identity typically injects:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_FEDERATED_TOKEN_FILE`

Your pod or service account configuration handles that. The application code does not change.

#### Azure Container Apps

Enable the managed identity on the container app and assign the Foundry permissions to that identity. If using a user-assigned identity, set `AZURE_CLIENT_ID` in the container app environment settings.

#### App Service

Enable managed identity on the app and set `AZURE_CLIENT_ID` only when using a user-assigned identity.

### Why this works

`DefaultAzureCredential` detects the platform-provided identity endpoint in Azure-hosted environments and authenticates with managed identity without any secret.

## Recommended usage by environment

| Environment | Recommended option |
|---|---|
| Local `docker compose` | Option A |
| Local developer container with Azure CLI installed | Option B |
| CI/CD | Option A |
| AKS / Container Apps / App Service in Azure | Option C |

## Troubleshooting

### `Resource not found`

Usually one of these:

- the endpoint is an Azure OpenAI endpoint instead of a Foundry project endpoint
- the project name in the endpoint is wrong
- `FOUNDRY_MODEL` does not match an existing deployment name

Correct format:

```text
https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
```

Wrong format for this app:

```text
https://<resource-name>.openai.azure.com/openai/v1/
```

### `DefaultAzureCredential failed to retrieve a token`

Check the active auth mode:

- Option A: verify `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`
- Option B: verify `az` is installed in the container and `${HOME}/.azure` is mounted
- Option C: verify managed identity is enabled and has the right role assignments

### Auth succeeds but model calls fail

Check:

- Foundry project RBAC
- model deployment name
- project endpoint

## Minimal examples

### Option A `.env`

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://my-foundry.services.ai.azure.com/api/projects/my-project
FOUNDRY_MODEL=gpt-5.4-mini
AZURE_CLIENT_ID=11111111-1111-1111-1111-111111111111
AZURE_TENANT_ID=22222222-2222-2222-2222-222222222222
AZURE_CLIENT_SECRET=<secret>
```

### Option B `.env`

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://my-foundry.services.ai.azure.com/api/projects/my-project
FOUNDRY_MODEL=gpt-5.4-mini
```

### Option C user-assigned identity env

```bash
AGENT_PROVIDER=foundry
FOUNDRY_ENDPOINT=https://my-foundry.services.ai.azure.com/api/projects/my-project
FOUNDRY_MODEL=gpt-5.4-mini
AZURE_CLIENT_ID=<managed-identity-client-id>
```