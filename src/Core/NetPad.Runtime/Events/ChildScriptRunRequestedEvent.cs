namespace NetPad.Events;

/// <summary>
/// Published when a running script requests another script be run as a child
/// (richer Util.Run). The reply is sent back to the script host as a message.
/// </summary>
public record ChildScriptRunRequestedEvent(Guid ParentScriptId, Guid CorrelationId, string Path) : IEvent;
