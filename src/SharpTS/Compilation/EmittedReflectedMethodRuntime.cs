using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required reflected-method lookup, wrapper cache and staged callable metadata.</summary>
public sealed class EmittedReflectedMethodRuntime
{
    internal EmittedReflectedMethodRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _callableFinalized;
    internal System.Reflection.MethodInfo? NativePropertyGetter { get; private set; }
    internal EmittedNativeMethodLookupRuntime? NativeMethodLookup { get; private set; }

    internal void DeclareNativeMethodLookup(ModuleBuilder module, EmittedNativeMethodLookupRuntime lookup)
    {
        EnsureMutable();
        if (NativeMethodLookup is not null || !ReferenceEquals(module, lookup.Module))
            throw new InvalidOperationException("Native method lookup requires one same-output query family.");
        NativeMethodLookup = lookup;
    }

    internal void DeclareNativePropertyGetter(ModuleBuilder module, MethodBuilder getter)
    {
        EnsureMutable();
        if (NativePropertyGetter is not null || !ReferenceEquals(module, getter.Module) || !getter.IsStatic || getter.IsGenericMethod ||
            getter.ReturnType != typeof(System.Reflection.MethodInfo) || getter.GetParameters().Length != 2 ||
            getter.GetParameters()[0].ParameterType != typeof(Type) || getter.GetParameters()[1].ParameterType != typeof(string))
            throw new InvalidOperationException("Native property lookup requires one exact static Type/string metadata helper.");
        NativePropertyGetter = getter;
    }

    public MethodBuilder? NativeProxyRejection { get; private set; }

    internal void DeclareNativeProxyRejection(TypeBuilder owner, MethodBuilder method)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(method);
        if (NativeProxyRejection is not null || method.DeclaringType != owner || !method.IsStatic || method.IsGenericMethod ||
            method.ReturnType != typeof(object) || method.GetParameters().Length != 0)
            throw new InvalidOperationException("Native Proxy rejection requires one exact static object-returning helper.");
        NativeProxyRejection = method;
    }

    private MethodBuilder? _toPascalCase;
    public MethodBuilder ToPascalCase
    {
        get => Require(_toPascalCase);
        internal set => SetHandle(ref _toPascalCase, value);
    }

    private MethodBuilder? _superMethod;
    public MethodBuilder SuperMethod
    {
        get => Require(_superMethod);
        internal set => SetHandle(ref _superMethod, value);
    }

    private MethodBuilder? _findMethod;
    public MethodBuilder FindMethod
    {
        get => Require(_findMethod);
        internal set => SetHandle(ref _findMethod, value);
    }

    private FieldBuilder? _cache;
    /// <summary>
    /// Weak per-receiver cache of reflected CLR method wrappers used by
    /// <see cref="EmittedObjectReadRuntime.FieldsProperty"/>. Values are
    /// ConcurrentDictionary&lt;string, object&gt; instances whose entries are
    /// emitted $TSFunction objects.
    /// </summary>
    public FieldBuilder Cache
    {
        get => Require(_cache);
        internal set => SetHandle(ref _cache, value);
    }

    private TypeBuilder? _callableType;
    public TypeBuilder CallableType
    {
        get => Require(_callableType);
        internal set => SetHandle(ref _callableType, value);
    }

    private ConstructorBuilder? _callableConstructor;
    public ConstructorBuilder CallableConstructor
    {
        get => Require(_callableConstructor);
        internal set => SetHandle(ref _callableConstructor, value);
    }

    private MethodBuilder? _callableInvoke;
    public MethodBuilder CallableInvoke
    {
        get => Require(_callableInvoke);
        internal set => SetHandle(ref _callableInvoke, value);
    }

    private FieldBuilder? _callableField;
    public FieldBuilder CallableField
    {
        get => Require(_callableField);
        internal set => SetHandle(ref _callableField, value);
    }

    private MethodBuilder? _invokeUnwrapped;
    public MethodBuilder InvokeUnwrapped
    {
        get => Require(_invokeUnwrapped);
        internal set => SetHandle(ref _invokeUnwrapped, value);
    }

    internal void MarkCallableFinalized()
    {
        EnsureMutable();
        _ = CallableType;
        _ = CallableConstructor;
        _ = CallableInvoke;
        _ = CallableField;
        if (_callableFinalized)
            throw new InvalidOperationException("Reflected callable is already finalized.");
        _callableFinalized = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Reflected method metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Reflected method metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Reflected method metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ToPascalCase;
        _ = SuperMethod;
        _ = FindMethod;
        _ = Cache;
        _ = CallableType;
        _ = CallableConstructor;
        _ = CallableInvoke;
        _ = CallableField;
        _ = InvokeUnwrapped;
        if (!_callableFinalized)
            throw new InvalidOperationException("Reflected callable has not been finalized.");
        if (NativeProxyRejection is { } rejection && rejection.GetILGenerator().ILOffset == 0)
            throw new InvalidOperationException("Native Proxy rejection body is incomplete.");
        IsComplete = true;
    }
}
