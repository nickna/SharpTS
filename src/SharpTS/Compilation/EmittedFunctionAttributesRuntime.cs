using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required per-compilation function attributes, completed before function wrappers.</summary>
public sealed class EmittedFunctionAttributesRuntime
{
    internal EmittedFunctionAttributesRuntime() { }
    public bool IsComplete { get; private set; }

    private TypeBuilder? _capturesArgumentsType;
    public TypeBuilder CapturesArgumentsType
    {
        get => Require(_capturesArgumentsType);
        internal set => SetHandle(ref _capturesArgumentsType, value);
    }

    private ConstructorBuilder? _capturesArgumentsCtor;
    public ConstructorBuilder CapturesArgumentsCtor
    {
        get => Require(_capturesArgumentsCtor);
        internal set => SetHandle(ref _capturesArgumentsCtor, value);
    }

    private TypeBuilder? _padUndefinedType;
    public TypeBuilder PadUndefinedType
    {
        get => Require(_padUndefinedType);
        internal set => SetHandle(ref _padUndefinedType, value);
    }

    private ConstructorBuilder? _padUndefinedCtor;
    public ConstructorBuilder PadUndefinedCtor
    {
        get => Require(_padUndefinedCtor);
        internal set => SetHandle(ref _padUndefinedCtor, value);
    }

    private TypeBuilder? _functionLengthType;
    public TypeBuilder FunctionLengthType
    {
        get => Require(_functionLengthType);
        internal set => SetHandle(ref _functionLengthType, value);
    }

    private ConstructorBuilder? _functionLengthCtor;
    public ConstructorBuilder FunctionLengthCtor
    {
        get => Require(_functionLengthCtor);
        internal set => SetHandle(ref _functionLengthCtor, value);
    }

    private FieldBuilder? _functionLengthValueField;
    public FieldBuilder FunctionLengthValueField
    {
        get => Require(_functionLengthValueField);
        internal set => SetHandle(ref _functionLengthValueField, value);
    }

    private TypeBuilder? _functionNameType;
    public TypeBuilder FunctionNameType
    {
        get => Require(_functionNameType);
        internal set => SetHandle(ref _functionNameType, value);
    }

    private ConstructorBuilder? _functionNameCtor;
    public ConstructorBuilder FunctionNameCtor
    {
        get => Require(_functionNameCtor);
        internal set => SetHandle(ref _functionNameCtor, value);
    }

    private FieldBuilder? _functionNameValueField;
    public FieldBuilder FunctionNameValueField
    {
        get => Require(_functionNameValueField);
        internal set => SetHandle(ref _functionNameValueField, value);
    }

    private TypeBuilder? _numericRest4Type;
    public TypeBuilder NumericRest4Type
    {
        get => Require(_numericRest4Type);
        internal set => SetHandle(ref _numericRest4Type, value);
    }

    private ConstructorBuilder? _numericRest4Ctor;
    public ConstructorBuilder NumericRest4Ctor
    {
        get => Require(_numericRest4Ctor);
        internal set => SetHandle(ref _numericRest4Ctor, value);
    }

    private FieldBuilder? _numericRest4ValueField;
    public FieldBuilder NumericRest4ValueField
    {
        get => Require(_numericRest4ValueField);
        internal set => SetHandle(ref _numericRest4ValueField, value);
    }

    private TypeBuilder? _nonConstructibleType;
    public TypeBuilder NonConstructibleType
    {
        get => Require(_nonConstructibleType);
        internal set => SetHandle(ref _nonConstructibleType, value);
    }

    private ConstructorBuilder? _nonConstructibleCtor;
    public ConstructorBuilder NonConstructibleCtor
    {
        get => Require(_nonConstructibleCtor);
        internal set => SetHandle(ref _nonConstructibleCtor, value);
    }

    private TypeBuilder? _expectsThisType;
    public TypeBuilder ExpectsThisType
    {
        get => Require(_expectsThisType);
        internal set => SetHandle(ref _expectsThisType, value);
    }

    private ConstructorBuilder? _expectsThisCtor;
    public ConstructorBuilder ExpectsThisCtor
    {
        get => Require(_expectsThisCtor);
        internal set => SetHandle(ref _expectsThisCtor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function attribute metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function attribute metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function attribute metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CapturesArgumentsType;
        _ = CapturesArgumentsCtor;
        _ = PadUndefinedType;
        _ = PadUndefinedCtor;
        _ = FunctionLengthType;
        _ = FunctionLengthCtor;
        _ = FunctionLengthValueField;
        _ = FunctionNameType;
        _ = FunctionNameCtor;
        _ = FunctionNameValueField;
        _ = NumericRest4Type;
        _ = NumericRest4Ctor;
        _ = NumericRest4ValueField;
        _ = NonConstructibleType;
        _ = NonConstructibleCtor;
        _ = ExpectsThisType;
        _ = ExpectsThisCtor;
        IsComplete = true;
    }
}
