using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required function property lookup and constructor-capability declarations.</summary>
public sealed class EmittedFunctionIntrospectionRuntime
{
    internal EmittedFunctionIntrospectionRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _getProperty;
    public MethodBuilder GetProperty
    {
        get => Require(_getProperty);
        internal set => SetHandle(ref _getProperty, value);
    }

    private MethodBuilder? _isConstructor;
    public MethodBuilder IsConstructor
    {
        get => Require(_isConstructor);
        internal set => SetHandle(ref _isConstructor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Function introspection metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Function introspection metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Function introspection metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = GetProperty;
        _ = IsConstructor;
        IsComplete = true;
    }
}
