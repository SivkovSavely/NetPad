using NetPad.Media;
using O2Html;
using O2Html.Common;
using O2Html.Dom;

namespace NetPad.Presentation.Html;

/// <summary>
/// Prepares output for presentation by formatting it as HTML.
/// </summary>
public static class HtmlPresenter
{
    private static readonly HtmlSerializerOptions _htmlSerializerOptions;

    static HtmlPresenter()
    {
        _htmlSerializerOptions = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.IgnoreAndSerializeCyclicReference,
            DoNotSerializeNonRootEmptyCollections = true,
            Converters =
            {
                // Dump-pipeline converters (ToDump customization, per-dump option gates) run
                // before the fork's presentational converters; both precede O2Html defaults.
                new ToDumpHtmlConverter(),
                new FormattedValueHtmlConverter(),
                new MemberFilteredObjectHtmlConverter(),
                new DumpOptionsCollectionHtmlConverter(),
                new CompositionHtmlConverter(),
                new DiffResultHtmlConverter(),
                new ImageHtmlConverter(),
                new AudioHtmlConverter(),
                new VideoHtmlConverter(),
                new MediaFileHtmlConverter(),
                new MediaFileCollectionConverter(),
                new HorizontalRunHtmlConverter(),
                new StyledValueHtmlConverter(),
                new HighlightedTextHtmlConverter(),
                new OnDemandValueHtmlConverter()
            },
        };

        var configFileValues = PresentationSettings.GetConfigFileValues();

