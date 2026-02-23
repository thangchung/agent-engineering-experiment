# Feature Specification: CoffeeShop Ordering System

**Feature Branch**: `001-coffeeshop-ordering`
**Created**: 2026-02-22
**Status**: Ready for Implementation
**Input**: Full coffeeshop ordering system: product catalog browsing, customer lookup, order placement and confirmation, order status tracking, and order history

**Explicitly out of scope**: user authentication / authorization (no login, no tokens, no API keys); customer tier discount or priority logic; real-time kitchen queue depth; persistent storage across restarts.

## Clarifications

### Session 2026-02-22

- Q: How does the user interact with the system — pure chat, hybrid, or structured UI primary? → A: Hybrid — chat sidebar for conversation + a persistent structured page (menu grid, order summary) that updates based on agent state via shared CopilotKit state (`useCopilotReadable` / `useCopilotAction`). The agent drives state changes; the structured page reflects them reactively.
- Q: Which canonical string is used for the order status between "confirmed" and "ready"? → A: `preparing` — aligns spec, API, frontend, and seed data to the value used in `orders.py` `_DEFAULT_ORDERS`. The term `in_preparation` is retired; no mapping layer is needed.
- Q: How is the estimated pickup time calculated? → A: Category-based fixed estimate — beverages-only orders: ≤5 minutes; food-only or mixed (food + beverages) orders: ≤10 minutes. The counter agent communicates this as a plain-language string (e.g., "Ready in about 5 minutes"). No real-time queue depth is required.
- Q: Does customer tier (gold/silver/standard) affect ordering behavior? → A: Display only — tier is shown as a label in the greeting and customer info panel. No discount, priority, or pickup-time logic is applied. Behavioral tier logic is explicitly out of scope.
- Q: Do the counter API endpoints require authentication? → A: No auth — all counter endpoints are unauthenticated. Customer identity is established solely by the in-flow lookup (email / customer ID / order ID). No tokens, API keys, or login screens are required. Auth is explicitly out of scope for this feature.
- Q: Should the spec require real-time streaming status updates during order fulfilment? → A: Yes — add FR requiring the counter to stream intermediate status updates (e.g., "barista is preparing…", "beverages ready, waiting on kitchen…") via AG-UI while barista, kitchen, or product-catalog are processing. The frontend MUST render these as progressive indicators.
- Q: How should the counter handle product-catalog MCP Server unreachability? → A: Fail fast — return a user-facing error ("menu unavailable, try again shortly") and do not place the order. No caching or fallback catalog.
- Q: When can a customer cancel an order? → A: Cancel only before `preparing` — same boundary as modification (FR-010/FR-011). Once workers start processing, cancellation is refused.
- Q: Where are `Others` category items routed? → A: Kitchen — `Others` is treated the same as `Food` for routing and pickup-time purposes.
- Q: What is the maximum quantity of a single item per order? → A: 5 units per item per order.

### Session 2026-02-23

- **CHK052 resolution** — FR-009 governs the pre-confirmation cart-building phase: items can be added, removed, or changed before calling `PlaceOrder`. Post-confirmation item changes (before `preparing`) are also permitted via `PATCH /api/v1/orders/{orderId}`, governed by FR-010/FR-011. Both phases share the same boundary: once `preparing` begins, neither items nor notes can be changed. FR-009 and FR-010 are not in conflict — they describe the same mutability window from two different entry points (UI cart vs. PATCH API).
- **CHK051 resolution** — The AG-UI streaming event `"order ready — all items complete"` and the `OrderStatus` transition to `Ready` in the in-memory store occur atomically. Counter emits the AG-UI event and writes `OrderStatus = Ready` in the same logical operation, after `Task.WhenAll` on both worker reply channels completes. Any client calling `GET /api/v1/orders/{orderId}` after receiving the "order ready" stream event MUST observe `"status": "ready"`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse Menu and Place an Order (Priority: P1)

A customer visits the coffeeshop counter (or app), identifies themselves, browses
the available menu items with prices, selects what they want, reviews an order
summary, and confirms — resulting in a placed and confirmed order.

**Why this priority**: This is the core business transaction. Without the ability
to place an order, the system delivers no value. Every other story depends on an
order existing.

**Independent Test**: A new customer can look themselves up, browse the full
menu, select items, view a summary with a total price, confirm the order, and
receive an order confirmation — without any other story being implemented.

**Acceptance Scenarios**:

