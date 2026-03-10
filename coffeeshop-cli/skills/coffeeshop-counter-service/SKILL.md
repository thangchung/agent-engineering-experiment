---
name: coffeeshop-counter-service
description: >
  Handle coffee shop counter requests end-to-end with structured CLI output:
  identify the customer, classify intent, build an order, and submit the
  final payload.
license: MIT
compatibility: >
  Requires coffeeshop-cli with --json support. The CLI proxies to upstream
  Python MCP servers (orders, product_catalogs) for data.
metadata:
  author: agent-skills-demo
  version: "3.3"
  category: operations
  loop-type: agentic
---

# Coffee Shop Order Submission Skill

Use this skill when a user wants to place or review a coffee order from chat.

## Agent Defaults

1. Always use commands with `--json` and reason over the JSON response.
2. Prefer specific calls over broad calls.
3. Keep each call focused on one intent transition.
4. If data is missing, ask a targeted clarification question before continuing.

## Setup

This skill uses coffeeshop-cli commands with `--json` output. All commands
are invoked using the Coffeeshop-Cli command.

## Command Reference

**Customer & Account:**
- `Coffeeshop-Cli models query Customer --email <email> --json` — lookup customer by email
- `Coffeeshop-Cli models query Customer --customer-id <id> --json` — lookup customer by ID
- `Coffeeshop-Cli models browse Customer --json` — list all customers

**Product Catalog:**
- `Coffeeshop-Cli models browse MenuItem --json` — list all menu items with prices

**Orders:**
- `Coffeeshop-Cli models submit Order --json` — create new order
  Input: `{"customer_id":"C-1001","items":[{"item_type":"LATTE","qty":2}]}`
  Returns: Complete order object with generated OrderId, Status=Pending, and calculated Total

## Structured Output

Use these response shapes during reasoning:

```json
{ "ok": true, "customer": { "customer_id": "C-1001", "name": "Alice" } }
```

```json
{ "ok": true, "items": [{ "item_type": "LATTE", "price": 4.5 }] }
```

```json
{ "customer_id": "C-1001", "items": [{ "name": "Latte", "qty": 2 }], "total": 9.0 }
```

## Agentic Loop

### Internal State

| Variable | Initial | Description |
|----------|---------|-------------|
| CUSTOMER | null | Customer record from lookup |
| INTENT | null | order-status / account / item-types / process-order |
| ORDER | null | Order being built or looked up |

### STEP 1 — INTAKE

Goal: Greet the customer and identify who they are.

1. Extract identifiers from the customer's message (email, customer_id, order_id).
2. IF email or customer_id provided:
  - Call `Coffeeshop-Cli models query Customer --email <email> --json` or `Coffeeshop-Cli models query Customer --customer-id <id> --json`
   - Store result as CUSTOMER.
3. IF only order_id provided:
  - Ask user for customer email or customer_id because order query is not exposed.
4. IF no identifier: ask the customer, wait, repeat step 1.
5. IF lookup returns `ok: false`: ask customer to try again.
6. Greet: "Hi {CUSTOMER.Name}, I have your account open. How can I help today?"

GOTO → STEP 2

### STEP 2 — CLASSIFY INTENT

Goal: Determine what the customer needs.

1. Classify into: order-status | account | item-types | process-order
2. Store in INTENT.

Route:
- **order-status**: Explain that direct order lookup is limited; ask for current status context and continue.
- **account**: Display CUSTOMER details. GOTO → STEP 2 (loop).
- **item-types**: Call `Coffeeshop-Cli models browse MenuItem --json`, then display menu. GOTO → STEP 2 (loop).
- **process-order**: GOTO → STEP 3.

### STEP 3 — REVIEW & CONFIRM ORDER

Goal: Build, price, and confirm the order.

1. Extract items + quantities from conversation.
2. Call `Coffeeshop-Cli models browse MenuItem --json` and get prices for selected items.
3. Calculate total = sum(price × qty).
4. Display summary:
   ```
  Order Summary
   Items:
   - 2x Latte — $4.50 each
   - 1x Croissant — $3.25 each
   Total: $12.25
   Does this look correct?
   ```
5. IF confirmed → store ORDER, GOTO STEP 4.
6. IF rejected → ask what to change, GOTO STEP 2.

### STEP 4 — FINALIZE ORDER

Goal: Create order and confirm.

1. Call: `Coffeeshop-Cli models submit Order --json`
   Body: `{"customer_id":"<CUSTOMER.CustomerId>","items":[{"item_type":"LATTE","qty":2}]}`
   Note: OrderId is auto-generated. Prices and total are auto-calculated. Status is set to Pending.
   Store result ORDER (includes OrderId, Total, Status).
2. Display: "Order {ORDER.OrderId} created. Total: ${ORDER.Total}. Estimated pickup: 5-10 minutes for drinks, 10-15 minutes with food."

GOTO → END

## Error Handling

- If customer lookup fails: ask for a corrected email or customer ID.
- If menu item is unknown: show closest matching menu options.
- If submit fails: restate the payload summary and retry once.

## Safety Notes

- Never invent unavailable item types.
- Never submit an order until the user confirms the summary.
- Keep all model calls JSON-first.
