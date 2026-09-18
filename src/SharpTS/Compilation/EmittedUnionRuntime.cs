using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required union-value protocol declarations for one emitted assembly.</summary>
public sealed class EmittedUnionRuntime
{
    internal EmittedUnionRuntime() { }
    public bool IsComplete { get; private set; }

    private Type? _interface;
    public Type Interface
    {
        get => Require(_interface);
        internal set => SetHandle(ref _interface, value);
    }

    private MethodInfo? _valueGetter;
    public MethodInfo ValueGetter
    {
        get => Require(_valueGetter);
        internal set => SetHandle(ref _valueGetter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Union-value metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Union-value metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Union-value metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Interface;
        _ = ValueGetter;
        IsComplete = true;
    }
}
