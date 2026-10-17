using System.Text.Json;
using System.IO;
using NetPad.Presentation.Html;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Controls;

/// <summary>A clickable button in the results pane.</summary>
public sealed class Button : Control
{
    private string _text;

    public Button(string text = "Button") => _text = text;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            NotifyChanged();
        }
    }

    public event EventHandler? Click;

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var button = root.AddAndGetElement("button").AddClass("btn btn-primary btn-sm");
        button.SetAttribute("type", "button");
        if (SupportsInteraction) button.SetAttribute("data-netpad-event", "click");
        if (!Enabled || !SupportsInteraction) button.SetAttribute("disabled", "disabled");
        button.AddChild(TextNode.EscapedText(Text));
        return root;
    }

    internal override bool DispatchEvent(string eventType, JsonElement payload)
    {
        if (eventType != "click" || !Enabled) return false;
        Click?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override void OnDisposed() => Click = null;
}

/// <summary>A single-line text input.</summary>
public class TextBox : Control
{
    private string _value;
    private string? _placeholder;

    public TextBox(string value = "") => _value = value;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            NotifyChanged();
        }
    }

    public string? Placeholder
    {
        get => _placeholder;
        set
        {
            if (_placeholder == value) return;
            _placeholder = value;
            NotifyChanged();
        }
    }

    public event EventHandler? ValueChanged;

    internal override string ControlType => "TextBox";

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var input = root.AddAndGetElement("input").AddClass("form-control form-control-sm");
        input.SetAttribute("type", "text");
        if (SupportsInteraction) input.SetAttribute("data-netpad-event", "input change");
        input.SetAttribute("value", Value);
        if (Placeholder != null) input.SetAttribute("placeholder", Placeholder);
        if (!Enabled || !SupportsInteraction) input.SetAttribute("disabled", "disabled");
        return root;
    }

    internal override bool DispatchEvent(string eventType, JsonElement payload)
    {
        if (eventType is not ("input" or "change") || payload.ValueKind != JsonValueKind.String || !Enabled)
            return false;

        var value = payload.GetString() ?? string.Empty;
        if (_value != value)
        {
            ApplyEventUpdate(() => _value = value);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    protected override void OnDisposed() => ValueChanged = null;
}

/// <summary>A multi-line text input.</summary>
public sealed class TextArea : TextBox
{
    public TextArea(string value = "") : base(value) { }

    internal override string ControlType => "TextArea";

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var textarea = root.AddAndGetElement("textarea").AddClass("form-control form-control-sm");
        if (SupportsInteraction) textarea.SetAttribute("data-netpad-event", "input change");
        if (Placeholder != null) textarea.SetAttribute("placeholder", Placeholder);
        if (!Enabled || !SupportsInteraction) textarea.SetAttribute("disabled", "disabled");
        textarea.AddChild(TextNode.EscapedText(Value));
        return root;
    }
}

/// <summary>A checkbox with an optional adjacent label.</summary>
public sealed class CheckBox : Control
{
    private bool _checked;
    private string? _label;

