using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Required WebCrypto accessor metadata with an optional implementation. The accessor
/// is declared even for tree-shaken programs, where its body returns null.
/// </summary>
public sealed class EmittedWebCryptoRuntime
{
    internal EmittedWebCryptoRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _getObject;
    public MethodBuilder GetObject
    {
        get => _getObject ?? throw new InvalidOperationException("WebCrypto metadata 'GetObject' has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            _getObject = value;
        }
    }

    public EmittedWebCryptoImplementation? Implementation { get; private set; }

    public EmittedWebCryptoImplementation RequireImplementation() => Implementation
        ?? throw new InvalidOperationException("WebCrypto implementation was not enabled for this compilation.");

    internal void BeginImplementationEmission()
    {
        EnsureMutable();
        if (Implementation is not null)
            throw new InvalidOperationException("WebCrypto implementation emission has already started.");
        Implementation = new EmittedWebCryptoImplementation();
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = GetObject;
        Implementation?.CompleteEmission();
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("WebCrypto metadata emission is already complete.");
    }
}
