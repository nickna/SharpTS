using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required array storage metadata for one compilation. Declarations support forward
/// references; completion validates the handles and freezes the component.
/// </summary>
public sealed class EmittedArrayStorageRuntime
{
    internal EmittedArrayStorageRuntime() { }

    public bool IsComplete { get; private set; }

    private ArrayQueueTypeInfo? _numberQueue;
    public ArrayQueueTypeInfo NumberQueue
    {
        get => Require(_numberQueue);
        internal set => SetHandle(ref _numberQueue, value);
    }

    private ArrayQueueTypeInfo? _booleanQueue;
    public ArrayQueueTypeInfo BooleanQueue
    {
        get => Require(_booleanQueue);
        internal set => SetHandle(ref _booleanQueue, value);
    }

    private ArrayQueueTypeInfo? _numberQueueWithHoles;
    public ArrayQueueTypeInfo NumberQueueWithHoles
    {
        get => Require(_numberQueueWithHoles);
        internal set => SetHandle(ref _numberQueueWithHoles, value);
    }

    private ArrayQueueTypeInfo? _booleanQueueWithHoles;
    public ArrayQueueTypeInfo BooleanQueueWithHoles
    {
        get => Require(_booleanQueueWithHoles);
        internal set => SetHandle(ref _booleanQueueWithHoles, value);
    }

    // $ArrayHole singleton — sentinel for ECMA-262 array holes (index in range but never written).
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.ArrayHole
    private Type? _holeType;
    public Type HoleType
    {
        get => Require(_holeType);
        internal set => SetHandle(ref _holeType, value);
    }

    private FieldInfo? _holeInstance;
    public FieldInfo HoleInstance
    {
        get => Require(_holeInstance);
        internal set => SetHandle(ref _holeInstance, value);
    }

