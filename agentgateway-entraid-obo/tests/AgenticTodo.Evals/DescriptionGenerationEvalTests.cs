using System.Diagnostics;
using System.Runtime.CompilerServices;
using AgenticTodo.TodoAgent;
using AgenticTodo.TodoAgent.Adapters.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticTodo.Evals;

// Real IChatClient through the gateway's LLM route -- no fakes. Skips (doesn't fail)
// without Docker/Foundry credentials.
[Trait("Category", "Integration")]
public sealed class DescriptionGenerationEvalTests : IAsyncLifetime
{
    private const int MaxWords = 40;
    private const double RequiredPassRate = 0.90;
    private const string GatewayLlmEndpoint = "http://localhost:4000";

    private static readonly string RepoRoot = GetRepoRoot();

    private static readonly string[] TodoNames =
    [
        "Buy milk", "Walk the dog", "Pay electricity bill", "Book dentist appointment",
        "Renew passport", "Water the plants", "Schedule car maintenance", "Return library books",
        "Plan weekend trip", "Clean the garage", "Update resume", "Call mom",
        "Submit expense report", "Buy birthday gift for Sam", "Fix the leaky faucet",
        "Backup laptop files", "Review pull request", "Prepare tax documents",
        "Book flight to Chicago", "Cancel gym membership", "Order new glasses",
        "Reserve restaurant for anniversary", "Send thank-you card", "Refill prescription",
        "Set up home Wi-Fi router", "Donate old clothes", "Learn basic Spanish phrases",
        "Organize the bookshelf", "Schedule annual health checkup", "Write weekly status update",
    ];

    private Process? aspireProcess;
    private IChatClient? chatClient;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        (chatClient as IDisposable)?.Dispose();

        if (aspireProcess is null)
        {
            return;
        }

        await RunCliAsync("aspire", "stop", TimeSpan.FromSeconds(30));
        if (!aspireProcess.HasExited)
        {
            aspireProcess.Kill(entireProcessTree: true);
        }

        aspireProcess.Dispose();
    }

    [SkippableFact]
    public async Task Description_generation_passes_the_rubric_on_at_least_90_percent_of_cases()
    {
        Skip.IfNot(File.Exists(Path.Combine(RepoRoot, ".env")), "requires .env (run scripts/setup-entra-obo-chain.ps1 first)");
        Skip.IfNot(await CommandSucceedsAsync("docker", "info"), "requires a running Docker daemon");

        await StartMeshAsync();
        await WaitForGatewayLlmRouteAsync(TimeSpan.FromSeconds(90));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentGateway:LlmEndpoint"] = GatewayLlmEndpoint,
                ["AI:Model"] = "agentic-todo", // must match gateway.yaml llm.models[].name, not the raw deployment name
            })
            .Build();

        var services = new ServiceCollection();
        services.AddChatClient(configuration);
        using var provider = services.BuildServiceProvider();
        chatClient = provider.GetRequiredService<IChatClient>();

        var generator = new ChatClientDescriptionGenerator(chatClient);

        var failures = new List<string>();
        foreach (var name in TodoNames)
        {
            var description = await generator.GenerateAsync(name, CancellationToken.None);
            var verdict = Rubric(name, description);
            if (verdict is not null)
            {
                failures.Add($"'{name}' -> '{description}': {verdict}");
                continue;
            }

            if (!await IsTopicallyRelatedAsync(name, description))
            {
                failures.Add($"'{name}' -> '{description}': judged not topically related");
            }
        }

        var passRate = (double)(TodoNames.Length - failures.Count) / TodoNames.Length;
        Assert.True(
            passRate >= RequiredPassRate,
            $"Pass rate {passRate:P0} below the {RequiredPassRate:P0} threshold.{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static string? Rubric(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "empty description";
        }

        var wordCount = description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount > MaxWords ? $"{wordCount} words (limit {MaxWords})" : null;
    }

    private async Task<bool> IsTopicallyRelatedAsync(string name, string description)
    {
        var judgePrompt =
            $"Todo: \"{name}\"\nDescription: \"{description}\"\n" +
            "Does the description relate to the todo? Reply with exactly one word: yes or no.";
        var response = await chatClient!.GetResponseAsync(judgePrompt, cancellationToken: CancellationToken.None);
        return response.Text.Trim().StartsWith("yes", StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartMeshAsync()
    {
        aspireProcess = new Process
        {
            StartInfo = new ProcessStartInfo("aspire", "run --non-interactive")
            {
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        aspireProcess.Start();
        await Task.CompletedTask;
    }

    private static async Task WaitForGatewayLlmRouteAsync(TimeSpan timeout)
    {
        using var http = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // No dedicated health path on the llm listener; a model-list request proves it's live.
                using var response = await http.GetAsync($"{GatewayLlmEndpoint}/v1/models");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"{GatewayLlmEndpoint} did not become reachable within {timeout}.");
    }

    private static async Task<bool> CommandSucceedsAsync(string command, string arguments)
    {
        var (exitCode, _, _) = await RunCliAsync(command, arguments, TimeSpan.FromSeconds(15));
        return exitCode == 0;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string command, string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(command, arguments)
            {
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty, $"{command} is not on PATH");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        await process.WaitForExitAsync(cts.Token);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string GetRepoRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
