---
name: coffeeshop-counter-service
description: >
  End-to-end counter: identify customer, classify intent, build order, confirm, submit.
  MCP-first, CLI fallback.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "3.4"
  category: operations
  loop-type: agentic
---

# Coffee Shop Order Submission Skill

## Data Access

| Action | MCP tool | CLI fallback |
|--------|----------|--------------|
| `lookup_customer` | `customer_lookup` | `Coffeeshop-Cli models query Customer ... --json` |
| `list_menu` | `menu_list_items` | `Coffeeshop-Cli models browse MenuItem --json` |
| `submit_order` | `order_submit` | `Coffeeshop-Cli models submit Order --json` |

## Agentic Loop

1. **INTAKE** — Identify customer.
2. **CLASSIFY INTENT** — `account | item-types | process-order | order-status`.
3. **REVIEW & CONFIRM** — Build items + total. Show summary. Require explicit confirmation.
4. **FINALIZE** — Submit. Return order ID, total, ETA.

## Safety Notes

- Never invent unavailable menu items.
- Never finalize before user confirmation.
