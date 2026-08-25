using System.Reflection;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class InteractiveTypesTests : IDisposable
{
    private readonly RecordingSink _sink = new();
    private readonly IDumpSink _previousSink;

    public InteractiveTypesTests()
    {
        _previousSink = DumpExtension.Sink;
        DumpExtension.UseSink(_sink);
    }

    public void Dispose()
    {
        DumpExtension.UseSink(_previousSink);
        Util.OnResultHostCommand = null;
        Util.OnHtmlHeadChanged = null;
        Util.OnRequestJsEval = null;
    }

    #region Hyperlinq

    [Fact]
    public void Hyperlinq_UrlLink_RendersHostDispatchedAction()
    {
        var html = HtmlPresenter.Serialize(new Hyperlinq(new Uri("https://example.com")));

        Assert.Contains("data-netpad-action-id", html);
        Assert.Contains("https://example.com", html);
    }

    [Fact]
    public void Hyperlinq_FileLink_RendersSourcePathAttributes()
    {
        var html = HtmlPresenter.Serialize(Hyperlinq.File("/tmp/x.cs", 12, 3));

        Assert.Contains("data-source-path=\"/tmp/x.cs\"", html);
        Assert.Contains("data-source-line=\"12\"", html);
        Assert.Contains("data-source-column=\"3\"", html);
        Assert.DoesNotContain("data-netpad-action-id", html);
    }

    [Fact]
    public void Hyperlinq_ActionLink_DispatchesRegisteredHandler()
    {
        var invoked = false;
        var html = HtmlPresenter.Serialize(new Hyperlinq(() => invoked = true, "click me"));

        var actionId = ExtractActionId(html);
        Assert.False(invoked);

        Util.InvokeScriptAction(actionId);

        Assert.True(invoked);
    }

    [Fact]
    public void Hyperlinq_StaleActionIdsFailSilently()
    {
        var html = HtmlPresenter.Serialize(new Hyperlinq(() => { }, "gone"));
        var actionId = ExtractActionId(html);

        ScriptActionRegistry.Clear();

        Util.InvokeScriptAction(actionId); // Must not throw.

        Assert.Empty(_sink.Writes);
    }

    [Fact]
    public void Hyperlinq_ActionExceptionsAreSurfacedAsErrorDumps()
    {
        var html = HtmlPresenter.Serialize(new Hyperlinq(() => throw new InvalidOperationException("boom"), "fail"));
        var actionId = ExtractActionId(html);

        Util.InvokeScriptAction(actionId);

        var errorWrite = _sink.Writes.Last(w => w.Output is Exception);
        Assert.Contains("boom", ((Exception)errorWrite.Output!).Message);
    }

    [Fact]
    public void IdenticalUrlsShareOneRegisteredAction()
    {
        HtmlPresenter.Serialize(new Hyperlinq(new Uri("https://dup.example/")));
        var first = ExtractActionId(HtmlPresenter.Serialize(new Hyperlinq(new Uri("https://dup.example/"))));

        var second = ExtractActionId(HtmlPresenter.Serialize(new Hyperlinq(new Uri("https://dup.example/"))));

        Assert.Equal(first, second);
    }

    private static string ExtractActionId(string html)
    {
        var marker = "data-netpad-action-id=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No action id in: {html}");
        start += marker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }

    #endregion

    #region Markdown / LaTeX

    [Fact]
    public void Markdown_ConverterStoresEscapedContent()
    {
        var html = HtmlPresenter.Serialize(Util.Markdown("# Hi\n<script>alert(1)</script>"));

        Assert.Contains("netpad-markdown", html);
        Assert.Contains("data-allow-raw-html=\"false\"", html);
        // The raw HTML must not be present unescaped.
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Markdown_AllowRawHtml_PassesThroughUnescaped()
    {
        var html = HtmlPresenter.Serialize(Util.Markdown("<b>bold</b>", allowRawHtml: true));

        Assert.Contains("data-allow-raw-html=\"true\"", html);
        Assert.Contains("&lt;b&gt;", html); // Stored escaped in DOM text; client renders raw when allowed.
        Assert.DoesNotContain("data-allow-raw-html=\"false\"", html);
    }

    [Fact]
    public void Latex_ConverterCarriesDisplayModeFlag()
    {
        var html = HtmlPresenter.Serialize(Util.Latex("\\frac{1}{2}", displayMode: false));

        Assert.Contains("netpad-latex", html);
        Assert.Contains("data-display-mode=\"false\"", html);
    }

    #endregion

    #region Util.HtmlHead

    [Fact]
    public void HtmlHead_DedupesIdenticalEntriesAndNotifiesChanges()
    {
        var notifications = new List<ScriptHtmlHeadEntry[]>();
        Util.OnHtmlHeadChanged = entries => notifications.Add(entries);

        Util.HtmlHead.AddCss("body{}");
        Util.HtmlHead.AddCss("body{}"); // Duplicate
        Util.HtmlHead.AddScriptLink("https://cdn.example/x.js");

        var last = notifications.Last();
        Assert.Equal(2, last.Length);
        Assert.Equal(HtmlHeadEntryType.Css, (HtmlHeadEntryType)last[0].Type);
        Assert.Equal(HtmlHeadEntryType.ScriptLink, (HtmlHeadEntryType)last[1].Type);
    }

    [Fact]
    public void HtmlHead_ResetClearsEntries()
    {
        Util.BeginInteractiveRunScope();
        Util.HtmlHead.AddCss("x{}");
        Assert.Single(HtmlHeadStateForTest.Snapshot());

        Util.BeginInteractiveRunScope();
        try
        {
            Assert.Empty(HtmlHeadStateForTest.Snapshot());
        }
        finally
        {
            Util.EndInteractiveRunScope();
        }
    }

    private static class HtmlHeadStateForTest
    {
        public static ScriptHtmlHeadEntry[] Snapshot() =>
            (ScriptHtmlHeadEntry[])typeof(Util)
                .Assembly
                .GetType("NetPad.ExecutionModel.ScriptServices.HtmlHeadState")!
                .GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [])!;
    }

    #endregion

    #region Result host commands + panels

    [Fact]
    public void ResultCommands_RouteThroughSeam_WhenInteractive()
    {
        var sent = new List<(ResultHostCommand Command, string? Payload)>();
        Util.OnResultHostCommand = (command, payload) => sent.Add((command, payload));

        Util.ClearResults();
        Util.HideEditor();
        Util.AutoScrollResults(true);

        Assert.Equal(
            [(ResultHostCommand.ClearResults, null), (ResultHostCommand.HideEditor, null), (ResultHostCommand.AutoScrollResults, "true")],
            sent);
    }

    [Fact]
    public void ResultCommands_DegradeWithNotice_WhenNotInteractive()
    {
        Util.ClearResults();

        var write = Assert.Single(_sink.Writes);
        Assert.Contains("metatext", write.Options?.CssClasses);
    }

    [Fact]
    public async Task PanelDumps_AreTaggedWithPanelName()
    {
        ScriptOutput? captured = null;
        var writer = new global::NetPad.ExecutionModel.ClientServer.ClientServerOutputHtmlWriter(
            json => { captured = global::NetPad.Common.JsonSerializer.Deserialize<ScriptOutput>(json); return Task.CompletedTask; });

        using (OutputRouting.EnterPanel("MyPanel"))
        {
            await writer.WriteResultAsync(new { X = 1 });
        }

        Assert.NotNull(captured);
        Assert.Equal("MyPanel", captured.PanelName);
        Assert.Null(captured.OutputId);
    }

    [Fact]
    public async Task MainResults_AreNotTaggedWithPanelName()
    {
        ScriptOutput? captured = null;
        var writer = new global::NetPad.ExecutionModel.ClientServer.ClientServerOutputHtmlWriter(
            json => { captured = global::NetPad.Common.JsonSerializer.Deserialize<ScriptOutput>(json); return Task.CompletedTask; });

        await writer.WriteResultAsync(new { X = 1 });

        Assert.NotNull(captured);
        Assert.Null(captured.PanelName);
    }

    #endregion

    private sealed class RecordingSink : IDumpSink
    {
        public List<(object? Output, DumpOptions? Options, string? OutputId, bool IsUpdate)> Writes { get; } = [];

        public void ResultWrite<T>(T? o, DumpOptions? options = null) => Writes.Add((o, options, null, false));

        public void ResultWrite<T>(T? o, DumpOptions? options, string? outputId, bool isUpdate) => Writes.Add((o, options, outputId, isUpdate));

        public void SqlWrite<T>(T? o, DumpOptions? options = null)
        {
        }
    }
}
