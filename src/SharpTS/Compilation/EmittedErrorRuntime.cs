using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required guest Error types, prototypes and exception bridges for one compilation.</summary>
public sealed class EmittedErrorRuntime
{
    internal EmittedErrorRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _createExceptionBodyEmitted;
    private bool _createFromTypeBodyEmitted;
    private bool _prototypeBodyEmitted;
    private bool _nativePrototypeBodiesEmitted;

    private FieldBuilder? _prototype;
    /// <summary>Error.prototype singleton dict — populated with a $TSFunction wrapping the spec-compliant ErrorToStringSpec IL helper. Returned by GetProperty's Type-receiver branch when receiver is typeof($Error). Required so <c>Error.prototype.toString.call(non-error)</c> hits the brand-checking helper instead of generic .NET reflection on $Error.</summary>
    public FieldBuilder Prototype
    {
        get => Require(_prototype);
        internal set => SetHandle(ref _prototype, value);
    }

    private MethodBuilder? _prototypePopulate;
    /// <summary>Idempotent populate for <see cref="ErrorPrototypeField"/>.</summary>
    public MethodBuilder PrototypePopulate
    {
        get => Require(_prototypePopulate);
        internal set => SetHandle(ref _prototypePopulate, value);
    }

    private FieldBuilder? _typeErrorPrototype;
    /// <summary>TypeError.prototype singleton dict. Per ECMA-262 §20.5.6.4 each NativeError prototype is a distinct object whose [[Prototype]] is Error.prototype, with own `constructor` / `name` / `message` slots. Lazy-populated.</summary>
    public FieldBuilder TypeErrorPrototype
    {
        get => Require(_typeErrorPrototype);
        internal set => SetHandle(ref _typeErrorPrototype, value);
    }

    private MethodBuilder? _typeErrorPrototypePopulate;
    public MethodBuilder TypeErrorPrototypePopulate
    {
        get => Require(_typeErrorPrototypePopulate);
        internal set => SetHandle(ref _typeErrorPrototypePopulate, value);
    }

    private FieldBuilder? _rangeErrorPrototype;
    public FieldBuilder RangeErrorPrototype
    {
        get => Require(_rangeErrorPrototype);
        internal set => SetHandle(ref _rangeErrorPrototype, value);
    }

    private MethodBuilder? _rangeErrorPrototypePopulate;
    public MethodBuilder RangeErrorPrototypePopulate
    {
        get => Require(_rangeErrorPrototypePopulate);
        internal set => SetHandle(ref _rangeErrorPrototypePopulate, value);
    }

    private FieldBuilder? _referenceErrorPrototype;
    public FieldBuilder ReferenceErrorPrototype
    {
        get => Require(_referenceErrorPrototype);
        internal set => SetHandle(ref _referenceErrorPrototype, value);
    }

    private MethodBuilder? _referenceErrorPrototypePopulate;
    public MethodBuilder ReferenceErrorPrototypePopulate
    {
        get => Require(_referenceErrorPrototypePopulate);
        internal set => SetHandle(ref _referenceErrorPrototypePopulate, value);
    }

    private FieldBuilder? _syntaxErrorPrototype;
    public FieldBuilder SyntaxErrorPrototype
    {
        get => Require(_syntaxErrorPrototype);
        internal set => SetHandle(ref _syntaxErrorPrototype, value);
    }

    private MethodBuilder? _syntaxErrorPrototypePopulate;
    public MethodBuilder SyntaxErrorPrototypePopulate
    {
        get => Require(_syntaxErrorPrototypePopulate);
        internal set => SetHandle(ref _syntaxErrorPrototypePopulate, value);
    }

    private FieldBuilder? _uRIErrorPrototype;
    public FieldBuilder URIErrorPrototype
    {
        get => Require(_uRIErrorPrototype);
        internal set => SetHandle(ref _uRIErrorPrototype, value);
    }

