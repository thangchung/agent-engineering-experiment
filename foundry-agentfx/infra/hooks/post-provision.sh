#!/usr/bin/env sh
set -e

echo "=== Post-provision: creating search indexes and knowledge base ==="

if [ -f "infra/create-search-indexes.py" ]; then
  if command -v python3 >/dev/null 2>&1; then
    PYTHON=python3
  elif command -v python >/dev/null 2>&1; then
    PYTHON=python
  else
    echo "Python not found — skipping index seeding."
    exit 0
  fi

  # Load azd env vars into shell
  eval "$(azd env get-values 2>/dev/null | sed 's/^/export /')" 2>/dev/null || true

  # azd does not export AZURE_TENANT_ID — fetch from az CLI
  if [ -z "$AZURE_TENANT_ID" ] && command -v az >/dev/null 2>&1; then
    AZURE_TENANT_ID=$(az account show --query tenantId -o tsv 2>/dev/null || true)
    export AZURE_TENANT_ID
  fi

  if [ -z "$AZURE_AI_SEARCH_SERVICE_ENDPOINT" ]; then
    echo "AZURE_AI_SEARCH_SERVICE_ENDPOINT not set — skipping index seeding."
    exit 0
  fi

  VENV_DIR="infra/.venv"
  if [ ! -x "$VENV_DIR/bin/python" ]; then
    $PYTHON -m venv "$VENV_DIR"
  fi
  VENV_PYTHON="$VENV_DIR/bin/python"

  # Install deps in a repo-local venv (no azure-search-documents needed — script uses REST directly)
  "$VENV_PYTHON" -m pip install --quiet --upgrade pip
  "$VENV_PYTHON" -m pip install --quiet azure-identity httpx python-dotenv

  echo "Creating search indexes..."
  "$VENV_PYTHON" infra/create-search-indexes.py || echo "Warning: search index creation failed (non-fatal)"

  echo "Creating Foundry Toolbox..."
  "$VENV_PYTHON" infra/create-toolbox.py || echo "Warning: toolbox creation failed (non-fatal — Toolbox API is preview)"
else
  echo "No post-provision scripts found — skipping."
fi
