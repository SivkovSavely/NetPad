using NetPad.Presentation;

namespace NetPad.ExecutionModel.ClientServer;

public interface IClientServerProcessOutputWriter
{
    Task WriteResultAsync(object? output, DumpOptions? options = null);
    Task WriteResultAsync(object? output, DumpOptions? options, string? outputId, bool isUpdate)
        => WriteResultAsync(output, options);
    Task WriteSqlAsync(object? output, DumpOptions? options = null);
}
