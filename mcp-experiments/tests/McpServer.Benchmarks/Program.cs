using BenchmarkDotNet.Running;

namespace McpServer.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
