# API Contracts: CoffeeShop Counter

**Date**: 2026-02-23
**Base URL**: Resolved via Aspire service discovery (no hard-coded `localhost` ports)
**Version**: `/api/v1/`
**Auth**: None (FR-019)
**Errors**: RFC 9457 Problem Details (`application/problem+json`)
**Serialization**: `System.Text.Json` (camelCase)

---

## Index

| File | Endpoints |
|------|-----------|
| [customers.md](customers.md) | `POST /api/v1/customers/lookup` |
| [menu.md](menu.md) | `GET /api/v1/menu` |
| [orders.md](orders.md) | `POST /api/v1/orders`, `GET /api/v1/orders/{orderId}`, `PATCH /api/v1/orders/{orderId}`, `DELETE /api/v1/orders/{orderId}`, `GET /api/v1/customers/{customerId}/orders`, `POST /api/v1/copilotkit` |

---

## Common Response Envelopes

### Success (200 / 201)
Response body is the typed record directly (no wrapper).

### Problem Details (4xx / 5xx)
```json
{
  "type": "https://coffeeshop.local/errors/not-found",
  "title": "Customer not found",
  "status": 404,
  "detail": "No customer found for identifier 'alice@nowhere.com'.",
  "correlationId": "a1b2c3d4"
}
```

### Common `type` values
| Type | Status |
|------|--------|
| `…/errors/not-found` | 404 |
| `…/errors/validation` | 400 |
| `…/errors/conflict` | 409 |
| `…/errors/service-unavailable` | 503 |
