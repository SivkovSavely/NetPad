using NetPad.ExecutionModel.ClientServer.Messages;

namespace NetPad.Events;

/// <summary>
/// Published when the set of Util.HtmlHead entries for a script's results document changes.
/// An empty entry list means previously added entries were cleared.
/// </summary>
public record ScriptHtmlHeadChangedEvent(Guid ScriptId, ScriptHtmlHeadEntry[] Entries) : IEvent;
