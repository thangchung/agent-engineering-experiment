# Local FIC issuer

Dev-only OIDC issuer for Entra federated identity credential experiments. It generates a local RSA key, serves OIDC discovery + JWKS, and writes a short-lived signed assertion JWT for `SignedAssertionFilePath`.

## Run

```bash
node tools/local-fic-issuer/local-fic-issuer.mjs
```

Expose it with a public HTTPS tunnel:

```bash
ngrok http 8787
```

Mint an assertion through the public URL shown by ngrok:

```bash
curl -fsS https://<ngrok-host>/mint
```

The response includes:

- `issuer`: pass to `setup-entra-obo-chain.ps1 -FederatedIssuer`
- `subject`: pass to `setup-entra-obo-chain.ps1 -FederatedSubject`
- `assertionPath`: set as AppHost `Parameters:federated-token-file`

## Wire LoopRuntime

```bash
dotnet user-secrets set "Parameters:federated-token-file" \
  "/Users/chungt02/source_codes/temp/loop-runtime/tools/local-fic-issuer/.state/assertion.jwt" \
  --project src/LoopRuntime.AppHost

pwsh tools/local-fic-issuer/add-local-fic-to-existing-blueprints.ps1 \
  -FederatedIssuer "https://<ngrok-host>" \
  -FederatedSubject "loop-runtime-local" \
  -FederatedAudience "api://AzureADTokenExchange"
```

Keep the issuer process and ngrok running while testing. Re-run `/mint` before the JWT expires.

Do not rerun `setup-entra-obo-chain.ps1` just to add FIC to this existing setup; it creates a new app chain. Use `add-local-fic-to-existing-blueprints.ps1` for the current Blueprint app IDs.