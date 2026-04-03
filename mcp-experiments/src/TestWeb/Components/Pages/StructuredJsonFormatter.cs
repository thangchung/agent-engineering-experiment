using System.Text;
using System.Text.Json;

namespace TestWeb.Components.Pages;

public static class StructuredJsonFormatter
{
    public static string FormatLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        bool previousWasSeparator = true;

        foreach (char character in name)
        {
            if (character is '_' or '-')
            {
                if (!previousWasSeparator)
                {
                    builder.Append(' ');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (!previousWasSeparator
                && (char.IsUpper(character) || char.IsDigit(character)))
            {
                builder.Append(' ');
            }

            builder.Append(previousWasSeparator ? char.ToUpperInvariant(character) : character);
            previousWasSeparator = false;
        }

        return builder.ToString();
    }

    public static string FormatScalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.ToString(),
        };
    }

    public static bool TryCreateTable(JsonElement value, out StructuredJsonTable table)
    {
        table = default!;
        if (value.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        JsonElement.ArrayEnumerator enumerator = value.EnumerateArray();
        if (!enumerator.MoveNext() || enumerator.Current.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        List<string> columns = enumerator.Current.EnumerateObject().Select(static property => property.Name).ToList();
        if (columns.Count == 0)
        {
            return false;
        }

        List<IReadOnlyList<string>> rows = [];
        if (!TryCreateRow(enumerator.Current, columns, out IReadOnlyList<string> firstRow))
        {
            return false;
        }

        rows.Add(firstRow);

        while (enumerator.MoveNext())
        {
            if (enumerator.Current.ValueKind is not JsonValueKind.Object
                || !TryCreateRow(enumerator.Current, columns, out IReadOnlyList<string> row))
            {
                return false;
            }

            rows.Add(row);
        }

        table = new StructuredJsonTable(columns, rows);
        return true;
    }

    private static bool TryCreateRow(JsonElement item, IReadOnlyList<string> columns, out IReadOnlyList<string> row)
    {
        row = [];
        List<JsonProperty> properties = item.EnumerateObject().ToList();
        if (properties.Count != columns.Count)
        {
            return false;
        }

        List<string> values = [];
        foreach (string column in columns)
        {
            if (!item.TryGetProperty(column, out JsonElement propertyValue) || !IsScalar(propertyValue))
            {
                return false;
            }

            values.Add(FormatScalar(propertyValue));
        }

        row = values;
        return true;
    }

    private static bool IsScalar(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    }
}

public sealed record StructuredJsonTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);