        UpdateSerializerSettings(configFileValues.maxDepth, configFileValues.maxCollectionSerializeLength);
    }

    public static bool IsDotNetTypeWithStringRepresentation(Type type) => HtmlSerializer.IsDotNetTypeWithStringRepresentation(type);

    public static void UpdateSerializerSettings(uint? maxDepth, uint? maxCollectionSerializeLength)
    {
        _htmlSerializerOptions.MaxDepth = maxDepth ?? PresentationSettings.MaxDepth;
        _htmlSerializerOptions.MaxCollectionSerializeLength = maxCollectionSerializeLength ?? PresentationSettings.MaxCollectionLength;
    }

    /// <summary>
    /// Serializes output to an HTML <see cref="Element"/>.
    /// </summary>
    /// <param name="output">The object to serialize.</param>
    /// <param name="options">Dump options</param>
    /// <param name="isError">
    /// If true, output will be considered an error. This has no effect if <paramref name="output"/> is
    /// an <see cref="Exception"/> as exceptions are always considered errors.
    /// </param>
    /// <returns>An HTML <see cref="Element"/> representing the <paramref name="output"/>.</returns>
    public static Element SerializeToElement(
        object? output,
        DumpOptions? options = null,
        bool isError = false)
    {
        options ??= new DumpOptions();

        using var _ = PresentationSerializationContext.Push(options);

        var outermostTraversal = ToDumpTraversal.BeginIfNotStarted();
        try
        {
            return SerializeToElementCore(output, options, isError);
        }
        finally
        {
            if (outermostTraversal)
            {
                ToDumpTraversal.End();
            }
        }
    }

    private static Element SerializeToElementCore(object? output, DumpOptions options, bool isError)
    {
        bool isTitled = options.Title != null;

        if (!isError && output is Exception)
        {
            isError = true;
        }

        // Per-dump overrides require a cloned serializer-options instance; otherwise the shared
        // configured instance (with settings-provided limits) is used as-is.
        var serializerOptions = _htmlSerializerOptions;
        if (options.MaxDepth is > 0 && options.MaxDepth != serializerOptions.MaxDepth)
        {
            serializerOptions = CloneSerializerOptions(maxDepth: options.MaxDepth);
        }

        // If output is already an HTML DOM element, do not serialize it.
        if (output is not Node node)
        {
            try
            {
                if (options.CodeType != null && output is string code)
                {
                    node = TextNode.RawText(code);
                }
                else
                {
                    node = HtmlSerializer.Serialize(output, serializerOptions);
                }
            }
            catch (Exception ex)
            {
                node = HtmlSerializer.Serialize("Could not serialize object to HTML. " + ex, serializerOptions);
                isError = true;
            }
        }

        // ALL outputs are wrapped in a <div class="group"></div>
        var group = new Element("div").AddClass("group");

        bool outputIsOnlyEscapedText = ContainsOnlyEscapedText(node);

        if (options.CssClasses?.Length > 0)
        {
            group.AddClass(options.CssClasses);
        }

        if (isError)
        {
            group.AddClass("error");
        }

        if (isTitled)
        {
            group.AddClass("titled")
                .AddAndGetElement("h6")
                .AddClass("title")
                .AddEscapedText(options.Title);

            if (outputIsOnlyEscapedText)
            {
                node = new Element("span").AddClass("text").AddChild(node);
            }
        }

        node = HandleSourceCode(node, group, options);

        group.AddChild(node);

        if (outputIsOnlyEscapedText)
        {
            group.AddClass("text");

            if (options.AppendNewLineToAllTextOutput == true)
            {
                group.AddElement("<br/>");
            }
        }
        else if (output is Image)
        {
            group.AddClass("image");
        }
        else if (output is Audio)
        {
            group.AddClass("audio");
        }
        else if (output is Video)
        {
            group.AddClass("video");
        }

        if (options.DestructAfterMs > 0)
        {
            group.SetAttribute("data-destruct", options.DestructAfterMs.Value.ToString());
        }

        if (options.Expanded != null)
        {
            group.SetAttribute("data-expanded", options.Expanded.Value ? "true" : "false");
        }

        return group;
    }

    private static HtmlSerializerOptions CloneSerializerOptions(uint? maxDepth = null, uint? maxCollectionSerializeLength = null)
    {
        var clone = new HtmlSerializerOptions
        {
            ReferenceLoopHandling = _htmlSerializerOptions.ReferenceLoopHandling,
            DoNotSerializeNonRootEmptyCollections = _htmlSerializerOptions.DoNotSerializeNonRootEmptyCollections,
            MaxCollectionSerializeLength = maxCollectionSerializeLength ?? _htmlSerializerOptions.MaxCollectionSerializeLength,
            MaxDepth = maxDepth ?? _htmlSerializerOptions.MaxDepth,
        };

        foreach (var converter in _htmlSerializerOptions.Converters)
        {
            clone.Converters.Add(converter);
        }

        return clone;
    }

    /// <summary>
    /// Serializes output to an HTML string.
    /// </summary>
    /// <param name="output">The object to serialize.</param>
    /// <param name="options">Dump options</param>
    /// <param name="isError">
    /// If true, output will be considered an error. This has no effect if <paramref name="output"/> is
    /// an <see cref="Exception"/> as</param> as exceptions are always considered errors.
    /// <returns>An HTML string representation of <paramref name="output"/>.</returns>
    public static string Serialize(
        object? output,
        DumpOptions? options = null,
        bool isError = false
    )
    {
        return SerializeToElement(output, options, isError).ToHtml();
    }

    internal static bool ContainsOnlyEscapedText(Node node)
    {
        return node is TextNode { IsEscaped: true } ||
               (node is Element element &&
                element.Children.Any() &&
                element.Children.All(c => c is TextNode { IsEscaped: true }));
    }

    internal static Node HandleSourceCode(Node serializedOutput, Element group, DumpOptions options)
    {
        bool outputIsCodeString = options.CodeType != null && serializedOutput is TextNode { IsEscaped: false };
        Element? codeElement = GetPreCodeElement(serializedOutput);

        bool shouldBeRenderedAsCode = outputIsCodeString || codeElement != null;

        if (!shouldBeRenderedAsCode)
        {
            return serializedOutput;
        }

        group.AddClass("code");

        if (codeElement == null)
        {
            var textNode = (TextNode)serializedOutput;

            var pre = new Element("pre");

            codeElement = pre.AddAndGetElement("code")
                .AddText(
                    // Angle brackets in code elements need to be escaped for proper rendering.
                    textNode.Text?
                        .Replace("<", HtmlConsts.HtmlLessThan)
                        .Replace(">", HtmlConsts.HtmlGreaterThan) ?? string.Empty
                );

            serializedOutput = pre;
        }

        if (options.CodeType != null)
        {
            codeElement.SetAttribute("language", options.CodeType);
        }

        return serializedOutput;
    }

    private static Element? GetPreCodeElement(Node node)
    {
        if (node is not Element { TagName: "pre" } element)
        {
            return null;
        }

        var codeElements = element.ChildElements.Where(el => el.TagName == "code").ToArray();

        if (codeElements.Length != 1)
        {
            return null;
        }

        return codeElements.First();
    }
}
