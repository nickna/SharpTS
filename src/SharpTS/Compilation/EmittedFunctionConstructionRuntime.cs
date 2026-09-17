using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required function constructors, cached wrapper factory and dynamic construction declarations.</summary>
public sealed class EmittedFunctionConstructionRuntime
{
    internal EmittedFunctionConstructionRuntime() { }
    public bool IsComplete { get; private set; }

    private ConstructorBuilder? _constructor;
    public ConstructorBuilder Constructor
    {
        get => Require(_constructor);
        internal set => SetHandle(ref _constructor, value);
    }

    private ConstructorBuilder? _cachedConstructor;
    /// <summary>
    /// Alternative constructor with cached name/length: $TSFunction(object target, MethodInfo method, string name, int length).
    /// Use when MethodInfo might not support GetParameters() (e.g., MethodBuilder tokens in persisted assemblies).
    /// </summary>
    public ConstructorBuilder CachedConstructor
    {
        get => Require(_cachedConstructor);
        internal set => SetHandle(ref _cachedConstructor, value);
    }

    private MethodBuilder? _getOrCreate;
    public MethodBuilder GetOrCreate
    {
        get => Require(_getOrCreate);
        internal set => SetHandle(ref _getOrCreate, value);
    }

    private MethodBuilder? _construct;
    public MethodBuilder Construct
    {
        get => Require(_construct);
        internal set => SetHandle(ref _construct, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function construction metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function construction metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function construction metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Constructor;
        _ = CachedConstructor;
        _ = GetOrCreate;
        _ = Construct;
        IsComplete = true;
    }
}
