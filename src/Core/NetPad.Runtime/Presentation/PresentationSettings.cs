using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NetPad.Presentation;

public static class PresentationSettings
{
    public const int MaxDepth = 64;
    public const int MaxCollectionLength = 1000;

    /// <summary>
    /// Reads presentation configuration from "scriptconfig.json" file. This is read and configured per-script; the
    /// "scriptconfig.json" file is deployed along with the script when it is executed.
    /// </summary>
    public static (uint? maxDepth, uint? maxCollectionSerializeLength) GetConfigFileValues()
    {
        var (maxDepth, maxCollectionSerializeLength, _) = ReadConfigFile();
        return (maxDepth, maxCollectionSerializeLength);
    }

    /// <summary>
    /// Reads the scripts library directory snapshot written to "scriptconfig.json"
    /// (used by <c>Util.GetMyScripts</c>).
    /// </summary>
    public static string? GetScriptsDirectoryPath() => ReadConfigFile().scriptsDirectoryPath;

    private static (uint? maxDepth, uint? maxCollectionSerializeLength, string? scriptsDirectoryPath) ReadConfigFile()
    {
        var scriptConfigFilePath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? string.Empty) ?? string.Empty,
            "scriptconfig.json"
        );

        if (!File.Exists(scriptConfigFilePath)) return (null, null, null);

        uint? maxDepth = null;
        uint? maxCollectionSerializeLength = null;
        string? scriptsDirectoryPath = null;

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(scriptConfigFilePath));

            if (!json.RootElement.TryGetProperty("output", out var outputSettings)) return (null, null, null);

            if (outputSettings.TryGetProperty("maxDepth", out var prop) && prop.TryGetUInt32(out var md))
            {
                maxDepth = md;
            }

            if (outputSettings.TryGetProperty("maxCollectionSerializeLength", out prop) && prop.TryGetUInt32(out md))
            {
                maxCollectionSerializeLength = md;
            }

            if (json.RootElement.TryGetProperty("scriptsDirectoryPath", out var dirProp) &&
                dirProp.ValueKind == JsonValueKind.String)
            {
                scriptsDirectoryPath = dirProp.GetString();
            }

            return (maxDepth, maxCollectionSerializeLength, scriptsDirectoryPath);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
