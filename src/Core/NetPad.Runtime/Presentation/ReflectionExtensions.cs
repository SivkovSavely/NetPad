namespace NetPad.Presentation;

/// <summary>
/// Script-visible extension methods for private reflection wrappers.
/// </summary>
public static class ReflectionExtensions
{
    public static dynamic? Uncapsulate<T>(this T obj)
        => ExecutionModel.ScriptServices.Util.Uncapsulate(obj);

    public static dynamic UncapsulateStatic(this Type type)
        => ExecutionModel.ScriptServices.Util.UncapsulateStatic(type);
}
