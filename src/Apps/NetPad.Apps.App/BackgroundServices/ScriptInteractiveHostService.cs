using Microsoft.Extensions.Logging;
using NetPad.Apps;
using NetPad.Apps.CQs;
using NetPad.Apps.UiInterop;
using NetPad.Events;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.Services;
using NetPad.Sessions;

namespace NetPad.BackgroundServices;

/// <summary>
/// Relays interactive requests made by running scripts to their results views and orchestrates
/// child-script runs for the richer Util.Run API.
/// </summary>
public class ScriptInteractiveHostService(
    IEventBus eventBus,
    IIpcService ipcService,
    ISession session,
    HeadlessScriptExecutionService headlessScriptExecutionService,
    ILoggerFactory loggerFactory)
    : BackgroundService(loggerFactory)
{
    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        eventBus.Subscribe<JsEvalRequestedEvent>(async ev =>
        {
            try
            {
                var response = await ipcService.SendAndReceiveAsync(new RunJsInResultsCommand(
                    ev.ScriptId,
                    ev.CorrelationId,
                    ev.Code,
                    ev.TimeoutMs));

                SendToHost(ev.ScriptId, new JsEvalResultMessage(
                    ev.CorrelationId,
                    response?.ResultJson,
                    response?.Error));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error relaying JS eval request for script {ScriptId}", ev.ScriptId);
                SendToHost(ev.ScriptId, new JsEvalResultMessage(ev.CorrelationId, null, ex.Message));
            }
        });

        eventBus.Subscribe<ChildScriptRunRequestedEvent>(async ev =>
        {
            ChildScriptCompletedMessage completion;

            try
            {
                var result = await headlessScriptExecutionService.RunByPathAsync(
                    ev.Path,
                    timeoutMs: null,
                    CancellationToken.None);

                completion = new ChildScriptCompletedMessage(
                    ev.CorrelationId,
                    result.Success,
                    result.Error,
                    result.Output?.ToArray());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error running child script {Path} for script {ScriptId}", ev.Path, ev.ParentScriptId);
                completion = new ChildScriptCompletedMessage(ev.CorrelationId, false, ex.ToString(), null);
            }

            SendToHost(ev.ParentScriptId, completion);
        });

        return Task.CompletedTask;
    }

    private void SendToHost(Guid scriptId, object message)
    {
        var environment = session.Get(scriptId);
        if (environment == null)
        {
            Logger.LogWarning("Cannot send message to script {ScriptId}: no environment found", scriptId);
            return;
        }

        try
        {
            environment.SendToScriptHost(message);
        }
        catch (Exception ex)
        {
            // The runner may not support direct host messaging (ex. external runs).
            Logger.LogWarning(ex, "Could not send message to script-host of script {ScriptId}", scriptId);
        }
    }
}
