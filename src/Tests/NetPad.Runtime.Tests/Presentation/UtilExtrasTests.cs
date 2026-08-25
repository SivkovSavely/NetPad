using System.Collections;
using System.Dynamic;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

public sealed class UtilExtrasTests : IDisposable
{
    private readonly RecordingSink _sink = new();
    private readonly IDumpSink _previousSink;

    public UtilExtrasTests()
    {
        _previousSink = DumpExtension.Sink;
        DumpExtension.UseSink(_sink);
    }

    public void Dispose()
    {
        DumpExtension.UseSink(_previousSink);
        ProgressBar.IsInteractiveSink = () => DumpExtension.Sink is global::NetPad.ExecutionModel.ClientServer.ClientServerDumpSink;
    }

    #region ToExpando

    [Fact]
    public void ToExpando_ConvertsAnonymousTypesRecursively()
    {
        var obj = new
        {
            Name = "n",
            Nested = new { Age = 5 },
            Items = new[] { new { X = 1 } }
        };

        var dict = (IDictionary<string, object?>)Util.ToExpando(obj);

        Assert.Equal("n", dict["Name"]);

        var nested = Assert.IsType<ExpandoObject>(dict["Nested"]);
        Assert.Equal(5, ((IDictionary<string, object?>)nested)["Age"]);

        var items = Assert.IsType<List<object?>>(dict["Items"]);
        var firstItem = Assert.IsType<ExpandoObject>(items[0]);
        Assert.Equal(1, ((IDictionary<string, object?>)firstItem)["X"]);
    }

    private sealed class PlainClass
    {
        public int A { get; set; }
    }

    [Fact]
    public void ToExpando_KeepsNonAnonymousObjectsAsIs()
    {
        var plain = new PlainClass();
        var obj = new { Value = plain };

        var dict = (IDictionary<string, object?>)Util.ToExpando(obj);

        Assert.Same(plain, dict["Value"]);
    }

    #endregion

    #region Pivot

    private sealed record Row(int Id, string Label);

