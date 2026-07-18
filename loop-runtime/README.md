# Introduction

Loop Runtime = Aspire app proving Entra Agent ID + OBO across SPA -> AgentGateway -> Executor -> Checker/MCP.

## Getting started

### 0. Tools

Need:

- .NET 10 SDK
- Aspire CLI 13.4.x
- Docker Desktop
- Node.js
- ngrok
- PowerShell 7: `pwsh`
- Microsoft Graph PowerShell SDK

Install Graph module once:

```powershell
Install-Module Microsoft.Graph -Scope CurrentUser -Force
```

Check local build first:

```bash
dotnet restore LoopRuntime.slnx
dotnet build LoopRuntime.slnx --verbosity:minimal
dotnet test tests/LoopRuntime.Tests/LoopRuntime.Tests.csproj --verbosity:minimal
```

### 1. Set local secrets

Secrets live in user-secrets or env vars. No keys in code.

```bash
dotnet user-secrets set "Parameters:azure-openai-endpoint" "https://<your-aoai-resource>.openai.azure.com/" --project src/LoopRuntime.AppHost
dotnet user-secrets set "Parameters:azure-openai-api-key" "<your-aoai-api-key>" --project src/LoopRuntime.AppHost
```

If `src/LoopRuntime.AppHost/appsettings*.json` has real key values, remove them or override with user-secrets before sharing. AppHost comments say same thing; obey it.

### 2. Start local FIC issuer

Terminal 1:

```bash
node tools/local-fic-issuer/local-fic-issuer.mjs
```

Terminal 2:

```bash
ngrok http 8787
```

Mint signed assertion through ngrok URL:

```bash
curl -fsS https://<ngrok-host>/mint
```

Keep output. Need these values:

- `issuer` -> public ngrok HTTPS URL
- `subject` -> usually `loop-runtime-local`
- `assertionPath` -> local JWT file path

Set assertion path for AppHost:

```bash
dotnet user-secrets set "Parameters:federated-token-file" "/absolute/path/to/tools/local-fic-issuer/.state/assertion.jwt" --project src/LoopRuntime.AppHost
```

JWT expires. Re-run `/mint` when token old. Keep Node issuer + ngrok running while app runs.

## Daily dev loop

For local Entra Agent Identity dev, keep the FIC issuer and ngrok alive the whole time.

Why: Entra validates `SignedAssertionFilePath` by fetching the issuer metadata and signing keys from the public issuer URL. In local dev, that public issuer URL is the ngrok HTTPS URL in front of `tools/local-fic-issuer/local-fic-issuer.mjs`.

Keep these terminals running:

```bash
node tools/local-fic-issuer/local-fic-issuer.mjs
ngrok http 8787
aspire start --non-interactive
```

If ngrok stops, token exchange can fail because Entra cannot fetch issuer metadata/JWKS. If ngrok restarts with a new URL, the old Entra FIC issuer no longer matches.

When ngrok URL changes:

```bash
curl -fsS https://<new-ngrok-host>/mint
dotnet user-secrets set "Parameters:federated-token-file" "/absolute/path/to/tools/local-fic-issuer/.state/assertion.jwt" --project src/LoopRuntime.AppHost
```

Then update FIC on existing Blueprint apps:

```powershell
pwsh ./tools/local-fic-issuer/add-local-fic-to-existing-blueprints.ps1 -FederatedIssuer "https://<new-ngrok-host>" -FederatedSubject "loop-runtime-local"
```

Then restart Aspire:

```bash
aspire stop --non-interactive
aspire start --non-interactive
```

If token exchange still fails after URL change, run repair too:

```powershell
pwsh ./tools/repair-agent-identity-consent.ps1
```

Stable dev option: use a reserved/static ngrok domain or another stable public HTTPS tunnel. Then FIC issuer does not change every session.

### 3. Create fresh Entra chain

Fresh tenant only. This script is not idempotent. It creates new app registrations / Agent Identity objects.

```powershell
pwsh ./setup-entra-obo-chain.ps1 \
	-FederatedIssuer "https://<ngrok-host>" \
	-FederatedSubject "loop-runtime-local"
```

Graph login needs high scopes. Accept consent if asked.

Script prints app IDs. Save them:

- `Web appId`
- `ExecutorAgent Blueprint appId`
- `ExecutorAgent agentIdentityAppId`
- `CheckerAgent Blueprint appId`
- `CheckerAgent agentIdentityAppId`
- `McpServer appId`
- `tenantId` from Graph/portal/`az account show`

### 4. Update repo IDs

Replace old IDs with new script output.

Files to update:

- `src/LoopRuntime.AppHost/Program.cs`
	- `tenantId`
	- MCP `AzureAd__ClientId`
	- Checker Blueprint `AzureAd__ClientId`
	- Checker `AgentIdentity__AgentIdentityId`
	- Executor Blueprint `AzureAd__ClientId`
	- Executor `AgentIdentity__AgentIdentityId`
	- `Mcp__Scopes__0`
	- `Checker__Scopes__0`
