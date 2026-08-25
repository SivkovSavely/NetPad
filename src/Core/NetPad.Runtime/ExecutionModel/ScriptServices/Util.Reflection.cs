using System.Dynamic;
using System.Globalization;
using System.Reflection;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    /// <summary>
    /// Wraps an object so that ALL of its instance members are accessible dynamically,
    /// regardless of visibility, including inherited private members.
    /// </summary>
    public static dynamic? Uncapsulate<T>(this T obj)
        => obj is null ? default : new UncapsulatedObject(obj);

    /// <summary>
    /// Wraps a type so that ALL of its static members are accessible dynamically, regardless of
    /// visibility. Constructors can be invoked via <c>wrapper.New(args)</c>.
    /// </summary>
    public static dynamic UncapsulateStatic(this Type type) => new UncapsulatedStatic(type);
}

internal static class ReflectionCoercion
{
    public static object? Coerce(Type targetType, object? value)
    {
        if (value == null) return null;

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var valueType = value.GetType();

        if (effectiveType.IsAssignableFrom(valueType)) return value;

        if (IsNumber(effectiveType) && IsNumber(valueType))
        {
            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        if (effectiveType.IsEnum)
        {
            if (value is string s) return Enum.Parse(effectiveType, s);
            if (IsNumber(valueType)) return Enum.ToObject(effectiveType, value);
        }

        return value;
    }

    public static bool IsNumber(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(byte) || underlying == typeof(sbyte) ||
               underlying == typeof(short) || underlying == typeof(ushort) ||
               underlying == typeof(int) || underlying == typeof(uint) ||
               underlying == typeof(long) || underlying == typeof(ulong) ||
               underlying == typeof(float) || underlying == typeof(double) ||
               underlying == typeof(decimal);
    }
}

/// <summary>Dynamic wrapper over one target object's instance members.</summary>
public sealed class UncapsulatedObject : DynamicObject
{
    private readonly object _target;

    internal UncapsulatedObject(object target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        TargetType = target.GetType();
    }

    public Type TargetType { get; }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        var member = ReflectionMember.Find(TargetType, binder.Name, isStatic: false);
        if (member == null)
        {
            result = null;
            return false;
        }

        result = Normalize(member.GetValue(_target));
        return true;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        var member = ReflectionMember.Find(TargetType, binder.Name, isStatic: false);
        if (member == null || !member.CanWrite)
        {
            return false;
        }

        member.SetValue(_target, ReflectionCoercion.Coerce(member.ValueType, value));
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        var method = MethodSelector.SelectMethod(
            TargetType,
            binder.Name,
            args ?? [],
            staticMembers: false);

        if (method == null)
        {
            result = null;
            return false;
        }

        var finalArgs = MethodSelector.BuildArguments(method, args ?? []);
        result = Normalize(method.MethodInfo.Invoke(_target, finalArgs));
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
    {
        var indexer = IndexerSelector.Select(TargetType, indexes, isStatic: false);
        if (indexer == null)
        {
            result = null;
            return false;
        }

        result = Normalize(indexer.GetValue(_target, indexes));
        return true;
    }

    public override bool TrySetIndex(SetIndexBinder binder, object?[] indexes, object? value)
    {
        var indexer = IndexerSelector.Select(TargetType, indexes, isStatic: false);
        if (indexer == null)
        {
            return false;
        }

        indexer.SetValue(_target, ReflectionCoercion.Coerce(indexer.PropertyType, value), indexes);
        return true;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
        => UncapsulatedStatic.EnumerateMembers(TargetType, isStatic: false).Select(m => m.Name).Distinct();

    private object? Normalize(object? value)
        => value is null or string or ValueType ? value : new UncapsulatedObject(value);
}

/// <summary>Dynamic wrapper over a type's static members plus private constructors.</summary>
public sealed class UncapsulatedStatic : DynamicObject
{
    public Type TargetType { get; }

    internal UncapsulatedStatic(Type type)
    {
        TargetType = type ?? throw new ArgumentNullException(nameof(type));
    }

    /// <summary>
    /// Invokes a constructor (including non-public ones) matching the supplied arguments.
    /// </summary>
    public object New(params object?[]? args)
    {
        args ??= [];

        var constructors = TargetType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(c => c.GetParameters().Length == args.Length ||
                        HasParamsArray(c.GetParameters(), args.Length))
            .ToList();

