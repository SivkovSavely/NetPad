using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Collections.Immutable;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Moq;
using NetPad.Compilation;
using NetPad.Compilation.Scripts;
using NetPad.Compilation.Scripts.Dependencies;
using NetPad.DotNet;
using NetPad.DotNet.CodeAnalysis;
using NetPad.Scripts;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class CodeInspectionBackendTests
{
    [Fact]
    public void Disassembly_helper_is_platform_gated_and_never_invokes_script_methods()
    {
        Assert.True(NativeDisassemblyService.IsSupportedPlatform(OSPlatform.Windows));
        Assert.True(NativeDisassemblyService.IsSupportedPlatform(OSPlatform.Linux));
        Assert.True(NativeDisassemblyService.IsSupportedPlatform(OSPlatform.OSX));
        Assert.False(NativeDisassemblyService.IsSupportedPlatform(OSPlatform.Create("FREEBSD")));
        Assert.True(NativeDisassemblyService.IsSupportedJitArchitecture(Architecture.X64));
        Assert.True(NativeDisassemblyService.IsSupportedJitArchitecture(Architecture.Arm64));
        Assert.False(NativeDisassemblyService.IsSupportedJitArchitecture((Architecture)999));

        var helper = DisassemblyHelperGenerator.Generate(new DisassemblyHelperConfiguration(
            DotNetFrameworkVersion.DotNet8,
            UseAspNet: false,
            ["/tmp/Example & Tools.dll"]));
        var project = XDocument.Parse(helper.ProjectFile);

        Assert.Equal("net8.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("/tmp/Example & Tools.dll", project.Descendants("HintPath").Single().Value);
        Assert.Contains("RuntimeHelpers.PrepareMethod", helper.ProgramFile);
        Assert.DoesNotContain(".Invoke(", helper.ProgramFile);
    }

    [Fact]
    public void Disassembly_workspace_removes_temporary_helper_artifacts()
    {
        var workspace = new DisassemblyWorkspace();
        var path = workspace.Path;
        File.WriteAllText(Path.Combine(path, "helper.txt"), "temporary");

        workspace.Dispose();

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task Disassembly_process_runner_kills_the_process_when_cancelled()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var process = NativeDisassemblyService.RunProcessAsync(
            "/bin/sleep",
            Path.GetTempPath(),
            ["30"],
            setJitDisassembly: false,
            cancellation.Token);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await process.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Native_disassembly_prepares_methods_without_running_the_user_static_constructor()
    {
        if (!NativeDisassemblyService.IsSupportedPlatform(
                OperatingSystem.IsWindows() ? OSPlatform.Windows :
                OperatingSystem.IsLinux() ? OSPlatform.Linux :
                OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Create("unsupported")))
        {
            return;
        }

        const string sideEffectMarker = "USER_SCRIPT_EXECUTED";
        const string code = "public class Probe { static Probe() => System.Console.WriteLine(\"USER_SCRIPT_EXECUTED\"); public static void Work() { } }";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var roslynCompilation = CSharpCompilation.Create(
            "CompiledScript",
            [syntaxTree],
            trustedAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emitResult = roslynCompilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var compilationResult = new CompilationResult(
            true,
            new AssemblyName("CompiledScript"),
            "CompiledScript.dll",
            assemblyStream.ToArray(),
            ImmutableArray<Diagnostic>.Empty);
        var parseResult = new ParseAndCompileResult(
            new CodeParsingResult(new SourceCode(string.Empty), new SourceCode(string.Empty), null),
            compilationResult);
        var scriptCompiler = new Mock<IScriptCompiler>();
        scriptCompiler.Setup(compiler => compiler.ParseAndCompileAsync(
                It.IsAny<string>(), It.IsAny<Script>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parseResult);
        var dependencyResolver = new Mock<IScriptDependencyResolver>();
        dependencyResolver.Setup(resolver => resolver.GetDependenciesAsync(
                It.IsAny<Script>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScriptDependencies([], []));
        var dotNetInfo = new Mock<IDotNetInfo>();
        dotNetInfo.Setup(info => info.LocateDotNetExecutableForFramework(DotNetFrameworkVersion.DotNet9))
            .Returns("dotnet");
        var logger = new Mock<ILogger<NativeDisassemblyService>>();
        var service = new NativeDisassemblyService(
            dotNetInfo.Object,
            scriptCompiler.Object,
            dependencyResolver.Object,
            logger.Object);
        var script = new Script(
            Guid.NewGuid(),
            "DisassemblyTest",
            new ScriptConfig(ScriptKind.Program, DotNetFrameworkVersion.DotNet9),
            "// current editor source");
        var workspacesBefore = Directory.GetDirectories(Path.GetTempPath(), "netpad-disassembly-*").ToHashSet();

        var result = await service.DisassembleAsync(script, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains("Prepared ", result.Content);
        Assert.DoesNotContain(sideEffectMarker, result.Content);
        Assert.Equal(workspacesBefore.Order(), Directory.GetDirectories(Path.GetTempPath(), "netpad-disassembly-*").Order());
    }

    [Fact]
    public async Task Native_disassembly_rejects_module_initializers_before_loading_the_script()
    {
        if (!NativeDisassemblyService.IsSupportedPlatform(
                OperatingSystem.IsWindows() ? OSPlatform.Windows :
                OperatingSystem.IsLinux() ? OSPlatform.Linux :
                OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Create("unsupported")))
        {
            return;
        }

        var markerPath = Path.Combine(Path.GetTempPath(), $"netpad-module-init-{Guid.NewGuid():N}.txt");
        var markerLiteral = JsonSerializer.Serialize(markerPath);
        var code = $$"""
            using System.IO;
            using System.Runtime.CompilerServices;

            public static class ModuleInitializerProbe
            {
                [ModuleInitializer]
                internal static void Initialize() => File.WriteAllText({{markerLiteral}}, "executed");
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ModuleInitializerScript",
            [syntaxTree],
            trustedAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var compilationResult = new CompilationResult(
            true,
            new AssemblyName("ModuleInitializerScript"),
            "ModuleInitializerScript.dll",
            assemblyStream.ToArray(),
            ImmutableArray<Diagnostic>.Empty);
        var parseResult = new ParseAndCompileResult(
            new CodeParsingResult(new SourceCode(string.Empty), new SourceCode(string.Empty), null),
            compilationResult);
        var scriptCompiler = new Mock<IScriptCompiler>();
        scriptCompiler.Setup(compiler => compiler.ParseAndCompileAsync(
                It.IsAny<string>(), It.IsAny<Script>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parseResult);
        var service = new NativeDisassemblyService(
            Mock.Of<IDotNetInfo>(),
            scriptCompiler.Object,
            Mock.Of<IScriptDependencyResolver>(),
            Mock.Of<ILogger<NativeDisassemblyService>>());
        var script = new Script(
            Guid.NewGuid(),
            "ModuleInitializerTest",
            new ScriptConfig(ScriptKind.Program, DotNetFrameworkVersion.DotNet9),
            "// current editor source");

        var result = await service.DisassembleAsync(script, CancellationToken.None);

        Assert.False(result.Supported);
        Assert.Contains("module initializer", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Decompiler_cache_persists_sources_and_invalidates_identity_path_and_file_changes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpad-decompiler-cache-{Guid.NewGuid():N}");
        var cacheDirectory = Path.Combine(directory, "cache");
        Directory.CreateDirectory(directory);
        var firstPath = Path.Combine(directory, "first.dll");
        var secondPath = Path.Combine(directory, "second.dll");
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");

        try
        {
            var cache = new AssemblyDecompilerCache(cacheDirectory);
            var factoryCalls = 0;
            var first = cache.GetOrAdd("Example, Version=1", firstPath, "Example.Type",
                () => { factoryCalls++; return "first decompiled source"; });
            var same = cache.GetOrAdd("Example, Version=1", firstPath, "Example.Type",
                () => { factoryCalls++; return "unexpected source"; });
            Assert.Equal(first, same);
            Assert.Equal(1, factoryCalls);

            var reopenedCache = new AssemblyDecompilerCache(cacheDirectory);
            var persisted = reopenedCache.GetOrAdd("Example, Version=1", firstPath, "Example.Type",
                () => { factoryCalls++; return "unexpected source"; });
            Assert.Equal(first, persisted);
            Assert.Equal(1, factoryCalls);

            var changedIdentity = reopenedCache.GetOrAdd("Example, Version=2", firstPath, "Example.Type",
                () => { factoryCalls++; return "changed identity"; });
            Assert.NotEqual(first, changedIdentity);
            Assert.Equal(1, cache.Count);

            var changedPath = reopenedCache.GetOrAdd("Example, Version=2", secondPath, "Example.Type",
                () => { factoryCalls++; return "changed path"; });
            Assert.NotEqual(changedIdentity, changedPath);
            Assert.Equal(1, cache.Count);

            File.WriteAllText(secondPath, "updated assembly content");
            var changedFile = reopenedCache.GetOrAdd("Example, Version=2", secondPath, "Example.Type",
                () => { factoryCalls++; return "changed file"; });
            Assert.NotEqual(changedPath, changedFile);
            Assert.Equal(4, factoryCalls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Source_symbols_fall_back_while_metadata_symbols_resolve_to_server_paths()
    {
        const string source = "public class SourceType { public string Value => string.Empty; }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var metadataPath = typeof(object).Assembly.Location;
        var metadataReference = MetadataReference.CreateFromFile(metadataPath);
        var compilation = CSharpCompilation.Create(
            "SymbolFallbackTests",
            [tree],
            [metadataReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var sourceSymbol = CodeDecompilationService.ResolveSymbolAtPosition(
            tree,
            semanticModel,
            line: 1,
            column: source.IndexOf("SourceType", StringComparison.Ordinal) + 1,
            userProgramStartLineNumber: 0,
            scriptPath: null,
            CancellationToken.None)!;
        var metadataSymbol = CodeDecompilationService.ResolveSymbolAtPosition(
            tree,
            semanticModel,
            line: 1,
            column: source.IndexOf("Empty", StringComparison.Ordinal) + 1,
            userProgramStartLineNumber: 0,
            scriptPath: null,
            CancellationToken.None)!;

        Assert.True(CodeDecompilationService.IsSourceSymbol(sourceSymbol));
        Assert.False(CodeDecompilationService.IsSourceSymbol(metadataSymbol));
        Assert.Null(CodeDecompilationService.ResolveMetadataAssemblyPath(
            compilation,
            ((INamedTypeSymbol)sourceSymbol).ContainingAssembly,
            [new CodeDecompilationService.ResolvedMetadataReference(metadataReference, metadataPath)]));
        Assert.Equal(metadataPath, CodeDecompilationService.ResolveMetadataAssemblyPath(
            compilation,
            metadataSymbol.ContainingAssembly,
            [new CodeDecompilationService.ResolvedMetadataReference(metadataReference, metadataPath)]));
    }

    [Fact]
    public void Source_symbol_responses_distinguish_script_source_from_loaded_source_files()
    {
        const string scriptCode = "public class LocalType {}";
        var scriptTree = CSharpSyntaxTree.ParseText(scriptCode);
        var scriptCompilation = CSharpCompilation.Create("ScriptSource", [scriptTree]);
        var scriptSymbol = scriptCompilation.GetSemanticModel(scriptTree)
            .GetDeclaredSymbol(scriptTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!;

        var scriptResponse = CodeDecompilationService.CreateSourceResponse(
            scriptSymbol,
            scriptCode,
            scriptPath: null,
            trustedSourcePaths: [],
            userProgramStartLineNumber: 0,
            CancellationToken.None)!;

        Assert.True(scriptResponse.IsSource);
        Assert.True(scriptResponse.IsCurrentScriptSource);
        Assert.Null(scriptResponse.SourcePath);
        Assert.Equal(1, scriptResponse.StartLine);
        Assert.Equal(string.Empty, scriptResponse.Content);

        const string mappedScriptCode = "first line\nsecond line\npublic class MappedLocalType {}";
        var mappedTree = CSharpSyntaxTree.ParseText("#line 3\npublic class MappedLocalType {}\n");
        var mappedCompilation = CSharpCompilation.Create("MappedScriptSource", [mappedTree]);
        var mappedSymbol = mappedCompilation.GetSemanticModel(mappedTree)
            .GetDeclaredSymbol(mappedTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!;
        var mappedResponse = CodeDecompilationService.CreateSourceResponse(
            mappedSymbol,
            mappedScriptCode,
            scriptPath: null,
            trustedSourcePaths: [],
            userProgramStartLineNumber: 20,
            CancellationToken.None)!;

        Assert.True(mappedResponse.IsCurrentScriptSource);
        Assert.Equal(3, mappedResponse.StartLine);

        var directory = Path.Combine(Path.GetTempPath(), $"netpad-loaded-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var loadedPath = Path.Combine(directory, "Loaded.cs");
        const string loadedText = "// header\npublic class LoadedType {}\n";
        File.WriteAllText(loadedPath, loadedText);

        try
        {
            var loadedCode = $"#line 2 \"{loadedPath}\"\npublic class LoadedType {{}}\n#line default\n";
            var loadedTree = CSharpSyntaxTree.ParseText(loadedCode);
            var loadedCompilation = CSharpCompilation.Create("LoadedSource", [loadedTree]);
            var loadedSymbol = loadedCompilation.GetSemanticModel(loadedTree)
                .GetDeclaredSymbol(loadedTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!;

            var loadedResponse = CodeDecompilationService.CreateSourceResponse(
                loadedSymbol,
                scriptCode,
                scriptPath: null,
                trustedSourcePaths: [loadedPath],
                userProgramStartLineNumber: 0,
                CancellationToken.None)!;

            Assert.True(loadedResponse.IsSource);
            Assert.False(loadedResponse.IsCurrentScriptSource);
            Assert.Equal(loadedPath, loadedResponse.SourcePath);
            Assert.Equal(loadedText, loadedResponse.Content);
            Assert.Equal(2, loadedResponse.StartLine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Source_symbol_response_does_not_read_an_unloaded_line_directive_path()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpad-untrusted-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var secretPath = Path.Combine(directory, "secret.cs");
        File.WriteAllText(secretPath, "class Secret {}\n");

        try
        {
            var code = $"#line 1 \"{secretPath}\"\npublic class LocalType {{}}\n#line default\n";
            var tree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("UntrustedLineMapping", [tree]);
            var symbol = compilation.GetSemanticModel(tree)
                .GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!;

            var response = CodeDecompilationService.CreateSourceResponse(
                symbol,
                "public class LocalType {}",
                scriptPath: null,
                trustedSourcePaths: [],
                userProgramStartLineNumber: 0,
                CancellationToken.None);

            Assert.Null(response);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Source_path_allowlist_does_not_treat_case_distinct_paths_as_equal()
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NetPad", "Sources"));
        var loadedPath = Path.Combine(directory, "Loaded.cs");
        var mappedPath = Path.Combine(directory, "loaded.cs");

        Assert.False(CodeDecompilationService.PathsEqual(loadedPath, mappedPath));
    }
}
