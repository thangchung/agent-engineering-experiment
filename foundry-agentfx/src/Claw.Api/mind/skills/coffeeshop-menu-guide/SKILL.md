---
name: coffeeshop-menu-guide
description: >
  Help users explore menu items, categories, and prices through ToolSearch.Gateway.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "1.1"
  category: discovery
---

# Coffee Shop Menu Guide Skill

Use this skill when the user asks for menu options, categories, prices, or recommendations.

## Data Access — `get_menu_catalog`

Use ToolSearch.Gateway only. Do not call MCP protocol tools directly and do not use CLI fallback.

1. `search_tools("menu list items", limit: 5)`
2. Pick the discovered `menu_list_items` tool and schema.
3. `call_tool("menu_list_items", {})`

Normalize to: `[{"item_type":"...","name":"...","category":"...","price":0.00}]`

## Workflow

1. Execute `get_menu_catalog`.
2. Filter/group by user intent. Return 3-6 options unless full list requested.
3. Offer handoff to `coffeeshop-counter-service` if user wants to order.

## Safety Notes

- Never fabricate items or prices.
- Format money as `$<amount with 2 decimals>`.
- If all paths fail, return concise retry guidance.
