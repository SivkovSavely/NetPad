using NetPad.Presentation;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class DumpContainerSurfaceTests : IDisposable
{
    private readonly RecordingDumpSink _sink = new();
    private readonly IDumpSink _previousSink;

    public DumpContainerSurfaceTests()
    {
        _previousSink = DumpExtension.Sink;
        DumpExtension.UseSink(_sink);
        DumpOptions.Default = new DumpOptions();
    }

    public void Dispose()
    {
        DumpExtension.UseSink(_previousSink);
        DumpOptions.Default = new DumpOptions();
    }

    [Fact]
    public void First_Dump_Establishes_Slot_And_Updates_Replace_In_Place()
    {
        var container = new DumpContainer("initial").Dump();

        container.UpdateContent("second");

        Assert.Equal(2, _sink.Writes.Count);
        Assert.False(_sink.Writes[0].IsUpdate);
        Assert.True(_sink.Writes[1].IsUpdate);
        Assert.Equal(_sink.Writes[0].OutputId, _sink.Writes[1].OutputId);
    }

    [Fact]
    public void AppendContent_Accumulates_Inside_Same_Slot()
    {
        var container = new DumpContainer("a").Dump();
        container.AppendContent("b");
        container.AppendContent("c");

        Assert.Equal(3, _sink.Writes.Count);
        Assert.All(_sink.Writes, w => Assert.Equal(_sink.Writes[0].OutputId, w.OutputId));
        Assert.True(_sink.Writes[^1].IsUpdate);
        Assert.IsType<VerticalSequence>(_sink.Writes[^1].Content);
    }

    [Fact]
    public void ClearContent_Empties_Slot_Without_Removing_It()
    {
        var container = new DumpContainer("a").Dump();

        // Ordinary output between writes keeps its position.
        "ordinary".Dump();

        container.ClearContent();
        container.UpdateContent("re-filled");

        Assert.Equal(4, _sink.Writes.Count);
        Assert.Null(_sink.Writes[2].Content);          // cleared body
        Assert.True(_sink.Writes[2].IsUpdate);
        Assert.Equal("re-filled", _sink.Writes[3].Content);
        Assert.Same("ordinary", _sink.Writes[1].Content); // ordering preserved
    }

    [Fact]
    public void Container_Title_Css_And_Options_Flow_To_Writes()
    {
        var container = new DumpContainer("x")
        {
            Title = "my-title",
            CssClasses = "my-css",
            Options = new DumpOptions(MaxRows: 3),
        };

        container.Dump();

        var options = _sink.Writes[0].Options!;
        Assert.Equal("my-title", options.Title);
        Assert.Equal("my-css", options.CssClasses);
        Assert.Equal((uint)3, options.MaxRows);
    }

    [Fact]
    public void Container_Options_Fall_Back_To_Script_Global_Defaults()
    {
        DumpOptions.Default = new DumpOptions(MaxDepth: 9);

        var container = new DumpContainer("x") { Title = "t" };
        container.Dump();

        var options = _sink.Writes[0].Options!;
        Assert.Equal("t", options.Title);
        Assert.Equal((uint)9, options.MaxDepth);
    }

    [Fact]
    public void Two_Containers_Have_Stable_Distinct_OutputIds()
    {
        var a = new DumpContainer(1).Dump();
        var b = new DumpContainer(2).Dump();
        a.Refresh();

        Assert.NotEqual(_sink.Writes[0].OutputId, _sink.Writes[1].OutputId);
        Assert.Equal(_sink.Writes[0].OutputId, _sink.Writes[2].OutputId);
    }

    [Fact]
    public void Updates_Before_First_Dump_Do_Not_Create_Phantom_Slots()
    {
        var container = new DumpContainer();
        container.UpdateContent("before");
        container.AppendContent("still-before");
        container.Dump();

        Assert.Single(_sink.Writes);
        Assert.False(_sink.Writes[0].IsUpdate);
        Assert.IsType<VerticalSequence>(_sink.Writes[0].Content); // both items rendered on first dump
    }

    private sealed class RecordingDumpSink : IDumpSink
    {
        public List<Write> Writes { get; } = [];

        public void ResultWrite<T>(T? content, DumpOptions? options = null)
        {
            Writes.Add(new Write(content, options, null, false));
        }

        public void ResultWrite<T>(T? content, DumpOptions? options, string? outputId, bool isUpdate)
        {
            Writes.Add(new Write(content, options, outputId, isUpdate));
        }

        public void SqlWrite<T>(T? content, DumpOptions? options = null)
        {
        }

        public sealed record Write(object? Content, DumpOptions? Options, string? OutputId, bool IsUpdate);
    }
}
