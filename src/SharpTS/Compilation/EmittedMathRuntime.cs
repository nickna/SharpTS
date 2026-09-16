using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Math singleton, numeric adapters and exact summation declarations for one compilation.</summary>
public sealed class EmittedMathRuntime
{
    internal EmittedMathRuntime() { }

    public bool IsComplete { get; private set; }

    // The singleton shell precedes adapters and Random; exact summation is declared later.
    private FieldBuilder? _singletonField;
    public FieldBuilder SingletonField
    {
        get => Require(_singletonField);
        internal set => Set(ref _singletonField, value);
    }

    private MethodBuilder? _singletonPopulateMethod;
    /// <summary>
    /// Populates <see cref="SingletonField"/> with $TSFunction wrappers for
    /// the Math static methods (max/min/floor/…) so value-form access
    /// (<c>const m = Math; m.max(1,2)</c>) resolves. Idempotent. See issue #276.
    /// </summary>
    public MethodBuilder SingletonPopulateMethod
    {
        get => Require(_singletonPopulateMethod);
        internal set => Set(ref _singletonPopulateMethod, value);
    }

    private MethodBuilder? _random;
    public MethodBuilder Random
    {
        get => Require(_random);
        internal set => Set(ref _random, value);
    }

    private MethodBuilder? _sumPrecise;
    public MethodBuilder SumPrecise
    {
        get => Require(_sumPrecise);
        internal set => Set(ref _sumPrecise, value);
    }

    private MethodBuilder? _floorAdapter;
    public MethodBuilder FloorAdapter
    {
        get => Require(_floorAdapter);
        internal set => Set(ref _floorAdapter, value);
    }

    private MethodBuilder? _ceilAdapter;
    public MethodBuilder CeilAdapter
    {
        get => Require(_ceilAdapter);
        internal set => Set(ref _ceilAdapter, value);
    }

    private MethodBuilder? _absAdapter;
    public MethodBuilder AbsAdapter
    {
        get => Require(_absAdapter);
        internal set => Set(ref _absAdapter, value);
    }

    private MethodBuilder? _sqrtAdapter;
    public MethodBuilder SqrtAdapter
    {
        get => Require(_sqrtAdapter);
        internal set => Set(ref _sqrtAdapter, value);
    }

    private MethodBuilder? _roundAdapter;
    public MethodBuilder RoundAdapter
    {
        get => Require(_roundAdapter);
        internal set => Set(ref _roundAdapter, value);
    }

    private MethodBuilder? _truncAdapter;
    public MethodBuilder TruncAdapter
    {
        get => Require(_truncAdapter);
        internal set => Set(ref _truncAdapter, value);
    }

    private MethodBuilder? _signAdapter;
    public MethodBuilder SignAdapter
    {
        get => Require(_signAdapter);
        internal set => Set(ref _signAdapter, value);
    }

    private MethodBuilder? _sinAdapter;
    public MethodBuilder SinAdapter
    {
        get => Require(_sinAdapter);
        internal set => Set(ref _sinAdapter, value);
    }

    private MethodBuilder? _cosAdapter;
    public MethodBuilder CosAdapter
    {
        get => Require(_cosAdapter);
        internal set => Set(ref _cosAdapter, value);
    }

    private MethodBuilder? _tanAdapter;
    public MethodBuilder TanAdapter
    {
        get => Require(_tanAdapter);
        internal set => Set(ref _tanAdapter, value);
    }

    private MethodBuilder? _logAdapter;
    public MethodBuilder LogAdapter
    {
        get => Require(_logAdapter);
        internal set => Set(ref _logAdapter, value);
    }

    private MethodBuilder? _expAdapter;
    public MethodBuilder ExpAdapter
    {
        get => Require(_expAdapter);
        internal set => Set(ref _expAdapter, value);
    }

    private MethodBuilder? _powAdapter;
    public MethodBuilder PowAdapter
    {
        get => Require(_powAdapter);
        internal set => Set(ref _powAdapter, value);
    }

    private MethodBuilder? _maxAdapter;
    public MethodBuilder MaxAdapter
    {
        get => Require(_maxAdapter);
        internal set => Set(ref _maxAdapter, value);
    }

    private MethodBuilder? _minAdapter;
    public MethodBuilder MinAdapter
    {
        get => Require(_minAdapter);
        internal set => Set(ref _minAdapter, value);
    }

    private MethodBuilder? _asinAdapter;
    public MethodBuilder AsinAdapter
    {
        get => Require(_asinAdapter);
        internal set => Set(ref _asinAdapter, value);
    }

    private MethodBuilder? _acosAdapter;
    public MethodBuilder AcosAdapter
    {
        get => Require(_acosAdapter);
        internal set => Set(ref _acosAdapter, value);
    }

    private MethodBuilder? _atanAdapter;
    public MethodBuilder AtanAdapter
    {
        get => Require(_atanAdapter);
        internal set => Set(ref _atanAdapter, value);
    }

