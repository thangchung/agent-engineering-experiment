# /// script
# requires-python = ">=3.10"
# dependencies = ["fastmcp>=3.0.0"]
# ///
"""
Mock Product Catalog MCP Server — simulates a coffee shop product catalog.

Item types mirror the C# ItemType enum from:
https://github.com/thangchung/coffeeshop-agent/blob/main/src/ProductCatalogService/Shared/StuffData.cs

State is persisted to a temp JSON file so it survives across subprocess
invocations (GitHub Copilot creates a new stdio session per tool call).

Run:
    uv run product_catalogs.py
"""

import copy
import json
import os
import tempfile

from fastmcp import FastMCP

mcp = FastMCP(
    "ProductCatalog",
    instructions="Simulated coffee shop product catalog for testing",
)

# ---------------------------------------------------------------------------
# Default catalog data — mirrors the C# ItemType enum
# Prices are fixed in the $2.00–$5.00 range (deterministic mock)
# ---------------------------------------------------------------------------

_DEFAULT_CATALOG: dict[str, dict] = {
    # Beverages
    "CAPPUCCINO": {
        "item_type": "CAPPUCCINO",
        "name": "CAPPUCCINO",
        "category": "Beverages",
        "price": 4.50,
    },
    "COFFEE_BLACK": {
        "item_type": "COFFEE_BLACK",
        "name": "COFFEE BLACK",
        "category": "Beverages",
        "price": 3.00,
    },
    "COFFEE_WITH_ROOM": {
        "item_type": "COFFEE_WITH_ROOM",
        "name": "COFFEE WITH ROOM",
        "category": "Beverages",
        "price": 3.25,
    },
    "ESPRESSO": {
        "item_type": "ESPRESSO",
        "name": "ESPRESSO",
        "category": "Beverages",
        "price": 3.50,
    },
    "ESPRESSO_DOUBLE": {
        "item_type": "ESPRESSO_DOUBLE",
        "name": "ESPRESSO DOUBLE",
        "category": "Beverages",
        "price": 4.00,
    },
    "LATTE": {
        "item_type": "LATTE",
        "name": "LATTE",
        "category": "Beverages",
        "price": 4.50,
    },
    # Food
    "CAKEPOP": {
        "item_type": "CAKEPOP",
        "name": "CAKEPOP",
        "category": "Food",
        "price": 2.50,
    },
    "CROISSANT": {
        "item_type": "CROISSANT",
        "name": "CROISSANT",
        "category": "Food",
        "price": 3.25,
    },
    "MUFFIN": {
        "item_type": "MUFFIN",
        "name": "MUFFIN",
        "category": "Food",
        "price": 3.00,
    },
    "CROISSANT_CHOCOLATE": {
        "item_type": "CROISSANT_CHOCOLATE",
        "name": "CROISSANT CHOCOLATE",
        "category": "Food",
        "price": 3.75,
    },
    # Others
    "CHICKEN_MEATBALLS": {
        "item_type": "CHICKEN_MEATBALLS",
        "name": "CHICKEN MEATBALLS",
        "category": "Others",
        "price": 4.25,
    },
}

_STATE_FILE = os.path.join(tempfile.gettempdir(), "mcp_product_catalog_state.json")


class Store:
    """Mutable session state persisted to disk between subprocess invocations."""

    def __init__(self):
        self.catalog: dict[str, dict] = {}
        self.load()

    def load(self):
        """Load state from disk, or use defaults if no file exists."""
        if os.path.exists(_STATE_FILE):
            with open(_STATE_FILE) as f:
                data = json.load(f)
            self.catalog = data.get("catalog", copy.deepcopy(_DEFAULT_CATALOG))
        else:
            self.catalog = copy.deepcopy(_DEFAULT_CATALOG)

    def save(self):
        """Persist current state to disk."""
        with open(_STATE_FILE, "w") as f:
            json.dump({"catalog": self.catalog}, f)

    def reset(self):
        self.catalog = copy.deepcopy(_DEFAULT_CATALOG)
        self.save()


store = Store()

# ---------------------------------------------------------------------------
# Tools
# ---------------------------------------------------------------------------


@mcp.tool(annotations={"readOnlyHint": True})
def get_item_types() -> dict:
    """Get the full list of available coffee shop item types with names, categories, and prices."""
    items = list(store.catalog.values())
    return {
        "ok": True,
        "count": len(items),
        "item_types": items,
    }


@mcp.tool(annotations={"readOnlyHint": True})
def get_items_prices(item_types: list[str]) -> dict:
    """Get price information for a list of item types.

    Args:
        item_types: List of item type keys to look up (e.g. ["LATTE", "MUFFIN"]).
                    Use get_item_types() first to see all valid keys.
    """
    results = []
    errors = []

    for key in item_types:
        normalized = key.strip().upper()
        item = store.catalog.get(normalized)
        if item:
            results.append(item)
        else:
            errors.append(f"Unknown item type: '{key}'")

    response: dict = {"ok": len(errors) == 0, "items": results}
    if errors:
        response["errors"] = errors
    return response


@mcp.tool()
def reset_state() -> dict:
    """Reset product catalog data back to its original defaults. Useful between test runs."""
    store.reset()
    return {"ok": True, "message": "Product catalog state reset to defaults"}


# ---------------------------------------------------------------------------
# Entry point — supports dual transport: stdio (default) and HTTP
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description="Product Catalog MCP Server")
    parser.add_argument(
        "--transport",
        choices=["stdio", "http"],
        default="stdio",
        help="Transport type (default: stdio)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=8002,
        help="Port for HTTP transport (default: 8002)",
    )
    args = parser.parse_args()

    if args.transport == "http":
        import uvicorn
        from starlette.middleware import Middleware
        from starlette.middleware.cors import CORSMiddleware

        asgi_app = mcp.http_app(
            middleware=[
                Middleware(
                    CORSMiddleware,
                    allow_origins=["*"],
                    allow_methods=["*"],
                    allow_headers=["*"],
                )
            ]
        )
        uvicorn.run(asgi_app, host="0.0.0.0", port=args.port)
    else:
        mcp.run()
