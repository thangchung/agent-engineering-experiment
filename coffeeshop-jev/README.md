# coffeeshop-jev

Coffee-shop ordering demo: .NET 10 + Aspire + Microsoft Agent Framework, using [Jev](https://docs.typesafe.ai/introduction)/OpenJev for intent- and confidence-routing instead of an LLM. 3 in-process MAF agents (Counter, Barista, Kitchen).

Design + rationale: `research.md`. Task breakdown: `tasks.md`.

## Runtime

```mermaid
flowchart LR
    B["Browser index.html"] -->|"POST /orders, SSE"| C
    B -->|"POST /orders/runId/answer"| C
    subgraph C["CounterService"]
        WF["MAF workflow"]
        CA["CounterAgent"]
        BA["BaristaAgent"]
        KA["KitchenAgent"]
        WF --- CA
        WF --- BA
        WF --- KA
    end
    C -->|"MCP HTTP: get_menu"| P["ProductCatalogService - MCP server"]
    C -->|"LAN: POST /v1/systemone - gate, split, guards"| J[("OpenJev - RTX 6000")]
    C -->|"HTTPS: /openai/v1 chat - 3 agents"| F[("Microsoft Foundry deployment")]
    C -.->|"OTLP"| D["Aspire dashboard"]
    P -.->|"OTLP"| D
```

## Workflow (Microsoft Agent Framework)

```mermaid
flowchart TD
    IN["order text"] --> G["Gate - Jev intent + on_menu"]
    G -->|"Accepted"| X["Extract - CounterAgent to lines"]
    G -->|"Unclear"| Q["Clarify - CounterAgent writes question"]
    G -->|"Rejected"| R["Reply - END"]
    X -->|"lines ok"| S["StationSplit - Jev per line, bands"]
    X -->|"no valid lines"| Q
    Q --> H{{"RequestPort ask-customer"}}
    H -->|"human answer"| G
    S -->|"fan-out"| BS["BaristaStation - BaristaAgent"]
    S -->|"fan-out"| KS["KitchenStation - KitchenAgent"]
    BS --> A["Deliver - barrier + CounterAgent reply"]
    KS --> A
    A --> OUT["output OrderResult"]
```

Same workflow, as a sequence diagram:

```mermaid
sequenceDiagram
    actor Customer
    participant WF as MAF Workflow (Gate/Extract/Split/Deliver)
    participant Jev as OpenJev
    participant CA as CounterAgent
    participant BA as BaristaAgent
    participant KA as KitchenAgent

    Customer->>WF: order text
    WF->>Jev: POST /v1/systemone (intent, on_menu)
    Jev-->>WF: intent + on_menu + confidence

    alt Rejected (off-topic / ask cap)
        WF-->>Customer: reply (END)
    else Unclear (low confidence or off-menu)
        WF->>CA: write clarify question
        CA-->>WF: question text
        WF-->>Customer: RequestPort ask-customer
        Customer->>WF: answer
        WF->>Jev: POST /v1/systemone (retry)
        Jev-->>WF: intent + on_menu
        Note over WF,Jev: loops back to Gate until Accepted/Rejected
    else Accepted
        WF->>CA: extract order lines
        CA-->>WF: OrderDraft (lines)

        alt no valid lines
            WF->>CA: write clarify question
            Note over WF,Customer: same ask/answer loop as Unclear, above
        else lines ok
            WF->>Jev: POST /v1/systemone (station_i per line)
            Jev-->>WF: station + confidence band per line

            par fan-out
                WF->>BA: barista lines
                BA-->>WF: StationTicket (barista)
            and
                WF->>KA: kitchen lines
                KA-->>WF: StationTicket (kitchen)
            end

            WF->>CA: write confirmation reply
            CA-->>WF: reply text
            WF-->>Customer: OrderResult (Completed)
        end
    end
```

## Prerequisites

- .NET SDK `10.0.302`+ (`global.json` pins it)
- A reachable OpenJev server (`/v1/systemone`)
- An OpenAI-compatible chat endpoint (Foundry, or any compatible gateway) for the 3 agents

## Configure secrets (once)

```bash
cd src/AppHost
dotnet user-secrets set "openjev-url" "http://<your-openjev-host>:<port>"
dotnet user-secrets set "openai-base-url" "https://<your-endpoint>/v1"
dotnet user-secrets set "openai-model" "<model-name>"
dotnet user-secrets set "openai-api-key" "<key>"
```

`AppHost` reads these via `Parameters:*` (see `AppHost.cs`). Local-only, never committed — don't put them in `appsettings.json`.

## Run + debug (Aspire)

```bash
aspire run
```

Or press F5 in VS Code / Rider with `AppHost` as the startup project — breakpoints in `CounterService`/`ProductCatalogService` work normally, Aspire attaches to both.

`aspire run` prints the dashboard URL with a login token:

- **Dashboard**: `https://localhost:17214/login?t=<token>` — traces, logs, resource health. GenAI-enriched spans for both the OpenJev calls and the agent LLM calls (`chat jev-latest`, `chat <model>`).
- **Counter UI**: `http://localhost:5100`
- **Catalog API**: `http://localhost:5101`

If `openjev-url` is unreachable, `counter` still starts (fails closed at the call site, not at startup).

## Run evals (Jev as judge)

Live evals hit the real OpenJev + chat endpoint — same secrets as above (`EvalConfig.cs` reads the same AppHost user-secrets store, no extra setup). If secrets aren't configured, these tests **fail fast** with the exact `dotnet user-secrets set` command to run — they don't skip silently.

```bash
# offline only (fast, no network, runs in CI)
dotnet test tests/CoffeeShop.Tests

# gate + station accuracy against real Jev
dotnet test tests/CoffeeShop.Evals --filter "GateEvals|StationEvals"

# JevJudge accept/escalate cascade (Jev judges; low-confidence items escalate to the LLM)
dotnet test tests/CoffeeShop.Evals --filter "JudgeCascadeTests|JevJudgeEscalationTests"

# variance smoke check (30 repeated reads x 3 frozen tickets)
dotnet test tests/CoffeeShop.Evals --filter "JevJudgeVarianceTests"

# build the combined report from whatever ran above
dotnet test tests/CoffeeShop.Evals --filter "FullyQualifiedName~CoffeeShop.Evals.Report"
```

Report: `tests/CoffeeShop.Evals/bin/Debug/net10.0/evals/out/report.md` (gate/station accuracy, JevJudge accept/escalate/human-review counts per rubric item). Variance detail: `evals/out/judge-variance.md`.

Design + what's implemented vs. descoped: `research.md` §10 (esp. §10.6a, §10.9).