1. **Given** a customer provides their email address,
   **When** the system looks them up,
   **Then** the system greets them by first name and presents the available menu options.

2. **Given** the system has displayed available item types with prices on the
   structured menu page (rendered and updated via shared CopilotKit state),
   **When** the customer selects one or more items (via the page or by naming
   them in chat),
   **Then** the structured order summary panel updates reactively to show the
   selected items and total price, and the chat agent asks for confirmation.

3. **Given** the customer has reviewed the order summary,
   **When** the customer confirms the order,
   **Then** the system creates the order, marks it as confirmed, provides the
   customer with their order ID, and communicates an estimated pickup time:
   "Ready in about 5 minutes" for beverage-only orders, or "Ready in about
   10 minutes" for food-only or mixed (food + beverage) orders.

4. **Given** a logged-in customer has chosen items,
   **When** they confirm the order,
   **Then** the order is persisted and retrievable by order ID immediately after.

5. **Given** a customer has confirmed an order containing beverages and food,
   **When** the order is dispatched to barista and kitchen,
   **Then** the frontend receives real-time streaming status updates
   (e.g., "barista is preparing…", "beverages ready, waiting on kitchen…",
   "order ready — all items complete") via AG-UI, and each update is
   rendered as a progressive status indicator within 2 seconds of the
   corresponding processing event.

---

### User Story 2 - Customer Identification (Priority: P1)

A customer must be identified before any personalized action (placing an order,
checking status, viewing history) can take place. The system supports lookup by
email address, customer ID, or an existing order ID.

**Why this priority**: All personalized flows require knowing who the customer is.
Without identification, no order can be placed, checked, or retrieved.

**Independent Test**: A customer can be looked up by email and by ID; an unknown
identifier returns a clear "not found" message with guidance on what to try next.

**Acceptance Scenarios**:

1. **Given** a customer provides a valid email address,
   **When** the system performs a lookup,
   **Then** the customer record (name, ID, tier) is returned and the customer is
   greeted by first name with their tier label displayed (e.g.,
   "Welcome back, Alice ✨ Gold Member").

2. **Given** a customer provides a valid customer ID,
   **When** the system performs a lookup,
   **Then** the same customer record is returned as in scenario 1.

3. **Given** a customer provides an existing order ID,
   **When** the system performs a lookup,
   **Then** the system resolves the associated customer and greets them,
   while also displaying the referenced order details.

4. **Given** a customer provides an unknown or misspelled identifier,
   **When** the system attempts a lookup,
   **Then** the system returns a clear "account not found" message and prompts
   the customer to provide another identifier.

5. **Given** a customer provides no identifier at all,
   **When** the system processes the request,
   **Then** the system politely asks for an email address or order number before
   proceeding.

---

### User Story 3 - Check Order Status (Priority: P2)

A customer who has placed an order wants to know the current status of that
order — whether it is confirmed, being prepared, ready for pickup, or completed.

**Why this priority**: After placing an order, status visibility is the most
common follow-up action. It reduces customer anxiety and unnecessary counter
interruptions.

**Independent Test**: An identified customer with an existing order can retrieve
its current status at any time and receive a human-readable status description.

**Acceptance Scenarios**:

1. **Given** an identified customer provides a valid order ID,
   **When** the system retrieves the order,
   **Then** the current status (e.g., "confirmed", "preparing", "ready",
   "completed") is displayed in plain language.

2. **Given** an identified customer asks about their most recent order without
   providing an order ID,
   **When** the system resolves the most recent order for that customer,
   **Then** the status of the latest order is displayed.

3. **Given** a customer queries a non-existent order ID,
   **When** the system attempts retrieval,
   **Then** the system returns a clear "order not found" message.

---

### User Story 4 - View Order History (Priority: P2)

A returning customer wants to see all their past orders so they can reorder
familiar items or track their spending.

**Why this priority**: Repeat customers are the backbone of a coffeeshop
business. Easy access to history reduces friction for reorders and builds loyalty.

**Independent Test**: An identified customer with at least two past orders can
retrieve a list of all their orders, each showing the order ID, items ordered,
total, status, and date.

**Acceptance Scenarios**:

1. **Given** an identified customer with at least one past order,
   **When** they request their order history,
   **Then** the system returns a list of all orders, each showing order ID,
   items, total price, status, and date — sorted most-recent first.

2. **Given** an identified customer with no prior orders,
   **When** they request order history,
   **Then** the system returns an empty history with a friendly message
   (e.g., "No orders yet — let's fix that!").

