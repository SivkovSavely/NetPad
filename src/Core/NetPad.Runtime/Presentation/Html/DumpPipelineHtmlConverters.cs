using System.Collections;
using System.Linq;
using System.Reflection;
using O2Html;
using O2Html.Common;
using O2Html.Converters;
using O2Html.Dom;
using O2Html.Dom.Elements;

namespace NetPad.Presentation.Html;

/// <summary>
/// Applies LINQPad-style ToDump customization during serialization: script-global transformers
/// first (see <see cref="DumpTransformer"/>), then a parameterless instance <c>ToDump()</c> hook
/// discovered on the value's type (any visibility, including inherited declarations).
/// The transformed result is serialized through the standard pipeline with this converter removed,
/// guarded by traversal state so each object is transformed at most once per dump and cycles
/// terminate. Errors thrown by user hooks render as an error node without aborting the dump.
/// </summary>
public class ToDumpHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        // The global transformer applies to every dumped value; instance hooks are type-based.
        return DumpTransformer.IsActive || ToDumpResolver.HasHook(type);
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        if (obj is null)
        {
            return new Null(htmlSerializer.SerializerOptions.CssClasses.Null);
        }

        var fallback = CreateFallbackSerializer(htmlSerializer);

        if (ToDumpTraversal.WasProcessed(obj))
        {
            return fallback.Serialize(obj, type, serializationScope);
        }

        // Mark the source up-front so self-returning/cyclic chains terminate inside Apply.
        ToDumpTraversal.MarkProcessed(obj);

        object? transformed;
        try
        {
            transformed = Apply(obj);
        }
        catch (Exception ex)
        {
            return BuildErrorNode(type, ex);
        }

        if (!ReferenceEquals(transformed, obj))
        {
            ToDumpTraversal.MarkProcessed(transformed!);
        }

        return fallback.Serialize(transformed, transformed!.GetType(), serializationScope);
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        var td = tr.AddAndGetElement("td").AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue);
        var fallback = CreateFallbackSerializer(htmlSerializer);

        if (obj is null || ToDumpTraversal.WasProcessed(obj))
        {
            td.AddChild(fallback.Serialize(obj, type, serializationScope));
            return;
        }

        ToDumpTraversal.MarkProcessed(obj);

        object? transformed;
        try
        {
            transformed = Apply(obj);
        }
        catch (Exception ex)
        {
            td.AddChild(BuildErrorNode(type, ex));
            return;
        }

        if (!ReferenceEquals(transformed, obj))
        {
            ToDumpTraversal.MarkProcessed(transformed!);
        }

        td.AddChild(fallback.Serialize(transformed, transformed!.GetType(), serializationScope));
    }

    private static object? Apply(object obj)
    {
        DumpTransformer.TryTransform(obj, out var current);

        if (current is null)
        {
            return null;
        }

        // Follow instance hooks across successive transformation results so customization chains
        // compose. A local reference set stops cycles that reuse instances; the guard bounds
        // pathological hooks that keep producing fresh hook-bearing instances.
        var chained = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var guard = 0;

        while (guard++ < 16)
        {
            var hook = ToDumpResolver.GetHook(current!.GetType());

            if (hook == null || !chained.Add(current))
            {
                break;
            }

            try
            {
                current = hook.Invoke(current, []);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static Element BuildErrorNode(Type type, Exception ex)
    {
        var group = new Element("div").AddClass("group").AddClass("error");
        group.AddEscapedText($"Error invoking ToDump() on {type.Name}: {ex.Message}");
        return group;
    }

    internal static HtmlSerializer CreateFallbackSerializer(HtmlSerializer serializer)
    {
        var sourceOptions = serializer.SerializerOptions;

        var options = new HtmlSerializerOptions
        {
            ReferenceLoopHandling = sourceOptions.ReferenceLoopHandling,
            DoNotSerializeNonRootEmptyCollections = sourceOptions.DoNotSerializeNonRootEmptyCollections,
            MaxCollectionSerializeLength = sourceOptions.MaxCollectionSerializeLength,
            MaxDepth = sourceOptions.MaxDepth,
        };

        foreach (var converter in sourceOptions.Converters)
        {
            if (converter is ToDumpHtmlConverter) continue;
            options.Converters.Add(converter);
        }

        // CSS class configuration is immutable during serialization; share it.
        CopyCssClasses(sourceOptions.CssClasses, options.CssClasses);

        return new HtmlSerializer(options);
    }

    private static void CopyCssClasses(CssClasses source, CssClasses target)
    {
        target.Null = source.Null;
        target.PropertyName = source.PropertyName;
        target.PropertyValue = source.PropertyValue;
        target.EmptyCollection = source.EmptyCollection;
        target.CyclicReference = source.CyclicReference;
        target.MaxDepthReached = source.MaxDepthReached;
        target.TableInfoHeader = source.TableInfoHeader;
        target.TableDataHeader = source.TableDataHeader;
    }
}

/// <summary>
/// Filters rendered object members according to the active dump's IncludeMembers/ExcludeMembers
/// options. Inactive unless those options are set for the current dump.
/// </summary>
public class MemberFilteredObjectHtmlConverter : ObjectHtmlConverter
{
    public override bool CanConvert(Type type)
    {
        var options = PresentationSerializationContext.Current;
        if (options?.IncludeMembers == null && options?.ExcludeMembers == null)
        {
            return false;
        }

        return HtmlSerializer.GetTypeCategory(type) == TypeCategory.SingleObject;
    }

    protected override PropertyInfo[] GetReadableProperties(HtmlSerializer htmlSerializer, Type type)
    {
        return Filter(HtmlSerializer.GetReadableProperties(type));
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        foreach (var property in Filter(HtmlSerializer.GetReadableProperties(type)))
        {
            object? value;
            try
            {
                value = property.GetValue(obj);
            }
            catch
            {
                value = string.Empty;
            }

            var propertyType = value?.GetType() ?? property.PropertyType;

            tr.AddAndGetElement("td")
                .AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue)
                .AddChild(htmlSerializer.Serialize(value, propertyType, serializationScope));
        }
    }

    private static PropertyInfo[] Filter(PropertyInfo[] properties)
    {
        var options = PresentationSerializationContext.Current;

        var include = options?.IncludeMembers;
        if (include is { Count: > 0 })
        {
            return properties.Where(p => include.Contains(p.Name)).ToArray();
        }

        var exclude = options?.ExcludeMembers;
        if (exclude is { Count: > 0 })
        {
            return properties.Where(p => !exclude.Contains(p.Name)).ToArray();
        }

        return properties;
    }
}

/// <summary>
/// Applies per-dump collection options: MaxRows row limiting and member-filtered column headers
/// for tables of objects (cells are filtered by <see cref="MemberFilteredObjectHtmlConverter"/>).
/// Inactive unless one of those options is set for the current dump.
/// </summary>
public class DumpOptionsCollectionHtmlConverter : CollectionHtmlConverter
{
    public override bool CanConvert(Type type)
    {
        var options = PresentationSerializationContext.Current;

        var applies =
            options?.MaxRows is > 0 ||
            options?.IncludeMembers is { Count: > 0 } ||
            options?.ExcludeMembers is { Count: > 0 };

        if (!applies)
        {
            return false;
        }

        return HtmlSerializer.GetTypeCategory(type) == TypeCategory.Collection;
    }

    protected override (Node node, int? collectionLength) Convert<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        var cssClasses = htmlSerializer.SerializerOptions.CssClasses;

        Type elementType = HtmlSerializer.GetCollectionElementType(type) ?? typeof(object);

        IEnumerable enumerable = obj as IEnumerable
                                 ?? throw new HtmlSerializationException($"Value of type {obj!.GetType()} is not an {nameof(IEnumerable)}.");

        var maxRows = PresentationSerializationContext.Current?.MaxRows;
        if (maxRows is > 0)
        {
            enumerable = new TakeEnumerable(enumerable, (int)Math.Min(maxRows.Value, int.MaxValue));
        }

        var table = new Table();

        var enumerationResult = Enumerate.Max(
            enumerable,
            htmlSerializer.SerializerOptions.MaxCollectionSerializeLength,
            (item, _) =>
            {
                var tr = table.Body.AddAndGetElement("tr");

                htmlSerializer.SerializeWithinTableRow(tr, item, elementType, serializationScope);

                if (!tr.Children.Any()) table.Body.RemoveChild(tr);
            });

        string headerRowText = GetHeaderRowText(
            enumerable,
            type,
            enumerationResult.ItemsProcessed,
            enumerationResult.CollectionLengthExceedsMax);

        if (HtmlSerializer.GetTypeCategory(elementType) == TypeCategory.SingleObject)
        {
            // Mirror the default converter's header layout, but with the ambient member filter
            // applied so headers stay aligned with the filtered row cells.
            var properties = FilteredProperties(elementType);

            if (properties.Any())
            {
                foreach (var property in properties)
                {
                    table.Head
                        .AddAndGetHeading(property.Name, property.PropertyType.GetReadableName(true))
                        .AddClass(cssClasses.PropertyName);
                }

                table.Head.ChildElements.Single().AddClass(cssClasses.TableDataHeader);
            }

            var infoHeaderRow = table.Head.InsertAndGetChild(0, new Element("tr"));

            infoHeaderRow
                .AddClass(cssClasses.TableInfoHeader)
                .SetTitle(type.GetReadableName(true))
                .AddAndGetElement("th")
                .SetAttribute("colspan", properties.Length.ToString())
                .AddEscapedText(headerRowText);
        }
        else
        {
            table.Head.AddHeading(headerRowText);
            table.Head.ChildElements.Single().AddClass(cssClasses.TableInfoHeader);
        }

        return (table, enumerationResult.ItemsProcessed);
    }

    private static PropertyInfo[] FilteredProperties(Type elementType)
    {
        var properties = HtmlSerializer.GetReadableProperties(elementType);
        var options = PresentationSerializationContext.Current;

        var include = options?.IncludeMembers;
        if (include is { Count: > 0 })
        {
            return properties.Where(p => include.Contains(p.Name)).ToArray();
        }

        var exclude = options?.ExcludeMembers;
        if (exclude is { Count: > 0 })
        {
            return properties.Where(p => !exclude.Contains(p.Name)).ToArray();
        }

        return properties;
    }

    private sealed class TakeEnumerable(IEnumerable inner, int take) : IEnumerable
    {
        public IEnumerator GetEnumerator() => new TakeEnumerator(inner.GetEnumerator(), take);
    }

    private sealed class TakeEnumerator(IEnumerator inner, int remaining) : IEnumerator
    {
        public bool MoveNext()
        {
            if (remaining <= 0) return false;
            remaining--;
            return inner.MoveNext();
        }

        public void Reset() => throw new NotSupportedException();

        public object? Current => inner.Current;

        object IEnumerator.Current => Current!;
    }
}

