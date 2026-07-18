# Export Feature Implementation Notes

## Scope
- Requested note file kept during implementation run.
- Project request is loop-runtime agent app; no dedicated export feature in plan/requirements.
- This file used as ongoing implementation/deviation log as requested.

## Deviation
- Conservative choice: keep this file as running implementation log.
- No standalone export feature implemented unless user provides export requirements.
- Initial solution command drift: used `dotnet slnx` during early setup; corrected to `dotnet sln LoopRuntime.slnx ...`.
- Aspire SDK mismatch in local env: switched AppHost project to package-based setup (`Aspire.Hosting.AppHost`) to keep local compile path stable.
- Full auth exchange (Okta/Auth0/Entra) not hardwired with tenant values because no secrets/tenant metadata in repo; gateway scaffold provided.

## Progress
- [x] Created note file.
- [x] Recreated missing entrypoint files:
	- `src/LoopRuntime.Executor/Program.cs`
	- `src/LoopRuntime.Checker/Program.cs`
	- `src/LoopRuntime.AppHost/Program.cs`
- [x] Repaired and repopulated `LoopRuntime.slnx` with all projects.
- [x] Fixed project-reference graph (removed cycle risk, added needed references).
- [x] Added deterministic tests:
	- `tests/LoopRuntime.Tests/LoopOrchestratorTests.cs`
	- `tests/LoopRuntime.Tests/CheckerAgentTests.cs`
- [x] Added eval baseline runner:
	- `tests/LoopRuntime.Evals/Program.cs`
- [x] Added AppHost gateway container + `gateway.yaml` scaffold.
- [x] Plan completion status updated in `plan.md`.

## Phase 2 (M3-M7) Entra / AgentGateway auth — latest status
- [x] M3-M5 app-side JWT auth + tests (18/18 passing).
- [x] M1 AppHost secrets wiring + stable gateway port `3032` (`isProxied: false`).
- [x] M2 gateway.yaml `jwtAuth` issuer/audience corrected to match actual Entra v2 tokens (`iss=https://login.microsoftonline.com/<tenant>/v2.0`, `aud=<raw appId GUID>`).
- [x] M6 SPA MSAL.js redirect-based flow implemented in `src/LoopRuntime.Executor/wwwroot/index.html`; browser login + `/run` through gateway returns 200.
- [x] M6 fix: gateway `executor` route changed to `backendAuth.passthrough: {}` so the validated inbound token is forwarded unchanged to Executor (avoids unnecessary token exchange on the entry route).
- [x] Zero-trust verification: direct unauthenticated `curl` to `:5001`, `:5002`, `:5003` returns 401.
- [x] Option 2 selected: keep Entra Agent ID Blueprint apps; do not convert to regular Web/API apps.
- [x] Gateway downstream routes changed to validate downstream target audience and `backendAuth.passthrough: {}`; app code now acquires downstream user-context bearer tokens before calling gateway.
- [x] `RemoteMcpTools` and `A2ACheckerClient` use `IAuthorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(...)` with configured `AzureAd:ClientId` Blueprint app IDs and `SignedAssertionFilePath` credentials.
- [x] Removed `Microsoft.Identity.Web.AgentIdentities` / `DownstreamApi` package usage. Proof: `AddAgentIdentities()`/OIDC-FIC helper registered singleton `ICustomSignedAssertionProvider` that consumed scoped `ITokenAcquirerFactory`, causing ASP.NET service-provider validation failure.
- [x] Fixed A2A host DI: `CheckerAIAgent` is singleton-safe for route mapping and creates a scope per real review request.
- [x] Validation: `dotnet test tests/LoopRuntime.Tests/LoopRuntime.Tests.csproj --verbosity:minimal` passes 18/18; `dotnet build LoopRuntime.slnx --verbosity:minimal` succeeds 0 errors.
- [~] M7 live end-to-end smoke is blocked by missing real FIC inputs: `Parameters:federated-token-file` is absent from AppHost user-secrets, and `setup-entra-obo-chain.ps1` still needs real `-FederatedIssuer` / `-FederatedSubject` matching that assertion source.

## Deviation / lessons learned
- Entra v2 access-token `aud` for scope `api://<guid>/access_as_user` is the raw `<guid>`, not `api://<guid>`.
- Aspire container endpoints default to random host ports; use `isProxied: false` to pin a localhost port.
- Gateway `jwtAuth` strips/consumes the inbound `Authorization` header; to forward the original bearer to a backend, add `backendAuth.passthrough: {}` on that backend.
- Agentic app registrations in Entra do not support gateway `urn:ietf:params:oauth:grant-type:jwt-bearer` OBO (`AADSTS82002`). Current fix keeps AgenticApp Blueprints and moves downstream token acquisition into app code using configured `SignedAssertionFilePath` credentials.
- Do not call `AddAgentIdentities()` in this ASP.NET host: the current package path fails DI validation because its OIDC-FIC custom signed assertion provider is singleton while depending on scoped token-acquisition services.
