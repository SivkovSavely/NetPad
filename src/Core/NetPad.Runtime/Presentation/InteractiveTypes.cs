using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.ExecutionModel.ScriptServices;
using O2Html;
using O2Html.Dom;

namespace NetPad.Presentation;

/// <summary>
/// Routes output written while a named result panel is active to that panel.
/// The ambient value is consumed when <see cref="ScriptOutput"/> records are created.
/// </summary>
internal static class OutputRouting
{
    private static readonly AsyncLocal<string?> CurrentPanel = new();

    public static string? PanelName => CurrentPanel.Value;

    public static IDisposable EnterPanel(string name)
    {
        var previous = CurrentPanel.Value;
        CurrentPanel.Value = name;
        return new PanelScope(previous);
    }

    public static void ExitPanel(string? restoreTo) => CurrentPanel.Value = restoreTo;

    public static void Reset() => CurrentPanel.Value = null;

    private sealed class PanelScope(string? previous) : IDisposable
    {
        private string? _previous = previous;

        public void Dispose()
        {
            ExitPanel(_previous);
            _previous = null;
        }
    }
}

public enum HyperlinqKind
{
    Url,
    File,
    Action
}

/// <summary>
/// A clickable link rendered in the results pane. Links can target a URI, a local file/script
/// (optionally with line/column), or an action executed by the script host when clicked.
/// </summary>
public sealed class Hyperlinq
{
    internal Hyperlinq(HyperlinqKind kind, string? url, string? filePath, int? lineNumber, int? columnNumber, string? displayText, Action? clickAction, string? actionId)
    {
        Kind = kind;
        Url = url;
        FilePath = filePath;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        DisplayText = displayText;
        ClickAction = clickAction;
        ActionId = actionId;
    }

    /// <summary>Creates a link to a URI.</summary>
    public Hyperlinq(Uri uri) : this(HyperlinqKind.Url, uri.ToString(), null, null, null, null, null, null)
    {
    }

    /// <summary>Creates a link to a URI with display text.</summary>
    public Hyperlinq(Uri uri, string displayText) : this(HyperlinqKind.Url, uri.ToString(), null, null, null, displayText, null, null)
    {
    }

    /// <summary>Creates a link that executes <paramref name="clickAction"/> in the script host when clicked.</summary>
    public Hyperlinq(Action clickAction, string displayText)
        : this(HyperlinqKind.Action, null, null, null, null, displayText, clickAction, "HL" + Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>
    /// Creates a link that opens a local file or script in the app when clicked,
    /// optionally navigating to a line/column.
    /// </summary>
    public static Hyperlinq File(string filePath, int? lineNumber = null, int? columnNumber = null, string? displayText = null)
        => new(HyperlinqKind.File, null, filePath, lineNumber, columnNumber, displayText, null, null);

    /// <summary>
    /// Creates a link that opens a NetPad script in the app when clicked.
    /// </summary>
    public static Hyperlinq Script(string scriptPath, string? displayText = null)
        => new(HyperlinqKind.File, null, scriptPath, null, null, displayText, null, null);

    public HyperlinqKind Kind { get; }

    public string? Url { get; }

    public string? FilePath { get; }

    public int? LineNumber { get; }

    public int? ColumnNumber { get; }

    public string? DisplayText { get; }

    internal Action? ClickAction { get; }

    internal string? ActionId { get; }

    internal string GetDisplayText()
    {
        if (!string.IsNullOrWhiteSpace(DisplayText)) return DisplayText;

        return Kind switch
        {
            HyperlinqKind.Url => Url ?? string.Empty,
            HyperlinqKind.File => FilePath ?? string.Empty,
            _ => string.Empty
        };
    }
}

/// <summary>
/// Markdown text rendered as HTML in the results pane. Raw HTML embedded in the markdown is
/// escaped by default; pass <paramref name="allowRawHtml"/> to allow it through unescaped.
/// </summary>
public sealed class MarkdownContent(string markdown, bool allowRawHtml = false)
{
    public string Markdown { get; } = markdown;

    public bool AllowRawHtml { get; } = allowRawHtml;
}

/// <summary>
/// LaTeX math rendered in the results pane using KaTeX.
/// </summary>
public sealed class LatexContent(string latex, bool displayMode = true)
{
    public string Latex { get; } = latex;

    public bool DisplayMode { get; } = displayMode;
}

/// <summary>
/// A handle to a named result panel created via <c>Util.OpenPanel</c>. Dumps and writes issued
/// through the handle are rendered in a dedicated tab beside the main results view.
/// </summary>
public sealed class ResultPanel
{
    internal ResultPanel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>Dumps a value into this panel.</summary>
    public void Dump(object? value, string? title = null)
    {
        using var _ = OutputRouting.EnterPanel(Name);
        value.Dump(title);
    }

    /// <summary>Writes plain text into this panel.</summary>
    public void Write(string text)
    {
        using var _ = OutputRouting.EnterPanel(Name);
        text.Dump();
    }

    /// <summary>Writes raw HTML into this panel.</summary>
    public void WriteHtml(string html)
    {
        using var _ = OutputRouting.EnterPanel(Name);
        TextNode.RawText(html).Dump();
    }

    /// <summary>Clears this panel's content.</summary>
    public void Clear() => InteractiveResultHost.SendCommand(ResultHostCommand.ClearResults, Name);

    /// <summary>Closes this panel.</summary>
    public void Close() => InteractiveResultHost.SendCommand(ResultHostCommand.RemovePanel, Name);
}