3. **Given** a customer views their history,
   **When** they select a past order,
   **Then** the full order details are displayed (same as order status view).

---

### User Story 5 - Modify an Order or Add Special Instructions (Priority: P3)

Before an order moves to preparation, a customer can update it: changing the
item selection, adding a note (e.g., "oat milk", "extra shot"), or cancelling.

**Why this priority**: Supports a higher-quality customer experience and reduces
waste from incorrectly prepared drinks, but is non-blocking for the initial
release.

**Independent Test**: A customer with a recently confirmed order (not yet in
preparation) can add a note to the order and have that note reflected when the
order details are retrieved.

**Acceptance Scenarios**:

1. **Given** an identified customer with a confirmed order that is not yet in
   preparation,
   **When** they submit a note or special instruction,
   **Then** the order is updated with that note and the customer receives
   confirmation that the update was applied.

2. **Given** an order that is already `preparing` or further along,
   **When** a customer attempts to modify it,
   **Then** the system informs them that the order can no longer be changed
   and explains the current status.

3. **Given** a customer modifies the item list before confirmation,
   **When** they reconfirm,
   **Then** the updated summary reflects the new items and total.

4. **Given** an identified customer with a confirmed order not yet in
   preparation,
   **When** they request cancellation,
   **Then** the order status is set to `cancelled`, and the customer receives
   confirmation that the order has been cancelled.

5. **Given** an order that is already `preparing` or further along,
   **When** a customer attempts to cancel it,
   **Then** the system informs them that the order can no longer be cancelled
   and explains the current status.

---

### Edge Cases

- Customer provides both email and customer ID that belong to different accounts —
  system uses the first provided identifier and does not merge accounts.
- A customer orders a mix of beverages and food items — system applies the
  longer (10-minute) pickup estimate.
- Customer places an order with zero items — system rejects with validation
  message before confirming.
- Two customers attempt to place an order simultaneously — both orders are
  accepted and assigned unique order IDs.
- A menu item becomes unavailable between the time the customer selects it and
  when they confirm — system rejects the confirmation with a clear "item no
  longer available" message and prompts re-selection.
- Customer provides a negative quantity or quantity exceeding a per-item limit
  (max 5) — system validates and rejects with guidance.
- Network error mid-order: the system either completes or fully rolls back the
  order; a partial order state is never persisted.
- Product-catalog MCP Server is down or times out — counter returns a clear
  "menu unavailable" error and does not create the order; the customer is
  prompted to retry.
- Customer attempts to cancel an order that is already `preparing` — system
  refuses cancellation and shows the current status.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow a customer to identify themselves using their
  email address, customer ID, or an existing order ID.
- **FR-002**: System MUST greet an identified customer by their first name.
- **FR-003**: System MUST return a clear "account not found" message and re-prompt
  when a customer provides an unrecognized identifier.
- **FR-004**: System MUST display all menu item types with their prices and
  availability status when a customer requests the menu. Unavailable items MUST
  appear in the listing (with a visual indicator) and MUST NOT be silently filtered.
- **FR-005**: System MUST present a complete order summary (items selected, unit
  prices, total) for customer review before an order is confirmed.
- **FR-006**: System MUST create an order and return a unique order ID when a
  customer confirms their order.
- **FR-007**: System MUST mark a newly created order as "confirmed" immediately
  upon successful order creation.
- **FR-008**: System MUST provide an estimated pickup time when an order is
  confirmed, calculated as follows:
  - Beverage-only order (all items in the `Beverages` category): **≤5 minutes**
    (communicated as "Ready in about 5 minutes").
  - Food-only or mixed order (at least one item in the `Food` or `Others`
    category): **≤10 minutes** (communicated as "Ready in about 10 minutes").
  The estimate is a fixed, category-based string — no real-time queue depth
  is required.
- **FR-009**: System MUST allow a customer to add, remove, or change items in
  their order before confirming.
- **FR-010**: System MUST allow a customer to add a text note or special
  instruction to a confirmed order that has not yet reached `preparing` status.
- **FR-011**: System MUST prevent modification of an order that has reached
  `preparing`, `ready`, `completed`, or `cancelled` status, and MUST inform
  the customer of the current order status.
- **FR-012**: System MUST display the current status of any order when queried
  by an identified customer.
- **FR-013**: System MUST return a customer's full order history, sorted
  most-recent first, when requested.
- **FR-014**: System MUST return a friendly empty-state message when a customer
  with no prior orders requests their history.
