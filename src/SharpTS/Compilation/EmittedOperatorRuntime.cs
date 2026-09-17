using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required operator declarations and equality body lifecycle for one compilation.</summary>
public sealed class EmittedOperatorRuntime
{
    internal EmittedOperatorRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _equalityBodyEmitted;

    private MethodBuilder? _updateNumeric;
    public MethodBuilder UpdateNumeric
    {
        get => Require(_updateNumeric);
        internal set => SetHandle(ref _updateNumeric, value);
    }

    private MethodBuilder? _lessThan;
    public MethodBuilder LessThan
    {
        get => Require(_lessThan);
        internal set => SetHandle(ref _lessThan, value);
    }

    private MethodBuilder? _lessThanOrEqual;
    public MethodBuilder LessThanOrEqual
    {
        get => Require(_lessThanOrEqual);
        internal set => SetHandle(ref _lessThanOrEqual, value);
    }

    private MethodBuilder? _typeOf;
    public MethodBuilder TypeOf
    {
        get => Require(_typeOf);
        internal set => SetHandle(ref _typeOf, value);
    }

    private MethodBuilder? _instanceOf;
    public MethodBuilder InstanceOf
    {
        get => Require(_instanceOf);
        internal set => SetHandle(ref _instanceOf, value);
    }

    private MethodBuilder? _hasIn;
    public MethodBuilder HasIn
    {
        get => Require(_hasIn);
        internal set => SetHandle(ref _hasIn, value);
    }

    private MethodBuilder? _proxyOrdinaryHas;
    public MethodBuilder ProxyOrdinaryHas
    {
        get => Require(_proxyOrdinaryHas);
        internal set => SetHandle(ref _proxyOrdinaryHas, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => SetHandle(ref _add, value);
    }

    private MethodBuilder? _looseEquals;
    public MethodBuilder LooseEquals
    {
        get => Require(_looseEquals);
        internal set => SetHandle(ref _looseEquals, value);
    }

    private MethodBuilder? _strictEquals;
    public MethodBuilder StrictEquals
    {
        get => Require(_strictEquals);
        internal set => SetHandle(ref _strictEquals, value);
    }

    private MethodBuilder? _warnSloppyDeleteVariable;
    public MethodBuilder WarnSloppyDeleteVariable
    {
        get => Require(_warnSloppyDeleteVariable);
        internal set => SetHandle(ref _warnSloppyDeleteVariable, value);
    }

    internal void MarkEqualityBodyEmitted()
    {
        EnsureMutable();
        _ = LooseEquals;
        if (_equalityBodyEmitted)
            throw new InvalidOperationException("Operator Equality body emission is already complete.");
        _equalityBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Operator metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Operator metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Operator metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = UpdateNumeric;
        _ = LessThan;
        _ = LessThanOrEqual;
        _ = TypeOf;
        _ = InstanceOf;
        _ = HasIn;
        _ = ProxyOrdinaryHas;
        _ = Add;
        _ = LooseEquals;
        _ = StrictEquals;
        _ = WarnSloppyDeleteVariable;
        if (!_equalityBodyEmitted)
            throw new InvalidOperationException("Operator Equality body has not been emitted.");
        IsComplete = true;
    }
}
