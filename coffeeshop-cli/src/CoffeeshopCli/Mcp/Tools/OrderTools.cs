namespace CoffeeshopCli.Mcp.Tools;

/// <summary>
/// Order-related tool definitions.
/// </summary>
public sealed class OrderTools
{
    public object CreateOrderDefinition() => new
    {
        name = "create_order",
        description = "Create an order from simplified input"
    };
}
