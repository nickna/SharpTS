using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Unit tests for the unified ISharpTSCallable interface: the CallV2 default-method
/// bridge for boxed-only implementors, native CallV2 overrides, and the
/// CallableInterop boxed-call helpers.
/// </summary>
public class CallableAdapterTests
{
    #region Test Implementations

    /// <summary>
    /// A boxed-only callable that adds two numbers — exercises the CallV2 DIM bridge.
    /// </summary>
    private class LegacyAddCallable : ISharpTSCallable
    {
        public int Arity() => 2;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            var a = (double)arguments[0]!;
            var b = (double)arguments[1]!;
            return a + b;
        }
    }

    /// <summary>
    /// A callable with a native CallV2 override alongside the boxed Call.
    /// </summary>
    private class DualCallable : ISharpTSCallable
    {
        public int Arity() => 1;

        public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
        {
            return arguments[0].AsNumber() * 2;
        }

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            // Distinct result so tests can detect which path ran.
            return (double)arguments[0]! * -1;
        }
    }

    private class NullHandlingCallable : ISharpTSCallable
    {
        public int Arity() => 1;
        public object? Call(Interpreter interpreter, List<object?> arguments) => arguments[0];
    }

    private class ZeroArityCallable : ISharpTSCallable
    {
        public int Arity() => 0;
        public object? Call(Interpreter interpreter, List<object?> arguments) => 42.0;
    }

    #endregion

    #region CallV2 DIM Bridge Tests

    [Fact]
    public void CallV2_OnBoxedOnlyCallable_ConvertsArgumentsAndResult()
    {
        ISharpTSCallable legacy = new LegacyAddCallable();

        ReadOnlySpan<RuntimeValue> args = [3.0, 4.0];
        var result = legacy.CallV2(null!, args);

        Assert.Equal(ValueKind.Number, result.Kind);
        Assert.Equal(7.0, result.AsNumber());
    }

    [Fact]
    public void CallV2_OnDualCallable_UsesNativeOverride()
    {
        ISharpTSCallable dual = new DualCallable();

        ReadOnlySpan<RuntimeValue> args = [7.0];
        var result = dual.CallV2(null!, args);

        // Native CallV2 doubles; the boxed Call negates. Interface dispatch
        // must pick the override, not the DIM bridge.
        Assert.Equal(14.0, result.AsNumber());
    }

    [Fact]
    public void CallV2_BridgePreservesNull()
    {
        ISharpTSCallable callable = new NullHandlingCallable();

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Null];
        var result = callable.CallV2(null!, args);

        Assert.Equal(ValueKind.Null, result.Kind);
    }

    [Fact]
    public void CallV2_BridgePreservesUndefined()
    {
        ISharpTSCallable callable = new NullHandlingCallable();

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Undefined];
        var result = callable.CallV2(null!, args);

        Assert.True(result.IsUndefined);
    }

    [Fact]
    public void CallV2_BridgeHandlesEmptyArguments()
    {
        ISharpTSCallable callable = new ZeroArityCallable();

        var result = callable.CallV2(null!, ReadOnlySpan<RuntimeValue>.Empty);

        Assert.Equal(42.0, result.AsNumber());
    }

    #endregion

    #region CallableInterop Tests

    [Fact]
    public void CallBoxed_OnDualCallable_RoutesThroughCallV2()
    {
        var dual = new DualCallable();

        var result = dual.CallBoxed(null!, [7.0]);

        // CallBoxed must dispatch via CallV2 (doubles), not legacy Call (negates).
        Assert.Equal(14.0, (double)result!);
    }

    [Fact]
    public void CallBoxed_OnBoxedOnlyCallable_RoundTripsValues()
    {
        var legacy = new LegacyAddCallable();

        var result = legacy.CallBoxed(null!, [5.0, 3.0]);

        Assert.Equal(8.0, (double)result!);
    }

    [Fact]
    public void ToRuntimeValues_ConvertsBoxedList()
    {
        var values = CallableInterop.ToRuntimeValues([1.0, "x", null, true]);

        Assert.Equal(4, values.Length);
        Assert.Equal(1.0, values[0].AsNumber());
        Assert.Equal("x", values[1].AsString());
        Assert.Equal(ValueKind.Null, values[2].Kind);
        Assert.True(values[3].AsBoolean());
    }

    [Fact]
    public void ToBoxedList_ConvertsSpan()
    {
        ReadOnlySpan<RuntimeValue> span = [2.0, RuntimeValue.Undefined];
        var boxed = CallableInterop.ToBoxedList(span);

        Assert.Equal(2, boxed.Count);
        Assert.Equal(2.0, (double)boxed[0]!);
    }

    #endregion
}
