using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required language sentinel declarations for one emitted assembly.</summary>
public sealed class EmittedSentinelRuntime
{
    internal EmittedSentinelRuntime() { }
    public bool IsComplete { get; private set; }

    private Type? _undefinedType;
    public Type UndefinedType
    {
        get => Require(_undefinedType);
        internal set => SetHandle(ref _undefinedType, value);
    }

    private FieldInfo? _undefinedInstance;
    public FieldInfo UndefinedInstance
    {
        get => Require(_undefinedInstance);
        internal set => SetHandle(ref _undefinedInstance, value);
    }

    private Type? _lexicalUninitializedType;
    public Type LexicalUninitializedType
    {
        get => Require(_lexicalUninitializedType);
        internal set => SetHandle(ref _lexicalUninitializedType, value);
    }

    private FieldInfo? _lexicalUninitializedInstance;
    public FieldInfo LexicalUninitializedInstance
    {
        get => Require(_lexicalUninitializedInstance);
        internal set => SetHandle(ref _lexicalUninitializedInstance, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Sentinel metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Sentinel metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Sentinel metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = UndefinedType;
        _ = UndefinedInstance;
        _ = LexicalUninitializedType;
        _ = LexicalUninitializedInstance;
        IsComplete = true;
    }
}
