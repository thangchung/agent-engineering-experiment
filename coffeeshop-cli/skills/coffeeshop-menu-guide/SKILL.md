---
name: coffeeshop-menu-guide
description: >
  Help users explore menu items, categories, and prices. MCP-first, CLI fallback.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "1.1"
  category: discovery
---

# Coffee Shop Menu Guide Skill

Use this skill when the user asks for menu options, categories, prices, or recommendations.

## Data Access — `get_menu_catalog`

1. `tools/call menu_list_items {}`
2. `Coffeeshop-Cli models browse MenuItem --json`

Normalize to: `[{"item_type":"...","name":"...","category":"...","price":0.00}]`

## Workflow

1. Execute `get_menu_catalog`.
2. Filter/group by user intent. Return 3-6 options unless full list requested.
3. Offer handoff to `coffeeshop-counter-service` if user wants to order.

## Safety Notes

- Never fabricate items or prices.
- Format money as `$<amount with 2 decimals>`.
- If all paths fail, return concise retry guidance.
