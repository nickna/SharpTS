using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Reflect namespace declarations and callable wrapper registry for one compilation.</summary>
public sealed class EmittedReflectNamespace
{
    internal EmittedReflectNamespace()
    {
        ValueFormMethods = new ReadOnlyDictionary<string, MethodBuilder>(_valueFormMethods);
    }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _singletonField;
    public FieldBuilder SingletonField
    {
        get => Require(_singletonField);
        internal set => SetHandle(ref _singletonField, value);
    }

    private MethodBuilder? _singletonPopulateMethod;
    public MethodBuilder SingletonPopulateMethod
    {
        get => Require(_singletonPopulateMethod);
        internal set => SetHandle(ref _singletonPopulateMethod, value);
    }

    private MethodBuilder? _apply;
    public MethodBuilder Apply
    {
        get => Require(_apply);
        internal set => SetHandle(ref _apply, value);
    }

    private MethodBuilder? _construct;
    public MethodBuilder Construct
    {
        get => Require(_construct);
        internal set => SetHandle(ref _construct, value);
    }

    private MethodBuilder? _deleteProperty;
    public MethodBuilder DeleteProperty
    {
        get => Require(_deleteProperty);
        internal set => SetHandle(ref _deleteProperty, value);
    }

    private MethodBuilder? _preventExtensions;
    public MethodBuilder PreventExtensions
    {
        get => Require(_preventExtensions);
        internal set => SetHandle(ref _preventExtensions, value);
    }

    private MethodBuilder? _setPrototypeOf;
    public MethodBuilder SetPrototypeOf
    {
        get => Require(_setPrototypeOf);
        internal set => SetHandle(ref _setPrototypeOf, value);
    }

    private MethodBuilder? _ownKeys;
    public MethodBuilder OwnKeys
    {
        get => Require(_ownKeys);
        internal set => SetHandle(ref _ownKeys, value);
    }

    private static readonly string[] RequiredValueFormNames =
    [
        "apply",
        "construct",
        "defineProperty",
        "deleteProperty",
        "get",
        "getOwnPropertyDescriptor",
        "getPrototypeOf",
        "has",
        "isExtensible",
        "ownKeys",
        "preventExtensions",
        "set",
        "setPrototypeOf",
    ];

    private readonly Dictionary<string, MethodBuilder> _valueFormMethods = new(StringComparer.Ordinal);

    /// <summary>Read-only live view of declared wrappers; registration ends at completion.</summary>
    public IReadOnlyDictionary<string, MethodBuilder> ValueFormMethods { get; }

    internal void RegisterValueFormMethod(string name, MethodBuilder method)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(method);
        if (!RequiredValueFormNames.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown Reflect value-form method '{name}'.", nameof(name));
        if (!_valueFormMethods.TryAdd(name, method))
            throw new InvalidOperationException($"Reflect value-form method '{name}' has already been declared.");
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Reflect namespace metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Reflect namespace metadata emission is already complete.");
    }

    internal void ValidateEmission()
    {
        EnsureMutable();
        _ = SingletonField;
        _ = SingletonPopulateMethod;
        _ = Apply;
        _ = Construct;
        _ = DeleteProperty;
        _ = PreventExtensions;
        _ = SetPrototypeOf;
        _ = OwnKeys;
        foreach (var name in RequiredValueFormNames)
            if (!_valueFormMethods.ContainsKey(name))
                throw new InvalidOperationException($"Reflect value-form method '{name}' has not been declared.");
    }

    internal void CompleteEmission()
    {
        ValidateEmission();
        IsComplete = true;
    }
}
