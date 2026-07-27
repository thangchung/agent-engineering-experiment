# Getting a user token to call TodoApi

Per prd §7 R-5. You need a **delegated user token** with audience `<TodoApi_ClientId>` and
scope `access_as_user` (bare client-ID audience, not `api://`-prefixed — see the note below).

## Option A — Azure CLI (fastest)

Pre-req: TodoApi's app registration must authorize the Azure CLI client:
`TodoApi > Expose an API > Authorized client applications` → add
`04b07795-8ddb-461a-bbee-02f9e1bf7b46` (Azure CLI) for the `access_as_user` scope.

```bash
az login --tenant <TENANT_ID>
az account get-access-token \
  --scope "api://<TodoApi_ClientId>/access_as_user" \
  --query accessToken -o tsv
```

Use the result as `Authorization: Bearer <token>` against either:
- `http://localhost:5001/todos` (TodoApi directly — this project's actual entry point, per
  the human-calls-TodoApi-directly decision; see prd §2), or
- the gateway's public port (`http://localhost:<gateway-port>/todos` — find the current
  port with `aspire describe --format Json`, since it's dynamically assigned).

## Option B — Device code (no CLI pre-authorization; needs a public-client app registration)

Register a public client (e.g. "TodoTestClient", "Allow public client flows" = yes),
grant it delegated permission `api://<TodoApi_ClientId>/access_as_user` + admin consent.

```bash
curl -s -X POST "https://login.microsoftonline.com/<TENANT_ID>/oauth2/v2.0/devicecode" \
  -d "client_id=<TodoTestClient_ClientId>" \
  -d "scope=api://<TodoApi_ClientId>/access_as_user offline_access"
# open verification_uri, enter user_code, then poll the token endpoint:
curl -s -X POST "https://login.microsoftonline.com/<TENANT_ID>/oauth2/v2.0/token" \
  -d "grant_type=urn:ietf:params:oauth:grant-type:device_code" \
  -d "client_id=<TodoTestClient_ClientId>" \
  -d "device_code=<device_code_from_above>"
# -> use .access_token as the Bearer
```

## Verifying the token

Decode it at [jwt.ms](https://jwt.ms) and confirm:

| Claim | Expected value |
|-------|----------------|
| `aud` | `<TodoApi_ClientId>` — **bare client-ID GUID**, never `api://`-prefixed. Real Entra v2.0 tokens always carry the bare form regardless of the `api://.../scope` string used to request them (confirmed live, 2026-07-23 — see plan TASK-044a). |
| `scp` | contains `access_as_user` |
| `ver` | `2.0` |

Wrong `aud`/`scp` → hop-1 OBO fails (`agentgateway`'s `jwtAuth` does a literal-string match
against `audiences:`, unlike `Microsoft.Identity.Web`'s in-process validation which is
lenient about the `api://` prefix).

## Scripted alternative

`tests/AgenticTodo.Tests/Integration/AspireMeshIntegrationTests.cs` mints a token the same
way (Option A, shelled out to `az`) as part of its live proof of the full OBO chain — read
it for a working, runnable reference.
