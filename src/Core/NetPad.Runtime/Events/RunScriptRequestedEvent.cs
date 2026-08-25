namespace NetPad.Events;

/// <summary>
/// Published when a running script requests that another script be opened and run.
/// </summary>
public record RunScriptRequestedEvent(string Path) : IEvent;