/// <summary>
/// Renders values whose declared type has a format string configured through the active dump's
/// FormatStrings option. Inactive unless a matching entry exists.
/// </summary>
public class FormattedValueHtmlConverter : HtmlConverter
{
    public override bool CanConvert(Type type)
    {
        return FindFormat(PresentationSerializationContext.Current, type) != null
               && HtmlSerializer.GetTypeCategory(type) == TypeCategory.DotNetTypeWithStringRepresentation;
    }

    public override Node WriteHtml<T>(T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        var format = FindFormat(PresentationSerializationContext.Current, type);

        string? text = null;

        if (format != null)
        {
            try
            {
                text = obj switch
                {
                    IFormattable formattable => formattable.ToString(format, null),
                    _ => obj?.ToString(),
                };
            }
            catch (FormatException)
            {
                text = null;
            }
        }

        text ??= obj?.ToString();

        var span = new Element("span").AddClass("text");
        span.AddEscapedText(text ?? string.Empty);
        return span;
    }

    public override void WriteHtmlWithinTableRow<T>(Element tr, T obj, Type type, SerializationScope serializationScope, HtmlSerializer htmlSerializer)
    {
        tr.AddAndGetElement("td")
            .AddClass(htmlSerializer.SerializerOptions.CssClasses.PropertyValue)
            .AddChild(WriteHtml(obj, type, serializationScope, htmlSerializer));
    }

    private static string? FindFormat(DumpOptions? options, Type type)
    {
        var formats = options?.FormatStrings;
        if (formats == null || formats.Count == 0)
        {
            return null;
        }

        if (formats.TryGetValue(type.FullName ?? type.Name, out var byFullName))
        {
            return byFullName;
        }

        formats.TryGetValue(type.Name, out var bySimpleName);
        return bySimpleName;
    }
}
