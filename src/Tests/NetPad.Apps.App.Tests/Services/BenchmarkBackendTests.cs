using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using NetPad.DotNet;
using NetPad.DotNet.References;
using NetPad.Presentation;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class BenchmarkBackendTests
{
    [Fact]
    public void Harness_includes_script_configuration_and_uses_a_benchmark_only_package_reference()
    {
        var configuration = new BenchmarkScriptConfiguration(
            DotNetFrameworkVersion.DotNet8,
            ["System.Linq", "Example.Tools"],
            [
                new PackageReference("Example&Tools", "Example tools", "1.2.3"),
                new PackageReference("BenchmarkDotNet", "User package", "0.13.12"),
                new AssemblyFileReference("/tmp/Example&Tools.dll")
            ],
            CheckedArithmetic: true,
            UseAspNet: true);

        var generated = BenchmarkHarnessGenerator.Generate(configuration, "public int Measure() => 42;");
        var project = XDocument.Parse(generated.ProjectFile);
        var packageReferences = project.Descendants("PackageReference").ToArray();
        var assemblyReference = project.Descendants("Reference").Single();

        Assert.Equal("net8.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("true", project.Descendants("CheckForOverflowUnderflow").Single().Value);
        Assert.Equal("Microsoft.AspNetCore.App",
            project.Descendants("FrameworkReference").Single().Attribute("Include")?.Value);
        Assert.Contains(packageReferences, package =>
            package.Attribute("Include")?.Value == "Example&Tools" && package.Attribute("Version")?.Value == "1.2.3");
        var benchmarkPackage = Assert.Single(packageReferences,
            package => package.Attribute("Include")?.Value == "BenchmarkDotNet");
        Assert.Equal(BenchmarkHarnessGenerator.BenchmarkDotNetPackageVersion,
            benchmarkPackage.Attribute("Version")?.Value);
        Assert.Equal("/tmp/Example&Tools.dll", assemblyReference.Element("HintPath")?.Value);
        Assert.Contains("using Example.Tools;", generated.ProgramFile);
        Assert.Contains("[Benchmark]\npublic int Measure()", generated.ProgramFile);
        Assert.DoesNotContain(configuration.References, reference =>
            reference is PackageReference package && package.PackageId == "BenchmarkDotNet"
            && package.Version == BenchmarkHarnessGenerator.BenchmarkDotNetPackageVersion);
    }

    [Fact]
    public void Harness_places_a_selected_statement_inside_a_benchmark_method()
    {
        var generated = BenchmarkHarnessGenerator.Generate(
            new BenchmarkScriptConfiguration(DotNetFrameworkVersion.DotNet8, [], [], false),
            "var value = 40 + 2;\n_ = value;");

        Assert.Contains("[Benchmark]\n    public void Benchmark()", generated.ProgramFile);
        Assert.Contains("var value = 40 + 2;\n_ = value;", generated.ProgramFile);
    }

    [Fact]
    public void Report_parser_reads_benchmark_statistics_and_allocations()
    {
        const string json = """
            {
              "Benchmarks": [
                {
                  "FullName": "Example.Benchmarks.Calculate",
                  "Statistics": {
                    "Mean": 12.5,
                    "ConfidenceInterval": { "Margin": 0.8 },
                    "StandardDeviation": 1.2
                  },
                  "Memory": { "BytesAllocatedPerOperation": 64 }
                }
              ]
            }
            """;

        var result = Assert.Single(BenchmarkReportParser.Parse(json));

        Assert.Equal("Example.Benchmarks.Calculate", result.Name);
        Assert.Equal(12.5, result.MeanNanoseconds);
        Assert.Equal(0.8, result.ErrorNanoseconds);
        Assert.Equal(1.2, result.StandardDeviationNanoseconds);
        Assert.Equal(64, result.AllocatedBytes);
    }

    [Fact]
    public void Html_output_is_safe_and_updates_reuse_the_same_result_output_id()
    {
        var operationId = Guid.NewGuid();
        var initial = BenchmarkOutputFactory.Create(operationId,
            BenchmarkHtml.Progress(["<script>alert(1)</script> &"]), isUpdate: false);
        var update = BenchmarkOutputFactory.Create(operationId,
            BenchmarkHtml.Completed([new BenchmarkResult("<img src=x>", 1, 0.1, 0.2, 0)]), isUpdate: true);

        Assert.Equal(ScriptOutputKind.Result, initial.Kind);
        Assert.Equal(ScriptOutputFormat.Html, initial.Format);
        Assert.False(initial.IsUpdate);
        Assert.Equal(initial.OutputId, update.OutputId);
        Assert.True(update.IsUpdate);
        Assert.DoesNotContain("<script>", initial.Body);
        Assert.Contains("&lt;script&gt;", initial.Body);
        Assert.DoesNotContain("<img src=x>", update.Body);
        Assert.Contains("Mean (ns)", update.Body);
        Assert.Contains("netpad-chart", update.Body);
    }

    [Fact]
    public async Task Process_tree_kill_terminates_a_child_process()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("/bin/sh")
        {
            ArgumentList = { "-c", "sleep 30 & echo $!; wait" },
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var childPidText = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(int.TryParse(childPidText, out var childPid));
        BenchmarkProcessTree.Kill(process);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var childStopped = false;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var statPath = $"/proc/{childPid}/stat";
            if (!File.Exists(statPath))
            {
                childStopped = true;
                break;
            }

            var stat = await File.ReadAllTextAsync(statPath);
            var state = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (state == "Z")
            {
                childStopped = true;
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(childStopped, "The child process was still running after the process tree was killed.");
    }
}
