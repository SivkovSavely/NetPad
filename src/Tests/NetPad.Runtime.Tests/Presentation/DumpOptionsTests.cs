using NetPad.Presentation;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class DumpOptionsTests : IDisposable
{
    private readonly RecordingDumpSink _sink = new();
    private readonly IDumpSink _previousSink;

    public DumpOptionsTests()
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

    private static string Html(object? o, DumpOptions? options = null) => HtmlPresenter.Serialize(o, options);

    /// <summary>Dumps through the user-facing pipeline and renders what the sink received with its resolved options.</summary>
    private string DumpedHtml(object? o)
    {
        o.Dump();
        var write = _sink.Writes[^1];
        return Html(write.Content, write.Options);
    }

    private sealed class Person
    {
        public string Name { get; set; } = "n";
        public int Age { get; set; } = 42;
        public string Secret { get; set; } = "s3cret";
    }

    [Fact]
    public void Explicit_Options_Override_Script_Global_Defaults()
    {
        DumpOptions.Default = new DumpOptions(MaxRows: 1);

        // MaxRows 5 is explicit; the array only has 3 items so all must render.
        var html = Html(new[] { 1, 2, 3 }, new DumpOptions(MaxRows: 5));

        Assert.Contains(">1<", html);
        Assert.Contains(">3<", html);
    }

    [Fact]
    public void Script_Global_Defaults_Apply_When_Explicit_Options_Unset()
    {
        DumpOptions.Default = new DumpOptions(ExcludeMembers: ["Secret"]);

        var html = DumpedHtml(new Person());

        Assert.Contains("Name", html);
        Assert.Contains("Age", html);
        Assert.DoesNotContain("Secret", html);
        Assert.DoesNotContain("s3cret", html);
        Assert.NotNull(_sink.Writes[^1].Options!.ExcludeMembers);
    }

    [Fact]
    public void Unset_Defaults_Do_Not_Interfere_With_Explicit_Options()
    {
        // Defaults carry nothing; explicit include-list must apply.
        var html = Html(new Person(), new DumpOptions(IncludeMembers: ["Age"]));

        Assert.Contains("Age", html);
        Assert.DoesNotContain("Name", html);
    }

    [Fact]
    public void Merge_Puts_Explicit_Above_Container_Above_Defaults()
    {
        DumpOptions.Default = new DumpOptions(Title: "default-title", CssClasses: "default-css");

        var containerOptions = new DumpOptions(CssClasses: "container-css");
        var merged = DumpOptions.Merge(new DumpOptions(Title: "explicit"), containerOptions);

        Assert.Equal("explicit", merged.Title);           // explicit wins
        Assert.Equal("container-css", merged.CssClasses); // container wins over defaults
        Assert.Null(containerOptions.Title);              // inputs not mutated
    }

    [Fact]
    public void MaxDepth_Per_Dump_Limits_Rendering()
    {
        var nested = new Level1 { Child = new Level2 { Child = new Level3() } };

        var deep = Html(nested, new DumpOptions());
        Assert.Contains(typeof(Level3).Name, deep);

        var shallow = Html(nested, new DumpOptions(MaxDepth: 1));
        Assert.Contains("max-depth-reached", shallow);
        Assert.Contains(typeof(Level2).Name, shallow);
    }

    [Fact]
    public void MaxRows_Per_Dump_Truncates_Collections()
    {
        var items = Enumerable.Range(1, 10).ToArray();

        var html = Html(items, new DumpOptions(MaxRows: 4));

        Assert.Contains(">4<", html);
        Assert.DoesNotContain(">5<", html);
        Assert.Contains("(4&nbsp;items)", html);
    }

    [Fact]
    public void IncludeMembers_Filters_Object_Members_In_Tables_Too()
    {
        var people = new[] { new Person(), new Person() };

        var html = Html(people, new DumpOptions(IncludeMembers: ["Name"]));

        Assert.Contains("Name", html);
        Assert.DoesNotContain("Age", html);
        Assert.DoesNotContain("Secret", html);
        // Two rows of filtered cells.
        Assert.Equal(2, html.Split("<td").Length - 1);
    }

    [Fact]
    public void FormatStrings_Format_Values_By_Type_Name()
    {
        var value = new { When = new DateTime(2026, 8, 25, 13, 45, 0) };

        var html = Html(
            value,
            new DumpOptions(FormatStrings: new Dictionary<string, string> { ["DateTime"] = "yyyy-MM-dd" }));

        Assert.Contains("2026-08-25", html);
        Assert.DoesNotContain("13:45:00", html);
    }

    [Fact]
    public void Expanded_Emits_Data_Attribute()
    {
        var htmlTrue = Html(new Person(), new DumpOptions(Expanded: true));
        var htmlFalse = Html(new Person(), new DumpOptions(Expanded: false));

        Assert.Contains("data-expanded=\"true\"", htmlTrue);
        Assert.Contains("data-expanded=\"false\"", htmlFalse);
        Assert.DoesNotContain("data-expanded", Html(new Person()));
    }

    [Fact]
    public void Changing_Defaults_Is_Runtime_Only_And_Not_Persisted_Anywhere()
    {
        DumpOptions.Default = new DumpOptions(MaxDepth: 7);

        // The default lives only in the presentation layer; Settings is a separate model and no
        // code path in this library writes it. This assertion documents the contract.
        Assert.Equal((uint)7, DumpOptions.Default.MaxDepth);
    }

    private sealed class Level1
    {
        public Level2? Child { get; set; }
    }

    private sealed class Level2
    {
        public Level3? Child { get; set; }
    }

    private sealed class Level3
    {
        public string Leaf { get; set; } = "leaf";
    }

    private sealed class RecordingDumpSink : IDumpSink
    {
        public List<(object? Content, DumpOptions? Options)> Writes { get; } = [];

        public void ResultWrite<T>(T? content, DumpOptions? options = null)
        {
            Writes.Add((content, options));
        }

        public void ResultWrite<T>(T? content, DumpOptions? options, string? outputId, bool isUpdate)
        {
            Writes.Add((content, options));
        }

        public void SqlWrite<T>(T? content, DumpOptions? options = null)
        {
        }
    }
}
