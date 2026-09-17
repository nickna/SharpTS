using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required symbol declarations for one compilation.</summary>
public sealed class EmittedSymbolRuntime
{
    internal EmittedSymbolRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _constructor;
    public ConstructorBuilder Constructor
    {
        get => Require(_constructor);
        internal set => SetHandle(ref _constructor, value);
    }

    private MethodBuilder? _toStringMethod;
    public MethodBuilder ToStringMethod
    {
        get => Require(_toStringMethod);
        internal set => SetHandle(ref _toStringMethod, value);
    }

    private MethodBuilder? _for;
    public MethodBuilder For
    {
        get => Require(_for);
        internal set => SetHandle(ref _for, value);
    }

    private MethodBuilder? _keyFor;
    public MethodBuilder KeyFor
    {
        get => Require(_keyFor);
        internal set => SetHandle(ref _keyFor, value);
    }

    private MethodBuilder? _descriptionGetter;
    public MethodBuilder DescriptionGetter
    {
        get => Require(_descriptionGetter);
        internal set => SetHandle(ref _descriptionGetter, value);
    }

    private FieldBuilder? _prototype;
    public FieldBuilder Prototype
    {
        get => Require(_prototype);
        internal set => SetHandle(ref _prototype, value);
    }

    private MethodBuilder? _populatePrototype;
    public MethodBuilder PopulatePrototype
    {
        get => Require(_populatePrototype);
        internal set => SetHandle(ref _populatePrototype, value);
    }

    private MethodBuilder? _prototypeDescription;
    public MethodBuilder PrototypeDescription
    {
        get => Require(_prototypeDescription);
        internal set => SetHandle(ref _prototypeDescription, value);
    }

    private MethodBuilder? _getStorage;
    public MethodBuilder GetStorage
    {
        get => Require(_getStorage);
        internal set => SetHandle(ref _getStorage, value);
    }

    private MethodBuilder? _tryGetStorage;
    public MethodBuilder TryGetStorage
    {
        get => Require(_tryGetStorage);
        internal set => SetHandle(ref _tryGetStorage, value);
    }

    private MethodBuilder? _isSymbol;
    public MethodBuilder IsSymbol
    {
        get => Require(_isSymbol);
        internal set => SetHandle(ref _isSymbol, value);
    }

    private FieldBuilder? _iterator;
    public FieldBuilder Iterator
    {
        get => Require(_iterator);
        internal set => SetHandle(ref _iterator, value);
    }

    private FieldBuilder? _asyncIterator;
    public FieldBuilder AsyncIterator
    {
        get => Require(_asyncIterator);
        internal set => SetHandle(ref _asyncIterator, value);
    }

    private FieldBuilder? _toStringTag;
    public FieldBuilder ToStringTag
    {
        get => Require(_toStringTag);
        internal set => SetHandle(ref _toStringTag, value);
    }

    private FieldBuilder? _hasInstance;
    public FieldBuilder HasInstance
    {
        get => Require(_hasInstance);
        internal set => SetHandle(ref _hasInstance, value);
    }

    private FieldBuilder? _isConcatSpreadable;
    public FieldBuilder IsConcatSpreadable
    {
        get => Require(_isConcatSpreadable);
        internal set => SetHandle(ref _isConcatSpreadable, value);
    }

    private FieldBuilder? _toPrimitive;
    public FieldBuilder ToPrimitive
    {
        get => Require(_toPrimitive);
        internal set => SetHandle(ref _toPrimitive, value);
    }

    private FieldBuilder? _species;
    public FieldBuilder Species
    {
        get => Require(_species);
        internal set => SetHandle(ref _species, value);
    }

    private FieldBuilder? _unscopables;
    public FieldBuilder Unscopables
    {
        get => Require(_unscopables);
        internal set => SetHandle(ref _unscopables, value);
    }

    private FieldBuilder? _dispose;
    public FieldBuilder Dispose
    {
        get => Require(_dispose);
        internal set => SetHandle(ref _dispose, value);
    }

    private FieldBuilder? _asyncDispose;
    public FieldBuilder AsyncDispose
    {
        get => Require(_asyncDispose);
        internal set => SetHandle(ref _asyncDispose, value);
    }

    private FieldBuilder? _match;
    public FieldBuilder Match
    {
        get => Require(_match);
        internal set => SetHandle(ref _match, value);
    }

    private FieldBuilder? _matchAll;
    public FieldBuilder MatchAll
    {
        get => Require(_matchAll);
        internal set => SetHandle(ref _matchAll, value);
    }

    private FieldBuilder? _replace;
    public FieldBuilder Replace
    {
        get => Require(_replace);
        internal set => SetHandle(ref _replace, value);
    }

    private FieldBuilder? _search;
    public FieldBuilder Search
    {
        get => Require(_search);
        internal set => SetHandle(ref _search, value);
    }

    private FieldBuilder? _split;
    public FieldBuilder Split
    {
        get => Require(_split);
        internal set => SetHandle(ref _split, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Symbol metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Symbol metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Constructor;
        _ = ToStringMethod;
        _ = For;
        _ = KeyFor;
        _ = DescriptionGetter;
        _ = Prototype;
        _ = PopulatePrototype;
        _ = PrototypeDescription;
        _ = GetStorage;
        _ = TryGetStorage;
        _ = IsSymbol;
        _ = Iterator;
        _ = AsyncIterator;
        _ = ToStringTag;
        _ = HasInstance;
        _ = IsConcatSpreadable;
        _ = ToPrimitive;
        _ = Species;
        _ = Unscopables;
        _ = Dispose;
        _ = AsyncDispose;
        _ = Match;
        _ = MatchAll;
        _ = Replace;
        _ = Search;
        _ = Split;
        IsComplete = true;
    }
}
