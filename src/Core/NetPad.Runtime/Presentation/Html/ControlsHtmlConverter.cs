using NetPad.Presentation.Controls;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Html;

public sealed class ControlsHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type) => typeof(Control).IsAssignableFrom(type);

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is not Control control)
        {
            throw new InvalidOperationException($"Expected a control, got {obj?.GetType().FullName}.");
        }

        var root = control.Render();
        foreach (var child in control.Children)
        {
            root.AddChild(HtmlPresenter.SerializeToElement(child));
        }

        if (control.Content != null)
        {
            root.AddChild(HtmlPresenter.SerializeToElement(control.Content));
        }

        ControlRegistry.Register(control);
        root.SetAttribute("data-netpad-control-id", control.ControlId);
        root.SetAttribute("data-netpad-control-type", control.ControlType);
        if (!ControlSerializationContext.IsInteractive) root.SetAttribute("data-netpad-static-control", "true");
        return root;
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td").AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }
}
