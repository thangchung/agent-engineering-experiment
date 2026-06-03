---
name: coffeeshop-customer-lookup
description: >
  Resolve customer identity and account basics by email, phone, name, or customer ID through ToolSearch.Gateway.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "1.1"
  category: support
---

# Coffee Shop Customer Lookup Skill

Use this skill when the user asks account-related questions or provides customer identifiers.

## Data Access — `lookup_customer`

Use ToolSearch.Gateway only. Do not call MCP protocol tools directly and do not use CLI fallback.

1. `search_tools("customer lookup", limit: 5)`
2. Pick the discovered `customer_lookup` tool and schema.
3. `call_tool("customer_lookup", {"query":"<email|phone|name|customer_id>"})`

`call_tool` returns MCP content text containing JSON. Parse that JSON.

Normalize to: `{"id":"...","name":"...","email":"...","phone":"..."}`

## Workflow

1. Extract email or customer ID from context.
2. Resolve `lookup_customer`. Prefer specific lookup over broad browse.
3. Return normalized summary.

## Safety Notes

- Never infer identity from a weak match.
- Never expose unrelated customer records.
- If lookup fails, ask for exact email/customer ID.
