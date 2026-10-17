using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using NetPad.CodeAnalysis;
using NetPad.Compilation;
using NetPad.Compilation.CSharp;
using NetPad.Configuration;
using NetPad.DotNet;
using NetPad.Utilities;

namespace NetPad.Runtime.Tests.Compilation.CSharp;

public class CSharpCodeCompilerTests(ITestOutputHelper testOutputHelper)
{
    private readonly ITestOutputHelper _testOutputHelper = testOutputHelper;

    [Fact]
    public void Can_Compile_EmptyProgram()
    {
        var code = GetProgram("");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.Select(d => d.GetMessage()).JoinToString("|"));
    }

    [Fact]
    public void Can_Compile_SimpleProgram()
    {
        var code = GetProgram("Console.WriteLine(\"Hello World\");");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.Select(d => d.GetMessage()).JoinToString("|"));
    }

    [Theory]
    [InlineData("Console.Write(\"Hello World\";")]
    [InlineData("Console.Write(\"Hello World\")")]
    [InlineData("Console.(\"Hello World\")")]
    [InlineData("Console.WriteE(\"Hello World\")")]
    [InlineData("Console.write(\"Hello World\");")]
    [InlineData("foobar")]
    [InlineData("var date = DateTime.Utcnow;")]
    public void Fails_On_Syntax_Error(string code)
    {
        code = GetProgram(code);

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.False(result.Success);
    }

    [Fact]
    public void Returns_Diagnostics_On_Compilation_Error()
    {
        var code = GetProgram("foobar");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Returns_Source_Generator_Diagnostics_Alongside_Emit_Diagnostics()
    {
        var code = GetProgram("int unused = 0;");
        var input = new CompilationInput(code, DotNetFrameworkVersion.DotNet6,
            assemblyFileReferences: [typeof(DiagnosticSourceGenerator).Assembly.Location]);

        var result = CreateCSharpCodeCompiler().Compile(input);

        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticSourceGenerator.DiagnosticId);
        Assert.Contains(result.Diagnostics, d => d.Id == "CS0219");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Overflow_Checks_Control_Arithmetic_Behavior(bool enabled)
    {
        const string code = "public class Program { public static int Run(int value) => value + 1; public static void Main() { } }";
        var input = new CompilationInput(code, DotNetFrameworkVersion.DotNet9)
            .WithAssemblyName($"OverflowChecks_{enabled}")
            .WithOverflowChecks(enabled);

        var result = CreateCSharpCodeCompiler().Compile(input);

        Assert.True(result.Success, result.Diagnostics.Select(d => d.GetMessage()).JoinToString("|"));
        var method = Assembly.Load(result.AssemblyBytes).GetType("Program")!.GetMethod("Run")!;
        if (enabled)
        {
            var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [int.MaxValue]));
            Assert.IsType<OverflowException>(exception.InnerException);
        }
        else
        {
            Assert.Equal(int.MinValue, method.Invoke(null, [int.MaxValue]));
        }
    }

    [Theory]
    [InlineData("System.Threading.Tasks", "var task = Task.CompletedTask;")]
    [InlineData("System.Collections.Generic", "var list = new List<string>();")]
    public void Can_Compile_Program_With_DotNet_Usings(string @namespace, string code)
    {
        code = GetProgram(code, @namespace);

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.Select(d => d.GetMessage()).JoinToString("|"));
    }

    [Fact]
    public void Can_Compile_CSharp8_Features()
    {
        var code = GetProgram("using var stream = new MemoryStream();", "System.IO");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp9_Features()
    {
        var code = GetProgram("DateTime datetime = new();");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp10_Features()
    {
        var code = GetProgram("var point = (1, 2); int x = 0; (x, int y) = point;");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet6));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp11_Features()
    {
        var code = GetProgram("var str = \"\"\"\nsome text\n\"\"\";");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet7));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp12_Features()
    {
        var code = GetProgram("var IncrementBy = (int source, int increment = 1) => source + increment;");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet8));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp13_Features()
    {
        var code = GetProgram("var lck = new System.Threading.Lock();");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet9));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    [Fact]
    public void Can_Compile_CSharp14_Features()
    {
        var code = GetProgram("var name = nameof(System.Collections.Generic.List<>);");

        var result = CreateCSharpCodeCompiler().Compile(new CompilationInput(code, DotNetFrameworkVersion.DotNet10));

        Assert.True(result.Success, result.Diagnostics.JoinToString(Environment.NewLine));
    }

    private CSharpCodeCompiler CreateCSharpCodeCompiler()
    {
        return new CSharpCodeCompiler(new DotNetInfo(new Settings(), NullLogger<DotNetInfo>.Instance), new CodeAnalysisService());
    }

    private string GetProgram(string code, params string[] additionalNamespaces)
    {
        var namespaces = new[] { "System" }.Union(additionalNamespaces)
            .Select(ns => $"using {ns};");

        var usings = string.Join("\n", namespaces);

        return $@"
{usings}
public class Program
{{
    public static void Main()
    {{
        {code}
    }}
}}
";
    }
}

#pragma warning disable RS1036, RS1041, RS1042, RS2008
[Generator]
public class DiagnosticSourceGenerator : ISourceGenerator
{
    public const string DiagnosticId = "NPADGEN001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Test generator diagnostic",
        "Test generator diagnostic",
        "Testing",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, Location.None));
    }
}
#pragma warning restore RS1036, RS1041, RS1042, RS2008
