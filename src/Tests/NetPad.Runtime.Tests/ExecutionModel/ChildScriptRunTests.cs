using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;

namespace NetPad.Runtime.Tests.ExecutionModel;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class ChildScriptRunTests : IDisposable
{
    private readonly string _scriptPath = Path.Combine(Path.GetTempPath(), $"netpad-child-{Guid.NewGuid():N}.netpad");

    public ChildScriptRunTests()
    {
        File.WriteAllText(_scriptPath, "child script");
        Util.BeginInteractiveRunScope();
    }

    public void Dispose()
    {
        Util.OnRequestChildScript = null;
        Util.EndInteractiveRunScope();
        File.Delete(_scriptPath);
    }

    [Fact]
    public async Task RunAsyncReturnsHandleAndRoutesCompletion()
    {
        Guid correlationId = Guid.Empty;
        string? requestedPath = null;
        Util.OnRequestChildScript = (id, path) =>
        {
            correlationId = id;
            requestedPath = path;
        };

        var handle = await Util.RunAsync(_scriptPath);

        Assert.Equal(Path.GetFullPath(_scriptPath), requestedPath);
        Assert.Equal(ChildScriptRunStatus.Pending, handle.Status);

        Util.ReceiveChildScriptCompleted(new ChildScriptCompletedMessage(
            correlationId,
            true,
            null,
            [new ScriptOutput(ScriptOutputKind.Result, 1, "child output")]));

        var result = await handle.Completion;
        Assert.True(result.Succeeded);
        Assert.True(handle.Succeeded);
        Assert.Equal(ChildScriptRunStatus.Completed, handle.Status);
        Assert.Equal("child output", Assert.Single(handle.Output).Body);
    }

    [Fact]
    public async Task StaleCompletionIsIgnoredAfterRunReset()
    {
        Guid correlationId = Guid.Empty;
        Util.OnRequestChildScript = (id, _) => correlationId = id;

        var handle = await Util.RunAsync(_scriptPath);
        Util.BeginInteractiveRunScope();

        var result = await handle.Completion;
        Assert.False(result.Succeeded);
        Assert.Contains("restarted", result.Error);

        // The old correlation id no longer targets a live handle.
        Util.ReceiveChildScriptCompleted(new ChildScriptCompletedMessage(correlationId, true, null, []));
        Assert.Equal(ChildScriptRunStatus.Failed, handle.Status);
    }
}