        var ctor = ConstructorSelector.Select(constructors, args);

        if (ctor == null)
        {
            throw new MissingMemberException(
                $"No constructor on {TargetType.Name} matches the supplied arguments.");
        }

        return Activator.CreateInstance(TargetType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            MethodSelector.BuildArguments(ctor, args),
            culture: null)!;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        var member = ReflectionMember.Find(TargetType, binder.Name, isStatic: true);
        if (member == null)
        {
            result = null;
            return false;
        }

        result = member.GetValue(null);
        return true;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        var member = ReflectionMember.Find(TargetType, binder.Name, isStatic: true);
        if (member == null || !member.CanWrite)
        {
            return false;
        }

        member.SetValue(null, ReflectionCoercion.Coerce(member.ValueType, value));
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        var method = MethodSelector.SelectMethod(
            TargetType,
            binder.Name,
            args ?? [],
            staticMembers: true);

        if (method == null)
        {
            result = null;
            return false;
        }

        var finalArgs = MethodSelector.BuildArguments(method, args ?? []);
        result = method.MethodInfo.Invoke(null, finalArgs);
        return true;
    }

    public override IEnumerable<string> GetDynamicMemberNames()
        => EnumerateMembers(TargetType, isStatic: true).Select(m => m.Name).Distinct();

    internal static IEnumerable<MemberInfo> EnumerateMembers(Type type, bool isStatic)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags | BindingFlags.GetField))
            {
                if (field.IsStatic == isStatic) yield return field;
            }

            foreach (var prop in current.GetProperties(flags))
            {
                var getter = prop.GetGetMethod(true);
                if (getter != null && getter.IsStatic == isStatic) yield return prop;
            }
        }
    }

    internal static bool HasParamsArray(ParameterInfo[] parameters, int argCount)
        => parameters.Length > 0 &&
           parameters[^1].GetCustomAttribute<ParamArrayAttribute>() != null &&
           parameters.Length - 1 <= argCount;
}

/// <summary>A field or property located on the most-derived declaring type.</summary>
internal sealed class ReflectionMember
{
    private ReflectionMember(FieldInfo? field, PropertyInfo? property)
    {
        Field = field;
        Property = property;
    }

    public FieldInfo? Field { get; }
    public PropertyInfo? Property { get; }

    public string Name => Field?.Name ?? Property!.Name;

    public Type ValueType => Field?.FieldType ?? Property!.PropertyType;

    public bool CanWrite => Field != null
        ? !Field.IsInitOnly && !Field.IsLiteral
        : Property!.GetSetMethod(true) != null;

    public object? GetValue(object? target)
        => Field != null ? Field.GetValue(target) : Property!.GetValue(target);

    public void SetValue(object? target, object? value)
    {
        if (Field != null)
        {
            Field.SetValue(target, value);
        }
        else
        {
            Property!.SetValue(target, value);
        }
    }

    public static ReflectionMember? Find(Type type, string name, bool isStatic)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        // Walk from the most-derived type down so shadowing resolves to the closest declaration.
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(name, flags);
            if (property?.GetGetMethod(true) is { } getter && getter.IsStatic == isStatic)
            {
                return new ReflectionMember(null, property);
            }

            var field = current.GetField(name, flags);
            if (field != null && field.IsStatic == isStatic)
            {
                return new ReflectionMember(field, null);
            }
        }

        return null;
    }
}

internal static class MethodSelector
{
    public record SelectedMethod(MethodInfo MethodInfo);

