using Microsoft.CodeAnalysis;
using NetPad.Compilation;
using NetPad.DotNet;

namespace NetPad.Runtime.Tests.Compilation;

public class PreprocessorSymbolsTests
{
    [Fact]
    public void Debug_OptimizationLevel_Symbols()
    {
        var symbols = PreprocessorSymbols.For(OptimizationLevel.Debug);
        Assert.Equal(["NETPAD", "DEBUG", "TRACE"], symbols);
    }

    [Fact]
    public void Release_OptimizationLevel_Symbols()
    {
        var symbols = PreprocessorSymbols.For(OptimizationLevel.Release);
        Assert.Equal(["NETPAD", "RELEASE"], symbols);
    }

    public static IEnumerable<object?[]> FrameworkVersionTestData =>
    [
        [DotNetFrameworkVersion.DotNet5, new[] { "NETPAD", "NET", "NET5_0", "NET5_0_OR_GREATER" }],
        [
            DotNetFrameworkVersion.DotNet6,
            new[] { "NETPAD", "NET", "NET6_0", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER" }
        ],
        [
            DotNetFrameworkVersion.DotNet8,
            new[]
            {
                "NETPAD", "NET",
                "NET8_0", "NET8_0_OR_GREATER",
                "NET7_0_OR_GREATER",
                "NET6_0_OR_GREATER",
                "NET5_0_OR_GREATER"
            }
        ]
    ];

    [Theory]
    [MemberData(nameof(FrameworkVersionTestData))]
    public void FrameworkVersion_Symbols(DotNetFrameworkVersion version, string[] expectedSymbols)
    {
        var symbols = PreprocessorSymbols.For(version);
        Assert.Equal(expectedSymbols, symbols);
    }

    public static IEnumerable<object?[]> OptimizationLevelAndFrameworkVersionTestData =>
    [
        [
            OptimizationLevel.Debug, DotNetFrameworkVersion.DotNet5,
            new[] { "NETPAD", "DEBUG", "TRACE", "NET", "NET5_0", "NET5_0_OR_GREATER" }
        ],
        [
            OptimizationLevel.Debug, DotNetFrameworkVersion.DotNet6,
            new[] { "NETPAD", "DEBUG", "TRACE", "NET", "NET6_0", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER" }
        ],
        [
            OptimizationLevel.Debug, DotNetFrameworkVersion.DotNet8,
            new[]
            {
                "NETPAD", "DEBUG", "TRACE", "NET",
                "NET8_0", "NET8_0_OR_GREATER",
                "NET7_0_OR_GREATER",
                "NET6_0_OR_GREATER",
                "NET5_0_OR_GREATER"
            }
        ],
        [
            OptimizationLevel.Release, DotNetFrameworkVersion.DotNet5,
            new[] { "NETPAD", "RELEASE", "NET", "NET5_0", "NET5_0_OR_GREATER" }
        ],
        [
            OptimizationLevel.Release, DotNetFrameworkVersion.DotNet6,
            new[] { "NETPAD", "RELEASE", "NET", "NET6_0", "NET6_0_OR_GREATER", "NET5_0_OR_GREATER" }
        ],
        [
            OptimizationLevel.Release, DotNetFrameworkVersion.DotNet8,
            new[]
            {
                "NETPAD", "RELEASE", "NET",
                "NET8_0", "NET8_0_OR_GREATER",
                "NET7_0_OR_GREATER",
                "NET6_0_OR_GREATER",
                "NET5_0_OR_GREATER"
            }
        ]
    ];

    [Theory]
    [MemberData(nameof(OptimizationLevelAndFrameworkVersionTestData))]
    public void OptimizationLevel_And_FrameworkVersion_Symbols(
        OptimizationLevel level,
        DotNetFrameworkVersion version,
        string[] expectedSymbols)
    {
        var symbols = PreprocessorSymbols.For(level, version);
        Assert.Equal(expectedSymbols, symbols);
    }

    [Fact]
    public void Gui_Context_Adds_Gui_Symbol()
    {
        var symbols = PreprocessorSymbols.For(
            OptimizationLevel.Debug,
            DotNetFrameworkVersion.DotNet9,
            ScriptCompilationContext.Gui);

        Assert.Contains("NETPAD_GUI", symbols);
        Assert.DoesNotContain("NETPAD_CLI", symbols);
        Assert.DoesNotContain("CMD", symbols);
    }

    [Fact]
    public void Headless_Context_Adds_Cli_And_Cmd_Symbols()
    {
        var symbols = PreprocessorSymbols.For(
            OptimizationLevel.Release,
            DotNetFrameworkVersion.DotNet9,
            ScriptCompilationContext.Headless);

        Assert.Contains("NETPAD_CLI", symbols);
        Assert.Contains("CMD", symbols);
        Assert.DoesNotContain("NETPAD_GUI", symbols);
    }
}
