using System.Text.Json;
using CounterService.Domain;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The Extract step (research.md §5.2): turns the conversation into catalog-validated order
/// lines. Jev cannot extract (research.md §0/§11.1 rule 6), so this is the one genuinely
/// generative step - and its output is never trusted blindly: unknown names are dropped,
/// quantities are clamped, and price always comes from the catalog (research.md §7).
/// </summary>
[SendsMessage(typeof(OrderDraft))]
[SendsMessage(typeof(Unclear))]
public sealed class ExtractExecutor(AIAgent counterAgent, IReadOnlyList<MenuItem> menu) : Executor<Accepted>("extract")
{
    private const int MaxQty = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override async ValueTask HandleAsync(Accepted message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var state = await context.ReadOrInitStateAsync(OrderState.Key, () => OrderState.Empty, OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);

        var prompt = ExtractPrompt.Build(state.History, menu);

        var lines = await TryExtractAsync(prompt, cancellationToken).ConfigureAwait(false)
            ?? await TryExtractAsync(prompt, cancellationToken).ConfigureAwait(false); // one re-ask on a parse failure

        object result = lines is { Count: > 0 }
            ? new OrderDraft(lines)
            : new Unclear("Sorry, I couldn't quite make out your order - could you tell me again what you'd like?");

        await context.SendMessageAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OrderLine>?> TryExtractAsync(string prompt, CancellationToken cancellationToken)
    {
        var response = await counterAgent.RunAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        OrderDraftDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<OrderDraftDto>(response.Text, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        return dto is null ? null : Validate(dto);
    }

    private List<OrderLine> Validate(OrderDraftDto dto)
    {
        var mergedQtyByItemId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in dto.Lines)
        {
            var item = FindMenuItem(line.Name);
            if (item is null || line.Qty <= 0)
            {
                continue; // never invent an item that was not on the menu (research.md §11.1 rule 6)
            }

            mergedQtyByItemId[item.Id] = mergedQtyByItemId.GetValueOrDefault(item.Id) + line.Qty;
        }

        return mergedQtyByItemId
            .Select(pair =>
            {
                var item = menu.First(m => m.Id == pair.Key);
                var clampedQty = Math.Min(pair.Value, MaxQty);
                return new OrderLine
                {
                    Name = item.Id,
                    Qty = clampedQty,
                    Price = item.PriceUsd, // never the LLM's number
                    Clamped = clampedQty != pair.Value,
                };
            })
            .ToList();
    }

    private MenuItem? FindMenuItem(string name) => menu.FirstOrDefault(m =>
        string.Equals(m.Id, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase));

    private sealed record OrderDraftDto(List<OrderLineDto> Lines);

    private sealed record OrderLineDto(string Name, int Qty);
}
