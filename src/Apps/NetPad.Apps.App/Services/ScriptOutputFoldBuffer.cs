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
/// </summary>
public sealed class ScriptOutputFoldBuffer : IOutputWriter<object>
{
    private const int MaxOutputSize = 100 * 1024; // ~100KB

    private readonly object _sync = new();
    private int _totalOutputSize;
    private bool _outputTruncated;

    public List<ScriptOutput> Output { get; } = [];
    public List<string> Errors { get; } = [];

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
                var ix = Output.FindIndex(o => o.OutputId == so.OutputId);
                if (ix >= 0)
                {
                    // Replace the slot's content in place, charging only the size difference against
                    // the limit so repeated small updates do not spuriously truncate the output.
                    var oldOutput = Output[ix];
                    var proposedTotal = _totalOutputSize
                        - (oldOutput.Body?.Length ?? 0)
                        + (so.Body?.Length ?? 0);
                    if (proposedTotal > MaxOutputSize)
                    {
                        _outputTruncated = true;
                        Output.Add(new ScriptOutput(ScriptOutputKind.Result, "[Output truncated: exceeded 100KB limit]"));
                        return Task.CompletedTask;
                    }

                    _totalOutputSize = proposedTotal;
                    Output[ix] = so with { Order = oldOutput.Order, IsUpdate = false };
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
                Output.Add(new ScriptOutput(ScriptOutputKind.Result, "[Output truncated: exceeded 100KB limit]"));
                return Task.CompletedTask;
            }

            Output.Add(so);
        }

        return Task.CompletedTask;
    }
}
