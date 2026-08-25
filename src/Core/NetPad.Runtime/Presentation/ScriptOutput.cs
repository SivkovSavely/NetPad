using System.Text.Json.Serialization;

namespace NetPad.Presentation;

public enum ScriptOutputKind
{
    Result,
    Sql,
    Error
}

public enum ScriptOutputFormat
{
    Text,
    Html,
    Json
}

[method: JsonConstructor]
public record ScriptOutput(
    ScriptOutputKind Kind,
    uint Order,
    string? Body,
    ScriptOutputFormat Format = ScriptOutputFormat.Text)
{
    public string? OutputId { get; init; }
    public bool IsUpdate { get; init; }

    /// <summary>
    /// The name of the result panel this output belongs to; <c>null</c> means the main results view.
    /// </summary>
    public string? PanelName { get; init; }

    public ScriptOutput(ScriptOutputKind kind, string? body, ScriptOutputFormat format = ScriptOutputFormat.Text)
        : this(kind, 0, body, format)
    {
    }
}