    public static SelectedMethod? SelectMethod(
        Type type,
        string name,
        object?[] args,
        bool staticMembers)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        List<MethodInfo> candidates = new();

        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var method in current.GetMethods(flags))
            {
                if (!method.Name.Equals(name, StringComparison.Ordinal)) continue;
                if (method.IsSpecialName) continue; // Property/event accessors are reached directly.
                if (method.IsStatic != staticMembers) continue;
                if (method.ContainsGenericParameters && !method.IsGenericMethodDefinition) continue;

                candidates.Add(method);
            }
        }

        var scored = new List<(MethodInfo Method, double Score)>();

        foreach (var method in candidates)
        {
            if (method.IsGenericMethodDefinition)
            {
                var genericScore = ScoreCandidate(
                    method, args, makeGeneric: true, out _, out var closedMethod);

                if (genericScore < double.MaxValue && closedMethod != null)
                {
                    scored.Add((closedMethod!, genericScore));
                }

                continue;
            }

            var openScore = ScoreCandidate(method, args, makeGeneric: false, out _, out _);
            if (openScore < double.MaxValue)
            {
                scored.Add((method, openScore));
            }
        }

        if (scored.Count == 0) return null;

        var best = scored.OrderBy(s => s.Score).First();

        // An ambiguous match exists when another distinct method has the identical best score.
        if (scored.Any(s => s.Score == best.Score && !SignatureEquals(s.Method, best.Method)))
        {
            throw new AmbiguousMatchException(
                $"More than one method '{name}' on {type.Name} matches the supplied arguments.");
        }

        return new SelectedMethod(best.Method);
    }

    private static bool SignatureEquals(MethodInfo a, MethodInfo b)
        => a.DeclaringType == b.DeclaringType &&
           a.ToString() == b.ToString();

    private static double ScoreCandidate(
        MethodInfo method,
        object?[] args,
        bool makeGeneric,
        out Type[]? genericArgs,
        out MethodInfo? closedMethod)
    {
        genericArgs = null;
        closedMethod = null;

        var parameters = method.GetParameters();

        MethodInfo effective = method;

        if (makeGeneric && method.IsGenericMethodDefinition)
        {
            var inferred = InferGenericArguments(method, parameters, args);
            if (inferred == null) return double.MaxValue;

            try
            {
                effective = method.MakeGenericMethod(inferred);
            }
            catch (ArgumentException)
            {
                return double.MaxValue;
            }

            genericArgs = inferred;
            closedMethod = effective;
            parameters = effective.GetParameters();
        }

        var direct = parameters.Length == args.Length;
        var paramsExpanded = !direct && parameters.Length > 0 &&
                             parameters[^1].GetCustomAttribute<ParamArrayAttribute>() != null &&
                             parameters.Length - 1 <= args.Length;

        if (!direct && !paramsExpanded) return double.MaxValue;

        double score = 0;

        for (var i = 0; i < (direct ? args.Length : parameters.Length - 1); i++)
        {
            score += ScoreArgument(parameters[i].ParameterType, args[i]);
            if (score >= double.MaxValue) return double.MaxValue;
        }

        if (paramsExpanded)
        {
            var elementType = parameters[^1].ParameterType.GetElementType()!;

            for (var i = parameters.Length - 1; i < args.Length; i++)
            {
                score += ScoreArgument(elementType, args[i]) + 0.5;
                if (score >= double.MaxValue) return double.MaxValue;
            }
        }

        return score;
    }

    private static Type[]? InferGenericArguments(MethodInfo method, ParameterInfo[] parameters, object?[] args)
    {
        var typeArgs = method.GetGenericArguments();
        var resolved = new Dictionary<Type, Type>();

        var count = Math.Min(parameters.Length, args.Length);

        for (var i = 0; i < count; i++)
        {
            var paramType = parameters[i].ParameterType;
            var argValue = args[i];

            if (argValue == null) continue;

            if (paramType.IsGenericParameter)
            {
                resolved[paramType] = argValue.GetType();
            }
            else if (paramType.IsArray && argValue.GetType().IsArray)
            {
                if (paramType.GetElementType()!.IsGenericParameter)
                {
                    resolved[paramType.GetElementType()!] =
                        argValue.GetType().GetElementType()!;
                }
            }
        }

        if (resolved.Count < typeArgs.Length) return null;

        try
        {
            return typeArgs.Select(t => resolved[t]).ToArray();
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static double ScoreArgument(Type parameterType, object? arg)
    {
        if (arg == null) return parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null
            ? double.MaxValue
            : 0;

        var argType = arg.GetType();

        if (parameterType == argType) return 0;
        if (parameterType.IsInstanceOfType(arg)) return 1;
        if (parameterType == typeof(object)) return 3;
        if (IsConvertibleNumber(parameterType, argType)) return 2;

        return double.MaxValue;
    }

    private static bool IsConvertibleNumber(Type target, Type source)
    {
        return IsNumber(target) && IsNumber(source);
    }

    private static bool IsNumber(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(byte) || underlying == typeof(sbyte) ||
               underlying == typeof(short) || underlying == typeof(ushort) ||
               underlying == typeof(int) || underlying == typeof(uint) ||
               underlying == typeof(long) || underlying == typeof(ulong) ||
               underlying == typeof(float) || underlying == typeof(double) ||
               underlying == typeof(decimal);
    }

    public static object?[] BuildArguments(ConstructorInfo constructor, object?[] args)
        => BuildArgumentsCore(constructor.GetParameters(), args);

    public static object?[] BuildArguments(SelectedMethod method, object?[] args)
        => BuildArgumentsCore(method.MethodInfo.GetParameters(), args);

    private static object?[] BuildArgumentsCore(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length == args.Length)
        {
            var exact = new object?[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                exact[i] = Coerce(parameters[i].ParameterType, args[i]);
            }
            return exact;
        }

        if (parameters.Length == 0 || args.Length < parameters.Length - 1)
        {
            throw new TargetParameterCountException();
        }

        var elementType = parameters[^1].ParameterType.GetElementType()!;
        var head = new object?[parameters.Length];
        var tail = Array.CreateInstance(elementType, args.Length - parameters.Length + 1);

        for (var i = 0; i < parameters.Length - 1; i++)
        {
            head[i] = Coerce(parameters[i].ParameterType, args[i]);
        }

        for (var i = parameters.Length - 1; i < args.Length; i++)
        {
            tail.SetValue(Coerce(elementType, args[i]), i - parameters.Length + 1);
        }

        head[^1] = tail;
        return head;
    }

    private static object? Coerce(Type parameterType, object? value)
    {
        if (value == null) return null;

        var targetType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        var valueType = value.GetType();

        if (targetType.IsAssignableFrom(valueType)) return value;

        if (IsNumber(targetType) && IsNumber(valueType))
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        if (targetType.IsEnum)
        {
            if (value is string s) return Enum.Parse(targetType, s);
            return Enum.ToObject(targetType, value);
        }

        return value;
    }
}

