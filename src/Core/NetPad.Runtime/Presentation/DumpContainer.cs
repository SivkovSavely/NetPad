namespace NetPad.Presentation;

/// <summary>
/// Represents a single result slot whose content can be replaced while a script runs.
/// </summary>
public sealed class DumpContainer
{
    private readonly string _outputId = Guid.NewGuid().ToString("N");
    private readonly object _sync = new();
    private readonly List<object?> _contents = [];
    private bool _isDumped;

    public DumpContainer()
    {
    }

    public DumpContainer(object? content)
    {
        _contents.Add(content);
    }

    /// <summary>
    /// A heading displayed above this container's rendered output.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// One or more CSS class names applied to this container's rendered output.
    /// </summary>
    public string? CssClasses { get; set; }

    /// <summary>
    /// Options applied to every write from this container. Members left unset fall back to
    /// script-global dump defaults (<see cref="DumpOptions.Default"/>).
    /// </summary>
    public DumpOptions? Options { get; set; }

    /// <summary>
    /// Gets the current content. When <see cref="AppendContent"/> has been used, a sequence of
    /// all appended items is returned.
    /// </summary>
    public object? Content
    {
        get
        {
            lock (_sync)
            {
                return _contents.Count switch
                {
                    0 => null,
                    1 => _contents[0],
                    _ => _contents.ToArray(),
                };
            }
        }
        set
        {
            lock (_sync)
            {
                _contents.Clear();
                _contents.Add(value);
                if (_isDumped) Write(true);
            }
        }
    }

    public void UpdateContent(object? content)
    {
        Content = content;
    }

    /// <summary>
    /// Appends content inside this container without replacing existing content.
    /// Does nothing visible until the container has been dumped.
    /// </summary>
    public void AppendContent(object? content)
    {
        lock (_sync)
        {
            _contents.Add(content);
            if (_isDumped) Write(true);
        }
    }

    /// <summary>
    /// Clears this container's content. The container's slot remains in place so subsequent
    /// output ordering is unaffected.
    /// </summary>
    public void ClearContent()
    {
        lock (_sync)
        {
            _contents.Clear();
            if (_isDumped) Write(true);
        }
    }

    public void Refresh()
    {
        lock (_sync)
        {
            if (_isDumped) Write(true);
        }
    }

    internal DumpContainer Dump()
    {
        // The guard and initial write are atomic so concurrent Dump() calls emit exactly one
        // initial output for this slot, and no update is written before it.
        lock (_sync)
        {
            if (!_isDumped)
            {
                _isDumped = true;
                Write(false);
            }
        }

        return this;
    }

    private void Write(bool isUpdate)
    {
        var body = _contents.Count switch
        {
            0 => null,
            1 => _contents[0],
            _ => new VerticalSequence(_contents),
        };

        // Precedence: container properties > container Options > script-global defaults.
        var options = DumpOptions.Merge(
            new DumpOptions(Title: Title, CssClasses: CssClasses).MergeFrom(Options));

        DumpExtension.Sink.ResultWrite(body, options, _outputId, isUpdate);
    }
}
