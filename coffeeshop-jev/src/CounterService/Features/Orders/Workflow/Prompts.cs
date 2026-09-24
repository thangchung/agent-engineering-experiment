using CounterService.Domain;

namespace CounterService.Features.Orders.Workflow;

// research.md §10.4: "Prompts are shared, not copied." These pure functions are called by both
// the real executors (ExtractExecutor, ClarifyExecutor, DeliverExecutor) and, later, the L2
// agent evals - so an eval of the prompt is an eval of the real thing, never a copy of it.

public static class ExtractPrompt
{
    public static string Build(IReadOnlyList<string> conversation, IReadOnlyList<MenuItem> menu)
    {
        var latest = conversation.Count > 0 ? conversation[^1] : string.Empty;
        var history = conversation.Count > 1 ? string.Join(" | ", conversation.Take(conversation.Count - 1)) : "(none)";

        return $$"""
            Menu:
            {{FormatMenu(menu)}}

            Earlier turns: {{history}}
            Customer's latest request: "{{latest}}"

            Extract the final order (after any corrections) as JSON:
            {"lines":[{"name":"<exact menu name>","qty":<integer>}]}
            If nothing on the menu was ordered, return {"lines":[]}.
            """;
    }

    private static string FormatMenu(IReadOnlyList<MenuItem> menu) =>
        string.Join('\n', menu.Select(m => $"- {m.Id} ({m.DisplayName}), ${m.PriceUsd}"));
}

public static class ClarifyPrompt
{
    public static string Build(string reason, IReadOnlyList<MenuItem> menu)
    {
        var menuNames = string.Join(", ", menu.Select(m => m.DisplayName));
        return $"""
            The customer's order needs clarification. Reason: {reason}
            Menu items: {menuNames}

            Write one short, friendly question (English, exactly one "?", no prices) to ask the
            customer next.
            """;
    }
}

public static class DeliverPrompt
{
    public static string Build(IReadOnlyList<OrderLine> lines, decimal total, bool anyClamped)
    {
        var linesText = string.Join(", ", lines.Select(l => $"{l.Qty}x {l.Name} (${l.Price})"));
        var clampNote = anyClamped ? " Mention that one quantity was reduced to the maximum of 20." : string.Empty;

        return $"""
            The order is confirmed: {linesText}
            Total: ${total}
            {clampNote}
            Write a short, friendly confirmation reply (English) telling the customer their order
            is being prepared. Do not promise anything beyond what was ordered.
            """;
    }
}
