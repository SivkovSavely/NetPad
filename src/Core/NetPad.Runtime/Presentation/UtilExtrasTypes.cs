using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation;



/// <summary>
/// A clickable placeholder rendered in the results pane. The underlying value is evaluated
/// only when the placeholder is clicked (client-server execution model). In other execution
/// models the value is evaluated eagerly.
/// </summary>
public sealed class OnDemandValue
{
    internal OnDemandValue(string? id, string title, Func<object?> factory)
    {
        Id = id;
        Title = title;
        Factory = factory;
    }

    /// <summary>
    /// The identifier used to correlate the placeholder with its value. <see langword="null"/>
    /// when the value was evaluated eagerly (no interactive results pane).
    /// </summary>
    public string? Id { get; }

    public string Title { get; }

    internal Func<object?> Factory { get; }
}

/// <summary>
/// Wraps a value with an inline CSS style applied to its rendered output.
/// Created by <c>Util.WithStyle</c>.
/// </summary>
public sealed class StyledValue(object? value, string style)
{
    public object? Value { get; } = value;

    public string Style { get; } = style;
}

/// <summary>
/// Text rendered with custom background and/or foreground colors.
/// Created by <c>Util.Highlight</c>.
/// </summary>
public sealed class HighlightedText(string text, string? backgroundColor = null, string? foregroundColor = null)
{
    public string Text { get; } = text;

    public string? BackgroundColor { get; } = backgroundColor;

    public string? ForegroundColor { get; } = foregroundColor;
}

/// <summary>
/// Lays out items side-by-side horizontally when dumped.
/// Created by <c>Util.HorizontalRun</c>.
/// </summary>
public sealed class HorizontalRun(bool wrapIfTooWide, params object?[] items)
{
    public bool WrapIfTooWide { get; } = wrapIfTooWide;

    public object?[] Items { get; } = items;
}

/// <summary>
/// A progress bar rendered in the results pane. In interactive (client-server) sessions it
/// renders an HTML progress bar that updates in-place. Otherwise updates are written to the
/// console as an ASCII progress bar.
/// </summary>
public sealed class ProgressBar
{
    private const int ConsoleBarWidth = 40;

    private readonly object _lock = new();
    private readonly string _outputId = "PB" + Guid.NewGuid().ToString("N");
    private readonly double _min;
    private readonly double _max;
    private readonly string? _title;
    private double _value;
    private bool _hasRenderedInitial;

    // Seam allowing tests to simulate an interactive session.
    internal static Func<bool> IsInteractiveSink { get; set; } =
        () => DumpExtension.Sink is ExecutionModel.ClientServer.ClientServerDumpSink;

    public ProgressBar(string? title = null)
        : this(0, 100, title)
    {
    }

    public ProgressBar(double min, double max, string? title = null)
    {
        if (max <= min)
        {
            throw new ArgumentException("Max must be greater than min.", nameof(max));
        }

        _min = min;
        _max = max;
        _title = title;
    }

    /// <summary>Gets the current progress value.</summary>
    public double Value
    {
        get { lock (_lock) return _value; }
    }

    /// <summary>
    /// Updates the progress bar to the specified value.
    /// </summary>
    /// <param name="value">The new progress value.</param>
    /// <param name="text">Optional text displayed alongside the progress bar.</param>
    public void Update(double value, string? text = null)
    {
        lock (_lock)
        {
            _value = Math.Clamp(value, _min, _max);
            Render(text);
        }
    }

    /// <summary>
    /// Marks the progress bar as complete (sets its value to max).
    /// </summary>
    /// <param name="text">Optional text displayed alongside the progress bar.</param>
    public void Complete(string? text = null)
    {
        Update(_max, text);
    }

    private void Render(string? text)
    {
        var percent = (_value - _min) / (_max - _min) * 100;

        if (IsInteractiveSink())
        {
            var options = new DumpOptions(Title: _title);
            DumpExtension.Sink.ResultWrite(BuildNode(percent), options, _outputId, _hasRenderedInitial);
            _hasRenderedInitial = true;
        }
        else
        {
            var filled = (int)Math.Round(percent / 100 * ConsoleBarWidth);
            var bar = new string('█', filled).PadRight(ConsoleBarWidth, '░');
            var suffix = text == null ? "" : $" {text}";
            System.Console.Out.WriteLine($"{bar} {percent:0}%{suffix}");
        }
    }

    private Element BuildNode(double percent)
    {
        var root = new Element("div").AddClass("progress");
        root.SetAttribute("role", "progressbar");
        root.GetOrAddAttribute("style")
            .Append("width:100%;height:14px;background:#e9ecef;border-radius:4px;overflow:hidden;");

        var bar = new Element("div").AddClass("progress-bar");
        bar.GetOrAddAttribute("style")
            .Append($"width:{Math.Clamp(percent, 0, 100):0.##}%;height:100%;background:#0d6efd;transition:width .2s;");
        root.AddChild(bar);

        return root;
    }
}
