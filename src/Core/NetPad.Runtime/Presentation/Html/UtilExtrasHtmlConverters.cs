using NetPad.ExecutionModel.ScriptServices;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Html;

public class HorizontalRunHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(HorizontalRun);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not HorizontalRun run)
        {
            throw new Exception($"Expected an object of type {typeof(HorizontalRun).FullName}, got {obj?.GetType().FullName}");
        }

        var container = new Element("div");
        container.GetOrAddAttribute("style").Append(
            $"display:flex;flex-wrap:{(run.WrapIfTooWide ? "wrap" : "nowrap")};gap:12px;align-items:flex-start;");

        foreach (var item in run.Items)
        {
            container.AddChild(HtmlPresenter.SerializeToElement(item));
        }

        return container;
    }
    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

}

public class StyledValueHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(StyledValue);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not StyledValue styled)
        {
            throw new Exception($"Expected an object of type {typeof(StyledValue).FullName}, got {obj?.GetType().FullName}");
        }

        var element = HtmlPresenter.SerializeToElement(styled.Value);
        element.GetOrAddAttribute("style").Append(styled.Style);
        return element;
    }
    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

}

public class HighlightedTextHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(HighlightedText);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not HighlightedText highlighted)
        {
            throw new Exception($"Expected an object of type {typeof(HighlightedText).FullName}, got {obj?.GetType().FullName}");
        }

        var span = new Element("span");

        if (!string.IsNullOrWhiteSpace(highlighted.BackgroundColor))
        {
            span.GetOrAddAttribute("style").Append($"background-color:{highlighted.BackgroundColor};");
        }

        if (!string.IsNullOrWhiteSpace(highlighted.ForegroundColor))
        {
            span.GetOrAddAttribute("style").Append($"color:{highlighted.ForegroundColor};");
        }

        span.AddChild(TextNode.EscapedText(highlighted.Text));
        return span;
    }
    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

}

public class OnDemandValueHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(OnDemandValue);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not OnDemandValue onDemand)
        {
            throw new Exception($"Expected an object of type {typeof(OnDemandValue).FullName}, got {obj?.GetType().FullName}");
        }

        var span = new Element("span");

        if (onDemand.Id == null)
        {
            // Value was evaluated eagerly; it is dumped separately.
            return span;
        }

        var anchor = span.AddAndGetElement("a");
        anchor.SetAttribute("href", "javascript:void(0)");
        anchor.SetAttribute("data-on-demand-id", onDemand.Id);
        anchor.GetOrAddAttribute("style").Append("cursor:pointer;text-decoration:underline dotted;");
        anchor.AddChild(TextNode.EscapedText(onDemand.Title));

        return span;
    }
    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

}

public class HyperlinqHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(Hyperlinq);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not Hyperlinq hyperlinq)
        {
            throw new Exception($"Expected an object of type {typeof(Hyperlinq).FullName}, got {obj?.GetType().FullName}");
        }

        var text = hyperlinq.GetDisplayText();

        var span = new Element("span");
        var anchor = span.AddAndGetElement("a");
        anchor.SetAttribute("href", "javascript:void(0)");
        anchor.GetOrAddAttribute("style").Append("cursor:pointer;text-decoration:underline dotted;");
        anchor.AddChild(TextNode.EscapedText(text));

        switch (hyperlinq.Kind)
        {
            case HyperlinqKind.Url:
                // Opened host-side so behavior is identical in the app window and external output windows.
                anchor.SetAttribute("data-netpad-action-id", UtilHyperlinqRegistry.RegisterUrl(hyperlinq.Url!));
                break;

            case HyperlinqKind.File:
                anchor.SetAttribute("data-source-path", hyperlinq.FilePath!);
                if (hyperlinq.LineNumber is > 0) anchor.SetAttribute("data-source-line", hyperlinq.LineNumber.Value.ToString());
                if (hyperlinq.ColumnNumber is > 0) anchor.SetAttribute("data-source-column", hyperlinq.ColumnNumber.Value.ToString());
                break;

            case HyperlinqKind.Action:
                anchor.SetAttribute("data-netpad-action-id", ScriptActionRegistry.Register(hyperlinq.ClickAction!));
                break;
        }

        return span;
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}

public class MarkdownContentHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(MarkdownContent);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not MarkdownContent markdown)
        {
            throw new Exception($"Expected an object of type {typeof(MarkdownContent).FullName}, got {obj?.GetType().FullName}");
        }

        var div = new Element("div").AddClass("netpad-markdown");
        div.SetAttribute("data-allow-raw-html", markdown.AllowRawHtml ? "true" : "false");
        div.AddChild(TextNode.EscapedText(markdown.Markdown));

        return div;
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}

public class LatexContentHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(LatexContent);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not LatexContent latex)
        {
            throw new Exception($"Expected an object of type {typeof(LatexContent).FullName}, got {obj?.GetType().FullName}");
        }

        var div = new Element("div").AddClass("netpad-latex");
        div.SetAttribute("data-display-mode", latex.DisplayMode ? "true" : "false");
        div.AddChild(TextNode.EscapedText(latex.Latex));

        return div;
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}
