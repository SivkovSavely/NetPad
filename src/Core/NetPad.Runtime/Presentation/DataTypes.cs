using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using NetPad.Presentation.Html;

namespace NetPad.Presentation;

/// <summary>
/// Options controlling CSV generation (<c>Util.ToCsvString</c>/<c>Util.WriteCsv</c>).
/// </summary>
public sealed record CsvOptions
{
    /// <summary>The field delimiter. Default ','.</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>The culture used to format values. Defaults to the invariant culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Whether a header row is written. Default true.</summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>The line terminator. Defaults to CRLF per RFC 4180.</summary>
    public string NewLine { get; init; } = "\r\n";
}

/// <summary>
/// Information about a NetPad script discovered in the script library (<c>Util.GetMyScripts</c>).
/// </summary>
public sealed record ScriptFileInfo(string Name, string Path, string Root, string Kind)
{
    public static ScriptFileInfo? TryCreate(string filePath, string root)
    {
        try
        {
            using var reader = new StreamReader(filePath);

            // .netpad format: {id}\n{json}\n#Code\n{code}
            var idLine = reader.ReadLine();
            var jsonLine = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(idLine) || jsonLine is null || !jsonLine.StartsWith('{'))
            {
                return null;
            }

            string kind = "Program";
            string? name = null;

            using (var json = JsonDocument.Parse(jsonLine))
            {
                if (json.RootElement.TryGetProperty("kind", out var kindEl) &&
                    kindEl.ValueKind == JsonValueKind.String)
                {
                    kind = kindEl.GetString() ?? kind;
                }

                if (json.RootElement.TryGetProperty("name", out var nameEl) &&
                    nameEl.ValueKind == JsonValueKind.String)
                {
                    name = nameEl.GetString();
                }
            }

            name ??= System.IO.Path.GetFileNameWithoutExtension(filePath);

            return new ScriptFileInfo(name, filePath, root, kind);
        }
        catch
        {
            // Unreadable/unrecognized files are skipped rather than failing enumeration.
            return null;
        }
    }
}

/// <summary>
/// Accumulates dumped output and renders it as a standalone HTML document.
/// Created via <c>Util.CreateXhtmlWriter()</c>; serialization reuses the standard HTML
/// presentation pipeline.
/// </summary>
public sealed class XhtmlWriter
{
    private readonly StringBuilder _body = new();

    /// <summary>Serializes a value and appends it to the document body.</summary>
    public XhtmlWriter Write(object? value)
    {
        if (value != null)
        {
            _body.Append(HtmlPresenter.Serialize(value));
        }

        return this;
    }

    /// <summary>Serializes a value and appends it to the document body, followed by a line break.</summary>
    public XhtmlWriter WriteLine(object? value)
    {
        Write(value);
        _body.AppendLine();
        return this;
    }

    public override string ToString()
    {
        return $$"""
                 <!DOCTYPE html>
                 <html xmlns="http://www.w3.org/1999/xhtml">
                 <head>
                     <meta charset="utf-8" />
                     <title>NetPad Output</title>
                 </head>
                 <body>
                 {{_body}}
                 </body>
                 </html>
                 """;
    }
}
