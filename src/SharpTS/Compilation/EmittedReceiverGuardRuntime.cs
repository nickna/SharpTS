using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required receiver-validation declaration for one emitted assembly.</summary>
public sealed class EmittedReceiverGuardRuntime
{
    internal EmittedReceiverGuardRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _requireObjectCoercibleThis;
    public MethodBuilder RequireObjectCoercibleThis
    {
        get => _requireObjectCoercibleThis ?? throw new InvalidOperationException("Receiver-validation metadata has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_requireObjectCoercibleThis is not null)
                throw new InvalidOperationException("Receiver-validation metadata has already been declared.");
            _requireObjectCoercibleThis = value;
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Receiver-validation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = RequireObjectCoercibleThis;
        IsComplete = true;
    }
}
