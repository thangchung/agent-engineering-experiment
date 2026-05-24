namespace McpServer.CodeMode.Validation;

public interface IPythonSyntaxValidator
{
    Task EnsureValidAsync(string code, CancellationToken ct);
}

public sealed class NullSyntaxValidator : IPythonSyntaxValidator
{
    public Task EnsureValidAsync(string code, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Task.CompletedTask;
    }
}

public sealed class SyntaxValidationException : InvalidOperationException
{
    public SyntaxValidationException(string message)
        : base(message)
    {
    }

    public SyntaxValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}