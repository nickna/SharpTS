using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Receiver-aware assignment declarations selected by Reflect or Proxy for one compilation.</summary>
public sealed class EmittedReflectAssignment
{
    internal EmittedReflectAssignment() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _set;
    public MethodBuilder Set
    {
        get => Require(_set);
        internal set => SetHandle(ref _set, value);
    }

    private MethodBuilder? _defineProperty;
    public MethodBuilder DefineProperty
    {
        get => Require(_defineProperty);
        internal set => SetHandle(ref _defineProperty, value);
    }

    private MethodBuilder? _definePropertyObjectAdapter;
    public MethodBuilder DefinePropertyObjectAdapter
    {
        get => Require(_definePropertyObjectAdapter);
        internal set => SetHandle(ref _definePropertyObjectAdapter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Reflect assignment metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Reflect assignment metadata emission is already complete.");
    }

    internal void ValidateEmission()
    {
        EnsureMutable();
        _ = Set;
        _ = DefineProperty;
        _ = DefinePropertyObjectAdapter;
    }

    internal void CompleteEmission()
    {
        ValidateEmission();
        IsComplete = true;
    }
}
