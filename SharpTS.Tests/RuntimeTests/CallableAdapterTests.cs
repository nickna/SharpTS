using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Unit tests for the ISharpTSCallable / ISharpTSCallableV2 adapter infrastructure.
/// </summary>
public class CallableAdapterTests
{
    #region Test Implementations

    /// <summary>
    /// A simple legacy callable that adds two numbers.
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
    /// A V2 callable that multiplies two numbers.
    /// </summary>
    private class V2MultiplyCallable : ISharpTSCallableV2
    {
        public int Arity => 2;

        public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
        {
            var a = arguments[0].AsNumber();
            var b = arguments[1].AsNumber();
            return a * b;
        }
    }

    /// <summary>
    /// A dual-interface callable that implements both interfaces.
    /// </summary>
    private class DualCallable : ISharpTSCallable, ISharpTSCallableV2
    {
        public int Arity => 1;
        int ISharpTSCallable.Arity() => 1;

        public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
        {
            return arguments[0].AsNumber() * 2;
        }

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            return (double)arguments[0]! * 2;
        }
    }

    #endregion

    #region CallableV2Adapter Tests

    [Fact]
    public void CallableV2Adapter_WrapsLegacyCallable()
    {
        var legacy = new LegacyAddCallable();
        var adapter = new CallableV2Adapter(legacy);

        Assert.Equal(2, adapter.Arity);
        Assert.Same(legacy, adapter.Inner);
    }

    [Fact]
    public void CallableV2Adapter_CallV2_ConvertArgumentsAndResult()
    {
        var legacy = new LegacyAddCallable();
        var adapter = new CallableV2Adapter(legacy);

        ReadOnlySpan<RuntimeValue> args = [3.0, 4.0];
        var result = adapter.CallV2(null!, args);

        Assert.Equal(ValueKind.Number, result.Kind);
        Assert.Equal(7.0, result.AsNumber());
    }

    [Fact]
    public void CallableV2Adapter_Constructor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CallableV2Adapter(null!));
    }

    #endregion

    #region CallableLegacyAdapter Tests

    [Fact]
    public void CallableLegacyAdapter_WrapsV2Callable()
    {
        var v2 = new V2MultiplyCallable();
        var adapter = new CallableLegacyAdapter(v2);

        Assert.Equal(2, adapter.Arity());
        Assert.Same(v2, adapter.Inner);
    }

    [Fact]
    public void CallableLegacyAdapter_Call_ConvertArgumentsAndResult()
    {
        var v2 = new V2MultiplyCallable();
        var adapter = new CallableLegacyAdapter(v2);

        var args = new List<object?> { 3.0, 4.0 };
        var result = adapter.Call(null!, args);

        Assert.IsType<double>(result);
        Assert.Equal(12.0, (double)result!);
    }

    [Fact]
    public void CallableLegacyAdapter_Constructor_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CallableLegacyAdapter(null!));
    }

    #endregion

    #region AsV2 Extension Tests

    [Fact]
    public void AsV2_OnLegacyCallable_ReturnsAdapter()
    {
        var legacy = new LegacyAddCallable();
        var v2 = legacy.AsV2();

        Assert.IsType<CallableV2Adapter>(v2);
        Assert.Same(legacy, ((CallableV2Adapter)v2).Inner);
    }

    [Fact]
    public void AsV2_OnDualCallable_ReturnsSameInstance()
    {
        var dual = new DualCallable();
        var v2 = ((ISharpTSCallable)dual).AsV2();

        // Should return the same instance since it already implements V2
        Assert.Same(dual, v2);
    }

    [Fact]
    public void AsV2_OnLegacyAdapter_UnwrapsToOriginalV2()
    {
        var v2Original = new V2MultiplyCallable();
        var legacyAdapter = new CallableLegacyAdapter(v2Original);
        var v2Result = legacyAdapter.AsV2();

        // Should unwrap back to the original V2
        Assert.Same(v2Original, v2Result);
    }

    #endregion

    #region AsLegacy Extension Tests

    [Fact]
    public void AsLegacy_OnV2Callable_ReturnsAdapter()
    {
        var v2 = new V2MultiplyCallable();
        var legacy = v2.AsLegacy();

        Assert.IsType<CallableLegacyAdapter>(legacy);
        Assert.Same(v2, ((CallableLegacyAdapter)legacy).Inner);
    }

    [Fact]
    public void AsLegacy_OnDualCallable_ReturnsSameInstance()
    {
        var dual = new DualCallable();
        var legacy = ((ISharpTSCallableV2)dual).AsLegacy();

        // Should return the same instance since it already implements legacy
        Assert.Same(dual, legacy);
    }

    [Fact]
    public void AsLegacy_OnV2Adapter_UnwrapsToOriginalLegacy()
    {
        var legacyOriginal = new LegacyAddCallable();
        var v2Adapter = new CallableV2Adapter(legacyOriginal);
        var legacyResult = v2Adapter.AsLegacy();

        // Should unwrap back to the original legacy
        Assert.Same(legacyOriginal, legacyResult);
    }

    #endregion

    #region CallWithRuntimeValues Extension Tests

    [Fact]
    public void CallWithRuntimeValues_OnLegacyCallable_Converts()
    {
        var legacy = new LegacyAddCallable();

        ReadOnlySpan<RuntimeValue> args = [5.0, 3.0];
        var result = legacy.CallWithRuntimeValues(null!, args);

        Assert.Equal(8.0, result.AsNumber());
    }

    [Fact]
    public void CallWithRuntimeValues_OnDualCallable_UsesV2Directly()
    {
        var dual = new DualCallable();

        ReadOnlySpan<RuntimeValue> args = [7.0];
        var result = ((ISharpTSCallable)dual).CallWithRuntimeValues(null!, args);

        Assert.Equal(14.0, result.AsNumber());
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_LegacyToV2ToLegacy_PreservesOriginal()
    {
        var original = new LegacyAddCallable();
        var v2 = original.AsV2();
        var legacy = v2.AsLegacy();

        Assert.Same(original, legacy);
    }

    [Fact]
    public void RoundTrip_V2ToLegacyToV2_PreservesOriginal()
    {
        var original = new V2MultiplyCallable();
        var legacy = original.AsLegacy();
        var v2 = legacy.AsV2();

        Assert.Same(original, v2);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void CallableV2Adapter_HandlesNullArguments()
    {
        var legacy = new NullHandlingCallable();
        var adapter = new CallableV2Adapter(legacy);

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Null];
        var result = adapter.CallV2(null!, args);

        Assert.Equal(ValueKind.Null, result.Kind);
    }

    [Fact]
    public void CallableV2Adapter_HandlesUndefinedArguments()
    {
        var legacy = new UndefinedHandlingCallable();
        var adapter = new CallableV2Adapter(legacy);

        ReadOnlySpan<RuntimeValue> args = [RuntimeValue.Undefined];
        var result = adapter.CallV2(null!, args);

        Assert.True(result.IsUndefined);
    }

    [Fact]
    public void CallableLegacyAdapter_HandlesEmptyArguments()
    {
        var v2 = new ZeroArityCallable();
        var adapter = new CallableLegacyAdapter(v2);

        var result = adapter.Call(null!, []);

        Assert.Equal(42.0, (double)result!);
    }

    private class NullHandlingCallable : ISharpTSCallable
    {
        public int Arity() => 1;
        public object? Call(Interpreter interpreter, List<object?> arguments) => arguments[0];
    }

    private class UndefinedHandlingCallable : ISharpTSCallable
    {
        public int Arity() => 1;
        public object? Call(Interpreter interpreter, List<object?> arguments) => arguments[0];
    }

    private class ZeroArityCallable : ISharpTSCallableV2
    {
        public int Arity => 0;
        public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments) => 42.0;
    }

    #endregion
}
