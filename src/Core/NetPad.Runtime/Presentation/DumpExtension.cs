namespace NetPad.Presentation;

public static class DumpExtension
{
    public static IDumpSink Sink { get; private set; }

    static DumpExtension()
    {
        Sink = new NullDumpSink();
    }

    public static void UseSink(IDumpSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
    }

    /// <summary>
    /// Resolves effective options for a user-initiated dump. Precedence: explicit per-call
    /// options &gt; script-global defaults (<see cref="DumpOptions.Default"/>, also surfaced as
    /// <c>Util.DumpDefaults</c>) &gt; application results settings &gt; built-in defaults.
    /// </summary>
    private static DumpOptions Prepare(DumpOptions? explicitOptions) => DumpOptions.Merge(explicitOptions);

    /// <summary>
    /// Dumps an object, or value, to the results console.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("o")]
    public static T? Dump<T>(this T? o, string? title = null, string? css = null, string? code = null, int? clear = null)
    {
        Sink.ResultWrite(o, Prepare(new DumpOptions(
            Title: title,
            CssClasses: css,
            CodeType: code,
            DestructAfterMs: clear
        )));

        return o;
    }

    public static DumpContainer Dump(this DumpContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.Dump();
    }

    /// <summary>
    /// Dumps an object, or value, to the results console, awaiting the call first.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("o")]
    public static async Task<T?> Dump<T>(this Task<T?> o, string? title = null, string? css = null, string? code = null, int? clear = null)
    {
        var result = await o.ConfigureAwait(false);
        Sink.ResultWrite(result, Prepare(new DumpOptions(
            Title: title,
            CssClasses: css,
            CodeType: code,
            DestructAfterMs: clear
        )));

        return result;
    }

    /// <summary>
    /// Dumps this object to the results console.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("o")]
    public static T? Dump<T>(this T? o, DumpOptions options)
    {
        Sink.ResultWrite(o, Prepare(options));
        return o;
    }

    /// <summary>
    /// Dumps this object to the results console.
    /// </summary>
    public static async Task<T> Dump<T>(this Task<T> o, DumpOptions options)
    {
        var result = await o.ConfigureAwait(false);
        Sink.ResultWrite(result, Prepare(options));
        return result;
    }

    /// <summary>
    /// Dumps this <see cref="Span{T}"/> to the results view.
    /// </summary>
    public static Span<T> Dump<T>(this Span<T> span, string? title = null, string? cssClasses = null, int? clear = null)
    {
        Sink.ResultWrite(span.ToArray(), Prepare(new DumpOptions(
            Title: title,
            CssClasses: cssClasses,
            DestructAfterMs: clear
        )));
        return span;
    }

    /// <summary>
    /// Dumps this <see cref="ReadOnlySpan{T}"/> to the results view.
    /// </summary>
    public static ReadOnlySpan<T> Dump<T>(this ReadOnlySpan<T> span, string? title = null, string? cssClasses = null, int? clear = null)
    {
        Sink.ResultWrite(span.ToArray(), Prepare(new DumpOptions(
            Title: title,
            CssClasses: cssClasses,
            DestructAfterMs: clear
        )));
        return span;
    }

    /// <summary>
    /// Dumps this object to the results console, using the dumped source expression as the title
    /// when no explicit title is provided. Ordinary <see cref="Dump"/> behavior is unchanged.
    /// </summary>
    /// <param name="o">The object to dump.</param>
    /// <param name="title">Optional explicit title; overrides the source expression.</param>
    /// <param name="expression">Captured automatically; the caller's argument expression.</param>
    /// <example><code>
    /// someComplicatedExpression.DumpTell();
    /// </code></example>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("o")]
    public static T? DumpTell<T>(this T? o, string? title = null,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(o))] string? expression = null)
    {
        Sink.ResultWrite(o, Prepare(new DumpOptions(Title: title ?? CollapseExpression(expression))));
        return o;
    }

    /// <summary>
    /// Enumerates an async sequence and dumps its items into a single result block, updating the
    /// block in place while enumerating when the session is interactive. Honors the row limit of
    /// the resolved dump options (<see cref="DumpOptions.MaxRows"/> or the application results
    /// setting), stops consuming the source once the limit is reached, supports cancellation, and
    /// renders enumeration errors without discarding items produced before the error.
    /// </summary>
    /// <returns>The items that were enumerated and dumped.</returns>
    public static async Task<List<T>> DumpAsync<T>(
        this IAsyncEnumerable<T> source,
        string? title = null,
        string? css = null,
        DumpOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var baseOptions = Prepare(new DumpOptions(Title: title, CssClasses: css).MergeFrom(options));

        // Row cap precedence mirrors serializer limits: explicit option, then application setting.
        var cap = (int)(baseOptions.MaxRows ?? PresentationSettings.MaxCollectionLength);

        var interactive = ProgressBar.IsInteractiveSink();

        var outputId = interactive ? "AE" + Guid.NewGuid().ToString("N") : null;
        var items = new List<T>(Math.Min(cap, 1024));
        var endState = AsyncDumpEndState.Completed;

        var initialWritten = false;
        var dirtySinceLastWrite = false;

        void WriteCurrent()
        {
            Sink.ResultWrite(items.ToArray(), baseOptions, outputId, initialWritten);
            initialWritten = true;
            dirtySinceLastWrite = false;
        }

        if (interactive)
        {
            // Establish the result slot immediately so later updates replace it in place and
            // keep their position relative to other output.
            WriteCurrent();
        }

        try
        {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (items.Count >= cap)
                {
                    endState = AsyncDumpEndState.RowLimitReached;
                    break;
                }

                items.Add(item);
                dirtySinceLastWrite = true;

                if (interactive && items.Count % AsyncDumpUpdateBatchSize == 0)
                {
                    WriteCurrent();
                }
            }
        }
        catch (OperationCanceledException)
        {
            endState = AsyncDumpEndState.Cancelled;
        }
        catch (Exception ex)
        {
            endState = AsyncDumpEndState.Faulted;

            if (interactive)
            {
                if (dirtySinceLastWrite) WriteCurrent();
            }
            else
            {
                WriteCurrent();
            }

            Sink.ResultWrite(
                $"Error enumerating dumped sequence after {items.Count} item(s): {ex.Message}",
                Prepare(new DumpOptions(CssClasses: "error")));

            throw;
        }

        if (!interactive || dirtySinceLastWrite)
        {
            WriteCurrent();
        }

        switch (endState)
        {
            case AsyncDumpEndState.RowLimitReached:
                Sink.ResultWrite(
                    $"Row limit reached ({cap}); enumeration stopped.",
                    Prepare(new DumpOptions(CssClasses: "metatext")));
                break;

            case AsyncDumpEndState.Cancelled:
                Sink.ResultWrite(
                    $"Sequence dump cancelled after {items.Count} item(s).",
                    Prepare(new DumpOptions(CssClasses: "metatext")));
                break;
        }

        return items;
    }

    internal static string? CollapseExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var collapsed = System.Text.RegularExpressions.Regex.Replace(expression, @"\s+", " ").Trim();

        const int maxLength = 100;
        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..(maxLength - 1)] + "…";
        }

        return collapsed;
    }

    private const int AsyncDumpUpdateBatchSize = 50;

    private enum AsyncDumpEndState
    {
        Completed,
        RowLimitReached,
        Cancelled,
        Faulted,
    }
}
