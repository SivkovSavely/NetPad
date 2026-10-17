using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetPad.ExecutionModel.ScriptServices;

namespace NetPad.Runtime.Tests.ExecutionModel.ScriptServices;

public class UtilCodeInspectionTests
{
    [Fact]
    public void Decompile_TypeAndMethod_ReturnCSharpSource()
    {
        var typeSource = Util.Decompile(typeof(CodeInspectionFixture));
        var methodSource = Util.Decompile(typeof(CodeInspectionFixture).GetMethod(nameof(CodeInspectionFixture.Add))!);

        Assert.Contains("class CodeInspectionFixture", typeSource);
        Assert.Contains("Add", typeSource);
        Assert.Contains("Add", methodSource);
        Assert.Contains("left + right", methodSource);
    }

    [Fact]
    public void Decompile_Path_ReturnsWholeModuleSource()
    {
        var source = Util.Decompile(typeof(CodeInspectionFixture).Assembly.Location);

        Assert.Contains("CodeInspectionFixture", source);
        Assert.Contains(nameof(CodeInspectionFixture.Add), source);
    }

    [Fact]
    public void GetIL_TypeMethodAndPath_ReturnModuleIL()
    {
        var typeIl = Util.GetIL(typeof(CodeInspectionFixture));
        var methodIl = Util.GetIL(typeof(CodeInspectionFixture).GetMethod(nameof(CodeInspectionFixture.Add))!);
        var pathIl = Util.GetIL(typeof(CodeInspectionFixture).Assembly.Location);

        Assert.Contains(nameof(CodeInspectionFixture), typeIl);
        Assert.Contains(nameof(CodeInspectionFixture.Add), methodIl);
        Assert.Contains(nameof(CodeInspectionFixture.Add), pathIl);
    }

    [Fact]
    public void Decompile_ValidatesAssemblyPathsAndDynamicAssemblies()
    {
        Assert.Throws<ArgumentNullException>(() => Util.Decompile((Type)null!));
        Assert.Throws<ArgumentNullException>(() => Util.Decompile((MethodBase)null!));
        Assert.Throws<ArgumentException>(() => Util.Decompile("  "));
        Assert.Throws<FileNotFoundException>(() => Util.Decompile("missing-inspection-fixture.dll"));

        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("DynamicInspectionFixture"), AssemblyBuilderAccess.Run);
        var dynamicType = dynamicAssembly.DefineDynamicModule("DynamicInspectionFixture").DefineType("DynamicFixture").CreateType()!;

