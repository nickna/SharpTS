using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional text-encoding declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedTextEncodingRuntime
{
    internal EmittedTextEncodingRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _encoderType;
    public TypeBuilder EncoderType
    {
        get => Require(_encoderType);
        internal set => Set(ref _encoderType, value);
    }

    private ConstructorBuilder? _encoderCtor;
    public ConstructorBuilder EncoderCtor
    {
        get => Require(_encoderCtor);
        internal set => Set(ref _encoderCtor, value);
    }

    private TypeBuilder? _decoderType;
    public TypeBuilder DecoderType
    {
        get => Require(_decoderType);
        internal set => Set(ref _decoderType, value);
    }

    private ConstructorBuilder? _decoderCtor;
    public ConstructorBuilder DecoderCtor
    {
        get => Require(_decoderCtor);
        internal set => Set(ref _decoderCtor, value);
    }

    private MethodBuilder? _decoderDecode;
    public MethodBuilder DecoderDecode
    {
        get => Require(_decoderDecode);
        internal set => Set(ref _decoderDecode, value);
    }

    private TypeBuilder? _decodeMethodType;
    public TypeBuilder DecodeMethodType
    {
        get => Require(_decodeMethodType);
        internal set => Set(ref _decodeMethodType, value);
    }

    private MethodBuilder? _decodeMethodInvoke;
    public MethodBuilder DecodeMethodInvoke
    {
        get => Require(_decodeMethodInvoke);
        internal set => Set(ref _decodeMethodInvoke, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Text-encoding metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Text-encoding metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = EncoderType;
        _ = EncoderCtor;
        _ = DecoderType;
        _ = DecoderCtor;
        _ = DecoderDecode;
        _ = DecodeMethodType;
        _ = DecodeMethodInvoke;
        IsComplete = true;
    }
}
