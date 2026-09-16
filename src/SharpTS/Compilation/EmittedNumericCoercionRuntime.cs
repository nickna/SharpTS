using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required numeric conversion declarations for one compilation.</summary>
public sealed class EmittedNumericCoercionRuntime
{
    internal EmittedNumericCoercionRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _toNumber;
    public MethodBuilder ToNumber
    {
        get => Require(_toNumber);
        internal set => Set(ref _toNumber, value);
    }

    private MethodBuilder? _convertToNumber;
    public MethodBuilder ConvertToNumber
    {
        get => Require(_convertToNumber);
        internal set => Set(ref _convertToNumber, value);
    }

    private MethodBuilder? _jsNumberToInt32;
    /// <summary>$Runtime.JsNumberToInt32(double) - allocation-free ECMA-262 ToInt32 for statically numeric operands.</summary>
    public MethodBuilder JsNumberToInt32
    {
        get => Require(_jsNumberToInt32);
        internal set => Set(ref _jsNumberToInt32, value);
    }

    private MethodBuilder? _jsToInt32;
    public MethodBuilder JsToInt32
    {
        get => Require(_jsToInt32);
        internal set => Set(ref _jsToInt32, value);
    }

    private MethodBuilder? _toIntegerOrInfinity;
    public MethodBuilder ToIntegerOrInfinity
    {
        get => Require(_toIntegerOrInfinity);
        internal set => Set(ref _toIntegerOrInfinity, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Required numeric coercion metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Required numeric coercion metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ToNumber;
        _ = ConvertToNumber;
        _ = JsNumberToInt32;
        _ = JsToInt32;
        _ = ToIntegerOrInfinity;
        IsComplete = true;
    }
}
