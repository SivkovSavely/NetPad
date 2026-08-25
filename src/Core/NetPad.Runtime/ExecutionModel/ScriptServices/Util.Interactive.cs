using System.Collections.Concurrent;
using NetPad.Configuration;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.Presentation;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    #region Run scope (cancellation, registries)

    private static CancellationTokenSource? _queryCancelCts;

    /// <summary>
    /// The cancellation token for the current run. Cancelled when the user requests cancellation,
    /// giving the script a chance to stop cooperatively before a hard stop is enforced.
    /// </summary>
    public static CancellationToken QueryCancelToken => _queryCancelCts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Starts a fresh interactive run scope: fresh <see cref="QueryCancelToken"/>, and cleared
    /// action/on-demand/panel/html-head registrations from previous runs.
    /// </summary>
    internal static void BeginInteractiveRunScope()
    {
        _queryCancelCts?.Cancel();
        _queryCancelCts?.Dispose();
        _queryCancelCts = new CancellationTokenSource();

        _softCancellationRequested = false;
        _nextUserInputMasked = false;

        ClearOnDemandRegistry();
        ScriptActionRegistry.Clear();
        HtmlHeadState.Reset();
        OutputRouting.Reset();
        KeepRunningManager.Reset();
    }

    /// <summary>
    /// Ends the current run scope, cancelling the run token so any waiters
    /// (ex. <c>KeepRunning</c>) are released.
    /// </summary>
    internal static void EndInteractiveRunScope()
    {
        try
        {
            _queryCancelCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static volatile bool _softCancellationRequested;

    /// <summary>True when cooperative cancellation has been requested for the current run.</summary>
    public static bool SoftCancellationRequested => _softCancellationRequested;

    /// <summary>
    /// Requests cooperative cancellation of the running script via its
    /// <see cref="QueryCancelToken"/>. The host falls back to a hard stop if the script does
    /// not end within a grace period.
    /// </summary>
    internal static void RequestCooperativeCancellation()
    {
        _softCancellationRequested = true;
        try
        {
            _queryCancelCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static volatile bool _nextUserInputMasked;

    internal static bool TakeNextUserInputMasked()
    {
        var masked = _nextUserInputMasked;
        _nextUserInputMasked = false;
        return masked;
    }

    #endregion

    #region Hyperlinq

    /// <summary>
    /// Invokes a previously registered Hyperlinq click handler or URL link. Unknown/stale ids
    /// (from a previous run or cleared results) are ignored.
    /// </summary>
    internal static void InvokeScriptAction(string actionId)
    {
        if (!ScriptActionRegistry.TryInvoke(actionId, out var error))
        {
            if (error != null)
            {
                SinkError(error);
            }
        }
    }

    private static void SinkError(string message)
    {
        DumpExtension.Sink.ResultWrite(
            new Exception(message),
            new DumpOptions(Title: "Link action failed"));
    }

    #endregion

    #region Markdown / LaTeX

    /// <summary>
    /// Renders markdown in the results pane. Raw HTML inside the markdown is escaped unless
    /// <paramref name="allowRawHtml"/> is true.
    /// </summary>
    public static MarkdownContent Markdown(string markdown, bool allowRawHtml = false)
        => new(markdown, allowRawHtml);

    /// <summary>
    /// Renders LaTeX math in the results pane (KaTeX). Set <paramref name="displayMode"/> to
    /// false for inline rendering.
    /// </summary>
    public static LatexContent Latex(string latex, bool displayMode = true)
        => new(latex, displayMode);

    #endregion

    #region Util.JS

    /// <summary>
    /// Executes JavaScript in the results view of the running script.
    /// Only supported when the script runs inside the NetPad app.
    /// </summary>
    public static class JS
    {
        /// <summary>Runs JavaScript statements in the script's results view.</summary>
        public static void Run(string script, int timeoutMs = 30_000)
            => Eval<object?>(script, timeoutMs);

        /// <summary>
        /// Evaluates a JavaScript expression in the script's results view and returns its
        /// JSON-serialized value deserialized to <typeparamref name="T"/>.
        /// </summary>
        public static T? Eval<T>(string expression, int timeoutMs = 30_000)
        {
            var onRequestJsEval = OnRequestJsEval;
            if (onRequestJsEval == null)
            {
                throw new NotSupportedException(
                    "Util.JS is only supported when the script runs inside the NetPad app.");
            }

            var resultJson = onRequestJsEval(expression, timeoutMs).GetAwaiter().GetResult();

            return resultJson == null ? default : System.Text.Json.JsonSerializer.Deserialize<T>(resultJson);
        }
    }

    internal static Func<string, int, Task<string?>>? OnRequestJsEval { get; set; }

    #endregion

    #region Util.HtmlHead

    /// <summary>
    /// Adds CSS styles, script references and raw HTML to the current script's results document.
    /// Entries are scoped to the current run and cleared when results are cleared or rerun.
    /// </summary>
    public static class HtmlHead
    {
        /// <summary>Adds a CSS style block.</summary>
        public static void AddCss(string css) => HtmlHeadState.Add(HtmlHeadEntryType.Css, css);

        /// <summary>Adds a link to an external stylesheet.</summary>
        public static void AddCssLink(string uri) => HtmlHeadState.Add(HtmlHeadEntryType.CssLink, uri);

        /// <summary>Adds a link to an external JavaScript file.</summary>
        public static void AddScriptLink(string uri) => HtmlHeadState.Add(HtmlHeadEntryType.ScriptLink, uri);

        /// <summary>Adds an inline JavaScript block.</summary>
        public static void AddScript(string js) => HtmlHeadState.Add(HtmlHeadEntryType.Script, js);

        /// <summary>Adds raw HTML to the document head.</summary>
        public static void AddRaw(string html) => HtmlHeadState.Add(HtmlHeadEntryType.Raw, html);
    }

    #endregion

    #region Result host commands

    /// <summary>Clears the current script's results without terminating the run.</summary>
    public static void ClearResults() => InteractiveResultHost.SendCommand(ResultHostCommand.ClearResults);

    /// <summary>Hides the editor pane.</summary>
    public static void HideEditor() => InteractiveResultHost.SendCommand(ResultHostCommand.HideEditor);

    /// <summary>Shows the editor pane.</summary>
    public static void ShowEditor() => InteractiveResultHost.SendCommand(ResultHostCommand.ShowEditor);

    /// <summary>Hides the results pane.</summary>
    public static void HideResults() => InteractiveResultHost.SendCommand(ResultHostCommand.HideResults);

    /// <summary>Shows the results pane.</summary>
    public static void ShowResults() => InteractiveResultHost.SendCommand(ResultHostCommand.ShowResults);

    /// <summary>
    /// Enables/disables auto-scroll of the results view while the script produces output.
    /// While enabled, scrolling away from the bottom pauses following until scrolled back.
    /// </summary>
    public static void AutoScrollResults(bool enabled)
        => InteractiveResultHost.SendCommand(ResultHostCommand.AutoScrollResults, enabled ? "true" : "false");

    internal static Action<ResultHostCommand, string?>? OnResultHostCommand { get; set; }

    internal static Action<ScriptHtmlHeadEntry[]>? OnHtmlHeadChanged { get; set; }

    #endregion

    #region Named result panels

    /// <summary>
    /// Opens (or returns) a named result panel rendered as a tab beside the main results view.
    /// </summary>
    public static ResultPanel OpenPanel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Panel name cannot be null or empty.", nameof(name));
        }

        InteractiveResultHost.SendCommand(ResultHostCommand.OpenPanel, name);
        return new ResultPanel(name);
    }

    /// <summary>Dumps a value to a new named result panel.</summary>
    public static void DumpToNewPanel(string name, object? value, string? title = null)
        => OpenPanel(name).Dump(value, title);

    #endregion

    #region KeepRunning

    /// <summary>
    /// Keeps the script host alive after the top-level code completes, allowing callbacks
    /// (ex. Hyperlinq actions, live controls) to remain usable. Dispose the returned lease
    /// to allow the host to exit; all leases must be released, or cancellation requested.
    /// </summary>
    public static IDisposable KeepRunning() => KeepRunningManager.Acquire();

    #endregion

    #region Passwords

    /// <summary>
    /// Gets a named secret/password previously saved via the app's user secrets store
    /// (<c>Util.Secrets</c>). When the key has no stored value, prompts the user interactively
    /// with a masked input. The returned value is never logged.
    /// </summary>
    public static string GetPassword(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Password name cannot be null or empty.", nameof(name));
        }

        var secret = Secrets.GetSecret(name);
        if (secret != null)
        {
            return secret.Value ?? string.Empty;
        }

        Console.Out.Write($"Password for '{name}': ");
        _nextUserInputMasked = true;
        return Console.ReadLine() ?? string.Empty;
    }

    #endregion
}

