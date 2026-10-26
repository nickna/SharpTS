using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required bound function and bind, call and apply wrapper declarations.</summary>
public sealed class EmittedFunctionBindingRuntime
{
    internal EmittedFunctionBindingRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _captureName;
    public MethodBuilder CaptureName
    {
        get => Require(_captureName);
        internal set => SetHandle(ref _captureName, value);
    }

    private MethodBuilder? _captureLength;
    public MethodBuilder CaptureLength
    {
        get => Require(_captureLength);
        internal set => SetHandle(ref _captureLength, value);
    }

    private MethodBuilder? _capturePrototype;
    public MethodBuilder CapturePrototype
    {
        get => Require(_capturePrototype);
        internal set => SetHandle(ref _capturePrototype, value);
    }

    private TypeBuilder? _boundType;
    public TypeBuilder BoundType
    {
        get => Require(_boundType);
        internal set => SetHandle(ref _boundType, value);
    }

    private ConstructorBuilder? _boundCtor;
    public ConstructorBuilder BoundCtor
    {
        get => Require(_boundCtor);
        internal set => SetHandle(ref _boundCtor, value);
    }

    private MethodBuilder? _boundInvoke;
    public MethodBuilder BoundInvoke
    {
        get => Require(_boundInvoke);
        internal set => SetHandle(ref _boundInvoke, value);
    }

    private MethodBuilder? _boundInvokeWithThis;
    public MethodBuilder BoundInvokeWithThis
    {
        get => Require(_boundInvokeWithThis);
        internal set => SetHandle(ref _boundInvokeWithThis, value);
    }

    private FieldBuilder? _boundTargetField;
    public FieldBuilder BoundTargetField
    {
        get => Require(_boundTargetField);
        internal set => SetHandle(ref _boundTargetField, value);
    }

    private FieldBuilder? _boundArgumentsField;
    public FieldBuilder BoundArgumentsField
    {
        get => Require(_boundArgumentsField);
        internal set => SetHandle(ref _boundArgumentsField, value);
    }

    private FieldBuilder? _boundNameField;
    public FieldBuilder BoundNameField
    {
        get => Require(_boundNameField);
        internal set => SetHandle(ref _boundNameField, value);
    }

    private FieldBuilder? _boundLengthField;
    public FieldBuilder BoundLengthField
    {
        get => Require(_boundLengthField);
        internal set => SetHandle(ref _boundLengthField, value);
    }

    private TypeBuilder? _anyType;
    public TypeBuilder AnyType
    {
        get => Require(_anyType);
        internal set => SetHandle(ref _anyType, value);
    }

    private ConstructorBuilder? _anyCtor;
    public ConstructorBuilder AnyCtor
    {
        get => Require(_anyCtor);
        internal set => SetHandle(ref _anyCtor, value);
    }

    private MethodBuilder? _anyInvoke;
    public MethodBuilder AnyInvoke
    {
        get => Require(_anyInvoke);
        internal set => SetHandle(ref _anyInvoke, value);
    }

    private FieldBuilder? _anyTargetField;
    public FieldBuilder AnyTargetField
    {
        get => Require(_anyTargetField);
        internal set => SetHandle(ref _anyTargetField, value);
    }

    private FieldBuilder? _anyArgumentsField;
    public FieldBuilder AnyArgumentsField
    {
        get => Require(_anyArgumentsField);
        internal set => SetHandle(ref _anyArgumentsField, value);
    }

    private FieldBuilder? _anyNameField;
    public FieldBuilder AnyNameField
    {
        get => Require(_anyNameField);
        internal set => SetHandle(ref _anyNameField, value);
    }

    private FieldBuilder? _anyLengthField;
    public FieldBuilder AnyLengthField
    {
        get => Require(_anyLengthField);
        internal set => SetHandle(ref _anyLengthField, value);
    }

    private TypeBuilder? _bindType;
    public TypeBuilder BindType
    {
        get => Require(_bindType);
        internal set => SetHandle(ref _bindType, value);
    }

    private ConstructorBuilder? _bindCtor;
    public ConstructorBuilder BindCtor
    {
        get => Require(_bindCtor);
        internal set => SetHandle(ref _bindCtor, value);
    }

    private MethodBuilder? _bindInvoke;
    public MethodBuilder BindInvoke
    {
        get => Require(_bindInvoke);
        internal set => SetHandle(ref _bindInvoke, value);
    }

    private TypeBuilder? _callType;
    public TypeBuilder CallType
    {
        get => Require(_callType);
        internal set => SetHandle(ref _callType, value);
    }

    private ConstructorBuilder? _callCtor;
    public ConstructorBuilder CallCtor
    {
        get => Require(_callCtor);
        internal set => SetHandle(ref _callCtor, value);
    }

    private MethodBuilder? _callInvoke;
    public MethodBuilder CallInvoke
    {
        get => Require(_callInvoke);
        internal set => SetHandle(ref _callInvoke, value);
    }

    private TypeBuilder? _applyType;
    public TypeBuilder ApplyType
    {
        get => Require(_applyType);
        internal set => SetHandle(ref _applyType, value);
    }

    private ConstructorBuilder? _applyCtor;
    public ConstructorBuilder ApplyCtor
    {
        get => Require(_applyCtor);
        internal set => SetHandle(ref _applyCtor, value);
    }

    private MethodBuilder? _applyInvoke;
    public MethodBuilder ApplyInvoke
    {
        get => Require(_applyInvoke);
        internal set => SetHandle(ref _applyInvoke, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function binding metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function binding metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function binding metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CaptureName;
        _ = CaptureLength;
        _ = CapturePrototype;
        _ = BoundType;
        _ = BoundCtor;
        _ = BoundInvoke;
        _ = BoundInvokeWithThis;
        _ = BoundTargetField;
        _ = BoundArgumentsField;
        _ = BoundNameField;
        _ = BoundLengthField;
        _ = AnyType;
        _ = AnyCtor;
        _ = AnyInvoke;
        _ = AnyTargetField;
        _ = AnyArgumentsField;
        _ = AnyNameField;
        _ = AnyLengthField;
        _ = BindType;
        _ = BindCtor;
        _ = BindInvoke;
        _ = CallType;
        _ = CallCtor;
        _ = CallInvoke;
        _ = ApplyType;
        _ = ApplyCtor;
        _ = ApplyInvoke;
        IsComplete = true;
    }
}
