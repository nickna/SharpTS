using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Unit tests for BuiltInMethod RuntimeValue (V2) support.
/// </summary>
public class BuiltInMethodV2Tests
{
    #region Constructor Tests

    [Fact]
    public void CreateV2_SetsProperties()
    {
        var method = BuiltInMethod.CreateV2("test", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsNumber() + args[1].AsNumber());

        Assert.Equal("test", method.Name);
        Assert.Equal(2, method.Arity());
        Assert.Equal(2, method.MinArity);
        Assert.Equal(2, method.MaxArity);
    }

    [Fact]
    public void CreateV2_VariableArity()
    {
        var method = BuiltInMethod.CreateV2("test", 1, 3,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsNumber());

        Assert.Equal(1, method.MinArity);
        Assert.Equal(3, method.MaxArity);
    }

    #endregion

    #region CallV2 Tests

    [Fact]
    public void CallV2_WithV2Implementation_CallsDirectly()
    {
        var callCount = 0;
        var method = BuiltInMethod.CreateV2("add", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
            {
                callCount++;
                return args[0].AsNumber() + args[1].AsNumber();
            });

        ReadOnlySpan<RuntimeValue> args = [3.0, 4.0];
        var result = method.CallV2(null!, args);

        Assert.Equal(1, callCount);
        Assert.Equal(7.0, result.AsNumber());
    }

    [Fact]
    public void CallV2_WithLegacyImplementation_ConvertsCorrectly()
    {
        var method = new BuiltInMethod("add", 2,
            (Interpreter interp, object? receiver, List<object?> args) =>
                (double)args[0]! + (double)args[1]!);

        ReadOnlySpan<RuntimeValue> args = [5.0, 6.0];
        var result = method.CallV2(null!, args);

        Assert.Equal(11.0, result.AsNumber());
    }

    [Fact]
    public void CallV2_BelowMinArity_Throws()
    {
        var method = BuiltInMethod.CreateV2("test", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> a) =>
                RuntimeValue.Null);

        RuntimeValue[] args = [1.0];
        var ex = Assert.Throws<Exception>(() => method.CallV2(null!, args));
        Assert.Contains("expects 2-2 arguments", ex.Message);
    }

    [Fact]
    public void CallV2_AboveMaxArity_Throws()
    {
        var method = BuiltInMethod.CreateV2("test", 1, 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> a) =>
                RuntimeValue.Null);

        RuntimeValue[] args = [1.0, 2.0, 3.0];
        var ex = Assert.Throws<Exception>(() => method.CallV2(null!, args));
        Assert.Contains("expects 1-2 arguments", ex.Message);
    }

    #endregion

    #region Legacy Call Tests

    [Fact]
    public void Call_WithV2Implementation_ConvertsCorrectly()
    {
        var method = BuiltInMethod.CreateV2("multiply", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsNumber() * args[1].AsNumber());

        var args = new List<object?> { 3.0, 4.0 };
        var result = method.Call(null!, args);

        Assert.Equal(12.0, result);
    }

    #endregion

    #region BindV2 Tests

    [Fact]
    public void BindV2_CreatesBindingWithRuntimeValue()
    {
        var method = BuiltInMethod.CreateV2("getReceiver", 0,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                receiver);

        var bound = method.BindV2(42.0);
        var result = bound.CallV2(null!, ReadOnlySpan<RuntimeValue>.Empty);

        Assert.Equal(42.0, result.AsNumber());
    }

    [Fact]
    public void BindV2_WithObject_PreservesReference()
    {
        var method = BuiltInMethod.CreateV2("getReceiver", 0,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                receiver);

        var testObj = new TestObject { Value = "test" };
        var bound = method.BindV2(RuntimeValue.FromObject(testObj));
        var result = bound.CallV2(null!, ReadOnlySpan<RuntimeValue>.Empty);

        Assert.True(result.TryAsObject<TestObject>(out var returned));
        Assert.Same(testObj, returned);
    }

    private class TestObject
    {
        public string Value { get; set; } = "";
    }

    #endregion

    #region ISharpTSCallableV2 Interface Tests

    [Fact]
    public void ImplementsISharpTSCallableV2()
    {
        var method = new BuiltInMethod("test", 1,
            (Interpreter interp, object? receiver, List<object?> args) => args[0]);

        Assert.IsAssignableFrom<ISharpTSCallableV2>(method);
    }

    [Fact]
    public void ISharpTSCallableV2_Arity_MatchesMinArity()
    {
        var method = new BuiltInMethod("test", 1, 3,
            (Interpreter interp, object? receiver, List<object?> args) => args[0]);

        ISharpTSCallableV2 v2 = method;
        Assert.Equal(1, v2.Arity);
    }

    #endregion

    #region Mixed Call Scenarios

    [Fact]
    public void MixedCalls_LegacyAndV2_WorkCorrectly()
    {
        var method = BuiltInMethod.CreateV2("double", 1,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsNumber() * 2);

        // Call via V2
        ReadOnlySpan<RuntimeValue> v2Args = [5.0];
        var v2Result = method.CallV2(null!, v2Args);
        Assert.Equal(10.0, v2Result.AsNumber());

        // Call via legacy
        var legacyArgs = new List<object?> { 7.0 };
        var legacyResult = method.Call(null!, legacyArgs);
        Assert.Equal(14.0, legacyResult);
    }

    [Fact]
    public void Bind_PreservesV2Capability()
    {
        var method = BuiltInMethod.CreateV2("addReceiver", 1,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                receiver.AsNumber() + args[0].AsNumber());

        var bound = method.Bind(10.0);

        ReadOnlySpan<RuntimeValue> args = [5.0];
        var result = bound.CallV2(null!, args);

        Assert.Equal(15.0, result.AsNumber());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CallV2_WithNullValues_HandlesCorrectly()
    {
        var method = BuiltInMethod.CreateV2("isNull", 1,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].IsNull);

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Null];
        var result = method.CallV2(null!, args);

        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void CallV2_WithUndefined_HandlesCorrectly()
    {
        var method = BuiltInMethod.CreateV2("isUndefined", 1,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].IsUndefined);

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Undefined];
        var result = method.CallV2(null!, args);

        Assert.True(result.AsBoolean());
    }

    [Fact]
    public void CallV2_EmptyArgs_Works()
    {
        var method = BuiltInMethod.CreateV2("constant", 0,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                42.0);

        var result = method.CallV2(null!, ReadOnlySpan<RuntimeValue>.Empty);

        Assert.Equal(42.0, result.AsNumber());
    }

    [Fact]
    public void CallV2_StringArguments_PreservesValue()
    {
        var method = BuiltInMethod.CreateV2("concat", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsString() + args[1].AsString());

        ReadOnlySpan<RuntimeValue> args = ["Hello, ", "World!"];
        var result = method.CallV2(null!, args);

        Assert.Equal("Hello, World!", result.AsString());
    }

    [Fact]
    public void CallV2_BooleanArguments_PreservesValue()
    {
        var method = BuiltInMethod.CreateV2("and", 2,
            (Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                args[0].AsBoolean() && args[1].AsBoolean());

        ReadOnlySpan<RuntimeValue> args = [true, false];
        var result = method.CallV2(null!, args);

        Assert.False(result.AsBoolean());
    }

    #endregion
}
