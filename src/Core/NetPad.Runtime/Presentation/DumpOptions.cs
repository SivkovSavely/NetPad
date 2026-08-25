namespace NetPad.Presentation;

/// <summary>
/// Dump customization options.
/// </summary>
/// <param name="Title">
/// A heading displayed above the dumped output to help distinguish multiple dumps.
/// For example, <c>Dump(person, "Current User")</c> renders a “Current User” heading.
/// </param>
/// <param name="CssClasses">
/// One or more CSS class names to apply to the output container for styling the rendered dump.
/// You can use standard Bootstrap v5 class names (e.g., <c>"text-success"</c>, <c>"w-25"</c>), or specify custom classes
/// that you've defined under Settings &gt; Styles.
/// For example: <c>Dump(obj, css: "card text-bg-warning w-25")</c>
/// </param>
/// <param name="CodeType">
/// If you’re dumping a code snippet, specify its language (e.g. <c>"csharp"</c>, <c>"json"</c>, <c>"xml"</c>, etc.).
/// The output will be syntax-highlighted using <see href="https://github.com/highlightjs/highlight.js/blob/main/SUPPORTED_LANGUAGES.md">Highlight.js</see>.
/// </param>
/// <param name="AppendNewLineToAllTextOutput">If true and the output is all text (not a nested structure), an additional line will be appended to the result.</param>
/// <param name="DestructAfterMs">
/// If provided, the dump will automatically be removed from the console after the given time in milliseconds.
/// For example, <c>clear: 5000</c> makes it disappear after 5 seconds.
/// </param>
/// <param name="MaxRows">
/// The maximum number of items rendered for a collection in this dump. Overrides the global
/// results setting for this dump only. Applies to collections; other values are unaffected.
/// </param>
/// <param name="MaxDepth">
/// The maximum object-graph depth rendered for this dump. Overrides the global results setting
/// for this dump only.
/// </param>
/// <param name="Expanded">
/// Force expansion state metadata for the rendered output. Rendered as a <c>data-expanded</c>
/// attribute on the output group so hosts can honor it where expansion applies.
/// </param>
/// <param name="IncludeMembers">
/// When set, only members whose names appear in this list are rendered for objects in this dump.
/// </param>
/// <param name="ExcludeMembers">
/// Members whose names appear in this list are not rendered for objects in this dump.
/// Ignored when <see cref="IncludeMembers"/> is set.
/// </param>
/// <param name="FormatStrings">
/// Per-type format strings applied when rendering values whose declared type matches a key.
/// Keys are type full names (e.g. <c>"System.DateTime"</c>) or simple names (e.g. <c>"DateTime"</c>);
/// values are standard .NET format strings (e.g. <c>"yyyy-MM-dd"</c>).
/// </param>
public record DumpOptions(
    string? Title = null,
    string? CssClasses = null,
    string? CodeType = null,
    bool? AppendNewLineToAllTextOutput = null,
    int? DestructAfterMs = null,
    uint? MaxRows = null,
    uint? MaxDepth = null,
    bool? Expanded = null,
    IReadOnlyList<string>? IncludeMembers = null,
    IReadOnlyList<string>? ExcludeMembers = null,
    IReadOnlyDictionary<string, string>? FormatStrings = null)
{
    /// <summary>
    /// Script-global dump defaults. Members set here apply to every dump unless overridden by
    /// explicit per-call/per-container options. Precedence: explicit options &gt;
    /// <see cref="Default"/> &gt; application Results settings &gt; built-in defaults.
    /// Assigning this property does not modify persisted application settings.
    /// </summary>
    public static DumpOptions Default { get; set; } = new();

    /// <summary>
    /// Gets or sets the order of the dumped output. This is used to override the normal
    /// behaviour of the order being assigned automatically.
    /// </summary>
    internal uint? Order { get; init; }

    /// <summary>
    /// Returns options where each unset (<see langword="null"/>) member of this instance is
    /// filled from <paramref name="fallback"/>. Explicit members always win.
    /// </summary>
    public DumpOptions MergeFrom(DumpOptions? fallback)
    {
        if (fallback == null) return this;

        return new DumpOptions(
            Title ?? fallback.Title,
            CssClasses ?? fallback.CssClasses,
            CodeType ?? fallback.CodeType,
            AppendNewLineToAllTextOutput ?? fallback.AppendNewLineToAllTextOutput,
            DestructAfterMs ?? fallback.DestructAfterMs,
            MaxRows ?? fallback.MaxRows,
            MaxDepth ?? fallback.MaxDepth,
            Expanded ?? fallback.Expanded,
            IncludeMembers ?? fallback.IncludeMembers,
            ExcludeMembers ?? fallback.ExcludeMembers,
            FormatStrings ?? fallback.FormatStrings
        )
        {
            Order = Order ?? fallback.Order
        };
    }

    /// <summary>
    /// Resolves the effective options for a dump by merging, in precedence order:
    /// explicit options, then <paramref name="containerOptions"/>, then script-global defaults.
    /// </summary>
    public static DumpOptions Merge(DumpOptions? explicitOptions, DumpOptions? containerOptions = null)
    {
        var defaults = Default;

        var merged = (explicitOptions ?? new DumpOptions()).MergeFrom(containerOptions);
        return merged.MergeFrom(defaults);
    }
}
