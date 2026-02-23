# Contract: Customer Lookup

**FR-001, FR-002, FR-003**
**Endpoint**: `POST /api/v1/customers/lookup`

---

## Request

```
POST /api/v1/customers/lookup
Content-Type: application/json
```

```json
{
  "identifier": "alice@example.com"
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `identifier` | `string` | yes | Email address, customer ID (`C-XXXX`), or order ID (`ORD-XXXX`) |

### C# Request Record
```csharp
public record CustomerLookupRequest(string Identifier);
```

---

## Responses

### 200 OK — Customer found

```json
{
  "customerId": "C-1001",
  "firstName": "Alice",
  "lastName": "Johnson",
  "email": "alice@example.com",
  "phone": "+1-555-0101",
  "tier": "gold",
  "greeting": "Welcome back, Alice ✨ Gold Member"
}
```

```csharp
public record CustomerLookupResponse(
    string CustomerId,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    string Tier,       // "gold" | "silver" | "standard"
    string Greeting    // FR-002: first name + tier label
);
```

### 404 Not Found — Identifier not recognized (FR-003)

```json
{
  "type": "https://coffeeshop.local/errors/not-found",
  "title": "Customer not found",
  "status": 404,
  "detail": "No account found for 'alice@nowhere.com'. Please provide your email address, customer ID, or an existing order number."
}
```

### 400 Bad Request — Missing/blank identifier

```json
{
  "type": "https://coffeeshop.local/errors/validation",
  "title": "Identifier required",
  "status": 400,
  "detail": "Please provide your email address, customer ID (C-XXXX), or an order number (ORD-XXXX)."
}
```

---

## Lookup Resolution Order

1. Try parse as order ID (`ORD-XXXX`) → resolve customer via order
2. Try parse as customer ID (`C-XXXX`) → direct lookup
3. Treat as email address (case-insensitive) → lookup by email
4. None match → 404

---

## Frontend Usage

Called by the CopilotKit AG-UI backend when the user provides their identifier
in the chat. The `greeting` field is surfaced as the first chat message.
The `tier` and `customerId` are stored in CopilotKit shared state
(`useCopilotReadable`) for display in the customer info panel.
