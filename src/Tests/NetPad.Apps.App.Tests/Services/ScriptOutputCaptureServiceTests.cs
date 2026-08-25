using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NetPad.Common;
using NetPad.Events;
using NetPad.ExecutionModel;
using NetPad.IO;
using NetPad.Presentation;
using NetPad.Scripts;
using NetPad.Services;

namespace NetPad.Apps.App.Tests.Services;

public sealed class ScriptOutputCaptureServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _serviceScope;
    private IOutputWriter<object> _forwarder = null!;
    private readonly ScriptEnvironment _environment;
    private readonly ScriptOutputCaptureService _capture;
    private readonly Guid _scriptId;

    public ScriptOutputCaptureServiceTests()
    {
        var runner = new Mock<IScriptRunner>();
        runner.Setup(r => r.AddOutput(It.IsAny<IOutputWriter<object>>()))
            .Callback<IOutputWriter<object>>(writer => _forwarder = writer);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton(Mock.Of<IScriptRunnerFactory>(f =>
            f.CreateRunner(It.IsAny<Script>()) == runner.Object));

        _serviceProvider = services.BuildServiceProvider();
        _serviceScope = _serviceProvider.CreateScope();
        _scriptId = Guid.NewGuid();
        _environment = new ScriptEnvironment(
            new Script(_scriptId, "Test", new ScriptConfig(ScriptKind.Program, GlobalConsts.AppDotNetFrameworkVersion)),
            _serviceScope);
        _capture = new ScriptOutputCaptureService(_serviceProvider.GetRequiredService<IEventBus>());
    }

    [Fact]
    public async Task MutableUpdatesAreFoldedAndRetainInitialMetadata()
    {
        _capture.StartCapture(_scriptId, _environment);

        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 4, "initial", ScriptOutputFormat.Html)
        {
            OutputId = "mutable"
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 5, "second", ScriptOutputFormat.Text)
        {
            OutputId = "mutable", IsUpdate = true
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 6, "latest", ScriptOutputFormat.Html)
        {
            OutputId = "mutable", IsUpdate = true
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 7, "after"));

        var output = await GetOutputAsync();
        Assert.Equal(2, output.Count);
        var mutable = output[0];
        Assert.Equal("latest", mutable.Body);
        Assert.Equal(ScriptOutputFormat.Html, mutable.Format);
        Assert.Equal("mutable", mutable.OutputId);
        Assert.False(mutable.IsUpdate);
        Assert.Equal((uint)4, mutable.Order);
        Assert.Equal("after", output[1].Body);
    }

    [Fact]
    public async Task OversizedReplacementTriggersTruncation()
    {
        _capture.StartCapture(_scriptId, _environment);
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, "initial")
        {
            OutputId = "mutable"
        });
        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 2, new string('b', 100 * 1024 + 1))
        {
            OutputId = "mutable", IsUpdate = true
        });

        var output = await GetOutputAsync();
        Assert.Equal(2, output.Count);
        Assert.Equal("[Output truncated: exceeded 100KB limit]", output[1].Body);
        Assert.Equal("mutable", output[0].OutputId);
        Assert.Equal("initial", output[0].Body);
    }

    [Fact]
    public async Task OrphanUpdateIsDropped()
    {
        _capture.StartCapture(_scriptId, _environment);

        await WriteAsync(new ScriptOutput(ScriptOutputKind.Result, 1, "ghost")
        {
            OutputId = "ghost", IsUpdate = true
        });

        var output = await GetOutputAsync();
        Assert.Empty(output);
    }

    private Task WriteAsync(ScriptOutput output) => _forwarder.WriteAsync(output);

    private async Task<IReadOnlyList<ScriptOutput>> GetOutputAsync()
    {
        var result = await _capture.GetCapturedOutputAsync(_scriptId, false, null, default);
        return result.Output!;
    }

    public void Dispose()
    {
        _capture.Dispose();
        _environment.Dispose();
        _serviceScope.Dispose();
        _serviceProvider.Dispose();
    }
}
