namespace NetPad.Events;

/// <summary>
/// Published when a running script requests user input. Consumers use this to prepare
/// input UIs (ex. masked password entry) before the input request reaches them.
/// </summary>
public record ScriptUserInputRequestedEvent(Guid ScriptId, bool IsMasked) : IEvent;
