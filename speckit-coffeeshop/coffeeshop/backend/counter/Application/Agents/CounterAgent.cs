using CoffeeShop.Counter.Application.UseCases;
using CoffeeShop.Counter.Endpoints;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;

namespace CoffeeShop.Counter.Application.Agents;

/// <summary>
/// The CoffeeShop counter agent.
/// Handles customer-facing chat interactions: identification, menu browsing,
/// order placement, status checks, history queries, and order modification.
///
/// Tool executors are defined as methods with [AgentTool] (or equivalent)
/// and called by the CopilotKit request handler in CopilotKitEndpoints (T037).
/// Registration per story: T025 (LookupCustomer), T032+T036 (Menu/Order), T041 (all).
/// </summary>
public sealed class CounterAgent : AgentApplication
{
    // -----------------------------------------------------------------
    // Required system prompt (all 6 rules from plan.md §Agent Instructions)
    // -----------------------------------------------------------------
    public const string AgentInstructions = """
        You are the counter agent at CoffeeShop. Help customers identify themselves,
        browse the menu, and manage their orders.

        ROUTING RULES:
        Route Beverages items to barista. Route Food and Others items to kitchen.
        For orders containing both Beverages and Food/Others items, dispatch to both
        workers simultaneously.

        MODIFICATION AND CANCELLATION BOUNDARY:
        Orders can only be modified or cancelled before `preparing`. If the boundary has
        passed, refuse and show the current status. Do not attempt to modify or cancel
        orders in `Preparing`, `Ready`, `Completed`, or `Cancelled` status.

        PICKUP-TIME RULE:
        Beverages-only orders: respond with 'Ready in about 5 minutes'.
        Any food or others item: respond with 'Ready in about 10 minutes'.

        PRODUCT CATALOG RULE:
        If the product catalog is unavailable, inform the customer and do not proceed to
        place the order. Respond with: "Menu is currently unavailable — please try again
        shortly."

        IDENTIFICATION REQUIREMENT:
        Always identify the customer before any order action. If no identifier is provided,
        ask for their email or order number. Use the LookupCustomer tool to resolve any
        identifier (email address, customer ID like C-1001, or order ID like ORD-5001).
        """;

    private readonly LookupCustomer _lookupCustomer;
    private readonly GetMenu _getMenu;
    private readonly PlaceOrder _placeOrder;

    public CounterAgent(
        AgentApplicationOptions options,
        LookupCustomer lookupCustomer,
        GetMenu getMenu,
        PlaceOrder placeOrder)
        : base(options)
    {
        _lookupCustomer = lookupCustomer;
        _getMenu = getMenu;
        _placeOrder = placeOrder;
    }

    // -----------------------------------------------------------------
    // T025 — LookupCustomer tool
    // -----------------------------------------------------------------

    /// <summary>
    /// Resolve a customer from email, customer ID (C-XXXX), or order ID (ORD-XXXX).
    /// Returns a <see cref="CustomerLookupResponse"/> including tier and greeting for FR-018.
    /// </summary>
    public async Task<CustomerLookupResponse> LookupCustomerAsync(
        string identifier,
        CancellationToken ct = default)
    {
        var customer = await _lookupCustomer.ExecuteAsync(identifier, ct);
        return CustomerEndpoints.ToResponse(customer);
    }

    // -----------------------------------------------------------------
    // T041 — GetMenu tool
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns the complete product catalog. All 11 items including unavailable ones.
    /// Throws (→ 503) if product-catalog MCP service is unreachable.
    /// </summary>
    public async Task<MenuEndpoints.MenuResponse> GetMenuAsync(CancellationToken ct = default)
    {
        var items = await _getMenu.ExecuteAsync(ct);
        var dtos = items
            .Select(m => new MenuEndpoints.MenuItemDto(
                m.Type.ToString(),
                m.DisplayName,
                m.Category.ToString(),
                m.Price,
                m.IsAvailable))
            .ToList()
            .AsReadOnly();
        return new MenuEndpoints.MenuResponse(dtos);
    }

    // -----------------------------------------------------------------
    // T041 — PlaceOrder tool
    // -----------------------------------------------------------------

    /// <summary>
    /// Place an order for an identified customer.
    /// Validates items, checks catalog availability, and returns the confirmed order.
    /// </summary>
    public async Task<OrderEndpoints.OrderDto> PlaceOrderAsync(
        string customerId,
        IReadOnlyList<OrderItemRequest> items,
        string? notes = null,
        CancellationToken ct = default)
    {
        var request = new PlaceOrderRequest(customerId, items, notes);
        var order = await _placeOrder.ExecuteAsync(request, ct);
        return OrderEndpoints.ToOrderDto(order);
    }
}
