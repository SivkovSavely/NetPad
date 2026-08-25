using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using NetPad.Assemblies;
using NetPad.ExecutionModel;
using NetPad.ExecutionModel.ClientServer;
using NetPad.ExecutionModel.ClientServer.Messages;
using NetPad.ExecutionModel.ScriptServices;
using NetPad.IO;
using NetPad.IO.IPC.Stdio;
using NetPad.Presentation;
using NetPad.Utilities;

namespace NetPad.Apps.ScriptHost;

public class ScriptRunner
{
    private readonly StdioIpcGateway _ipcGateway;
    private readonly HashSet<string> _scriptHostLoadedAssemblies = new();
    private TaskCompletionSource<string?>? _userInputRequest;
    private readonly JsEvalBridge _jsEvalBridge;
    private bool _firstRun = true;

    public ScriptRunner(StdioIpcGateway ipcGateway)
    {
        _ipcGateway = ipcGateway;
        DumpExtension.UseSink(ClientServerDumpSink.Instance);

        // Allows scripts to request other scripts to be opened and run in the parent app (Util.Run).
        Util.OnRequestRunScript = path => _ipcGateway.Send(new RunScriptFromPathMessage(path));

        // JavaScript evaluation requests are relayed through the parent app into the script's results view.
        _jsEvalBridge = new JsEvalBridge(request => _ipcGateway.Send(request));
        Util.OnRequestJsEval = (code, timeoutMs) => _jsEvalBridge.EvaluateAsync(code, timeoutMs);
        Util.OnResultHostCommand = (command, payloadJson) =>
            _ipcGateway.Send(new ResultHostCommandMessage(command, payloadJson));
        Util.OnHtmlHeadChanged = entries =>
            _ipcGateway.Send(new ScriptHtmlHeadMessage(entries));
    }

    public void Run(RunScriptMessage message)
    {
        // Fresh cancellation token and cleared interactive registrations for each run.
        Util.BeginInteractiveRunScope();

        Util.Stopwatch.Reset();

        Util.SetUserScript(new UserScript(
            message.ScriptId,
            message.ScriptName,
            message.ScriptFilePath,
            message.IsDirty
        ));

        ClientServerDumpSink.Instance.RedirectStdIO(
            str =>
            {
                _ipcGateway.Send(new ScriptOutputMessage(str));
                return Task.CompletedTask;
            },
            () =>
            {
                _userInputRequest = new TaskCompletionSource<string?>();
                _ipcGateway.Send(new RequestUserInputMessage(Util.TakeNextUserInputMasked()));
                return _userInputRequest.Task.Result;
            }
        );

        if (_firstRun)
        {
            StartForwardingMemCacheItemInfoChanges();
            _firstRun = false;
        }

        try
        {
            // Load script-host assemblies into default AssemblyLoadContext
            var scriptHostAssembliesToLoad = Directory.GetFiles(message.ScriptHostDepDirPath)
                .Where(f => Path.GetExtension(f).EqualsIgnoreCase(".dll") && !_scriptHostLoadedAssemblies.Contains(f));

            foreach (var file in scriptHostAssembliesToLoad)
            {
                Try.Run(() => Assembly.LoadFrom(file));
                _scriptHostLoadedAssemblies.Add(file);
            }

            Execute(message.ScriptAssemblyPath, message.ProbingPaths);

            // While the script holds Util.KeepRunning leases, stay alive so its callbacks remain usable.
            if (KeepRunningManager.HasActiveLeases)
            {
                KeepRunningManager.WaitUntilReleasedAsync(Util.QueryCancelToken).GetAwaiter().GetResult();
            }

            var result = Util.SoftCancellationRequested
                ? RunResult.RunCancelled()
                : RunResult.Success(Util.Stopwatch.ElapsedMilliseconds);

            _ipcGateway.Send(new ScriptRunCompleteMessage(
                result,
                Util.RestartHostOnEveryRun
            ));
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            HandleRunException(e.InnerException);
        }
        catch (Exception e)
        {
            HandleRunException(e);
        }
        finally
        {
            Util.EndInteractiveRunScope();
            GcUtil.CollectAndWait();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(string scriptAssemblyPath, string[] probingPaths)
    {
        using var assemblyLoader = new UnloadableAssemblyLoadContext(scriptAssemblyPath);

        assemblyLoader.UseProbing(probingPaths, [IsNotAlreadyLoadedInDefaultContext]);

        var assembly = assemblyLoader.LoadFromAssemblyPath(scriptAssemblyPath);

        Util.Stopwatch.Restart();

        assembly.EntryPoint!.Invoke(null, [Array.Empty<string>()]);

        Util.Stopwatch.Stop();
    }

    /// <summary>
    /// Prevents loading assemblies from probing paths if they are already loaded in the default
    /// AssemblyLoadContext. Loading the same assembly in both the custom ALC and the default ALC
    /// creates type identity splits that cause TypeLoadExceptions.
    /// </summary>
    private static bool IsNotAlreadyLoadedInDefaultContext(FilePath filePath, AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
        {
            return true;
        }

        foreach (var loadedAssembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loadedAssembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void HandleRunException(Exception exception)
    {
        Util.Stopwatch.Stop();
        _ipcGateway.Send(new ScriptRunCompleteMessage(
            RunResult.ScriptCompletionFailure(Util.Stopwatch.ElapsedMilliseconds),
            Util.RestartHostOnEveryRun,
            exception.ToString()
        ));
    }

    public void ReceiveUserInput(ReceiveUserInputMessage message)
    {
        _userInputRequest?.TrySetResult(message.Input);
    }

    public void ExpandOutput(ExpandOutputMessage message)
    {
        Util.ExpandOnDemand(message.OutputId);
    }

    public void InvokeScriptAction(InvokeScriptActionMessage message)
    {
        Util.InvokeScriptAction(message.ActionId);
    }

    public void RequestSoftCancellation(CancelScriptMessage _)
    {
        Util.RequestCooperativeCancellation();
    }

    public void ReceiveJsEvalResult(JsEvalResultMessage message)
    {
        _jsEvalBridge.Receive(message);
    }

    private void StartForwardingMemCacheItemInfoChanges()
    {
        var debounced = DelegateUtil.Debounce(
            () => _ipcGateway.Send(new MemCacheItemInfoChangedMessage(Util.Cache.GetItemInfos())),
            100);

        Util.Cache.MemCacheItemInfoChanged += (_, _) => debounced();
    }

    public static void DumpMemCacheItem(DumpMemCacheItemMessage message)
    {
        if (Util.Cache.TryGet(message.Key, out var value))
        {
            Util.Dump(value, new DumpOptions("Cache Key: " + message.Key)
            {
                Order = 0
            });
        }
    }

    public static void DeleteMemCacheItem(DeleteMemCacheItemMessage message)
    {
        Util.Cache.Remove(message.Key);
    }

    public static void ClearMemCache(ClearMemCacheMessage _)
    {
        Util.Cache.Clear();
    }
}
