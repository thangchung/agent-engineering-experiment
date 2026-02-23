# Contract: Menu / Product Catalog

**FR-004**
**Endpoint**: `GET /api/v1/menu`

---

## Request

```
GET /api/v1/menu
```

No request body. No authentication required.

---

## Responses

### 200 OK — Catalog available

```json
{
  "items": [
    {
      "type": "CAPPUCCINO",
      "displayName": "CAPPUCCINO",
      "category": "Beverages",
      "price": 4.50,
      "isAvailable": true
    },
    {
      "type": "LATTE",
      "displayName": "LATTE",
      "category": "Beverages",
      "price": 4.50,
      "isAvailable": true
    },
    {
      "type": "CAKEPOP",
      "displayName": "CAKEPOP",
      "category": "Food",
      "price": 2.50,
      "isAvailable": true
    },
    {
      "type": "CHICKEN_MEATBALLS",
      "displayName": "CHICKEN MEATBALLS",
      "category": "Others",
      "price": 4.25,
      "isAvailable": true
    }
  ]
}
```

```csharp
public record MenuResponse(IReadOnlyList<MenuItemDto> Items);

public record MenuItemDto(
    string  Type,         // ItemType enum name
    string  DisplayName,
    string  Category,     // "Beverages" | "Food" | "Others"
    decimal Price,
    bool    IsAvailable
);
```

### 503 Service Unavailable — product-catalog MCP unreachable (FR-021)

```json
{
  "type": "https://coffeeshop.local/errors/service-unavailable",
  "title": "Menu unavailable",
  "status": 503,
  "detail": "Menu is currently unavailable — please try again shortly."
}
```

---

## Implementation Notes

- Counter acts as MCP **client**; product-catalog is the MCP **server**.
- Counter URL for product-catalog resolved via Aspire service discovery.
- On MCP call failure (timeout, connection refused, non-success status): fail fast — return 503. No caching, no fallback (FR-021).
- The menu is surfaced on the structured page via `useCopilotReadable` in the frontend. The menu grid renders `isAvailable: true` items only.
