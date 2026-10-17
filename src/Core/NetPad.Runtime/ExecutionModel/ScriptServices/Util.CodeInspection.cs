using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using NetPad.CodeAnalysis;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    /// <summary>Decompiles the specified type to C# source.</summary>
    public static string Decompile(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var path = GetAssemblyPath(type.Assembly);
        var decompiler = new CSharpDecompiler(path, new DecompilerSettings());
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return decompiler.DecompileTypeAsString(new FullTypeName(definition.FullName!));
    }

    /// <summary>Decompiles the specified method or constructor to C# source.</summary>
    public static string Decompile(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var path = GetAssemblyPath(method.Module.Assembly);
        var decompiler = new CSharpDecompiler(path, new DecompilerSettings());
        var handle = MetadataTokens.EntityHandle(method.MetadataToken);
        return decompiler.DecompileAsString(handle);
    }

    /// <summary>Decompiles the complete assembly module at the specified path to C# source.</summary>
    public static string Decompile(string assemblyPath)
    {
        var path = ValidateAssemblyPath(assemblyPath);
        var decompiler = new CSharpDecompiler(path, new DecompilerSettings());
        return decompiler.DecompileWholeModuleAsString();
    }

    /// <summary>Gets the IL for the module containing the specified type.</summary>
    public static string GetIL(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return GetIL(GetAssemblyPath(type.Assembly));
    }

    /// <summary>Gets the IL for the module containing the specified method or constructor.</summary>
    public static string GetIL(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return GetIL(GetAssemblyPath(method.Module.Assembly));
    }

    /// <summary>Gets the IL for the assembly module at the specified path.</summary>
    public static string GetIL(string assemblyPath)
    {
        var path = ValidateAssemblyPath(assemblyPath);
        return new CodeAnalysisService().GetIntermediateLanguage(File.ReadAllBytes(path));
    }

    /// <summary>Gets native JIT disassembly for a method without invoking it.</summary>
    /// <remarks>
    /// The method is prepared in an isolated .NET process. Cancellation terminates that process and its child processes.
    /// </remarks>
    public static JitDisassemblyResult GetJitDisassembly(
        MethodBase method,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (!IsSupportedJitPlatform(GetCurrentPlatform()))
        {
            return new JitDisassemblyResult(false,
                Error: "Native JIT disassembly is supported only on Windows, Linux, and macOS.");
        }

        if (!IsSupportedJitArchitecture(RuntimeInformation.ProcessArchitecture))
        {
            return new JitDisassemblyResult(false,
                Error: $"Native JIT disassembly is not supported on {RuntimeInformation.ProcessArchitecture}.");
        }

        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            return new JitDisassemblyResult(false,
                Error: "The current runtime does not support JIT compilation.");
        }

        if (method.ContainsGenericParameters || method.IsGenericMethod || method.DeclaringType?.IsGenericType == true)
        {
            return new JitDisassemblyResult(false,
                Error: "Generic or open methods and methods on generic types are not supported for JIT disassembly.");
        }

        if (method.IsAbstract)
        {
            return new JitDisassemblyResult(false, Error: "Abstract methods cannot be JIT compiled.");
        }

        string assemblyPath;
        try
        {
            assemblyPath = GetAssemblyPath(method.Module.Assembly);
        }
        catch (Exception exception) when (exception is NotSupportedException or FileNotFoundException or IOException)
        {
            return new JitDisassemblyResult(false, Error: exception.Message);
        }

        try
        {
            if (HasModuleInitializer(assemblyPath))
            {
                return new JitDisassemblyResult(false,
                    Error: "JIT disassembly is disabled for assemblies that declare a module initializer.");
            }
        }
        catch (Exception exception)
        {
            return new JitDisassemblyResult(false,
                Error: $"The target assembly could not be inspected safely: {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return GetJitDisassemblyCoreAsync(method, assemblyPath, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new JitDisassemblyResult(false,
                Error: $"The isolated JIT disassembly helper could not run: {exception.Message}");
        }
    }

    internal static bool IsSupportedJitPlatform(OSPlatform platform)
        => platform == OSPlatform.Windows || platform == OSPlatform.Linux || platform == OSPlatform.OSX;

    internal static bool IsSupportedJitArchitecture(Architecture architecture)
        => architecture is Architecture.X86 or Architecture.X64 or Architecture.Arm or Architecture.Arm64;

    internal static bool HasModuleInitializer(string assemblyPath)
    {
        using var stream = File.OpenRead(ValidateAssemblyPath(assemblyPath));
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

    internal static bool IsCompatibleJitAssemblyIdentity(AssemblyName candidate, AssemblyName requested)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(requested);

        return string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(candidate.CultureName ?? string.Empty, requested.CultureName ?? string.Empty,
                   StringComparison.OrdinalIgnoreCase)
               && (candidate.GetPublicKeyToken() ?? []).AsSpan()
                   .SequenceEqual(requested.GetPublicKeyToken() ?? [])
               && (requested.Version == null || candidate.Version != null && candidate.Version >= requested.Version);
    }

    internal static string? SelectHighestCompatibleJitAssemblyIdentity(
        IEnumerable<string> candidateIdentities,
        AssemblyName requested)
    {
        ArgumentNullException.ThrowIfNull(candidateIdentities);
        ArgumentNullException.ThrowIfNull(requested);

        return candidateIdentities
            .Select(identity => new AssemblyName(identity))
            .Where(candidate => IsCompatibleJitAssemblyIdentity(candidate, requested))
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault()
            ?.FullName;
    }

    private static async Task<JitDisassemblyResult> GetJitDisassemblyCoreAsync(
        MethodBase method,
        string assemblyPath,
        CancellationToken cancellationToken)
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"netpad-jit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var framework = $"net{System.Environment.Version.Major}.0";
            var projectPath = Path.Combine(workspace, "JitInspection.csproj");
            var sourcePath = Path.Combine(workspace, "Program.cs");
            var dependencyManifestPath = Path.Combine(workspace, "dependencies.json");
            var helperAssemblyPath = Path.Combine(workspace, "bin", "Release", framework,
                "JitInspection.dll");
            var dependencies = GetJitDependencies(method.Module.Assembly);
            var frameworkReferences = string.Join(System.Environment.NewLine,
                GetRequiredJitFrameworkReferences(dependencies)
                    .Select(reference => $"    <FrameworkReference Include=\"{reference}\" />"));
            var frameworkReferenceItemGroup = frameworkReferences.Length == 0
                ? string.Empty
                : $"<ItemGroup>{System.Environment.NewLine}{frameworkReferences}{System.Environment.NewLine}  </ItemGroup>";

            await File.WriteAllTextAsync(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{{framework}}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <NuGetAudit>false</NuGetAudit>
                    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
                  </PropertyGroup>
                  {{frameworkReferenceItemGroup}}
                </Project>
                """, cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(sourcePath, HelperSource, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(dependencyManifestPath,
                JsonSerializer.Serialize(dependencies), cancellationToken).ConfigureAwait(false);

            var dotnet = FindDotNetExecutable();
            var build = await RunInspectionProcessAsync(
                dotnet,
                workspace,
                ["build", projectPath, "--configuration", "Release", "--nologo", "--verbosity", "quiet"],
                setJitEnvironment: false,
                methodFilter: string.Empty,
                cancellationToken).ConfigureAwait(false);

            if (build.ExitCode != 0)
            {
                return new JitDisassemblyResult(false,
                    Error: $"The isolated JIT helper could not be built. A .NET {System.Environment.Version.Major}.0 SDK is required. {build.Output}".Trim());
            }

            var run = await RunInspectionProcessAsync(
                dotnet,
                workspace,
                [helperAssemblyPath, assemblyPath, method.MetadataToken.ToString(System.Globalization.CultureInfo.InvariantCulture), dependencyManifestPath],
                setJitEnvironment: true,
                GetJitDisassemblyFilter(method),
                cancellationToken).ConfigureAwait(false);

            if (run.ExitCode != 0)
            {
                return new JitDisassemblyResult(true,
                    Error: $"The isolated JIT helper failed (exit code {run.ExitCode}). {run.Output}".Trim());
            }

            return new JitDisassemblyResult(true, Content: run.Output);
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    private static async Task<InspectionProcessResult> RunInspectionProcessAsync(
        string executable,
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        bool setJitEnvironment,
        string methodFilter,
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

        if (setJitEnvironment)
        {
            startInfo.Environment["COMPlus_JitDisasm"] = methodFilter;
            startInfo.Environment["COMPlus_TieredCompilation"] = "0";
            startInfo.Environment["COMPlus_ReadyToRun"] = "0";
            startInfo.Environment["DOTNET_JitDisasm"] = methodFilter;
            startInfo.Environment["DOTNET_TieredCompilation"] = "0";
            startInfo.Environment["DOTNET_ReadyToRun"] = "0";
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{executable}'.");
        }

        using var cancellationRegistration = cancellationToken.Register(() => KillProcessTree(process));
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            KillProcessTree(process);
        }

        var output = await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        return new InspectionProcessResult(process.ExitCode,
            string.Join(System.Environment.NewLine, output.Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private static IReadOnlyCollection<JitDependency> GetJitDependencies(Assembly targetAssembly)
    {
        var dependencies = new Dictionary<string, JitDependency>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        var visited = new HashSet<Assembly>();
        pending.Enqueue(targetAssembly);

        while (pending.TryDequeue(out var assembly))
        {
            if (!visited.Add(assembly))
            {
                continue;
            }

            var loadContext = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assembly dependency;
                try
                {
                    dependency = loadContext.LoadFromAssemblyName(reference);
                }
                catch (FileNotFoundException)
                {
                    continue;
                }

                if (dependency.IsDynamic || string.IsNullOrWhiteSpace(dependency.Location))
                {
                    continue;
                }

                var path = Path.GetFullPath(dependency.Location);
                if (IsFrameworkAssemblyPath(path))
                {
                    continue;
                }

                var identity = dependency.GetName().FullName;
                if (identity == null)
                {
                    continue;
                }

                dependencies.TryAdd(identity, new JitDependency(identity, path));
                pending.Enqueue(dependency);
            }
        }

        return dependencies.Values.ToArray();
    }

    private static bool IsFrameworkAssemblyPath(string path)
    {
        return IsFrameworkAssemblyPath(path, RuntimeEnvironment.GetRuntimeDirectory());
    }

    internal static bool IsFrameworkAssemblyPath(string path, string runtimeDirectory)
    {
        var relativePath = Path.GetRelativePath(Path.GetFullPath(runtimeDirectory), Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return !Path.IsPathRooted(relativePath)
               && relativePath != ".."
               && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", comparison);
    }

    private static IReadOnlyCollection<string> GetRequiredJitFrameworkReferences(
        IReadOnlyCollection<JitDependency> dependencies)
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var sharedDirectory = Directory.GetParent(runtimeDirectory)?.Parent?.FullName;
        if (sharedDirectory == null)
        {
            return [];
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return dependencies
            .Select(dependency => Path.GetRelativePath(sharedDirectory, dependency.Path))
            .Where(relativePath => !Path.IsPathRooted(relativePath)
                                   && relativePath != ".."
                                   && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", comparison))
            .Select(relativePath => relativePath.Split(Path.DirectorySeparatorChar)[0])
            .Where(framework => framework is "Microsoft.AspNetCore.App" or "Microsoft.WindowsDesktop.App")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetJitDisassemblyFilter(MethodBase method)
        => $"*{method.DeclaringType?.FullName}:{method.Name}*";

    private static string FindDotNetExecutable()
    {
        var hostPath = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var dotnetName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var dotnetRoot = System.Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var rootedPath = Path.Combine(dotnetRoot, dotnetName);
            if (File.Exists(rootedPath))
            {
                return rootedPath;
            }
        }

        using var currentProcess = Process.GetCurrentProcess();
        var currentPath = currentProcess.MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            string.Equals(Path.GetFileName(currentPath), dotnetName, StringComparison.OrdinalIgnoreCase))
        {
            return currentPath;
        }

        return dotnetName;
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
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetAssemblyPath(Assembly assembly)
    {
        if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
        {
            throw new NotSupportedException("The assembly is dynamic or has no file path and cannot be inspected.");
        }

        return ValidateAssemblyPath(assembly.Location);
    }

    private static string ValidateAssemblyPath(string assemblyPath)
    {
        if (assemblyPath == null)
        {
            throw new ArgumentNullException(nameof(assemblyPath));
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("An assembly file path is required.", nameof(assemblyPath));
        }

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Assembly file was not found: '{fullPath}'.", fullPath);
        }

        return fullPath;
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        return OSPlatform.Create(RuntimeInformation.OSDescription);
    }

    private sealed record InspectionProcessResult(int ExitCode, string Output);

    private sealed record JitDependency(string Identity, string Path);

    private const string HelperSource = """
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using System.Runtime.Loader;
        using System.Text.Json;

        if (args.Length != 3)
        {
            Console.Error.WriteLine("Expected an assembly path, metadata token, and dependency manifest.");
            return 2;
        }

        try
        {
            var dependencies = JsonSerializer.Deserialize<Dependency[]>(File.ReadAllText(args[2])) ?? [];
            AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
            {
                var dependency = dependencies
                    .Select(candidate => (Dependency: candidate, Identity: new AssemblyName(candidate.Identity)))
                    .Where(candidate => IsCompatible(candidate.Identity, assemblyName))
                    .OrderByDescending(candidate => candidate.Identity.Version)
                    .FirstOrDefault()
                    .Dependency;
                return dependency == null
                    ? null
                    : AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dependency.Path));
            };

            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var token = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            var method = assembly.ManifestModule.ResolveMethod(token);
            if (method == null || method.ContainsGenericParameters)
            {
                Console.Error.WriteLine("The specified method could not be resolved or is open generic.");
                return 3;
            }

            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            Console.WriteLine($"Prepared {method} without invoking it.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }

        static bool IsCompatible(AssemblyName candidate, AssemblyName requested)
        {
            return string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(candidate.CultureName ?? string.Empty, requested.CultureName ?? string.Empty,
                       StringComparison.OrdinalIgnoreCase)
                   && (candidate.GetPublicKeyToken() ?? []).AsSpan()
                       .SequenceEqual(requested.GetPublicKeyToken() ?? [])
                   && (requested.Version == null || candidate.Version != null && candidate.Version >= requested.Version);
        }

        sealed record Dependency(string Identity, string Path);
        """;
}

/// <summary>The result of attempting to get native JIT disassembly for a method.</summary>
public sealed record JitDisassemblyResult(bool IsSupported, string? Content = null, string? Error = null);