- `src/LoopRuntime.AppHost/gateway.yaml`
	- all issuer tenant IDs
	- all `jwks.url` tenant IDs
	- `audiences` for executor/checker/mcp routes
- `src/LoopRuntime.Executor/wwwroot/index.html`
	- MSAL `clientId` = Web appId
	- MSAL `authority` tenant ID
	- `execScope` = Executor Blueprint scope
- `src/LoopRuntime.Executor/appsettings.json`
	- tenant + Executor Blueprint client ID
- `src/LoopRuntime.Checker/appsettings.json`
	- tenant + Checker Blueprint client ID
- `src/LoopRuntime.Mcp/appsettings.json`
	- tenant + MCP client ID
- `tools/local-fic-issuer/add-local-fic-to-existing-blueprints.ps1`
	- Executor/Checker Blueprint app IDs if you reuse helper later
- `tools/repair-agent-identity-consent.ps1`
	- all app IDs if you need repair later

Do not use Blueprint appId where Agent Identity instance appId belongs. They are different.

### 5. Existing tenant repair path

If setup already ran and only consent/FIC changed, do not rerun full setup.

Update FIC for existing Blueprint apps after ngrok URL changes:

```powershell
pwsh ./tools/local-fic-issuer/add-local-fic-to-existing-blueprints.ps1 \
	-FederatedIssuer "https://<ngrok-host>" \
	-FederatedSubject "loop-runtime-local"
```

Repair Agent Identity caller consent/preauthorization:

```powershell
pwsh ./tools/repair-agent-identity-consent.ps1
```

If using new app IDs, pass parameters instead of editing defaults:

```powershell
pwsh ./tools/repair-agent-identity-consent.ps1 \
	-WebAppId "<webAppId>" \
	-ExecutorBlueprintAppId "<execBlueprintAppId>" \
	-CheckerBlueprintAppId "<checkerBlueprintAppId>" \
	-McpAppId "<mcpAppId>" \
	-ExecutorAgentIdentityAppId "<execAgentIdentityAppId>" \
	-CheckerAgentIdentityAppId "<checkerAgentIdentityAppId>"
```

### 6. Run app with Aspire

Use Aspire. Do not `dotnet run` individual services for normal dev.

```bash
aspire start --non-interactive
aspire wait executor --non-interactive
aspire wait checker --non-interactive
aspire wait mcp --non-interactive
```

Open:

```text
http://localhost:5003/index.html
```

Click `Generate`. First click should redirect current window to Entra login. After return, click/run continues and calls gateway `http://localhost:3032/run`.

### 7. Prove with logs

Good proof commands:

```bash
aspire logs executor --tail 200 | rg 'Run request|AADSTS|consent|invalid_grant|Msal|CreateAuthorization'
aspire logs checker --tail 200 | rg 'AADSTS|consent|invalid_grant|Msal|MCP|verdict'
aspire logs agentgateway --tail 200 | rg 'executor|a2a|mcp|401|500|route.name'
```

Expected good path:

- Browser login redirects in current window.
- `/run` reaches Executor.
- No `AADSTS82002`.
- No `AADSTS65001`.
- Executor calls Checker and MCP through AgentGateway.

Error map:

- `AADSTS82002` -> wrong path: AgenticApp client tried gateway jwt-bearer OBO. Use app-code AgentIdentities path.
- `AADSTS82001` -> wrong fallback: AgenticApp client tried app-only/client-credentials.
- `AADSTS65001 consent_required` -> missing Agent Identity instance preauth/grant. Run repair script.
- `IDW10502` -> wrapper error. Read inner MSAL/AADSTS error.

### 8. Test after changes

Fast auth-focused test:

```bash
dotnet test tests/LoopRuntime.Tests/LoopRuntime.Tests.csproj --filter RemoteMcpToolsTests --verbosity:minimal
```

Full local check:

```bash
dotnet build LoopRuntime.slnx --verbosity:minimal
dotnet test tests/LoopRuntime.Tests/LoopRuntime.Tests.csproj --verbosity:minimal
```

PowerShell syntax check before running tenant scripts:

```bash
pwsh -NoProfile -Command '$tokens=$null; $errors=$null; $null = [System.Management.Automation.Language.Parser]::ParseFile("setup-entra-obo-chain.ps1", [ref]$tokens, [ref]$errors); if ($errors) { $errors | Format-List; exit 1 }'
pwsh -NoProfile -Command '$tokens=$null; $errors=$null; $null = [System.Management.Automation.Language.Parser]::ParseFile("tools/repair-agent-identity-consent.ps1", [ref]$tokens, [ref]$errors); if ($errors) { $errors | Format-List; exit 1 }'
```

### 9. Stop app

```bash
aspire stop --non-interactive
```

Stop ngrok + local FIC issuer too.


## References
- https://github.com/microsoft/GitHub-Copilot-for-Azure/tree/main/plugin/skills
- https://github.com/uninhibited-scholar/loop-runtime