using LoopRuntime.Agents;
using LoopRuntime.Agents.Fakes;
using LoopRuntime.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoopRuntime.Tests;

public sealed class CheckerAgentTests
{
    [Fact]
    public async Task ReturnsOkWhenRunSucceeds()
    {
        var tools = new FakeTools(new RunResult(0, "ok", string.Empty, false));
        var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);

        var verdict = await checker.ReviewAsync("s1", "/tmp/main.py", CancellationToken.None);

        Assert.True(verdict.Ok);
        Assert.Null(verdict.Feedback);
    }

    [Fact]
    public async Task ReturnsFeedbackWhenRunFails()
    {
        var tools = new FakeTools(new RunResult(1, string.Empty, "NameError: x", false));
        var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);

        var verdict = await checker.ReviewAsync("s1", "/tmp/main.py", CancellationToken.None);

        Assert.False(verdict.Ok);
        Assert.NotNull(verdict.Feedback);
        Assert.Contains("NameError", verdict.Feedback!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsTimeoutFeedbackWhenRunTimesOut()
    {
        var tools = new FakeTools(new RunResult(-1, string.Empty, string.Empty, true));
        var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);

        var verdict = await checker.ReviewAsync("s1", "/tmp/main.py", CancellationToken.None);

        Assert.False(verdict.Ok);
        Assert.NotNull(verdict.Feedback);
        Assert.Contains("timed out", verdict.Feedback!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeTools : IMcpTools
    {
        private readonly RunResult _runResult;

        public FakeTools(RunResult runResult)
        {
            _runResult = runResult;
        }

        public Task<string> WriteFileAsync(string sessionId, string name, string content, CancellationToken cancellationToken) =>
            Task.FromResult("/tmp/main.py");

        public Task<string> EditFileAsync(string sessionId, string path, string content, CancellationToken cancellationToken) =>
            Task.FromResult(path);

        public Task<string> LoadFileAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult("print('ok')");

        public Task<RunResult> RunPythonAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(_runResult);
    }
}