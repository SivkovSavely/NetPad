using NetPad.Common;
using NetPad.ExecutionModel.ClientServer;
using NetPad.Presentation;

namespace NetPad.Runtime.Tests.Presentation;

public sealed class DumpContainerTests : IDisposable
{
    private readonly RecordingDumpSink _sink = new();
    private readonly IDumpSink _previousSink;

    public DumpContainerTests()
    {
        _previousSink = DumpExtension.Sink;
        DumpExtension.UseSink(_sink);
    }

    [Fact]
    public void DumpContainer_WritesInitialValueAndUpdatesTheSameOutput()
    {
        _ = new DumpContainer("initial").Dump();
        var empty = new DumpContainer().Dump();
        empty.Content = "populated";
        empty.UpdateContent("updated");
        empty.Refresh();

        var ordinary = "ordinary".Dump();

        var initialWrite = _sink.Writes[0];
        Assert.Equal("initial", initialWrite.Content);
        Assert.NotNull(initialWrite.OutputId);
        Assert.False(initialWrite.IsUpdate);

        var containerWrites = _sink.Writes.Skip(1).Take(4).ToArray();
        Assert.Equal(4, containerWrites.Length);
        Assert.NotNull(containerWrites[0].OutputId);
        Assert.False(containerWrites[0].IsUpdate);
        Assert.Null(containerWrites[0].Content);
        Assert.All(containerWrites, write => Assert.Equal(containerWrites[0].OutputId, write.OutputId));
        Assert.All(containerWrites.Skip(1), write => Assert.True(write.IsUpdate));
        Assert.Equal("updated", containerWrites[2].Content);
        Assert.Same(ordinary, _sink.Writes[5].Content);
        Assert.Null(_sink.Writes[5].OutputId);
        Assert.False(_sink.Writes[5].IsUpdate);
    }

    [Fact]
    public async Task ClientServerOutputHtmlWriter_SerializesOutputIdAndUpdate()
    {
        var messages = new List<string>();
        var writer = new ClientServerOutputHtmlWriter(message =>
        {
            messages.Add(message);
            return Task.CompletedTask;
        });

        await writer.WriteResultAsync("value", new DumpOptions { Order = 7 }, "output-id", true);
        await writer.WriteResultAsync("ordinary", new DumpOptions { Order = 8 });

        var updated = JsonSerializer.Deserialize<ScriptOutput>(messages[0]);
        var ordinary = JsonSerializer.Deserialize<ScriptOutput>(messages[1]);
        Assert.Equal((uint)7, updated!.Order);
        Assert.Equal("output-id", updated.OutputId);
        Assert.True(updated.IsUpdate);
        Assert.Equal((uint)8, ordinary!.Order);
        Assert.Null(ordinary.OutputId);
        Assert.False(ordinary.IsUpdate);
    }

    [Fact]
    public void Refresh_ReEmitsTheSameMutableReferenceWithTheSameOutputId()
    {
        var content = new List<int> { 1 };
        var container = new DumpContainer(content);
        container.Dump();
        var initial = _sink.Writes[^1];
        content.Add(3);
        container.Refresh();
        var update = _sink.Writes[^1];

        Assert.NotSame(initial, update);
        Assert.Same(content, update.Content);
        Assert.Equal(initial.OutputId, update.OutputId);
        Assert.True(update.IsUpdate);
    }

    public void Dispose()
    {
        DumpExtension.UseSink(_previousSink);
    }

    private sealed class RecordingDumpSink : IDumpSink
    {
        public List<Write> Writes { get; } = [];

        public void ResultWrite<T>(T? content, DumpOptions? options = null)
        {
            Writes.Add(new Write(content, null, false));
        }

        public void ResultWrite<T>(T? content, DumpOptions? options, string? outputId, bool isUpdate)
        {
            Writes.Add(new Write(content, outputId, isUpdate));
        }

        public void SqlWrite<T>(T? content, DumpOptions? options = null)
        {
        }

        public sealed record Write(object? Content, string? OutputId, bool IsUpdate);
    }
}
