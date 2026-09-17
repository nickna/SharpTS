using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required named, computed, field, list and element reading metadata for one compilation.</summary>
public sealed class EmittedObjectReadRuntime
{
    internal EmittedObjectReadRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _propertyBodyEmitted;

    private MethodBuilder? _property;
    public MethodBuilder Property
    {
        get => Require(_property);
        internal set => SetHandle(ref _property, value);
    }

    private MethodBuilder? _fieldsProperty;
    public MethodBuilder FieldsProperty
    {
        get => Require(_fieldsProperty);
        internal set => SetHandle(ref _fieldsProperty, value);
    }

    private MethodBuilder? _listProperty;
    public MethodBuilder ListProperty
    {
        get => Require(_listProperty);
        internal set => SetHandle(ref _listProperty, value);
    }

    private MethodBuilder? _index;
    /// <summary>$Runtime.FunctionProtoCall(__this, args) — ECMA-262 §20.2.3.3 Function.prototype.call. Dispatches __this with args[0] as thisArg, args[1..] as call args.</summary>
    /// <summary>$Runtime.FunctionProtoApply(__this, args) — ECMA-262 §20.2.3.1 Function.prototype.apply. Dispatches __this with args[0] as thisArg, args[1] (array-like) as call args.</summary>
    /// <summary>$Runtime.FunctionProtoBind(__this, args) — ECMA-262 §20.2.3.2 Function.prototype.bind. Returns a $BoundTSFunction (or shim) capturing __this + thisArg + boundArgs.</summary>
    /// <summary>$Runtime.FunctionProtoToString(__this) — ECMA-262 §20.2.3.5. Stringifies the function (or returns native-source-like text).</summary>
    public MethodBuilder Index
    {
        get => Require(_index);
        internal set => SetHandle(ref _index, value);
    }

    private MethodBuilder? _length;
    public MethodBuilder Length
    {
        get => Require(_length);
        internal set => SetHandle(ref _length, value);
    }

    private MethodBuilder? _element;
    public MethodBuilder Element
    {
        get => Require(_element);
        internal set => SetHandle(ref _element, value);
    }

    internal void MarkPropertyBodyEmitted()
    {
        EnsureMutable();
        _ = Property;
        if (_propertyBodyEmitted)
            throw new InvalidOperationException("Object read Property body emission is already complete.");
        _propertyBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object read metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object read metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object read metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Property;
        _ = FieldsProperty;
        _ = ListProperty;
        _ = Index;
        _ = Length;
        _ = Element;
        if (!_propertyBodyEmitted)
            throw new InvalidOperationException("Object read Property body has not been emitted.");
        IsComplete = true;
    }
}