        var exception = Assert.Throws<NotSupportedException>(() => Util.Decompile(dynamicType));
        Assert.Contains("dynamic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetJitDisassembly_ReportsUnsupportedInputs()
    {
        var dynamicMethod = new DynamicMethod("DynamicFixtureMethod", typeof(void), Type.EmptyTypes);
        var dynamicResult = Util.GetJitDisassembly(dynamicMethod);
        var genericMethod = typeof(CodeInspectionFixture).GetMethod(nameof(CodeInspectionFixture.Identity))!;
        var genericResult = Util.GetJitDisassembly(genericMethod);

        Assert.False(dynamicResult.IsSupported);
        Assert.Contains("dynamic", dynamicResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(genericResult.IsSupported);
        Assert.Contains("generic", genericResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Util.IsSupportedJitPlatform(OSPlatform.Create("unsupported")));
        Assert.True(Util.IsSupportedJitArchitecture(Architecture.X64));
        Assert.True(Util.IsSupportedJitArchitecture(Architecture.Arm64));
        Assert.False(Util.IsSupportedJitArchitecture((Architecture)999));
    }

    [Fact]
    public void IsFrameworkAssemblyPath_ExcludesOnlyCurrentCoreRuntimeDirectory()
    {
        var sharedDirectory = Path.Combine(Path.GetTempPath(), "dotnet", "shared");
        var runtimeDirectory = Path.Combine(sharedDirectory, "Microsoft.NETCore.App", "9.0.0");
        var runtimeAssembly = Path.Combine(runtimeDirectory, "System.Private.CoreLib.dll");
        var aspNetCoreAssembly = Path.Combine(sharedDirectory, "Microsoft.AspNetCore.App", "9.0.0",
            "Microsoft.AspNetCore.Http.dll");
        var windowsDesktopAssembly = Path.Combine(sharedDirectory, "Microsoft.WindowsDesktop.App", "9.0.0",
            "PresentationFramework.dll");

        Assert.True(Util.IsFrameworkAssemblyPath(runtimeAssembly, runtimeDirectory));
        Assert.False(Util.IsFrameworkAssemblyPath(aspNetCoreAssembly, runtimeDirectory));
        Assert.False(Util.IsFrameworkAssemblyPath(windowsDesktopAssembly, runtimeDirectory));
    }

    [Fact]
    public void HasModuleInitializer_DetectsWithoutLoadingAssembly()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpad-module-initializer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "ModuleInitializerFixture.dll");
        var markerPath = Path.Combine(directory, "module-initializer-ran.txt");
        var markerLiteral = System.Text.Json.JsonSerializer.Serialize(markerPath);
        var source = $$"""
            using System.IO;
            using System.Runtime.CompilerServices;

            public static class ModuleInitializerFixture
            {
                [ModuleInitializer]
                internal static void Initialize() => File.WriteAllText({{markerLiteral}}, "executed");
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ModuleInitializerFixture",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        try
        {
            using var assemblyStream = new MemoryStream();
            var emitResult = compilation.Emit(assemblyStream);
            Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
            File.WriteAllBytes(assemblyPath, assemblyStream.ToArray());

            Assert.True(Util.HasModuleInitializer(assemblyPath));
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SelectHighestCompatibleJitAssemblyIdentity_RequiresMatchingNameCultureAndToken()
    {
        var requested = new AssemblyName(
            "InspectionDependency, Version=2.0.0.0, Culture=neutral, PublicKeyToken=0011223344556677");
        var lowerVersion = "InspectionDependency, Version=1.9.0.0, Culture=neutral, PublicKeyToken=0011223344556677";
        var compatibleVersion = "inspectiondependency, Version=2.1.0.0, Culture=neutral, PublicKeyToken=0011223344556677";
        var highestCompatibleVersion = "InspectionDependency, Version=3.0.0.0, Culture=neutral, PublicKeyToken=0011223344556677";
        var differentName = "AnotherDependency, Version=9.0.0.0, Culture=neutral, PublicKeyToken=0011223344556677";
        var differentCulture = "InspectionDependency, Version=9.0.0.0, Culture=fr-FR, PublicKeyToken=0011223344556677";
        var differentToken = "InspectionDependency, Version=9.0.0.0, Culture=neutral, PublicKeyToken=8899aabbccddeeff";

        Assert.True(Util.IsCompatibleJitAssemblyIdentity(new AssemblyName(compatibleVersion), requested));
        Assert.False(Util.IsCompatibleJitAssemblyIdentity(new AssemblyName(lowerVersion), requested));
        Assert.False(Util.IsCompatibleJitAssemblyIdentity(new AssemblyName(differentName), requested));
        Assert.False(Util.IsCompatibleJitAssemblyIdentity(new AssemblyName(differentCulture), requested));
        Assert.False(Util.IsCompatibleJitAssemblyIdentity(new AssemblyName(differentToken), requested));
        Assert.Equal(highestCompatibleVersion,
            Util.SelectHighestCompatibleJitAssemblyIdentity(
                [lowerVersion, compatibleVersion, highestCompatibleVersion, differentName, differentCulture, differentToken],
                requested));
    }

    [Fact]
    public void GetJitDisassembly_PreparesMethodInIsolatedProcess()
    {
        _ = typeof(CSharpSyntaxTree).Assembly;
        var method = typeof(CodeInspectionFixture).GetMethod(nameof(CodeInspectionFixture.Main))!;
        var result = Util.GetJitDisassembly(method);

        if (!Util.IsSupportedJitPlatform(GetCurrentPlatform()))
        {
            Assert.False(result.IsSupported);
            Assert.Contains("Windows, Linux, and macOS", result.Error);
            return;
        }

        Assert.True(result.IsSupported, result.Error);
        Assert.Contains("CSharpSyntaxTree", result.Content);
        var listings = result.Content!.Split(System.Environment.NewLine)
            .Where(line => line.Contains("Assembly listing for method", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(listings);
        Assert.All(listings, listing =>
        {
            Assert.Contains("CodeInspectionFixture:Main", listing);
            Assert.DoesNotContain("Program:", listing);
        });
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        return OSPlatform.Create(RuntimeInformation.OSDescription);
    }
}

public sealed class CodeInspectionFixture
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Add(int left, int right) => left + right;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Main(string source) => CSharpSyntaxTree.ParseText(source).GetRoot().ToFullString();

    public T Identity<T>(T value) => value;
}
