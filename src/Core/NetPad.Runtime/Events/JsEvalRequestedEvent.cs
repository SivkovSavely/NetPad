namespace NetPad.Events;

/// <summary>
/// Published when a running script requests JavaScript to be evaluated in its results view.
/// </summary>
public record JsEvalRequestedEvent(Guid ScriptId, Guid CorrelationId, string Code, int TimeoutMs) : IEvent;
