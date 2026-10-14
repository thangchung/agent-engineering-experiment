---
name: coffeeshop-acceptance-tests
description: "**WORKFLOW SKILL** — Run the coffeeshop-jev WebApp UI acceptance tests (WebApp/acceptance, scenarios A1-A8, driven with playwright-cli against a live `aspire run` stack) and, when one fails, collect logs, order traces and screenshots, find the cause, fix it and re-run. WHEN: \"run the acceptance tests\", \"run A1\", \"UI acceptance\", \"check the WebApp end to end\", \"an acceptance scenario failed\", \"verify a decision model provider\", \"smoke test the coffee shop\". INVOKES: aspire CLI, playwright-cli, dotnet test, the counter API. FOR SINGLE OPERATIONS: use the aspire or playwright-cli skills directly."
license: MIT
metadata:
  author: coffeeshop-jev
  version: "1.0.0"
---

# coffeeshop-jev acceptance tests

Executable UI spec for `WebApp/`: `WebApp/acceptance/A1..A8_*.js`, run by `WebApp/acceptance/run.sh`. Scenario meanings and per-provider results: `jev_routing_research.md` §17.10 (the single source of truth). UX of record: `ui-prototypes/`.

Run all commands from the repo root `coffeeshop-jev/` unless said otherwise. Keep command output short: pipe through `cut -c1-260`, `tail`, or `grep`, and never print API keys.

## 0. Preconditions (check, don't assume)

1. Tools: `aspire --version`, `playwright-cli --help | head -3`, `dotnet --list-sdks` (needs `11.0.100-rc.2…` or newer, `global.json` pins it).
2. Config: `src/AppHost/appsettings.Development.json` (gitignored, may start with a BOM: read it with `encoding='utf-8-sig'`) has `Parameters` with `decision-model-provider` and that provider's `decision-model-{provider}-url` / `-api-key` (`-model` for `microsoft`), plus `openai-base-url`, `openai-model`, `openai-api-key`. Print only the **names** and the provider value, never the keys.
3. The provider is reachable: `laya` needs `laya-serve` on `:8002` (`GET /health`), `openjev` a LAN host (often unreachable from a sandbox), `microsoft` a Foundry deployment (`az cognitiveservices account deployment list -g <rg> -n <account>` shows the deployment name to use), `typesafe` model `jev-latest`.
4. Offline gate first (fast, catches most regressions): `dotnet build CoffeeShop.slnx -warnaserror` then `dotnet test tests/CoffeeShop.Tests`. If these fail, fix them before any UI run.

## 1. Run

```bash
aspire stop >/dev/null 2>&1; sleep 2                                   # no stale stack
aspire run --detach --non-interactive --nologo --format Json >/dev/null 2>&1
sleep 35                                                               # catalog, counter, web
aspire describe --format Json | python3 -c "import sys,json;t=sys.stdin.read();d=json.loads(t[t.index('{'):]);print([(r['name'][:8],r.get('healthStatus')) for r in d['resources'] if r['name'].startswith(('web','counter','catalog'))])"
( cd "$TMPDIR" 2>/dev/null || cd /tmp; playwright-cli close >/dev/null 2>&1; playwright-cli open http://localhost:5200 >/dev/null 2>&1 )
WebApp/acceptance/run.sh               # A1..A8 (a few minutes: real decision model + LLM)
WebApp/acceptance/run.sh A1 A4         # or only some
```

- Web is `http://localhost:5200`, counter API `http://localhost:5100`. If a resource is not Healthy after ~60 s, go to section 3 before running anything.
- `playwright-cli open` must have run (it writes `.playwright-cli/` in the current directory: use a scratch directory, not the repo). If `run.sh` prints **no result lines**, the browser session is closed: reopen it.
- Success per scenario: `"A1 ok"`. Failure: `### Error` and one line, e.g. `ASSERT: all 5 ticket steps done` or `TimeoutError: locator.waitFor`. `run.sh` exits non-zero on any failure.
- When finished (pass or give up): `aspire stop`, `playwright-cli close`.

## 2. Pass → report

State per scenario ok/failed, the provider and model used (from the order details `provider / model`), and the offline test count. Do not claim a provider "passes" for scenarios you did not run. Unknowns stay unknown.

## 3. Failure workflow: collect, diagnose, fix, re-run

Do these in order for **each failed scenario**, stop at the first step that explains it, and keep a short note of what you saw.

### 3.1 Re-run alone, to rule out flakiness
`WebApp/acceptance/run.sh A4` (twice if the failure was a `TimeoutError`). Known flaky classes are in 3.4. A scenario that passes alone but fails in the suite usually depends on state left by a previous one (guest id, cart).

### 3.2 Collect evidence (all cheap, run them before reading any code)

