# /// script
# requires-python = ">=3.10"
# dependencies = ["fastmcp>=3.0.0"]
# ///
"""
Mock Order & Account MCP Server — simulates a coffee shop order management system.

State is persisted to a temp JSON file so it survives across subprocess
invocations (GitHub Copilot creates a new stdio session per tool call).

Run:
    uv run orders.py
"""

import copy
import json
import os
import random
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from fastmcp import FastMCP
from fastmcp.server.apps import AppConfig, ResourceCSP

mcp = FastMCP(
    "Orders",
    instructions="Simulated coffee shop order management system for testing",
)

# ---------------------------------------------------------------------------
# Stateful data store (deep-copied from defaults so state persists across
# calls within a session but can be reset between test runs)
# ---------------------------------------------------------------------------

_DEFAULT_CUSTOMERS: dict[str, dict] = {
    "C-1001": {
        "customer_id": "C-1001",
        "name": "Alice Johnson",
        "email": "alice@example.com",
        "phone": "+1-555-0101",
        "tier": "gold",
        "account_created": "2023-06-15",
    },
    "C-1002": {
        "customer_id": "C-1002",
        "name": "Bob Martinez",
        "email": "bob.m@example.com",
        "phone": "+1-555-0102",
        "tier": "silver",
        "account_created": "2024-01-22",
    },
    "C-1003": {
        "customer_id": "C-1003",
        "name": "Carol Wei",
        "email": "carol.wei@example.com",
        "phone": "+1-555-0103",
        "tier": "standard",
        "account_created": "2025-03-10",
    },
}

_DEFAULT_ORDERS: dict[str, dict] = {
    "ORD-5001": {
        "order_id": "ORD-5001",
        "customer_id": "C-1001",
        "status": "completed",
        "placed_at": "2026-02-10T08:30:00Z",
        "total": 8.00,
        "items": [
            {"item_type": "LATTE", "name": "LATTE", "qty": 1, "price": 4.50},
            {"item_type": "CROISSANT", "name": "CROISSANT", "qty": 1, "price": 3.25},
        ],
        "notes": [],
    },
    "ORD-5002": {
        "order_id": "ORD-5002",
        "customer_id": "C-1001",
        "status": "preparing",
        "placed_at": "2026-02-21T09:15:00Z",
        "total": 7.50,
        "items": [
            {"item_type": "CAPPUCCINO", "name": "CAPPUCCINO", "qty": 1, "price": 4.50},
            {"item_type": "CAKEPOP", "name": "CAKEPOP", "qty": 1, "price": 2.50},
        ],
        "notes": [],
    },
    "ORD-5003": {
        "order_id": "ORD-5003",
        "customer_id": "C-1002",
        "status": "pending",
        "placed_at": "2026-02-21T10:00:00Z",
        "total": 10.75,
        "items": [
            {"item_type": "ESPRESSO_DOUBLE", "name": "ESPRESSO DOUBLE", "qty": 1, "price": 4.00},
            {"item_type": "MUFFIN", "name": "MUFFIN", "qty": 1, "price": 3.00},
            {"item_type": "COFFEE_BLACK", "name": "COFFEE BLACK", "qty": 1, "price": 3.00},
        ],
        "notes": [],
    },
}

VALID_STATUSES = ["pending", "confirmed", "preparing", "ready", "completed", "cancelled"]

_MENU_ITEMS = [
    {"item_type": "CAPPUCCINO", "name": "CAPPUCCINO", "category": "Beverages", "price": 4.50},
    {"item_type": "COFFEE_BLACK", "name": "COFFEE BLACK", "category": "Beverages", "price": 3.00},
    {"item_type": "COFFEE_WITH_ROOM", "name": "COFFEE WITH ROOM", "category": "Beverages", "price": 3.00},
    {"item_type": "ESPRESSO", "name": "ESPRESSO", "category": "Beverages", "price": 3.50},
    {"item_type": "ESPRESSO_DOUBLE", "name": "ESPRESSO DOUBLE", "category": "Beverages", "price": 4.00},
    {"item_type": "LATTE", "name": "LATTE", "category": "Beverages", "price": 4.50},
    {"item_type": "CAKEPOP", "name": "CAKEPOP", "category": "Food", "price": 2.50},
    {"item_type": "CROISSANT", "name": "CROISSANT", "category": "Food", "price": 3.25},
    {"item_type": "MUFFIN", "name": "MUFFIN", "category": "Food", "price": 3.00},
    {"item_type": "CROISSANT_CHOCOLATE", "name": "CROISSANT CHOCOLATE", "category": "Food", "price": 3.75},
    {"item_type": "CHICKEN_MEATBALLS", "name": "CHICKEN MEATBALLS", "category": "Other", "price": 5.00},
]

