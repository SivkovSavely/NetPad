using NetPad.Compilation.Directives;
using NetPad.DotNet.References;

namespace NetPad.Runtime.Tests.Compilation.Directives;

public class LoadDirectiveProcessorTests
{
    [Fact]
    public void Resolves_Relative_And_Transitive_Loads_And_Collects_Usings()
    {
        using var directory = new TemporaryDirectory();
        var childDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "lib")).FullName;
        var childPath = Path.Combine(childDirectory, "child.cs");
        var nestedPath = Path.Combine(childDirectory, "nested.cs");
        File.WriteAllText(childPath, "using System.Text;\nvar loaded = 1;\n#load \"nested.cs\"");
        File.WriteAllText(nestedPath, "var nested = 2;");
        var scriptPath = Path.Combine(directory.Path, "root.netpad");

        var result = LoadDirectiveProcessor.Process("#load \"lib/child.cs\"\nConsole.WriteLine(loaded + nested);", scriptPath);

        Assert.Equal([childPath, nestedPath], result.LoadedPaths);
        Assert.Contains("var loaded = 1;", result.Code);
        Assert.Contains("var nested = 2;", result.Code);
        Assert.DoesNotContain("#load", result.Code);
        Assert.Contains("System.Text", result.AdditionalUsings);
        Assert.Contains($"#line 1 \"{childPath.Replace("\\", "\\\\")}\"", result.Code);
        Assert.Contains($"#line 2 \"{scriptPath.Replace("\\", "\\\\")}\"", result.Code);
    }

    [Fact]
    public void Expands_Recursive_Wildcards_In_Sorted_Order()
    {
        using var directory = new TemporaryDirectory();
        var parts = Directory.CreateDirectory(Path.Combine(directory.Path, "parts")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(parts, "nested")).FullName;
        File.WriteAllText(Path.Combine(parts, "z.cs"), "var z = 3;");
        File.WriteAllText(Path.Combine(parts, "a.cs"), "var a = 1;");
        File.WriteAllText(Path.Combine(nested, "m.cs"), "var m = 2;");

        var result = LoadDirectiveProcessor.Process("#load \"parts/**/*.cs\"", Path.Combine(directory.Path, "root.cs"));

        Assert.Equal(
            [Path.Combine(parts, "a.cs"), Path.Combine(nested, "m.cs"), Path.Combine(parts, "z.cs")],
            result.LoadedPaths);
        Assert.True(result.Code.IndexOf("var a", StringComparison.Ordinal) < result.Code.IndexOf("var m", StringComparison.Ordinal));
        Assert.True(result.Code.IndexOf("var m", StringComparison.Ordinal) < result.Code.IndexOf("var z", StringComparison.Ordinal));
    }

    [Fact]
    public void Loads_Netpad_Code_And_Configured_Namespaces()
    {
        using var directory = new TemporaryDirectory();
        var loadedPath = Path.Combine(directory.Path, "loaded.netpad");
        File.WriteAllText(loadedPath,
            "00000000-0000-0000-0000-000000000001\n{\"Config\":{\"Namespaces\":[\"System.Net\"]}}\n#Code\nusing System.Text;\nvar loaded = 42;");

        var result = LoadDirectiveProcessor.Process("#load \"loaded.netpad\"", Path.Combine(directory.Path, "root.cs"));

        Assert.Contains("var loaded = 42;", result.Code);
        Assert.Contains("System.Net", result.AdditionalUsings);
        Assert.Contains("System.Text", result.AdditionalUsings);
        Assert.Contains($"#line 4 \"{loadedPath.Replace("\\", "\\\\")}\"", result.Code);
    }

    [Fact]
    public void Loads_Netpad_References_And_Collects_Static_ToDump_Transformers_In_Load_Order()
    {
        using var directory = new TemporaryDirectory();
        var loadedPath = Path.Combine(directory.Path, "loaded.netpad");
        File.WriteAllText(loadedPath,
            "00000000-0000-0000-0000-000000000001\n" +
            "{\"Config\":{\"References\":[{\"discriminator\":\"PackageReference\",\"Title\":\"Example\",\"PackageId\":\"Example.Package\",\"Version\":\"1.2.3\"}]}}\n" +
            "#Code\n" +
            "namespace Loaded; public static class First { public static object? ToDump(object? value) => value; }");
        var secondPath = Path.Combine(directory.Path, "second.cs");
        File.WriteAllText(secondPath,
            "namespace Loaded; public static class Second { public static object? ToDump(object? value) => value; }");

        var result = LoadDirectiveProcessor.Process("#load \"loaded.netpad\"\n#load \"second.cs\"", Path.Combine(directory.Path, "root.cs"));

        Assert.IsType<PackageReference>(Assert.Single(result.AdditionalReferences));
        Assert.Equal(
            ["global::Loaded.First.ToDump", "global::Loaded.Second.ToDump"],
            result.ToDumpTransformers);
        var initializer = result.GetToDumpInitializerCode();
        Assert.Contains("DumpTransformer.Clear()", initializer);
        Assert.True(initializer.IndexOf("Loaded.First.ToDump", StringComparison.Ordinal)
                    < initializer.IndexOf("Loaded.Second.ToDump", StringComparison.Ordinal));
        Assert.Contains("ModuleInitializer", initializer);
    }

    [Fact]
    public void Root_Usings_After_Load_Are_Hoisted_Without_Shifting_Source_Lines()
    {
        using var directory = new TemporaryDirectory();
        var loadedPath = Path.Combine(directory.Path, "loaded.cs");
        var scriptPath = Path.Combine(directory.Path, "root.cs");
        File.WriteAllText(loadedPath, "public class LoadedType { }");

        var result = LoadDirectiveProcessor.Process(
            "#load \"loaded.cs\"\nusing System.Text;\nvar text = new StringBuilder();",
            scriptPath);

        Assert.Contains("System.Text", result.AdditionalUsings);
        Assert.DoesNotContain("using System.Text", result.Code);
        Assert.Contains("var text = new StringBuilder();", result.Code);
        Assert.Contains($"#line 2 \"{scriptPath.Replace("\\", "\\\\")}\"", result.Code);
        Assert.True(result.Code.IndexOf("var text", StringComparison.Ordinal)
                    < result.Code.IndexOf("class LoadedType", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("void", "global::Program.Main1();")]
    [InlineData("global::System.Threading.Tasks.Task", "await global::Program.Main1();")]
    public void Alternate_Entry_Point_Compilation_Excludes_Top_Level_Code(string returnType, string invocation)
    {
        const string code = "Console.WriteLine(\"ordinary run\");\n" +
                            "public partial class Program { public static RETURN_TYPE Main1() { } }";
        var source = code.Replace("RETURN_TYPE", returnType, StringComparison.Ordinal);
        var loaded = LoadDirectiveProcessor.Process(source, "/tmp/alternate-entry.netpad");

        var compilationCode = loaded.GetCompilationCode("/tmp/alternate-entry.netpad", "Main1");

        Assert.DoesNotContain("Console.WriteLine(\"ordinary run\")", compilationCode);
        Assert.Contains(invocation, compilationCode);
        Assert.Contains("public static " + returnType + " Main1()", compilationCode);
    }

    [Fact]
    public void Missing_Alternate_Entry_Point_Produces_Compiler_Diagnostic()
    {
        var loaded = LoadDirectiveProcessor.Process("Console.WriteLine(1);", null);

        Assert.StartsWith("#error No parameterless static entry point named Main1 was found.",
            loaded.GetCompilationCode(null, "Main1"));
    }

    [Fact]
    public void Reports_Cycle_And_Unsaved_Relative_Load_As_Compiler_Diagnostics()
    {
        using var directory = new TemporaryDirectory();
        var firstPath = Path.Combine(directory.Path, "first.cs");
        var secondPath = Path.Combine(directory.Path, "second.cs");
        File.WriteAllText(firstPath, "#load \"second.cs\"");
        File.WriteAllText(secondPath, "#load \"first.cs\"");

        var cycle = LoadDirectiveProcessor.Process("#load \"first.cs\"", Path.Combine(directory.Path, "root.cs"));
        var unsaved = LoadDirectiveProcessor.Process("#load \"child.cs\"", null);

        Assert.Contains("#error #load at line 1: #load cycle detected", cycle.Code);
        Assert.Contains($"#line 1 \"{secondPath.Replace("\\", "\\\\")}\"", cycle.Code);
        Assert.Contains("relative #load paths require the script to be saved", unsaved.Code);
    }

    [Fact]
    public void Does_Not_Interpret_Load_Text_In_Comments_Or_Raw_Strings()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "real.cs"), "var loaded = 1;");
        var code = "// #load \"missing-comment.cs\"\n" +
                   "var text = \"\"\"\n#load \"missing-string.cs\"\n\"\"\";\n" +
                   "#load \"real.cs\"";

        var result = LoadDirectiveProcessor.Process(code, Path.Combine(directory.Path, "root.cs"));

        Assert.Equal([Path.Combine(directory.Path, "real.cs")], result.LoadedPaths);
        Assert.Contains("#load \"missing-comment.cs\"", result.Code);
        Assert.Contains("#load \"missing-string.cs\"", result.Code);
        Assert.Contains("var loaded = 1;", result.Code);
    }

    [Fact]
    public void InputHash_Changes_When_Loaded_Source_Changes()
    {
        using var directory = new TemporaryDirectory();
        var loadedPath = Path.Combine(directory.Path, "child.cs");
        File.WriteAllText(loadedPath, "var loaded = 1;");
        var scriptPath = Path.Combine(directory.Path, "root.cs");

        var first = LoadDirectiveProcessor.Process("#load \"child.cs\"", scriptPath);
        File.WriteAllText(loadedPath, "var loaded = 2;");
        var second = LoadDirectiveProcessor.Process("#load \"child.cs\"", scriptPath);

        Assert.NotEqual(first.InputHash, second.InputHash);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
