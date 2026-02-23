# Tasks: CoffeeShop Ordering System

**Feature branch**: `001-coffeeshop-ordering` | **Generated**: 2026-02-23
**Input**: [spec.md](spec.md) · [plan.md](plan.md) · [data-model.md](data-model.md) · [contracts/](contracts/) · [research.md](research.md) · [quickstart.md](quickstart.md)

**Tests**: Included — Constitution III mandates Test-First (NON-NEGOTIABLE). All test tasks MUST fail before implementation tasks begin.

**Organization**: Grouped by user story to enable independent implementation and testing of each story.
US2 (Customer Identification) precedes US1 (Place an Order) because customer lookup is a code-level prerequisite for placing orders; both are P1 priority.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[US#]**: User story this task belongs to
- Tests are [T] tasks — MUST be written first and confirmed FAILING before implementation

---

## Phase 1: Setup

**Purpose**: Create solution skeleton, project scaffolding, and tooling configuration so every subsequent phase has a buildable baseline.

- [X] T001 Create solution and directory tree: `CoffeeShop.sln`, `coffeeshop/apphost/`, `coffeeshop/servicedefaults/`, `coffeeshop/backend/counter/`, `coffeeshop/backend/product-catalog/`, `coffeeshop/frontend/`, `coffeeshop/tests/integration/`, `coffeeshop/tests/unit/` per plan.md Project Structure
- [X] T002 Initialize all .csproj files with correct package references: `counter.csproj` (Microsoft.Agents.AI, GitHub.Copilot.SDK, OpenTelemetry packages), `product-catalog.csproj`, `apphost.csproj` (Aspire.Hosting, Aspire.Hosting.NodeJs), `servicedefaults.csproj` (ServiceDefaults packages), `tests/integration/tests.csproj` (xUnit, Aspire.Hosting.Testing), `tests/unit/tests.csproj` (xUnit) — add all to `CoffeeShop.sln`
- [X] T003 [P] Initialize frontend in `coffeeshop/frontend/`: `package.json` (next, @copilotkit/react-core, @copilotkit/react-ui, shadcn-ui, tailwindcss, vitest, @testing-library/react, @vercel/otel, @opentelemetry/sdk-web, @opentelemetry/exporter-trace-otlp-http), `tsconfig.json` with `"strict": true`, `tailwind.config.ts`, `next.config.ts`
- [X] T004 [P] Configure backend linting: `.editorconfig` at solution root; configure `<Nullable>enable</Nullable>` + `<ImplicitUsings>enable</ImplicitUsings>` in all .csproj files; configure ESLint with TypeScript strict rules in `coffeeshop/frontend/.eslintrc.json`

**Checkpoint**: `dotnet build CoffeeShop.sln` succeeds (no source yet); `npm install` in `coffeeshop/frontend/` succeeds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story work begins. All domain entities, stores, in-process channels, AppHost wiring, MAF scaffolding, and frontend shell.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Implement `coffeeshop/servicedefaults/Extensions.cs`: `AddServiceDefaults()` (OTel traces with ASP.NET Core + HttpClient instrumentation, OTLP exporter from `OTEL_EXPORTER_OTLP_ENDPOINT`, metrics, health checks) and `MapDefaultEndpoints()` (`/health` aggregate + `/alive` liveness + `/healthz` alias via `MapHealthChecks("/healthz")`). counter and product-catalog MUST call `builder.AddServiceDefaults()` in their `Program.cs`.
- [X] T006 Define all domain enums in `coffeeshop/backend/counter/Domain/Enums.cs`: `CustomerTier` (Gold, Silver, Standard), `ItemCategory` (Beverages, Food, Others), `ItemType` (all 11 types from data-model.md), `OrderStatus` (Pending, Confirmed, Preparing, Ready, Completed, Cancelled)
- [X] T007 [P] Define `Customer` record in `coffeeshop/backend/counter/Domain/Customer.cs`: fields Id, FirstName, LastName, Email, Phone, Tier (CustomerTier), AccountCreated (DateOnly)
- [X] T008 [P] Define `MenuItem` record in `coffeeshop/backend/counter/Domain/MenuItem.cs`: fields Type (ItemType), DisplayName, Category (ItemCategory), Price (decimal), IsAvailable (bool)
- [X] T009 [P] Define `OrderItem` record in `coffeeshop/backend/counter/Domain/OrderItem.cs`: fields Type, DisplayName, Quantity (1–5), UnitPrice, LineTotal
- [X] T010 [P] Define `Order` record in `coffeeshop/backend/counter/Domain/Order.cs`: fields Id, CustomerId, Items (IReadOnlyList\<OrderItem\>), TotalPrice, Status (OrderStatus), Notes (string?), EstimatedPickup (string?), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset)
- [X] T011 Define `OrderDispatch` and `WorkerResult` records in `coffeeshop/backend/counter/Domain/ChannelMessages.cs`: `OrderDispatch(OrderId, CustomerId, Items, CorrelationId)`, `WorkerResult(CorrelationId, Success, ErrorMessage?)` — CorrelationId generated as `$"{order.Id}-{Guid.NewGuid():N}"`
- [X] T012 Implement `InMemoryCustomerStore` in `coffeeshop/backend/counter/Infrastructure/InMemoryCustomerStore.cs`: `ConcurrentDictionary<string, Customer>` keyed by Id + `Dictionary<string, string>` email→Id index; expose `TryGetById`, `TryGetByEmail`, `TryAdd` (thread-safe)
- [X] T013 Implement `InMemoryOrderStore` in `coffeeshop/backend/counter/Infrastructure/InMemoryOrderStore.cs`: `ConcurrentDictionary<string, Order>` keyed by OrderId + `Dictionary<string, List<string>>` customerId→orderIds index; expose `TryAdd`, `TryUpdate` (compare-and-swap via `TryUpdate`/`AddOrUpdate`), `TryGetById`, `GetByCustomerId` — prohibit direct `dict[key] = value`
- [X] T014 Implement `SeedData.cs` in `coffeeshop/backend/counter/Infrastructure/SeedData.cs`: 3 seed customers (C-1001 Alice Gold, C-1002 Bob Silver, C-1003 Carol Standard), 11 seed `MenuItem` instances (all ItemTypes from data-model.md with prices + IsAvailable), 3 seed orders (ORD-5001/ORD-5002/ORD-5003 in various statuses) — loaded at startup via `IHostedService` or `app.Lifetime.ApplicationStarted`
- [X] T015 Register `Channel<T>` singletons in `coffeeshop/backend/counter/Program.cs`: `BaristaDispatchChannel` (`Channel<OrderDispatch>`), `KitchenDispatchChannel` (`Channel<OrderDispatch>`), `BaristaReplyChannel` (`Channel<WorkerResult>`), `KitchenReplyChannel` (`Channel<WorkerResult>`) — all `Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = false, SingleReader = true })`
- [X] T016 Scaffold `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs`: class with `Instructions` string (all 6 required role/routing/boundary/pickup/MCP/identification rules from plan.md §Agent Instructions); register `CopilotClient` as DI singleton; register MAF agent runner in DI — no tools yet (added per story)
- [X] T017 Wire `coffeeshop/apphost/Program.cs`: `AddProject<Projects.ProductCatalog>("product-catalog")`, `AddProject<Projects.Counter>("counter").WithReference(catalog).WithExternalHttpEndpoints()`, `AddNpmApp("frontend", "../frontend", "dev").WithReference(counter).WithEnvironment("COUNTER_URL", counter.GetEndpoint("http")).WithHttpEndpoint(env: "PORT")` — no barista/kitchen AddProject (workers are IHostedService inside counter)
- [X] T018 [P] Scaffold `coffeeshop/frontend/src/app/layout.tsx`: root layout with `<CopilotKit runtimeUrl="/api/copilotkit">` provider wrapping children — runtimeUrl MUST be `/api/copilotkit` (same-origin), NOT the Aspire-injected COUNTER_URL
- [X] T019 [P] Implement `coffeeshop/frontend/src/instrumentation.ts`: OTel init with `@vercel/otel` + `@opentelemetry/exporter-trace-otlp-http`, service name `"coffeeshop-frontend"`, OTLP endpoint from `process.env.OTEL_EXPORTER_OTLP_ENDPOINT`, instrumented spans for page loads + fetch calls to `/api/copilotkit` + counter REST endpoints
- [X] T020 [P] Define shared TypeScript interfaces in `coffeeshop/frontend/src/types/index.ts`: `CustomerDto`, `MenuItemDto`, `OrderItemDto`, `OrderDto` (matching contracts/orders.md C# records exactly), `OrderHistoryResponse`, `CustomerLookupResponse`

**Checkpoint**: `dotnet build CoffeeShop.sln` succeeds with no warnings. `dotnet run --project coffeeshop/apphost` starts without error. `/health` returns 200 for counter. No hardcoded `localhost:NNNN` in any production source.

---

## Phase 3: User Story 2 — Customer Identification (Priority: P1)

**Goal**: A customer can identify themselves using email, customer ID, or order ID; the system greets them by first name and displays their tier. Unrecognized identifiers return a clear error message.

**Independent Test**: Hit `GET /api/v1/customers/lookup?identifier=alice@example.com` → returns `{"id":"C-1001","firstName":"Alice",...}`. Hit with unknown identifier → returns 404 with RFC 9457 body. CounterAgent greets the customer by first name in chat.

**Tests for User Story 2** ⚠️ Write first — confirm FAILING before T023

- [X] T021 [P] [US2] Write unit test for `LookupCustomer` use case covering: email lookup (case-insensitive), customerId lookup, orderId→customer lookup, unknown identifier → `CustomerNotFoundException` in `coffeeshop/tests/unit/UseCases/LookupCustomerTests.cs`
- [X] T022 [P] [US2] Write unit test for `InMemoryCustomerStore` covering: `TryGetByEmail` case-insensitive, `TryGetById`, concurrent `TryAdd` uniqueness in `coffeeshop/tests/unit/Infrastructure/CustomerStoreTests.cs`

**Implementation for User Story 2**

- [X] T023 [US2] Implement `LookupCustomer` use case in `coffeeshop/backend/counter/Application/UseCases/LookupCustomer.cs`: accept `string identifier`; resolve via customerId first, then email (case-insensitive), then orderId→customerId; return `Customer` or throw `CustomerNotFoundException`; log resolution path via OTel span
- [X] T024 [US2] Implement `GET /api/v1/customers/lookup` endpoint in `coffeeshop/backend/counter/Endpoints/CustomerEndpoints.cs`: query param `?identifier=`; maps to `LookupCustomer`; returns `CustomerLookupResponse` (200) or RFC 9457 404; no auth required (FR-019)
- [X] T025 [US2] Register `LookupCustomer` tool on `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs`: tool name `LookupCustomer`, param `identifier: string`, return `CustomerLookupResponse` — include tier in response for FR-018 greeting
- [X] T026 [US2] Implement `CustomerPanel` component in `coffeeshop/frontend/src/components/ui/CustomerPanel.tsx` (displays firstName, tier badge) and register `setCustomer` `useCopilotAction` in `coffeeshop/frontend/src/app/page.tsx` that sets `useCopilotReadable("customer", ...)` state

**Checkpoint**: Start Aspire AppHost → chat "I'm Alice" → CounterAgent calls `LookupCustomer("alice@example.com")` → `CustomerPanel` shows "Alice · Gold". Unknown identifier returns "account not found" in chat.

---

## Phase 4: User Story 1 — Browse Menu and Place an Order (Priority: P1) 🎯 MVP

**Goal**: An identified customer can browse the full menu (all items with prices and availability shown), select items, review an order summary, confirm, and receive a confirmed order with an estimated pickup time. Real-time streaming status updates are emitted at all 4 AG-UI boundaries during fulfilment.

**Independent Test**: `POST /api/v1/orders` with a valid identified customer → 201 with `"status":"confirmed"`, `estimatedPickup` set, stream events emitted for all 4 boundaries. `GET /api/v1/menu` returns all 11 items including unavailable ones. BaristaWorker + KitchenWorker complete their dispatch loops and write `WorkerResult` to reply channels.

**Tests for User Story 1** ⚠️ Write first — confirm FAILING before T030

- [X] T027 [P] [US1] Write unit test for `PlaceOrder` covering: pre-placement validation sequence (empty order → 400, invalid qty → 400, MCP unavailable → 503, item unavailable → 409), pickup-time calculation (beverages-only → "5 minutes", mixed → "10 minutes"), `TryAdd` to store only after all validation passes in `coffeeshop/tests/unit/UseCases/PlaceOrderTests.cs`
- [X] T028 [P] [US1] Write unit test for `BaristaWorker` covering: reads `OrderDispatch` from channel, invokes `RunStreamingAsync`, writes `WorkerResult(Success=true)` to reply channel; exception path writes `WorkerResult(Success=false, ErrorMessage=...)` in `coffeeshop/tests/unit/Workers/BaristaWorkerTests.cs`
- [X] T029 [P] [US1] Write unit test for `KitchenWorker` (same coverage as BaristaWorker but for food/others items) in `coffeeshop/tests/unit/Workers/KitchenWorkerTests.cs`

**Implementation for User Story 1**

- [X] T030 [US1] Implement product-catalog MCP Server in `coffeeshop/backend/product-catalog/Program.cs`: expose `get_menu_items` (no params → `MenuItemDto[]`) and `get_item_by_type` (`type: string` → `MenuItemDto | null`) MCP tools; seed with all 11 `MenuItemDto` instances; call `AddServiceDefaults()` + `MapDefaultEndpoints()`
- [X] T031 [US1] Implement `McpProductCatalogClient` in `coffeeshop/backend/counter/Infrastructure/McpProductCatalogClient.cs`: typed `HttpClient` with Aspire-injected base URL (from `WithReference(catalog)`), 5-second per-call timeout, `GetMenuItemsAsync()` + `GetItemByTypeAsync(string type)` — on timeout/error throw `ProductCatalogUnavailableException` (→ fail-fast 503)
- [X] T032 [US1] Implement `GetMenu` use case + `GET /api/v1/menu` endpoint in `coffeeshop/backend/counter/Application/UseCases/GetMenu.cs` and `coffeeshop/backend/counter/Endpoints/MenuEndpoints.cs`: calls `McpProductCatalogClient.GetMenuItemsAsync()`; returns **all** items including `isAvailable: false` (never filter — plan.md §MCP isAvailable Semantics); 503 on MCP failure
- [X] T033 [P] [US1] Implement `BaristaWorker` `IHostedService` in `coffeeshop/backend/counter/Workers/BaristaWorker.cs`: background `Channel<OrderDispatch>` reader loop; invokes MAF `BaristaAgent.RunStreamingAsync` per dispatch (streaming output consumed for OTel spans only — does NOT emit AG-UI events); writes `WorkerResult` to `BaristaReplyChannel`; catches all exceptions → `WorkerResult(Success=false, ErrorMessage=...)`
- [X] T034 [P] [US1] Implement `KitchenWorker` `IHostedService` in `coffeeshop/backend/counter/Workers/KitchenWorker.cs`: same structure as BaristaWorker but reads `KitchenDispatchChannel` and writes to `KitchenReplyChannel`; scope: Food + Others items
- [X] T035 [US1] Implement `PlaceOrder` use case in `coffeeshop/backend/counter/Application/UseCases/PlaceOrder.cs`: enforce pre-placement validation sequence (T027 test cases); build `Order` in local memory; `InMemoryOrderStore.TryAdd`; calculate `EstimatedPickup` string; classify items by `ItemCategory`; write `OrderDispatch` to BaristaDispatchChannel and/or KitchenDispatchChannel; `Task.WhenAll` both reply channels with 30-second `CancellationToken`; timeout/partial failure → set order `Cancelled`, return user-facing error; emit AG-UI events at all 4 boundaries (a)–(d) via `RunStreamingAsync`
- [X] T036 [US1] Implement `POST /api/v1/orders` endpoint in `coffeeshop/backend/counter/Endpoints/OrderEndpoints.cs`: maps `PlaceOrderRequest` → `PlaceOrder`; uses `RunStreamingAsync` (NOT `RunAsync`) for fulfilment; streams AG-UI events; returns 201 `OrderDto` on success — all error shapes per contracts/orders.md
- [X] T037 [US1] Implement `POST /api/v1/copilotkit` AG-UI streaming endpoint in `coffeeshop/backend/counter/Endpoints/CopilotKitEndpoints.cs`: receives CopilotKit runtime format; runs `CounterAgent` via MAF `RunStreamingAsync`; response `Content-Type: text/event-stream`; do not buffer — pipe through directly
- [X] T038 [US1] Implement Next.js AG-UI route bridge in `coffeeshop/frontend/src/app/api/copilotkit/route.ts`: use `CopilotRuntime` from `@copilotkit/react-core/server`; forward all requests to `${process.env.COUNTER_URL}/api/v1/copilotkit`; response must be SSE (`text/event-stream`); do not set `runtimeUrl` to COUNTER_URL directly in layout.tsx
- [X] T039 [P] [US1] Implement `MenuGrid` component in `coffeeshop/frontend/src/components/ui/MenuGrid.tsx`: renders all `MenuItemDto[]` items; unavailable items shown with "Unavailable" badge (greyed-out); each item button has `aria-label="{displayName} — ${price}"` + keyboard activatable (Enter/Space); clicking an item adds to current order state
- [X] T040 [P] [US1] Implement `OrderSummaryPanel` + `StatusBadge` in `coffeeshop/frontend/src/components/ui/OrderSummaryPanel.tsx`: shows items, quantities, line totals, total price; `StatusBadge` has `role="status"` + `aria-live="polite"`; panel has `aria-live="polite"` on status update region; hidden when no active order
- [X] T041 [US1] Register `GetMenu` + `PlaceOrder` tools on `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs`; register `updateMenu` + `updateOrderSummary` `useCopilotAction` handlers in `coffeeshop/frontend/src/app/page.tsx` bound to `MenuGrid` and `OrderSummaryPanel` state

**Checkpoint**: Start Aspire AppHost → identify as Alice → ask for menu → `MenuGrid` populates with all items → select a Latte + Croissant → confirm → SSE stream shows all 4 status events in chat → order confirmed with `"estimatedPickup": "Ready in about 10 minutes"`. Product-catalog 503 shows "Menu is currently unavailable…" in `MenuGrid`. This is the **MVP** — demo-ready at this checkpoint.

---

## Phase 5: User Story 3 — Check Order Status (Priority: P2)

**Goal**: An identified customer can query the current status of any order by order ID and receive a plain-language status description.

**Independent Test**: `GET /api/v1/orders/ORD-5001` → 200 `OrderDto` with current status (uses seed order `ORD-5001` — status `confirmed`). Query for non-existent order → 404. CounterAgent responds with status in natural language.

**Tests for User Story 3** ⚠️ Write first — confirm FAILING before T043

- [ ] T042 [P] [US3] Write unit test for `GetOrderStatus` covering: valid orderId → returns `OrderDto`, non-existent orderId → `OrderNotFoundException` in `coffeeshop/tests/unit/UseCases/GetOrderStatusTests.cs`

**Implementation for User Story 3**

- [ ] T043 [US3] Implement `GetOrderStatus` use case in `coffeeshop/backend/counter/Application/UseCases/GetOrderStatus.cs`: reads `InMemoryOrderStore.TryGetById`; returns `OrderDto` mapping; throw `OrderNotFoundException` if not found
- [ ] T044 [US3] Implement `GET /api/v1/orders/{orderId}` endpoint in `coffeeshop/backend/counter/Endpoints/OrderEndpoints.cs`: maps `{orderId}` → `GetOrderStatus`; returns 200 `OrderDto` or RFC 9457 404
- [ ] T045 [US3] Register `GetOrderStatus` tool on `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs` (param `orderId: string`, return `OrderDto`)
- [ ] T046 [US3] Register `updateOrderStatus` `useCopilotAction` in `coffeeshop/frontend/src/app/page.tsx`: updates `currentOrder.status` in state; `StatusBadge` re-renders with new status text within 2 seconds of event (WCAG `aria-live="polite"`)

**Checkpoint**: In chat "What's the status of ORD-5001?" → `CounterAgent` calls `GetOrderStatus`, `StatusBadge` updates. Query non-existent order ID (e.g. `ORD-9999`) → "order not found" in chat.

---

## Phase 6: User Story 4 — View Order History (Priority: P2)

**Goal**: An identified customer can retrieve their full order history sorted most-recent first. Customers with no orders receive the friendly empty-state message.

**Independent Test**: `GET /api/v1/customers/C-1001/orders` → 200 `OrderHistoryResponse` with at least 2 seed orders, sorted most-recent first. `GET /api/v1/customers/C-1003/orders` → `{"customerId":"C-1003","orders":[]}`.

**Tests for User Story 4** ⚠️ Write first — confirm FAILING before T048

- [ ] T047 [P] [US4] Write unit test for `GetOrderHistory` covering: customer with 2+ orders (sorted most-recent first), customer with no orders (returns empty list not error), unknown customerId → `CustomerNotFoundException` in `coffeeshop/tests/unit/UseCases/GetOrderHistoryTests.cs`

**Implementation for User Story 4**

- [ ] T048 [US4] Implement `GetOrderHistory` use case in `coffeeshop/backend/counter/Application/UseCases/GetOrderHistory.cs`: calls `InMemoryOrderStore.GetByCustomerId`; maps to `IReadOnlyList<OrderDto>`; sort by `CreatedAt` descending; return `OrderHistoryResponse(CustomerId, Orders)` — empty list is valid (not 404)
- [ ] T049 [US4] Implement `GET /api/v1/customers/{customerId}/orders` endpoint in `coffeeshop/backend/counter/Endpoints/CustomerEndpoints.cs`: maps `{customerId}` → `GetOrderHistory`; returns 200 `OrderHistoryResponse` (C# record from contracts/orders.md) — never 404 for empty history (FR-014)
- [ ] T050 [US4] Register `GetOrderHistory` tool on `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs` (param `customerId: string`, return `OrderHistoryResponse`)
- [ ] T051 [US4] Implement `OrderHistoryList` component in `coffeeshop/frontend/src/components/ui/OrderHistoryList.tsx` (renders `OrderDto[]` rows; shows "No orders yet — let's fix that!" when empty — FR-014) and wire into `coffeeshop/frontend/src/app/page.tsx`; register `useCopilotReadable("orderHistory", orders)` so CounterAgent can read history in context and register `useCopilotAction("showOrderHistory")` so CounterAgent can trigger the history panel to open

**Checkpoint**: "Show me my order history" → CounterAgent calls `GetOrderHistory("C-1001")` → `OrderHistoryList` renders seed orders sorted newest-first. C-1003 (no orders) → "No orders yet — let's fix that!" message.

---

## Phase 7: User Story 5 — Modify an Order or Add Special Instructions (Priority: P3)

**Goal**: Before an order reaches `preparing`, a customer can change items, add/change a note, or cancel. Attempts to modify or cancel a `preparing`/`ready`/`completed`/`cancelled` order are refused with the current status.

**Independent Test**: `PATCH /api/v1/orders/ORD-5001` with `{"notes":"Oat milk"}` on a `confirmed` order → 200 `OrderDto` with updated notes. Same PATCH on `ORD-5002` (a `preparing` order) → 409. `DELETE /api/v1/orders/ORD-5001` on `confirmed` → 200 `{"orderId":"ORD-5001","status":"cancelled"}`. DELETE on `ORD-5002` (`preparing`) → 409.

**Tests for User Story 5** ⚠️ Write first — confirm FAILING before T053

- [ ] T052 [P] [US5] Write unit test for `ModifyOrder` covering: update notes on `Pending` → success, update notes on `Confirmed` → success, attempt on `Preparing` → `OrderNotModifiableException`, attempt on `Ready`/`Completed`/`Cancelled` → `OrderNotModifiableException`, update items list (valid quantities) → success in `coffeeshop/tests/unit/UseCases/ModifyOrderTests.cs`
- [ ] T053 [P] [US5] Write unit test for `CancelOrder` covering: cancel `Pending` → `Cancelled`, cancel `Confirmed` → `Cancelled`, cancel `Preparing` → `OrderNotCancellableException`, cancel `Ready`/`Completed` → `OrderNotCancellableException` in `coffeeshop/tests/unit/UseCases/CancelOrderTests.cs`

**Implementation for User Story 5**

- [ ] T054 [US5] Implement `ModifyOrder` use case in `coffeeshop/backend/counter/Application/UseCases/ModifyOrder.cs`: guard `Status ∈ {Pending, Confirmed}` only; apply `Notes` patch and/or `Items` patch; recalculate `TotalPrice`; `InMemoryOrderStore.TryUpdate` (compare-and-swap, not direct assignment); throw `OrderNotModifiableException` with current status for blocked states
- [ ] T055 [US5] Implement `CancelOrder` use case in `coffeeshop/backend/counter/Application/UseCases/CancelOrder.cs`: guard `Status ∈ {Pending, Confirmed}` only; `InMemoryOrderStore.TryUpdate` status to `Cancelled`; throw `OrderNotCancellableException` with current status for blocked states
- [ ] T056 [P] [US5] Implement `PATCH /api/v1/orders/{orderId}` endpoint in `coffeeshop/backend/counter/Endpoints/OrderEndpoints.cs`: maps `ModifyOrderRequest` → `ModifyOrder`; returns 200 `OrderDto` or RFC 9457 409 with current status detail
- [ ] T057 [P] [US5] Implement `DELETE /api/v1/orders/{orderId}` endpoint in `coffeeshop/backend/counter/Endpoints/OrderEndpoints.cs`: maps → `CancelOrder`; returns 200 `CancelOrderResponse` (`{"orderId":…,"status":"cancelled"}`) or RFC 9457 409
- [ ] T058 [US5] Register `ModifyOrder` + `CancelOrder` tools on `CounterAgent` in `coffeeshop/backend/counter/Application/Agents/CounterAgent.cs` (both include Instructions enforcement: refuse modification/cancellation once `preparing`)
- [ ] T059 [US5] Add modification/cancellation `useCopilotAction` handlers in `coffeeshop/frontend/src/app/page.tsx`: `updateOrderSummary` (already exists — reuse for post-modify refresh); `cancelOrder` action updates `currentOrder.status` to `"cancelled"` in `OrderSummaryPanel`

**Checkpoint**: All 5 user stories are independently functional. Full end-to-end customer flow works from identification through order history and modification.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Integration tests, accessibility audit, no-hardcoded-port scan, end-to-end smoke test.

- [ ] T060 [P] Write integration test suite in `coffeeshop/tests/integration/BackendApiTests.cs` using `DistributedApplicationTestingBuilder`: customer lookup (all 3 identifiers + not-found), `GET /api/v1/menu` (all 11 items present including unavailable), `POST /api/v1/orders` → 201 correct status + estimatedPickup, `GET /api/v1/orders/{id}` → 200, `GET /api/v1/customers/{id}/orders` → sorted list
- [ ] T061 [P] Write integration test suite in `coffeeshop/tests/integration/OrderFulfilmentTests.cs`: beverage-only order (BaristaWorker only, "5 min"), food-only order (KitchenWorker only, "10 min"), mixed order (`Task.WhenAll` both workers, "10 min"), concurrent order placement (two simultaneous `POST /api/v1/orders` → unique IDs), worker timeout → order `Cancelled` with correct error message
- [ ] T062 [P] Write frontend Vitest tests in `coffeeshop/frontend/tests/`: `MenuGrid.test.tsx` (all items render, unavailable items show badge, item button has aria-label, keyboard activation), `CustomerPanel.test.tsx` (name + tier display), `OrderSummaryPanel.test.tsx` (status badge aria-live, hidden when no order)
- [ ] T062b [P] Write integration test suite in `coffeeshop/tests/integration/FrontEndTests.cs` using `DistributedApplicationTestingBuilder`: frontend resource reaches `Running` state; `GET /` returns 200; CopilotKit route (`/api/copilotkit`) is reachable from the AppHost test environment
- [ ] T063 Audit WCAG 2.1 AA compliance across all components in `coffeeshop/frontend/src/components/`: verify `aria-label="{displayName} — ${price}"` on MenuGrid items, `aria-live="polite"` on OrderSummaryPanel + StatusBadge, `role="status"` on StatusBadge, `aria-label="Order assistant"` on CopilotKit chat input — fix any gaps
- [ ] T064 Scan all production source files in `coffeeshop/backend/` and `coffeeshop/frontend/src/` for hardcoded `localhost:[0-9]+` patterns — any match is a PR blocker; fix by using Aspire service discovery env vars or `WithReference()`-injected URIs
- [ ] T065 Run `quickstart.md` validation: follow all steps in `coffeeshop/specs/001-coffeeshop-ordering/quickstart.md` from a clean clone — start AppHost, execute all 5 user story scenarios end-to-end, confirm all acceptance scenarios pass

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 → Phase 2 → Phase 3 (US2) → Phase 4 (US1) → Phase 5 (US3)
                                                    → Phase 6 (US4)
                                                    → Phase 7 (US5)
                  ↓ (all phases complete)
                  Phase 8 (Polish)
```

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US2 (Phase 3)**: Depends on Phase 2. Prerequisite for US1 (customer entity needed for PlaceOrder)
- **US1 (Phase 4)**: Depends on Phase 2 + Phase 3 (customer lookup used during order placement)
- **US3, US4, US5 (Phases 5–7)**: Each depends only on Phase 2 (independent of each other; US3/US4/US5 can start in parallel after Phase 2 if not blocked by US1 ordering concerns)
- **Polish (Phase 8)**: Depends on all desired user story phases being complete

### Within Each User Story Phase

1. Tests FIRST — write and confirm FAILING
2. Use cases (application layer) — depends on tests
3. Endpoints — depends on use cases
4. Agent tool registration — depends on use cases
5. Frontend components — depends on endpoint contracts being stable
6. Frontend action wiring — depends on components

### Parallel Opportunities

- T003, T004 (Phase 1): Parallelize after T001/T002
- T007, T008, T009, T010, T011 (Phase 2): All domain entities parallelizable
- T018, T019, T020 (Phase 2): Frontend scaffold tasks parallelizable
- T021, T022 (Phase 3): Both unit tests parallelize
- T027, T028, T029 (Phase 4): All 3 unit tests parallelize
- T033, T034 (Phase 4): BaristaWorker + KitchenWorker parallelize
- T039, T040 (Phase 4): MenuGrid + OrderSummaryPanel parallelize
- T056, T057 (Phase 7): PATCH + DELETE endpoints parallelize
- T060, T061, T062 (Phase 8): All integration/frontend tests parallelize

---

## Parallel Example: User Story 1 (Phase 4)

```
# Write all 3 unit tests in parallel (all different files):
T027: PlaceOrderTests.cs
T028: BaristaWorkerTests.cs
T029: KitchenWorkerTests.cs

# After T030 (product-catalog) + T031 (McpProductCatalogClient) complete, parallelize:
T033: BaristaWorker.cs
T034: KitchenWorker.cs

# After T035 (PlaceOrder use case) completes, parallelize:
T039: MenuGrid.tsx
T040: OrderSummaryPanel.tsx
```

---

## Implementation Strategy

### MVP First (User Stories 2 + 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (**CRITICAL — blocks all stories**)
3. Complete Phase 3: User Story 2 (Customer Identification)
4. Complete Phase 4: User Story 1 (Browse + Place Order)
5. **STOP and VALIDATE**: Full place-order flow with streaming works end-to-end
6. Demo / review at this checkpoint — system delivers core value

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation (buildable skeleton)
2. Phase 3 → Customer Identification testable independently
3. Phase 4 → **MVP** — customer can identify, browse, and place orders with streaming
4. Phase 5 → Add order status queries
5. Phase 6 → Add order history
6. Phase 7 → Add modification / cancellation
7. Phase 8 → Integration coverage, WCAG audit, smoke test sign-off

### Parallel Team Strategy

After Phase 2 completes:

- **Developer A**: Phase 3 (US2) → Phase 4 (US1 — heaviest complexity)
- **Developer B**: Phase 5 (US3) + Phase 6 (US4) — both small, sequential, similar pattern
- **Developer C**: Phase 7 (US5) + Phase 8 (Polish)

---

## Notes

- `[P]` tasks operate on different files with no unmet dependencies — safe to parallelize
- `[US#]` maps each task to its user story for traceability
- All test tasks MUST fail before implementation begins (Constitution III)
- `RunAsync` is banned for order fulfilment — use `RunStreamingAsync` only (FR-020, plan.md)
- No `AddProject<Projects.Barista>` or `AddProject<Projects.Kitchen>` in AppHost — barista/kitchen are `IHostedService` workers inside counter (C1 fix)
- Direct `dict[key] = value` on `InMemoryOrderStore`/`InMemoryCustomerStore` is a PR blocker — use `TryAdd`/`TryUpdate` only
- All inter-service URLs via Aspire service discovery — no hardcoded `localhost:NNNN` permitted in production source
- Commit after each checkpoint or logical group of tasks