| Evidence | Command |
|---|---|
| Resource health | `aspire describe --format Json` (as above) |
| Counter logs (decision-model failures, workflow errors) | `aspire logs counter -n 80 2>&1 \| grep -iE "warn\|fail\|error\|decision" \| cut -c1-300` |
| Web logs (Blazor circuit crashes, render exceptions) | `aspire logs web -n 80 2>&1 \| grep -iE "exception\|error\|fail\|   at " \| cut -c1-300` |
| **Order traces** (what the counter decided and why) | `GET http://localhost:5100/orders` and `/orders/{n}`: print `result.status` and each `trace.audit` row (`gate`, `gate-error`, `clarify`, `result`) and `trace.agUi`. A `gate-error` row carries `provider / model: HTTP <status> (POST <url>) <server message>`. Use `ctx_execute` / a short script, not curl, if your shell blocks curl |
| What the page showed | `playwright-cli screenshot --filename=fail.png`, then look at it; `playwright-cli snapshot`; `playwright-cli console` (browser errors; `[ERROR] … unhandled exception on the current circuit` means a server render crash: read the web logs) |
| The failing assertion's inputs | re-run the scenario's steps by hand with `playwright-cli run-code "async (page) => { … return text }"`, printing `.innerText()` of the ticket (`[data-testid=order-ticket]`), the chat (`.sc-ai-message-list`), the hint, the audit log |
| Provider probe | POST one Gate-shaped request straight to the provider URL (same path, auth and model as `DecisionProfile`) and print `answers`. See `jev_routing_research.md` §18 for shapes and measured values |
| Recent changes | `git status --short`, `git diff --stat`, `git log -5 --oneline` |

### 3.3 Diagnose: symptom → likely cause → where to look

| Symptom | Likely cause | Check / fix |
|---|---|---|
| Every message answers "We can't take orders right now" / ticket says "Ordering was offline" | the Gate's provider call fails | `gate-error` audit row. `400 Unknown model: english` = provider and model name disagree (a laya model on the TypeSafe URL); `404 DeploymentNotFound` = wrong Foundry deployment name; `404` with a doubled path = URL already contains the path (normalised for `microsoft`); `401/403` = key; connection refused / timeout = server down or unreachable. Fix the config (`decision-model-provider`, the provider's url/key/model), restart the stack |
| Every message answers "Sorry, I can only help with food and drink orders" | provider answers `other` / low confidence, or a state key echoes an answer label (`history` made laya say `order_history`) | order trace `gate` row (intent, confidence, on-menu); compare with the profile cut-offs in `DecisionProfile.cs`; for a weak provider check `GateMode`/cut-offs (§18.4-18.5) |
| Menu / clarify / history scenarios fail, orders pass | provider misreads those intents | same: `gate` rows; provider limits are in §18.4 |
| `ASSERT: …ticket total $12.00: Current order` | the check ran before the run started (race), or the run ended without an order | wait on the ticket total, not on message counts; read the order trace |
| `TimeoutError` waiting for a reply (150 s) | LLM slow or failing (`openai-*`), the run crashed, or the circuit died | counter logs for the LLM call, web logs for a circuit crash, `aspire describe` |
| Browser console: unhandled exception on the circuit | a Razor component threw | web logs stack trace; typical past causes: `CultureInfo("en-US")` under `InvariantGlobalization`, a missing `[JsonPropertyName]` or `AddGeneratedToolBlocks()`, a missing render mode |
| Menu card empty or prices `$0.00` | tool-result DTO not deserialised | `[JsonPropertyName]` on the DTOs in `WebApp/Models/Dtos.cs` |
| `A2`/`A6` fail only on a decisive provider | the test input became "off-topic" instead of "unclear" | "hmm" is not a reliable unclear input; the scripts use "something sweet" |
| Build error `Microsoft.Extensions.Validation.Embedded` in `WebApp` | flaky first restore after a package change | run the build again |
| No results printed at all | browser session closed | `playwright-cli open http://localhost:5200` |

### 3.4 Fix, then re-run

1. Decide **product bug vs test bug vs environment**. Product bug: fix the code and add or extend a unit test in `tests/CoffeeShop.Tests` (the offline suite must stay green). Test bug: fix the script in `WebApp/acceptance/`, and make it wait (`waitFor`, `waitForFunction`) for the UI state it asserts instead of reading right after a click, because Blazor updates over a websocket. Environment (provider down, wrong deployment name, expired key): fix the config or report it; do not weaken an assertion to make a scenario pass.
2. Rebuild what changed: `dotnet build CoffeeShop.slnx -warnaserror`, `dotnet format CoffeeShop.slnx`, `dotnet test tests/CoffeeShop.Tests`. For WebApp-only changes `aspire resource web restart` is enough after `dotnet build WebApp`; for counter or AppHost changes use `aspire stop` and start again.
3. Re-run the failed scenario, then the **whole suite** once. Keep the loop to at most 3 fix attempts per scenario; then stop and report what was collected and what you think is wrong.
4. If the fix changes behaviour that the docs describe, update `jev_routing_research.md` (§17.10 results, §18 for providers), `ui-prototypes/` (UX of record) and the README, then `graphify update .`.

### 3.5 Failure report (always, even when fixed)

For each failed scenario: the assertion line, the evidence that explained it (log line, audit row, screenshot finding), the cause classified as product / test / environment, the change made (file and what), and the re-run result. Do not paste keys, full logs or whole traces.

## Guardrails

- Never print, log or commit API keys from `appsettings.Development.json` or user-secrets; mask when showing config.
- Do not commit unless asked. `bin/`, `obj/`, `.playwright-cli/`, screenshots and the dev settings file stay out of git.
- The acceptance run uses real services and a few LLM calls per scenario; do not loop it more than needed.
- Always stop the stack (`aspire stop`) and close the browser (`playwright-cli close`) at the end.
