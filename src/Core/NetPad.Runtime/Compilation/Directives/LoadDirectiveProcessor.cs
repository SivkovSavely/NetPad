using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetPad.Common;
using NetPad.DotNet;

namespace NetPad.Compilation.Directives;

public sealed record LoadDirectiveResult(
    string Code,
    string InputHash,
    IReadOnlyList<string> LoadedPaths,
    IReadOnlyList<string> AdditionalUsings,
    IReadOnlyList<Reference> AdditionalReferences,
    IReadOnlyList<string> ToDumpTransformers)
{
    public string GetCompilationCode(string? sourcePath, string? entryPoint = null)
    {
        if (entryPoint != null)
        {
            return GetEntryPointCompilationCode(sourcePath, entryPoint);
        }

        return Code;
    }

    public string GetToDumpInitializerCode()
    {
        var builder = new StringBuilder(ToDumpTransformers.Count * 96 + 256);
        builder.AppendLine("public partial class Program")
            .AppendLine("{")
            .AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]")
            .AppendLine("    internal static void __NetPadInitializeDumpTransformers()")
            .AppendLine("    {")
            .AppendLine("        global::NetPad.Presentation.DumpTransformer.Clear();");
        foreach (var transformer in ToDumpTransformers)
        {
            builder.Append("        global::NetPad.Presentation.DumpTransformer.Register(")
                .Append(transformer)
                .AppendLine(");");
        }

        builder.AppendLine("    }")
            .AppendLine("}");
        return builder.ToString();
    }

    private string GetEntryPointCompilationCode(string? sourcePath, string entryPoint)
    {
        if (entryPoint.Length != 5 || !entryPoint.StartsWith("Main", StringComparison.Ordinal)
            || entryPoint[4] is < '1' or > '9')
        {
            return "#error Unsupported entry point. Expected Main1 through Main9.";
        }

        var root = CSharpSyntaxTree.ParseText(Code).GetCompilationUnitRoot();
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.ValueText == entryPoint
                                      && method.Modifiers.Any(SyntaxKind.StaticKeyword)
                                      && method.TypeParameterList == null
                                      && method.ParameterList.Parameters.Count == 0);

        var containingTypes = method?.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().ToArray() ?? [];
        if (method == null || containingTypes.Length == 0
            || containingTypes.Any(type => type is TypeDeclarationSyntax declaration
                                           && declaration.TypeParameterList?.Parameters.Count > 0))
        {
            return $"#error No parameterless static entry point named {entryPoint} was found.";
        }

        var namespaceNames = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(@namespace => @namespace.Name.ToString());
        var typeNames = containingTypes.Select(type => type.Identifier.ValueText);
        var qualifiedTypeName = "global::" + string.Join('.', namespaceNames.Concat(typeNames));
        var returnType = method.ReturnType.ToString().Replace("global::", "", StringComparison.Ordinal);
        var awaitsResult = returnType is "Task" or "System.Threading.Tasks.Task"
                           || returnType.StartsWith("Task<", StringComparison.Ordinal)
                           || returnType.StartsWith("System.Threading.Tasks.Task<", StringComparison.Ordinal);
        var invocation = $"{(awaitsResult ? "await " : "")}{qualifiedTypeName}.{entryPoint}();";

        var prefix = string.Concat(root.Externs.Select(node => node.ToFullString()))
                     + string.Concat(root.Usings.Select(node => node.ToFullString()))
                     + string.Concat(root.AttributeLists.Select(node => node.ToFullString()));
        var members = root.Members
            .Where(member => member is not GlobalStatementSyntax)
            .Select(member => member is FileScopedNamespaceDeclarationSyntax fileScopedNamespace
                ? SyntaxFactory.NamespaceDeclaration(fileScopedNamespace.Name)
                    .WithExterns(fileScopedNamespace.Externs)
                    .WithUsings(fileScopedNamespace.Usings)
                    .WithMembers(fileScopedNamespace.Members)
                    .WithLeadingTrivia(fileScopedNamespace.GetLeadingTrivia())
                    .WithTrailingTrivia(fileScopedNamespace.GetTrailingTrivia())
                    .ToFullString()
                : member.ToFullString());

        var builder = new StringBuilder(Code.Length + 128);
        builder.Append(prefix)
            .AppendLine()
            .AppendLine(invocation);
        AppendLineDirective(builder, 1, sourcePath);
        builder.Append(string.Join(Environment.NewLine, members));
        return builder.ToString();
    }

    private static void AppendLineDirective(StringBuilder builder, int lineNumber, string? sourcePath)
    {
        builder.Append("#line ").Append(lineNumber);
        if (sourcePath != null)
        {
            builder.Append(" \"")
                .Append(sourcePath.Replace("\\", "\\\\").Replace("\"", "\\\""))
                .Append('\"');
        }

        builder.AppendLine();
    }
}

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

        var rootCode = ConvertFileScopedNamespace(code);
        foreach (var @using in ExtractUsings(ref rootCode))
        {
            AddUsing(state, @using);
        }
        AppendSource(rootCode, normalizedSourcePath, 1, output, state, activePaths);
        var expandedCode = ReorderCompilationUnitMembers(output.ToString());

        var hashInput = string.Join("\n", state.LoadedFiles.Select(x => $"{x.Path}\0{x.Hash}")
            .Concat(state.Errors));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();

        return new LoadDirectiveResult(
            expandedCode,
            hash,
            state.LoadedPaths,
            state.AdditionalUsings,
            state.AdditionalReferences,
            state.ToDumpTransformers);
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
                    AddUsing(state, @using);
                }

                foreach (var reference in loadedSource.References)
                {
                    if (state.AdditionalReferences.Any(existing => existing.Equals(reference)))
                    {
                        continue;
                    }

                    state.AdditionalReferences.Add(reference);
                }

                state.ToDumpTransformers.AddRange(loadedSource.ToDumpTransformers);

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
        var references = new List<Reference>();

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

                var referenceArray = GetProperty(config, "References");
                if (referenceArray?.ValueKind == JsonValueKind.Array)
                {
                    references.AddRange(System.Text.Json.JsonSerializer.Deserialize<Reference[]>(referenceArray.Value.GetRawText()) ?? []);
                }
            }

            code = string.Join("\n", lines.Skip(codeMarkerIndex + 1));
            firstCodeLine = codeMarkerIndex + 2;
        }

        code = ConvertFileScopedNamespace(code);
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var toDumpTransformers = GetToDumpTransformers(syntaxTree.GetRoot());
        usings.AddRange(ExtractUsings(ref code));

        return new LoadedSource(code, firstCodeLine, usings, references, toDumpTransformers);
    }

    private static IReadOnlyList<string> ExtractUsings(ref string code)
    {
        var usingDirectives = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot().Usings;
        if (usingDirectives.Count == 0)
        {
            return [];
        }

        var chars = code.ToCharArray();
        var usings = new List<string>(usingDirectives.Count);
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
        return usings;
    }

    private static void AddUsing(ProcessingState state, string @using)
    {
        if (state.AdditionalUsingSet.Add(@using))
        {
            state.AdditionalUsings.Add(@using);
        }
    }

    private static string ReorderCompilationUnitMembers(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
        var globalStatements = root.Members.Where(member => member is GlobalStatementSyntax).ToArray();
        if (globalStatements.Length == 0)
        {
            return code;
        }

        var declarations = root.Members
            .Where(member => member is not GlobalStatementSyntax)
            .Select(member => member is FileScopedNamespaceDeclarationSyntax fileScopedNamespace
                ? SyntaxFactory.NamespaceDeclaration(fileScopedNamespace.Name)
                    .WithExterns(fileScopedNamespace.Externs)
                    .WithUsings(fileScopedNamespace.Usings)
                    .WithMembers(fileScopedNamespace.Members)
                    .WithLeadingTrivia(fileScopedNamespace.GetLeadingTrivia())
                    .WithTrailingTrivia(fileScopedNamespace.GetTrailingTrivia())
                : member);

        return root.WithMembers(SyntaxFactory.List(globalStatements.Concat(declarations))).ToFullString();
    }

    private static string ConvertFileScopedNamespace(string code)
    {
        var fileScopedNamespace = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot().Members
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScopedNamespace == null)
        {
            return code;
        }

        var semicolonIndex = fileScopedNamespace.SemicolonToken.SpanStart;
        return code[..semicolonIndex] + " {" + code[(semicolonIndex + 1)..] + Environment.NewLine + "}";
    }

    private static IReadOnlyList<string> GetToDumpTransformers(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "ToDump"
                             && method.Modifiers.Any(SyntaxKind.StaticKeyword)
                             && method.TypeParameterList == null
                             && method.ParameterList.Parameters.Count == 1
                             && IsObjectType(method.ReturnType)
                             && IsObjectType(method.ParameterList.Parameters[0].Type)
                             && !method.Modifiers.Any(SyntaxKind.PrivateKeyword)
                             && !method.Modifiers.Any(SyntaxKind.ProtectedKeyword))
            .Select(GetQualifiedMethodName)
            .Where(name => name != null)
            .Cast<string>()
            .ToArray();

        static bool IsObjectType(TypeSyntax? type)
        {
            return type?.ToString() is "object" or "object?" or "global::System.Object" or "global::System.Object?";
        }

        static string? GetQualifiedMethodName(MethodDeclarationSyntax method)
        {
            var containingTypes = method.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().ToArray();
            if (containingTypes.Length == 0 || containingTypes.Any(type =>
                    type is TypeDeclarationSyntax declaration && declaration.TypeParameterList?.Parameters.Count > 0))
            {
                return null;
            }

            var namespaces = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(@namespace => @namespace.Name.ToString());
            var types = containingTypes.Select(type => type.Identifier.ValueText);
            return "global::" + string.Join('.', namespaces.Concat(types)) + "." + method.Identifier.ValueText;
        }
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
        public List<Reference> AdditionalReferences { get; } = [];
        public List<string> ToDumpTransformers { get; } = [];
        public List<string> Errors { get; } = [];
    }

    private sealed record LoadedSource(
        string Code,
        int FirstCodeLine,
        IReadOnlyList<string> Usings,
        IReadOnlyList<Reference> References,
        IReadOnlyList<string> ToDumpTransformers);
}
