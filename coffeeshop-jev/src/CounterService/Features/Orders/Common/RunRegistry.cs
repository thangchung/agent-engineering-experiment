using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CounterService.Features.Orders.Common;

public enum SubmitAnswerResult
{
    Ok,
    NotFound,
    NotPending,
}

/// <summary>
/// research.md §5.4/B5: the SSE loop is the ONLY code that ever touches a live workflow
/// <c>StreamingRun</c>. The <c>/answer</c> endpoint never sees the run itself - it just drops
/// text into this run's channel, and the SSE loop (already awaiting on that same channel)
/// picks it up and calls <c>SendResponseAsync</c> itself. This avoids any cross-request race on
/// the run object.
/// </summary>
public sealed class RunRegistry
{
    private sealed class RunState
    {
        public Channel<string> Answers { get; } = Channel.CreateBounded<string>(1);
        public volatile bool Pending;
    }

    private readonly ConcurrentDictionary<string, RunState> _runs = [];

    public string Register()
    {
        var runId = Guid.NewGuid().ToString("N");
        _runs[runId] = new RunState();
        return runId;
    }

    public void MarkPending(string runId)
    {
        if (_runs.TryGetValue(runId, out var state))
        {
            state.Pending = true;
        }
    }

    public async Task<string> WaitForAnswerAsync(string runId, CancellationToken cancellationToken)
    {
        var state = _runs[runId];
        var answer = await state.Answers.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        state.Pending = false;
        return answer;
    }

    public SubmitAnswerResult TrySubmitAnswer(string runId, string answer)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return SubmitAnswerResult.NotFound;
        }

        if (!state.Pending)
        {
            return SubmitAnswerResult.NotPending;
        }

        return state.Answers.Writer.TryWrite(answer) ? SubmitAnswerResult.Ok : SubmitAnswerResult.NotPending;
    }

    public void Remove(string runId) => _runs.TryRemove(runId, out _);
}
