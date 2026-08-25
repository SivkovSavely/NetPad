using NetPad.Presentation;
using NetPad.Presentation.Diff;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    /// <summary>
    /// Script-global dump defaults applied to every dump unless overridden by explicit options.
    /// Alias for <see cref="DumpOptions.Default"/>. Assigning this never modifies persisted
    /// application settings.
    /// </summary>
    public static DumpOptions DumpDefaults
    {
        get => DumpOptions.Default;
        set => DumpOptions.Default = value ?? new DumpOptions();
    }

    /// <summary>
    /// Registers a script-wide dump transformer invoked for every dumped value before any
    /// per-instance <c>ToDump()</c> hook. Returns a disposable that unregisters the transformer.
    /// </summary>
    public static IDisposable RegisterToDumpTransformer(Func<object?, object?> transformer)
    {
        return DumpTransformer.Register(transformer);
    }

    /// <summary>
    /// Highlights a value's rendered output when the predicate is true at dump time.
    /// </summary>
    public static ConditionallyHighlighted HighlightIf(object? value, Func<object?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ConditionallyHighlighted(value, predicate);
    }

    /// <summary>
    /// Adds CSS class(es) to a value's rendered output.
    /// </summary>
    public static CssClassedValue WithCssClass(object? value, string cssClasses)
    {
        ArgumentNullException.ThrowIfNull(cssClasses);
        return new CssClassedValue(value, cssClasses);
    }

    /// <summary>
    /// Lays out items inline as spaced words when dumped.
    /// </summary>
    public static WordRun WordRun(params object?[] items)
    {
        return new WordRun(items);
    }

    /// <summary>
    /// Stacks items vertically when dumped.
    /// </summary>
    public static VerticalSequence VerticalRun(params object?[] items)
    {
        return new VerticalSequence(items);
    }

    /// <summary>
    /// Renders a heading above a value when dumped.
    /// </summary>
    public static TitledValue WithHeading(string title, object? value)
    {
        return new TitledValue(title, value);
    }

    /// <summary>
    /// Compares two values and dumps a human-readable, structured difference report.
    /// Supports meaningful text (line), sequence, dictionary, and object/member comparisons.
    /// </summary>
    /// <returns>The structured difference result that was dumped.</returns>
    public static DiffResult Dif(object? expected, object? actual)
    {
        var result = DiffEngine.Compare(expected, actual);
        DumpExtension.Dump(result, "Diff");
        return result;
    }
}
