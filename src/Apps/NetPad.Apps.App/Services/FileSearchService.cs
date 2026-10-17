using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetPad.Configuration;

namespace NetPad.Services;

public sealed record FileSearchResult(string Path, int LineNumber, string Line);

public sealed class FileSearchService(Settings settings)
{
    private readonly ConcurrentDictionary<string, string> _openFiles = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public sealed record OpenFileResult(string Handle, string Path, string Text);

    public async Task<OpenFileResult> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = GetAllowedPath(path);
        var text = await ReadTextAsync(fullPath, cancellationToken);
        var handle = Guid.NewGuid().ToString("N");
        _openFiles[handle] = fullPath;
        return new OpenFileResult(handle, fullPath, text);
    }

    public async Task<string> ReloadAsync(string handle, CancellationToken cancellationToken) =>
        await ReadTextAsync(GetHandlePath(handle), cancellationToken);

    public async Task SaveAsync(string handle, string text, CancellationToken cancellationToken)
    {
        var path = GetHandlePath(handle);
        await File.WriteAllTextAsync(path, text, StrictUtf8, cancellationToken);
    }

    private string GetHandlePath(string handle) =>
        _openFiles.TryGetValue(handle, out var path) ? GetAllowedPath(path) : throw new FileNotFoundException("Open file handle not found.");

    private string GetAllowedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetExtension(fullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(fullPath).Equals(".text", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported file type.");

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var allowed = settings.GetScriptLibraryRoots().Any(root =>
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, comparison);
        });
        if (!allowed) throw new UnauthorizedAccessException("File is outside the script library roots.");
        return fullPath;
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Contains((byte)0)) throw new InvalidDataException("Binary files are not supported.");
        return StrictUtf8.GetString(bytes);
    }

    public async Task<IReadOnlyList<FileSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var results = new List<FileSearchResult>();
        var seenPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in settings.GetScriptLibraryRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenPaths.Add(Path.GetFullPath(path))) continue;
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".netpad", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".text", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    if (bytes.Contains((byte)0)) continue;
                    var lines = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
                    var inCode = !extension.Equals(".netpad", StringComparison.OrdinalIgnoreCase);
                    for (var i = 0; i < lines.Length && results.Count < 500; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!inCode)
                        {
                            if (lines[i].Trim() == "#Code") inCode = true;
                            continue;
                        }
                        var line = lines[i].TrimEnd('\r');
                        if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                            results.Add(new FileSearchResult(path, i + 1, line));
                    }
                    if (results.Count >= 500) return results;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return results;
    }
}
