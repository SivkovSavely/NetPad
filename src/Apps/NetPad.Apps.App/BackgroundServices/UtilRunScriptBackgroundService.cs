using MediatR;
using Microsoft.Extensions.Logging;
using NetPad.Apps.CQs;
using NetPad.Events;
using NetPad.ExecutionModel;
using NetPad.Scripts;

namespace NetPad.BackgroundServices;

/// <summary>
/// Handles requests made by running scripts (via the script-host process) to open and run other
/// scripts. This powers <c>Util.Run(scriptPath)</c>.
/// </summary>
public class UtilRunScriptBackgroundService(
    IEventBus eventBus,
    IMediator mediator,
    ILoggerFactory loggerFactory)
    : NetPad.Apps.BackgroundService(loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<UtilRunScriptBackgroundService>();

    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        eventBus.Subscribe<RunScriptRequestedEvent>(async ev =>
        {
            try
            {
                var environment = await mediator.Send(new OpenScriptCommand(ev.Path)) as ScriptEnvironment;

                if (environment == null)
                {
                    _logger.LogError("Could not open script '{Path}' requested via Util.Run", ev.Path);
                    return;
                }

                await mediator.Send(new RunScriptCommand(environment.Script.Id, new RunOptions()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running script '{Path}' requested via Util.Run", ev.Path);
            }
        });

        return Task.CompletedTask;
    }
}
