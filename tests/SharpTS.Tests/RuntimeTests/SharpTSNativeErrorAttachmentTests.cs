using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public class SharpTSNativeErrorAttachmentTests
{
    [Theory]
    [InlineData("Error")]
    [InlineData("TypeError")]
    public void NativeErrorDefaultsCanBeReadBeforeInterpreterAttachment(string typeName)
    {
        var error = new SharpTSErrorClass("Error", null);
        var klass = typeName == "Error" ? error : new SharpTSErrorClass(typeName, error);
        var instance = new SharpTSInstance(klass);

        Assert.Equal(typeName, instance.Get(Name("name")));
        Assert.Equal("", instance.Get(Name("message")));
        Assert.True(instance.HasProperty("name"));
        Assert.True(instance.HasProperty("message"));
        Assert.Same(klass, instance.Get(Name("constructor")));
        Assert.IsAssignableFrom<ISharpTSCallable>(instance.Get(Name("toString")));
        Assert.Same(SharpTSUndefined.Instance, instance.Get(Name("missing")));
        Assert.False(instance.HasProperty("missing"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeErrorPropertiesCanBeWrittenBeforeInterpreterAttachment(bool strict)
    {
        var klass = new SharpTSErrorClass("Error", null);
        var instance = new SharpTSInstance(klass);

        if (strict) instance.SetStrict(Name("name"), "custom", strictMode: true);
        else instance.Set(Name("name"), "custom");

        Assert.Equal("custom", instance.Get(Name("name")));
        Assert.True(instance.HasProperty("name"));
        Assert.Equal("Error", klass.Prototype.GetMember("name"));
        Assert.Equal("custom", instance.GetOwnPropertyDescriptor("name")!.Value);
        Assert.True(instance.DeleteField("name"));
        Assert.Equal("Error", instance.Get(Name("name")));
    }

    [Fact]
    public void UnattachedSubclassReadsLiveInheritedDefaultsAndPreservesNull()
    {
        var error = new SharpTSErrorClass("Error", null);
        var parent = new SharpTSErrorClass("TypeError", error);
        var child = new SharpTSErrorClass("Custom", parent, [], [], []);
        var instance = new SharpTSInstance(child);

        Assert.Equal("TypeError", instance.Get(Name("name")));
        parent.Prototype.SetExtra("name", null);
        Assert.Null(instance.Get(Name("name")));
        Assert.True(instance.HasProperty("name"));
        Assert.True(parent.Prototype.DeleteProperty("name"));
        Assert.Equal("Error", instance.Get(Name("name")));
        Assert.True(error.Prototype.DeleteProperty("name"));
        Assert.Same(SharpTSUndefined.Instance, instance.Get(Name("name")));
        Assert.False(instance.HasProperty("name"));
    }

    [Fact]
    public void AttachingInterpreterKeepsInheritedErrorPropertiesLive()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var klass = new SharpTSErrorClass("Error", null);
        var instance = new SharpTSInstance(klass);
        Assert.Equal("Error", instance.Get(Name("name")));
        instance.SetInterpreter(interpreter);

        klass.Prototype.SetExtra("name", "live");
        Assert.Equal("live", instance.Get(Name("name")));
        Assert.True(instance.HasProperty("name"));
        instance.SetStrict(Name("name"), "own", strictMode: true);
        Assert.Equal("own", instance.Get(Name("name")));
        Assert.True(instance.DeleteField("name"));
        Assert.Equal("live", instance.Get(Name("name")));
        Assert.True(klass.Prototype.DeleteProperty("name"));
        Assert.Same(SharpTSUndefined.Instance, instance.Get(Name("name")));
        Assert.False(instance.HasProperty("name"));
    }

    private static Token Name(string name) => new(TokenType.IDENTIFIER, name, null, 1);
}
