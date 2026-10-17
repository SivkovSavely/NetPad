using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetPad.Apps.UiInterop;
using NetPad.Configuration;
using NetPad.DotNet;
using NetPad.DotNet.References;
using NetPad.IO;
using NetPad.Scripts;
using NetPad.Scripts.Events;

namespace NetPad.Services;

public sealed class BenchmarkService(
    IDotNetInfo dotNetInfo,
    IServiceScopeFactory scopeFactory,
    ILogger<BenchmarkService> logger)
{
    private static readonly DirectoryPath BenchmarkDirectoryPath = AppDataProvider.ProcessTempDirectoryPath.Combine("Benchmarks");
    private readonly Dictionary<Guid, BenchmarkRun> _runs = new();
    private readonly object _runsLock = new();

    public Guid Start(Script script, string selectedCode)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedCode);

        var configuration = new BenchmarkScriptConfiguration(
            script.Config.TargetFrameworkVersion,
            script.Config.Namespaces.ToArray(),
            script.Config.References.ToArray(),
            script.Config.CheckedArithmetic,
            script.Config.UseAspNet);
        var run = new BenchmarkRun(Guid.NewGuid(), script.Id);

        lock (_runsLock)
        {
            _runs.Add(run.OperationId, run);
        }

        _ = Task.Run(() => ExecuteAsync(run, configuration, selectedCode));
        return run.OperationId;
    }

    public bool Cancel(Guid scriptId, Guid operationId)
    {
        BenchmarkRun? run;
        lock (_runsLock)
        {
            if (!_runs.TryGetValue(operationId, out run) || run.ScriptId != scriptId)
            {
                return false;
            }
        }

        Cancel(run);
        return true;
    }

    public async Task CancelAllAsync()
    {
        BenchmarkRun[] runs;
        lock (_runsLock)
        {
            runs = _runs.Values.ToArray();
        }

        foreach (var run in runs)
        {
            Cancel(run);
        }

        await Task.WhenAll(runs.Select(run => run.Completion.Task));
    }

    public void CleanUpStaleRuns()
    {
        BenchmarkDirectoryPath.DeleteIfExists();
    }

    private void Cancel(BenchmarkRun run)
    {
        run.RequestCancellation();
        try
        {
            run.KillProcessTree();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not terminate benchmark operation {OperationId}", run.OperationId);
        }
    }

    private async Task ExecuteAsync(BenchmarkRun run, BenchmarkScriptConfiguration configuration, string selectedCode)
    {
        var runDirectory = BenchmarkDirectoryPath.Combine(run.OperationId.ToString("N"));
        IIpcService? ipcService = null;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            ipcService = scope.ServiceProvider.GetRequiredService<IIpcService>();
            await PublishAsync(run, ipcService, "Preparing isolated benchmark project...", isUpdate: false);

            if (run.IsCancellationRequested)
            {
                await PublishFinalAsync(run, ipcService, "Benchmark cancelled.", []);
                return;
            }

            runDirectory.CreateIfNotExists();
            var harness = BenchmarkHarnessGenerator.Generate(configuration, selectedCode);
            await File.WriteAllTextAsync(runDirectory.CombineFilePath("BenchmarkHarness.csproj").Path, harness.ProjectFile);
            await File.WriteAllTextAsync(runDirectory.CombineFilePath("BenchmarkProgram.cs").Path, harness.ProgramFile);

            var dotNetExecutable = dotNetInfo.LocateDotNetExecutableForFramework(configuration.TargetFrameworkVersion)
                                   ?? dotNetInfo.LocateDotNetExecutableOrThrow();
            var startInfo = new ProcessStartInfo(dotNetExecutable)
            {
                WorkingDirectory = runDirectory.Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");

            using var process = new Process { StartInfo = startInfo };
            if (!run.TryStartProcess(process))
            {
                await PublishFinalAsync(run, ipcService, "Benchmark cancelled.", run.GetProgressLines());
                return;
            }

            try
            {
                var stdoutTask = PumpOutputAsync(run, ipcService, process.StandardOutput);
                var stderrTask = PumpOutputAsync(run, ipcService, process.StandardError);
                await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);

                if (run.IsCancellationRequested)
                {
                    await PublishFinalAsync(run, ipcService, "Benchmark cancelled.", run.GetProgressLines());
                }
                else if (process.ExitCode != 0)
                {
                    await PublishFinalAsync(run, ipcService, "Benchmark harness failed to build or run.", run.GetProgressLines());
                }
                else
                {
                    var reportPath = Directory.GetFiles(runDirectory.Path, "*-report-full.json", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (reportPath == null)
                    {
                        await PublishFinalAsync(run, ipcService, "Benchmark completed without a JSON report.", run.GetProgressLines());
                    }
                    else
                    {
                        var results = BenchmarkReportParser.Parse(await File.ReadAllTextAsync(reportPath));
                        await PublishFinalAsync(run, ipcService, BenchmarkHtml.Completed(results), bodyIsHtml: true);
                    }
                }
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        BenchmarkProcessTree.Kill(process);
                    }
                }
                finally
                {
                    run.ClearProcess(process);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Benchmark operation {OperationId} failed", run.OperationId);
            if (ipcService != null)
            {
                await PublishFinalAsync(run, ipcService, ex.Message, run.GetProgressLines());
            }
        }
        finally
        {
            try
            {
                run.KillProcessTree();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not stop benchmark process tree for {OperationId}", run.OperationId);
            }

            try
            {
                runDirectory.DeleteIfExists();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove benchmark artifacts for {OperationId}", run.OperationId);
            }

            lock (_runsLock)
            {
                _runs.Remove(run.OperationId);
            }
            run.Completion.TrySetResult(true);
        }
    }

    private async Task PumpOutputAsync(BenchmarkRun run, IIpcService ipcService, StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await PublishAsync(run, ipcService, line, isUpdate: true);
        }
    }

    private async Task PublishAsync(BenchmarkRun run, IIpcService ipcService, string line, bool isUpdate)
    {
        await run.OutputLock.WaitAsync();
        try
        {
            run.AddProgressLine(line);
            var body = BenchmarkHtml.Progress(run.GetProgressLines());
            await SendOutputAsync(run, ipcService, body, isUpdate);
        }
        finally
        {
            run.OutputLock.Release();
        }
    }

    private async Task PublishFinalAsync(
        BenchmarkRun run,
        IIpcService ipcService,
        string body,
        IReadOnlyCollection<string>? logLines = null,
        bool bodyIsHtml = false)
    {
        await run.OutputLock.WaitAsync();
        try
        {
            var html = bodyIsHtml ? body : BenchmarkHtml.Failure(body, logLines ?? []);
            await SendOutputAsync(run, ipcService, html, isUpdate: true);
        }
        finally
        {
            run.OutputLock.Release();
        }
    }

    private async Task SendOutputAsync(BenchmarkRun run, IIpcService ipcService, string html, bool isUpdate)
    {
        try
        {
            await ipcService.SendAsync(new ScriptOutputEmittedEvent(
                run.ScriptId,
                BenchmarkOutputFactory.Create(run.OperationId, html, isUpdate)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not emit benchmark output for operation {OperationId}", run.OperationId);
        }
    }

    private sealed class BenchmarkRun(Guid operationId, Guid scriptId)
    {
        private const int MaxProgressLines = 200;
        private readonly object _processLock = new();
        private readonly object _progressLock = new();
        private readonly List<string> _progressLines = [];
        private readonly CancellationTokenSource _cancellation = new();
        private Process? _process;

        public Guid OperationId { get; } = operationId;
        public Guid ScriptId { get; } = scriptId;
        public SemaphoreSlim OutputLock { get; } = new(1, 1);
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

        public bool TryStartProcess(Process process)
        {
            lock (_processLock)
            {
                if (_cancellation.IsCancellationRequested)
                {
                    return false;
                }

                if (!process.Start())
                {
                    throw new InvalidOperationException("Could not start the benchmark process.");
                }

                _process = process;
                return true;
            }
        }

        public void RequestCancellation() => _cancellation.Cancel();

        public void KillProcessTree()
        {
            lock (_processLock)
            {
                if (_process != null)
                {
                    BenchmarkProcessTree.Kill(_process);
                }
            }
        }

        public void ClearProcess(Process process)
        {
            lock (_processLock)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }
        }

        public void AddProgressLine(string line)
        {
            lock (_progressLock)
            {
                _progressLines.Add(line);
                if (_progressLines.Count > MaxProgressLines)
                {
                    _progressLines.RemoveAt(0);
                }
            }
        }

        public IReadOnlyCollection<string> GetProgressLines()
        {
            lock (_progressLock)
            {
                return _progressLines.ToArray();
            }
        }
    }
}

internal static class BenchmarkProcessTree
{
    internal static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
