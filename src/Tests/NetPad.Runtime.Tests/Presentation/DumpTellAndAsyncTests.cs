using NetPad.Presentation;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class DumpTellAndAsyncTests : IDisposable
{
    private readonly RecordingDumpSink _sink = new();
    private readonly IDumpSink _previousSink;
    private readonly Func<bool> _previousInteractive;

    public DumpTellAndAsyncTests()
    {
        _previousSink = DumpExtension.Sink;
        _previousInteractive = ProgressBar.IsInteractiveSink;
        DumpExtension.UseSink(_sink);
        ProgressBar.IsInteractiveSink = () => false;
        DumpOptions.Default = new DumpOptions();
    }

    public void Dispose()
    {
        DumpExtension.UseSink(_previousSink);
        ProgressBar.IsInteractiveSink = _previousInteractive;
        DumpOptions.Default = new DumpOptions();
    }

    [Fact]
    public void DumpTell_Uses_Source_Expression_As_Title()
    {
        var someComplicatedExpression = 42;
        someComplicatedExpression.DumpTell();

        Assert.Equal("someComplicatedExpression", _sink.Writes[^1].Options?.Title);
    }

    [Fact]
    public void DumpTell_Collapses_Multiline_Expressions()
    {
        var value = new[] { 1, 2 };
        value
            .Where(i => i > 0)
            .ToArray()
            .DumpTell();

        var title = _sink.Writes[^1].Options?.Title ?? "";
        Assert.DoesNotContain("\n", title);
        Assert.Contains("value", title);
    }

    [Fact]
    public void DumpTell_Explicit_Title_Wins()
    {
        "x".DumpTell("explicit");

        Assert.Equal("explicit", _sink.Writes[^1].Options?.Title ?? "");
    }

    [Fact]
    public void Ordinary_Dump_Does_Not_Gain_Expression_Titles()
    {
        var value = 5;
        value.Dump();

        Assert.Null(_sink.Writes[^1].Options?.Title);
    }


    [Fact]
    public async Task DumpAsync_Produces_Single_Block_In_NonInteractive_Session()
    {
        await Generate(5).DumpAsync("numbers");

        var resultWrites = _sink.Writes.Where(w => w.OutputId == null).ToList();
        var write = Assert.Single(resultWrites);

        var items = Assert.IsType<int[]>(write.Content);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
        Assert.Equal("numbers", write.Options?.Title);
    }

    [Fact]
    public async Task DumpAsync_Honors_Row_Limit()
    {
        await Generate(100).DumpAsync(options: new DumpOptions(MaxRows: 10));

        var write = _sink.Writes.Single(w => w.Content is int[]);
        Assert.Equal(10, ((int[])write.Content!).Length);
        Assert.Contains(_sink.Writes, w => w.Content is string s && s.Contains("Row limit reached"));
    }

    [Fact]
    public async Task DumpAsync_Reports_Errors_After_Partial_Rows_And_Rethrows()
    {
        static async IAsyncEnumerable<int> Faulting()
        {
            yield return 1;
            yield return 2;
            await Task.Yield();
            throw new InvalidOperationException("enumeration-boom");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Faulting().DumpAsync());

        var partial = Assert.IsType<int[]>(_sink.Writes.Single(w => w.Content is int[]).Content);
        Assert.Equal(new[] { 1, 2 }, partial);
        Assert.Contains(_sink.Writes, w => w.Content is string s && s.Contains("enumeration-boom") && (w.Options?.CssClasses ?? "").Contains("error"));
    }

    [Fact]
    public async Task DumpAsync_Stops_On_Cancellation()
    {
        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<int> CancellingSource(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; ; i++)
            {
                if (i == 5) cts.Cancel();
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }

        var dumped = await CancellingSource(cts.Token).DumpAsync(cancellationToken: cts.Token);

        Assert.Equal(5, dumped.Count);
        Assert.Contains(_sink.Writes, w => w.Content is string s && s.Contains("cancelled"));
    }

    [Fact]
    public async Task DumpAsync_Respects_MaxRows_From_Script_Global_Defaults()
    {
        DumpOptions.Default = new DumpOptions(MaxRows: 3);

        await Generate(50).DumpAsync();

        var write = _sink.Writes.Single(w => w.Content is int[]);
        Assert.Equal(3, ((int[])write.Content!).Length);
    }

    private static async IAsyncEnumerable<int> Generate(
        int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }

    private sealed class RecordingDumpSink : IDumpSink
    {
        public List<Write> Writes { get; } = [];

        public void ResultWrite<T>(T? content, DumpOptions? options = null)
        {
            Writes.Add(new Write(content, options, null));
        }

        public void ResultWrite<T>(T? content, DumpOptions? options, string? outputId, bool isUpdate)
        {
            Writes.Add(new Write(content, options, outputId));
        }

        public void SqlWrite<T>(T? content, DumpOptions? options = null)
        {
        }

        public sealed record Write(object? Content, DumpOptions? Options, string? OutputId);
    }
}