    public CheckBox(bool isChecked = false, string? label = null)
    {
        _checked = isChecked;
        _label = label;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            NotifyChanged();
        }
    }

    public string? Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            NotifyChanged();
        }
    }

    public event EventHandler? CheckedChanged;

    internal override Element Render()
    {
        var root = CreateRoot("label").AddClass("form-check-label");
        var input = root.AddAndGetElement("input");
        input.AddClass("form-check-input");
        input.SetAttribute("type", "checkbox");
        if (SupportsInteraction) input.SetAttribute("data-netpad-event", "change");
        if (Checked) input.SetAttribute("checked", "checked");
        if (!Enabled || !SupportsInteraction) input.SetAttribute("disabled", "disabled");
        if (!string.IsNullOrEmpty(Label)) root.AddChild(TextNode.EscapedText(" " + Label));
        return root;
    }

    internal override bool DispatchEvent(string eventType, JsonElement payload)
    {
        if (eventType != "change" || payload.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !Enabled)
            return false;

        var value = payload.GetBoolean();
        if (_checked != value)
        {
            ApplyEventUpdate(() => _checked = value);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    protected override void OnDisposed() => CheckedChanged = null;
}

public sealed record SelectOption(string Value, string Text);

/// <summary>A select control whose option set and selected value can be changed at runtime.</summary>
public sealed class SelectBox : Control
{
    private readonly HashSet<string> _selectedValues = new(StringComparer.Ordinal);
    private bool _multiple;

    public SelectBox(IEnumerable<string>? items = null)
    {
        Options = new System.Collections.ObjectModel.ObservableCollection<SelectOption>(
            (items ?? []).Select(item => new SelectOption(item, item)));
        Options.CollectionChanged += OnOptionsChanged;
        if (Options.FirstOrDefault() is { } first) _selectedValues.Add(first.Value);
    }

    public System.Collections.ObjectModel.ObservableCollection<SelectOption> Options { get; }

    public string? SelectedValue
    {
        get => SelectedValues.FirstOrDefault();
        set => SetSelectedValues(value == null ? [] : [value]);
    }

    public IReadOnlyList<string> SelectedValues => Options
        .Where(option => _selectedValues.Contains(option.Value))
        .Select(option => option.Value)
        .ToArray();

    public bool Multiple
    {
        get => _multiple;
        set
        {
            if (_multiple == value) return;
            ApplyEventUpdate(() =>
            {
                _multiple = value;
                if (!value)
                {
                    var first = SelectedValue;
                    _selectedValues.Clear();
                    if (first != null) _selectedValues.Add(first);
                }
            });
            NotifyChanged();
        }
    }

    public event EventHandler? SelectionChanged;

    public void SetOptions(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacement = items.Select(item => new SelectOption(item, item)).ToArray();
        ApplyEventUpdate(() =>
        {
            Options.Clear();
            foreach (var item in replacement) Options.Add(item);
            _selectedValues.RemoveWhere(value => !Options.Any(option => option.Value == value));
            if (_selectedValues.Count == 0 && Options.FirstOrDefault() is { } first) _selectedValues.Add(first.Value);
        });
        NotifyChanged();
    }

    public void SetSelectedValues(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var replacement = values.Distinct(StringComparer.Ordinal).ToArray();
        if (!Multiple && replacement.Length > 1)
            throw new InvalidOperationException("Multiple values require the SelectBox.Multiple option.");
        if (replacement.Any(value => !Options.Any(option => option.Value == value)))
            throw new ArgumentException("A selected value is not present in the option set.", nameof(values));
        if (_selectedValues.SetEquals(replacement)) return;

        ApplyEventUpdate(() =>
        {
            _selectedValues.Clear();
            _selectedValues.UnionWith(replacement);
        });
        NotifyChanged();
    }

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var select = root.AddAndGetElement("select").AddClass("form-select form-select-sm");
        if (SupportsInteraction) select.SetAttribute("data-netpad-event", "change");
        if (!Enabled || !SupportsInteraction) select.SetAttribute("disabled", "disabled");
        if (Multiple) select.SetAttribute("multiple", "multiple");

        foreach (var option in Options)
        {
            var element = select.AddAndGetElement("option");
            element.SetAttribute("value", option.Value);
            if (_selectedValues.Contains(option.Value)) element.SetAttribute("selected", "selected");
            element.AddChild(TextNode.EscapedText(option.Text));
        }

        return root;
    }

    internal override bool DispatchEvent(string eventType, JsonElement payload)
    {
        if (eventType != "change" || payload.ValueKind != JsonValueKind.String || !Enabled) return false;

        string[] values;
        if (Multiple)
        {
            using var document = JsonDocument.Parse(payload.GetString() ?? "null");
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String)) return false;
            values = document.RootElement.EnumerateArray().Select(value => value.GetString()!).ToArray();
        }
        else
        {
            values = [payload.GetString() ?? string.Empty];
        }

        if (values.Any(value => !Options.Any(option => option.Value == value))) return false;
        if (!_selectedValues.SetEquals(values))
        {
            ApplyEventUpdate(() =>
            {
                _selectedValues.Clear();
                _selectedValues.UnionWith(values);
            });
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    protected override void OnDisposed()
    {
        SelectionChanged = null;
        Options.CollectionChanged -= OnOptionsChanged;
    }

    private void OnOptionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => NotifyChanged();
}

/// <summary>A block container for arbitrary dumpable children.</summary>
public sealed class Div : Control
{
    public Div() { }

