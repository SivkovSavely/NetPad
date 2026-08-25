using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Html;

/// <summary>
/// Renders composition wrappers produced by Util output-composition helpers. Children are
/// serialized through the standard dump pipeline, so composition works recursively with objects,
/// nested runs, DumpContainer content, and other presentational wrappers.
/// </summary>
public class CompositionHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(VerticalSequence)
               || type == typeof(WordRun)
               || type == typeof(TitledValue)
               || type == typeof(ConditionallyHighlighted)
               || type == typeof(CssClassedValue);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        switch (obj)
        {
            case VerticalSequence sequence:
            {
                var container = new Element("div");
                container.GetOrAddAttribute("style").Append("display:flex;flex-direction:column;gap:6px;align-items:flex-start;");

                foreach (var item in sequence.Items)
                {
                    container.AddChild(HtmlPresenter.SerializeToElement(item));
                }

                return container;
            }

            case WordRun run:
            {
                var container = new Element("div");
                container.GetOrAddAttribute("style").Append("display:inline-flex;flex-wrap:wrap;gap:8px;align-items:baseline;");

                foreach (var item in run.Items)
                {
                    container.AddChild(HtmlPresenter.SerializeToElement(item));
                }

                return container;
            }

            case TitledValue titled:
            {
                return HtmlPresenter.SerializeToElement(titled.Value, new DumpOptions(Title: titled.Title));
            }

            case ConditionallyHighlighted highlighted:
            {
                var element = HtmlPresenter.SerializeToElement(highlighted.Value);
                if (highlighted.ShouldHighlight)
                {
                    element.AddClass("highlight-if");
                    element.GetOrAddAttribute("style").Append("background-color:#fff3cd;");
                }

                return element;
            }

            case CssClassedValue cssClassed:
            {
                var element = HtmlPresenter.SerializeToElement(cssClassed.Value);
                if (!string.IsNullOrWhiteSpace(cssClassed.CssClasses))
                {
                    element.AddClass(cssClassed.CssClasses);
                }

                return element;
            }

            default:
                throw new Exception($"Expected a composition wrapper type, got {obj?.GetType().FullName}");
        }
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td")
            .AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue)
            .AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}
