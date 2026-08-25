namespace NetPad.Apps.CQs;

public class PromptUserForInputCommand(Guid scriptId, bool isMasked = false) : Command<string?>
{
    public Guid ScriptId { get; } = scriptId;

    /// <summary>When true, the client should mask the input (ex. password prompts).</summary>
    public bool IsMasked { get; } = isMasked;
}
