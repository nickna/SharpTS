using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required enum-reverse declaration for one emitted assembly.</summary>
public sealed class EmittedEnumRuntime
{
    internal EmittedEnumRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _getMemberName;
    public MethodBuilder GetMemberName
    {
        get => _getMemberName ?? throw new InvalidOperationException("Enum reverse-lookup metadata has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_getMemberName is not null)
                throw new InvalidOperationException("Enum reverse-lookup metadata has already been declared.");
            _getMemberName = value;
        }
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Enum reverse-lookup metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = GetMemberName;
        IsComplete = true;
    }
}