/// <summary>
/// Sends result-host commands to the parent app when running interactively; degrades gracefully
/// with a metatext notice otherwise.
/// </summary>
internal static class InteractiveResultHost
{
    public static void SendCommand(ResultHostCommand command, string? payload = null)
    {
        var handler = Util.OnResultHostCommand;
        if (handler != null)
        {
            handler(command, payload);
            return;
        }

        ("Result host commands are only supported when the script runs inside the NetPad app.")
            .Dump(css: "metatext");
    }
}

/// <summary>
/// Registry of Hyperlinq click handlers for the current run. Cleared per run alongside other
/// interactive registrations; stale ids fail silently.
/// </summary>
internal static class ScriptActionRegistry
{
    private static readonly ConcurrentDictionary<string, Action> Actions = new();

    public static string Register(Action action)
    {
        var id = "HL" + Guid.NewGuid().ToString("N");
        Actions[id] = action;
        return id;
    }

    public static bool TryInvoke(string actionId, out string? error)
    {
        error = null;

        if (!Actions.TryGetValue(actionId, out var action))
        {
            // Stale id (previous run or cleared output) — fail silently.
            return false;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return false;
        }

        return true;
    }

    public static void Clear() => Actions.Clear();
}

/// <summary>
/// Registers Hyperlinq URL links for host-side dispatch, deduplicating identical URLs.
/// </summary>
internal static class UtilHyperlinqRegistry
{
    private static readonly ConcurrentDictionary<string, string> UrlToActionId = new();

