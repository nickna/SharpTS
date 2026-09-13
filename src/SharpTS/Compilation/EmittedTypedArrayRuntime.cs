using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Required TypedArray detection metadata with an optional implementation. The detection
/// helper is declared even for tree-shaken programs, where its body returns false.
/// </summary>
public sealed class EmittedTypedArrayRuntime
{
    internal EmittedTypedArrayRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _isTypedArray;
    public MethodBuilder IsTypedArray
    {
        get => _isTypedArray ?? throw new InvalidOperationException("TypedArray metadata 'IsTypedArray' has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            _isTypedArray = value;
        }
    }

    public EmittedTypedArrayImplementation? Implementation { get; private set; }

    public EmittedTypedArrayImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("TypedArray implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("TypedArray implementation emission has already started.");
        Implementation = new EmittedTypedArrayImplementation();
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = IsTypedArray;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("TypedArray metadata emission is already complete.");
    }
}