    public Div(params object?[] children)
    {
        foreach (var child in children) Children.Add(child);
    }

    public Div(IEnumerable<object?> children) : this(children.ToArray()) { }

    internal override Element Render()
    {
        return CreateRoot("div");
    }

}

/// <summary>An inline container for arbitrary dumpable children.</summary>
public sealed class Span : Control
{
    public Span() { }

    public Span(params object?[] children)
    {
        foreach (var child in children) Children.Add(child);
    }

    public Span(IEnumerable<object?> children) : this(children.ToArray()) { }

    internal override Element Render()
    {
        var root = CreateRoot("div");
        root.AddClass("netpad-span");
        root.GetOrAddAttribute("style").Append("display:inline;");
        return root;
    }

}

/// <summary>An image in the results pane. Unlike <see cref="NetPad.Media.Image"/>, this is a live control.</summary>
public sealed class ImageControl : Control
{
    private string _source;
    private string? _alternateText;

    public ImageControl(string source, string? alternateText = null)
    {
        _source = source;
        _alternateText = alternateText;
    }

    public ImageControl(Uri source, string? alternateText = null) : this(source.ToString(), alternateText) { }

    public string Source
    {
        get => _source;
        set
        {
            if (_source == value) return;
            _source = value;
            NotifyChanged();
        }
    }

    public string? AlternateText
    {
        get => _alternateText;
        set
        {
            if (_alternateText == value) return;
            _alternateText = value;
            NotifyChanged();
        }
    }

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var image = root.AddAndGetElement("img");
        image.SetAttribute("src", ResolveImageSource(Source));
        image.SetAttribute("alt", AlternateText ?? string.Empty);
        image.GetOrAddAttribute("style").Append("max-width:100%;height:auto;");
        return root;
    }

    private static string ResolveImageSource(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)) return uri.ToString();
        return new Uri(Path.GetFullPath(source)).AbsoluteUri;
    }
}

public enum FlexDirection
{
    Row,
    Column
}

/// <summary>Lays out dumpable children in a responsive row or column.</summary>
public sealed class FlexBox : Control
{
    private FlexDirection _direction;

    public FlexBox(FlexDirection direction = FlexDirection.Row, params object?[] children)
    {
        _direction = direction;
        foreach (var child in children) Children.Add(child);
    }

    public FlexBox(string direction, params object?[] children)
        : this(Enum.TryParse<FlexDirection>(direction, true, out var parsed) ? parsed : FlexDirection.Row, children) { }

    public FlexDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value) return;
            _direction = value;
            NotifyChanged();
        }
    }

    internal override Element Render()
    {
        var root = CreateRoot("div");
        root.GetOrAddAttribute("style").Append($"display:flex;flex-direction:{(Direction == FlexDirection.Row ? "row" : "column")};flex-wrap:wrap;gap:12px;align-items:flex-start;");
        return root;
    }
}

public sealed record TabPage(string Name, object? Content);

/// <summary>A live tab strip whose selected page is synchronized with script state.</summary>
public sealed class TabControl : Control
{
    private string? _selectedTab;

    public TabControl(params TabPage[] tabs)
    {
        Tabs = new System.Collections.ObjectModel.ObservableCollection<TabPage>(tabs);
        Tabs.CollectionChanged += OnTabsChanged;
        _selectedTab = Tabs.FirstOrDefault()?.Name;
    }

    public System.Collections.ObjectModel.ObservableCollection<TabPage> Tabs { get; }

