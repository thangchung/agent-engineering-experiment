---
name: coffeeshop-menu-guide
description: >
  Help users explore menu items, categories, and prices with concise,
  structured responses powered by coffeeshop-cli JSON output.
license: MIT
compatibility: >
  Requires coffeeshop-cli with models browse support for MenuItem and --json.
metadata:
  author: coffeeshop-cli
  version: "1.0"
  category: discovery
  loop-type: deterministic
---

# Coffee Shop Menu Guide Skill

Use this skill when the user wants to browse menu options, compare prices, or decide what to order.

## Agent Defaults

1. Always call `Coffeeshop-Cli models browse MenuItem --json` before making menu claims.
2. Group suggestions by category when possible.
3. Keep recommendations short and concrete.

## Command Reference

- `Coffeeshop-Cli models browse MenuItem --json` — return complete menu catalog.

## Common Patterns

```bash
Coffeeshop-Cli models browse MenuItem --json
```

Then format outputs like:

- "Coffee: Latte ($4.50), Cappuccino ($4.00)"
- "Food: Croissant ($3.25), Blueberry Muffin ($2.75)"

## Workflow

1. Retrieve menu catalog with `--json`.
2. Filter or group by user intent (e.g., hot drinks, budget options, food add-ons).
3. Present 3-6 options max unless user requests full list.
4. Offer to proceed to order capture via `coffeeshop-counter-service` flow.

## Error Handling

- If browse fails, ask the user to retry and report the command error summary.
- If requested item is not found, suggest nearest available alternatives.

## Safety Notes

- Do not fabricate menu items or prices.
- Keep currency formatting consistent: `$<amount with 2 decimals>`.
