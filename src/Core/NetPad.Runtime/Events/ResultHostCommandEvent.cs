using NetPad.ExecutionModel.ClientServer.Messages;

namespace NetPad.Events;

/// <summary>
/// Published when a running script issues a result-host command (clear results, hide panes,
/// auto-scroll, open/remove panels).
/// </summary>
public record ResultHostCommandEvent(Guid ScriptId, ResultHostCommand Command, string? PayloadJson) : IEvent;
