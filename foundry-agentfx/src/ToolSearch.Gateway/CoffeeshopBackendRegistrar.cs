using ToolSearch.Gateway.Registry;
using ToolSearch.Gateway.Search;
using ToolSearch.Gateway.ToolSearch;

namespace ToolSearch.Gateway;

public static class CoffeeshopBackendRegistrar
{
    public static IReadOnlyList<ToolDescriptor> Build(CoffeeshopMcpSession session)
    {
        return
        [
            MakeTool(session, "menu_list_items",
                "List all available menu items including name, category, and price.",
                """{"type":"object","properties":{}}""",
                ["coffeeshop", "menu", "list"]),

            MakeTool(session, "menu_get_item",
                "Get details for a specific menu item by its ID.",
                """{"type":"object","properties":{"id":{"type":"string","description":"The menu item ID, e.g. 'latte'"}},"required":["id"]}""",
                ["coffeeshop", "menu", "item"]),

            MakeTool(session, "customer_lookup",
                "Look up a customer by name, phone number, or email address.",
                """{"type":"object","properties":{"query":{"type":"string","description":"Customer name, phone, or email"}},"required":["query"]}""",
                ["coffeeshop", "customer"]),

            MakeTool(session, "order_submit",
                "Submit a coffee order for a customer. Returns order confirmation with total price.",
                """{"type":"object","properties":{"customerId":{"type":"string","description":"Customer ID from customer_lookup"},"items":{"type":"array","items":{"type":"object","properties":{"menuItemId":{"type":"string"},"quantity":{"type":"integer"}},"required":["menuItemId","quantity"]},"description":"List of items to order"}},"required":["customerId","items"]}""",
                ["coffeeshop", "order"]),
        ];
    }

    private static ToolDescriptor MakeTool(
        CoffeeshopMcpSession session,
        string name,
        string description,
        string schema,
        IReadOnlyList<string> tags)
    {
        return new ToolDescriptor(
            Name: name,
            Description: description,
            InputJsonSchema: schema,
            Tags: tags,
            IsPinned: false,
            IsSynthetic: false,
            IsVisible: _ => true,
            Handler: async (arguments, ct) => (object?)await session.CallToolAsync(name, arguments, ct));
    }
}
