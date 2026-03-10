---
name: coffeeshop-customer-lookup
description: >
  Resolve customer identity and account basics by email or customer ID using
  coffeeshop-cli query and browse commands with JSON responses.
license: MIT
compatibility: >
  Requires coffeeshop-cli with models query Customer and models browse Customer.
metadata:
  author: coffeeshop-cli
  version: "1.0"
  category: support
  loop-type: deterministic
---

# Coffee Shop Customer Lookup Skill

Use this skill when the user asks account-related questions or provides customer identifiers.

## Agent Defaults

1. Prefer specific lookup (`models query`) over broad browsing.
2. Use `models browse Customer --json` only when explicit discovery is requested.
3. Keep responses focused on customer_id, name, email, and tier.

## Command Reference

- `Coffeeshop-Cli models query Customer --email <email> --json`
- `Coffeeshop-Cli models query Customer --customer-id <id> --json`
- `Coffeeshop-Cli models browse Customer --json`

## Workflow

1. Extract identifier from user input.
2. If email exists, run email query.
3. If customer ID exists, run ID query.
4. If neither exists, ask for one identifier.
5. Return normalized account summary.

## Structured Output

Expected query success shape:

```json
{
  "ok": true,
  "customer": {
    "customer_id": "C-1001",
    "name": "Alice",
    "email": "alice@example.com",
    "tier": "Gold"
  }
}
```

## Error Handling

- If lookup returns `ok: false`, ask user to confirm exact email or customer ID.
- If command fails, return a concise retry instruction.

## Safety Notes

- Never infer customer identity from partial guesses.
- Never expose unrelated customer records.
