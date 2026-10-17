using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetPad.Common;

namespace NetPad.Compilation.Directives;

public sealed record LoadDirectiveResult(
    string Code,
    string InputHash,
    IReadOnlyList<string> LoadedPaths,
    IReadOnlyList<string> AdditionalUsings);

public static class LoadDirectiveProcessor
{
    private static readonly Regex _loadDirectivePattern = new(
        "^\\s*#load\\s+\"(?<path>[^\"]+)\"\\s*(?://.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static LoadDirectiveResult Process(string code, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(code);

        var state = new ProcessingState();
        var output = new StringBuilder(code.Length);
        var normalizedSourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? null
            : Path.GetFullPath(sourcePath);
        var activePaths = new List<string>();

        if (normalizedSourcePath != null)
        {
            activePaths.Add(normalizedSourcePath);
        }

        AppendSource(code, normalizedSourcePath, 1, output, state, activePaths);

        var hashInput = string.Join("\n", state.LoadedFiles.Select(x => $"{x.Path}\0{x.Hash}")
            .Concat(state.Errors));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();

        return new LoadDirectiveResult(output.ToString(), hash, state.LoadedPaths, state.AdditionalUsings);
    }

    private static void AppendSource(
        string code,
        string? sourcePath,
        int firstLine,
        StringBuilder output,
        ProcessingState state,
        List<string> activePaths)
    {
        var directiveLines = CSharpSyntaxTree.ParseText(code).GetRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Where(trivia => trivia.IsDirective)
            .Select(trivia => trivia.GetLocation().GetLineSpan().StartLinePosition.Line)
            .ToHashSet();
        using var reader = new StringReader(code);
        var lineNumber = firstLine;
        var localLineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmedLine = line.TrimStart();
            if (!directiveLines.Contains(localLineNumber) || !trimmedLine.StartsWith("#load", StringComparison.Ordinal))
            {
                output.AppendLine(line);
                lineNumber++;
                localLineNumber++;
                continue;
            }

            var match = _loadDirectivePattern.Match(line);
            if (!match.Success)
            {
                AppendError(output, state, sourcePath, lineNumber, "invalid #load directive; expected #load \"path-or-pattern\"");
                lineNumber++;
                localLineNumber++;
                continue;
            }

            var pattern = match.Groups["path"].Value;
            IReadOnlyList<string> paths;
            try
            {
                paths = ResolvePaths(pattern, sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppendError(output, state, sourcePath, lineNumber, $"could not resolve #load '{pattern}': {ex.Message}");
                lineNumber++;
                localLineNumber++;
                continue;
            }

            if (paths.Count == 0)
            {
                var reason = !Path.IsPathRooted(pattern) && sourcePath == null
                    ? "relative #load paths require the script to be saved"
                    : $"#load path or pattern '{pattern}' matched no files";
                AppendError(output, state, sourcePath, lineNumber, reason);
                lineNumber++;
                localLineNumber++;
                continue;
            }

            foreach (var loadedPath in paths)
            {
                if (activePaths.Any(path => string.Equals(path, loadedPath, PlatformUtil.PathComparison)))
                {
                    AppendError(output, state, sourcePath, lineNumber, $"#load cycle detected for '{loadedPath}'");
                    continue;
                }

                string loadedText;
                try
                {
                    loadedText = File.ReadAllText(loadedPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    AppendError(output, state, sourcePath, lineNumber, $"could not read #load file '{loadedPath}': {ex.Message}");
                    continue;
                }

                state.LoadedPaths.Add(loadedPath);
                state.LoadedFiles.Add((loadedPath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loadedText)))));

                LoadedSource loadedSource;
                try
                {
                    loadedSource = ParseLoadedSource(loadedPath, loadedText);
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    AppendError(output, state, sourcePath, lineNumber, $"could not read loaded script '{loadedPath}': {ex.Message}");
                    continue;
                }

                foreach (var @using in loadedSource.Usings)
                {
                    if (!state.AdditionalUsingSet.Add(@using))
                    {
                        continue;
                    }

                    state.AdditionalUsings.Add(@using);
                }

                activePaths.Add(loadedPath);
                AppendLineDirective(output, loadedSource.FirstCodeLine, loadedPath);
                AppendSource(loadedSource.Code, loadedPath, loadedSource.FirstCodeLine, output, state, activePaths);
                activePaths.RemoveAt(activePaths.Count - 1);
                AppendLineDirective(output, lineNumber + 1, sourcePath);
            }