ORDER_FORM_URI = "ui://orders/order_form.html"

# Store current customer for the active form session
_current_form_customer: dict | None = None

_STATE_FILE = os.path.join(tempfile.gettempdir(), "mcp_orders_state.json")


class Store:
    """Mutable session state persisted to disk between subprocess invocations."""

    def __init__(self):
        self.customers: dict = {}
        self.orders: dict = {}
        self._email_index: dict = {}
        self.load()

    def load(self):
        """Load state from disk, or use defaults if no file exists."""
        if os.path.exists(_STATE_FILE):
            with open(_STATE_FILE) as f:
                data = json.load(f)
            self.customers = data.get("customers", copy.deepcopy(_DEFAULT_CUSTOMERS))
            self.orders = data.get("orders", copy.deepcopy(_DEFAULT_ORDERS))
        else:
            self.customers = copy.deepcopy(_DEFAULT_CUSTOMERS)
            self.orders = copy.deepcopy(_DEFAULT_ORDERS)
        self._email_index = {c["email"]: cid for cid, c in self.customers.items()}

    def save(self):
        """Persist current state to disk."""
        with open(_STATE_FILE, "w") as f:
            json.dump({"customers": self.customers, "orders": self.orders}, f)

    def customer_by_email(self, email: str) -> dict | None:
        cid = self._email_index.get(email)
        return self.customers.get(cid) if cid else None

    def reset(self):
        self.customers = copy.deepcopy(_DEFAULT_CUSTOMERS)
        self.orders = copy.deepcopy(_DEFAULT_ORDERS)
        self._email_index = {c["email"]: cid for cid, c in self.customers.items()}
        self.save()


store = Store()


def _now_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _new_order_id() -> str:
    return f"ORD-{random.randint(6000, 9999)}"


# ---------------------------------------------------------------------------
# Tools
# ---------------------------------------------------------------------------


@mcp.tool(annotations={"readOnlyHint": True})
def lookup_customer(
    email: str | None = None,
    customer_id: str | None = None,
) -> dict:
    """Look up a coffee shop customer by email address or customer ID.

    Provide at least one of `email` or `customer_id`.
    """
    if email:
        customer = store.customer_by_email(email)
        if not customer:
            return {"ok": False, "error": f"No customer found with email '{email}'"}
        return {"ok": True, "customer": customer}
    if customer_id:
        customer = store.customers.get(customer_id)
        if not customer:
            return {"ok": False, "error": f"No customer found with id '{customer_id}'"}
        return {"ok": True, "customer": customer}
    return {"ok": False, "error": "Provide email or customer_id"}


@mcp.tool(annotations={"readOnlyHint": True})
def get_order(order_id: str) -> dict:
    """Get full details for a single coffee shop order by its order ID."""
    order = store.orders.get(order_id)
    if not order:
        return {"ok": False, "error": f"No order found with id '{order_id}'"}
    return {"ok": True, "order": order}


@mcp.tool(annotations={"readOnlyHint": True})
def order_history(customer_id: str) -> dict:
    """Return all orders for a given customer, newest first.

    Args:
        customer_id: The customer ID (e.g. "C-1001").
    """
    if customer_id not in store.customers:
        return {"ok": False, "error": f"No customer found with id '{customer_id}'"}
    orders = sorted(
        [o for o in store.orders.values() if o["customer_id"] == customer_id],
        key=lambda o: o["placed_at"],
        reverse=True,
    )
    return {
        "ok": True,
        "customer_id": customer_id,
        "order_count": len(orders),
        "orders": orders,
    }


@mcp.tool(annotations={"readOnlyHint": True}, app=AppConfig(resource_uri=ORDER_FORM_URI))
def get_menu() -> dict:
    """Get the coffee shop menu with all available items and prices.
    
    This tool is designed for the order form UI to load menu data.
    Returns all menu items with item_type, name, category, and price.
    """
    return {
        "ok": True,
        "menu": _MENU_ITEMS,
        "count": len(_MENU_ITEMS)
    }


