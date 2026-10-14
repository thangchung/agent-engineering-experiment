using System.Globalization;
using WebApp.Models;

namespace WebApp;

public static class Money
{
    public static string Format(decimal amount) => "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Summary(IReadOnlyList<LineInfo> lines, Func<string, string> display)
    {
        if (lines.Count == 0)
        {
            return "nothing";
        }

        var parts = lines.Select(l => $"{l.Qty} {display(l.Name).ToLowerInvariant()}").ToList();
        return parts.Count > 1 ? string.Join(", ", parts[..^1]) + " and " + parts[^1] : parts[0];
    }
}
