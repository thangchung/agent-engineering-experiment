# /// script
# requires-python = ">=3.10"
# dependencies = [
#     "agent-framework-github-copilot>=1.0.0b0",
#     "agent-framework-ag-ui>=1.0.0b0",
#     "fastapi",
#     "uvicorn",
#     "starlette",
# ]
# ///
"""
Coffee Shop MAF Agent Host

Wraps the coffeeshop skill into a GitHubCopilotAgent and exposes it via
AG-UI protocol over HTTP so CopilotKit (or any AG-UI client) can connect.

Run:
    uv run agent-host/main.py

Env vars:
    GITHUB_TOKEN              — GitHub Personal Access Token (required)
    GITHUB_COPILOT_MODEL      — Model name (default: gpt-4o)
    GITHUB_COPILOT_CLI_PATH   — Path to Copilot CLI binary (default: copilot)
    ORDERS_MCP_URL            — Orders MCP HTTP URL (default: http://localhost:8001/mcp)
    PRODUCT_ITEMS_MCP_URL     — Product Catalog MCP HTTP URL (default: http://localhost:8002/mcp)
    AGENT_HOST_PORT           — Port to listen on (default: 8000)
"""

import os
import uvicorn
from pathlib import Path

from fastapi import FastAPI
from starlette.middleware.cors import CORSMiddleware

from agent_framework_github_copilot import GitHubCopilotAgent
from copilot.types import MCPRemoteServerConfig
from agent_framework.ag_ui import add_agent_framework_fastapi_endpoint

# ---------------------------------------------------------------------------
# Load coffeeshop skill as system instructions
# ---------------------------------------------------------------------------
_skill_path = (
    Path(__file__).parent.parent / "skills" / "coffeeshop" / "SKILL.md"
)
_skill_content = _skill_path.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# MCP server URLs
# ---------------------------------------------------------------------------
_orders_url = os.getenv("ORDERS_MCP_URL", "http://localhost:8001/mcp")
_product_items_url = os.getenv("PRODUCT_ITEMS_MCP_URL", "http://localhost:8002/mcp")

# ---------------------------------------------------------------------------
# Create MAF GitHubCopilotAgent with coffeeshop skill + MCP servers
# ---------------------------------------------------------------------------
agent = GitHubCopilotAgent(
    instructions=_skill_content,
    default_options={
        "model": os.getenv("GITHUB_COPILOT_MODEL", "gpt-4o"),
        "mcp_servers": {
            "orders": MCPRemoteServerConfig(
                type="http",
                url=_orders_url,
            ),
            "product_items": MCPRemoteServerConfig(
                type="http",
                url=_product_items_url,
            ),
        },
    },
)

# ---------------------------------------------------------------------------
# FastAPI app with AG-UI endpoint + CORS
# ---------------------------------------------------------------------------
app = FastAPI(title="Coffee Shop Agent Host", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Expose the agent at "/" via AG-UI SSE streaming protocol
add_agent_framework_fastapi_endpoint(app, agent, "/")


@app.get("/health")
async def health():
    return {"status": "ok", "agent": "coffeeshop"}


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    port = int(os.getenv("AGENT_HOST_PORT", "8010"))
    uvicorn.run(app, host="0.0.0.0", port=port)
