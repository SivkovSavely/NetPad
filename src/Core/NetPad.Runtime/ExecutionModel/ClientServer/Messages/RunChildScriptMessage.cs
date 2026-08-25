namespace NetPad.ExecutionModel.ClientServer.Messages;

/// <summary>
/// Sent from a running script host to request that another script be run as a child of this run
/// (richer Util.Run). The parent app orchestrates the child run and replies with
/// <see cref="ChildScriptCompletedMessage"/>.
/// </summary>
public record RunChildScriptMessage(Guid CorrelationId, string Path);
