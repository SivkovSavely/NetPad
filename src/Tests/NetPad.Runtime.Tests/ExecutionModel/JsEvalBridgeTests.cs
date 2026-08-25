using NetPad.ExecutionModel.ClientServer;
using NetPad.ExecutionModel.ClientServer.Messages;

namespace NetPad.Runtime.Tests.ExecutionModel;

/// <summary>
/// Host-side JS evaluation bridge: correlation ids pair requests with results; stale or unknown
/// correlation ids are rejected; errors propagate as script-side exceptions.
/// </summary>
public sealed class JsEvalBridgeTests
{
    [Fact]
    public async Task CorrelationIdPairsRequestAndResult()
    {
        var sent = new List<RequestJsEvalMessage>();
        var bridge = new JsEvalBridge(message => sent.Add(message));

        var evaluate = bridge.EvaluateAsync("1 + 1", 5_000);

        // Wait for the request to be sent, then reply with the matching correlation id.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (sent.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var request = Assert.Single(sent);
        Assert.Equal("1 + 1", request.Code);

        bridge.Receive(new JsEvalResultMessage(request.CorrelationId, "\"2\"", null));

        Assert.Equal("\"2\"", await evaluate);
    }

    [Fact]
    public async Task ErrorsPropagateAsScriptSideExceptions()
    {
        var correlationId = Guid.Empty;
        var bridge = new JsEvalBridge(message => correlationId = message.CorrelationId);

        var evaluate = bridge.EvaluateAsync("throw new Error('nope')", 5_000);
        bridge.Receive(new JsEvalResultMessage(correlationId, null, "Error: nope"));

        var exception = await Assert.ThrowsAsync<Exception>(() => evaluate);
        Assert.Contains("Error: nope", exception.Message);
    }

    [Fact]
    public async Task TimeoutThrowsAndLateResultsAreIgnored()
    {
        var sent = new List<RequestJsEvalMessage>();
        var bridge = new JsEvalBridge(message => sent.Add(message));

        await Assert.ThrowsAsync<TimeoutException>(
            () => bridge.EvaluateAsync("slow()", 50));

        var request = Assert.Single(sent);
        bridge.Receive(new JsEvalResultMessage(request.CorrelationId, "\"late\"", null)); // Must not throw.
    }
}