    private MethodBuilder? _uRIErrorPrototypePopulate;
    public MethodBuilder URIErrorPrototypePopulate
    {
        get => Require(_uRIErrorPrototypePopulate);
        internal set => SetHandle(ref _uRIErrorPrototypePopulate, value);
    }

    private FieldBuilder? _evalErrorPrototype;
    public FieldBuilder EvalErrorPrototype
    {
        get => Require(_evalErrorPrototype);
        internal set => SetHandle(ref _evalErrorPrototype, value);
    }

    private MethodBuilder? _evalErrorPrototypePopulate;
    public MethodBuilder EvalErrorPrototypePopulate
    {
        get => Require(_evalErrorPrototypePopulate);
        internal set => SetHandle(ref _evalErrorPrototypePopulate, value);
    }

    private FieldBuilder? _aggregateErrorPrototype;
    public FieldBuilder AggregateErrorPrototype
    {
        get => Require(_aggregateErrorPrototype);
        internal set => SetHandle(ref _aggregateErrorPrototype, value);
    }

    private MethodBuilder? _aggregateErrorPrototypePopulate;
    public MethodBuilder AggregateErrorPrototypePopulate
    {
        get => Require(_aggregateErrorPrototypePopulate);
        internal set => SetHandle(ref _aggregateErrorPrototypePopulate, value);
    }

    private MethodBuilder? _throwStrictSyntaxError;
    public MethodBuilder ThrowStrictSyntaxError
    {
        get => Require(_throwStrictSyntaxError);
        internal set => SetHandle(ref _throwStrictSyntaxError, value);
    }

    private MethodBuilder? _createException;
    public MethodBuilder CreateException
    {
        get => Require(_createException);
        internal set => SetHandle(ref _createException, value);
    }

    private MethodBuilder? _wrapException;
    public MethodBuilder WrapException
    {
        get => Require(_wrapException);
        internal set => SetHandle(ref _wrapException, value);
    }

    private MethodBuilder? _throwUndefinedVariable;
    public MethodBuilder ThrowUndefinedVariable
    {
        get => Require(_throwUndefinedVariable);
        internal set => SetHandle(ref _throwUndefinedVariable, value);
    }

    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _messageConstructor;
    public ConstructorBuilder MessageConstructor
    {
        get => Require(_messageConstructor);
        internal set => SetHandle(ref _messageConstructor, value);
    }

    private ConstructorBuilder? _nameMessageConstructor;
    public ConstructorBuilder NameMessageConstructor
    {
        get => Require(_nameMessageConstructor);
        internal set => SetHandle(ref _nameMessageConstructor, value);
    }

    private MethodBuilder? _nameGetter;
    public MethodBuilder NameGetter
    {
        get => Require(_nameGetter);
        internal set => SetHandle(ref _nameGetter, value);
    }

    private MethodBuilder? _nameSetter;
    public MethodBuilder NameSetter
    {
        get => Require(_nameSetter);
        internal set => SetHandle(ref _nameSetter, value);
    }

    private MethodBuilder? _messageGetter;
    public MethodBuilder MessageGetter
    {
        get => Require(_messageGetter);
        internal set => SetHandle(ref _messageGetter, value);
    }

    private MethodBuilder? _messageSetter;
    public MethodBuilder MessageSetter
    {
        get => Require(_messageSetter);
        internal set => SetHandle(ref _messageSetter, value);
    }

    private MethodBuilder? _stackGetter;
    public MethodBuilder StackGetter
    {
        get => Require(_stackGetter);
        internal set => SetHandle(ref _stackGetter, value);
    }

    private MethodBuilder? _stackSetter;
    public MethodBuilder StackSetter
    {
        get => Require(_stackSetter);
        internal set => SetHandle(ref _stackSetter, value);
    }

    private MethodBuilder? _capturedStackSetter;
    public MethodBuilder CapturedStackSetter
    {
        get => Require(_capturedStackSetter);
        internal set => SetHandle(ref _capturedStackSetter, value);
    }

    private MethodBuilder? _causeGetter;
    public MethodBuilder CauseGetter
    {
        get => Require(_causeGetter);
        internal set => SetHandle(ref _causeGetter, value);
    }