    // $Array type - emitted for standalone assemblies
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray
    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => SetHandle(ref _ctor, value);
    }

    private ConstructorBuilder? _literalCtor;
    public ConstructorBuilder LiteralCtor
    {
        get => Require(_literalCtor);
        internal set => SetHandle(ref _literalCtor, value);
    }

    private ConstructorBuilder? _numericLiteralCtor;
    public ConstructorBuilder NumericLiteralCtor
    {
        get => Require(_numericLiteralCtor);
        internal set => SetHandle(ref _numericLiteralCtor, value);
    }

    private ConstructorBuilder? _restCtor;
    public ConstructorBuilder RestCtor
    {
        get => Require(_restCtor);
        internal set => SetHandle(ref _restCtor, value);
    }

    private MethodBuilder? _createNumericRest;
    public MethodBuilder CreateNumericRest
    {
        get => Require(_createNumericRest);
        internal set => SetHandle(ref _createNumericRest, value);
    }

    private MethodBuilder? _appendRest;
    public MethodBuilder AppendRest
    {
        get => Require(_appendRest);
        internal set => SetHandle(ref _appendRest, value);
    }

    private MethodBuilder? _appendRestDouble;
    public MethodBuilder AppendRestDouble
    {
        get => Require(_appendRestDouble);
        internal set => SetHandle(ref _appendRestDouble, value);
    }

    private MethodBuilder? _appendRestValue;
    public MethodBuilder AppendRestValue
    {
        get => Require(_appendRestValue);
        internal set => SetHandle(ref _appendRestValue, value);
    }

    private MethodBuilder? _reserveRest;
    public MethodBuilder ReserveRest
    {
        get => Require(_reserveRest);
        internal set => SetHandle(ref _reserveRest, value);
    }

    private MethodBuilder? _appendNumericRestSource;
    public MethodBuilder AppendNumericRestSource
    {
        get => Require(_appendNumericRestSource);
        internal set => SetHandle(ref _appendNumericRestSource, value);
    }

    private MethodBuilder? _finishRest;
    public MethodBuilder FinishRest
    {
        get => Require(_finishRest);
        internal set => SetHandle(ref _finishRest, value);
    }

    private ConstructorBuilder? _ctorFromCtorArgs;
    /// <summary>$Array(object?[] ctorArgs) — ECMA-262 Array-constructor semantics for guest classes extending Array (#233): implicit ctors and super(...) chain through this.</summary>
    public ConstructorBuilder CtorFromCtorArgs
    {
        get => Require(_ctorFromCtorArgs);
        internal set => SetHandle(ref _ctorFromCtorArgs, value);
    }

    private MethodBuilder? _elementsGetter;
    public MethodBuilder ElementsGetter
    {
        get => Require(_elementsGetter);
        internal set => SetHandle(ref _elementsGetter, value);
    }

    private MethodBuilder? _freeze;
    public MethodBuilder Freeze
    {
        get => Require(_freeze);
        internal set => SetHandle(ref _freeze, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    private MethodBuilder? _set;
    public MethodBuilder Set
    {
        get => Require(_set);
        internal set => SetHandle(ref _set, value);
    }

    // Stage E.2 additions (long-indexed sparse/hole-aware API).
    // Mirrors SharpTSArray public surface. Legacy int-indexed Get/Set above
    // continue to work (they widen to the long path internally).
    // Count is inherited from List<object?> — no custom getter.
    private MethodBuilder? _longLengthGetter;
    public MethodBuilder LongLengthGetter
    {
        get => Require(_longLengthGetter);
        internal set => SetHandle(ref _longLengthGetter, value);
    }

    private MethodBuilder? _lengthGetter;
    public MethodBuilder LengthGetter
    {
        get => Require(_lengthGetter);
        internal set => SetHandle(ref _lengthGetter, value);
    }

    private MethodBuilder? _hasIndex;
    public MethodBuilder HasIndex
    {
        get => Require(_hasIndex);
        internal set => SetHandle(ref _hasIndex, value);
    }

    private MethodBuilder? _getLong;
    public MethodBuilder GetLong
    {
        get => Require(_getLong);
        internal set => SetHandle(ref _getLong, value);
    }

    private MethodBuilder? _setLong;
    public MethodBuilder SetLong
    {
        get => Require(_setLong);
        internal set => SetHandle(ref _setLong, value);
    }

    private MethodBuilder? _setStrictLong;
    public MethodBuilder SetStrictLong
    {
        get => Require(_setStrictLong);
        internal set => SetHandle(ref _setStrictLong, value);
    }

    private MethodBuilder? _setLength;
    public MethodBuilder SetLength
    {
        get => Require(_setLength);
        internal set => SetHandle(ref _setLength, value);
    }

    private MethodBuilder? _deleteAt;
    public MethodBuilder DeleteAt
    {
        get => Require(_deleteAt);
        internal set => SetHandle(ref _deleteAt, value);
    }

    // Unboxed packed-double elements-kind accessors (number[] unboxing project).
    // GetDouble/SetDouble/PushDouble are the fast paths the compiler emits at
    // statically-number[] sites; EnsureBoxed is the deopt (numeric -> boxed).
    private MethodBuilder? _canGetDouble;
    public MethodBuilder CanGetDouble
    {
        get => Require(_canGetDouble);
        internal set => SetHandle(ref _canGetDouble, value);
    }

    private MethodBuilder? _tryGetBoxedDouble;
    public MethodBuilder TryGetBoxedDouble
    {
        get => Require(_tryGetBoxedDouble);
        internal set => SetHandle(ref _tryGetBoxedDouble, value);
    }

    private MethodBuilder? _getDouble;
    public MethodBuilder GetDouble
    {
        get => Require(_getDouble);
        internal set => SetHandle(ref _getDouble, value);
    }

    private MethodBuilder? _setDouble;
    public MethodBuilder SetDouble
    {
        get => Require(_setDouble);
        internal set => SetHandle(ref _setDouble, value);
    }

    private MethodBuilder? _pushDouble;
    public MethodBuilder PushDouble
    {
        get => Require(_pushDouble);
        internal set => SetHandle(ref _pushDouble, value);
    }

    private MethodBuilder? _ensureDoubleCapacity;
    public MethodBuilder EnsureDoubleCapacity
    {
        get => Require(_ensureDoubleCapacity);
        internal set => SetHandle(ref _ensureDoubleCapacity, value);
    }

    private MethodBuilder? _ensureBoxed;
    public MethodBuilder EnsureBoxed
    {
        get => Require(_ensureBoxed);
        internal set => SetHandle(ref _ensureBoxed, value);
    }

    private MethodBuilder? _isNumericGetter;
    public MethodBuilder IsNumericGetter
    {
        get => Require(_isNumericGetter);
        internal set => SetHandle(ref _isNumericGetter, value);
    }

    private MethodBuilder? _numericCountGetter;
    public MethodBuilder NumericCountGetter
    {
        get => Require(_numericCountGetter);
        internal set => SetHandle(ref _numericCountGetter, value);
    }

    private MethodBuilder? _canMutateNumericGetter;
    public MethodBuilder CanMutateNumericGetter
    {
        get => Require(_canMutateNumericGetter);
        internal set => SetHandle(ref _canMutateNumericGetter, value);
    }

    private MethodBuilder? _shiftNumeric;
    public MethodBuilder ShiftNumeric
    {
        get => Require(_shiftNumeric);
        internal set => SetHandle(ref _shiftNumeric, value);
    }

    private MethodBuilder? _unshiftNumeric;
    public MethodBuilder UnshiftNumeric
    {
        get => Require(_unshiftNumeric);
        internal set => SetHandle(ref _unshiftNumeric, value);
    }

    private MethodBuilder? _cloneNumeric;
    public MethodBuilder CloneNumeric
    {
        get => Require(_cloneNumeric);
        internal set => SetHandle(ref _cloneNumeric, value);
    }

    private MethodBuilder? _sortNumeric;
    public MethodBuilder SortNumeric
    {
        get => Require(_sortNumeric);
        internal set => SetHandle(ref _sortNumeric, value);
    }

    // Flips an empty $Array into numeric (unboxed double[]) mode — emitted at
    // statically-number[] array-creation sites so escaping number[] arrays start
    // unboxed. No-op on a non-empty / sparse array (stays boxed).
    private MethodBuilder? _markNumeric;
    public MethodBuilder MarkNumeric
    {
        get => Require(_markNumeric);
        internal set => SetHandle(ref _markNumeric, value);
    }

    // Sets $Array._isNonExtensible so the unboxed PushDouble fast path refuses to append; called by
    // Object.seal / Object.preventExtensions (which otherwise only register the array externally).
    private MethodBuilder? _markNonExtensible;
    public MethodBuilder MarkNonExtensible
    {
        get => Require(_markNonExtensible);
        internal set => SetHandle(ref _markNonExtensible, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Array storage metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Array storage metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = NumberQueue;
        _ = BooleanQueue;
        _ = NumberQueueWithHoles;
        _ = BooleanQueueWithHoles;
        _ = HoleType;
        _ = HoleInstance;
        _ = Type;
        _ = Ctor;
        _ = LiteralCtor;
        _ = NumericLiteralCtor;
        _ = RestCtor;
        _ = CreateNumericRest;
        _ = AppendRest;
        _ = AppendRestDouble;
        _ = AppendRestValue;
        _ = ReserveRest;
        _ = AppendNumericRestSource;
        _ = FinishRest;
        _ = CtorFromCtorArgs;
        _ = ElementsGetter;
        _ = Freeze;
        _ = Get;
        _ = Set;
        _ = LongLengthGetter;
        _ = LengthGetter;
        _ = HasIndex;
        _ = GetLong;
        _ = SetLong;
        _ = SetStrictLong;
        _ = SetLength;
        _ = DeleteAt;
        _ = CanGetDouble;
        _ = TryGetBoxedDouble;
        _ = GetDouble;
        _ = SetDouble;
        _ = PushDouble;
        _ = EnsureDoubleCapacity;
        _ = EnsureBoxed;
        _ = IsNumericGetter;
        _ = NumericCountGetter;
        _ = CanMutateNumericGetter;
        _ = ShiftNumeric;
        _ = UnshiftNumeric;
        _ = CloneNumeric;
        _ = SortNumeric;
        _ = MarkNumeric;
        _ = MarkNonExtensible;
        ValidateQueue(NumberQueue, nameof(NumberQueue));
        ValidateQueue(BooleanQueue, nameof(BooleanQueue));
        ValidateQueue(NumberQueueWithHoles, nameof(NumberQueueWithHoles));
        ValidateQueue(BooleanQueueWithHoles, nameof(BooleanQueueWithHoles));
        IsComplete = true;
    }

    private static void ValidateQueue(ArrayQueueTypeInfo queue, string name)
    {
        _ = Require(queue.Type, $"{name}.{nameof(queue.Type)}");
        _ = Require(queue.Constructor, $"{name}.{nameof(queue.Constructor)}");
        _ = Require(queue.Elements, $"{name}.{nameof(queue.Elements)}");
        _ = Require(queue.Count, $"{name}.{nameof(queue.Count)}");
        _ = Require(queue.Push, $"{name}.{nameof(queue.Push)}");
        _ = Require(queue.Unshift, $"{name}.{nameof(queue.Unshift)}");
        _ = Require(queue.Shift, $"{name}.{nameof(queue.Shift)}");
        _ = Require(queue.Get, $"{name}.{nameof(queue.Get)}");
        _ = Require(queue.Set, $"{name}.{nameof(queue.Set)}");
        _ = Require(queue.Reserve, $"{name}.{nameof(queue.Reserve)}");
        // Only number queues declare unboxed numeric reads; boolean queues omit them.
        if (queue.Elements.Kind == ArrayElementsKind.Double)
        {
            _ = Require(queue.ShiftNumber, $"{name}.{nameof(queue.ShiftNumber)}");
            _ = Require(queue.GetNumber, $"{name}.{nameof(queue.GetNumber)}");
        }
    }
}
