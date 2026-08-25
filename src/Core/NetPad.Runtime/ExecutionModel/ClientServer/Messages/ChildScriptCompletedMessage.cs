using NetPad.Presentation;

namespace NetPad.ExecutionModel.ClientServer.Messages;

public record ChildScriptCompletedMessage(
    Guid CorrelationId,
    bool Success,
    string? Error,
    ScriptOutput[]? Output);