    private MethodBuilder? _atan2Adapter;
    public MethodBuilder Atan2Adapter
    {
        get => Require(_atan2Adapter);
        internal set => Set(ref _atan2Adapter, value);
    }

    private MethodBuilder? _sinhAdapter;
    public MethodBuilder SinhAdapter
    {
        get => Require(_sinhAdapter);
        internal set => Set(ref _sinhAdapter, value);
    }

    private MethodBuilder? _coshAdapter;
    public MethodBuilder CoshAdapter
    {
        get => Require(_coshAdapter);
        internal set => Set(ref _coshAdapter, value);
    }

    private MethodBuilder? _tanhAdapter;
    public MethodBuilder TanhAdapter
    {
        get => Require(_tanhAdapter);
        internal set => Set(ref _tanhAdapter, value);
    }

    private MethodBuilder? _asinhAdapter;
    public MethodBuilder AsinhAdapter
    {
        get => Require(_asinhAdapter);
        internal set => Set(ref _asinhAdapter, value);
    }

    private MethodBuilder? _acoshAdapter;
    public MethodBuilder AcoshAdapter
    {
        get => Require(_acoshAdapter);
        internal set => Set(ref _acoshAdapter, value);
    }

    private MethodBuilder? _atanhAdapter;
    public MethodBuilder AtanhAdapter
    {
        get => Require(_atanhAdapter);
        internal set => Set(ref _atanhAdapter, value);
    }

    private MethodBuilder? _cbrtAdapter;
    public MethodBuilder CbrtAdapter
    {
        get => Require(_cbrtAdapter);
        internal set => Set(ref _cbrtAdapter, value);
    }

    private MethodBuilder? _log10Adapter;
    public MethodBuilder Log10Adapter
    {
        get => Require(_log10Adapter);
        internal set => Set(ref _log10Adapter, value);
    }

    private MethodBuilder? _log2Adapter;
    public MethodBuilder Log2Adapter
    {
        get => Require(_log2Adapter);
        internal set => Set(ref _log2Adapter, value);
    }

    private MethodBuilder? _log1pAdapter;
    public MethodBuilder Log1pAdapter
    {
        get => Require(_log1pAdapter);
        internal set => Set(ref _log1pAdapter, value);
    }

    private MethodBuilder? _expm1Adapter;
    public MethodBuilder Expm1Adapter
    {
        get => Require(_expm1Adapter);
        internal set => Set(ref _expm1Adapter, value);
    }

    private MethodBuilder? _froundAdapter;
    public MethodBuilder FroundAdapter
    {
        get => Require(_froundAdapter);
        internal set => Set(ref _froundAdapter, value);
    }

    private MethodBuilder? _f16RoundAdapter;
    public MethodBuilder F16RoundAdapter
    {
        get => Require(_f16RoundAdapter);
        internal set => Set(ref _f16RoundAdapter, value);
    }

    private MethodBuilder? _clz32Adapter;
    public MethodBuilder Clz32Adapter
    {
        get => Require(_clz32Adapter);
        internal set => Set(ref _clz32Adapter, value);
    }

    private MethodBuilder? _imulAdapter;
    public MethodBuilder ImulAdapter
    {
        get => Require(_imulAdapter);
        internal set => Set(ref _imulAdapter, value);
    }

    private MethodBuilder? _hypotAdapter;
    public MethodBuilder HypotAdapter
    {
        get => Require(_hypotAdapter);
        internal set => Set(ref _hypotAdapter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Math metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Math metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = SingletonField;
        _ = SingletonPopulateMethod;
        _ = Random;
        _ = SumPrecise;
        _ = FloorAdapter;
        _ = CeilAdapter;
        _ = AbsAdapter;
        _ = SqrtAdapter;
        _ = RoundAdapter;
        _ = TruncAdapter;
        _ = SignAdapter;
        _ = SinAdapter;
        _ = CosAdapter;
        _ = TanAdapter;
        _ = LogAdapter;
        _ = ExpAdapter;
        _ = PowAdapter;
        _ = MaxAdapter;
        _ = MinAdapter;
        _ = AsinAdapter;
        _ = AcosAdapter;
        _ = AtanAdapter;
        _ = Atan2Adapter;
        _ = SinhAdapter;
        _ = CoshAdapter;
        _ = TanhAdapter;
        _ = AsinhAdapter;
        _ = AcoshAdapter;
        _ = AtanhAdapter;
        _ = CbrtAdapter;
        _ = Log10Adapter;
        _ = Log2Adapter;
        _ = Log1pAdapter;
        _ = Expm1Adapter;
        _ = FroundAdapter;
        _ = F16RoundAdapter;
        _ = Clz32Adapter;
        _ = ImulAdapter;
        _ = HypotAdapter;
        IsComplete = true;
    }
}
