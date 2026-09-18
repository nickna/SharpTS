using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional numeric iterator-result value type and baked member metadata.</summary>
public sealed class EmittedStableIteratorResultRuntime
{
    internal EmittedStableIteratorResultRuntime() { }
    public bool IsComplete { get; private set; }

    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorInfo? _ctor;
    public ConstructorInfo Ctor
    {
        get => Require(_ctor);
        internal set => SetHandle(ref _ctor, value);
    }

    private FieldInfo? _value;
    public FieldInfo Value
    {
        get => Require(_value);
        internal set => SetHandle(ref _value, value);
    }

    private FieldInfo? _done;
    public FieldInfo Done
    {
        get => Require(_done);
        internal set => SetHandle(ref _done, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Stable iterator result metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Stable iterator result metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Stable iterator result metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = Value;
        _ = Done;
        IsComplete = true;
    }
}
