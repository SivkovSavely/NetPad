using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.Presentation;
using O2Html;

namespace NetPad.Presentation.Controls;

/// <summary>A live result control rendered inside the script's results pane.</summary>
public abstract class Control : IDisposable
{
    private readonly object _updateLock = new();
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _styles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _attributesView;
    private readonly IReadOnlyDictionary<string, string> _stylesView;
    private string? _cssClasses;
    private bool _enabled = true;
    private bool _visible = true;
    private object? _content;
    private bool _suppressUpdates;
    private bool _disposed;
    private bool _registered;
    private bool _hasRendered;
    private Control? _root;
    private DumpOptions? _dumpOptions;
    private string? _panelName;

    protected Control()
    {
        ControlId = "CT" + Guid.NewGuid().ToString("N");
        _attributesView = new ReadOnlyDictionary<string, string>(_attributes);
        _stylesView = new ReadOnlyDictionary<string, string>(_styles);
        Children = new ObservableCollection<object?>();
        Children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>A stable identifier used to route browser events to this control.</summary>
    public string ControlId { get; }

    /// <summary>Css classes added to the control's root element.</summary>
    public string? CssClasses
    {
        get => _cssClasses;
        set
        {
            if (_cssClasses == value) return;
            _cssClasses = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the control accepts input.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the control is visible.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            NotifyChanged();
        }
    }

    /// <summary>Optional dumpable content rendered after the control's built-in elements.</summary>
    public object? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            _content = value;
            NotifyChanged();
        }
    }

    /// <summary>Arbitrary children rendered inside this control.</summary>
    public ObservableCollection<object?> Children { get; }

    /// <summary>Read-only view of safe HTML attributes applied to the control root.</summary>
    public IReadOnlyDictionary<string, string> Attributes => _attributesView;

    /// <summary>Read-only view of inline CSS properties applied to the control root.</summary>
    public IReadOnlyDictionary<string, string> Styles => _stylesView;

    /// <summary>Sets a non-event HTML attribute on the control root.</summary>
    public void SetAttribute(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Attribute name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(value);
        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("srcdoc", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("data-netpad-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Event, style, srcdoc, and reserved NetPad attributes are not allowed.", nameof(name));
        }

        _attributes[name] = value;
        NotifyChanged();
    }

    /// <summary>Removes an HTML attribute from the control root.</summary>
    public bool RemoveAttribute(string name)
    {
        var removed = _attributes.Remove(name);
        if (removed) NotifyChanged();
        return removed;
    }

    /// <summary>Sets an inline CSS property on the control root.</summary>
    public void SetStyle(string property, string value)
    {
        if (string.IsNullOrWhiteSpace(property)) throw new ArgumentException("Style property cannot be empty.", nameof(property));
        ArgumentNullException.ThrowIfNull(value);
        _styles[property] = value;
        NotifyChanged();
    }

    /// <summary>Removes an inline CSS property from the control root.</summary>
    public bool RemoveStyle(string property)
    {
        var removed = _styles.Remove(property);
        if (removed) NotifyChanged();
        return removed;
    }

    /// <summary>Adds one or more CSS classes to the control root.</summary>
    public void AddCssClass(string cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass)) throw new ArgumentException("CSS class cannot be empty.", nameof(cssClass));
        CssClasses = string.Join(' ', new[] { CssClasses, cssClass }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>Dumps this control into a stable mutable result slot.</summary>
    public Control Dump(DumpOptions? options = null)
    {
        if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        _dumpOptions = DumpOptions.Merge(options);
        var interactive = ProgressBar.IsInteractiveSink();

        if (interactive)
        {
            ControlRegistry.BeginRender(this);
            _root = this;
            _panelName = OutputRouting.PanelName;
            using var scope = ControlSerializationContext.Enter(this, _panelName, interactive: true);
            try
            {
                DumpExtension.Sink.ResultWrite(this, _dumpOptions, ControlId, _hasRendered);
                _hasRendered = true;
            }
            finally
            {
                ControlRegistry.EndRender(this);
            }
        }
        else
        {
            using var scope = ControlSerializationContext.Enter(this, OutputRouting.PanelName, interactive: false);
            DumpExtension.Sink.ResultWrite(this, _dumpOptions);
        }

        return this;
    }

    /// <summary>Disposes this control and prevents future updates or event handling.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        ControlRegistry.Remove(this);
        Invalidate();
        Children.CollectionChanged -= OnChildrenChanged;
        GC.SuppressFinalize(this);
    }

    internal string OutputId => ControlId;

    internal bool IsDisposed => _disposed;

    internal virtual string ControlType => GetType().Name;

    internal abstract O2Html.Dom.Element Render();

    internal virtual bool DispatchEvent(string eventType, JsonElement payload)
    {
        return false;
    }

    internal void RegisterUnder(Control root)
    {
        if (_disposed) return;
        _root = root;
        _registered = true;
    }

    internal void DetachFromRoot()
    {
        _root = null;
        _registered = false;
    }

    internal void Invalidate()
    {
        if (_disposed) return;
        _disposed = true;
        DetachFromRoot();
        Children.CollectionChanged -= OnChildrenChanged;
        OnDisposed();
    }

    protected virtual void OnDisposed() { }

    internal bool IsRegistered => _registered && !_disposed;

    internal void ApplyEventUpdate(Action update)
    {
        _suppressUpdates = true;
        try
        {
            update();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    protected void NotifyChanged()
    {
        if (_suppressUpdates || _disposed) return;
        var root = _root;
        if (root == null || !root._registered || !root._hasRendered) return;

        lock (root._updateLock)
        {
            if (root._disposed || !root._registered || !root._hasRendered) return;
            ControlRegistry.BeginRender(root);
            using var routingScope = OutputRouting.EnterPanel(root._panelName);
            using var scope = ControlSerializationContext.Enter(root, root._panelName, interactive: true);
            try
            {
                DumpExtension.Sink.ResultWrite(root, root._dumpOptions, root.OutputId, true);
            }
            finally
            {
                ControlRegistry.EndRender(root);
            }
        }
    }

    protected O2Html.Dom.Element CreateRoot(string tagName)
    {
        var root = new O2Html.Dom.Element(tagName).AddClass("netpad-control");
        if (!string.IsNullOrWhiteSpace(CssClasses)) root.AddClass(CssClasses);
        if (!Visible) root.GetOrAddAttribute("style").Append("display:none;");
        if (!Enabled) root.SetAttribute("aria-disabled", "true");

        foreach (var (name, value) in _attributes)
        {
            root.SetAttribute(name, value);
        }

        foreach (var (property, value) in _styles)
        {
            root.GetOrAddAttribute("style").Append($"{property}:{value};");
        }

        return root;
    }

    protected bool SupportsInteraction => ControlSerializationContext.IsInteractive;

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyChanged();
}

internal static class ControlSerializationContext
{
    private sealed record State(Control Root, string? PanelName, bool Interactive);
    private static readonly AsyncLocal<State?> Current = new();

    public static Control? Root => Current.Value?.Root;
    public static string? PanelName => Current.Value?.PanelName;
    public static bool IsInteractive => Current.Value?.Interactive == true;

    public static IDisposable Enter(Control root, string? panelName, bool interactive)
    {
        var previous = Current.Value;
        Current.Value = new State(root, panelName, interactive);
        return new Scope(previous);
    }

    private sealed class Scope(State? previous) : IDisposable
    {
        private State? _previous = previous;
        public void Dispose()
        {
            Current.Value = _previous;
            _previous = null;
        }
    }
}

internal static class ControlRegistry
{
    private sealed record Registration(Control Control, Control Root, string? PanelName);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Registration> Controls = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Control, HashSet<string>> PendingRoots = new();

    public static void BeginRender(Control root)
    {
        PendingRoots[root] = Controls
            .Where(pair => ReferenceEquals(pair.Value.Root, root))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static void EndRender(Control root)
    {
        if (!PendingRoots.TryRemove(root, out var staleIds)) return;
        foreach (var id in staleIds)
        {
            if (Controls.TryGetValue(id, out var existing) && ReferenceEquals(existing.Root, root) &&
                Controls.TryRemove(id, out var removed))
            {
                removed.Control.Invalidate();
            }
        }
    }

    public static void Register(Control control)
    {
        var root = ControlSerializationContext.Root;
        if (root == null || control.IsDisposed || !ControlSerializationContext.IsInteractive || !ProgressBar.IsInteractiveSink()) return;

        control.RegisterUnder(root);
        Controls[control.ControlId] = new Registration(control, root, ControlSerializationContext.PanelName);
        if (PendingRoots.TryGetValue(root, out var pending))
        {
            lock (pending) pending.Remove(control.ControlId);
        }
    }

    public static bool Dispatch(ControlEventMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ControlId) ||
            string.IsNullOrEmpty(message.EventType) ||
            string.IsNullOrEmpty(message.PayloadJson) ||
            message.PayloadJson.Length > 65_536 ||
            !Controls.TryGetValue(message.ControlId, out var registration)) return false;

        try
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            var payload = document.RootElement;
            if (payload.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined) return false;
            if (!registration.Control.IsRegistered || registration.Control.IsDisposed) return false;

            return registration.Control.DispatchEvent(message.EventType, payload);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void Clear(string? panelName = null, bool matchPanel = false)
    {
        PendingRoots.Clear();
        var roots = new HashSet<Control>();
        foreach (var pair in Controls)
        {
            var registration = pair.Value;
            if (matchPanel && !string.Equals(registration.PanelName, panelName, StringComparison.Ordinal)) continue;
            roots.Add(registration.Root);
            if (Controls.TryRemove(pair.Key, out var removed)) removed.Control.Invalidate();
        }

        foreach (var root in roots)
        {
            foreach (var pair in Controls)
            {
                if (ReferenceEquals(pair.Value.Root, root)) Controls.TryRemove(pair.Key, out _);
            }

            root.Invalidate();
        }
    }

    public static void Remove(Control control)
    {
        var root = ControlSerializationContext.Root ?? control;
        foreach (var pair in Controls)
        {
            if (ReferenceEquals(pair.Value.Control, control) ||
                (ReferenceEquals(control, root) && ReferenceEquals(pair.Value.Root, root)))
            {
                Controls.TryRemove(pair.Key, out var removed);
                removed?.Control.Invalidate();
            }
        }

        control.DetachFromRoot();
    }
}
