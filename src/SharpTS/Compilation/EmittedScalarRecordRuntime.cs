using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional scalar-record declarations for one emitted runtime.</summary>
public sealed class EmittedScalarRecordRuntime
{
    internal EmittedScalarRecordRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _arrayConstructor;
    public ConstructorBuilder ArrayConstructor
    {
        get => Require(_arrayConstructor);
        internal set => SetHandle(ref _arrayConstructor, value);
    }

    private MethodBuilder? _shapeGetter;
    public MethodBuilder ShapeGetter
    {
        get => Require(_shapeGetter);
        internal set => SetHandle(ref _shapeGetter, value);
    }

    private MethodBuilder? _getValue;
    public MethodBuilder GetValue
    {
        get => Require(_getValue);
        internal set => SetHandle(ref _getValue, value);
    }

    private MethodBuilder? _isMaterializedGetter;
    public MethodBuilder IsMaterializedGetter
    {
        get => Require(_isMaterializedGetter);
        internal set => SetHandle(ref _isMaterializedGetter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Scalar record metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Scalar record metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        EnsureMutable();
        _ = Type;
        _ = ArrayConstructor;
        _ = ShapeGetter;
        _ = GetValue;
        _ = IsMaterializedGetter;
    }

    internal void CompleteEmission()
    {
        ValidateDeclarations();
        IsComplete = true;
    }
}