    private MethodBuilder? _causeSetter;
    public MethodBuilder CauseSetter
    {
        get => Require(_causeSetter);
        internal set => SetHandle(ref _causeSetter, value);
    }

    private MethodBuilder? _hasCauseGetter;
    public MethodBuilder HasCauseGetter
    {
        get => Require(_hasCauseGetter);
        internal set => SetHandle(ref _hasCauseGetter, value);
    }

    private MethodBuilder? _codeGetter;
    public MethodBuilder CodeGetter
    {
        get => Require(_codeGetter);
        internal set => SetHandle(ref _codeGetter, value);
    }

    private MethodBuilder? _codeSetter;
    public MethodBuilder CodeSetter
    {
        get => Require(_codeSetter);
        internal set => SetHandle(ref _codeSetter, value);
    }

    private MethodBuilder? _syscallGetter;
    public MethodBuilder SyscallGetter
    {
        get => Require(_syscallGetter);
        internal set => SetHandle(ref _syscallGetter, value);
    }

    private MethodBuilder? _syscallSetter;
    public MethodBuilder SyscallSetter
    {
        get => Require(_syscallSetter);
        internal set => SetHandle(ref _syscallSetter, value);
    }

    private Type? _typeErrorType;
    public Type TypeErrorType
    {
        get => Require(_typeErrorType);
        internal set => SetHandle(ref _typeErrorType, value);
    }

    private ConstructorBuilder? _typeErrorConstructor;
    public ConstructorBuilder TypeErrorConstructor
    {
        get => Require(_typeErrorConstructor);
        internal set => SetHandle(ref _typeErrorConstructor, value);
    }

    private Type? _rangeErrorType;
    public Type RangeErrorType
    {
        get => Require(_rangeErrorType);
        internal set => SetHandle(ref _rangeErrorType, value);
    }

    private ConstructorBuilder? _rangeErrorConstructor;
    public ConstructorBuilder RangeErrorConstructor
    {
        get => Require(_rangeErrorConstructor);
        internal set => SetHandle(ref _rangeErrorConstructor, value);
    }

    private Type? _referenceErrorType;
    public Type ReferenceErrorType
    {
        get => Require(_referenceErrorType);
        internal set => SetHandle(ref _referenceErrorType, value);
    }

    private ConstructorBuilder? _referenceErrorConstructor;
    public ConstructorBuilder ReferenceErrorConstructor
    {
        get => Require(_referenceErrorConstructor);
        internal set => SetHandle(ref _referenceErrorConstructor, value);
    }

    private Type? _syntaxErrorType;
    public Type SyntaxErrorType
    {
        get => Require(_syntaxErrorType);
        internal set => SetHandle(ref _syntaxErrorType, value);
    }

    private ConstructorBuilder? _syntaxErrorConstructor;
    public ConstructorBuilder SyntaxErrorConstructor
    {
        get => Require(_syntaxErrorConstructor);
        internal set => SetHandle(ref _syntaxErrorConstructor, value);
    }

    private Type? _uRIErrorType;
    public Type URIErrorType
    {
        get => Require(_uRIErrorType);
        internal set => SetHandle(ref _uRIErrorType, value);
    }

    private ConstructorBuilder? _uRIErrorConstructor;
    public ConstructorBuilder URIErrorConstructor
    {
        get => Require(_uRIErrorConstructor);
        internal set => SetHandle(ref _uRIErrorConstructor, value);
    }

    private Type? _evalErrorType;
    public Type EvalErrorType
    {
        get => Require(_evalErrorType);
        internal set => SetHandle(ref _evalErrorType, value);
    }

    private ConstructorBuilder? _evalErrorConstructor;
    public ConstructorBuilder EvalErrorConstructor
    {
        get => Require(_evalErrorConstructor);
        internal set => SetHandle(ref _evalErrorConstructor, value);
    }

