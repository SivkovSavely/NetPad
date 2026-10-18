using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using NetPad.CodeAnalysis;
using NetPad.DotNet;

namespace NetPad.Compilation.CSharp;

/// <summary>
/// An implementation of <see cref="ICodeCompiler"/> that compiles C#.NET code.
/// </summary>
public class CSharpCodeCompiler(IDotNetInfo dotNetInfo, ICodeAnalysisService codeAnalysisService)
    : ICodeCompiler
{
    private static readonly ConcurrentDictionary<(string Path, long LastWriteTicks), PortableExecutableReference>
        _metadataReferenceCache = new();

    public CompilationResult Compile(CompilationInput input)
    {
        string assemblyName = !string.IsNullOrWhiteSpace(input.AssemblyName) ? input.AssemblyName : "NetPadScript";
        string assemblyFileExtension = GetCompiledAssemblyFileExtension(input.OutputKind);
        string assemblyFileName = $"{assemblyName}{assemblyFileExtension}";

        var (compilation, generatorDiagnostics) = CreateCompilation(input, assemblyName);

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));

        stream.Seek(0, SeekOrigin.Begin);
        var assemblyBytes = stream.ToArray();

        return new CompilationResult(
            result.Success && !generatorDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            new AssemblyName(assemblyName),
            assemblyFileName,
            assemblyBytes,
            generatorDiagnostics.AddRange(result.Diagnostics));
    }

    private (CSharpCompilation Compilation, ImmutableArray<Diagnostic> GeneratorDiagnostics) CreateCompilation(CompilationInput input, string assemblyName)
    {
        SyntaxTree syntaxTree = codeAnalysisService.GetSyntaxTree(
            input.Code,
            input.TargetFrameworkVersion,
            input.OptimizationLevel,
            context: input.CompilationContext);

        // Build references to all assemblies we need to include in the compilation, starting with the assemblies
        // of the selected .NET SDK.
        var dotNetRoot = dotNetInfo.LocateDotNetRootDirectoryForFramework(input.TargetFrameworkVersion)
                         ?? dotNetInfo.LocateDotNetRootDirectoryOrThrow();
        var assemblyLocations = FrameworkAssemblies.GetAssemblyLocations(
            dotNetRoot,
            input.TargetFrameworkVersion,
            input.UseAspNet);

        // Add assemblies coming from input
        foreach (var assemblyReferenceLocation in input.AssemblyFileReferences)
        {
            assemblyLocations.Add(assemblyReferenceLocation);
        }

        // Add a reference to the NetPad runtime
        assemblyLocations.Add(typeof(INetPadRuntimeLibMarker).Assembly.Location);

        // Convert locations to metadata references
        var references = BuildMetadataReferences(input.AssemblyImageReferences, assemblyLocations);

        var compilationOptions = new CSharpCompilationOptions(input.OutputKind)
            .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
            .WithOptimizationLevel(input.OptimizationLevel)
            .WithOverflowChecks(input.OverflowChecks)
            .WithAllowUnsafe(true);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [syntaxTree],
            options: compilationOptions,
            references: references
        );

        // Run the built-in source generators
        var generators = GetSourceGenerators(assemblyLocations.Concat(input.AnalyzerFileReferences), out var discoveryDiagnostics);
        var driver = CSharpGeneratorDriver.Create(
            generators: generators,
            additionalTexts: null,
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options,
            optionsProvider: null);

        try
        {
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var sourceGenCompilation, out var generatorDiagnostics);
            return ((CSharpCompilation)sourceGenCompilation, discoveryDiagnostics.AddRange(generatorDiagnostics));
        }
        catch (Exception exception)
        {
            var descriptor = new DiagnosticDescriptor(
                "NPADGENRUN",
                "Source generator execution failure",
                "Source generator execution failed: {0}",
                "Compilation",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);
            var error = Diagnostic.Create(descriptor, Location.None, exception.Message);
            return (compilation, discoveryDiagnostics.Add(error));
        }
    }

    private static PortableExecutableReference[] BuildMetadataReferences(
        IEnumerable<byte[]> assemblyImages,
        HashSet<string> assemblyLocations)
    {
        return assemblyImages
            .Select(i => MetadataReference.CreateFromImage(i))
            .Union(assemblyLocations.Select(loc => _metadataReferenceCache.GetOrAdd(
                (loc, File.GetLastWriteTimeUtc(loc).Ticks),
                key => MetadataReference.CreateFromFile(key.Path))
            ))
            .ToArray();
    }

    private static ISourceGenerator[] GetSourceGenerators(IEnumerable<string> assemblyLocations, out ImmutableArray<Diagnostic> diagnostics)
    {
        var assemblyLoader = new FromAssemblyLoader();
        var generators = new List<ISourceGenerator>();
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        var descriptor = new DiagnosticDescriptor(
            "NPADGENLOAD",
            "Source generator load failure",
            "Failed to load source generators from '{0}': {1}",
            "Compilation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        foreach (var path in assemblyLocations.Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(path))
            {
                errors.Add(Diagnostic.Create(descriptor, Location.None, path, "Analyzer assembly file does not exist."));
                continue;
            }

            try
            {
                generators.AddRange(new AnalyzerFileReference(path, assemblyLoader).GetGenerators("C#"));
            }
            catch (Exception exception)
            {
                errors.Add(Diagnostic.Create(descriptor, Location.None, path, exception.Message));
            }
        }

        diagnostics = errors.ToImmutable();
        return generators.ToArray();
    }

    private static string GetCompiledAssemblyFileExtension(OutputKind outputKind)
    {
        var executableExt = PlatformUtil.GetPlatformExecutableExtension();

        return outputKind switch
        {
            OutputKind.DynamicallyLinkedLibrary => ".dll",
            OutputKind.ConsoleApplication => executableExt,
            OutputKind.WindowsApplication => executableExt,
            OutputKind.WindowsRuntimeMetadata => executableExt,
            OutputKind.WindowsRuntimeApplication => ".winmdobj",
            OutputKind.NetModule => ".netmodule",
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind), outputKind, null)
        };
    }
}

file class FromAssemblyLoader : IAnalyzerAssemblyLoader
{
    private readonly ConcurrentDictionary<string, Assembly> _loaded = new();

    public void AddDependencyLocation(string fullPath)
    {
    }

    public Assembly LoadFromPath(string fullPath)
    {
        return _loaded.GetOrAdd(fullPath, Assembly.LoadFrom);
    }
}
