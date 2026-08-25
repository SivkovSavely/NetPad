using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetPad.Apps.UiInterop;
using NetPad.Common;
using NetPad.Events;
using NetPad.ExecutionModel;
using NetPad.Presentation;
using NetPad.Scripts;
using NetPad.Scripts.Events;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class ScriptEnvironmentIpcOutputWriterTests : IDisposable
{
    private static readonly TimeSpan _messageTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait before concluding that a message will not be sent.</summary>
    private static readonly TimeSpan _noMessageGracePeriod = TimeSpan.FromMilliseconds(500);

    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IEventBus _eventBus;
    private readonly ScriptEnvironment _environment;
    private readonly ScriptEnvironmentIpcOutputWriter _writer;
    private readonly List<IpcMessage> _sentMessages = [];
    private readonly Lock _sentMessagesLock = new();
    private readonly TaskCompletionSource _messageSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScriptEnvironmentIpcOutputWriterTests()
    {
        var runnerFactory = new Mock<IScriptRunnerFactory>();
        runnerFactory.Setup(f => f.CreateRunner(It.IsAny<Script>())).Returns(Mock.Of<IScriptRunner>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton(runnerFactory.Object);

        _serviceProvider = services.BuildServiceProvider();
        _serviceScope = _serviceProvider.CreateScope();
        _eventBus = _serviceProvider.GetRequiredService<IEventBus>();

        _environment = new ScriptEnvironment(
            new Script(Guid.NewGuid(), "Test", new ScriptConfig(ScriptKind.Program, GlobalConsts.AppDotNetFrameworkVersion)),
            _serviceScope);

        var ipcService = new Mock<IIpcService>();
        ipcService
            .Setup(s => s.SendAsync(It.IsAny<IpcMessageBatch>(), It.IsAny<CancellationToken>()))
            .Callback<IpcMessageBatch, CancellationToken>((batch, _) =>
            {
                lock (_sentMessagesLock)
                {
                    _sentMessages.AddRange(batch.Messages);
                }

                _messageSent.TrySetResult();
            })
            .Returns(Task.CompletedTask);

        _writer = new ScriptEnvironmentIpcOutputWriter(
            _environment,
            ipcService.Object,
            _eventBus,
            NullLogger<ScriptEnvironmentIpcOutputWriter>.Instance);
    }

    [Fact]
    public async Task SystemNotice_IsSentAfterScriptIsStopped()
    {
        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);
        await SetStatusAsync(ScriptStatus.Running, ScriptStatus.Stopping);

        await _writer.WriteAsync(new ScriptOutput(ScriptOutputKind.Result, "Script stopped at: 12:00:00 AM"));

        var message = Assert.Single(await WaitForSentMessagesAsync());
        var emitted = Assert.IsType<ScriptOutputEmittedEvent>(message.Message);
        var body = emitted.Output.Body?.Replace("&nbsp;", " ");

        Assert.Contains("Script stopped at: 12:00:00 AM", body);
        Assert.Contains("raw", body);
    }

    [Fact]
    public async Task UserOutput_IsSentWhileScriptIsRunning()
    {
        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);

        await _writer.WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 0, "<span>output</span>", ScriptOutputFormat.Html));

        var message = Assert.Single(await WaitForSentMessagesAsync());
        var emitted = Assert.IsType<ScriptOutputEmittedEvent>(message.Message);
        Assert.Equal("<span>output</span>", emitted.Output.Body);
    }

    [Fact]
    public async Task UserOutput_IsNotSentAfterScriptIsStopped()
    {
        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);
        await SetStatusAsync(ScriptStatus.Running, ScriptStatus.Stopping);

        await _writer.WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 0, "<span>output</span>", ScriptOutputFormat.Html));

        await Task.Delay(_noMessageGracePeriod);

        Assert.Empty(GetSentMessages());
    }

    [Fact]
    public async Task OutputUpdate_IsSentAfterUserOutputLimitIsReached()
    {
        const int maxUserOutputMessagesPerRun = 10100;

        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);

        // An initially dumped mutable output consumes a normal user output slot.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result, 1, "initial", ScriptOutputFormat.Html)
        {
            OutputId = "output-id"
        });

        // Ordinary results consume the rest of the per-run user output budget.
        for (var i = 2; i <= maxUserOutputMessagesPerRun; i++)
        {
            await _writer.WriteAsync(new ScriptOutput(ScriptOutputKind.Result, (uint)i, "ordinary", ScriptOutputFormat.Html));
        }

        // The next ordinary result exceeds the limit and must be dropped.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result,
            maxUserOutputMessagesPerRun + 1,
            "dropped",
            ScriptOutputFormat.Html));

        // An IsUpdate without an OutputId is not a genuine update and gets no bypass either.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result,
            maxUserOutputMessagesPerRun + 2,
            "malformed",
            ScriptOutputFormat.Html)
        {
            IsUpdate = true
        });

        // Wait until the full ordinary budget drains through IPC, then until the limit notice is sent.
        await WaitForEmittedAsync(output => !output.Output.IsUpdate && output.Output.Order == maxUserOutputMessagesPerRun);
        await WaitForEmittedAsync(output => output.Output.Body?.Replace("&nbsp;", " ").Contains("Output limit reached.") == true);

        var emittedEvents = GetSentMessages()
            .Select(message => message.Message)
            .OfType<ScriptOutputEmittedEvent>()
            .ToList();

        // The initial mutable output and every ordinary result filled the normal budget exactly.
        var normalOutputs = emittedEvents.Where(emitted => !emitted.Output.IsUpdate && emitted.Output.Order > 0).ToList();
        Assert.Equal(maxUserOutputMessagesPerRun, normalOutputs.Count);

        var initialMutable = Assert.Single(normalOutputs, emitted => emitted.Output.OutputId == "output-id");
        Assert.Equal("initial", initialMutable.Output.Body);
        Assert.False(initialMutable.Output.IsUpdate);

        Assert.DoesNotContain(normalOutputs, emitted => emitted.Output.Body == "dropped");
        Assert.Single(emittedEvents, emitted => emitted.Output.Body?.Replace("&nbsp;", " ").Contains("Output limit reached.") == true);
        Assert.DoesNotContain(emittedEvents, emitted => emitted.Output.Body == "malformed");

        // A genuine update is still emitted after the limit is reached, unordered so the frontend does
        // not wait forever for the sequential order of the dropped ordinary results that preceded it.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result,
            maxUserOutputMessagesPerRun + 3,
            "updated",
            ScriptOutputFormat.Html)
        {
            OutputId = "output-id",
            IsUpdate = true
        });

        var update = await WaitForEmittedAsync(output =>
            output.Output.OutputId == "output-id" &&
            output.Output.IsUpdate &&
            output.Output.Body == "updated");

        Assert.Equal(0u, update.Output.Order);
    }

    [Fact]
    public async Task OutputUpdates_AreBoundedPerRun()
    {
        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);

        for (var i = 0; i < 10101; i++)
        {
            await _writer.WriteAsync(new ScriptOutput(
                ScriptOutputKind.Result, 0, "updated", ScriptOutputFormat.Html)
            {
                OutputId = "output-id",
                IsUpdate = true
            });
        }

        var deadline = DateTime.UtcNow + _messageTimeout;
        while (DateTime.UtcNow < deadline && GetSentMessages().Count < 10100)
        {
            await Task.Delay(50);
        }

        var updates = GetSentMessages().Count(message =>
            message.Message is ScriptOutputEmittedEvent {Output.IsUpdate: true});
        Assert.Equal(10100, updates);
    }

    [Fact]
    public async Task OrdinaryResultAfterDroppedUpdate_IsSentUnordered()
    {
        const int maxUserOutputMessagesPerRun = 10100;

        await SetStatusAsync(ScriptStatus.Ready, ScriptStatus.Running);

        // An initially dumped mutable output consumes a normal user output slot.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result, 1, "initial", ScriptOutputFormat.Html)
        {
            OutputId = "output-id"
        });

        // Updates 2..10102: the first 10100 are sent, the last one exceeds the per-run mutation
        // budget and is dropped, leaving a permanent gap at its order.
        for (var i = 2; i <= maxUserOutputMessagesPerRun + 2; i++)
        {
            await _writer.WriteAsync(new ScriptOutput(
                ScriptOutputKind.Result, (uint)i, "updated", ScriptOutputFormat.Html)
            {
                OutputId = "output-id",
                IsUpdate = true
            });
        }

        // Wait until the last accepted update drains through IPC, then past the grace period so
        // the dropped update has every chance to (wrongly) appear.
        await WaitForEmittedAsync(output => output.Output.IsUpdate && output.Output.Order == maxUserOutputMessagesPerRun + 1);
        await Task.Delay(_noMessageGracePeriod);

        var emittedEvents = GetSentMessages()
            .Select(message => message.Message)
            .OfType<ScriptOutputEmittedEvent>()
            .ToList();

        // The mutation budget allows exactly Max updates; the one beyond it is not sent.
        Assert.Equal(maxUserOutputMessagesPerRun, emittedEvents.Count(emitted => emitted.Output.IsUpdate));
        Assert.DoesNotContain(emittedEvents, emitted => emitted.Output.Order == maxUserOutputMessagesPerRun + 2);

        // Updates sent before the drop keep their sequential order.
        Assert.Contains(emittedEvents, emitted => emitted.Output.IsUpdate && emitted.Output.Order == 2);

        // A subsequent ordinary result is under its separate budget and would normally queue with
        // its own order; it must go out unordered instead or the frontend would wait forever for
        // the order of the dropped update.
        await _writer.WriteAsync(new ScriptOutput(
            ScriptOutputKind.Result,
            maxUserOutputMessagesPerRun + 3,
            "after-drop",
            ScriptOutputFormat.Html));

        var afterDrop = await WaitForEmittedAsync(output =>
            !output.Output.IsUpdate && output.Output.Body == "after-drop");

        Assert.Equal(0u, afterDrop.Output.Order);
    }

    private async Task<ScriptOutputEmittedEvent> WaitForEmittedAsync(Func<ScriptOutputEmittedEvent, bool> predicate)
    {
        var deadline = DateTime.UtcNow + _messageTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = GetSentMessages()
                .Select(message => message.Message)
                .OfType<ScriptOutputEmittedEvent>()
                .FirstOrDefault(predicate);

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for the expected output to be sent.");
    }

    private async Task SetStatusAsync(ScriptStatus oldStatus, ScriptStatus newStatus)
    {
        await _eventBus.PublishAsync(new EnvironmentPropertyChangedEvent(
            _environment.Script.Id,
            nameof(ScriptEnvironment.Status),
            oldStatus,
            newStatus));
    }

    private async Task<IReadOnlyList<IpcMessage>> WaitForSentMessagesAsync()
    {
        var completed = await Task.WhenAny(_messageSent.Task, Task.Delay(_messageTimeout));

        Assert.True(completed == _messageSent.Task, "Timed out waiting for output to be sent to IPC clients.");

        return GetSentMessages();
    }

    private IReadOnlyList<IpcMessage> GetSentMessages()
    {
        lock (_sentMessagesLock)
        {
            return _sentMessages.ToArray();
        }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _environment.Dispose();
        _serviceScope.Dispose();
        _serviceProvider.Dispose();
    }
}
