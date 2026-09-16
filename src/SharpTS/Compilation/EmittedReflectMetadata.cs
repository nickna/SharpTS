using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Reflect metadata and decorator declarations for one compilation.</summary>
public sealed class EmittedReflectMetadata
{
    internal EmittedReflectMetadata() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _define;
    public MethodBuilder Define
    {
        get => Require(_define);
        internal set => SetHandle(ref _define, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    private MethodBuilder? _has;
    public MethodBuilder Has
    {
        get => Require(_has);
        internal set => SetHandle(ref _has, value);
    }

    private MethodBuilder? _getKeys;
    public MethodBuilder GetKeys
    {
        get => Require(_getKeys);
        internal set => SetHandle(ref _getKeys, value);
    }

    private MethodBuilder? _delete;
    public MethodBuilder Delete
    {
        get => Require(_delete);
        internal set => SetHandle(ref _delete, value);
    }

    private ConstructorBuilder? _decoratorConstructor;
    public ConstructorBuilder DecoratorConstructor
    {
        get => Require(_decoratorConstructor);
        internal set => SetHandle(ref _decoratorConstructor, value);
    }

    private MethodBuilder? _decoratorInvoke;
    public MethodBuilder DecoratorInvoke
    {
        get => Require(_decoratorInvoke);
        internal set => SetHandle(ref _decoratorInvoke, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Reflect metadata metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Reflect metadata metadata emission is already complete.");
    }

    internal void ValidateEmission()
    {
        EnsureMutable();
        _ = Define;
        _ = Get;
        _ = Has;
        _ = GetKeys;
        _ = Delete;
        _ = DecoratorConstructor;
        _ = DecoratorInvoke;
    }

    internal void CompleteEmission()
    {
        ValidateEmission();
        IsComplete = true;
    }
}
