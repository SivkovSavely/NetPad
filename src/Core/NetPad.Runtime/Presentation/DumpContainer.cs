namespace NetPad.Presentation;

/// <summary>
/// Represents a single result slot whose content can be replaced while a script runs.
/// </summary>
public sealed class DumpContainer
{
    private readonly string _outputId = Guid.NewGuid().ToString("N");
    private readonly object _sync = new();
    private bool _isDumped;
    private object? _content;

    public DumpContainer()
    {
    }

    public DumpContainer(object? content)
    {
        _content = content;
    }

    public object? Content
    {
        get => _content;
        set
        {
            lock (_sync)
            {
                _content = value;
                if (_isDumped) Write(true);
            }
        }
    }

    public void UpdateContent(object? content)
    {
        Content = content;
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
        DumpExtension.Sink.ResultWrite(_content, null, _outputId, isUpdate);
    }
}
