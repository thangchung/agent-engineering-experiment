---
name: entra-a2a-mcp-obo
description: >
  Best practices, tips, and gotchas for Entra ID, Entra Agent ID, A2A protocol,
  MCP protocol, and agentgateway-based OBO token exchange — distilled from
  loop-runtime's entra_agent_id*.md docs. Use when setting up, wiring, or
  debugging Entra app registrations, Entra Agent ID blueprints, OBO chains,
  A2A/MCP auth, app-code Agent Identity OBO, Microsoft.Identity.Web.AgentIdentities,
  fmi_path, or agentgateway jwtAuth/oauthTokenExchange config. ALWAYS remind
  the user in chat (don't silently assume) when a real value is needed for:
  tenantId, webAppId, execAppId, execAppSecret, checkAppId, checkAppSecret,
  mcpAppId, executorAgentIdentityAppId, checkerAgentIdentityAppId.
license: MIT
---

# Entra + A2A + MCP + Agentgateway OBO — Best Practices

Distilled from real setup runs and source-code verification (agentgateway v1.4.0-alpha.1,
MCP C# SDK, A2A .NET SDK). Prove, don't guess — every rule below traces to a verified source
or a real error hit during setup.

## 0. Reminder rule — ALWAYS surface real inputs

These are per-tenant/per-app real values. Never invent, never leave silently blank.
Whenever a task needs one, **say so explicitly in chat** before/while writing config, e.g.
`👉 INPUT needed: <execAppId> — get from setup script output "ExecutorAgent Blueprint appId=..."`.

| Placeholder | What | Typical source |
|---|---|---|
| `tenantId` | Entra tenant GUID | `az account show --query tenantId` / `Connect-MgGraph` output |
| `webAppId` | Web SPA app registration `appId` | setup script output `Web appId=...` |
| `execAppId` | ExecutorAgent Blueprint `appId` | setup script output `ExecutorAgent Blueprint appId=...` |
| `execAppSecret` | ExecutorAgent Blueprint client secret | `az ad app credential reset --id <execAppId>` |
| `checkAppId` | CheckerAgent Blueprint `appId` | setup script output `CheckerAgent Blueprint appId=...` |
| `checkAppSecret` | CheckerAgent Blueprint client secret | `az ad app credential reset --id <checkAppId>` |
| `mcpAppId` | McpServer app registration `appId` | setup script output `McpServer appId=...` |
| `executorAgentIdentityAppId` | Executor Agent Identity instance appId | setup script output `ExecutorAgent instance agentIdentityAppId=...` |
| `checkerAgentIdentityAppId` | Checker Agent Identity instance appId | setup script output `CheckerAgent instance agentIdentityAppId=...` |

Never guess these. Never fill code/config with them silently — flag with `👉 INPUT` inline
and call it out in the chat response too.

## 1. Entra ID app registrations (entra-app-registration)

- Web/SPA apps: use `redirectUri = window.location.origin`, never hardcode a port unless it's fixed.
- Service/API apps (Mcp, Checker, Executor when acting as resource): expose an API scope
  named `access_as_user`, take note of its scope GUID — needed for OBO permission grants.
- Always record `appId` (client ID) — everything downstream (jwtAuth audiences, MSAL config,
  appsettings `AzureAd:ClientId`) keys off it. Tenant ID is shared across all apps.
- Secrets: never print/store in scripts or docs. Generate via `az ad app credential reset`,
  pipe straight into `dotnet user-secrets set` or Key Vault. Rotate on expiry.
- `preAuthorizedApplications` PATCH payloads on `api.oauth2PermissionScopes` — Graph API is
  picky: `permissionIds` (not `scopeIds`), array of GUIDs, wrapped correctly. A 400
  `"An unexpected 'StartArray' node..."` almost always means a naked array where an object with
  a `value` key on it or a wrapper property was expected — check Graph API docs for the exact schema, don't guess field names.

## 2. Entra Agent ID (entra-agent-id) — Blueprint pattern

- "Agent Identity" = Blueprint application + BlueprintPrincipal (service principal) + Agent
  Identity service principal. All three must exist before wiring OBO.
- Blueprint appId and Agent Identity instance appId are different. For downstream app-code
  OBO, `AzureAd:ClientId` stays the Blueprint appId, but `.WithAgentIdentity(...)` must use
  the Agent Identity instance appId.
- Common early failure: `400 Agent Blueprint Principal does not exist` → BlueprintPrincipal
  creation step was skipped or failed silently. Rerun that specific Graph call before creating
  the Agent Identity — don't rerun the whole script (not idempotent, creates duplicate apps).
- Scripts that provision these objects are NOT idempotent. Always have a paired cleanup script
  ready before running setup — if a step fails mid-way, clean up fully, then rerun from scratch.
- 403 on any Graph call → check signed-in role/consent, and that `Connect-MgGraph`/token scopes
  match what the script needs (`Application.ReadWrite.All`, etc.), not just "logged in".

## 3. Chained/multi-hop OBO

- Chain: Web → ExecutorAgent → {CheckerAgent, McpServer}; CheckerAgent → McpServer too.
- Each hop needs its OWN delegated permission grant / `preAuthorizedApplications` entry — a
  grant from Web→Exec does NOT implicitly cover Exec→Checker. Wire every arrow in the chain.
- For AgenticApp Blueprint clients, do NOT rely on agentgateway `oauthTokenExchange` for the
  downstream OBO hop. Live proof: Entra rejects AgenticApp clients for gateway-side grants with
  `AADSTS82002` (`jwt-bearer` OBO) and `AADSTS82001` (`client_credentials`). Correct path is
  app code using Microsoft.Identity.Web AgentIdentities and `fmi_path`.
- With agentgateway doing token exchange (RFC 7523 jwt-bearer, not RFC 8693 token-exchange):
  only use this for compatible non-Agentic clients. Config shape is `grantType: jwtBearer`,
  assertion sent as `assertion` param (not `subject_token`), `clientAuth.method: clientSecretPost`
  (Entra wants creds in body), and `additionalParams.requested_token_use: '"on_behalf_of"'` —
  note the inner quotes, it's a CEL string literal. `actorToken`/`resources`/`requestedTokenType`
  are RFC-8693-only — Entra's jwtBearer grant rejects them.
- Verified in agentgateway source (v1.4.0-alpha.1, commit `13d6cc332a5bdd7b48e488157ffff01d85877934`):
  `insert_exchanged_token` explicitly `remove()`s the original `Authorization` header before
  inserting the exchanged token — original credential is never forwarded alongside the new one.

## 3A. AgenticApp Blueprint OBO — app-code path that worked

- Use `Microsoft.Identity.Web.AgentIdentities` with `Microsoft.Identity.Web` in the Executor and
  Checker apps. Register `AddMicrosoftIdentityWebApi(...).EnableTokenAcquisitionToCallDownstreamApi().AddInMemoryTokenCaches()`.
- Also call `builder.Services.AddAgentIdentities()`.
- Acquire downstream tokens with:
  ```csharp
  await authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
      scopes,
      new AuthorizationHeaderProviderOptions().WithAgentIdentity(agentIdentityId),
      principal,
      cancellationToken);
  ```
- `WithAgentIdentity(agentIdentityId)` selects the Agent Identity by writing
  `AcquireTokenOptions.ExtraParameters["fmiPathForClientAssertion"]`. Do not assert or inspect
  `AcquireTokenOptions.FmiPath`; it stays empty in current packages.
- In Aspire/AppHost env, pass:
  - `AzureAd__ClientId=<Blueprint appId>`
  - `AgentIdentity__AgentIdentityId=<Agent Identity instance appId>`
  - `AzureAd__ClientCredentials__0__SourceType=SignedAssertionFilePath`
  - `AzureAd__ClientCredentials__0__SignedAssertionFileDiskPath=<federatedTokenFile>`
  - `AZURE_FEDERATED_TOKEN_FILE=<federatedTokenFile>`
- If `AddAgentIdentities()` causes ASP.NET DI validation failures around
  `Microsoft.Identity.Web.OidcFic.OidcIdpSignedAssertionLoader`, `ICustomSignedAssertionProvider`,
  `Microsoft.Identity.Web.DefaultCertificateLoader`, or `ICredentialsLoader`, check service
  lifetimes. In loop-runtime, scoped re-registration of the OIDC-FIC signed assertion provider
  and certificate loader fixed singleton-consuming-scoped validation failures.
- Regression tests should fake `IAuthorizationHeaderProvider` and assert
  `LastOptions.AcquireTokenOptions.ExtraParameters["fmiPathForClientAssertion"] == <agent identity instance appId>`
  for both MCP and A2A callers.

## 3B. Consent/preauthorization rules for Agent Identity instances

- Downstream API `preAuthorizedApplications` must include Agent Identity instance appIds, not only
  Blueprint appIds. Missing this produced live `AADSTS65001 consent_required` for
  `ExecutorAgent-instance-1` after app-code Agent Identity OBO was active.
- Wire both layers when repairing an existing tenant:
  - API app `preAuthorizedApplications` includes caller appIds with `permissionIds` for `access_as_user`.
  - `oauth2PermissionGrants` exists from caller service principal objectId to resource service
    principal objectId for the same scope.
- Required chain in this repo:
  - Web app → Executor Blueprint API.
  - Executor Blueprint and Executor Agent Identity instance → Checker API.
  - Executor Blueprint, Checker Blueprint, Executor Agent Identity instance, and Checker Agent
    Identity instance → MCP API.
- Do not rerun non-idempotent setup just to fix consent. Use a focused repair script that resolves
  apps/service principals by appId and patches only preauth/grants.

## 3C. FIC assertion facts

- Entra does not mint the FIC assertion. An external OIDC issuer signs a JWT with:
  - `iss=<issuer URL>`
  - `sub=<configured subject>`
  - `aud=api://AzureADTokenExchange`
- Local dev can use a tiny issuer + ngrok, write assertion to disk, then point
  `SignedAssertionFileDiskPath` / `AZURE_FEDERATED_TOKEN_FILE` at that file.
- FIC must exist on the Blueprint app registration for the issuer/subject/audience tuple. Wrong
  issuer, stale ngrok URL, wrong subject, or missing FIC means token acquisition fails before app
  can call downstream APIs.

## 4. MCP protocol auth

- Server-to-server MCP (no interactive client like VS Code/Claude Desktop): plain `jwtAuth` at
  the gateway + `AddMicrosoftIdentityWebApi` + `.RequireAuthorization()` on `MapMcp()` is enough.
  Skip `mcpAuthentication`/PRM discovery/401 dance — that's for interactive MCP clients only.
- Reference sample (`ProtectedMcpServer/Program.cs` in modelcontextprotocol/csharp-sdk) validates
  `aud = serverUrl` (RFC 8707 resource-indicator, literal URL). Entra's own convention is
  `aud = api://<appId>` — a deliberate, acceptable deviation, not a bug, when using
  `AddMicrosoftIdentityWebApi`.
- If multiple callers hit the same MCP backend with different OBO client identities (e.g.
  Executor and Checker both calling Mcp), split gateway routes per caller
  (`/mcp-from-exec`, `/mcp-from-checker`) — each needs its own `jwtAuth` audience and its own
  `oauthTokenExchange.clientAuth` (own Blueprint credentials). Downstream app code needs a
  configurable path suffix (don't hardcode `/mcp`).

## 5. A2A protocol auth

- A2A .NET SDK enforces ZERO auth itself (confirmed in `a2a-dotnet/docs/security.md`, pinned
  commit `8fe65cfaa65a72b2d63bc9bef2e2d32fddc12a18`) — the host app must protect everything.
- Protect BOTH `MapA2AJsonRpc(...)` AND `MapWellKnownAgentCard(...)` (`/.well-known/agent-card.json`)
  with `.RequireAuthorization()`. Missing the agent-card route is a real, easy-to-miss gap —
  the spec explicitly requires it protected too.

## 6. Agentgateway config

- Check what gateway.yaml already fronts before designing new auth — in this repo agentgateway
  already routes a2a/mcp/executor traffic; adding `jwtAuth`+`backendAuth.oauthTokenExchange` is
  additive config, not a new proxy layer.
- CORS: gateway `cors.allowOrigins` needed if SPA calls the gateway cross-origin (e.g. SPA
  served from app's own static host, `/run` called via absolute gateway URL).
- Narrow route matches (`pathPrefix`/`exact` + explicit `method`) over catch-alls once auth
  policies are added — a catch-all `pathPrefix: /` with `jwtAuth` will also 401-block static
  file serving on the same host if not scoped carefully.
- Don't guess agentgateway internals — verify against the pinned source tag/commit before
  documenting a claim as fact. Blog posts are a starting point, not proof.

## 7. Secrets plumbing (Aspire)

- Flow: `dotnet user-secrets set "Parameters:x-client-secret" "..."` → Aspire
  `builder.AddParameter("x-client-secret", secret: true)` → `.WithEnvironment("X_CLIENT_SECRET", ...)`
  baked into the container's env → `gateway.yaml`'s `${X_CLIENT_SECRET}` interpolates it.
  The container never touches `dotnet user-secrets` directly.

## 8. General "prove don't guess" discipline

- For any claim about a library/gateway/SDK behavior, cite the exact file/line or doc section
  verified, at a pinned commit/tag. If unverified, label it a guess/assumption, not a fact.
- Milestone/plan docs: copy verbatim config/diagrams from the source design doc instead of
  regenerating — avoids drift between "design" and "plan" documents.
- Live browser + Aspire logs beat theory. If UI returns 500/401, capture browser result, then use
  filtered `aspire logs <resource>` for `AADSTS|consent|invalid_grant|Msal|CreateAuthorization`.
- Error map from loop-runtime:
  - `AADSTS82002`: AgenticApp client rejected for gateway `jwt-bearer` OBO. Move OBO to app code
    with AgentIdentities/fmi_path.
  - `AADSTS82001`: AgenticApp client rejected for app-only/client-credentials token. Do not use
    client credentials as fallback for AgenticApp downstream calls.
  - `AADSTS65001 consent_required`: app code path likely active, but caller appId/SP lacks consent
    or API preauthorization. Add Agent Identity instance appIds to preauth/grants.
  - `IDW10502`: Microsoft.Identity.Web wrapped downstream token failure. Inspect inner MSAL/Entra
    error; do not stop at IDW code.
