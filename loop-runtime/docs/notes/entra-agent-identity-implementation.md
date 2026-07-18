# Entra Agent Identity Implementation Notes

## 2026-07-18 live auth proof

- Browser `/run` through AgentGateway reaches Executor after current-window MSAL redirect login.
- Previous downstream failure `AADSTS82002` is gone after switching app code to `Microsoft.Identity.Web.AgentIdentities` and `.WithAgentIdentity(...)`.
- `WithAgentIdentity(agentIdentityId)` writes the Agent Identity selection into `AcquireTokenOptions.ExtraParameters["fmiPathForClientAssertion"]`; it does not populate `AcquireTokenOptions.FmiPath`.
- Live `/run` now fails at downstream token acquisition with `AADSTS65001 consent_required` for `ExecutorAgent-instance-1` appId `6e1590ce-042e-49e8-bfd1-75104fa813a7`.
- Meaning: app code is now using Agent Identity, but tenant grants/pre-authorization must include Agent Identity instance appIds as callers, not only Blueprint appIds.

## Repo changes

- `RemoteMcpTools` and `A2ACheckerClient` both call `CreateAuthorizationHeaderForUserAsync(..., new AuthorizationHeaderProviderOptions().WithAgentIdentity(...), ...)`.
- Executor and Checker register `AddAgentIdentities()`.
- Executor and Checker re-register `OidcIdpSignedAssertionLoader` and `DefaultCertificateLoader` as scoped to satisfy ASP.NET DI validation for this host.
- `setup-entra-obo-chain.ps1` now records Agent Identity instance appIds and includes those appIds in downstream `preAuthorizedApplications`.
- `tools/repair-agent-identity-consent.ps1` repairs an existing tenant without rerunning destructive setup.

## Validation

- `dotnet test tests/LoopRuntime.Tests/LoopRuntime.Tests.csproj --filter RemoteMcpToolsTests --verbosity:minimal` passes: 3 tests, including MCP and A2A Agent Identity option checks.
- `pwsh` parser checks pass for `setup-entra-obo-chain.ps1` and `tools/repair-agent-identity-consent.ps1`.

## Next live step

Run:

```powershell
pwsh ./tools/repair-agent-identity-consent.ps1
```

Then restart Aspire and rerun browser smoke. Expected next proof: no `AADSTS65001` and no `AADSTS82002`; if the app returns `needs-work`, inspect MCP/tool-call logs next.