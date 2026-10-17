using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NetPad.Compilation;
using NetPad.Compilation.Scripts;
using NetPad.Compilation.Scripts.Dependencies;
using NetPad.DotNet;
using NetPad.DotNet.References;
using NetPad.Scripts;

namespace NetPad.Services;

public sealed record CodePaneContent(string Language, string Content, string? Error = null, bool Supported = true);

public sealed record NativeDisassemblyResult(
    bool Supported,
    string? Content,
    string? Error = null,
    int StatusCode = StatusCodes.Status200OK);

internal sealed record GeneratedDisassemblyHelper(string ProjectFile, string ProgramFile);

internal sealed record DisassemblyHelperConfiguration(
    DotNetFrameworkVersion TargetFrameworkVersion,
    bool UseAspNet,
    IReadOnlyCollection<string> ReferencePaths);

internal static class DisassemblyHelperGenerator
{
    internal static GeneratedDisassemblyHelper Generate(DisassemblyHelperConfiguration configuration)
    {
        var project = new XElement("Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", configuration.TargetFrameworkVersion.GetTargetFrameworkMoniker()),
                new XElement("ImplicitUsings", "enable"),
                new XElement("Nullable", "enable")));

        foreach (var path in configuration.ReferencePaths.Distinct(StringComparer.Ordinal))
        {
            project.Add(new XElement("ItemGroup",
                new XElement("Reference",
                    new XAttribute("Include", Path.GetFileNameWithoutExtension(path)),
                    new XElement("HintPath", path),
                    new XElement("Private", "true"))));
        }

        if (configuration.UseAspNet)
        {
            project.Add(new XElement("ItemGroup",
                new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App"))));
        }

        const string program = """
            using System.Reflection;
            using System.Runtime.CompilerServices;
            using System.Runtime.Loader;

            if (args.Length != 1)
            {
                Console.Error.WriteLine("Expected the compiled script assembly path.");
                return 2;
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }

            var prepared = 0;
            foreach (var type in types)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var methods = type.GetMethods(flags).Cast<MethodBase>()
                    .Concat(type.GetConstructors(flags));

                foreach (var method in methods)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                        prepared++;
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"Could not prepare {method}: {exception.Message}");
                    }
                }
            }

            Console.Error.WriteLine($"Prepared {prepared} methods without invoking them.");
            return 0;
            """;

        return new GeneratedDisassemblyHelper(
            new XDocument(new XDeclaration("1.0", "utf-8", null), project).ToString(),
            program);
    }
}

internal sealed class DisassemblyWorkspace : IDisposable
{
    internal DisassemblyWorkspace()
    {
        Path = Directory.CreateTempSubdirectory("netpad-disassembly-").FullName;
    }

