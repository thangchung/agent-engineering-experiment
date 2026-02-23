# Data Model: CoffeeShop Ordering System

**Date**: 2026-02-23
**Spec**: [spec.md](spec.md)
**Storage**: In-memory only — `ConcurrentDictionary` / `IReadOnlyList<T>` singletons. No persistence.

---

## Enums

### `CustomerTier`
```csharp
public enum CustomerTier { Gold, Silver, Standard }
```
Display-only. No discount or priority logic.

### `ItemCategory`
```csharp
public enum ItemCategory { Beverages, Food, Others }
```
Routing rules: `Beverages` → barista; `Food` and `Others` → kitchen.

### `ItemType`
```csharp
public enum ItemType
{
    CAPPUCCINO, COFFEE_BLACK, COFFEE_WITH_ROOM,
    ESPRESSO, ESPRESSO_DOUBLE, LATTE,             // Beverages
    CAKEPOP, CROISSANT, MUFFIN, CROISSANT_CHOCOLATE, // Food
    CHICKEN_MEATBALLS                             // Others
}
```

### `OrderStatus`
```csharp
public enum OrderStatus { Pending, Confirmed, Preparing, Ready, Completed, Cancelled }
```
Lifecycle (valid transitions only):
```
Pending → Confirmed → Preparing → Ready → Completed
                    ↘ Cancelled  (also reachable from Pending and Confirmed)
```
- Modification allowed: `Pending` and `Confirmed` only (before `Preparing`)
- Cancellation allowed: `Pending` and `Confirmed` only (FR-022)
- Term `in_preparation` is **retired** — use `Preparing` everywhere

---

## Entities

### `Customer`
```csharp
public record Customer(
    string   Id,           // Format: "C-XXXX" (e.g., "C-1001")
    string   FirstName,
    string   LastName,
    string   Email,
    string   Phone,
    CustomerTier Tier,
    DateOnly AccountCreated
);
```
**Lookup keys**: `Id`, `Email` (case-insensitive). Also resolvable via `Order.CustomerId`.

### `MenuItem`
```csharp
public record MenuItem(
    ItemType     Type,
    string       DisplayName,
    ItemCategory Category,
    decimal      Price,
    bool         IsAvailable
);
```
Reference data — immutable at runtime. Exposed by product-catalog MCP Server.

### `OrderItem`
```csharp
public record OrderItem(
    ItemType Type,
    string   DisplayName,
    int      Quantity,     // 1–5 (FR-024)
    decimal  UnitPrice,
    decimal  LineTotal     // = UnitPrice * Quantity
);
```
**Validation**: `Quantity` must be ≥1 and ≤5.

### `Order`
```csharp
public record Order(
    string          Id,              // Format: "ORD-XXXX" (runtime: ORD-{6000–9999})
    string          CustomerId,
    IReadOnlyList<OrderItem> Items,
    decimal         TotalPrice,
    OrderStatus     Status,
    string?         Notes,           // Special instructions; nullable
    string?         EstimatedPickup, // Set at Confirmed; "Ready in about N minutes"; null before confirmation
    DateTimeOffset  CreatedAt,
    DateTimeOffset  UpdatedAt
);
```
**Invariants**:
- `Items` must be non-empty on confirmation (FR-015)
- All `Items` must be available at confirmation time (FR-016)
- `TotalPrice` = sum of all `LineTotal` values
- `Id` is unique across all orders in the store

---

## State Transitions (Valid Only)

| From → To | Trigger | Guard |
|-----------|---------|-------|
| `Pending` → `Confirmed` | Customer confirms order | Items non-empty; all items available |
| `Confirmed` → `Preparing` | Counter dispatches to barista / kitchen | — |
| `Preparing` → `Ready` | All workers signal completion | — |
| `Ready` → `Completed` | Pickup acknowledged | — |
| `Pending` → `Cancelled` | Customer cancels | — |
| `Confirmed` → `Cancelled` | Customer cancels | Before dispatch (before `Preparing`) |
| Any → `Preparing`/`Ready`/`Completed` | Modification or cancel attempt | REFUSED — return current status |

