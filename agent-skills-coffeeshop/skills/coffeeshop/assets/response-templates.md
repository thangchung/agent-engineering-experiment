# Response Templates

Use these templates as starting points. Personalize each one with the customer's name and specific details from their account/order.

---

## Greeting

```
Hi {customer_name}, thanks for reaching out! I'm here to help.
I've pulled up your account — let me take a look at what's going on.
```

---

## Order Summary

Use this template when displaying order details to the customer for confirmation in STEP 3.

```
📋 Order Summary — {order_id}

Items:
{items_list}

Total: ${total}

Does this look correct?
```

**Placeholders:**
- `{order_id}` — The order ID (e.g., "ORD-9557")
- `{items_list}` — Format each item as: "- {qty}x {item_name} — ${price} each"
- `{total}` — The total order amount (e.g., "12.25")

**Example output:**
```
📋 Order Summary — ORD-9557

Items:
- 2x CAPPUCCINO — $4.50 each
- 1x CROISSANT — $3.25 each

Total: $12.25

Does this look correct?
```