    internal string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

public sealed class NativeDisassemblyService(
    IDotNetInfo dotNetInfo,
    IScriptCompiler scriptCompiler,
    IScriptDependencyResolver dependencyResolver,
    ILogger<NativeDisassemblyService> logger)
{
    internal static bool IsSupportedPlatform(OSPlatform platform)
        => platform == OSPlatform.Windows || platform == OSPlatform.Linux || platform == OSPlatform.OSX;

    internal static bool IsSupportedJitArchitecture(Architecture architecture)
        => architecture is Architecture.X86 or Architecture.X64 or Architecture.Arm or Architecture.Arm64;

    public async Task<NativeDisassemblyResult> DisassembleAsync(Script script, CancellationToken cancellationToken)
    {
        if (!IsSupportedPlatform(GetCurrentPlatform()))
        {
            return new NativeDisassemblyResult(false, null,
                "Native JIT disassembly is supported only on Windows, Linux, and macOS.",
                StatusCodes.Status501NotImplemented);
        }

        if (!IsSupportedJitArchitecture(RuntimeInformation.ProcessArchitecture))
        {
            return new NativeDisassemblyResult(false, null,
                $"Native JIT disassembly is not supported on {RuntimeInformation.ProcessArchitecture}.",
                StatusCodes.Status501NotImplemented);
        }

        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            return new NativeDisassemblyResult(false, null,
                "The current runtime does not support JIT compilation.",
                StatusCodes.Status501NotImplemented);
        }

        try
        {
            return await DisassembleSupportedScriptAsync(script, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Native disassembly failed for script {ScriptId}", script.Id);
            return new NativeDisassemblyResult(true, null, "Native JIT disassembly failed.", StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<NativeDisassemblyResult> DisassembleSupportedScriptAsync(
        Script script,
        CancellationToken cancellationToken)
    {
        var compileResult = await scriptCompiler.ParseAndCompileAsync(script.Code, script, cancellationToken);
        if (compileResult == null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new NativeDisassemblyResult(true, null, "Script compilation was cancelled or failed.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var (parsingResult, compilationResult) = compileResult;
        if (!compilationResult.Success)
        {
            var errors = compilationResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => DiagnosticsHelper.ReduceStacktraceLineNumbers(
                    diagnostic,
                    parsingResult.UserProgramStartLineNumber));
            return new NativeDisassemblyResult(true, null, string.Join(Environment.NewLine, errors),
                StatusCodes.Status422UnprocessableEntity);
        }

        if (HasModuleInitializer(compilationResult.AssemblyBytes))
        {
            return new NativeDisassemblyResult(false, null,
                "Native JIT disassembly is disabled for scripts that declare a module initializer.",
                StatusCodes.Status501NotImplemented);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var workspace = new DisassemblyWorkspace();
        var assemblyPath = System.IO.Path.Combine(workspace.Path, compilationResult.AssemblyFileName);
        await File.WriteAllBytesAsync(assemblyPath, compilationResult.AssemblyBytes, cancellationToken);

        var dependencies = await dependencyResolver.GetDependenciesAsync(script, cancellationToken);
        var referencesDirectory = System.IO.Path.Combine(workspace.Path, "references");
        Directory.CreateDirectory(referencesDirectory);
        var referencePaths = await WriteReferenceAssembliesAsync(
            dependencies,
            referencesDirectory,
            dependencyResolver.GetUserVisibleAssemblies(),
            cancellationToken);

        var helper = DisassemblyHelperGenerator.Generate(new DisassemblyHelperConfiguration(
            script.Config.TargetFrameworkVersion,
            script.Config.UseAspNet,
            referencePaths));
        var projectPath = System.IO.Path.Combine(workspace.Path, "DisassemblyHelper.csproj");
        await File.WriteAllTextAsync(projectPath, helper.ProjectFile, cancellationToken);
        await File.WriteAllTextAsync(System.IO.Path.Combine(workspace.Path, "Program.cs"), helper.ProgramFile,
            cancellationToken);

        var dotNetExecutable = dotNetInfo.LocateDotNetExecutableForFramework(script.Config.TargetFrameworkVersion)
                               ?? dotNetInfo.LocateDotNetExecutableOrThrow();

        var build = await RunProcessAsync(dotNetExecutable, workspace.Path,
            ["build", projectPath, "--configuration", "Release", "--nologo"],
            setJitDisassembly: false, cancellationToken);
        if (build.ExitCode != 0)
        {
            logger.LogWarning("Disassembly helper build failed: {Output}", build.Output);
            return new NativeDisassemblyResult(true, null, "The isolated disassembly helper could not be built.",
                StatusCodes.Status500InternalServerError);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var helperAssemblyPath = System.IO.Path.Combine(workspace.Path, "bin", "Release",
            script.Config.TargetFrameworkVersion.GetTargetFrameworkMoniker(), "DisassemblyHelper.dll");
        var run = await RunProcessAsync(dotNetExecutable, workspace.Path,
            [helperAssemblyPath, assemblyPath],
            setJitDisassembly: true, cancellationToken);
        if (run.ExitCode != 0)
        {
            logger.LogWarning("Disassembly helper failed: {Output}", run.Output);
            return new NativeDisassemblyResult(true, null, "The isolated disassembly helper failed.",
                StatusCodes.Status500InternalServerError);
        }

        return new NativeDisassemblyResult(true, run.Output);
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        return OSPlatform.Create(RuntimeInformation.OSDescription);
    }

    private static async Task<string[]> WriteReferenceAssembliesAsync(
        ScriptDependencies dependencies,
        string referencesDirectory,
        IEnumerable<string> userVisibleAssemblies,
        CancellationToken cancellationToken)
    {
        var imagePaths = new List<string>();
        foreach (var dependency in dependencies.References.Where(dependency => dependency.Dependant != Dependant.ScriptHost))
        {
            if (dependency.Reference is not AssemblyImageReference imageReference)
            {
                continue;
            }

            var image = imageReference.AssemblyImage;
            var hash = Convert.ToHexString(SHA256.HashData(image.Image));
            var fileName = System.IO.Path.GetFileNameWithoutExtension(image.ConstructAssemblyFileName());
            var path = System.IO.Path.Combine(referencesDirectory, $"{fileName}-{hash}.dll");
            await File.WriteAllBytesAsync(path, imageReference.AssemblyImage.Image, cancellationToken);
            imagePaths.Add(path);
        }

        var filePaths = dependencies.References
            .Where(dependency => dependency.Dependant != Dependant.ScriptHost)
            .SelectMany(dependency => dependency.Assets)
            .Where(asset => asset.IsManagedAssembly)
            .Select(asset => asset.Path)
            .Concat(imagePaths)
            .Concat(userVisibleAssemblies)
            .Append(typeof(INetPadRuntimeLibMarker).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Where(File.Exists)
            .Select(path => new { Path = path, Name = System.Reflection.AssemblyName.GetAssemblyName(path) })
            .GroupBy(item => item.Name.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Name.Version).Last().Path)
            .ToArray();

        return filePaths;
    }

    internal static async Task<ProcessResult> RunProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        bool setJitDisassembly,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (setJitDisassembly)
        {
            startInfo.Environment["COMPlus_JitDisasm"] = "*";
            startInfo.Environment["COMPlus_TieredCompilation"] = "0";
            startInfo.Environment["COMPlus_ReadyToRun"] = "0";
            startInfo.Environment["DOTNET_JitDisasm"] = "*";
            startInfo.Environment["DOTNET_TieredCompilation"] = "0";
            startInfo.Environment["DOTNET_ReadyToRun"] = "0";
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the isolated disassembly process.");
        }

        using var cancellationRegistration = cancellationToken.Register(() => KillProcessTree(process));
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var output = await Task.WhenAll(standardOutput, standardError);
        return new ProcessResult(process.ExitCode, string.Join(Environment.NewLine, output.Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    internal static bool HasModuleInitializer(byte[] assemblyBytes)
    {
        using var stream = new MemoryStream(assemblyBytes, writable: false);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return false;
        }

        var metadata = peReader.GetMetadataReader();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Name) != "<Module>")
            {
                continue;
            }

            return type.GetMethods().Any(methodHandle =>
                metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name) == ".cctor");
        }

        return false;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    internal sealed record ProcessResult(int ExitCode, string Output);
}
