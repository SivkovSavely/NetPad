using NetPad.Presentation;

namespace NetPad.ExecutionModel.External.Interface;

internal interface IExternalProcessOutputWriter
{
    Task WriteResultAsync(object? output, DumpOptions? options = null, string? outputId = null, bool isUpdate = false);
    Task WriteSqlAsync(object? output, DumpOptions? options = null);
}
