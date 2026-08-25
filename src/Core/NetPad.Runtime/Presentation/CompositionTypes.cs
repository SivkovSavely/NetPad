using System.Linq;

namespace NetPad.Presentation;

/// <summary>
/// Stacks items vertically when dumped. Created by <see cref="DumpContainer"/> when it holds
/// multiple appended items, or by <c>Util.VerticalRun</c>.
/// </summary>
public sealed class VerticalSequence(params IEnumerable<object?> items)
{
    public IReadOnlyList<object?> Items { get; } = items.ToList();
}

/// <summary>
/// Lays out items inline as words (small gaps, wrapping) when dumped.
/// Created by <c>Util.WordRun</c>.
/// </summary>
public sealed class WordRun(params object?[] items)
{
    public object?[] Items { get; } = items;
}

/// <summary>
/// Renders a heading above a value when dumped. Created by <c>Util.WithHeading</c>.
/// </summary>
public sealed class TitledValue(string title, object? value)
{
    public string Title { get; } = title;

    public object? Value { get; } = value;
}

/// <summary>
/// Conditionally highlights a value's rendered output based on a predicate evaluated at dump time.
/// Created by <c>Util.HighlightIf</c>.
/// </summary>
public sealed class ConditionallyHighlighted(object? value, Func<object?, bool> predicate)
{
    public object? Value { get; } = value;

    public bool ShouldHighlight => predicate(Value);
}

/// <summary>
/// Adds CSS class(es) to a value's rendered output. Created by <c>Util.WithCssClass</c>.
/// </summary>
public sealed class CssClassedValue(object? value, string cssClasses)
{
    public object? Value { get; } = value;

    public string CssClasses { get; } = cssClasses;
}
