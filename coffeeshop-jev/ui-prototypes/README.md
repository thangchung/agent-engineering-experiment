# WebApp click-through prototype

The design of record for `WebApp/` (built; `jev_routing_research.md` §17). Open `index.html` in a browser. There's no build step and no server: every response is simulated in `app.js`. Keep this folder, the built `WebApp/`, and §17 in step: a UX change lands in all three, and `WebApp/acceptance/` is the executable version of the table below.

## What to click

| Flow | How | WebApp piece it mocks | Acceptance |
|---|---|---|---|
| Submit an order (chat) | Order tab → type, or tap "2 lattes and a croissant" | `CoffeeShopAgent` run; the ticket on the right is the `order_progress` `STATE_SNAPSHOT` | A1 |
| Clarify as a chat turn | "something sweet", then answer in the next message | §17.3: clarify ends the run, no `RequestPort` wait | A2 |
| Off-topic | "what's the weather today?" | `Rejected(OffTopic)`; shows as "Not placed" | A3 |
| Show all products | Menu tab, or ask "what do you have?" in chat | `show_menu` tool result → inline menu card | A4 |
| Submit an order (menu) | Menu tab → + / − on items → Place order | same run; the cart is sent as the message "2 × Latte, 1 × Muffin" | A4 |
| Order history | "Your orders" tab (Mine / Everyone today, status filters), or ask "what did I order before?" | §17.4 button path (`GET /orders?customerId`) and chat path (`order_history` intent → `show_history` card) | A5 |
| Empty history | guest chip → Start as a new guest | empty-history template | A5 |
| Ask cap | send an unclear message three times | `IntentPolicy` MaxAsks = 2 → not placed | A6 |
| **Order details** | Your orders → click `#6` (or "Order details, audit log and AG-UI events"), or open `#/orders/6` | §17.9: `GET /orders/{number}`, the page `OrderDetail.razor` | A8 |
| Decision model offline | guest chip → Simulate decision model offline, then send anything | G4 fail-closed at the Gate: refused as `DecisionModelDown`, with a `gate-error` audit row | (unit test `AguiEndpointTests`) |
| Protocol view | guest chip → Show AG-UI events | the live event stream the browser receives | A7 |
| Suggestion chips | always visible above the input, also after the first message | `Order.razor` suggestions | A2 |
| Light / dark | sun/moon button left of the guest chip | follows the system theme until you pick one; your pick is remembered | A7 |

### Order details page (`#/orders/{n}`)

- **Header:** order number, status pill, time, customer id, decision model `provider / model`.
- **Receipt:** lines with flags, total, barista and kitchen tickets with steps, the reply, the reject reason, the conversation that led to the order.
- **Audit log** (what the counter decided, with time offsets): `message`, `provider`, `gate` (decision, intent, confidence, on-menu score, model), `split`, `ticket`, `result`, plus `clarify` / `gate-error` when they happened.
- **AG-UI events** (what the front door streamed): `RUN_STARTED`, `STATE_SNAPSHOT`, `TEXT_MESSAGE_CONTENT`, `TOOL_CALL_RESULT`, `RUN_FINISHED`.
- **Raw record (JSON)** and a "no audit log recorded" notice for orders without a trace (the seed orders here; legacy `POST /orders` orders in the app).
- Unknown number: "There is no order #n. It may be from before the last restart."

## Not real (prototype only)

- The decision model and the LLM are keyword rules in `gate()` / `extract()`. Confidence numbers are fabricated but stable per message. The rules reproduce every `expect.gate` + `expect.lines` in `tests/CoffeeShop.Evals/golden/orders.jsonl` (22/22, checked in Node), plus G30/G32 (G30-G32 are proposals, not in the file yet).
- The injection refusal ("ignore previous instructions…") mocks the proposed JT4 guard. The real app doesn't have it.
- Orders, traces and the guest id live in this browser's `localStorage` (in memory when the browser blocks it). Seed data: 2 orders for you and 3 for other guests, without traces.
- Provider and model are fixed to `prototype / keyword rules`. The real app takes them from the active provider profile (§18).

## Known differences from the built WebApp

| Area | Prototype | WebApp |
|---|---|---|
| Chat widget | hand-rolled markup | `Microsoft.AspNetCore.Components.AI` (`MessageList`, `MessageInput`), themed with the same tokens |
| Guest id | `guest-` + 8 hex | same |
| Ticket step texts | "Decision model: place_order 0.93, on menu 0.95" | same, from the real gate answer |
| Replies | friendly sentence with item names | the LLM's text, which may show menu ids such as `CHICKEN_MEATBALLS` (JT5 would fix) |
| Audit/AG-UI log | recorded for every finished order, also the refused ones | same, kept in memory by `OrderStore` |
| Simulate offline toggle | present | not built (provider failures show the same refusal) |
