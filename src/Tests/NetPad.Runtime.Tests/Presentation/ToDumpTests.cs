using System.Collections;
using System.Reflection;
using NetPad.Presentation;
using NetPad.Presentation.Html;

namespace NetPad.Runtime.Tests.Presentation;

[Collection("NetPad.Presentation.StaticPipeline")]
public sealed class ToDumpTests : IDisposable
{
    public ToDumpTests()
    {
        DumpTransformer.Clear();
        DumpOptions.Default = new DumpOptions();
    }

    public void Dispose()
    {
        DumpTransformer.Clear();
        DumpOptions.Default = new DumpOptions();
    }

    private static string Html(object? o) => HtmlPresenter.Serialize(o);

    private sealed class PublicHook
    {
        public string Name { get; set; } = "original";

        public object ToDump() => new { Name, Customized = true };
    }

    private sealed class PrivateHook
    {
        public string Name { get; set; } = "original";

        private object ToDump() => "private-hook-value";
    }

    private class InheritedHookBase
    {
        protected object ToDump() => "from-base";
    }

    private sealed class InheritedHook : InheritedHookBase
    {
    }

    private sealed class SelfReturningHook
    {
        public object ToDump() => this;
    }

    private sealed class CyclicA
    {
        public object ToDump() => new CyclicB();
    }

    private sealed class CyclicB
    {
        public object ToDump() => new CyclicA();
    }

    private sealed class ThrowingHook
    {
        public object ToDump() => throw new InvalidOperationException("boom-from-hook");
    }

    private sealed class NoHook
    {
        public string Value { get; set; } = "plain";
    }

    [Fact]
    public void Public_Instance_Hook_Is_Applied()
    {
        var html = Html(new PublicHook());

        Assert.Contains("Customized", html);
        Assert.Contains("original", html);
    }

    [Fact]
    public void Private_Instance_Hook_Is_Applied()
    {
        var html = Html(new PrivateHook());

        Assert.Contains("private-hook-value", html);
        // The underlying object's member must not be rendered; the hook result replaces it.
        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public void Inherited_NonPublic_Hook_Is_Applied()
    {
        Assert.Contains("from-base", Html(new InheritedHook()));
    }

    [Fact]
    public void Global_Transformer_Runs_Before_Instance_Hook()
    {
        using var registration = DumpTransformer.Register(value => value is PublicHook ? "global-won" : value);
        var html = Html(new PublicHook());

        Assert.Contains("global-won", html);
        Assert.DoesNotContain("Customized", html);
    }

    [Fact]
    public void Global_Transformer_Composes_In_Registration_Order()
    {
        using var outer = DumpTransformer.Register(value => value is int i ? i * 2 : value);
        using var inner = DumpTransformer.Register(value => value is int i ? i + 1 : value);

        var html = Html(5);

        // (5 * 2) + 1
        Assert.Contains("11", html);
    }

    [Fact]
    public void Null_Is_Not_Transformed()
    {
        var invoked = false;
        using var _ = DumpTransformer.Register(value =>
        {
            invoked = true;
            return value;
        });

        var html = Html(null as NoHook);

        Assert.Contains("null", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(invoked);
    }

    [Fact]
    public void Self_Returning_Hook_Terminates()
    {
        var html = Html(new SelfReturningHook());

        Assert.Contains("SelfReturningHook", html);
    }

    [Fact]
    public void Cyclic_Transform_Chain_Terminates()
    {
        // A hook chain that keeps producing fresh hook-bearing instances must terminate (guard-
        // bounded) without a stack overflow and still render a value.
        var html = Html(new CyclicA());

        Assert.Contains("Cyclic", html);
    }

    [Fact]
    public void Hook_Throwing_Renders_Error_Node_And_Does_Not_Poison_Stream()
    {
        var errorHtml = Html(new ThrowingHook());
        Assert.Contains("ToDump()", errorHtml);
        Assert.Contains("boom-from-hook", errorHtml);

        // Subsequent dumps are unaffected.
        Assert.Contains("plain", Html(new NoHook()));
    }

    [Fact]
    public void Nested_Members_And_Collections_Are_Transformed_Once_Each()
    {
        WithCounterHook.Invocations = 0;
        var original = new WithCounterHook();

        var html = Html(new[] { original, original });

        // Same reference twice in one traversal is transformed only once.
        Assert.Equal(1, WithCounterHook.Invocations);
        Assert.Contains("counter-hook", html);
    }

    private sealed class WithCounterHook
    {
        public static int Invocations;

        public object ToDump()
        {
            Invocations++;
            return "counter-hook";
        }
    }

    [Fact]
    public void Types_Without_Hooks_Render_Normally()
    {
        var html = Html(new NoHook { Value = "kept" });

        Assert.Contains("kept", html);
        Assert.Contains("Value", html);
    }

    [Fact]
    public void Resolver_Finds_Hook_On_Type_With_Any_Visibility()
    {
        Assert.NotNull(ToDumpResolverForTests(typeof(PublicHook)));
        Assert.NotNull(ToDumpResolverForTests(typeof(PrivateHook)));
        Assert.NotNull(ToDumpResolverForTests(typeof(InheritedHook)));
        Assert.Null(ToDumpResolverForTests(typeof(NoHook)));
        Assert.Null(ToDumpResolverForTests(typeof(string)));
    }

    private static MethodInfo? ToDumpResolverForTests(Type type) => ToDumpResolver.GetHook(type);
}
