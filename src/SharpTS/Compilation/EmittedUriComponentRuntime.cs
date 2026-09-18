using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required URI component declarations for one emitted assembly.</summary>
public sealed class EmittedUriComponentRuntime
{
    internal EmittedUriComponentRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _encode;
    public MethodBuilder Encode
    {
        get => Require(_encode);
        internal set => SetHandle(ref _encode, value);
    }

    private MethodBuilder? _decode;
    public MethodBuilder Decode
    {
        get => Require(_decode);
        internal set => SetHandle(ref _decode, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("URI component metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("URI component metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("URI component metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Encode;
        _ = Decode;
        IsComplete = true;
    }
}
