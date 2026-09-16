using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional BigInt implementation declarations for one compilation.</summary>
public sealed class EmittedBigIntImplementation
{
    internal EmittedBigIntImplementation() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => Set(ref _create, value);
    }

    private MethodBuilder? _asIntN;
    public MethodBuilder AsIntN
    {
        get => Require(_asIntN);
        internal set => Set(ref _asIntN, value);
    }

    private MethodBuilder? _asUintN;
    public MethodBuilder AsUintN
    {
        get => Require(_asUintN);
        internal set => Set(ref _asUintN, value);
    }

    private MethodBuilder? _toBigInt;
    /// <summary>$Runtime.ToBigInt(object) -> boxed BigInteger — strict ECMA-262 ToBigInt (unlike the callable BigInt conversion, Number primitives are rejected).</summary>
    public MethodBuilder ToBigInt
    {
        get => Require(_toBigInt);
        internal set => Set(ref _toBigInt, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => Set(ref _add, value);
    }

    private MethodBuilder? _subtract;
    public MethodBuilder Subtract
    {
        get => Require(_subtract);
        internal set => Set(ref _subtract, value);
    }

    private MethodBuilder? _multiply;
    public MethodBuilder Multiply
    {
        get => Require(_multiply);
        internal set => Set(ref _multiply, value);
    }

    private MethodBuilder? _divide;
    public MethodBuilder Divide
    {
        get => Require(_divide);
        internal set => Set(ref _divide, value);
    }

    private MethodBuilder? _remainder;
    public MethodBuilder Remainder
    {
        get => Require(_remainder);
        internal set => Set(ref _remainder, value);
    }

    private MethodBuilder? _pow;
    public MethodBuilder Pow
    {
        get => Require(_pow);
        internal set => Set(ref _pow, value);
    }

    private MethodBuilder? _negate;
    public MethodBuilder Negate
    {
        get => Require(_negate);
        internal set => Set(ref _negate, value);
    }

    private MethodBuilder? _bitwiseAnd;
    public MethodBuilder BitwiseAnd
    {
        get => Require(_bitwiseAnd);
        internal set => Set(ref _bitwiseAnd, value);
    }

    private MethodBuilder? _bitwiseOr;
    public MethodBuilder BitwiseOr
    {
        get => Require(_bitwiseOr);
        internal set => Set(ref _bitwiseOr, value);
    }

    private MethodBuilder? _bitwiseXor;
    public MethodBuilder BitwiseXor
    {
        get => Require(_bitwiseXor);
        internal set => Set(ref _bitwiseXor, value);
    }

    private MethodBuilder? _bitwiseNot;
    public MethodBuilder BitwiseNot
    {
        get => Require(_bitwiseNot);
        internal set => Set(ref _bitwiseNot, value);
    }

    private MethodBuilder? _leftShift;
    public MethodBuilder LeftShift
    {
        get => Require(_leftShift);
        internal set => Set(ref _leftShift, value);
    }

    private MethodBuilder? _rightShift;
    public MethodBuilder RightShift
    {
        get => Require(_rightShift);
        internal set => Set(ref _rightShift, value);
    }

    private MethodBuilder? _equals;
    public new MethodBuilder Equals
    {
        get => Require(_equals);
        internal set => Set(ref _equals, value);
    }

    private MethodBuilder? _looseEquals;
    /// <summary>$Runtime.BigIntLooseEquals(object, object) -> bool — ECMA-262 7.2.15 loose equality for a bigint vs a non-bigint (number/string/boolean); used by the compiled `10n == 10` / `10n == "10"` paths so a Double/String operand is coerced instead of cast-crashing.</summary>
    public MethodBuilder LooseEquals
    {
        get => Require(_looseEquals);
        internal set => Set(ref _looseEquals, value);
    }

    private MethodBuilder? _toStringRadix;
    /// <summary>$Runtime.BigIntToStringRadix(object value, double radix) -> string — BigInt.prototype.toString([radix]); radix 2–36 with lowercase digits (radix 10 = bare decimal). Backs compiled `(255n).toString(16)`.</summary>
    public MethodBuilder ToStringRadix
    {
        get => Require(_toStringRadix);
        internal set => Set(ref _toStringRadix, value);
    }

    private MethodBuilder? _lessThan;
    public MethodBuilder LessThan
    {
        get => Require(_lessThan);
        internal set => Set(ref _lessThan, value);
    }

    private MethodBuilder? _lessThanOrEqual;
    public MethodBuilder LessThanOrEqual
    {
        get => Require(_lessThanOrEqual);
        internal set => Set(ref _lessThanOrEqual, value);
    }

    private MethodBuilder? _greaterThan;
    public MethodBuilder GreaterThan
    {
        get => Require(_greaterThan);
        internal set => Set(ref _greaterThan, value);
    }

    private MethodBuilder? _greaterThanOrEqual;
    public MethodBuilder GreaterThanOrEqual
    {
        get => Require(_greaterThanOrEqual);
        internal set => Set(ref _greaterThanOrEqual, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Optional BigInt implementation metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Optional BigInt implementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = AsIntN;
        _ = AsUintN;
        _ = ToBigInt;
        _ = Add;
        _ = Subtract;
        _ = Multiply;
        _ = Divide;
        _ = Remainder;
        _ = Pow;
        _ = Negate;
        _ = BitwiseAnd;
        _ = BitwiseOr;
        _ = BitwiseXor;
        _ = BitwiseNot;
        _ = LeftShift;
        _ = RightShift;
        _ = Equals;
        _ = LooseEquals;
        _ = ToStringRadix;
        _ = LessThan;
        _ = LessThanOrEqual;
        _ = GreaterThan;
        _ = GreaterThanOrEqual;
        IsComplete = true;
    }
}
