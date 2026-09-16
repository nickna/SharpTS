using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Number parsing, formatting and prototype declarations for one compilation.</summary>
public sealed class EmittedNumberRuntime
{
    internal EmittedNumberRuntime() { }

    public bool IsComplete { get; private set; }

    // Format, prototype population and cached formatting infrastructure have early declaration stages.
    private MethodBuilder? _format;
    /// <summary>$Runtime.FormatNumber(double) -> string — ECMA-262 7.1.12.1 Number::toString(10).
    /// Byte-for-byte equivalent of SharpTS.Compilation.RuntimeTypes.FormatNumber so interpreted
    /// and compiled output match. Emitted in RuntimeEmitter.NumberFormat.cs.</summary>
    public MethodBuilder Format
    {
        get => Require(_format);
        internal set => Set(ref _format, value);
    }

    private FieldBuilder? _prototypeField;
    /// <summary>Number.prototype singleton; mirror of <see cref="EmittedRuntime.BooleanPrototypeField"/> for primitive doubles.</summary>
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => Set(ref _prototypeField, value);
    }

    private MethodBuilder? _prototypePopulateMethod;
    /// <summary>Populates <see cref="PrototypeField"/> with $TSFunction wrappers; idempotent.</summary>
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => Set(ref _prototypePopulateMethod, value);
    }

    private MethodBuilder? _parseInt;
    public MethodBuilder ParseInt
    {
        get => Require(_parseInt);
        internal set => Set(ref _parseInt, value);
    }

    private MethodBuilder? _parseIntString;
    /// <summary>Typed intrinsic parse path: already-coerced string plus native radix.</summary>
    public MethodBuilder ParseIntString
    {
        get => Require(_parseIntString);
        internal set => Set(ref _parseIntString, value);
    }

    private MethodBuilder? _parseIntDecimalString;
    /// <summary>Allocation-free decimal parser for a stable string and literal radix 10.</summary>
    public MethodBuilder ParseIntDecimalString
    {
        get => Require(_parseIntDecimalString);
        internal set => Set(ref _parseIntDecimalString, value);
    }

    private MethodBuilder? _parseFloat;
    public MethodBuilder ParseFloat
    {
        get => Require(_parseFloat);
        internal set => Set(ref _parseFloat, value);
    }

    private MethodBuilder? _isNaN;
    public MethodBuilder IsNaN
    {
        get => Require(_isNaN);
        internal set => Set(ref _isNaN, value);
    }

    private MethodBuilder? _isFinite;
    public MethodBuilder IsFinite
    {
        get => Require(_isFinite);
        internal set => Set(ref _isFinite, value);
    }

    private MethodBuilder? _isInteger;
    public MethodBuilder IsInteger
    {
        get => Require(_isInteger);
        internal set => Set(ref _isInteger, value);
    }

    private MethodBuilder? _isSafeInteger;
    public MethodBuilder IsSafeInteger
    {
        get => Require(_isSafeInteger);
        internal set => Set(ref _isSafeInteger, value);
    }

    private MethodBuilder? _globalIsNaN;
    public MethodBuilder GlobalIsNaN
    {
        get => Require(_globalIsNaN);
        internal set => Set(ref _globalIsNaN, value);
    }

    private MethodBuilder? _globalIsFinite;
    public MethodBuilder GlobalIsFinite
    {
        get => Require(_globalIsFinite);
        internal set => Set(ref _globalIsFinite, value);
    }

    private MethodBuilder? _toFixed;
    public MethodBuilder ToFixed
    {
        get => Require(_toFixed);
        internal set => Set(ref _toFixed, value);
    }

    private MethodBuilder? _toFixedDouble;
    /// <summary>Typed intrinsic exact fixed formatter: native receiver and digits.</summary>
    public MethodBuilder ToFixedDouble
    {
        get => Require(_toFixedDouble);
        internal set => Set(ref _toFixedDouble, value);
    }

    private FieldBuilder? _fixedUInt64FormatterField;
    public FieldBuilder FixedUInt64FormatterField
    {
        get => Require(_fixedUInt64FormatterField);
        internal set => Set(ref _fixedUInt64FormatterField, value);
    }

    private MethodBuilder? _fixedUInt64FormatterCallback;
    public MethodBuilder FixedUInt64FormatterCallback
    {
        get => Require(_fixedUInt64FormatterCallback);
        internal set => Set(ref _fixedUInt64FormatterCallback, value);
    }

    private MethodBuilder? _toPrecision;
    public MethodBuilder ToPrecision
    {
        get => Require(_toPrecision);
        internal set => Set(ref _toPrecision, value);
    }

    private MethodBuilder? _toExponential;
    public MethodBuilder ToExponential
    {
        get => Require(_toExponential);
        internal set => Set(ref _toExponential, value);
    }

    private MethodBuilder? _toStringRadix;
    public MethodBuilder ToStringRadix
    {
        get => Require(_toStringRadix);
        internal set => Set(ref _toStringRadix, value);
    }

    private MethodBuilder? _parseIntHelper;
    public MethodBuilder ParseIntHelper
    {
        get => Require(_parseIntHelper);
        internal set => Set(ref _parseIntHelper, value);
    }

    private MethodBuilder? _getDigitValue;
    public MethodBuilder GetDigitValue
    {
        get => Require(_getDigitValue);
        internal set => Set(ref _getDigitValue, value);
    }

    private MethodBuilder? _convertIntToRadix;
    public MethodBuilder ConvertIntToRadix
    {
        get => Require(_convertIntToRadix);
        internal set => Set(ref _convertIntToRadix, value);
    }

    private MethodBuilder? _getValidFloatPart;
    public MethodBuilder GetValidFloatPart
    {
        get => Require(_getValidFloatPart);
        internal set => Set(ref _getValidFloatPart, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"number metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("number metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Format;
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        _ = ParseInt;
        _ = ParseIntString;
        _ = ParseIntDecimalString;
        _ = ParseFloat;
        _ = IsNaN;
        _ = IsFinite;
        _ = IsInteger;
        _ = IsSafeInteger;
        _ = GlobalIsNaN;
        _ = GlobalIsFinite;
        _ = ToFixed;
        _ = ToFixedDouble;
        _ = FixedUInt64FormatterField;
        _ = FixedUInt64FormatterCallback;
        _ = ToPrecision;
        _ = ToExponential;
        _ = ToStringRadix;
        _ = ParseIntHelper;
        _ = GetDigitValue;
        _ = ConvertIntToRadix;
        _ = GetValidFloatPart;
        IsComplete = true;
    }
}
