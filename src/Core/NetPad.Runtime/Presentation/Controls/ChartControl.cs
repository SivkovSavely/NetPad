using System.Collections.ObjectModel;
using System.Text.Json;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation.Controls;

public enum ChartType
{
    Line,
    Bar,
    Column,
    Scatter,
    Pie
}

/// <summary>A data point for a NetPad-native chart.</summary>
public sealed record ChartDataPoint(string? Label, double X, double Y)
{
    public ChartDataPoint(string? label, double value) : this(label, 0, value) { }
}

/// <summary>
/// A mutable chart model rendered as responsive SVG in the app and as a data table in static
/// output sinks.
/// </summary>
public sealed class ChartControl : Control
{
    private ChartType _type;
    private string? _title;

    public ChartControl(IEnumerable<ChartDataPoint> data, ChartType type = ChartType.Line)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = new ObservableCollection<ChartDataPoint>(data);
        Data.CollectionChanged += OnDataChanged;
        _type = type;
    }

    public ChartControl(IEnumerable<double> values, ChartType type = ChartType.Line)
        : this(values.Select((value, index) => new ChartDataPoint(index.ToString(), index, value)), type) { }

    public ObservableCollection<ChartDataPoint> Data { get; }

    public ChartType Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            NotifyChanged();
        }
    }

    public string? Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            NotifyChanged();
        }
    }

    internal override string ControlType => "Chart";

    internal override Element Render()
    {
        var root = CreateRoot("div").AddClass("netpad-chart");
        root.SetAttribute("data-netpad-chart", "true");
        root.SetAttribute("data-chart-model", JsonSerializer.Serialize(
            new ChartModel(Type.ToString().ToLowerInvariant(), Title, Data.ToArray()),
            ChartJsonOptions));

        var table = root.AddAndGetElement("table").AddClass("netpad-chart-fallback");
        if (!string.IsNullOrWhiteSpace(Title))
        {
            var caption = table.AddAndGetElement("caption");
            caption.AddChild(TextNode.EscapedText(Title));
        }

        var header = table.AddAndGetElement("thead").AddAndGetElement("tr");
        header.AddAndGetElement("th").AddChild(TextNode.EscapedText("Label"));
        header.AddAndGetElement("th").AddChild(TextNode.EscapedText("X"));
        header.AddAndGetElement("th").AddChild(TextNode.EscapedText("Y"));
        var body = table.AddAndGetElement("tbody");
        foreach (var point in Data)
        {
            var row = body.AddAndGetElement("tr");
            row.AddAndGetElement("td").AddChild(TextNode.EscapedText(point.Label ?? string.Empty));
            row.AddAndGetElement("td").AddChild(TextNode.EscapedText(point.X.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
            row.AddAndGetElement("td").AddChild(TextNode.EscapedText(point.Y.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return root;
    }

    private sealed record ChartModel(string Type, string? Title, IReadOnlyList<ChartDataPoint> Points);

    private static readonly JsonSerializerOptions ChartJsonOptions = new(JsonSerializerDefaults.Web);

    protected override void OnDisposed() => Data.CollectionChanged -= OnDataChanged;

    private void OnDataChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => NotifyChanged();
}
