using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgenticTodo.ServiceDefaults;
using Xunit;

namespace AgenticTodo.Tests.Integration;

// Can't use Aspire.Hosting.Testing's DistributedApplicationTestingBuilder.CreateAsync<Projects.X>
// -- that needs a Projects.* type from an AppHost project reference, but this repo's AppHost
// is file-based (no .csproj). Drives the real `aspire` CLI out-of-process instead, and skips
// (not fails) without Docker/Entra credentials.
[Trait("Category", "Integration")]
public sealed class AspireMeshIntegrationTests : IAsyncLifetime
{
    private const string TodoApiBaseUrl = "http://localhost:5001";
    private const string TodoAgentBaseUrl = "http://localhost:5002";

    private static readonly string RepoRoot = GetRepoRoot();
    private static readonly HttpClient Http = new();

    private Process? aspireProcess;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
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
    public async Task Full_dual_obo_chain_creates_and_persists_a_todo()
    {
        Skip.IfNot(File.Exists(Path.Combine(RepoRoot, ".env")), "requires .env (run scripts/setup-entra-obo-chain.ps1 first)");
        Skip.IfNot(await CommandSucceedsAsync("az", "account show"), "requires `az login`");
        Skip.IfNot(await CommandSucceedsAsync("docker", "info"), "requires a running Docker daemon");

        var dotEnv = LoadDotEnv();
        var todoApiClientId = RequireEnv(dotEnv, "TODOAPI_CLIENT_ID");
        var todoAgentClientId = RequireEnv(dotEnv, "TODOAGENT_BLUEPRINT_CLIENT_ID");

        await StartMeshAsync();
        await WaitForHealthyAsync($"{TodoApiBaseUrl}/health", TimeSpan.FromSeconds(90));

        var accessToken = await MintUserTokenAsync(todoApiClientId);
        var callerOid = DecodeClaim(accessToken, "oid");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{TodoApiBaseUrl}/todos")
        {
            Content = JsonContent.Create(new { name = "Integration test todo" }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST /todos failed with {response.StatusCode}: {body}");

        using var createdTodo = JsonDocument.Parse(body);
        Assert.Equal(callerOid, createdTodo.RootElement.GetProperty("userId").GetString());

        // TodoAgent's last-seen inbound token IS the post-hop-1-exchange token.
        using var lastTokenResponse = await Http.GetAsync($"{TodoAgentBaseUrl}/diagnostics/last-token");
        Assert.True(lastTokenResponse.IsSuccessStatusCode, "TodoAgent's /diagnostics/last-token was unreachable");

        var snapshot = await lastTokenResponse.Content.ReadFromJsonAsync<TokenDiagnosticsSnapshot>(JsonSerializerOptions.Web);
        Assert.NotNull(snapshot);
        Assert.Equal(todoAgentClientId, snapshot!.Aud);
        Assert.Contains("access_as_user", snapshot.Scp ?? string.Empty);
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

    private static async Task WaitForHealthyAsync(string healthUrl, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await Http.GetAsync(healthUrl);
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

        throw new TimeoutException($"{healthUrl} did not become healthy within {timeout}.");
    }

    private static async Task<string> MintUserTokenAsync(string todoApiClientId)
    {
        var (exitCode, stdout, stderr) = await RunCliAsync(
            "az", $"account get-access-token --scope api://{todoApiClientId}/access_as_user -o json", TimeSpan.FromSeconds(30));
        Assert.True(exitCode == 0, $"az account get-access-token failed: {stderr}");

        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("az account get-access-token returned no accessToken.");
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

    private static string DecodeClaim(string jwt, string claimName)
    {
        var segments = jwt.Split('.');
        var padded = segments[1].Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(padded));
        return document.RootElement.GetProperty(claimName).GetString()
            ?? throw new InvalidOperationException($"Token has no '{claimName}' claim.");
    }

    // .env.local is the committed placeholder template; .env is git-ignored and holds the
    // real values scripts/setup-entra-obo-chain.ps1 writes -- same layering as apphost.cs.
    private static Dictionary<string, string> LoadDotEnv()
    {
        var values = new Dictionary<string, string>();

        foreach (var fileName in new[] { ".env.local", ".env" })
        {
            var path = Path.Combine(RepoRoot, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex < 0)
                {
                    continue;
                }

                values[trimmed[..separatorIndex]] = trimmed[(separatorIndex + 1)..];
            }
        }

        return values;
    }

    private static string RequireEnv(Dictionary<string, string> dotEnv, string key) =>
        dotEnv.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : throw new InvalidOperationException($".env is missing {key}.");

    private static string GetRepoRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));
}