internal static class ConstructorSelector
{
    public static ConstructorInfo? Select(List<ConstructorInfo> candidates, object?[] args)
    {
        var scored = new List<(ConstructorInfo Ctor, double Score)>();

        foreach (var ctor in candidates)
        {
            var parameters = ctor.GetParameters();

            var direct = parameters.Length == args.Length;
            var paramsExpanded = !direct && UncapsulatedStatic.HasParamsArray(parameters, args.Length);

            if (!direct && !paramsExpanded) continue;

            double score = 0;
            var valid = true;

            void Score(Type parameterType, object? arg, double penalty)
            {
                if (arg == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                    {
                        valid = false;
                    }
                    return;
                }

                var argType = arg.GetType();

                if (parameterType == argType) score += penalty;
                else if (parameterType.IsInstanceOfType(arg)) score += penalty + 1;
                else if (parameterType == typeof(object)) score += penalty + 3;
                else valid = false;
            }

            for (var i = 0; i < (direct ? args.Length : parameters.Length - 1) && valid; i++)
            {
                Score(parameters[i].ParameterType, args[i], 0);
            }

            if (paramsExpanded && valid)
            {
                var elementType = parameters[^1].ParameterType.GetElementType()!;
                for (var i = parameters.Length - 1; i < args.Length && valid; i++)
                {
                    Score(elementType, args[i], 0.5);
                }
            }

            if (valid) scored.Add((ctor, score));
        }

        if (scored.Count == 0) return null;

        var best = scored.MinBy(s => s.Score);
        return best.Ctor;
    }
}

internal static class IndexerSelector
{
    public static PropertyInfo? Select(Type type, object?[] indexes, bool isStatic)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var prop in current.GetProperties(flags))
            {
                if (prop.Name != "Item") continue;
                if (!prop.CanRead) continue;

                var getter = prop.GetGetMethod(true);
                if (getter == null || getter.IsStatic != isStatic) continue;

                var indexTypes = getter.GetParameters().Select(p => p.ParameterType).ToArray();
                if (indexTypes.Length != indexes.Length) continue;

                var matches = true;
                for (var i = 0; i < indexes.Length; i++)
                {
                    if (indexes[i] == null) continue;
                    if (!indexTypes[i].IsInstanceOfType(indexes[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches) return prop;
            }
        }

        return null;
    }
}
