using Moq;
using NetPad.Apps.Scripts;
using NetPad.DotNet;

namespace NetPad.Apps.Common.Tests.Scripts;

public class ScriptConfigCheckedArithmeticTests
{
    [Fact]
    public void Missing_CheckedArithmetic_Value_Defaults_To_Checked()
    {
        var config = new ScriptSerializer.ScriptConfigData
        {
            TargetFrameworkVersion = DotNetFrameworkVersion.DotNet9
        }.ToScriptConfig(Mock.Of<IDotNetInfo>());

        Assert.True(config.CheckedArithmetic);
    }

    [Fact]
    public void Explicit_Unchecked_Value_Is_Persisted()
    {
        var config = new ScriptSerializer.ScriptConfigData
        {
            TargetFrameworkVersion = DotNetFrameworkVersion.DotNet9,
            CheckedArithmetic = false
        }.ToScriptConfig(Mock.Of<IDotNetInfo>());

        Assert.False(config.CheckedArithmetic);
        Assert.False(ScriptSerializer.ScriptConfigData.From(config).CheckedArithmetic);
    }
}