    public static string RegisterUrl(string url)
        => UrlToActionId.GetOrAdd(url, u => ScriptActionRegistry.Register(() =>
            ExecutionModel.ScriptServices.Util.OpenUrl(u)));
}

/// <summary>
/// Holds the current run's Util.HtmlHead entries, deduplicating identical resources and
/// notifying the host whenever entries change.
/// </summary>
internal static class HtmlHeadState
{
    private readonly record struct EntryKey(int Type, string Content);

    private static readonly object Lock = new();
    private static List<ScriptHtmlHeadEntry>? _entries;
    private static HashSet<EntryKey>? _keys;

    public static void Add(HtmlHeadEntryType type, string content)
    {
        lock (Lock)
        {
            var key = new EntryKey((int)type, content);
            _keys ??= new HashSet<EntryKey>();
            _entries ??= new List<ScriptHtmlHeadEntry>();

            if (!_keys.Add(key))
            {
                return; // Dedupe identical resources.
            }

            _entries.Add(new ScriptHtmlHeadEntry((int)type, content));
        }

        Util.OnHtmlHeadChanged?.Invoke(Snapshot());
    }

    public static ScriptHtmlHeadEntry[] Snapshot()
    {
        lock (Lock)
        {
            return _entries?.ToArray() ?? [];
        }
    }

    public static void Reset()
    {
        List<ScriptHtmlHeadEntry>? removed = null;

        lock (Lock)
        {
            if (_entries is { Count: > 0 })
            {
                removed = _entries;
            }

            _entries = null;
            _keys = null;
        }

        if (removed != null)
        {
            Util.OnHtmlHeadChanged?.Invoke([]);
        }
    }
}

/// <summary>
/// Lease-based lifetime retention allowing a run to outlive its top-level code while
/// interactive callbacks stay usable.
/// </summary>
internal static class KeepRunningManager
{
    private static readonly object Lock = new();
    private static State _state = new();

    private sealed class State
    {
        public HashSet<Lease> Leases { get; } = new();
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Action> ResetCallbacks { get; } = new();
        public bool Closed;
    }

    public static IDisposable Acquire()
    {
        State state;

        lock (Lock)
        {
            state = _state;
        }

        var lease = new Lease(state);

        lock (Lock)
        {
            if (!state.Closed)
            {
                state.Leases.Add(lease);
            }
        }

        return lease;
    }

    public static bool HasActiveLeases
    {
        get
        {
            lock (Lock)
            {
                return !_state.Closed && _state.Leases.Count > 0;
            }
        }
    }

    /// <summary>
    /// Completes when all leases have been released, the provided token is cancelled, or the
    /// manager is reset (new run starting).
    /// </summary>
    public static async Task WaitUntilReleasedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource released;
        bool hasLeases;
        State observed;

        lock (Lock)
        {
            observed = _state;
            hasLeases = !observed.Closed && observed.Leases.Count > 0;
            released = observed.Released;
        }

        if (!hasLeases)
        {
            return;
        }

        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelTcs.TrySetResult());

        var resetTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Lock)
        {
            if (_state != observed)
            {
                resetTcs.TrySetResult();
            }
            else
            {
                observed.ResetCallbacks.Add(() => resetTcs.TrySetResult());
            }
        }

        var completed = await Task.WhenAny(released.Task, cancelTcs.Task, resetTcs.Task).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
    }

    public static void Reset()
    {
        Action[] callbacks;

        lock (Lock)
        {
            var old = _state;
            old.Closed = true;
            _state = new State();
            callbacks = [.. old.ResetCallbacks];
        }

        foreach (var callback in callbacks)
        {
            callback();
        }
    }

    private sealed class Lease(State state) : IDisposable
    {
        private State? _state = state;

        public void Dispose()
        {
            var state = _state;
            if (state == null) return;
            _state = null;

            lock (state)
            {
                state.Leases.Remove(this);
                if (state.Leases.Count == 0 && !state.Closed)
                {
                    state.Released.TrySetResult();
                }
            }
        }
    }
}
