namespace LoopRuntime.Contracts;

public sealed record Feedback(
    string Reason,
    IReadOnlyList<string> Fixes)
{
    public override string ToString()
    {
        if (Fixes.Count == 0)
        {
            return $"Reason: {Reason}";
        }

        return $"Reason: {Reason}\nFixes:\n- {string.Join("\n- ", Fixes)}";
    }
}
