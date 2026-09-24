using System.Text.Json.Nodes;

namespace Jev.Client;

/// <summary>
/// One of Jev's three primitives (research.md §2): choice, score, noul.
/// Construct with the static factories; <see cref="ToJson"/> is used internally by
/// <see cref="JevClient"/> to build the request body.
/// </summary>
public abstract class JevQuestion
{
    private protected JevQuestion(string instructions)
    {
        Instructions = instructions;
    }

    public string Instructions { get; }

    /// <summary>
    /// "Which of these options?" <paramref name="criteria"/> maps an option name to its
    /// description (or null for no description). At most 255 options (Jev API limit).
    /// </summary>
    public static JevQuestion Choice(string instructions, IReadOnlyDictionary<string, string?> criteria) =>
        new ChoiceQuestion(instructions, criteria);

    /// <summary>
    /// "Which level?" <paramref name="levels"/> is an ordered list, index 0 is the lowest level.
    /// 2 to 10 levels (Jev API limit).
    /// </summary>
    public static JevQuestion Score(string instructions, IReadOnlyList<string> levels) =>
        new ScoreQuestion(instructions, levels);

    /// <summary>
    /// "Is this true?" <paramref name="criteria"/> is optional: descriptions for "true"/"false".
    /// </summary>
    public static JevQuestion Noul(string instructions, IReadOnlyDictionary<string, string>? criteria = null) =>
        new NoulQuestion(instructions, criteria);

    internal abstract JsonObject ToJson();

    private sealed class ChoiceQuestion(string instructions, IReadOnlyDictionary<string, string?> criteria)
        : JevQuestion(instructions)
    {
        internal override JsonObject ToJson()
        {
            var criteriaNode = new JsonObject();
            foreach (var (key, value) in criteria)
            {
                criteriaNode[key] = value is null ? null : JsonValue.Create(value);
            }

            return new JsonObject
            {
                ["type"] = "choice",
                ["instructions"] = Instructions,
                ["criteria"] = criteriaNode,
            };
        }
    }

    private sealed class ScoreQuestion(string instructions, IReadOnlyList<string> levels)
        : JevQuestion(instructions)
    {
        internal override JsonObject ToJson() => new()
        {
            ["type"] = "score",
            ["instructions"] = Instructions,
            ["criteria"] = new JsonArray(levels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
        };
    }

    private sealed class NoulQuestion(string instructions, IReadOnlyDictionary<string, string>? criteria)
        : JevQuestion(instructions)
    {
        internal override JsonObject ToJson()
        {
            var node = new JsonObject
            {
                ["type"] = "noul",
                ["instructions"] = Instructions,
            };

            if (criteria is not null)
            {
                var criteriaNode = new JsonObject();
                foreach (var (key, value) in criteria)
                {
                    criteriaNode[key] = JsonValue.Create(value);
                }

                node["criteria"] = criteriaNode;
            }

            return node;
        }
    }
}