    private Type? _aggregateErrorType;
    public Type AggregateErrorType
    {
        get => Require(_aggregateErrorType);
        internal set => SetHandle(ref _aggregateErrorType, value);
    }

    private ConstructorBuilder? _aggregateErrorConstructor;
    public ConstructorBuilder AggregateErrorConstructor
    {
        get => Require(_aggregateErrorConstructor);
        internal set => SetHandle(ref _aggregateErrorConstructor, value);
    }

    private MethodBuilder? _aggregateErrorErrorsGetter;
    public MethodBuilder AggregateErrorErrorsGetter
    {
        get => Require(_aggregateErrorErrorsGetter);
        internal set => SetHandle(ref _aggregateErrorErrorsGetter, value);
    }

    private MethodBuilder? _createError;
    public MethodBuilder CreateError
    {
        get => Require(_createError);
        internal set => SetHandle(ref _createError, value);
    }

    private MethodBuilder? _createErrorFromTypeOrNull;
    public MethodBuilder CreateErrorFromTypeOrNull
    {
        get => Require(_createErrorFromTypeOrNull);
        internal set => SetHandle(ref _createErrorFromTypeOrNull, value);
    }

    private MethodBuilder? _getName;
    public MethodBuilder GetName
    {
        get => Require(_getName);
        internal set => SetHandle(ref _getName, value);
    }

    private MethodBuilder? _getMessage;
    public MethodBuilder GetMessage
    {
        get => Require(_getMessage);
        internal set => SetHandle(ref _getMessage, value);
    }

    private MethodBuilder? _getStack;
    public MethodBuilder GetStack
    {
        get => Require(_getStack);
        internal set => SetHandle(ref _getStack, value);
    }

    private MethodBuilder? _setName;
    public MethodBuilder SetName
    {
        get => Require(_setName);
        internal set => SetHandle(ref _setName, value);
    }

    private MethodBuilder? _setMessage;
    public MethodBuilder SetMessage
    {
        get => Require(_setMessage);
        internal set => SetHandle(ref _setMessage, value);
    }

    private MethodBuilder? _defineMessageProperty;
    public MethodBuilder DefineMessageProperty
    {
        get => Require(_defineMessageProperty);
        internal set => SetHandle(ref _defineMessageProperty, value);
    }

    private MethodBuilder? _setStack;
    public MethodBuilder SetStack
    {
        get => Require(_setStack);
        internal set => SetHandle(ref _setStack, value);
    }

    private MethodBuilder? _getCause;
    public MethodBuilder GetCause
    {
        get => Require(_getCause);
        internal set => SetHandle(ref _getCause, value);
    }

    private MethodBuilder? _setCause;
    public MethodBuilder SetCause
    {
        get => Require(_setCause);
        internal set => SetHandle(ref _setCause, value);
    }

    private MethodBuilder? _isError;
    public MethodBuilder IsError
    {
        get => Require(_isError);
        internal set => SetHandle(ref _isError, value);
    }

    private MethodBuilder? _aggregateErrorGetErrors;
    public MethodBuilder AggregateErrorGetErrors
    {
        get => Require(_aggregateErrorGetErrors);
        internal set => SetHandle(ref _aggregateErrorGetErrors, value);
    }

    private Type? _thrownValueType;
    public Type ThrownValueType
    {
        get => Require(_thrownValueType);
        internal set => SetHandle(ref _thrownValueType, value);
    }

    private ConstructorBuilder? _thrownValueConstructor;
    public ConstructorBuilder ThrownValueConstructor
    {
        get => Require(_thrownValueConstructor);
        internal set => SetHandle(ref _thrownValueConstructor, value);
    }

    private MethodBuilder? _thrownValueValueGetter;
    public MethodBuilder ThrownValueValueGetter
    {
        get => Require(_thrownValueValueGetter);
        internal set => SetHandle(ref _thrownValueValueGetter, value);
    }

    internal void MarkCreateExceptionBodyEmitted()
    {
        EnsureMutable();
        if (_createExceptionBodyEmitted)
            throw new InvalidOperationException("Error CreateExceptionBody emission is already complete.");
        _createExceptionBodyEmitted = true;
    }

