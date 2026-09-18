using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required resource-disposal declaration for one emitted assembly.</summary>
public sealed class EmittedResourceDisposalRuntime
{
    internal EmittedResourceDisposalRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _dispose;
    public MethodBuilder Dispose
    {
        get => _dispose ?? throw new InvalidOperationException("Resource-disposal metadata has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_dispose is not null)
                throw new InvalidOperationException("Resource-disposal metadata has already been declared.");
            _dispose = value;
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Resource-disposal metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Dispose;
        IsComplete = true;
    }
}