    [Fact]
    public void Pivot_TransposesRowsIntoPropertyColumns()
    {
        var result = Util.Pivot(new[] { new Row(1, "a"), new Row(2, "b") });

        Assert.Equal(new List<object?> { 1, 2 }, result["Id"]);
        Assert.Equal(new List<object?> { "a", "b" }, result["Label"]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Pivot_NonGenericOverload_InfersPropertiesFromFirstRow()
    {
        IEnumerable rows = new[] { new Row(1, "a"), new Row(2, "b") };

        var result = Util.Pivot(rows);

        Assert.Equal(new List<object?> { 1, 2 }, result["Id"]);
        Assert.Equal(new List<object?> { "a", "b" }, result["Label"]);
    }

    [Fact]
    public void Pivot_EmptySource_ReturnsNoRows()
    {
        var genericResult = Util.Pivot(Array.Empty<Row>());
        Assert.Equal(2, genericResult.Count);
        Assert.All(genericResult.Values, v => Assert.Empty(v));

        Assert.Empty(Util.Pivot((IEnumerable)Array.Empty<Row>()));
    }

    #endregion

    #region GetAsmPath

    [Fact]
    public void GetAsmPath_ReturnsAssemblyLocation()
    {
        var expected = typeof(Util).Assembly.Location;

        Assert.Equal(expected, Util.GetAsmPath(typeof(Util).Assembly));
        Assert.Equal(expected, Util.GetAsmPath(typeof(Util)));
        Assert.Equal(expected, Util.GetAsmPath<DumpContainer>());
    }

    #endregion

    #region ReadLine

    [Fact]
    public void ReadLine_WritesPromptAndReadsInput()
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Console.SetIn(new StringReader("hello"));
            using var output = new StringWriter();
            Console.SetOut(output);

            var input = Util.ReadLine("Name? ");

            Assert.Equal("hello", input);
            Assert.Contains("Name?", output.ToString());
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    #endregion

    #region Cmd

    [Fact]
    public void Cmd_CapturesOutputAndErrors()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // covered implicitly on Windows by the same shell logic
        }

        var result = Util.Cmd("echo out-message && echo err-message 1>&2", echoOutput: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("out-message", result.Output);
        Assert.Contains("err-message", result.Errors);
    }

    #endregion

    #region Highlight / WithStyle / Metatext / HorizontalRun

    [Fact]
    public void Highlight_DumpsTextWithColors()
    {
        Util.Highlight("hi", backgroundColor: "#00ff00", foregroundColor: "red");

        var write = Assert.Single(_sink.Writes);
        var html = HtmlPresenter.SerializeToElement(write.Output).ToHtml();

        Assert.Contains("background-color:#00ff00", html);
        Assert.Contains("color:red", html);
        Assert.Contains(">hi<", html);
    }

    [Fact]
    public void WithStyle_AppliesInlineStyleToRenderedValue()
    {
        Util.WithStyle(new[] { new { A = 1 } }, "border:1px solid red").Dump();

        var write = Assert.Single(_sink.Writes);
        var html = HtmlPresenter.SerializeToElement(write.Output).ToHtml();

        Assert.Contains("border:1px solid red", html);
        Assert.Contains("A", html);
    }

    [Fact]
    public void Metatext_DumpsWithMetatextCssClass()
    {
        Util.Metatext("some meta info");

        var write = Assert.Single(_sink.Writes);
        Assert.Equal("metatext", write.Options?.CssClasses);
        Assert.Equal("some meta info", write.Output);
    }

    [Fact]
    public void HorizontalRun_LaysOutItemsSideBySide()
    {
        Util.HorizontalRun("left", 42).Dump();

        var write = Assert.Single(_sink.Writes);
        var html = HtmlPresenter.SerializeToElement(write.Output).ToHtml();

        Assert.Contains("display:flex", html);
        Assert.Contains("flex-wrap:wrap", html);
        Assert.Contains(">left<", html);
        Assert.Contains(">42<", html);
    }

    [Fact]
    public void HorizontalRun_NoWrap_IsRespected()
    {
        Util.HorizontalRun(wrapIfTooWide: false, "x").Dump();

        var write = Assert.Single(_sink.Writes);
        var html = HtmlPresenter.SerializeToElement(write.Output).ToHtml();

        Assert.Contains("flex-wrap:nowrap", html);
    }

    #endregion

    #region ProgressBar

    [Fact]
    public void ProgressBar_NonInteractiveSink_FallsBackToConsoleLines()
    {
        var originalOut = Console.Out;
        ProgressBar.IsInteractiveSink = () => false;

        try
        {
            using var console = new StringWriter();
            Console.SetOut(console);

            var progressBar = new ProgressBar("work");
            progressBar.Update(50, "halfway");

            var text = console.ToString();
            Assert.Matches(@"█{20}░{20} 50% halfway", text.TrimEnd());
            _sink.Writes.AssertNoWrites();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void ProgressBar_InteractiveSink_RendersAndUpdatesSameSlot()
    {
        ProgressBar.IsInteractiveSink = () => true;

        var progressBar = new ProgressBar("work");
        progressBar.Update(25);
        progressBar.Update(75);
        progressBar.Complete("done");

        Assert.Equal(3, _sink.Writes.Count);
        Assert.All(_sink.Writes, w => Assert.Equal(_sink.Writes[0].OutputId, w.OutputId));
        Assert.StartsWith("PB", _sink.Writes[0].OutputId);

        Assert.False(_sink.Writes[0].IsUpdate);
        Assert.True(_sink.Writes[1].IsUpdate);
        Assert.True(_sink.Writes[2].IsUpdate);

        var firstHtml = HtmlPresenter.SerializeToElement(_sink.Writes[0].Output).ToHtml();
        Assert.Contains("width:25%", firstHtml);

        var lastHtml = HtmlPresenter.SerializeToElement(_sink.Writes[2].Output).ToHtml();
        Assert.Contains("width:100%", lastHtml);
    }

    #endregion

    #region OnDemand

    [Fact]
    public void OnDemand_NonInteractiveSink_EvaluatesEagerly()
    {
        ProgressBar.IsInteractiveSink = () => false;
        var evaluations = 0;

        var onDemand = Util.OnDemand("lazy", () =>
        {
            evaluations++;
            return 123;
        });

        Assert.Null(onDemand.Id);
        Assert.Equal(1, evaluations);

        var write = Assert.Single(_sink.Writes);
        Assert.Equal(123, write.Output);
        Assert.Equal("lazy", write.Options?.Title);
    }

    [Fact]
    public void OnDemand_InteractiveSink_RegistersValueAndExpandsOnClick()
    {
        ProgressBar.IsInteractiveSink = () => true;
        var evaluations = 0;

        var onDemand = Util.OnDemand("lazy", () =>
        {
            evaluations++;
            return 123;
        });

        Assert.NotNull(onDemand.Id);
        Assert.Equal(0, evaluations);
        _sink.Writes.AssertNoWrites();

        Util.ExpandOnDemand(onDemand.Id!);
        Assert.Equal(1, evaluations);

        var write = Assert.Single(_sink.Writes);
        Assert.Equal(123, write.Output);
        Assert.Equal(onDemand.Id, write.OutputId);
        Assert.True(write.IsUpdate);

        // The value stays registered so it can be re-expanded.
        Util.ExpandOnDemand(onDemand.Id!);
        Assert.Equal(2, evaluations);
        Assert.Equal(2, _sink.Writes.Count);
    }

    [Fact]
    public void ExpandOnDemand_FactoryError_WritesErrorToUpdateSlot()
    {
        ProgressBar.IsInteractiveSink = () => true;

        var onDemand = Util.OnDemand<int>("boom", () => throw new InvalidOperationException("nope"));
        Util.ExpandOnDemand(onDemand.Id!);

        var write = Assert.Single(_sink.Writes);
        Assert.Contains("nope", Assert.IsType<string>(write.Output));
        Assert.Equal(onDemand.Id, write.OutputId);
        Assert.True(write.IsUpdate);
    }

    [Fact]
    public void ClearOnDemandRegistry_PreventsExpansion()
    {
        ProgressBar.IsInteractiveSink = () => true;

        var onDemand = Util.OnDemand("lazy", () => 1);
        Util.ClearOnDemandRegistry();

        Util.ExpandOnDemand(onDemand.Id!);

        _sink.Writes.AssertNoWrites();
    }

    #endregion

    #region Run

    [Fact]
    public void Run_ThrowsWhenNoRunScriptSenderIsWired()
    {
        Util.OnRequestRunScript = null;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"netpad-util-run-{Guid.NewGuid():N}.netpad");
        File.WriteAllText(scriptPath, "<Script />");

        try
        {
            Assert.Throws<InvalidOperationException>(() => Util.Run(scriptPath));
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public void Run_ThrowsWhenScriptFileDoesNotExist()
    {
        string? sent = null;
        Util.OnRequestRunScript = path => sent = path;

        Assert.Throws<FileNotFoundException>(() => Util.Run("/nonexistent/script.netpad"));
        Assert.Null(sent);
    }

    [Fact]
    public void Run_SendsFullPathOfExistingScript()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"netpad-util-run-{Guid.NewGuid():N}.netpad");
        File.WriteAllText(scriptPath, "<Script />");

        try
        {
            string? sent = null;
            Util.OnRequestRunScript = path => sent = path;

            Util.Run(scriptPath);

            Assert.Equal(Path.GetFullPath(scriptPath), sent);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    #endregion

    #region Image

    [Fact]
    public void Image_FromPath_WrapsFilePath()
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"netpad-img-{Guid.NewGuid():N}.png");
        using (File.Create(imagePath))
        {
        }

        try
        {
            var image = Util.Image(imagePath);

            Assert.Equal(imagePath, image.FilePath);
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public void Image_FromBytes_ProducesDataUri()
    {
        var image = Util.Image([1, 2, 3], "image/png");

        Assert.StartsWith("data:image/png;base64,", image.Base64Data);
    }

    #endregion

    private sealed class RecordingSink : IDumpSink
    {
        public List<(object? Output, DumpOptions? Options, string? OutputId, bool IsUpdate)> Writes { get; } = new();

        public void ResultWrite<T>(T? o, DumpOptions? options = null)
        {
            Writes.Add((o, options, null, false));
        }

        public void ResultWrite<T>(T? o, DumpOptions? options, string? outputId, bool isUpdate)
        {
            Writes.Add((o, options, outputId, isUpdate));
        }

        public void SqlWrite<T>(T? o, DumpOptions? options = null)
        {
        }
    }
}

file static class WriteCollectionAssertions
{
    public static void AssertNoWrites(
        this List<(object? Output, DumpOptions? Options, string? OutputId, bool IsUpdate)> writes)
    {
        Assert.True(writes.Count == 0, $"Expected no writes, got {writes.Count}.");
    }
}
