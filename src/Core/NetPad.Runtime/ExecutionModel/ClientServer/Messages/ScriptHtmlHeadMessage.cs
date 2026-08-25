namespace NetPad.ExecutionModel.ClientServer.Messages;

public enum HtmlHeadEntryType
{
    Css = 1,
    CssLink = 2,
    ScriptLink = 3,
    Script = 4,
    Raw = 5
}

public record ScriptHtmlHeadEntry(int Type, string Content);

public record ScriptHtmlHeadMessage(ScriptHtmlHeadEntry[] Entries);
