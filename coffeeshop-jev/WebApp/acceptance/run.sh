#!/usr/bin/env bash
# Acceptance run against a live stack (`aspire run --detach` first). Real DecisionModel + LLM: a full pass takes a few minutes.
# Usage: acceptance/run.sh [A1 A4 ...]
cd "$(dirname "$0")" || exit 1
if [ $# -gt 0 ]; then files=$(for a in "$@"; do ls "$a"_*.js; done); else files=$(ls A*.js); fi
fail=0
for f in $files; do
  echo "== $f"
  out=$(playwright-cli run-code "async (page) => { $(cat _lib.js); return await ($(cat "$f"))(page); }" 2>&1)
  echo "$out" | grep -E '^"|### Error|Error:' | head -5
  echo "$out" | grep -qE '### Error|Error:' && fail=1
done
exit $fail
