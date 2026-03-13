---
name: coffeeshop-customer-lookup
description: >
  Resolve customer identity and account basics by email or customer ID. MCP-first, CLI fallback.
license: MIT
metadata:
  author: coffeeshop-cli
  version: "1.1"
  category: support
---

# Coffee Shop Customer Lookup Skill

Use this skill when the user asks account-related questions or provides customer identifiers.

## Data Access — `lookup_customer`

1. `tools/call customer_lookup {"email":"<email>"}` or `{"customer_id":"<id>"}`
2. `Coffeeshop-Cli models query Customer --email <email> --json`
   (or `--customer-id <id>`, or `models browse Customer --json`)

Normalize to: `{"customer_id":"...","name":"...","email":"...","tier":"..."}`

## Workflow

1. Extract email or customer ID from context.
2. Resolve `lookup_customer`. Prefer specific lookup over broad browse.
3. Return normalized summary.

## Safety Notes

- Never infer identity from a weak match.
- Never expose unrelated customer records.
- If lookup fails, ask for exact email/customer ID.
