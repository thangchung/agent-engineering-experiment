---
name: coffeeshop-counter-service
description: >
  End-to-end counter: identify customer, classify intent, build order, confirm, submit.
  ToolSearch.Gateway-first. No direct MCP or CLI calls.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "3.4"
  category: operations
  loop-type: agentic
---

# Coffee Shop Order Submission Skill

## Data Access

Use ToolSearch.Gateway only. Do not call MCP protocol tools directly and do not use CLI fallback.

| Action | Search first | Then call discovered tool |
|--------|--------------|---------------------------|
| `lookup_customer` | `search_tools("customer lookup", limit: 5)` | `call_tool("customer_lookup", {"query":"<email|phone|name|customer_id>"})` |
| `list_menu` | `search_tools("menu list items", limit: 5)` | `call_tool("menu_list_items", {})` |
| `submit_order` | `search_tools("submit coffee order", limit: 5)` | `call_tool("order_submit", {"customerId":"<customer id from customer_lookup id field>","items":[{"menuItemId":"<menu_item_id>","quantity":1}]})` |

## Agentic Loop

1. **INTAKE** — Identify customer. If the user has not provided email, phone, name, or customer ID, ask for that missing identifier in plain text and stop. Do not call tools yet.
2. **CLASSIFY INTENT** — `account | item-types | process-order | order-status`.
3. **REVIEW & CONFIRM** — After customer lookup succeeds, reply with customer name and ask confirmation for the pending order. Do not call menu or order tools in the same turn as customer lookup. Build items + total only after user confirms.
4. **FINALIZE** — Submit. Return order ID, total, ETA.

## Safety Notes

- Never invent unavailable menu items.
- Never finalize before user confirmation.
