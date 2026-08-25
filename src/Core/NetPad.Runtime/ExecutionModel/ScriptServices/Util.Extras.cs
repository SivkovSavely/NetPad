using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NetPad.ExecutionModel.ClientServer;
using NetPad.Media;
using NetPad.Presentation;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    private static readonly ConcurrentDictionary<string, OnDemandValue> OnDemandRegistry = new();

    /// <summary>
    /// Invoked when a running script requests another script to be opened and run (<see cref="Run"/>).
    /// Wired up by the client-server execution model; <see langword="null"/> elsewhere.
    /// </summary>
    internal static Action<string>? OnRequestRunScript { get; set; }

    /// <summary>
    /// The file path of the current script/query, if saved to disk.
    /// </summary>
    public static string? CurrentQueryPath => Script.FilePath;

    /// <summary>
    /// Writes faint gray metadata-style text to the results pane.
    /// </summary>
    public static void Metatext(string text)
    {
        text.Dump(css: "metatext");
    }

    /// <summary>
    /// Writes text to the results pane with a custom background and/or foreground color.
    /// Colors are any valid CSS color (name, hex, rgb(), ...).
    /// </summary>
    public static void Highlight(string text, string? backgroundColor = null, string? foregroundColor = null)
    {
        DumpExtension.Sink.ResultWrite(new HighlightedText(text, backgroundColor, foregroundColor), new DumpOptions());
    }

    /// <summary>
    /// Wraps a value with an inline CSS style that is applied when the value is dumped.
    /// </summary>
    public static StyledValue WithStyle(object? value, string style)
    {
        return new StyledValue(value, style);
    }

    /// <summary>
    /// Creates an image that is rendered in the results pane when dumped.
    /// </summary>
    /// <param name="pathOrUri">An image file path or image URI.</param>
    public static Media.Image Image(string pathOrUri)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(pathOrUri));
        }

        return File.Exists(pathOrUri)
            ? new Media.Image(pathOrUri)
            : new Media.Image(new Uri(pathOrUri));
    }

    /// <summary>
    /// Creates an image from raw bytes that is rendered in the results pane when dumped.
    /// </summary>
    /// <param name="bytes">The raw image bytes.</param>
    /// <param name="mimeType">The image MIME type, e.g. <c>image/png</c>.</param>
    public static Media.Image Image(byte[] bytes, string mimeType = "image/png")
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return MediaFile<Media.Image>.FromBytes(bytes, mimeType);
    }

    /// <summary>
    /// Gets the file path of the assembly that contains the given type.
    /// </summary>
    public static string GetAsmPath(Assembly assembly) => assembly.Location;

    /// <summary>
    /// Gets the file path of the assembly that contains the given type.
    /// </summary>
    public static string GetAsmPath(Type type) => type.Assembly.Location;

    /// <summary>
    /// Gets the file path of the assembly that contains <typeparamref name="T"/>.
    /// </summary>
    public static string GetAsmPath<T>() => typeof(T).Assembly.Location;

    /// <summary>
    /// Executes a command line in the system shell (/bin/bash on Unix, cmd.exe on Windows),
    /// capturing standard output and error.
    /// </summary>
    /// <param name="commandLine">The command line to execute.</param>
    /// <param name="workingDirectory">The working directory for the command. Defaults to the current directory.</param>
    /// <param name="echoOutput">If true, output is echoed live to this script's output.</param>
    public static CmdResult Cmd(string commandLine, string? workingDirectory = null, bool echoOutput = true)
    {
        return RunShell(commandLine, workingDirectory, echoOutput).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes a program with arguments, capturing standard output and error.
    /// </summary>
    public static CmdResult Cmd(string fileName, string args, string? workingDirectory = null, bool echoOutput = true)
    {
        return RunShell($"{fileName} {args}", workingDirectory, echoOutput).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes a command line in the system shell asynchronously, capturing standard output and error.
    /// </summary>
    public static Task<CmdResult> CmdAsync(string commandLine, string? workingDirectory = null, bool echoOutput = true)
    {
        return RunShell(commandLine, workingDirectory, echoOutput);
    }

    /// <summary>
    /// Rotates rows into columns: returns a dictionary keyed by property name whose values are
    /// the row-by-row values of that property.
    /// </summary>
    public static Dictionary<string, List<object?>> Pivot<T>(IEnumerable<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        var result = properties.ToDictionary(p => p.Name, _ => new List<object?>());

        foreach (var row in rows)
        {
            foreach (var property in properties)
            {
                result[property.Name].Add(property.GetValue(row));
            }
        }

        return result;
    }

    /// <summary>
    /// Rotates rows into columns. Item properties are inferred from the first row's runtime type.
    /// </summary>
    public static Dictionary<string, List<object?>> Pivot(IEnumerable rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var items = rows.Cast<object?>().ToArray();
        var itemType = items.FirstOrDefault()?.GetType();

        if (itemType == null)
        {
            return [];
        }

        var properties = itemType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        var result = properties.ToDictionary(p => p.Name, _ => new List<object?>());

        foreach (var item in items.Where(i => i?.GetType() == itemType))
        {
            foreach (var property in properties)
            {
                result[property.Name].Add(property.GetValue(item));
            }
        }

        return result;
    }

    /// <summary>
    /// Converts anonymous types into <see cref="ExpandoObject"/>s recursively so they can be
    /// accessed dynamically. Nested anonymous types and collections are converted too; other
    /// objects are copied shallowly as-is.
    /// </summary>
    public static ExpandoObject ToExpando(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        return (ExpandoObject)ConvertToExpando(obj, [])!;
    }

    /// <summary>
    /// Reads a line of input from the user, optionally displaying a prompt.
    /// </summary>
    public static string? ReadLine(string prompt = "")
    {
        if (prompt.Length > 0)
        {
            Console.Write(prompt);
        }

        return Console.ReadLine();
    }

    /// <summary>
    /// Signals the debugger to pause execution at this point.
    /// </summary>
    public static void Break()
    {
        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
        else
        {
            Debugger.Launch();
        }
    }

    /// <summary>
    /// Registers a lazy value that is evaluated only when the user clicks its placeholder in the
    /// results pane. In non-interactive sessions the value is evaluated eagerly.
    /// </summary>
    /// <param name="title">The text displayed on the clickable placeholder.</param>
    /// <param name="factory">A callback producing the value when the placeholder is clicked.</param>
    public static OnDemandValue OnDemand<T>(string title, Func<T?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return RegisterOnDemand(title, () => (object?)factory());
    }

    /// <summary>
    /// Registers a lazy value that is evaluated only when the user clicks its placeholder in the
    /// results pane. In non-interactive sessions the value is evaluated eagerly.
    /// </summary>
    public static OnDemandValue OnDemand(string title, Func<object?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return RegisterOnDemand(title, factory);
    }

    /// <summary>
    /// Lays out items side-by-side horizontally when dumped.
    /// </summary>
    /// <param name="items">The items to lay out.</param>
    public static HorizontalRun HorizontalRun(params object?[] items)
    {
        return new HorizontalRun(true, items);
    }

    /// <summary>
    /// Lays out items side-by-side horizontally when dumped.
    /// </summary>
    /// <param name="wrapIfTooWide">Whether items wrap to additional lines if they exceed the available width.</param>
    /// <param name="items">The items to lay out.</param>
    public static HorizontalRun HorizontalRun(bool wrapIfTooWide, params object?[] items)
    {
        return new HorizontalRun(wrapIfTooWide, items);
    }

    /// <summary>
    /// Opens and runs another script. Only supported when running inside the NetPad app.
    /// </summary>
    /// <param name="scriptPath">The path of the script (.netpad) file to run.</param>
    public static void Run(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(scriptPath));
        }

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Script file not found.", scriptPath);
        }

        var sender = OnRequestRunScript
                     ?? throw new InvalidOperationException(
                         "Util.Run is only supported when the script runs inside the NetPad app.");

        sender(Path.GetFullPath(scriptPath));
    }

    private static OnDemandValue RegisterOnDemand(string title, Func<object?> factory)
    {
        if (!ProgressBar_IsInteractiveSink())
        {
            // Non-interactive session: evaluate eagerly.
            factory().Dump(title);
            return new OnDemandValue(null, title, factory);
        }

        var id = "OD" + Guid.NewGuid().ToString("N");
        var onDemand = new OnDemandValue(id, title, factory);
        OnDemandRegistry[id] = onDemand;
        return onDemand;
    }

    internal static void ClearOnDemandRegistry()
    {
        OnDemandRegistry.Clear();
    }

    internal static void ExpandOnDemand(string outputId)
    {
        if (!OnDemandRegistry.TryGetValue(outputId, out var onDemand))
        {
            return;
        }

        object? value;

        try
        {
            value = onDemand.Factory();
        }
        catch (Exception ex)
        {
            DumpExtension.Sink.ResultWrite(
                $"Error evaluating on-demand value '{onDemand.Title}': {ex.Message}",
                new DumpOptions(),
                outputId,
                true);
            return;
        }

        DumpExtension.Sink.ResultWrite(value, null, outputId, true);
    }

    private static bool ProgressBar_IsInteractiveSink() => ProgressBar.IsInteractiveSink();

    private static async Task<CmdResult> RunShell(string commandLine, string? workingDirectory, bool echoOutput)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? System.Environment.CurrentDirectory,
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c {commandLine}";
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(commandLine);
        }

        var output = new StringBuilder();
        var errors = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        void OnOutputDataReceived(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (output) output.AppendLine(e.Data);
            if (echoOutput) Console.Out.WriteLine(e.Data);
        }

        void OnErrorDataReceived(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (errors) errors.AppendLine(e.Data);
            if (echoOutput) Console.Error.WriteLine(e.Data);
        }

        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        lock (output) lock (errors)
            return new CmdResult(process.ExitCode, output.ToString(), errors.ToString());
    }

    private static object? ConvertToExpando(object? obj, HashSet<object> visited)
    {
        if (obj == null || obj.GetType().IsPrimitive || obj is string || obj.GetType().IsEnum)
        {
            return obj;
        }

        if (!visited.Add(obj))
        {
            // Cycle detected; stop recursing.
            return null;
        }

        var type = obj.GetType();

        if (IsAnonymousType(type))
        {
            var expando = new ExpandoObject();
            var dictionary = (IDictionary<string, object?>)expando;

            foreach (var property in type
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            {
                dictionary[property.Name] = ConvertToExpando(property.GetValue(obj), visited);
            }

            return expando;
        }

        if (obj is IEnumerable enumerable)
        {
            var list = new List<object?>();

            foreach (var item in enumerable)
            {
                list.Add(ConvertToExpando(item, visited));
            }

            return list;
        }

        return obj;
    }

    private static bool IsAnonymousType(Type type)
    {
        return type.IsGenericType
               && type.IsSealed
               && type.Namespace == null
               && type.IsDefined(typeof(CompilerGeneratedAttribute), false)
               && (type.Name.Contains("AnonymousType", StringComparison.Ordinal) ||
                   type.Name.StartsWith("<>", StringComparison.Ordinal));
    }
}

/// <summary>
/// The result of a command executed with <see cref="Util.Cmd(string,string?,bool)"/>.
/// </summary>
/// <param name="ExitCode">The exit code of the command.</param>
/// <param name="Output">Everything written to standard output.</param>
/// <param name="Errors">Everything written to standard error.</param>
public sealed record CmdResult(int ExitCode, string Output, string Errors);
