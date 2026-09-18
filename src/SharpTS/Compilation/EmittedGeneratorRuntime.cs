using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required synchronous and native-number generator protocol declarations.</summary>
public sealed class EmittedGeneratorRuntime
{
    internal EmittedGeneratorRuntime() { }
    public bool IsComplete { get; private set; }

    // Generator interface ($IGenerator extends IEnumerator<object> with Return/Throw)
    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private MethodBuilder? _iterator;
    public MethodBuilder Iterator
    {
        get => Require(_iterator);
        internal set => SetHandle(ref _iterator, value);
    }

    private MethodBuilder? _next;
    public MethodBuilder Next
    {
        get => Require(_next);
        internal set => SetHandle(ref _next, value);
    }

    private MethodBuilder? _return;
    public MethodBuilder Return
    {
        get => Require(_return);
        internal set => SetHandle(ref _return, value);
    }

    private MethodBuilder? _throw;
    public MethodBuilder Throw
    {
        get => Require(_throw);
        internal set => SetHandle(ref _throw, value);
    }

    // Private typed bridge implemented only by sync generators whose complete
    // yield set is proven numeric. Direct for...of lowering uses it to avoid
    // iterator-result allocation and per-yield number boxing while the public
    // $IGenerator ABI remains object-valued.
    private TypeBuilder? _numericType;
    public TypeBuilder NumericType
    {
        get => Require(_numericType);
        internal set => SetHandle(ref _numericType, value);
    }

    private MethodBuilder? _numericMoveNext;
    public MethodBuilder NumericMoveNext
    {
        get => Require(_numericMoveNext);
        internal set => SetHandle(ref _numericMoveNext, value);
    }

    private MethodBuilder? _numericCurrent;
    public MethodBuilder NumericCurrent
    {
        get => Require(_numericCurrent);
        internal set => SetHandle(ref _numericCurrent, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Generator protocol metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Generator protocol metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Generator protocol metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Iterator;
        _ = Next;
        _ = Return;
        _ = Throw;
        _ = NumericType;
        _ = NumericMoveNext;
        _ = NumericCurrent;
        IsComplete = true;
    }
}
