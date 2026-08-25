namespace NetPad.ExecutionModel.ClientServer.Messages;

/// <param name="IsMasked">When true, the app should mask the user's input (ex. password prompts).</param>
public record RequestUserInputMessage(bool IsMasked = false);