---

## In-Memory Stores

### `InMemoryCustomerStore`
```
ConcurrentDictionary<string, Customer>  // key = CustomerId
  + index: Dictionary<string, string>   // key = Email.ToLower() → CustomerId
```

### `InMemoryOrderStore`
```
ConcurrentDictionary<string, Order>     // key = OrderId
  + index: Dictionary<string, List<string>> // key = CustomerId → List<OrderId>
```

### Product Catalog (product-catalog service)
```
IReadOnlyList<MenuItem>  // immutable singleton; populated at startup
```

---

## Seed Data

### Customers (`SeedData.cs` in counter `Infrastructure/`)

| ID | Name | Email | Phone | Tier | Account Created |
|----|------|-------|-------|------|-----------------|
| C-1001 | Alice Johnson | alice@example.com | +1-555-0101 | gold | 2023-06-15 |
| C-1002 | Bob Martinez | bob.m@example.com | +1-555-0102 | silver | 2024-01-22 |
| C-1003 | Carol Wei | carol.wei@example.com | +1-555-0103 | standard | 2025-03-10 |

### Product Catalog (product-catalog `Program.cs` or `SeedData.cs`)

| Key | Display Name | Category | Price | Available |
|-----|-------------|----------|-------|-----------|
| CAPPUCCINO | CAPPUCCINO | Beverages | $4.50 | true |
| COFFEE_BLACK | COFFEE BLACK | Beverages | $3.00 | true |
| COFFEE_WITH_ROOM | COFFEE WITH ROOM | Beverages | $3.25 | true |
| ESPRESSO | ESPRESSO | Beverages | $3.50 | true |
| ESPRESSO_DOUBLE | ESPRESSO DOUBLE | Beverages | $4.00 | true |
| LATTE | LATTE | Beverages | $4.50 | true |
| CAKEPOP | CAKEPOP | Food | $2.50 | true |
| CROISSANT | CROISSANT | Food | $3.25 | true |
| MUFFIN | MUFFIN | Food | $3.00 | true |
| CROISSANT_CHOCOLATE | CROISSANT CHOCOLATE | Food | $3.75 | true |
| CHICKEN_MEATBALLS | CHICKEN MEATBALLS | Others | $4.25 | true |

### Demo Orders (counter `SeedData.cs`)

| Order ID | Customer | Status | Items | Total |
|----------|----------|--------|-------|-------|
| ORD-5001 | C-1001 | Completed | LATTE ×1 ($4.50) + CROISSANT ×1 ($3.25) | $7.75 |
| ORD-5002 | C-1001 | Preparing | CAPPUCCINO ×1 ($4.50) + CAKEPOP ×1 ($2.50) | $7.00 |
| ORD-5003 | C-1002 | Pending | ESPRESSO_DOUBLE ×1 ($4.00) + MUFFIN ×1 ($3.00) + COFFEE_BLACK ×1 ($3.00) | $10.00 |

> Note: Totals computed as sum of unit prices × quantities. Seed data totals
> from constitution's table may vary slightly; `SeedData.cs` is authoritative.

---

## Pickup Time Estimation (FR-008)

| Order Content | Estimate String |
|---------------|----------------|
| All items in `Beverages` | "Ready in about 5 minutes" |
| Any item in `Food` or `Others` | "Ready in about 10 minutes" |

Computed at confirmation time. No real-time queue depth.

---

## Order ID Generation

Runtime orders: `ORD-{random 6000–9999}` (e.g., `ORD-6342`).
Seed orders use `ORD-5001`, `ORD-5002`, `ORD-5003` (out of runtime range — no collision).

Uniqueness: draw random number; if `ORD-{n}` already exists, retry. Acceptable at dev scale.
