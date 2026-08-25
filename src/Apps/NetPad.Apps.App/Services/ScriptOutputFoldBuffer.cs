using System.Collections.Generic;
using NetPad.IO;
using NetPad.Presentation;
using NetPad.Scripts;

namespace NetPad.Services;

/// <summary>
/// An <see cref="IOutputWriter{TOutput}"/> that buffers script output while folding mutable output
/// updates (<see cref="DumpContainer"/>) into their initial outputs, enforcing a max output size.
/// Shared by headless runs and GUI-run capture so both paths return the same final output shape:
/// one entry per output slot with updates already applied, orphan updates dropped.
///
/// Named result panels (<see cref="ScriptOutput.PanelName"/>) are preserved on buffered outputs;
/// <see cref="Output"/> flattens them sequentially after the main results, each panel preceded
/// by a heading, so non-UI consumers still receive all content in a deterministic order.
/// </summary>
public sealed class ScriptOutputFoldBuffer : IOutputWriter<object>
{
    private const int MaxOutputSize = 100 * 1024; // ~100KB

    private readonly object _sync = new();
    private readonly List<ScriptOutput> _outputs = [];
    private readonly List<string> _panelOrder = [];
    private int _totalOutputSize;
    private bool _outputTruncated;

    public List<string> Errors { get; } = [];

    /// <summary>
    /// The final output: main results followed by each named panel's content sequentially,
    /// with a heading text output before each panel's content.
    /// </summary>
    public List<ScriptOutput> Output
    {
        get
        {
            lock (_sync)
            {
                if (_panelOrder.Count == 0)
                {
                    return [.. _outputs];
                }

                var flattened = new List<ScriptOutput>(_outputs.Count);

                foreach (var output in _outputs)
                {
                    if (output.PanelName == null)
                    {
                        flattened.Add(output);
                    }
                }

                foreach (var panel in _panelOrder)
                {
                    flattened.Add(new ScriptOutput(
                        ScriptOutputKind.Result,
                        $"[{panel}]",
                        ScriptOutputFormat.Text));

                    foreach (var output in _outputs)
                    {
                        if (output.PanelName == panel)
                        {
                            flattened.Add(output);
                        }
                    }
                }

                return flattened;
            }
        }
    }

    public Task WriteAsync(object? output, string? title = null, CancellationToken cancellationToken = default)
    {
        if (_outputTruncated || output is not ScriptOutput so) return Task.CompletedTask;

        lock (_sync)
        {
            if (so.Kind == ScriptOutputKind.Error)
            {
                Errors.Add(so.Body ?? string.Empty);
                return Task.CompletedTask;
            }

            if (so.IsUpdate && so.OutputId is not null)
            {
                var ix = _outputs.FindIndex(o => o.OutputId == so.OutputId);
                if (ix >= 0)
                {
                    // Replace the slot's content in place, charging only the size difference against
                    // the limit so repeated small updates do not spuriously truncate the output.
                    var oldOutput = _outputs[ix];
                    var proposedTotal = _totalOutputSize
                        - (oldOutput.Body?.Length ?? 0)
                        + (so.Body?.Length ?? 0);
                    if (proposedTotal > MaxOutputSize)
                    {
                        _outputTruncated = true;
                        _outputs.Add(new ScriptOutput(ScriptOutputKind.Result, "[Output truncated: exceeded 100KB limit]"));
                        return Task.CompletedTask;
                    }

                    _totalOutputSize = proposedTotal;
                    _outputs[ix] = so with { Order = oldOutput.Order, IsUpdate = false };
                    return Task.CompletedTask;
                }

                // An update whose initial output was never captured (ex. a DumpContainer kept
                // alive across runs) has nothing to fold into and is dropped.
                return Task.CompletedTask;
            }

            _totalOutputSize += so.Body?.Length ?? 0;
            if (_totalOutputSize > MaxOutputSize)
            {
                _outputTruncated = true;
                _outputs.Add(new ScriptOutput(ScriptOutputKind.Result, "[Output truncated: exceeded 100KB limit]"));
                return Task.CompletedTask;
            }

            if (so.PanelName != null && !_panelOrder.Contains(so.PanelName))
            {
                _panelOrder.Add(so.PanelName);
            }

            _outputs.Add(so);
        }

        return Task.CompletedTask;
    }
}
