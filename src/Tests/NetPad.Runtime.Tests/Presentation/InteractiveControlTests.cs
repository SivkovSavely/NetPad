using System.Text.Json;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;
using NetPad.Presentation.Controls;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class InteractiveControlTests : IDisposable
{
    private readonly RecordingSink _sink = new();
    private readonly IDumpSink _previousSink;
    private readonly Func<bool> _previousInteractiveSink;

    public InteractiveControlTests()
    {
        _previousSink = DumpExtension.Sink;
        _previousInteractiveSink = ProgressBar.IsInteractiveSink;
        DumpExtension.UseSink(_sink);
        ProgressBar.IsInteractiveSink = () => true;
        ControlRegistry.Clear();
    }

    public void Dispose()
    {
        ControlRegistry.Clear();
        DumpExtension.UseSink(_previousSink);
        ProgressBar.IsInteractiveSink = _previousInteractiveSink;
    }

    [Fact]
    public void Dump_RendersStableControlIdAndPropertyChangesUpdateSameSlot()
    {
        var button = new Button("before");
        button.Text = "pending";
        Assert.Empty(_sink.Writes);

        button.Dump();

        var first = Assert.Single(_sink.Writes);
        Assert.Equal(button.ControlId, first.OutputId);
        Assert.False(first.IsUpdate);
        Assert.Contains($"data-netpad-control-id=\"{button.ControlId}\"", first.Html);
        Assert.Contains("data-netpad-control-type=\"Button\"", first.Html);

        button.Text = "after";

        Assert.Equal(2, _sink.Writes.Count);
        Assert.Equal(button.ControlId, _sink.Writes[1].OutputId);
        Assert.True(_sink.Writes[1].IsUpdate);
        Assert.Contains("after", _sink.Writes[1].Html);
    }

    [Fact]
    public void Events_UpdateTextCheckboxAndSelectionWithoutEchoingInput()
    {
        var text = new TextBox("start");
        var textarea = new TextArea("multi-line");
        var checkbox = new CheckBox(false, "Enabled");
        var select = new SelectBox(["a", "b"]);
        string? changedText = null;
        string? changedArea = null;
        var checkboxChanged = false;
        var selectionChanged = false;
        text.ValueChanged += (_, _) => changedText = text.Value;
        textarea.ValueChanged += (_, _) => changedArea = textarea.Value;
        checkbox.CheckedChanged += (_, _) => checkboxChanged = true;
        select.SelectionChanged += (_, _) => selectionChanged = true;

        text.Dump();
        textarea.Dump();
        checkbox.Dump();
        select.Dump();
        var writeCount = _sink.Writes.Count;

        Util.DispatchControlEvent(new(text.ControlId, "input", JsonSerializer.Serialize("typed")));
        Util.DispatchControlEvent(new(textarea.ControlId, "change", JsonSerializer.Serialize("line two")));
        Util.DispatchControlEvent(new(checkbox.ControlId, "change", "true"));
        Util.DispatchControlEvent(new(select.ControlId, "change", JsonSerializer.Serialize("b")));

        Assert.Equal("typed", text.Value);
        Assert.Equal("typed", changedText);
        Assert.Equal("line two", textarea.Value);
        Assert.Equal("line two", changedArea);
        Assert.True(checkbox.Checked);
        Assert.True(checkboxChanged);
        Assert.Equal("b", select.SelectedValue);
        Assert.True(selectionChanged);
        Assert.Equal(writeCount, _sink.Writes.Count);

        select.SetOptions(["b", "c"]);
        Assert.Equal(writeCount + 1, _sink.Writes.Count);
        Assert.Contains("value=\"c\"", _sink.Writes[^1].Html);

        var multiSelect = new SelectBox(["a", "b", "c"]) { Multiple = true };
        multiSelect.Dump();
        var encodedValues = JsonSerializer.Serialize(new[] { "b", "c" });
        Util.DispatchControlEvent(new(multiSelect.ControlId, "change", JsonSerializer.Serialize(encodedValues)));
        Assert.Equal(new[] { "b", "c" }, multiSelect.SelectedValues);
    }

    [Fact]
    public void TabControlSelectionAndContainerCompositionStayMutable()
    {
        var changed = false;
        var tabs = new TabControl(new TabPage("one", new Span("first")), new TabPage("two", new Div("second")));
        tabs.SelectionChanged += (_, _) => changed = true;
        tabs.Dump();

        Util.DispatchControlEvent(new(tabs.ControlId, "click", JsonSerializer.Serialize("two")));

        Assert.Equal("two", tabs.SelectedTab);
        Assert.True(changed);
        Assert.Equal(tabs.ControlId, _sink.Writes[^1].OutputId);
        Assert.True(_sink.Writes[^1].IsUpdate);
        Assert.Contains("netpad-tab-panel", _sink.Writes[^1].Html);

        var flex = new FlexBox(FlexDirection.Column, "top", new ImageControl("data:image/png;base64,AA=="));
        flex.Dump();
        Assert.Contains("flex-direction:column", _sink.Writes[^1].Html);
        Assert.Contains("data:image/png;base64,AA==", _sink.Writes[^1].Html);
    }

    [Fact]
    public void ControlAttributesCannotInstallInlineHandlers()
    {
        var control = new Div();
        Assert.Throws<ArgumentException>(() => control.SetAttribute("onclick", "alert(1)"));
        Assert.Throws<ArgumentException>(() => control.SetAttribute("srcdoc", "<script></script>"));
        Assert.Throws<ArgumentException>(() => control.SetAttribute("style", "color:red"));
        Assert.Throws<ArgumentException>(() => control.SetAttribute("data-netpad-control-id", "spoofed"));
    }

    [Fact]
    public void NestedControlEventsAndMutationsUseParentSlot()
    {
        var clicks = 0;
        var button = new Button("nested");
        button.Click += (_, _) => clicks++;
        var div = new Div(button);

        div.Dump();
        Assert.True(button.IsRegistered);

        Assert.Contains($"data-netpad-control-id=\"{button.ControlId}\"", _sink.Writes[0].Html);
        Util.DispatchControlEvent(new(button.ControlId, "click", "null"));
        Assert.Equal(1, clicks);

        button.Text = "updated child";

        Assert.Equal(div.ControlId, _sink.Writes[^1].OutputId);
        Assert.True(_sink.Writes[^1].IsUpdate);
        Assert.Contains("updated&nbsp;child", _sink.Writes[^1].Html);

        div.Children.Clear();
        var count = _sink.Writes.Count;
        Util.DispatchControlEvent(new(button.ControlId, "click", "null"));
        Assert.Equal(1, clicks);
        Assert.Equal(count, _sink.Writes.Count);
    }

    [Fact]
    public void ClearAndDisposeInvalidateEventsAndStopSlotUpdates()
    {
        var clicks = 0;
        var button = new Button("live");
        button.Click += (_, _) => clicks++;
        button.Dump();

        ControlRegistry.Clear();
        Util.DispatchControlEvent(new(button.ControlId, "click", "null"));
        button.Text = "ignored";

        Assert.Equal(0, clicks);
        Assert.Single(_sink.Writes);
        Assert.Throws<ObjectDisposedException>(() => button.Dump());
    }

    [Fact]
    public void ViewersAndChartRenderSafeFallbacksAndUpdateLiveSources()
    {
        var markdown = new MarkdownViewer("# old");
        var latex = new LatexViewer("x");
        markdown.Dump();
        latex.Dump();
        Assert.True(markdown.IsRegistered);
        Assert.True(latex.IsRegistered);
        markdown.Markdown = "# new";
        latex.Latex = "y^2";

        Assert.Contains("#&nbsp;new", _sink.Writes[^2].Html);
        Assert.Contains("y^2", _sink.Writes[^1].Html);

        var iframeHtml = HtmlPresenter.Serialize(new IFrame("<h1>content</h1>"));
        Assert.Contains("sandbox=\"\"", iframeHtml);
        Assert.Contains("srcdoc", iframeHtml);

        var chart = Util.Chart(new[] { 2d, 4d }, ChartType.Column);
        var chartHtml = HtmlPresenter.Serialize(chart);
        Assert.Contains("data-chart-model", chartHtml);
        Assert.Contains("netpad-chart-fallback", chartHtml);
        Assert.Contains("column", chartHtml);
    }

    [Fact]
    public void NonInteractiveDumpIsStaticAndFormControlsAreDisabled()
    {
        ProgressBar.IsInteractiveSink = () => false;
        var button = new Button("static");

        button.Dump();

        var write = Assert.Single(_sink.Writes);
        Assert.Null(write.OutputId);
        Assert.Contains("data-netpad-static-control=\"true\"", write.Html);
        Assert.Contains("disabled=\"disabled\"", write.Html);
    }

    private sealed class RecordingSink : IDumpSink
    {
        public List<(object? Output, DumpOptions? Options, string? OutputId, bool IsUpdate, string Html)> Writes { get; } = [];

        public void ResultWrite<T>(T? o, DumpOptions? options = null) => Add(o, options, null, false);

        public void ResultWrite<T>(T? o, DumpOptions? options, string? outputId, bool isUpdate) => Add(o, options, outputId, isUpdate);

        public void SqlWrite<T>(T? o, DumpOptions? options = null) { }

        private void Add<T>(T? output, DumpOptions? options, string? outputId, bool isUpdate)
            => Writes.Add((output, options, outputId, isUpdate, HtmlPresenter.Serialize(output, options)));
    }
}
