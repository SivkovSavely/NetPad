using System.Collections.Concurrent;
using System.Reflection;

namespace NetPad.Presentation;

/// <summary>
/// Script-global dump customization. A transformer registered here is applied to every value
/// passing through the dump pipeline before any per-instance <c>ToDump()</c> hook runs.
/// Transformers compose in registration order (first registered runs first/outermost).
/// </summary>
public static class DumpTransformer
{
    private static readonly List<Func<object?, object?>> _transformers = [];
    private static readonly object _sync = new();

    /// <summary>
    /// Registers a script-global dump transformer. Returns a disposable that unregisters it.
    /// </summary>
    public static IDisposable Register(Func<object?, object?> transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);

        lock (_sync)
        {
            _transformers.Add(transformer);
        }

        return new Registration(transformer);
    }

    public static void Clear()
    {
        lock (_sync)
        {
            _transformers.Clear();
        }
    }

    public static bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _transformers.Count > 0;
            }
        }
    }

    internal static bool TryTransform(object? value, out object? transformed)
    {
        Func<object?, object?>[] transformers;

        lock (_sync)
        {
            if (_transformers.Count == 0)
            {
                transformed = value;
                return false;
            }

            transformers = [.. _transformers];
        }

        transformed = value;
        foreach (var transformer in transformers)
        {
            transformed = transformer(transformed);
        }

        return true;
    }

    private sealed class Registration(Func<object?, object?> transformer) : IDisposable
    {
        public void Dispose()
        {
            lock (_sync)
            {
                _transformers.Remove(transformer);
            }
        }
    }
}

/// <summary>
/// Discovers parameterless instance <c>ToDump()</c> hooks on types. Results are cached per type;
/// method lookup includes non-public methods and inherited declarations.
/// </summary>
internal static class ToDumpResolver
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> _hookCache = new();

    public static bool HasHook(Type type) => GetHook(type) != null;

    public static MethodInfo? GetHook(Type type)
    {
        return _hookCache.GetOrAdd(type, static t =>
        {
            if (t.IsPrimitive || t == typeof(string) || t.IsEnum || t.IsInterface || t == typeof(object))
            {
                return null;
            }

            for (var current = t; current != null && current != typeof(object); current = current.BaseType)
            {
                MethodInfo? hook = null;
                foreach (var method in current.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!method.IsGenericMethodDefinition
                        && method.Name == "ToDump"
                        && method.ReturnType != typeof(void)
                        && method.GetParameters().Length == 0)
                    {
                        hook = method;
                        break;
                    }
                }

                // The most derived declaration wins; stop at the first type in the chain that
                // declares the hook so overrides/shadows behave predictably.
                if (hook != null)
                {
                    return hook;
                }
            }

            return null;
        });
    }
}

/// <summary>
/// Tracks objects already processed by ToDump customization during one serialization traversal so
/// transformations are invoked at most once per object and cycles terminate. The traversal is a
/// synchronous call stack, so thread-static state is sufficient.
/// </summary>
internal static class ToDumpTraversal
{
    [ThreadStatic] private static HashSet<object>? _processed;
    [ThreadStatic] private static int _depth;

    /// <summary>
    /// Starts a traversal if one is not already running on this thread (nested calls, e.g.
    /// composition converters re-entering <see cref="Html.HtmlPresenter"/>, join the outer
    /// traversal). Returns true when this call started the outermost traversal.
    /// </summary>
    public static bool BeginIfNotStarted()
    {
        if (_depth++ == 0)
        {
            _processed = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return true;
        }

        return false;
    }

    public static void End()
    {
        if (_depth > 0 && --_depth == 0)
        {
            _processed = null;
        }
    }

    public static bool WasProcessed(object obj) => _processed?.Contains(obj) == true;

    public static void MarkProcessed(object obj) => _processed?.Add(obj);
}