    internal void MarkCreateFromTypeBodyEmitted()
    {
        EnsureMutable();
        if (_createFromTypeBodyEmitted)
            throw new InvalidOperationException("Error CreateFromTypeBody emission is already complete.");
        _createFromTypeBodyEmitted = true;
    }

    internal void MarkPrototypeBodyEmitted()
    {
        EnsureMutable();
        if (_prototypeBodyEmitted)
            throw new InvalidOperationException("Error PrototypeBody emission is already complete.");
        _prototypeBodyEmitted = true;
    }

    internal void MarkNativePrototypeBodiesEmitted()
    {
        EnsureMutable();
        if (_nativePrototypeBodiesEmitted)
            throw new InvalidOperationException("Error NativePrototypeBodies emission is already complete.");
        _nativePrototypeBodiesEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Error metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Error metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Error metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Prototype;
        _ = PrototypePopulate;
        _ = TypeErrorPrototype;
        _ = TypeErrorPrototypePopulate;
        _ = RangeErrorPrototype;
        _ = RangeErrorPrototypePopulate;
        _ = ReferenceErrorPrototype;
        _ = ReferenceErrorPrototypePopulate;
        _ = SyntaxErrorPrototype;
        _ = SyntaxErrorPrototypePopulate;
        _ = URIErrorPrototype;
        _ = URIErrorPrototypePopulate;
        _ = EvalErrorPrototype;
        _ = EvalErrorPrototypePopulate;
        _ = AggregateErrorPrototype;
        _ = AggregateErrorPrototypePopulate;
        _ = ThrowStrictSyntaxError;
        _ = CreateException;
        _ = WrapException;
        _ = ThrowUndefinedVariable;
        _ = Type;
        _ = MessageConstructor;
        _ = NameMessageConstructor;
        _ = NameGetter;
        _ = NameSetter;
        _ = MessageGetter;
        _ = MessageSetter;
        _ = StackGetter;
        _ = StackSetter;
        _ = CapturedStackSetter;
        _ = CauseGetter;
        _ = CauseSetter;
        _ = HasCauseGetter;
        _ = CodeGetter;
        _ = CodeSetter;
        _ = SyscallGetter;
        _ = SyscallSetter;
        _ = TypeErrorType;
        _ = TypeErrorConstructor;
        _ = RangeErrorType;
        _ = RangeErrorConstructor;
        _ = ReferenceErrorType;
        _ = ReferenceErrorConstructor;
        _ = SyntaxErrorType;
        _ = SyntaxErrorConstructor;
        _ = URIErrorType;
        _ = URIErrorConstructor;
        _ = EvalErrorType;
        _ = EvalErrorConstructor;
        _ = AggregateErrorType;
        _ = AggregateErrorConstructor;
        _ = AggregateErrorErrorsGetter;
        _ = CreateError;
        _ = CreateErrorFromTypeOrNull;
        _ = GetName;
        _ = GetMessage;
        _ = GetStack;
        _ = SetName;
        _ = SetMessage;
        _ = DefineMessageProperty;
        _ = SetStack;
        _ = GetCause;
        _ = SetCause;
        _ = IsError;
        _ = AggregateErrorGetErrors;
        _ = ThrownValueType;
        _ = ThrownValueConstructor;
        _ = ThrownValueValueGetter;
        if (!_createExceptionBodyEmitted)
            throw new InvalidOperationException("Error CreateExceptionBody has not been emitted.");
        if (!_createFromTypeBodyEmitted)
            throw new InvalidOperationException("Error CreateFromTypeBody has not been emitted.");
        if (!_prototypeBodyEmitted)
            throw new InvalidOperationException("Error PrototypeBody has not been emitted.");
        if (!_nativePrototypeBodiesEmitted)
            throw new InvalidOperationException("Error NativePrototypeBodies has not been emitted.");
        IsComplete = true;
    }
}
