using CoffeeshopCli.Models;
using CoffeeshopCli.Services;
using ModelContextProtocol.Server;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// HTTP MCP bridge tool surface used by cloud MCP clients.
/// </summary>
public sealed class CoffeeshopMcpBridgeTools
{
    private readonly IDiscoveryService _discovery;
    private readonly SkillParser _skillParser;
    private readonly OrderSubmitHandler _orderSubmitHandler;

    /// <summary>
    /// Creates a bridge tools instance with discovery, parsing, and order submission services.
    /// </summary>
    public CoffeeshopMcpBridgeTools(
        IDiscoveryService discovery,
        SkillParser skillParser,
        OrderSubmitHandler orderSubmitHandler)
    {
        _discovery = discovery;
        _skillParser = skillParser;
        _orderSubmitHandler = orderSubmitHandler;
    }

    /// <summary>
    /// Lists discovered skills for remote clients.
    /// </summary>
    [McpServerTool(Name = "skill_list", Title = "List discovered skills")]
    public object SkillList()
    {
        return new
        {
            ok = true,
            skills = _discovery.DiscoverSkills().Select(s => new
            {
                name = s.Name,
                description = s.Description,
                version = s.Version,
                category = s.Category,
                loop_type = s.LoopType
            })
        };
    }

    /// <summary>
    /// Shows one parsed skill manifest by name.
    /// </summary>
    [McpServerTool(Name = "skill_show", Title = "Show one skill manifest")]
    public object SkillShow(string name)
    {
        var skill = _discovery.DiscoverSkills()
            .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
        {
            return new { ok = false, error = "skill_not_found", name };
        }

        var content = skill.Content ?? File.ReadAllText(skill.Path);
        var manifest = _skillParser.Parse(content);

        return new
        {
            ok = true,
            skill = new
            {
                name = manifest.Frontmatter.Name,
                description = manifest.Frontmatter.Description,
                metadata = manifest.Frontmatter.Metadata,
                body = manifest.Body
            }
        };
    }

    /// <summary>
    /// Invokes supported skills using a non-interactive cloud-safe contract.
    /// </summary>
    [McpServerTool(Name = "skill_invoke", Title = "Invoke skill non-interactive")]
    public async Task<object> SkillInvoke(string name, SkillInvokeArgs args)
    {
        if (!name.Equals("coffeeshop-counter-service", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = false, error = "skill_not_supported_for_remote", name };
        }

        if (!args.intent.Equals("process-order", StringComparison.OrdinalIgnoreCase))
        {
            return new { ok = false, error = "intent_not_supported_for_submit", intent = args.intent };
        }

        if (!args.confirm)
        {
            return new
            {
                ok = true,
                state = new
                {
                    customer_id = args.customer_id,
                    intent = args.intent,
                    items = args.items,
                    confirmed = false
                },
                message = "confirmation_required"
            };
        }

        var input = new SimplifiedOrderInput
        {
            CustomerId = args.customer_id,
            Items = (args.items ?? []).Select(i => new SimplifiedOrderItemInput
            {
                ItemType = Enum.Parse<ItemType>(i.item_type, ignoreCase: true),
                Qty = i.qty
            }).ToList()
        };

        var order = await _orderSubmitHandler.SubmitAsync(input);

        return new
        {
            ok = true,
            skill = name,
            state = new
            {
                customer_id = order.CustomerId,
                intent = args.intent,
                confirmed = true
            },
            result = new
            {
                order_id = order.OrderId,
                status = order.Status.ToString(),
                total = order.Total,
                placed_at = order.PlacedAt
            }
        };
    }

    /// <summary>
    /// Lists menu items for agent menu discovery.
    /// </summary>
    [McpServerTool(Name = "menu_list_items", Title = "List menu items")]
    public object MenuListItems()
    {
        return new
        {
            ok = true,
            items = SampleDataStore.Menu.Select(i => new
            {
                item_type = i.ItemType.ToString(),
                name = i.Name,
                category = i.Category,
                price = i.Price
            })
        };
    }

    /// <summary>
    /// Looks up one customer by email or customer id.
    /// </summary>
    [McpServerTool(Name = "customer_lookup", Title = "Lookup customer by email or id")]
    public object CustomerLookup(string? email = null, string? customer_id = null)
    {
        var customer = !string.IsNullOrWhiteSpace(email)
            ? SampleDataStore.GetCustomerByEmail(email)
            : !string.IsNullOrWhiteSpace(customer_id)
                ? SampleDataStore.GetCustomerById(customer_id)
                : null;

        if (customer is null)
        {
            return new { ok = false, error = "customer_not_found" };
        }

        return new
        {
            ok = true,
            customer = new
            {
                customer_id = customer.CustomerId,
                name = customer.Name,
                email = customer.Email,
                tier = customer.Tier.ToString()
            }
        };
    }

    /// <summary>
    /// Submits a simplified order contract.
    /// </summary>
    [McpServerTool(Name = "order_submit", Title = "Submit simplified order")]
    public async Task<object> OrderSubmit(string customer_id, List<OrderLineInput> items)
    {
        var input = new SimplifiedOrderInput
        {
            CustomerId = customer_id,
            Items = items.Select(i => new SimplifiedOrderItemInput
            {
                ItemType = Enum.Parse<ItemType>(i.item_type, ignoreCase: true),
                Qty = i.qty
            }).ToList()
        };

        var order = await _orderSubmitHandler.SubmitAsync(input);

        return new
        {
            ok = true,
            order_id = order.OrderId,
            customer_id = order.CustomerId,
            status = order.Status.ToString(),
            total = order.Total,
            placed_at = order.PlacedAt
        };
    }

    /// <summary>
    /// Simplified order line input contract.
    /// </summary>
    public sealed record OrderLineInput(string item_type, int qty);

    /// <summary>
    /// Non-interactive skill invocation input contract.
    /// </summary>
    public sealed record SkillInvokeArgs(
        string customer_id,
        string intent,
        List<OrderLineInput>? items,
        bool confirm = false);
}
