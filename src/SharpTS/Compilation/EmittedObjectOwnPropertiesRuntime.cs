using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required own-property predicates and legacy accessor helpers for one compilation.</summary>
public sealed class EmittedObjectOwnPropertiesRuntime
{
    internal EmittedObjectOwnPropertiesRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _hasOwnProperty;
    /// <summary>$Runtime.HasOwnPropertyHelper(obj, name) — backs <c>obj.hasOwnProperty(name)</c> for $TSFunction / $Object / Dictionary / List receivers.</summary>
    public MethodBuilder HasOwnProperty
    {
        get => Require(_hasOwnProperty);
        internal set => SetHandle(ref _hasOwnProperty, value);
    }

    private MethodBuilder? _isEnumerable;
    /// <summary>$Runtime.PropertyIsEnumerableHelper(obj, name) — backs <c>obj.propertyIsEnumerable(name)</c>. Honors PDS descriptor's Enumerable bit; falls back to <c>hasOwn(name)</c> (default-enumerable for plain dict entries).</summary>
    public MethodBuilder IsEnumerable
    {
        get => Require(_isEnumerable);
        internal set => SetHandle(ref _isEnumerable, value);
    }

    private MethodBuilder? _lookupGetter;
    /// <summary>$Runtime.LookupGetterHelper(obj, key) — backs <c>Object.prototype.__lookupGetter__</c> (ECMA-262 §B.2.2.4). Walks prototype chain.</summary>
    public MethodBuilder LookupGetter
    {
        get => Require(_lookupGetter);
        internal set => SetHandle(ref _lookupGetter, value);
    }

    private MethodBuilder? _lookupSetter;
    /// <summary>$Runtime.LookupSetterHelper(obj, key) — backs <c>Object.prototype.__lookupSetter__</c> (ECMA-262 §B.2.2.5).</summary>
    public MethodBuilder LookupSetter
    {
        get => Require(_lookupSetter);
        internal set => SetHandle(ref _lookupSetter, value);
    }

    private MethodBuilder? _defineGetter;
    /// <summary>$Runtime.DefineGetterHelper(obj, key, fn) — backs <c>Object.prototype.__defineGetter__</c> (ECMA-262 §B.2.2.2).</summary>
    public MethodBuilder DefineGetter
    {
        get => Require(_defineGetter);
        internal set => SetHandle(ref _defineGetter, value);
    }

    private MethodBuilder? _defineSetter;
    /// <summary>$Runtime.DefineSetterHelper(obj, key, fn) — backs <c>Object.prototype.__defineSetter__</c> (ECMA-262 §B.2.2.3).</summary>
    public MethodBuilder DefineSetter
    {
        get => Require(_defineSetter);
        internal set => SetHandle(ref _defineSetter, value);
    }

    private MethodBuilder? _hasOwn;
    public MethodBuilder HasOwn
    {
        get => Require(_hasOwn);
        internal set => SetHandle(ref _hasOwn, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Own-property metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Own-property metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Own-property metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = HasOwnProperty;
        _ = IsEnumerable;
        _ = LookupGetter;
        _ = LookupSetter;
        _ = DefineGetter;
        _ = DefineSetter;
        _ = HasOwn;
        IsComplete = true;
    }
}
