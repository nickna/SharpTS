using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Verifies that the managed runtime continues to accept open-world CLR shapes
/// supplied by an assembly other than SharpTS.
/// </summary>
public sealed class ManagedStructuralClrReflectionTests
{
    [Fact]
    public void VmContext_ExtractProperties_AcceptsExternalFieldsShape()
    {
        var external = new ExternalStructuralObject();
        external.Fields["answer"] = 42.0;

        var properties = VmContext.ExtractProperties(external);

        Assert.Equal(42.0, properties["answer"]);
    }

    [Fact]
    public void PropertyDescriptor_FromAnyObject_AcceptsExternalPropertyShape()
    {
        var external = new ExternalStructuralObject();
        external.SetProperty("value", "external");
        external.SetProperty("writable", true);

        var descriptor = SharpTSPropertyDescriptor.FromAnyObject(external);

        Assert.True(descriptor.HasValue);
        Assert.Equal("external", descriptor.Value);
        Assert.True(descriptor.HasWritable);
        Assert.True(descriptor.Writable);
        Assert.False(descriptor.HasEnumerable);
    }

    [Fact]
    public void CallableAdapter_AcceptsExternalInvokeShape()
    {
        var callback = TSFunctionCallableAdapter.WrapCallback(
            new ExternalStructuralObject());

        var result = callback.Call(
            interpreter: null!,
            arguments: ["first", 2.0]);

        Assert.Equal("first:2", result);
    }

    private sealed class ExternalStructuralObject
    {
        private readonly Dictionary<string, object?> _properties = new();

        public Dictionary<string, object?> Fields { get; } = new();

        public bool HasProperty(string name) => _properties.ContainsKey(name);

        public object? GetProperty(string name) =>
            _properties.GetValueOrDefault(name);

        public void SetProperty(string name, object? value) =>
            _properties[name] = value;

        public object? Invoke(object?[] arguments) =>
            $"{arguments[0]}:{arguments[1]:0}";
    }
}
