using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NetPad.CodeAnalysis;
using NetPad.Compilation;
using NetPad.Compilation.Directives;
using NetPad.Compilation.Scripts.Dependencies;
using NetPad.Configuration;
using NetPad.DotNet;
using NetPad.DotNet.CodeAnalysis;
using NetPad.DotNet.References;
using NetPad.IO;
using NetPad.Scripts;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using RoslynSpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace NetPad.Services;

public sealed record CodeDecompilationResponse(
    string Language,
    string Content,
    string AssemblyIdentity,
    string SymbolIdentity,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    bool IsSource = false,
    bool IsCurrentScriptSource = false,
    string? SourcePath = null);

public sealed record CodeDecompilationRequest(int Line, int Column);

internal sealed class AssemblyDecompilerCache
{
    private readonly object _lock = new();
    private readonly string _cacheDirectoryPath;

    private sealed class CacheMetadata
    {
        public CacheMetadata() { }

        public string AssemblyIdentity { get; set; } = string.Empty;
        public string AssemblyPath { get; set; } = string.Empty;
        public long LastWriteTimeUtcTicks { get; set; }
        public long Length { get; set; }
    }

    internal AssemblyDecompilerCache(string? cacheDirectoryPath = null)
    {
        _cacheDirectoryPath = cacheDirectoryPath ?? AppDataProvider.AppDataDirectoryPath
            .Combine("Cache", "DecompiledSources").Path;
    }

    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return Directory.Exists(_cacheDirectoryPath)
                    ? Directory.EnumerateDirectories(_cacheDirectoryPath)
                        .Count(directory => File.Exists(Path.Combine(directory, "metadata.json")))
                    : 0;
            }
        }
    }

    internal string GetOrAdd(string identity, string path, string typeName, Func<string> factory)
    {
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The referenced assembly no longer exists.", fullPath);
        }

        var metadata = new CacheMetadata
        {
            AssemblyIdentity = identity,
            AssemblyPath = fullPath,
            LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks,
            Length = file.Length
        };
        var cacheKey = Hash(identity + "\0" + fullPath);
        var assemblyCacheDirectoryPath = Path.Combine(_cacheDirectoryPath, cacheKey);

        lock (_lock)
        {
            Directory.CreateDirectory(_cacheDirectoryPath);
            EvictStaleEntries(metadata);

            if (Directory.Exists(assemblyCacheDirectoryPath))
            {
                var existing = ReadMetadata(assemblyCacheDirectoryPath);
                if (!MetadataMatches(existing, metadata))
                {
                    Directory.Delete(assemblyCacheDirectoryPath, recursive: true);
                }
            }

            Directory.CreateDirectory(assemblyCacheDirectoryPath);
            var metadataPath = Path.Combine(assemblyCacheDirectoryPath, "metadata.json");
            if (!File.Exists(metadataPath))
            {
                WriteAtomically(metadataPath, JsonSerializer.Serialize(metadata));
            }

            var typeSourcePath = Path.Combine(assemblyCacheDirectoryPath, Hash(typeName) + ".cs");
            if (File.Exists(typeSourcePath))
            {
                return File.ReadAllText(typeSourcePath);
            }

            var source = factory();
            WriteAtomically(typeSourcePath, source);
            return source;
        }
    }

    private void EvictStaleEntries(CacheMetadata current)
    {
        foreach (var directory in Directory.EnumerateDirectories(_cacheDirectoryPath))
        {
            var existing = ReadMetadata(directory);
            if (existing == null)
            {
                continue;
            }

            var sameIdentity = string.Equals(existing.AssemblyIdentity, current.AssemblyIdentity, StringComparison.Ordinal);
            var samePath = string.Equals(existing.AssemblyPath, current.AssemblyPath, StringComparison.Ordinal);
            var stale = (sameIdentity && !samePath)
                || (samePath && !sameIdentity)
                || (sameIdentity && samePath && !MetadataMatches(existing, current));

            if (stale)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static CacheMetadata? ReadMetadata(string directory)
    {
        try
        {
            var path = Path.Combine(directory, "metadata.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<CacheMetadata>(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool MetadataMatches(CacheMetadata? left, CacheMetadata right)
    {
        return left != null
            && string.Equals(left.AssemblyIdentity, right.AssemblyIdentity, StringComparison.Ordinal)
            && string.Equals(left.AssemblyPath, right.AssemblyPath, StringComparison.Ordinal)
            && left.LastWriteTimeUtcTicks == right.LastWriteTimeUtcTicks
            && left.Length == right.Length;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void WriteAtomically(string path, string content)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

public sealed class CodeDecompilationService : IDisposable
{
    private readonly IScriptDependencyResolver _dependencyResolver;
    private readonly ICodeParser _codeParser;
    private readonly ICodeAnalysisService _codeAnalysisService;
    private readonly IDotNetInfo _dotNetInfo;
    private readonly ILogger<CodeDecompilationService> _logger;
    private readonly AssemblyDecompilerCache _decompilerCache = new();
    private readonly string _imageReferenceDirectory = Path.Combine(Path.GetTempPath(), $"netpad-decompilation-{Guid.NewGuid():N}");
    private readonly object _imageReferenceLock = new();

    public CodeDecompilationService(
        IScriptDependencyResolver dependencyResolver,
        ICodeParser codeParser,
        ICodeAnalysisService codeAnalysisService,
        IDotNetInfo dotNetInfo,
        ILogger<CodeDecompilationService> logger)
    {
        _dependencyResolver = dependencyResolver;
        _codeParser = codeParser;
        _codeAnalysisService = codeAnalysisService;
        _dotNetInfo = dotNetInfo;
        _logger = logger;
    }

    public async Task<CodeDecompilationResponse?> DecompileAtPositionAsync(
        Script script,
        CodeDecompilationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Line < 1 || request.Column < 1 || string.IsNullOrWhiteSpace(script.Code))
        {
            return null;
        }

        var loadedSources = LoadDirectiveProcessor.Process(script.Code, script.Path);
        var trustedSourcePaths = loadedSources.LoadedPaths.ToList();
        if (!string.IsNullOrWhiteSpace(script.Path))
        {
            trustedSourcePaths.Add(script.Path);
        }

        var dependencies = await _dependencyResolver.GetDependenciesAsync(script, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var parsingResult = _codeParser.Parse(
            script,
            loadedSources.Code,
            new CodeParsingOptions
            {
                IncludeAspNetUsings = script.Config.UseAspNet,
                AdditionalCode = new SourceCodeCollection(dependencies.Code.SelectMany(item => item.Code)),
                AdditionalUsings = loadedSources.AdditionalUsings.ToArray()
            });
        parsingResult.BootstrapperProgram.Code.Update(parsingResult.BootstrapperProgram.Code.Value?
            .Replace("SCRIPT_ID", script.Id.ToString())
            .Replace("SCRIPT_NAME", script.Name)
            .Replace("SCRIPT_LOCATION", script.Path));

        var source = parsingResult.GetFullProgram().ToCodeString();
        var syntaxTree = _codeAnalysisService.GetSyntaxTree(
            source,
            script.Config.TargetFrameworkVersion,
            script.Config.OptimizationLevel,
            cancellationToken);
        var references = await CreateMetadataReferencesAsync(script, dependencies, cancellationToken);
        var compilation = CSharpCompilation.Create(
            "NetPadScript",
            [syntaxTree],
            references.Select(reference => reference.Reference),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                .WithOptimizationLevel(script.Config.OptimizationLevel)
                .WithOverflowChecks(script.Config.CheckedArithmetic)
                .WithAllowUnsafe(true));

        if (compilation.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var symbol = ResolveSymbolAtPosition(
            syntaxTree,
            semanticModel,
            request.Line,
            request.Column,
            parsingResult.UserProgramStartLineNumber,
            script.Path,
            cancellationToken);
        if (symbol == null)
        {
            return null;
        }

        if (IsSourceSymbol(symbol))
        {
            return CreateSourceResponse(
                symbol,
                script.Code,
                script.Path,
                trustedSourcePaths,
                parsingResult.UserProgramStartLineNumber,
                cancellationToken);
        }

        var containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (containingType == null || IsSourceSymbol(containingType))
        {
            return null;
        }

        var path = ResolveMetadataAssemblyPath(compilation, containingType.ContainingAssembly, references);
        if (path == null)
        {
            return null;
        }

        var assemblyIdentity = containingType.ContainingAssembly.Identity.ToString();
        var metadataTypeName = GetMetadataTypeName(containingType.OriginalDefinition);
        try
        {
            var content = _decompilerCache.GetOrAdd(
                assemblyIdentity,
                path,
                metadataTypeName,
                () =>
                {
                    var decompiler = new CSharpDecompiler(path, new DecompilerSettings());
                    return decompiler.DecompileTypeAsString(new FullTypeName(metadataTypeName));
                });

            var symbolSpan = FindSymbolSpan(content, symbol, containingType);
            return new CodeDecompilationResponse(
                "csharp",
                content,
                assemblyIdentity,
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                symbolSpan.StartLine,
                symbolSpan.StartColumn,
                symbolSpan.EndLine,
                symbolSpan.EndColumn);
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or IOException)
        {
            _logger.LogWarning(exception, "Could not decompile {Symbol} from {Assembly}", symbol, assemblyIdentity);
            return null;
        }
    }

    internal static bool IsSourceSymbol(RoslynSymbol symbol) => symbol.Locations.Any(location => location.IsInSource);

    internal static CodeDecompilationResponse? CreateSourceResponse(
        RoslynSymbol symbol,
        string scriptCode,
        string? scriptPath,
        IReadOnlyCollection<string> trustedSourcePaths,
        int userProgramStartLineNumber,
        CancellationToken cancellationToken)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
        if (location?.SourceTree == null)
        {
            return null;
        }

        var mappedLineSpan = location.GetMappedLineSpan();
        var span = mappedLineSpan.Span;
        var mappedPath = mappedLineSpan.Path;
        var hasMappedPath = !string.IsNullOrWhiteSpace(mappedPath);
        if (hasMappedPath && !trustedSourcePaths.Any(path => PathsEqual(path, mappedPath)))
        {
            return null;
        }

        var originalSpan = location.GetLineSpan().Span;
        var hasLineMapping = span.Start.Line != originalSpan.Start.Line ||
                             span.Start.Character != originalSpan.Start.Character ||
                             span.End.Line != originalSpan.End.Line ||
                             span.End.Character != originalSpan.End.Character;
        var lineOffset = hasLineMapping ? 0 : userProgramStartLineNumber;
        var isCurrentScriptSource = hasMappedPath
            ? !string.IsNullOrWhiteSpace(scriptPath) && PathsEqual(mappedPath, scriptPath)
            : span.Start.Line - lineOffset >= 0 &&
              span.Start.Line - lineOffset < scriptCode.Count(character => character == '\n') + 1;

        int startLine;
        int startColumn;
        int endLine;
        int endColumn;
        string? sourcePath;
        string content;

        if (isCurrentScriptSource)
        {
            var sourceLineOffset = hasMappedPath ? 0 : lineOffset;
            startLine = span.Start.Line - sourceLineOffset + 1;
            startColumn = span.Start.Character + 1;
            endLine = span.End.Line - sourceLineOffset + 1;
            endColumn = span.End.Character + 1;
            sourcePath = scriptPath;
            content = string.Empty;
        }
        else
        {
            if (!hasMappedPath)
            {
                return null;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourcePath = Path.GetFullPath(mappedPath);
                content = File.ReadAllText(sourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }

            startLine = span.Start.Line + 1;
            startColumn = span.Start.Character + 1;
            endLine = span.End.Line + 1;
            endColumn = span.End.Character + 1;
        }

        return new CodeDecompilationResponse(
            "csharp",
            content,
            string.Empty,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            startLine,
            startColumn,
            endLine,
            endColumn,
            IsSource: true,
            IsCurrentScriptSource: isCurrentScriptSource,
            SourcePath: sourcePath);
    }

    internal static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return string.Equals(first, second, PlatformUtil.PathComparison);
        }
    }

    internal static string? ResolveMetadataAssemblyPath(
        CSharpCompilation compilation,
        IAssemblySymbol assembly,
        IReadOnlyList<ResolvedMetadataReference> references)
    {
        foreach (var reference in references)
        {
            if (SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference.Reference), assembly))
            {
                return reference.Path;
            }
        }

        return null;
    }

    internal static RoslynSymbol? ResolveSymbolAtPosition(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        int line,
        int column,
        int userProgramStartLineNumber,
        string? scriptPath,
        CancellationToken cancellationToken)
    {
        var lineIndex = line - 1;
        var columnIndex = column - 1;
        var root = syntaxTree.GetRoot(cancellationToken);
        var token = root.DescendantTokens()
            .Where(candidate => !candidate.IsMissing && IsTokenAtEditorPosition(
                candidate,
                lineIndex,
                columnIndex,
                userProgramStartLineNumber,
                scriptPath))
            .OrderBy(candidate => candidate.Span.Length)
            .FirstOrDefault();

        if (token.RawKind == 0)
        {
            return null;
        }

        foreach (var node in token.Parent?.AncestorsAndSelf() ?? [])
        {
            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken);
            if (symbolInfo.Symbol != null)
            {
                return symbolInfo.Symbol;
            }

            if (symbolInfo.CandidateSymbols.Length == 1)
            {
                return symbolInfo.CandidateSymbols[0];
            }

            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol != null)
            {
                return declaredSymbol;
            }
        }

        return null;
    }

    internal static string GetMetadataTypeName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";
        return namespaceName + string.Join("+", names);
    }

    private async Task<List<ResolvedMetadataReference>> CreateMetadataReferencesAsync(
        Script script,
        ScriptDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var root = _dotNetInfo.LocateDotNetRootDirectoryForFramework(script.Config.TargetFrameworkVersion)
                   ?? _dotNetInfo.LocateDotNetRootDirectoryOrThrow();
        var frameworkPaths = FrameworkAssemblies.GetAssemblyLocations(
            new DirectoryPath(root),
            script.Config.TargetFrameworkVersion,
            script.Config.UseAspNet);
        var filePaths = frameworkPaths
            .Concat(GetManagedDependencyPaths(dependencies))
            .Append(typeof(INetPadRuntimeLibMarker).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var result = new List<ResolvedMetadataReference>();
        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var decompilerPath = frameworkPaths.Contains(path)
                    ? FindRuntimeImplementationAssemblyPath(path, root, script.Config.TargetFrameworkVersion) ?? path
                    : path;
                result.Add(new ResolvedMetadataReference(MetadataReference.CreateFromFile(path), decompilerPath));
            }
        }

        var imageReferences = dependencies.References
            .Where(dependency => dependency.Dependant != Dependant.ScriptHost)
            .Select(dependency => dependency.Reference)
            .OfType<AssemblyImageReference>()
            .DistinctBy(reference => reference.AssemblyImage.AssemblyName.FullName);

        foreach (var imageReference in imageReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await MaterializeAssemblyImageAsync(imageReference.AssemblyImage, cancellationToken);
            result.Add(new ResolvedMetadataReference(MetadataReference.CreateFromFile(path), path));
        }

        return result;
    }

    private static string? FindRuntimeImplementationAssemblyPath(
        string referencePath,
        string dotNetRoot,
        DotNetFrameworkVersion targetFrameworkVersion)
    {
        var assemblyName = AssemblyName.GetAssemblyName(referencePath).Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        foreach (var framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        {
            var sharedDirectory = Path.Combine(dotNetRoot, "shared", framework);
            if (!Directory.Exists(sharedDirectory))
            {
                continue;
            }

            var runtimeDirectories = Directory.GetDirectories(sharedDirectory)
                .Select(path => new
                {
                    Path = path,
                    Version = Version.TryParse(Path.GetFileName(path).Split('-', 2)[0], out var version)
                        ? version
                        : null
                })
                .Where(item => item.Version?.Major == targetFrameworkVersion.GetMajorVersion())
                .OrderByDescending(item => item.Version)
                .Select(item => item.Path);

            foreach (var runtimeDirectory in runtimeDirectories)
            {
                var implementationPath = Path.Combine(runtimeDirectory, assemblyName + ".dll");
                if (File.Exists(implementationPath))
                {
                    return implementationPath;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetManagedDependencyPaths(ScriptDependencies dependencies)
    {
        return dependencies.References
            .Where(dependency => dependency.Dependant != Dependant.ScriptHost)
            .SelectMany(dependency => dependency.Assets)
            .DistinctBy(asset => asset.Path)
            .Where(asset => asset.IsManagedAssembly)
            .Select(asset => new { asset.Path, Name = AssemblyName.GetAssemblyName(asset.Path) })
            .GroupBy(item => item.Name.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Name.Version).Last().Path);
    }

    private Task<string> MaterializeAssemblyImageAsync(AssemblyImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_imageReferenceDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(image.Image));
        var fileName = $"{Path.GetFileNameWithoutExtension(image.ConstructAssemblyFileName())}-{hash}.dll";
        var path = Path.Combine(_imageReferenceDirectory, fileName);
        lock (_imageReferenceLock)
        {
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, image.Image);
            }
        }

        return Task.FromResult(path);
    }

    private static string? ResolveMetadataAssemblyPath(
        CSharpCompilation compilation,
        IAssemblySymbol assembly,
        List<ResolvedMetadataReference> references)
        => ResolveMetadataAssemblyPath(compilation, assembly, (IReadOnlyList<ResolvedMetadataReference>)references);

    private static bool IsTokenAtEditorPosition(
        SyntaxToken token,
        int editorLine,
        int editorColumn,
        int userProgramStartLineNumber,
        string? scriptPath)
    {
        var span = token.GetLocation().GetMappedLineSpan();
        var path = span.Path;
        int line;
        int column;
        int endLine;
        int endColumn;

        if (!string.IsNullOrWhiteSpace(scriptPath) && !string.IsNullOrWhiteSpace(path))
        {
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(scriptPath), PlatformUtil.PathComparison))
            {
                return false;
            }

            line = span.StartLinePosition.Line;
            column = span.StartLinePosition.Character;
            endLine = span.EndLinePosition.Line;
            endColumn = span.EndLinePosition.Character;
        }
        else if (string.IsNullOrWhiteSpace(path))
        {
            line = span.StartLinePosition.Line - userProgramStartLineNumber;
            column = span.StartLinePosition.Character;
            endLine = span.EndLinePosition.Line - userProgramStartLineNumber;
            endColumn = span.EndLinePosition.Character;
        }
        else
        {
            return false;
        }

        return (editorLine > line || editorLine == line && editorColumn >= column) &&
               (editorLine < endLine || editorLine == endLine && editorColumn <= endColumn);
    }

    private static (int StartLine, int StartColumn, int EndLine, int EndColumn) FindSymbolSpan(
        string content,
        RoslynSymbol symbol,
        INamedTypeSymbol containingType)
    {
        if (symbol is IParameterSymbol parameter)
        {
            return FindSymbolSpan(content, parameter.ContainingSymbol, containingType);
        }

        if (symbol is ITypeParameterSymbol typeParameter)
        {
            return FindSymbolSpan(content, typeParameter.ContainingSymbol, containingType);
        }

        var root = CSharpSyntaxTree.ParseText(content).GetCompilationUnitRoot();
        SyntaxNode? node = null;

        if (symbol is INamedTypeSymbol namedType)
        {
            var name = namedType.MetadataName.Split('`')[0];
            node = root.DescendantNodes().FirstOrDefault(candidate =>
                candidate is BaseTypeDeclarationSyntax type && type.Identifier.ValueText == name ||
                candidate is DelegateDeclarationSyntax @delegate && @delegate.Identifier.ValueText == name);
        }
        else if (symbol is IMethodSymbol method)
        {
            node = method.MethodKind switch
            {
                Microsoft.CodeAnalysis.MethodKind.Constructor or Microsoft.CodeAnalysis.MethodKind.StaticConstructor => root.DescendantNodes()
                    .OfType<ConstructorDeclarationSyntax>()
                    .Where(candidate => candidate.ParameterList.Parameters.Count == method.Parameters.Length &&
                                        candidate.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)) ==
                                        (method.MethodKind == Microsoft.CodeAnalysis.MethodKind.StaticConstructor))
                    .OrderByDescending(candidate => GetParameterMatchScore(candidate.ParameterList.Parameters, method.Parameters))
                    .FirstOrDefault(),
                _ => root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(candidate => candidate.Identifier.ValueText == method.Name &&
                                        candidate.ParameterList.Parameters.Count == method.Parameters.Length)
                    .OrderByDescending(candidate => GetParameterMatchScore(candidate.ParameterList.Parameters, method.Parameters))
                    .FirstOrDefault()
            };
        }
        else if (symbol is IPropertySymbol property)
        {
            node = root.DescendantNodes().FirstOrDefault(candidate =>
                candidate is PropertyDeclarationSyntax declaration && declaration.Identifier.ValueText == property.Name ||
                candidate is IndexerDeclarationSyntax indexer && property.IsIndexer &&
                indexer.ParameterList.Parameters.Count == property.Parameters.Length);
        }
        else if (symbol is IEventSymbol @event)
        {
            node = root.DescendantNodes().FirstOrDefault(candidate =>
                candidate is EventDeclarationSyntax eventDeclaration &&
                eventDeclaration.Identifier.ValueText == @event.Name ||
                candidate is EventFieldDeclarationSyntax fieldDeclaration &&
                fieldDeclaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == @event.Name));
        }
        else if (symbol is IFieldSymbol field)
        {
            node = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.ValueText == field.Name);
        }

        node ??= root.DescendantNodes().FirstOrDefault(candidate =>
            candidate is BaseTypeDeclarationSyntax type &&
            type.Identifier.ValueText == containingType.MetadataName.Split('`')[0]) ?? root;
        var span = node.GetLocation().GetLineSpan().Span;
        return (span.Start.Line + 1, span.Start.Character + 1, span.End.Line + 1, span.End.Character + 1);
    }

    private static int GetParameterMatchScore(
        SeparatedSyntaxList<ParameterSyntax> syntaxParameters,
        IReadOnlyList<IParameterSymbol> symbolParameters)
    {
        var score = 0;
        for (var index = 0; index < symbolParameters.Count; index++)
        {
            var syntaxParameter = syntaxParameters[index];
            var symbolParameter = symbolParameters[index];
            if (syntaxParameter.Identifier.ValueText == symbolParameter.Name)
            {
                score++;
            }

            if (syntaxParameter.Type != null && IsMatchingType(syntaxParameter.Type.ToString(), symbolParameter.Type))
            {
                score++;
            }
        }

        return score;
    }

    private static bool IsMatchingType(string syntaxType, ITypeSymbol symbolType)
    {
        var normalized = syntaxType.Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .TrimEnd('?');

        if (symbolType is IArrayTypeSymbol arrayType)
        {
            return normalized.EndsWith("[]", StringComparison.Ordinal) &&
                   IsMatchingType(normalized[..^2], arrayType.ElementType);
        }

        var specialTypeName = symbolType.SpecialType switch
        {
            RoslynSpecialType.System_Boolean => "bool",
            RoslynSpecialType.System_Byte => "byte",
            RoslynSpecialType.System_Char => "char",
            RoslynSpecialType.System_Decimal => "decimal",
            RoslynSpecialType.System_Double => "double",
            RoslynSpecialType.System_Int16 => "short",
            RoslynSpecialType.System_Int32 => "int",
            RoslynSpecialType.System_Int64 => "long",
            RoslynSpecialType.System_Object => "object",
            RoslynSpecialType.System_SByte => "sbyte",
            RoslynSpecialType.System_Single => "float",
            RoslynSpecialType.System_String => "string",
            RoslynSpecialType.System_UInt16 => "ushort",
            RoslynSpecialType.System_UInt32 => "uint",
            RoslynSpecialType.System_UInt64 => "ulong",
            _ => null
        };

        if (specialTypeName != null && normalized == specialTypeName)
        {
            return true;
        }

        var typeName = symbolType.Name;
        var genericStart = normalized.IndexOf('<');
        var syntaxName = genericStart < 0 ? normalized : normalized[..genericStart];
        syntaxName = syntaxName[(syntaxName.LastIndexOf('.') + 1)..];
        return syntaxName == typeName || syntaxName == symbolType.MetadataName;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_imageReferenceDirectory))
            {
                Directory.Delete(_imageReferenceDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not remove temporary decompiler reference assemblies");
        }
    }

    internal sealed record ResolvedMetadataReference(MetadataReference Reference, string Path);
}
