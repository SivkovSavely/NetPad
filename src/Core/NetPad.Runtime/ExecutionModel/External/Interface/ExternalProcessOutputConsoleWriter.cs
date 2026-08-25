using NetPad.Presentation;
using NetPad.Presentation.Console;

namespace NetPad.ExecutionModel.External.Interface;

/// <summary>
/// Writes output emitted by the script to the console as console-formatted text.
/// </summary>
internal class ExternalProcessOutputConsoleWriter(bool plainText, bool minimal) : IExternalProcessOutputWriter
{
    public Task WriteResultAsync(object? output, DumpOptions? options = null, string? outputId = null, bool isUpdate = false)
    {
        options ??= new DumpOptions();

        ConsolePresenter.Serialize(output, options.Title, plainText, minimal);

        return Task.CompletedTask;
    }

    public Task WriteSqlAsync(object? output, DumpOptions? options = null)
    {
        // Don't print SQL output
        return Task.CompletedTask;
    }
}
