namespace NetPad.Presentation;

/// <summary>
/// Carries the active per-dump <see cref="DumpOptions"/> through the synchronous serialization
/// traversal so gating converters (row limits, member filters, format strings) can honor them
/// without threading options through every O2Html converter signature.
/// </summary>
internal static class PresentationSerializationContext
{
    [ThreadStatic] private static DumpOptions? _current;

    public static DumpOptions? Current => _current;

    public static IDisposable Push(DumpOptions options)
    {
        var previous = _current;
        _current = options;
        return new Scope(previous);
    }

    private sealed class Scope(DumpOptions? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}