- **FR-015**: System MUST reject an order with no items selected, returning a
  validation message to the customer.
- **FR-016**: System MUST reject a confirmation if a selected item is no longer
  available, and MUST prompt the customer to revise their selection.
- **FR-017**: The frontend MUST implement a hybrid interaction model: a
  persistent structured page (menu grid, order summary panel) that updates
  reactively via shared CopilotKit state, alongside a CopilotKit chat sidebar
  for all conversational interaction. The agent drives state changes via
  `useCopilotAction`; the page reflects them via `useCopilotReadable`. Neither
  the chat nor the page alone is sufficient — both MUST be present.
- **FR-018**: System MUST display the customer's tier label (gold, silver, or
  standard) in the greeting message and in the customer info panel. Tier MUST
  NOT affect pricing, pickup time, or any other business logic.
- **FR-019**: All counter API endpoints MUST be unauthenticated. Customer
  identity is established by the in-conversation lookup flow (email address,
  customer ID, or order ID). No bearer tokens, API keys, or session cookies
  are required or accepted.
- **FR-020**: During order fulfilment the counter MUST stream intermediate
  status updates to the frontend via the AG-UI protocol in real time. Updates
  MUST be emitted at each processing boundary:
  (a) product-catalog lookup started ("looking up menu…"),
  (b) items dispatched to barista and/or kitchen ("barista is preparing your
  latte…"),
  (c) each individual worker completion as it arrives ("beverages ready,
  waiting on kitchen…"),
  (d) final aggregated result ("order ready — all items complete").
  The frontend MUST render these updates as progressive status indicators
  (inline chat messages or an order-card status badge). The non-streaming
  path (`RunAsync`) is NOT permitted for order fulfilment.
- **FR-021**: If the product-catalog MCP Server is unreachable or returns an
  error, the counter MUST fail fast: return a user-facing error message
  (e.g., "Menu is currently unavailable — please try again shortly") and
  MUST NOT place or confirm the order. No cached or fallback catalog data
  is served.
- **FR-022**: A customer MUST be able to cancel an order that has not yet
  reached `preparing` status. Cancellation sets the order status to
  `cancelled`. Once the order is `preparing` or further along, cancellation
  MUST be refused with a message explaining the current status. This follows
  the same mutability boundary as modification (FR-010/FR-011).
- **FR-023**: The counter MUST route order line items by product category:
  `Beverages` → barista; `Food` and `Others` → kitchen. The `Others`
  category is treated identically to `Food` for both routing and
  pickup-time estimation.
- **FR-024**: The maximum quantity of any single item in one order is **5**.
  The system MUST reject quantities ≤0 or >5 with a validation message
  (e.g., "You can order between 1 and 5 of each item").

### Key Entities

- **Customer**: A registered person who can place, check, or history orders.
  Key attributes: customer ID, first name, email address, phone, tier
  (`gold` | `silver` | `standard`). Tier is display-only; it carries no
  discount or priority behaviour.

- **Item Type**: A category of product on the menu (e.g., Espresso, Latte,
  Cappuccino). Key attributes: item type ID, name.

- **Menu Item**: A specific priced variant of an item type available for ordering.
  Key attributes: item type reference, price, availability status.

- **Order**: A request placed by a customer for one or more menu items.
  Key attributes: order ID, customer reference, list of ordered items,
  total price, status, special instructions/notes, timestamp.

- **Order Status**: The lifecycle state of an order.
  Permitted values: `pending` → `confirmed` → `preparing` → `ready` → `completed` | `cancelled`.
  (formerly referred to as `in_preparation`; that term is retired across all layers)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An identified customer can browse the menu, select items, and
  receive an order confirmation within 3 minutes of starting the interaction.
- **SC-002**: 100% of confirmed orders are assigned a unique, retrievable order
  ID that is available immediately after confirmation.
- **SC-003**: An identified customer can retrieve their current order status
  within 5 seconds of requesting it.
- **SC-004**: An identified customer can retrieve their full order history within
  5 seconds of requesting it.
- **SC-005**: Unrecognized customer identifiers receive a clear error message
  100% of the time — no silent failures or blank responses.
- **SC-006**: Attempts to confirm an order with unavailable items are rejected
  100% of the time, with a user-actionable message.
- **SC-007**: Order modifications submitted before preparation begins are
  reflected in the order details within 2 seconds.
- **SC-008**: The system maintains consistent order data integrity — a partial
  order state is never persisted after a failed confirmation.
