namespace McpServer.CodeMode.Validation;

public sealed class PreflightOptions
{
    public bool Enabled { get; set; }

    public string PythonPath { get; set; } = OperatingSystem.IsWindows() ? "python" : "python3";

    public int TimeoutMs { get; set; } = 2000;
}