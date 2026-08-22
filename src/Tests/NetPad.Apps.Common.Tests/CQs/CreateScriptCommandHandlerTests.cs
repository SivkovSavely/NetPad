using Moq;
using NetPad.Apps.CQs;
using NetPad.Configuration;
using NetPad.DotNet;
using NetPad.Events;
using NetPad.Scripts;

namespace NetPad.Apps.Common.Tests.CQs;

public class CreateScriptCommandHandlerTests
{
    [Fact]
    public async Task Uses_configured_default_when_framework_is_available()
    {
        var handler = CreateHandler(DotNetFrameworkVersion.DotNet8,
            [new DotNetSdkVersion(new SemanticVersion("8.0.100"))]);

        var script = await handler.Handle(new CreateScriptCommand(null), CancellationToken.None);

        Assert.Equal(DotNetFrameworkVersion.DotNet8, script.Config.TargetFrameworkVersion);
    }

    [Fact]
    public async Task Uses_latest_supported_framework_when_no_default_is_configured()
    {
        var handler = CreateHandler(null,
            [new DotNetSdkVersion(new SemanticVersion("9.0.100"))]);

        var script = await handler.Handle(new CreateScriptCommand(null), CancellationToken.None);

        Assert.Equal(DotNetFrameworkVersion.DotNet9, script.Config.TargetFrameworkVersion);
    }

    [Fact]
    public async Task Falls_back_when_configured_framework_is_unavailable()
    {
        var handler = CreateHandler(DotNetFrameworkVersion.DotNet8,
            [new DotNetSdkVersion(new SemanticVersion("9.0.100"))]);

        var script = await handler.Handle(new CreateScriptCommand(null), CancellationToken.None);

        Assert.Equal(DotNetFrameworkVersion.DotNet9, script.Config.TargetFrameworkVersion);
    }

    private static CreateScriptCommand.Handler CreateHandler(
        DotNetFrameworkVersion? configuredVersion,
        DotNetSdkVersion[] installedVersions)
    {
        var settings = new Settings().SetDefaultScriptTargetFrameworkVersion(configuredVersion);
        var dotNetInfo = new Mock<IDotNetInfo>();
        dotNetInfo.Setup(x => x.GetDotNetSdkVersions()).Returns(installedVersions);
        dotNetInfo.Setup(x => x.GetLatestSupportedDotNetSdkVersion(false))
            .Returns(installedVersions.MaxBy(x => x.Version));

        var repository = new Mock<IScriptRepository>();
        repository.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<DotNetFrameworkVersion>()))
            .ReturnsAsync((string name, DotNetFrameworkVersion version) =>
                new Script(Guid.NewGuid(), name, new ScriptConfig(ScriptKind.Program, version)));

        return new CreateScriptCommand.Handler(
            Mock.Of<IScriptNameGenerator>(x => x.Generate(It.IsAny<string>()) == "Script"),
            repository.Object,
            dotNetInfo.Object,
            settings,
            new EventBus());
    }
}