@mcp.tool(annotations={"readOnlyHint": True}, app=AppConfig(resource_uri=ORDER_FORM_URI))
def get_form_context() -> dict:
    """Get the current customer context for the order form.
    
    This tool is called by the order form UI to retrieve the customer information
    for the active form session.
    """
    global _current_form_customer
    if not _current_form_customer:
        return {
            "ok": False,
            "error": "No active form session. Please reopen the order form."
        }
    return {
        "ok": True,
        "customer_id": _current_form_customer["customer_id"],
        "customer_name": _current_form_customer["name"],
        "customer_email": _current_form_customer["email"],
    }


@mcp.tool(annotations={"readOnlyHint": True}, app=AppConfig(resource_uri=ORDER_FORM_URI))
def open_order_form(customer_id: str) -> dict:
    """Open the interactive order form for a customer.

    This presents the HTML order form UI where the customer can browse the menu,
    select items from dropdowns, see prices (read-only), adjust quantities,
    and submit the order directly from the form.

    Call this tool when the customer wants to browse menu items or place an order.

    Args:
        customer_id: The customer ID (e.g. "C-1001").
    """
    global _current_form_customer
    customer = store.customers.get(customer_id)
    if not customer:
        return {"ok": False, "error": f"No customer found with id '{customer_id}'"}
    
    # Store customer for form session
    _current_form_customer = customer
    
    return {
        "ok": True,
        "message": "Order form opened. The customer can now browse the menu and place an order.",
        "customer_id": customer_id,
        "customer_name": customer["name"],
    }


@mcp.tool()
def create_order(customer_id: str, order_dto: dict) -> dict:
    """Create a new coffee shop order for a customer.

    Args:
        customer_id: The customer ID (e.g. "C-1001").
        order_dto: Order details dict with an "items" list. Each item must have:
                   - item_type (str): Item type key, e.g. "LATTE"
                   - name (str): Display name, e.g. "LATTE"
                   - qty (int): Quantity
                   - price (float): Unit price

    Example order_dto:
        {
            "items": [
                {"item_type": "LATTE", "name": "LATTE", "qty": 1, "price": 4.50},
                {"item_type": "MUFFIN", "name": "MUFFIN", "qty": 2, "price": 3.00}
            ]
        }
    """
    if customer_id not in store.customers:
        return {"ok": False, "error": f"No customer found with id '{customer_id}'"}

    items = order_dto.get("items", [])
    if not items:
        return {"ok": False, "error": "order_dto must include a non-empty 'items' list"}

    # Calculate total
    total = sum(item.get("price", 0) * item.get("qty", 1) for item in items)

    order_id = _new_order_id()
    # Ensure uniqueness
    while order_id in store.orders:
        order_id = _new_order_id()

    order = {
        "order_id": order_id,
        "customer_id": customer_id,
        "status": "pending",
        "placed_at": _now_iso(),
        "total": round(total, 2),
        "items": items,
        "notes": [],
    }

    store.orders[order_id] = order
    store.save()
    return {"ok": True, "order_id": order_id, "order": order}


@mcp.tool()
def update_order(
    order_id: str,
    status: str | None = None,
    add_note: str | None = None,
) -> dict:
    """Update a coffee shop order's status and/or add a note.

    Args:
        order_id: The order to update.
        status: New status. One of: pending, confirmed, preparing, ready, completed, cancelled.
        add_note: Free-text note to append to the order.
    """
    order = store.orders.get(order_id)
    if not order:
        return {"ok": False, "error": f"No order found with id '{order_id}'"}

    changes = []

    if status:
        if status not in VALID_STATUSES:
            return {
                "ok": False,
                "error": f"Invalid status '{status}'. Must be one of: {VALID_STATUSES}",
            }
        old_status = order["status"]
        order["status"] = status
        changes.append(f"status: {old_status} -> {status}")

    if add_note:
        order["notes"].append({
            "text": add_note,
            "author": "auto-agent",
            "timestamp": _now_iso(),
        })
        changes.append("note added")

    store.save()
    return {"ok": True, "order_id": order_id, "changes": changes, "order": order}


@mcp.tool()
def reset_state() -> dict:
    """Reset all order and customer data back to its original defaults. Useful between test runs."""
    store.reset()
    return {"ok": True, "message": "Order state reset to defaults"}


# ---------------------------------------------------------------------------
# Resources for UI
# ---------------------------------------------------------------------------

@mcp.resource(
    ORDER_FORM_URI,
    app=AppConfig(
        csp=ResourceCSP(resource_domains=["https://unpkg.com"])
    )
)
def order_form_view() -> str:
    """Serve the interactive order form HTML for creating orders."""
    html_path = Path(__file__).parent / "order_form.html"
    return html_path.read_text(encoding="utf-8")


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    mcp.run()
