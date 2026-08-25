using System.Linq;
using NetPad.Presentation.Diff;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Html;

/// <summary>
/// Renders <see cref="DiffResult"/> as a human-readable difference report: colored line diffs for
/// text comparisons and structured rows (path/values) for structural differences.
/// </summary>
public class DiffResultHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return type == typeof(DiffResult);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not DiffResult result)
        {
            throw new Exception($"Expected an object of type {typeof(DiffResult).FullName}, got {obj?.GetType().FullName}");
        }

        var container = new Element("div").AddClass("dif-result");

        if (!result.HasDifferences)
        {
            container.AddAndGetElement("span").AddEscapedText("Values are equal.");
            return container;
        }

        if (result.TextDiffs.Count > 0)
        {
            var textBlock = container.AddAndGetElement("div").AddClass("dif-text");
            foreach (var line in result.TextDiffs)
            {
                var lineEl = textBlock.AddAndGetElement("div").AddClass("dif-line");
                switch (line.Kind)
                {
                    case DiffKind.Added:
                        lineEl.AddClass("dif-added");
                        lineEl.GetOrAddAttribute("style").Append("background-color:#d1e7dd;");
                        lineEl.AddEscapedText($"+ {line.Text}");
                        break;
                    case DiffKind.Removed:
                        lineEl.AddClass("dif-removed");
                        lineEl.GetOrAddAttribute("style").Append("background-color:#f8d7da;");
                        lineEl.AddEscapedText($"- {line.Text}");
                        break;
                    case DiffKind.Changed:
                        lineEl.AddClass("dif-changed");
                        lineEl.GetOrAddAttribute("style").Append("background-color:#fff3cd;");
                        lineEl.AddEscapedText($"~ {line.Text}");
                        break;
                }
            }
        }

        var structuralEntries = StructuralEntries(result).ToList();

        if (structuralEntries.Count == 0)
        {
            return container;
        }

        var table = container.AddAndGetElement("table").AddClass("dif-table");
        var head = table.AddAndGetElement("thead").AddAndGetElement("tr");
        head.AddAndGetElement("th").AddEscapedText("Path");
        head.AddAndGetElement("th").AddEscapedText("Difference");

        var body = table.AddAndGetElement("tbody");
        foreach (var entry in structuralEntries)
        {
            var tr = body.AddAndGetElement("tr");
            tr.AddAndGetElement("td").AddEscapedText(entry.Path);

            var td = tr.AddAndGetElement("td");
            switch (entry.Kind)
            {
                case DiffKind.Added:
                    td.GetOrAddAttribute("style").Append("background-color:#d1e7dd;");
                    td.AddEscapedText($"+ {FormatValue(entry.Right)}");
                    break;
                case DiffKind.Removed:
                    td.GetOrAddAttribute("style").Append("background-color:#f8d7da;");
                    td.AddEscapedText($"- {FormatValue(entry.Left)}");
                    break;
                default:
                    td.GetOrAddAttribute("style").Append("background-color:#fff3cd;");
                    td.AddEscapedText($"{FormatValue(entry.Left)} => {FormatValue(entry.Right)}");
                    break;
            }
        }

        return container;
    }

    private static IEnumerable<DiffEntry> StructuralEntries(DiffResult result)
    {
        // A pure textual comparison (string vs string) renders entirely as the line block above.
        if (result.TextDiffs.Count > 0 && result.Entries.All(e => e.Path is "text"))
        {
            yield break;
        }

        foreach (var entry in result.Entries.Where(e => e.Path is not ("text" or "value")))
        {
            yield return entry;
        }

        // Root-level non-text changed values (path "value") render as a table row.
        foreach (var entry in result.Entries.Where(e => e.Path == "value"))
        {
            yield return entry;
        }
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td")
            .AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue)
            .AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "(null)";

        if (value is string s)
        {
            const int max = 200;
            return s.Length <= max ? $"\"{s}\"" : $"\"{s[..max]}…\"";
        }

        var text = value.ToString();
        if (text is { Length: > 200 })
        {
            text = text[..200] + "…";
        }

        return text ?? value.GetType().Name;
    }
}