    public string? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value || !Tabs.Any(tab => tab.Name == value)) return;
            _selectedTab = value;
            NotifyChanged();
        }
    }

    public event EventHandler? SelectionChanged;

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var navigation = root.AddAndGetElement("div").AddClass("netpad-tab-list");
        navigation.GetOrAddAttribute("style").Append("display:flex;flex-wrap:wrap;gap:4px;margin-bottom:8px;");
        var content = root.AddAndGetElement("div").AddClass("netpad-tab-content");
        foreach (var tab in Tabs)
        {
            var button = navigation.AddAndGetElement("button").AddClass("btn btn-sm btn-outline-primary");
            button.SetAttribute("type", "button");
            if (SupportsInteraction) button.SetAttribute("data-netpad-event", "click");
            button.SetAttribute("data-netpad-value", tab.Name);
            button.SetAttribute("aria-selected", tab.Name == SelectedTab ? "true" : "false");
            if (tab.Name == SelectedTab) button.AddClass("active");
            if (!Enabled || !SupportsInteraction) button.SetAttribute("disabled", "disabled");
            button.AddChild(TextNode.EscapedText(tab.Name));

            var panel = content.AddAndGetElement("div");
            panel.AddClass("netpad-tab-panel");
            if (tab.Name != SelectedTab) panel.SetAttribute("hidden", "hidden");
            panel.AddChild(HtmlPresenter.SerializeToElement(tab.Content));
        }

        return root;
    }

    internal override bool DispatchEvent(string eventType, JsonElement payload)
    {
        if (eventType != "click" || payload.ValueKind != JsonValueKind.String || !Enabled) return false;
        var value = payload.GetString();
        if (Tabs.Any(tab => tab.Name == value) && _selectedTab != value)
        {
            ApplyEventUpdate(() => _selectedTab = value);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            NotifyChanged();
        }

        return true;
    }

    protected override void OnDisposed()
    {
        SelectionChanged = null;
        Tabs.CollectionChanged -= OnTabsChanged;
    }

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!Tabs.Any(tab => tab.Name == _selectedTab)) _selectedTab = Tabs.FirstOrDefault()?.Name;
        NotifyChanged();
    }
}

/// <summary>A live markdown source rendered by the results view.</summary>
public sealed class MarkdownViewer : Control
{
    private string _markdown;
    private bool _allowRawHtml;

    public MarkdownViewer(string markdown, bool allowRawHtml = false)
    {
        _markdown = markdown;
        _allowRawHtml = allowRawHtml;
    }

    public string Markdown
    {
        get => _markdown;
        set
        {
            if (_markdown == value) return;
            _markdown = value;
            NotifyChanged();
        }
    }

    public bool AllowRawHtml
    {
        get => _allowRawHtml;
        set
        {
            if (_allowRawHtml == value) return;
            _allowRawHtml = value;
            NotifyChanged();
        }
    }

    internal override Element Render()
    {
        var root = CreateRoot("div").AddClass("netpad-markdown");
        root.SetAttribute("data-allow-raw-html", AllowRawHtml ? "true" : "false");
        root.AddChild(TextNode.EscapedText(Markdown));
        return root;
    }
}

/// <summary>A live LaTeX source rendered by KaTeX in the results view.</summary>
public sealed class LatexViewer : Control
{
    private string _latex;
    private bool _displayMode;

    public LatexViewer(string latex, bool displayMode = true)
    {
        _latex = latex;
        _displayMode = displayMode;
    }

    public string Latex
    {
        get => _latex;
        set
        {
            if (_latex == value) return;
            _latex = value;
            NotifyChanged();
        }
    }

    public bool DisplayMode
    {
        get => _displayMode;
        set
        {
            if (_displayMode == value) return;
            _displayMode = value;
            NotifyChanged();
        }
    }

    internal override Element Render()
    {
        var root = CreateRoot("div").AddClass("netpad-latex");
        root.SetAttribute("data-display-mode", DisplayMode ? "true" : "false");
        root.AddChild(TextNode.EscapedText(Latex));
        return root;
    }
}

/// <summary>An isolated iframe; sandbox permissions are intentionally not configurable.</summary>
public sealed class IFrame : Control
{
    private string _source;

    public IFrame(string urlOrHtml) => _source = urlOrHtml;

    public string Source
    {
        get => _source;
        set
        {
            if (_source == value) return;
            _source = value;
            NotifyChanged();
        }
    }

    internal override Element Render()
    {
        var root = CreateRoot("div");
        var frame = root.AddAndGetElement("iframe");
        frame.SetAttribute("sandbox", "");
        frame.SetAttribute("referrerpolicy", "no-referrer");
        frame.GetOrAddAttribute("style").Append("width:100%;min-height:240px;border:0;");
        if (Uri.TryCreate(Source, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "data")
        {
            frame.SetAttribute("src", uri.ToString());
        }
        else
        {
            frame.SetAttribute("srcdoc", Source);
        }

        return root;
    }
}
