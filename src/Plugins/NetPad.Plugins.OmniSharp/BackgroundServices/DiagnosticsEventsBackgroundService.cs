using System.Text.Json;
using NetPad.Apps.UiInterop;
using NetPad.Events;
using NetPad.Plugins.OmniSharp.Events;
using NetPad.Apps;

namespace NetPad.Plugins.OmniSharp.BackgroundServices;

/// <summary>
/// Sends diagnostics events to IPC clients.
/// </summary>
/// <param name="ipcService"></param>
/// <param name="eventBus"></param>
/// <param name="loggerFactory"></param>
public class DiagnosticsEventsBackgroundService(
    IIpcService ipcService,
    IEventBus eventBus,
    ILoggerFactory loggerFactory) : BackgroundService(loggerFactory)
{
    private readonly List<IDisposable> _disposables = [];

    protected override Task StartingAsync(CancellationToken cancellationToken)
    {
        var serverStartSubscription = eventBus.Subscribe<OmniSharpServerStartedEvent>(ev =>
        {
            var server = ev.AppOmniSharpServer;
            var scriptId = server.ScriptId;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Diagnostic events are only emitted by a server instance after it receives this
                    // request. The frontend sends it when an editor attaches, which happens once per
                    // page load; servers started afterwards (restarts, auto-restarts) would otherwise
                    // never emit diagnostics again and stale editor squiggles would persist.
                    await server.OmniSharpServer.SendAsync(new OmniSharpDiagnosticRequest
                    {
                        FileName = server.Project.UserProgramFilePath
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to start diagnostics on OmniSharp server for script {ScriptId}", scriptId);
                }
            });

            server.OmniSharpServer.SubscribeToEvent("Diagnostic", async node =>
            {
                var body = node["Body"];

                var diagnostics = body?.Deserialize<OmniSharpDiagnosticMessage>();
                if (diagnostics == null)
                    return;

                // Only send diagnostics for user program
                var results = diagnostics.Results
                    .Where(d => d.FileName == server.Project.UserProgramFilePath)
                    .ToArray();

                if (results.Length == 0)
                {
                    return;
                }

                diagnostics.Results = results;

                await ipcService.SendAsync(new OmniSharpDiagnosticsEvent(scriptId, diagnostics));
            });

            return Task.CompletedTask;
        });

        _disposables.Add(serverStartSubscription);

        return Task.CompletedTask;
    }

    protected override Task StoppingAsync(CancellationToken cancellationToken)
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        return Task.CompletedTask;
    }
}
