using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required display and language string-conversion declarations for one compilation.</summary>
public sealed class EmittedStringCoercionRuntime
{
    internal EmittedStringCoercionRuntime() { }

    public bool IsComplete { get; private set; }

    // Stringify, ToJsString and StringifyCoerce are declared before their bodies.
    private MethodBuilder? _concatInt64;
    /// <summary>$Runtime.ConcatStringInt64(string, long, bool) -> string —
    /// allocation-minimal concatenation for proven integer loop counters.</summary>
    public MethodBuilder ConcatInt64
    {
        get => Require(_concatInt64);
        internal set => Set(ref _concatInt64, value);
    }

    private MethodBuilder? _stringify;
    public MethodBuilder Stringify
    {
        get => Require(_stringify);
        internal set => Set(ref _stringify, value);
    }

    private MethodBuilder? _toJsString;
    public MethodBuilder ToJsString
    {
        get => Require(_toJsString);
        internal set => Set(ref _toJsString, value);
    }

    private MethodBuilder? _fromValue;
    /// <summary>$Runtime.StringFromValue(object) -> string — ECMA-262 §22.1.1.1 String(value) call form: Symbol → SymbolDescriptiveString (via $TSSymbol.ToString()); everything else → ToJsString. Only the String() constructor-call form is exempt from ToString's Symbol TypeError; implicit coercions (template literals, concat) must keep throwing.</summary>
    public MethodBuilder FromValue
    {
        get => Require(_fromValue);
        internal set => Set(ref _fromValue, value);
    }

    private MethodBuilder? _stringifyCoerce;
    /// <summary>$Runtime.StringifyCoerce(object) -> string — Stringify with the ECMA-262 §7.1.17 Symbol guard: implicit ToString coercion sites (template-literal interpolation, string <c>+</c>/<c>+=</c> concatenation) throw TypeError for Symbol values. Console formatting and the String() call form must NOT route through this.</summary>
    public MethodBuilder StringifyCoerce
    {
        get => Require(_stringifyCoerce);
        internal set => Set(ref _stringifyCoerce, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"string coercion metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("string coercion metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ConcatInt64;
        _ = Stringify;
        _ = ToJsString;
        _ = FromValue;
        _ = StringifyCoerce;
        IsComplete = true;
    }
}
