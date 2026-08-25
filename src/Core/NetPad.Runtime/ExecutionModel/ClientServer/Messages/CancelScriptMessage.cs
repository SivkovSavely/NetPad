namespace NetPad.ExecutionModel.ClientServer.Messages;

/// <summary>
/// Requests cooperative cancellation of the running script. If the script does not stop within
/// the grace period the app falls back to a hard stop.
/// </summary>
public record CancelScriptMessage;
