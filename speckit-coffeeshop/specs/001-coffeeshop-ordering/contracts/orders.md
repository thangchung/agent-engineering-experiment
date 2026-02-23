# Contract: Orders

**FR-005 through FR-016, FR-020 through FR-024**
**Endpoints**:
- `POST /api/v1/orders` — place order
- `GET /api/v1/orders/{orderId}` — get order status
- `PATCH /api/v1/orders/{orderId}` — modify order (note or items)
- `DELETE /api/v1/orders/{orderId}` — cancel order
- `GET /api/v1/customers/{customerId}/orders` — order history
- `POST /api/v1/copilotkit` — AG-UI streaming endpoint (CopilotKit)

---

## Shared Types

```csharp
public record OrderItemRequest(
    string Type,       // ItemType enum name (e.g., "LATTE")
    int    Quantity    // 1–5 (FR-024)
);

public record OrderItemDto(
    string  Type,
    string  DisplayName,
    int     Quantity,
    decimal UnitPrice,
    decimal LineTotal
);

public record OrderDto(
    string                    Id,
    string                    CustomerId,
    IReadOnlyList<OrderItemDto> Items,
    decimal                   TotalPrice,
    string                    Status,        // "pending" | "confirmed" | "preparing" | "ready" | "completed" | "cancelled"
    string?                   Notes,
    string?                   EstimatedPickup,  // present on Confirmed
    DateTimeOffset            CreatedAt,
    DateTimeOffset            UpdatedAt
);
```

---

## POST /api/v1/orders — Place Order (FR-005, FR-006, FR-007, FR-008, FR-015, FR-016, FR-020, FR-023, FR-024)

### Request
```json
{
  "customerId": "C-1001",
  "items": [
    { "type": "LATTE", "quantity": 2 },
    { "type": "CROISSANT", "quantity": 1 }
  ],
  "notes": "Extra hot, please"
}
```

```csharp
public record PlaceOrderRequest(
    string                     CustomerId,
    IReadOnlyList<OrderItemRequest> Items,
    string?                    Notes
);
```

### 201 Created — Order confirmed + streaming initiated

```json
{
  "orderId": "ORD-7342",
  "customerId": "C-1001",
  "items": [
    { "type": "LATTE", "displayName": "LATTE", "quantity": 2, "unitPrice": 4.50, "lineTotal": 9.00 },
    { "type": "CROISSANT", "displayName": "CROISSANT", "quantity": 1, "unitPrice": 3.25, "lineTotal": 3.25 }
  ],
  "totalPrice": 12.25,
  "status": "confirmed",
  "notes": "Extra hot, please",
  "estimatedPickup": "Ready in about 10 minutes",
  "createdAt": "2026-02-23T10:15:00Z",
  "updatedAt": "2026-02-23T10:15:00Z"
}
```

