using NetPad.ExecutionModel.ScriptServices;
using NetPad.Presentation;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class CompositionHelperTests
{
    [Fact]
    public void WordRun_Renders_Items_Inline()
    {
        var html = HtmlPresenter.Serialize(Util.WordRun("one", "two"));

        Assert.Contains("one", html);
        Assert.Contains("two", html);
        Assert.Contains("inline-flex", html);
    }

    [Fact]
    public void VerticalRun_Stacks_Items()
    {
        var html = HtmlPresenter.Serialize(Util.VerticalRun("top", "bottom"));

        Assert.Contains("top", html);
        Assert.Contains("bottom", html);
        Assert.Contains("flex-direction:column", html);
    }

    [Fact]
    public void WithHeading_Renders_Title_Above_Value()
    {
        var html = HtmlPresenter.Serialize(Util.WithHeading("my heading", "body text"));

        // Escaped-text rendering turns spaces into non-breaking spaces.
        Assert.Contains("my&nbsp;heading", html);
        Assert.Contains("body&nbsp;text", html);
        Assert.Contains("titled", html);
    }

    [Fact]
    public void HighlightIf_Highlights_Only_When_Predicate_Is_True()
    {
        var highlighted = HtmlPresenter.Serialize(Util.HighlightIf(10, v => v is int i && i > 5));
        Assert.Contains("highlight-if", highlighted);

        var notHighlighted = HtmlPresenter.Serialize(Util.HighlightIf(1, v => v is int i && i > 5));
        Assert.DoesNotContain("highlight-if", notHighlighted);
    }

    [Fact]
    public void WithCssClass_Adds_Class_To_Rendered_Output()
    {
        var html = HtmlPresenter.Serialize(Util.WithCssClass(new { A = 1 }, "text-success"));

        Assert.Contains("text-success", html);
    }

    [Fact]
    public void Helpers_Compose_Recursively()
    {
        var composed = Util.WithHeading(
            "outer",
            Util.VerticalRun(
                Util.WordRun("a", "b"),
                Util.WithCssClass("styled", "text-warning")));

        var html = HtmlPresenter.Serialize(composed);

        Assert.Contains("flex-direction:column", html);
        Assert.Contains("inline-flex", html);
        Assert.Contains("text-warning", html);
        Assert.Contains("styled", html);
        Assert.Contains("titled", html);
    }
}
