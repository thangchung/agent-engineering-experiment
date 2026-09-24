using System.Text.Json;
using CounterService.Domain;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// One station (barista or kitchen) preparing its lines (research.md §5.2). An empty order for
/// this station skips the LLM call entirely - the fan-in barrier still needs a message from
/// every source, so it always returns a ticket, never nothing (research.md B4/B8: any agent
/// failure falls back to the original coffeeshop-agent's deterministic text, never an
/// exception that could leave the barrier waiting forever).
/// </summary>
[SendsMessage(typeof(StationTicket))]
public sealed class StationExecutor(Station station, AIAgent agent, string id) : Executor<SplitOrder, StationTicket>(id)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override async ValueTask<StationTicket> HandleAsync(SplitOrder message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var mine = message.Lines.Where(l => l.Station == station).ToList();
        if (mine.Count == 0)
        {
            return StationTicket.Empty(station);
        }

        try
        {
            var payload = JsonSerializer.Serialize(mine.Select(l => new { l.Name, l.Qty }), Json);
            var response = await agent.RunAsync([new ChatMessage(ChatRole.User, payload)], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var dto = JsonSerializer.Deserialize<TicketDto>(response.Text, Json);
            if (dto is null || !CoversExactly(dto, mine))
            {
                return StationTicket.Fallback(station, mine);
            }

            return new StationTicket
            {
                Station = station,
                Items = dto.Items.Select(i => new TicketItem { Name = i.Name, Qty = i.Qty, Steps = i.Steps, Minutes = i.Minutes }).ToList(),
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Any agent failure - a thrown exception, malformed JSON, a dropped/invented item -
            // falls back to a deterministic ticket. The barrier must never be left waiting.
            return StationTicket.Fallback(station, mine);
        }
    }

    /// <summary>The agent must not drop, add, or change the quantity of a line it was given
    /// (research.md §10.5 R-T2).</summary>
    private static bool CoversExactly(TicketDto dto, List<OrderLine> lines)
    {
        if (dto.Items.Count != lines.Count)
        {
            return false;
        }

        var expected = lines.ToDictionary(l => l.Name, l => l.Qty, StringComparer.OrdinalIgnoreCase);
        foreach (var item in dto.Items)
        {
            if (!expected.TryGetValue(item.Name, out var qty) || qty != item.Qty)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record TicketDto(List<TicketItemDto> Items);

    private sealed record TicketItemDto(string Name, int Qty, List<string>? Steps, int? Minutes);
}
