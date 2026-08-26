using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetPad.Apps.CQs;
using NetPad.Apps.UiInterop;
using NetPad.BackgroundServices;
using NetPad.Events;
using NetPad.Services;
using NetPad.Sessions;

namespace NetPad.Apps.App.Tests.BackgroundServices;

public sealed class ScriptInteractiveHostServiceTests
{
    [Fact]
    public async Task JsEvaluationRequestsAreForwardedToTheClient()
    {
        var eventBus = new EventBus();
        var ipc = new Mock<IIpcService>();
        var session = new Mock<ISession>();
        var jsResponse = new JsEvalResponse { ResultJson = "42" };

        ipc.Setup(x => x.SendAndReceiveAsync(
                It.IsAny<RunJsInResultsCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsResponse);

        var service = new ScriptInteractiveHostService(
            eventBus,
            ipc.Object,
            session.Object,
            headlessScriptExecutionService: null!,
            NullLoggerFactory.Instance);

        await service.StartAsync(CancellationToken.None);

        var scriptId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await eventBus.PublishAsync(new JsEvalRequestedEvent(scriptId, correlationId, "1 + 1", 1000));

        ipc.Verify(x => x.SendAndReceiveAsync(
                It.Is<RunJsInResultsCommand>(command =>
                    command.ScriptId == scriptId &&
                    command.CorrelationId == correlationId &&
                    command.Code == "1 + 1" &&
                    command.TimeoutMs == 1000),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
