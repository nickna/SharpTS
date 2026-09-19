using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required shared runtime type, declared before helpers and completed after deferred bodies.</summary>
public sealed class EmittedRuntimeClass
{
    internal EmittedRuntimeClass() { }
    private TypeBuilder? _type;
    public bool IsComplete { get; private set; }

    /// <summary>The shared helper type, available for forward references before finalization.</summary>
    public TypeBuilder Type
    {
        get => _type ?? throw new InvalidOperationException("Shared runtime type has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_type is not null)
                throw new InvalidOperationException("Shared runtime type has already been declared.");
            _type = value;
        }
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if (!Type.IsCreated())
            throw new InvalidOperationException("Shared runtime type has not been finalized.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Shared runtime type emission is already complete.");
    }
}