> For the **agentic fulfilment path** (via `/api/v1/copilotkit`), the counter
> uses `RunStreamingAsync`. AG-UI events are streamed before the final 201
> response. See [Streaming section](#streaming-via-copilotkit) below.

### 400 Bad Request — No items (FR-015)
```json
{
  "type": "https://coffeeshop.local/errors/validation",
  "title": "Order requires at least one item",
  "status": 400,
  "detail": "Please add at least one menu item before confirming."
}
```

### 400 Bad Request — Invalid quantity (FR-024)
```json
{
  "type": "https://coffeeshop.local/errors/validation",
  "title": "Invalid quantity",
  "status": 400,
  "detail": "You can order between 1 and 5 of each item. 'LATTE' quantity 7 is not allowed."
}
```

### 409 Conflict — Item unavailable (FR-016)
```json
{
  "type": "https://coffeeshop.local/errors/conflict",
  "title": "Item unavailable",
  "status": 409,
  "detail": "MUFFIN is no longer available. Please revise your selection."
}
```

### 503 Service Unavailable — product-catalog unreachable (FR-021)
```json
{
  "type": "https://coffeeshop.local/errors/service-unavailable",
  "title": "Menu unavailable",
  "status": 503,
  "detail": "Menu is currently unavailable — please try again shortly."
}
```

---

## GET /api/v1/orders/{orderId} — Order Status (FR-012)

### 200 OK
```json
{
  "id": "ORD-7342",
  "customerId": "C-1001",
  "items": [ ... ],
  "totalPrice": 12.25,
  "status": "preparing",
  "notes": "Extra hot, please",
  "estimatedPickup": "Ready in about 10 minutes",
  "createdAt": "2026-02-23T10:15:00Z",
  "updatedAt": "2026-02-23T10:16:30Z"
}
```

### 404 Not Found
```json
{
  "type": "https://coffeeshop.local/errors/not-found",
  "title": "Order not found",
  "status": 404,
  "detail": "No order found with ID 'ORD-9999'."
}
```

---

## PATCH /api/v1/orders/{orderId} — Modify Order (FR-009, FR-010, FR-011)

Modify notes or item list before the order reaches `preparing`.

### Request
```json
{
  "notes": "Oat milk, no sugar",
  "items": [
    { "type": "LATTE", "quantity": 1 },
    { "type": "CROISSANT", "quantity": 2 }
  ]
}
```

```csharp
public record ModifyOrderRequest(
    string?                    Notes,
    IReadOnlyList<OrderItemRequest>? Items  // null = don't change items
);
```

### 200 OK — Updated order
Returns full `OrderDto`.

### 409 Conflict — Order no longer modifiable (FR-011)
```json
{
  "type": "https://coffeeshop.local/errors/conflict",
  "title": "Order cannot be modified",
  "status": 409,
  "detail": "Order ORD-7342 is already 'preparing' and can no longer be changed."
}
```

---

## DELETE /api/v1/orders/{orderId} — Cancel Order (FR-022)

### 200 OK
```json
{ "orderId": "ORD-7342", "status": "cancelled" }
```

```csharp
public record CancelOrderResponse(string OrderId, string Status);
```

### 409 Conflict — Order already in preparation or beyond (FR-022)
```json
{
  "type": "https://coffeeshop.local/errors/conflict",
  "title": "Order cannot be cancelled",
  "status": 409,
  "detail": "Order ORD-7342 is already 'preparing' and can no longer be cancelled."
}
```

---

## GET /api/v1/customers/{customerId}/orders — Order History (FR-013, FR-014)

### 200 OK — Has orders
```json
{
  "customerId": "C-1001",
  "orders": [
    {
      "id": "ORD-7342",
      "items": [ ... ],
      "totalPrice": 12.25,
      "status": "completed",
      "createdAt": "2026-02-23T10:15:00Z",
      "updatedAt": "2026-02-23T10:20:00Z"
    }
  ]
}
```
Sorted most-recent first.

```csharp
public record OrderHistoryResponse(
    string CustomerId,
    IReadOnlyList<OrderDto> Orders   // sorted most-recent first
);
```

### 200 OK — No orders (FR-014)
```json
{
  "customerId": "C-1003",
  "orders": []
}
```
Frontend renders: "No orders yet — let's fix that!"

---

## POST /api/v1/copilotkit — Streaming AG-UI Endpoint (FR-017, FR-020)

This is the CopilotKit AG-UI integration route.
Implemented in `frontend/src/app/api/copilotkit/route.ts` and proxied by Next.js to the counter.

### Protocol
- **Content-Type (request)**: `application/json` (CopilotKit runtime format)
- **Content-Type (response)**: `text/event-stream` (Server-Sent Events)
- **Streaming events** emitted by counter via `RunStreamingAsync`:

| Event Key | Payload | Trigger |
|-----------|---------|---------|
| `status` | `"looking up product catalog…"` | (a) MCP call initiated |
| `status` | `"barista is preparing your [item]…"` | (b) dispatched to barista |
| `status` | `"kitchen is preparing your [item]…"` | (b) dispatched to kitchen |
| `status` | `"barista & kitchen preparing your order…"` | (b) dispatched both (mixed) |
| `status` | `"beverages ready, waiting on kitchen…"` | (c) barista done; kitchen still running |
| `status` | `"kitchen ready, waiting on barista…"` | (c) kitchen done; barista still running |
| `status` | `"order ready — all items complete"` | (d) all workers done |
| `message` | Final agent response text | End of stream |

Frontend renders `status` events as progressive status indicators (inline
chat messages or order-card badge). Each update MUST arrive within 2 seconds
of the corresponding processing event (SC acceptance scenario 5).

> **Rule**: `RunAsync` is NOT permitted for order fulfilment (FR-020, Constitution).
