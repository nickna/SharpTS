using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required object integrity and deleted-built-in metadata for one compilation.</summary>
public sealed class EmittedObjectStateRuntime
{
    internal EmittedObjectStateRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _isExtensibleBodyEmitted;

    private FieldBuilder? _frozenObjects;
    public FieldBuilder FrozenObjects
    {
        get => Require(_frozenObjects);
        internal set => SetHandle(ref _frozenObjects, value);
    }

    private FieldBuilder? _sealedObjects;
    public FieldBuilder SealedObjects
    {
        get => Require(_sealedObjects);
        internal set => SetHandle(ref _sealedObjects, value);
    }

    private FieldBuilder? _nonExtensibleObjects;
    public FieldBuilder NonExtensibleObjects
    {
        get => Require(_nonExtensibleObjects);
        internal set => SetHandle(ref _nonExtensibleObjects, value);
    }

    private FieldBuilder? _deletedBuiltins;
    /// <summary>
    /// Per-object set of deleted built-in property names — `name`/`length`
    /// on functions are configurable per ECMA-262 §17, but their values are
    /// synthetic in compiled mode (no real backing slot). This table records
    /// which names have been deleted so HasOwnPropertyHelper / GetFunctionMethod /
    /// ObjectGetOwnPropertyDescriptor can hide them after `delete fn.name`.
    /// Stored as ConditionalWeakTable&lt;object, HashSet&lt;string&gt;&gt; via the
    /// open generic ConditionalWeakTable&lt;object, object&gt; (value is downcast
    /// to HashSet&lt;string&gt; on read).
    /// </summary>
    public FieldBuilder DeletedBuiltins
    {
        get => Require(_deletedBuiltins);
        internal set => SetHandle(ref _deletedBuiltins, value);
    }

    private MethodBuilder? _markBuiltinDeleted;
    /// <summary>$Runtime.MarkBuiltinDeleted(object obj, string name) — adds <paramref name="name"/> to the deleted-set for <paramref name="obj"/>, lazily creating the set.</summary>
    public MethodBuilder MarkBuiltinDeleted
    {
        get => Require(_markBuiltinDeleted);
        internal set => SetHandle(ref _markBuiltinDeleted, value);
    }

    private MethodBuilder? _isBuiltinDeleted;
    /// <summary>$Runtime.IsBuiltinDeleted(object obj, string name) — true iff <paramref name="name"/> was deleted on <paramref name="obj"/>.</summary>
    public MethodBuilder IsBuiltinDeleted
    {
        get => Require(_isBuiltinDeleted);
        internal set => SetHandle(ref _isBuiltinDeleted, value);
    }

    private MethodBuilder? _freeze;
    public MethodBuilder Freeze
    {
        get => Require(_freeze);
        internal set => SetHandle(ref _freeze, value);
    }

    private MethodBuilder? _seal;
    public MethodBuilder Seal
    {
        get => Require(_seal);
        internal set => SetHandle(ref _seal, value);
    }

    private MethodBuilder? _isFrozen;
    public MethodBuilder IsFrozen
    {
        get => Require(_isFrozen);
        internal set => SetHandle(ref _isFrozen, value);
    }

    private MethodBuilder? _isSealed;
    public MethodBuilder IsSealed
    {
        get => Require(_isSealed);
        internal set => SetHandle(ref _isSealed, value);
    }

    private MethodBuilder? _preventExtensions;
    public MethodBuilder PreventExtensions
    {
        get => Require(_preventExtensions);
        internal set => SetHandle(ref _preventExtensions, value);
    }

    private MethodBuilder? _isExtensible;
    public MethodBuilder IsExtensible
    {
        get => Require(_isExtensible);
        internal set => SetHandle(ref _isExtensible, value);
    }

    internal void MarkIsExtensibleBodyEmitted()
    {
        EnsureMutable();
        if (_isExtensibleBodyEmitted)
            throw new InvalidOperationException("Object extensibility body emission is already complete.");
        _isExtensibleBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object state metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object state metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object state metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = FrozenObjects;
        _ = SealedObjects;
        _ = NonExtensibleObjects;
        _ = DeletedBuiltins;
        _ = MarkBuiltinDeleted;
        _ = IsBuiltinDeleted;
        _ = Freeze;
        _ = Seal;
        _ = IsFrozen;
        _ = IsSealed;
        _ = PreventExtensions;
        _ = IsExtensible;
        if (!_isExtensibleBodyEmitted)
            throw new InvalidOperationException("Object extensibility body has not been emitted.");
        IsComplete = true;
    }
}
