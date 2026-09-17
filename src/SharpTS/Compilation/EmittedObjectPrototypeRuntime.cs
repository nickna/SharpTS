using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required Object prototype and prototype-chain operations for one compilation.</summary>
public sealed class EmittedObjectPrototypeRuntime
{
    internal EmittedObjectPrototypeRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _getPrototypeOfBodyEmitted;
    private bool _populateBodyEmitted;

    private FieldBuilder? _prototype;
    /// <summary>Object.prototype singleton dict, populated lazily with hasOwnProperty/isPrototypeOf/toString/valueOf wrappers.</summary>
    public FieldBuilder Prototype
    {
        get => Require(_prototype);
        internal set => SetHandle(ref _prototype, value);
    }

    private MethodBuilder? _populate;
    /// <summary>Idempotent populate for <see cref="Prototype"/>.</summary>
    public MethodBuilder Populate
    {
        get => Require(_populate);
        internal set => SetHandle(ref _populate, value);
    }

    private MethodBuilder? _toStringMethod;
    /// <summary>$Runtime.ObjectProtoToString(this) — ECMA-262 19.1.3.6 toString returns "[object X]" branded by receiver type. Wired into Object.prototype.toString slot for borrowed-method dispatch (`obj.toString = Object.prototype.toString; obj.toString()`).</summary>
    public MethodBuilder ToStringMethod
    {
        get => Require(_toStringMethod);
        internal set => SetHandle(ref _toStringMethod, value);
    }

    private MethodBuilder? _valueOf;
    /// <summary>$Runtime.ObjectProtoValueOf(this) — ECMA-262 19.1.3.7. Returns the receiver as-is (primitives stay primitive, objects stay objects). Wired into Object.prototype.valueOf so the materializer's ToPrimitive picks up the inherited method and sees a non-primitive return for plain objects (triggering the toString fallback).</summary>
    public MethodBuilder ValueOf
    {
        get => Require(_valueOf);
        internal set => SetHandle(ref _valueOf, value);
    }

    private MethodBuilder? _toLocaleString;
    /// <summary>$Runtime.ObjectProtoToLocaleString(this) — ECMA-262 20.1.3.5. Wraps ObjectProtoToString with the null/undef TypeError throw mandated by ToObject(this); other receivers delegate to ObjectProtoToString.</summary>
    public MethodBuilder ToLocaleString
    {
        get => Require(_toLocaleString);
        internal set => SetHandle(ref _toLocaleString, value);
    }

    private MethodBuilder? _isPrototypeOf;
    /// <summary>$Runtime.IsPrototypeOfHelper(receiver, target) — backs <c>receiver.isPrototypeOf(target)</c>; walks target's prototype chain via PDS.</summary>
    public MethodBuilder IsPrototypeOf
    {
        get => Require(_isPrototypeOf);
        internal set => SetHandle(ref _isPrototypeOf, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _createValueForm;
    /// <summary>
    /// Value-form dispatch wrapper for Object.create: maps a null (reflection-
    /// padded, i.e. absent) props slot to $Undefined before delegating, so
    /// under-application doesn't trip ObjectCreate's explicit-null TypeError.
    /// </summary>
    public MethodBuilder CreateValueForm
    {
        get => Require(_createValueForm);
        internal set => SetHandle(ref _createValueForm, value);
    }

    private MethodBuilder? _getPrototypeOf;
    public MethodBuilder GetPrototypeOf
    {
        get => Require(_getPrototypeOf);
        internal set => SetHandle(ref _getPrototypeOf, value);
    }

    private MethodBuilder? _setPrototypeOf;
    public MethodBuilder SetPrototypeOf
    {
        get => Require(_setPrototypeOf);
        internal set => SetHandle(ref _setPrototypeOf, value);
    }

    internal void MarkGetPrototypeOfBodyEmitted()
    {
        EnsureMutable();
        if (_getPrototypeOfBodyEmitted)
            throw new InvalidOperationException("ObjectPrototypes GetPrototypeOfBody emission is already complete.");
        _getPrototypeOfBodyEmitted = true;
    }

    internal void MarkPopulateBodyEmitted()
    {
        EnsureMutable();
        if (_populateBodyEmitted)
            throw new InvalidOperationException("ObjectPrototypes PopulateBody emission is already complete.");
        _populateBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Prototype metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Prototype metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Prototype metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Prototype;
        _ = Populate;
        _ = ToStringMethod;
        _ = ValueOf;
        _ = ToLocaleString;
        _ = IsPrototypeOf;
        _ = Create;
        _ = CreateValueForm;
        _ = GetPrototypeOf;
        _ = SetPrototypeOf;
        if (!_getPrototypeOfBodyEmitted)
            throw new InvalidOperationException("ObjectPrototypes GetPrototypeOfBody has not been emitted.");
        if (!_populateBodyEmitted)
            throw new InvalidOperationException("ObjectPrototypes PopulateBody has not been emitted.");
        IsComplete = true;
    }
}
