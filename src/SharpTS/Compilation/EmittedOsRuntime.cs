using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional OS helper declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedOsRuntime
{
    internal EmittedOsRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _freemem;
    public MethodBuilder Freemem
    {
        get => Require(_freemem);
        internal set => Set(ref _freemem, value);
    }

    private MethodBuilder? _loadavg;
    public MethodBuilder Loadavg
    {
        get => Require(_loadavg);
        internal set => Set(ref _loadavg, value);
    }

    private MethodBuilder? _networkInterfaces;
    public MethodBuilder NetworkInterfaces
    {
        get => Require(_networkInterfaces);
        internal set => Set(ref _networkInterfaces, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"OS metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("OS metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Freemem;
        _ = Loadavg;
        _ = NetworkInterfaces;
        IsComplete = true;
    }
}
