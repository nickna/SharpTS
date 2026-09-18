using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required lazy iterator adapters and iterator helper methods.</summary>
public sealed class EmittedIteratorHelpersRuntime
{
    internal EmittedIteratorHelpersRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _normalizeToEnumerator;
    public MethodBuilder NormalizeToEnumerator
    {
        get => Require(_normalizeToEnumerator);
        internal set => SetHandle(ref _normalizeToEnumerator, value);
    }

    private MethodBuilder? _map;
    public MethodBuilder Map
    {
        get => Require(_map);
        internal set => SetHandle(ref _map, value);
    }

    private MethodBuilder? _filter;
    public MethodBuilder Filter
    {
        get => Require(_filter);
        internal set => SetHandle(ref _filter, value);
    }

    private MethodBuilder? _take;
    public MethodBuilder Take
    {
        get => Require(_take);
        internal set => SetHandle(ref _take, value);
    }

    private MethodBuilder? _drop;
    public MethodBuilder Drop
    {
        get => Require(_drop);
        internal set => SetHandle(ref _drop, value);
    }

    private MethodBuilder? _flatMap;
    public MethodBuilder FlatMap
    {
        get => Require(_flatMap);
        internal set => SetHandle(ref _flatMap, value);
    }

    private MethodBuilder? _reduce;
    public MethodBuilder Reduce
    {
        get => Require(_reduce);
        internal set => SetHandle(ref _reduce, value);
    }

    private MethodBuilder? _toArray;
    public MethodBuilder ToArray
    {
        get => Require(_toArray);
        internal set => SetHandle(ref _toArray, value);
    }

    private MethodBuilder? _forEach;
    public MethodBuilder ForEach
    {
        get => Require(_forEach);
        internal set => SetHandle(ref _forEach, value);
    }

    private MethodBuilder? _some;
    public MethodBuilder Some
    {
        get => Require(_some);
        internal set => SetHandle(ref _some, value);
    }

    private MethodBuilder? _every;
    public MethodBuilder Every
    {
        get => Require(_every);
        internal set => SetHandle(ref _every, value);
    }

    private MethodBuilder? _find;
    public MethodBuilder Find
    {
        get => Require(_find);
        internal set => SetHandle(ref _find, value);
    }

    private MethodBuilder? _next;
    public MethodBuilder Next
    {
        get => Require(_next);
        internal set => SetHandle(ref _next, value);
    }

    private MethodBuilder? _from;
    public MethodBuilder From
    {
        get => Require(_from);
        internal set => SetHandle(ref _from, value);
    }

    private ConstructorBuilder? _mapIteratorCtor;
    public ConstructorBuilder MapIteratorCtor
    {
        get => Require(_mapIteratorCtor);
        internal set => SetHandle(ref _mapIteratorCtor, value);
    }

    private ConstructorBuilder? _filterIteratorCtor;
    public ConstructorBuilder FilterIteratorCtor
    {
        get => Require(_filterIteratorCtor);
        internal set => SetHandle(ref _filterIteratorCtor, value);
    }

    private ConstructorBuilder? _takeIteratorCtor;
    public ConstructorBuilder TakeIteratorCtor
    {
        get => Require(_takeIteratorCtor);
        internal set => SetHandle(ref _takeIteratorCtor, value);
    }

    private ConstructorBuilder? _dropIteratorCtor;
    public ConstructorBuilder DropIteratorCtor
    {
        get => Require(_dropIteratorCtor);
        internal set => SetHandle(ref _dropIteratorCtor, value);
    }

    private ConstructorBuilder? _flatMapIteratorCtor;
    public ConstructorBuilder FlatMapIteratorCtor
    {
        get => Require(_flatMapIteratorCtor);
        internal set => SetHandle(ref _flatMapIteratorCtor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Iterator helper metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Iterator helper metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Iterator helper metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = NormalizeToEnumerator;
        _ = Map;
        _ = Filter;
        _ = Take;
        _ = Drop;
        _ = FlatMap;
        _ = Reduce;
        _ = ToArray;
        _ = ForEach;
        _ = Some;
        _ = Every;
        _ = Find;
        _ = Next;
        _ = From;
        _ = MapIteratorCtor;
        _ = FilterIteratorCtor;
        _ = TakeIteratorCtor;
        _ = DropIteratorCtor;
        _ = FlatMapIteratorCtor;
        IsComplete = true;
    }
}
