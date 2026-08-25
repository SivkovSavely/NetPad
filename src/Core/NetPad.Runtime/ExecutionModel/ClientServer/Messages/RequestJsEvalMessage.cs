namespace NetPad.ExecutionModel.ClientServer.Messages;

public record RequestJsEvalMessage(Guid CorrelationId, string Code, int TimeoutMs);
