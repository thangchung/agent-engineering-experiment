namespace McpServer.CodeMode;

/// <summary>
/// Tracks the minimum discovery flow required before calling execute.
/// </summary>
public sealed class CodeModeWorkflowGuard
{
    private readonly object syncRoot = new();
    private bool searchSeen;
    private bool schemaSeen;

    /// <summary>
    /// Marks that a discovery search has run.
    /// </summary>
    public void MarkSearch()
    {
        lock (syncRoot)
        {
            searchSeen = true;
        }
    }

    /// <summary>
    /// Marks that schema lookup has run.
    /// </summary>
    public void MarkSchema()
    {
        lock (syncRoot)
        {
            schemaSeen = true;
        }
    }

    /// <summary>
    /// Ensures search and schema lookup were called before execute, then resets the workflow.
    /// </summary>
    public void EnsureReadyForExecuteAndReset()
    {
        lock (syncRoot)
        {
            if (!searchSeen || !schemaSeen)
            {
                throw new InvalidOperationException(
                    "Code mode execute requires discovery first. " +
                    "Call search, then get_schema, and only then call execute.");
            }

            searchSeen = false;
            schemaSeen = false;
        }
    }
}