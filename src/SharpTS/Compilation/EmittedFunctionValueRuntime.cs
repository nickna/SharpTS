using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required function values, invocation caches and receiver-context declarations.</summary>
public sealed class EmittedFunctionValueRuntime
{
    internal EmittedFunctionValueRuntime() { }
    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private MethodBuilder? _invoke;
    public MethodBuilder Invoke
    {
        get => Require(_invoke);
        internal set => SetHandle(ref _invoke, value);
    }

    private MethodBuilder? _invokeWithThis;
    public MethodBuilder InvokeWithThis
    {
        get => Require(_invokeWithThis);
        internal set => SetHandle(ref _invokeWithThis, value);
    }

    private MethodBuilder? _getTarget;
    public MethodBuilder GetTarget
    {
        get => Require(_getTarget);
        internal set => SetHandle(ref _getTarget, value);
    }

    private MethodBuilder? _bindThis;
    public MethodBuilder BindThis
    {
        get => Require(_bindThis);
        internal set => SetHandle(ref _bindThis, value);
    }

    private MethodBuilder? _lengthGetter;
    public MethodBuilder LengthGetter
    {
        get => Require(_lengthGetter);
        internal set => SetHandle(ref _lengthGetter, value);
    }

    private MethodBuilder? _nameGetter;
    public MethodBuilder NameGetter
    {
        get => Require(_nameGetter);
        internal set => SetHandle(ref _nameGetter, value);
    }

    private FieldBuilder? _prototypeCacheField;
    public FieldBuilder PrototypeCacheField
    {
        get => Require(_prototypeCacheField);
        internal set => SetHandle(ref _prototypeCacheField, value);
    }

    private MethodBuilder? _getMethodInfo;
    public MethodBuilder GetMethodInfo
    {
        get => Require(_getMethodInfo);
        internal set => SetHandle(ref _getMethodInfo, value);
    }

    private FieldBuilder? _paramCountField;
    public FieldBuilder ParamCountField
    {
        get => Require(_paramCountField);
        internal set => SetHandle(ref _paramCountField, value);
    }

    private FieldBuilder? _expectsThisField;
    public FieldBuilder ExpectsThisField
    {
        get => Require(_expectsThisField);
        internal set => SetHandle(ref _expectsThisField, value);
    }

    private FieldBuilder? _capturesArgumentsField;
    public FieldBuilder CapturesArgumentsField
    {
        get => Require(_capturesArgumentsField);
        internal set => SetHandle(ref _capturesArgumentsField, value);
    }

    private FieldBuilder? _numericRest4Field;
    public FieldBuilder NumericRest4Field
    {
        get => Require(_numericRest4Field);
        internal set => SetHandle(ref _numericRest4Field, value);
    }

    private MethodBuilder? _invokeWithThis0;
    public MethodBuilder InvokeWithThis0
    {
        get => Require(_invokeWithThis0);
        internal set => SetHandle(ref _invokeWithThis0, value);
    }

    private FieldBuilder? _currentThisField;
    /// <summary>
    /// Thread-static field holding the current function's <c>this</c> when the
    /// enclosing call path (e.g. <c>$Runtime.NewOnFunction</c>) has a thisArg that
    /// the compiled method signature can't otherwise receive. <see cref="LocalVariableResolver.LoadThis"/>
    /// falls back to this field when a method has no <c>__this</c> param and no
    /// captured <c>this</c>.
    /// </summary>
    public FieldBuilder CurrentThisField
    {
        get => Require(_currentThisField);
        internal set => SetHandle(ref _currentThisField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function value metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function value metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function value metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Invoke;
        _ = InvokeWithThis;
        _ = GetTarget;
        _ = BindThis;
        _ = LengthGetter;
        _ = NameGetter;
        _ = PrototypeCacheField;
        _ = GetMethodInfo;
        _ = ParamCountField;
        _ = ExpectsThisField;
        _ = CapturesArgumentsField;
        _ = NumericRest4Field;
        _ = InvokeWithThis0;
        _ = CurrentThisField;
        IsComplete = true;
    }
}
