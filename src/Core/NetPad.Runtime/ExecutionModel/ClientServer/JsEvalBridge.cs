using System.Collections.Concurrent;
using NetPad.ExecutionModel.ClientServer.Messages;

namespace NetPad.ExecutionModel.ClientServer;

/// <summary>
/// Correlates JavaScript evaluation requests made by a script with results returned from the
/// app. Unknown/stale correlation ids (timed-out evaluations or previous runs) are ignored.
/// </summary>
public sealed class JsEvalBridge(Action<RequestJsEvalMessage> send)
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<JsEvalOutcome>> _pending = new();

    /// <summary>
    /// Sends an evaluation request and waits for its result or the timeout.
    /// </summary>
    /// <exception cref="TimeoutException">When no result arrives within <paramref name="timeoutMs"/>.</exception>
    /// <exception cref="Exception">When the evaluation failed client-side.</exception>
    public async Task<string?> EvaluateAsync(string code, int timeoutMs)
    {
        var correlationId = Guid.NewGuid();
        var pending = new TaskCompletionSource<JsEvalOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = pending;

        send(new RequestJsEvalMessage(correlationId, code, timeoutMs));

        var completed = await Task.WhenAny(pending.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);

        if (completed != pending.Task)
        {
            _pending.TryRemove(correlationId, out _);
            throw new TimeoutException($"The JavaScript evaluation timed out after {timeoutMs}ms.");
        }

        var outcome = await pending.Task.ConfigureAwait(false);

        if (!string.IsNullOrEmpty(outcome.Error))
        {
            throw new Exception($"The JavaScript evaluation failed: {outcome.Error}");
        }

        return outcome.ResultJson;
    }

    public void Receive(JsEvalResultMessage message)
    {
        // Unknown/stale correlation ids are ignored.
        if (!_pending.TryRemove(message.CorrelationId, out var pending))
        {
            return;
        }

        pending.TrySetResult(new JsEvalOutcome(message.ResultJson, message.Error));
    }

    /// <summary>
    /// Completes all requests from a run that is being discarded. Later responses for those
    /// correlation ids are ignored because they have been removed from the pending map.
    /// </summary>
    public void Reset(string error)
    {
        foreach (var pair in _pending.ToArray())
        {
            if (_pending.TryRemove(pair.Key, out var pending))
            {
                pending.TrySetResult(new JsEvalOutcome(null, error));
            }
        }
    }
}

public readonly record struct JsEvalOutcome(string? ResultJson, string? Error);
