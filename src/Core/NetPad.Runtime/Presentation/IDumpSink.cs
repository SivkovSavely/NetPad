namespace NetPad.Presentation;

public interface IDumpSink
{
    void ResultWrite<T>(T? o, DumpOptions? options = null);
    void ResultWrite<T>(T? o, DumpOptions? options, string? outputId, bool isUpdate)
        => ResultWrite(o, options);
    void SqlWrite<T>(T? o, DumpOptions? options = null);
}
