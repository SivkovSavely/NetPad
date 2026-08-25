using NetPad.Apps.CQs;

namespace NetPad.Apps.CQs;

/// <summary>
/// Sent from the host to the client that owns a script's results view, asking it to evaluate
/// JavaScript in that view (Util.JS). The client replies with the JSON-serialized result or error.
/// </summary>
public class RunJsInResultsCommand(
    Guid scriptId,
    Guid correlationId,
    string code,
    int timeoutMs)
    : Command<JsEvalResponse>
{
    public Guid ScriptId { get; } = scriptId;

    public Guid CorrelationId { get; } = correlationId;

    public string Code { get; } = code;

    public int TimeoutMs { get; } = timeoutMs;
}

public class JsEvalResponse
{
    /// <summary>The JSON-serialized evaluation result, when successful.</summary>
    public string? ResultJson { get; set; }

    /// <summary>The error message, when evaluation failed.</summary>
    public string? Error { get; set; }
}
