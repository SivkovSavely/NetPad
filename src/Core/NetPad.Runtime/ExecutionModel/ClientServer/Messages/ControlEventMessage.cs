namespace NetPad.ExecutionModel.ClientServer.Messages;

/// <summary>A JSON-primitive browser event routed to one live script control.</summary>
public sealed record ControlEventMessage(
    string ControlId,
    string EventType,
    string PayloadJson,
    Guid? ScriptRunId = null);
