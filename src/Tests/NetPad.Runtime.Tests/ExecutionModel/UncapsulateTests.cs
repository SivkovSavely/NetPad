using System.Reflection;
using Microsoft.CSharp.RuntimeBinder;
using NetPad.Presentation;

namespace NetPad.ExecutionModel.ScriptServices.Tests;

public sealed class UncapsulateTests
{
#pragma warning disable 414 // Field value is observed through reflection/dynamic dispatch.

    private sealed class Target
    {
        private string _privateField = "field-value";
        private string PrivateProperty { get; set; } = "prop-value";

        private int Multiply(int x, int y) => x * y;

        private int Overloaded(string s) => 1;
        private int Overloaded(int i) => 2;
    }

    private class BasePrivate
    {
        private string HiddenByBase => "base-hidden";
    }

    private sealed class Derived : BasePrivate;

    [Fact]
    public void ReadsPrivateFields()
    {
        dynamic uncapsulated = new Target().Uncapsulate()!;

        Assert.Equal("field-value", uncapsulated._privateField);
    }

    [Fact]
    public void WritesToPrivateFields()
    {
        var target = new Target();
        dynamic uncapsulated = target.Uncapsulate()!;

        uncapsulated._privateField = "updated";

        var field = typeof(Target).GetField("_privateField", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal("updated", field.GetValue(target));
    }

    [Fact]
    public void ReadsPrivateProperties()
    {
        dynamic uncapsulated = new Target().Uncapsulate()!;

        Assert.Equal("prop-value", uncapsulated.PrivateProperty);
    }

    [Fact]
    public void InvokesPrivateMethods()
    {
        dynamic uncapsulated = new Target().Uncapsulate()!;

        Assert.Equal(6, uncapsulated.Multiply(2, 3));
    }

    [Fact]
    public void SelectsExactOverload()
    {
        dynamic uncapsulated = new Target().Uncapsulate()!;

        Assert.Equal(2, uncapsulated.Overloaded(10));
        Assert.Equal(1, uncapsulated.Overloaded("x"));
    }

    [Fact]
    public void ReachesInheritedPrivateMembers()
    {
        dynamic uncapsulated = ((Derived)typeof(Derived).UncapsulateStatic().New()).Uncapsulate()!;

        Assert.Equal("base-hidden", uncapsulated.HiddenByBase);
    }

    [Fact]
    public void StaticMembersAreAccessible()
    {
        dynamic statics = typeof(SecretHolder).UncapsulateStatic();

        Assert.Equal("secret", statics.SecretValue);
    }

    [Fact]
    public void PrivateConstructorIsInvokable()
    {
        dynamic statics = typeof(SealedWithPrivateCtor).UncapsulateStatic();

        var instance = (SealedWithPrivateCtor)statics.New("hello");

        Assert.Equal("hello", instance.Message);
    }

    [Fact]
    public void GenericMethodsWithInferredTypeArgumentsAreInvokable()
    {
        dynamic uncapsulated = new GenericHolder().Uncapsulate()!;

        Assert.Equal(42, uncapsulated.Echo(42));
    }

    [Fact]
    public void MissingMemberFailsDynamicDispatch()
    {
        dynamic uncapsulated = new Target().Uncapsulate()!;

        Assert.Throws<RuntimeBinderException>(() => _ = uncapsulated.DoesNotExist);
    }

    private static class SecretHolder
    {
        private const string SecretValue = "secret";
    }

    private sealed class SealedWithPrivateCtor
    {
        public string Message { get; }

        private SealedWithPrivateCtor(string message)
        {
            Message = message;
        }
    }

    private sealed class GenericHolder
    {
        private T Echo<T>(T value) => value;
    }
}
