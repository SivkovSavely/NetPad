namespace NetPad.ExecutionModel.ClientServer.Messages;

public record JsEvalResultMessage(Guid CorrelationId, string? ResultJson, string? Error);
