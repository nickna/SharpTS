using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required NodeError declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedNodeErrorRuntime
{
    internal EmittedNodeErrorRuntime() { }

    public bool IsComplete { get; private set; }

    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodBuilder? _codeGetter;
    public MethodBuilder CodeGetter
    {
        get => Require(_codeGetter);
        internal set => Set(ref _codeGetter, value);
    }

    private MethodBuilder? _syscallGetter;
    public MethodBuilder SyscallGetter
    {
        get => Require(_syscallGetter);
        internal set => Set(ref _syscallGetter, value);
    }

    private MethodBuilder? _pathGetter;
    public MethodBuilder PathGetter
    {
        get => Require(_pathGetter);
        internal set => Set(ref _pathGetter, value);
    }

    private MethodBuilder? _throw;
    public MethodBuilder Throw
    {
        get => Require(_throw);
        internal set => Set(ref _throw, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"NodeError metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("NodeError metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = CodeGetter;
        _ = SyscallGetter;
        _ = PathGetter;
        _ = Throw;
        IsComplete = true;
    }
}
