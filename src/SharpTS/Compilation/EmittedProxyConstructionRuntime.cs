using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Proxy factory declarations for one emitted assembly.</summary>
public sealed class EmittedProxyConstructionRuntime
{
    internal EmittedProxyConstructionRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _createRevocable;
    public MethodBuilder CreateRevocable
    {
        get => Require(_createRevocable);
        internal set => SetHandle(ref _createRevocable, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Proxy-construction metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Proxy-construction metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Proxy-construction metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = CreateRevocable;
        IsComplete = true;
    }
}