            lineNumber++;
            localLineNumber++;
        }
    }

    private static IReadOnlyList<string> ResolvePaths(string pattern, string? sourcePath)
    {
        if (!Path.IsPathRooted(pattern) && sourcePath == null)
        {
            return [];
        }

        var baseDirectory = sourcePath == null ? null : Path.GetDirectoryName(sourcePath);
        var fullPattern = Path.IsPathRooted(pattern)
            ? Path.GetFullPath(pattern)
            : Path.GetFullPath(pattern, baseDirectory!);

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return File.Exists(fullPattern) ? [fullPattern] : [];
        }

        var pathRoot = Path.GetPathRoot(fullPattern)
                       ?? throw new ArgumentException($"Invalid path pattern '{pattern}'");
        var segments = fullPattern[pathRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<string>();
        Expand(pathRoot, segments, 0, matches);
        return matches.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static void Expand(string currentPath, string[] segments, int index, List<string> matches)
    {
        if (index == segments.Length)
        {
            if (File.Exists(currentPath))
            {
                matches.Add(Path.GetFullPath(currentPath));
            }

            return;
        }

        var segment = segments[index];
        if (segment == "**")
        {
            Expand(currentPath, segments, index + 1, matches);
            foreach (var directory in EnumerateDirectories(currentPath))
            {
                Expand(directory, segments, index, matches);
            }

            return;
        }

        var isFileSegment = index == segments.Length - 1;
        if (!segment.Contains('*') && !segment.Contains('?'))
        {
            var nextPath = Path.Combine(currentPath, segment);
            if (isFileSegment)
            {
                Expand(nextPath, segments, index + 1, matches);
            }
            else if (Directory.Exists(nextPath))
            {
                Expand(nextPath, segments, index + 1, matches);
            }

            return;
        }

        if (isFileSegment)
        {
            foreach (var file in Directory.EnumerateFiles(currentPath)
                         .Where(path => MatchesSegment(Path.GetFileName(path), segment)))
            {
                Expand(file, segments, index + 1, matches);
            }
        }
        else
        {
            foreach (var directory in EnumerateDirectories(currentPath)
                         .Where(path => MatchesSegment(Path.GetFileName(path), segment)))
            {
                Expand(directory, segments, index + 1, matches);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string path)
    {
        return Directory.EnumerateDirectories(path)
            .Where(directory => (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            .OrderBy(directory => directory, StringComparer.Ordinal);
    }

    private static bool MatchesSegment(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var options = PlatformUtil.PathComparison == StringComparison.OrdinalIgnoreCase
            ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            : RegexOptions.CultureInvariant;
        return Regex.IsMatch(value, regex, options);
    }

    private static LoadedSource ParseLoadedSource(string path, string text)
    {
        string code = text;
        var firstCodeLine = 1;
        var usings = new List<string>();

        if (path.EndsWith(".netpad", PlatformUtil.PathComparison))
        {
            var lines = text.Split('\n');
            var codeMarkerIndex = Array.FindIndex(lines, line => line.TrimEnd('\r').Trim() == "#Code");
            if (codeMarkerIndex < 0)
            {
                throw new InvalidDataException("The script is missing its #Code marker.");
            }

            var configJson = string.Join("\n", lines.Skip(1).Take(codeMarkerIndex - 1)).Trim();
            if (configJson.Length > 0)
            {
                using var document = JsonDocument.Parse(configJson);
                var config = GetProperty(document.RootElement, "Config") ?? document.RootElement;
                var namespaces = GetProperty(config, "Namespaces");
                if (namespaces?.ValueKind == JsonValueKind.Array)
                {
                    usings.AddRange(namespaces.Value.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()!)
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            code = string.Join("\n", lines.Skip(codeMarkerIndex + 1));
            firstCodeLine = codeMarkerIndex + 2;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var usingDirectives = syntaxTree.GetCompilationUnitRoot().Usings;
        if (usingDirectives.Count > 0)
        {
            var chars = code.ToCharArray();
            foreach (var usingDirective in usingDirectives)
            {
                var value = new StringBuilder();
                if (!usingDirective.StaticKeyword.IsKind(SyntaxKind.None))
                {
                    value.Append("static ");
                }

                if (usingDirective.Alias != null)
                {
                    value.Append(usingDirective.Alias.Name).Append(" = ");
                }

                value.Append(usingDirective.Name);
                usings.Add(value.ToString());

                for (var index = usingDirective.SpanStart; index < usingDirective.Span.End; index++)
                {
                    if (chars[index] is not ('\r' or '\n'))
                    {
                        chars[index] = ' ';
                    }
                }
            }

            code = new string(chars);
        }

        return new LoadedSource(code, firstCodeLine, usings);
    }

    private static JsonElement? GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static void AppendError(StringBuilder output, ProcessingState state, string? sourcePath, int lineNumber, string message)
    {
        var error = $"#load at line {lineNumber}: {message}";
        state.Errors.Add($"{sourcePath ?? "<unsaved script>"}:{lineNumber}:{error}");
        AppendLineDirective(output, lineNumber, sourcePath);
        output.Append("#error ").AppendLine(error);
        AppendLineDirective(output, lineNumber + 1, sourcePath);
    }

    private static void AppendLineDirective(StringBuilder output, int lineNumber, string? sourcePath)
    {
        output.Append("#line ").Append(lineNumber);
        if (sourcePath != null)
        {
            var escapedPath = sourcePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            output.Append(" \"").Append(escapedPath).Append('\"');
        }

        output.AppendLine();
    }

    private sealed class ProcessingState
    {
        public List<string> LoadedPaths { get; } = [];
        public List<(string Path, string Hash)> LoadedFiles { get; } = [];
        public List<string> AdditionalUsings { get; } = [];
        public HashSet<string> AdditionalUsingSet { get; } = new(StringComparer.Ordinal);
        public List<string> Errors { get; } = [];
    }

    private sealed record LoadedSource(string Code, int FirstCodeLine, IReadOnlyList<string> Usings);
}
