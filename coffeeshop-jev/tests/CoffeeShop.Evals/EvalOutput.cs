using System.Text.Json;

namespace CoffeeShop.Evals;

/// <summary>`evals/out/*.jsonl` - shared by every eval test that writes rows for
/// <see cref="Report"/> to read back.</summary>
public static class EvalOutput
{
    public static void WriteJsonl<T>(string fileName, IEnumerable<T> rows, JsonSerializerOptions json)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "evals", "out");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, fileName), rows.Select(r => JsonSerializer.Serialize(r, json)));
    }
}
