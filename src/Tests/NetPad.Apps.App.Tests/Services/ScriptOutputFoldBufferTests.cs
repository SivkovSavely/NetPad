using NetPad.Presentation;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

/// <summary>
/// The buffer shared by headless runs and GUI-run capture for folding mutable output updates.
/// </summary>
public sealed class ScriptOutputFoldBufferTests
{
    private readonly ScriptOutputFoldBuffer _buffer = new();

    private Task WriteAsync(ScriptOutput output) => _buffer.WriteAsync(output);

    [Fact]
    public async Task UpdatesAreFoldedIntoTheirInitialOutput()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 4, "initial", ScriptOutputFormat.Html)
        {
            OutputId = "mutable"
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 5, "second", ScriptOutputFormat.Text)
        {
            OutputId = "mutable", IsUpdate = true
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 6, "latest", ScriptOutputFormat.Html)
        {
            OutputId = "mutable", IsUpdate = true
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 7, "after"));

        Assert.Equal(2, _buffer.Output.Count);
        var mutable = _buffer.Output[0];
        Assert.Equal("latest", mutable.Body);
        Assert.Equal(ScriptOutputFormat.Html, mutable.Format);
        Assert.Equal((uint)4, mutable.Order);
        Assert.False(mutable.IsUpdate);
        Assert.Equal("after", _buffer.Output[1].Body);
    }

    [Fact]
    public async Task OrphanUpdatesAreDropped()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, "orphan-update")
        {
            OutputId = "never-dumped", IsUpdate = true
        });

        Assert.Empty(_buffer.Output);
    }

    [Fact]
    public async Task RepeatedSmallUpdatesDoNotTruncateSmallFinalOutput()
    {
        // Each intermediate version is large on its own, but the folded result is small.
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, new string('x', 90 * 1024))
        {
            OutputId = "mutable"
        });

        for (var i = 0; i < 10_000; i++)
        {
            await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, (uint)(2 + i), $"v{i}")
            {
                OutputId = "mutable", IsUpdate = true
            });
        }

        var output = Assert.Single(_buffer.Output);
        Assert.Equal("v9999", output.Body);
        Assert.DoesNotContain(_buffer.Output, o => o.Body?.Contains("truncated") == true);
    }

    [Fact]
    public async Task OversizedReplacementTriggersTruncation()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, new string('x', 90 * 1024))
        {
            OutputId = "mutable"
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 2, new string('y', 101 * 1024))
        {
            OutputId = "mutable", IsUpdate = true
        });

        Assert.Equal(2, _buffer.Output.Count);
        Assert.Contains("[Output truncated: exceeded 100KB limit]", _buffer.Output[1].Body);

        // Further output is ignored once truncated.
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 3, "ignored"));
        Assert.Equal(2, _buffer.Output.Count);
    }

    [Fact]
    public async Task ErrorsAreCollectedSeparately()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Error, 1, "(1,1): error CS0103: oh no"));
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 2, "value"));

        Assert.DoesNotContain(_buffer.Output, o => o.Kind == ScriptOutputKind.Error);
        Assert.Single(_buffer.Output);
        var error = Assert.Single(_buffer.Errors);
        Assert.Equal("(1,1): error CS0103: oh no", error);
    }

    [Fact]
    public async Task PanelsAreFlattenedAfterMainResultsWithHeadings()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, "main"));
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 2, "panel-a-1") { PanelName = "A" });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 3, "panel-b-1") { PanelName = "B" });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 4, "panel-a-2") { PanelName = "A" });

        var output = _buffer.Output;

        // Main results first (panel outputs removed), then panels in creation order with headings.
        Assert.Equal(
            new[] { "main", "[A]", "panel-a-1", "panel-a-2", "[B]", "panel-b-1" },
            output.Select(o => o.Body).ToArray());
    }

    [Fact]
    public async Task PanelUpdatesAreFoldedLikeMainSlots()
    {
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, "initial") { PanelName = "P", OutputId = "slot" });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 2, "updated")
        {
            PanelName = "P", OutputId = "slot", IsUpdate = true
        });

        var bodies = _buffer.Output.Select(o => o.Body).ToArray();
        Assert.Equal(new[] { "[P]", "updated" }, bodies);
    }
}
