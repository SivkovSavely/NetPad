using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetPad.DotNet;
using NetPad.DotNet.References;

namespace NetPad.Services;

internal sealed record BenchmarkScriptConfiguration(
    DotNetFrameworkVersion TargetFrameworkVersion,
    IReadOnlyCollection<string> Namespaces,
    IReadOnlyCollection<Reference> References,
    bool CheckedArithmetic,
    bool UseAspNet = false);

internal sealed record GeneratedBenchmarkHarness(string ProjectFile, string ProgramFile);

internal static class BenchmarkHarnessGenerator
{
    internal const string BenchmarkDotNetPackageVersion = "0.14.0";

    internal static GeneratedBenchmarkHarness Generate(BenchmarkScriptConfiguration configuration, string selectedCode)
    {
        var project = new XElement("Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", configuration.TargetFrameworkVersion.GetTargetFrameworkMoniker()),
                new XElement("CheckForOverflowUnderflow", configuration.CheckedArithmetic ? "true" : "false"),
                new XElement("AllowUnsafeBlocks", "true")),
            new XElement("ItemGroup",
                new XElement("PackageReference",
                    new XAttribute("Include", "BenchmarkDotNet"),
                    new XAttribute("Version", BenchmarkDotNetPackageVersion))));

        if (configuration.UseAspNet)
        {
            project.Add(new XElement("ItemGroup",
                new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App"))));
        }

        var packageReferences = configuration.References
            .OfType<PackageReference>()
            .Where(reference => !string.Equals(reference.PackageId, "BenchmarkDotNet", StringComparison.OrdinalIgnoreCase));
        foreach (var reference in packageReferences)
        {
            project.Add(new XElement("ItemGroup",
                new XElement("PackageReference",
                    new XAttribute("Include", reference.PackageId),
                    new XAttribute("Version", reference.Version))));
        }

        foreach (var reference in configuration.References.OfType<AssemblyFileReference>())
        {
            project.Add(new XElement("ItemGroup",
                new XElement("Reference",
                    new XAttribute("Include", Path.GetFileNameWithoutExtension(reference.AssemblyPath)),
                    new XElement("HintPath", reference.AssemblyPath),
                    new XElement("Private", "true"))));
        }

        return new GeneratedBenchmarkHarness(
            new XDocument(new XDeclaration("1.0", "utf-8", null), project).ToString(),
            GenerateProgram(configuration.Namespaces, selectedCode));
    }

    private static string GenerateProgram(IReadOnlyCollection<string> namespaces, string selectedCode)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using BenchmarkDotNet.Attributes;");
        builder.AppendLine("using BenchmarkDotNet.Configs;");
        builder.AppendLine("using BenchmarkDotNet.Exporters.Json;");
        builder.AppendLine("using BenchmarkDotNet.Running;");

        foreach (var @namespace in namespaces.Where(ns => !string.IsNullOrWhiteSpace(ns)).Distinct())
        {
            builder.Append("using ").Append(@namespace.Trim()).AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine("namespace NetPad.BenchmarkHarness;");
        builder.AppendLine();
        builder.AppendLine("[MemoryDiagnoser]");
        builder.AppendLine("public class BenchmarkCases");
        builder.AppendLine("{");

        var parsedStatement = SyntaxFactory.ParseStatement(selectedCode);
        if (!parsedStatement.ContainsDiagnostics && parsedStatement is not LocalFunctionStatementSyntax)
        {
            builder.AppendLine("    [Benchmark]");
            builder.AppendLine("    public void Benchmark()");
            builder.AppendLine("    {");
            builder.AppendLine(selectedCode);
            builder.AppendLine("    }");
        }
        else if (SyntaxFactory.ParseMemberDeclaration(selectedCode) is MethodDeclarationSyntax method
                 && !method.ContainsDiagnostics)
        {
            builder.AppendLine("    [Benchmark]");
            builder.AppendLine(selectedCode);
        }
        else if (SyntaxFactory.ParseMemberDeclaration(selectedCode) is PropertyDeclarationSyntax property
                 && !property.ContainsDiagnostics)
        {
            builder.AppendLine(selectedCode);
            builder.Append("    [Benchmark]").AppendLine();
            builder.Append("    public void Benchmark() { _ = ").Append(property.Identifier.Text).AppendLine("; }");
        }
        else if (SyntaxFactory.ParseMemberDeclaration(selectedCode) is FieldDeclarationSyntax field
                 && !field.ContainsDiagnostics)
        {
            builder.AppendLine(selectedCode);
            builder.AppendLine("    [Benchmark]");
            builder.AppendLine("    public void Benchmark()");
            builder.AppendLine("    {");
            foreach (var variable in field.Declaration.Variables)
            {
                builder.Append("        _ = ").Append(variable.Identifier.Text).AppendLine(";");
            }
            builder.AppendLine("    }");
        }
        else
        {
            builder.AppendLine("    [Benchmark]");
            builder.AppendLine("    public void Benchmark()");
            builder.AppendLine("    {");
            builder.AppendLine(selectedCode);
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("public static class Program");
        builder.AppendLine("{");
        builder.AppendLine("    public static void Main() => BenchmarkRunner.Run<BenchmarkCases>(");
        builder.AppendLine("        ManualConfig.Create(DefaultConfig.Instance).AddExporter(JsonExporter.Full));");
        builder.AppendLine("}");
        return builder.ToString();
    }
}
