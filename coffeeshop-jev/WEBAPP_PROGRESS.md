# WebApp (AG-UI) progress

Source: `jev_routing_research.md` §17 (option A), UX from `ui-prototypes/`. Check a box only after its command ran and passed.

## Decisions
- UI project at `WebApp/` (repo root, per request), net11.0. `AppHost` net11.0. Everything else stays net10.0.
- Legacy `POST /orders` SSE, `/answer`, `RunRegistry` stay (tests + eval harness use them). `wwwroot` is gone. Retire later = WT7.
- Skipped from prototype: "Simulate Jev offline" toggle (mock-only; the real Jev-down path is covered by `GateExecutorTests`).

## Tasks

### T1 Toolchain
- [x] `global.json` → 11 RC SDK, `AppHost` net11
  - [x] `dotnet build CoffeeShop.slnx -warnaserror` clean
  - [x] `dotnet test tests/CoffeeShop.Tests` green on net10 projects
### T2 Remove old UI
- [x] delete `src/CounterService/wwwroot`, drop `UseDefaultFiles/UseStaticFiles`
  - [x] build + tests green, no `wwwroot` references left (tasks.md DoD line fixed)
### T3 Store: customer id + lines
- [x] `OrderRecord.CustomerId`, `ListByCustomer`, `OrderResult.Lines`, `GET /orders?customerId=`
  - [x] filter returns only that customer, newest first (covered by the `EndpointTests` store-backed test; no separate `OrderStoreTests`)
  - [x] `EndpointTests`: `?customerId` filter over HTTP
### T4 Chat history intent
- [x] Gate `order_history` choice → `HistoryRequested` → `ShowHistoryExecutor` → `HistoryResponse`
  - [x] `PolicyTests`: `order_history` high confidence → `HistoryRequested`
  - [x] `WorkflowTests`: history request yields `HistoryResponse` (2 orders newest first; empty for new customer)
### T5 Clarify as turn boundary
- [x] `OrderWorkflow.Build(turnBoundary, seed)`; clarify yields `ClarifyRequest` output, no `RequestPort`
  - [x] `WorkflowTests`: unclear → run ends with `ClarifyRequest`; seeded history + asks=2 → `Rejected(AskCap)` (G16)
### T6 AG-UI front door
- [x] `CoffeeShopAgent : AIAgent` + `AddAGUIServer` + `MapAGUIServer("/agui")`
  - [x] contract test via `AGUIChatClient`: order → `STATE_SNAPSHOT` with lines/tickets/total + final text (G01)
  - [x] contract test: unclear → question text, state `asks=1`, then next run completes
  - [x] contract test: menu request → `show_menu` tool result; history request → `show_history`
  - [x] contract test: bad/oversize state is ignored (customerId, history caps)
### T7 WebApp shell
- [x] `WebApp` project (Blazor Web App, interactive server), `Components.AI` pins from §17.6, named SSE client, `CounterAgentConnection`
- [x] AppHost: `web` project, `WithReference(counter).WaitFor(counter)`, external http endpoint
  - [x] `aspire run` starts catalog, counter, web all Healthy
- [x] layout: topbar, tabs (Order / Menu + cart count / Your orders), theme toggle (persisted, follows system), guest popover (id, new guest), skip link
### T8 Order page
- [x] `ChatPage` chat, welcome text, suggestion chips, 500-char cap
- [x] ticket rail: 5 steps, station columns, flags, total, clamp note, order number
- [x] inline menu card, inline history card, empty-history template
- [x] clarify answered in next turn; ask cap shows "not placed"
- [x] error banner on `RUN_ERROR`
### T9 Menu page
- [x] board (bar / kitchen), steppers 0..20, cart bar (count, total, Clear, Place order → sends "2 × Latte, …" in chat)
### T10 History page
- [x] Mine / Everyone today, status chips, slips with lines/total, "how the counter handled it", empty state with menu link
### T11 AG-UI events panel
- [x] toggle in guest popover, event log, Clear
### T12 Acceptance (playwright-cli against `aspire run`)
- [x] A1 golden path "2 lattes and a croissant" → ticket total $12.25-ish from catalog, reply in chat, order in history
- [x] A2 "something sweet" → question → "the first one" / "muffin" → completes
- [x] A3 off-topic → refusal, shown as "Not placed" in history
- [x] A4 "what do you have?" → inline menu card; menu tab → cart → place order → chat run
- [x] A5 "what did I order before?" → history card; new guest → empty template
- [x] A6 ask cap after 3 unclear messages ("something sweet" x3; "hmm" is classified off-topic by real Jev)
- [x] A7 theme toggle persists after reload; keyboard: Enter sends, Shift+Enter newline; mobile width 390px has no horizontal scroll
### T13 Docs
- [x] README run section, `jev_routing_research.md` §17 status → built, `graphify update .`

## Log
- T1/T2 done: build 0 warn, 85 tests pass on SDK 11.0.100-rc.2 (net10 projects unchanged). Only history docs mention wwwroot.
- T3-T6 done: 98 tests pass (+13: AG-UI contract x7 via real `AGUIChatClient`, history/turn-boundary workflow x4, policy x1, customer filter x1). Spike answers: custom `AIAgent` emitting `FunctionCallContent`+`FunctionResultContent` in ONE update → client gets `ToolCallEnd`/`ToolCallResult` and `MapResultAsStateSnapshot` → `StateSnapshotEvent` (U-W1 closed). Client also sees the `order_progress` call/result contents, so the UI must not render a tool block for it. Jev script exhaustion in `ScriptedJevHandler` (queue `.Last()` on empty) is a test-helper trap: give one entry per run.
- T7-T12 done. `WebApp/acceptance/run.sh` A1-A7 all pass on the final build (real Jev + LLM, stack from `aspire run --detach`). `dotnet build CoffeeShop.slnx -warnaserror` 0 warnings, `dotnet format --verify-no-changes` clean, 98 offline tests pass.
- Not verified: live `GateEvals` (R-G1 regression for the 5th `order_history` choice) fail with Jev HTTP 403 in the eval harness config, unrelated to this change; the app itself reaches Jev fine. Run them with working eval secrets before relying on the Gate accuracy number.
- Bugs found by driving the real UI (fixed): `CultureInfo("en-US")` throws under InvariantGlobalization; `[ToolBlock]` needs `AddGeneratedToolBlocks()` + `[JsonPropertyName]`; layout badge needs `Changed` on cart edits; `Routes` needs an explicit render mode; acceptance `say()` must compare against the chat's own assistant count.
