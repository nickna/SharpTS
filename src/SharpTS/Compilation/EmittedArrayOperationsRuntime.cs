using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required array operation metadata for one compilation. Declarations support forward
/// references; completion validates the handles and freezes the component.
/// </summary>
public sealed class EmittedArrayOperationsRuntime
{
    internal EmittedArrayOperationsRuntime() { }

    public bool IsComplete { get; private set; }

    // Array methods
    private MethodBuilder? _setElement;
    public MethodBuilder SetElement
    {
        get => Require(_setElement);
        internal set => SetHandle(ref _setElement, value);
    }

    private MethodBuilder? _setElementDouble;
    public MethodBuilder SetElementDouble
    {
        get => Require(_setElementDouble);
        internal set => SetHandle(ref _setElementDouble, value);
    }

    private MethodBuilder? _setElementBool;
    public MethodBuilder SetElementBool
    {
        get => Require(_setElementBool);
        internal set => SetHandle(ref _setElementBool, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _isArray;
    public MethodBuilder IsArray
    {
        get => Require(_isArray);
        internal set => SetHandle(ref _isArray, value);
    }

    private MethodBuilder? _from;
    public MethodBuilder From
    {
        get => Require(_from);
        internal set => SetHandle(ref _from, value);
    }

    /// <summary>Stage 4y: ArrayFrom wrapped for value-form (object[] adapter).</summary>
    private MethodBuilder? _fromAdapter;
    public MethodBuilder FromAdapter
    {
        get => Require(_fromAdapter);
        internal set => SetHandle(ref _fromAdapter, value);
    }

    private MethodBuilder? _of;
    public MethodBuilder Of
    {
        get => Require(_of);
        internal set => SetHandle(ref _of, value);
    }

    // Array(…) / new Array(…) dispatch (issue #61).
    private MethodBuilder? _constructor;
    public MethodBuilder Constructor
    {
        get => Require(_constructor);
        internal set => SetHandle(ref _constructor, value);
    }

    private MethodBuilder? _concatSources;
    public MethodBuilder ConcatSources
    {
        get => Require(_concatSources);
        internal set => SetHandle(ref _concatSources, value);
    }

    private MethodBuilder? _pop;
    public MethodBuilder Pop
    {
        get => Require(_pop);
        internal set => SetHandle(ref _pop, value);
    }

    private MethodBuilder? _popProto;
    public MethodBuilder PopProto
    {
        get => Require(_popProto);
        internal set => SetHandle(ref _popProto, value);
    }

    private MethodBuilder? _shift;
    public MethodBuilder Shift
    {
        get => Require(_shift);
        internal set => SetHandle(ref _shift, value);
    }

    private MethodBuilder? _shiftDouble;
    public MethodBuilder ShiftDouble
    {
        get => Require(_shiftDouble);
        internal set => SetHandle(ref _shiftDouble, value);
    }

    private MethodBuilder? _shiftBool;
    public MethodBuilder ShiftBool
    {
        get => Require(_shiftBool);
        internal set => SetHandle(ref _shiftBool, value);
    }

    private MethodBuilder? _shiftProto;
    public MethodBuilder ShiftProto
    {
        get => Require(_shiftProto);
        internal set => SetHandle(ref _shiftProto, value);
    }

    private MethodBuilder? _shiftNumber;
    public MethodBuilder ShiftNumber
    {
        get => Require(_shiftNumber);
        internal set => SetHandle(ref _shiftNumber, value);
    }

    private MethodBuilder? _unshift;
    public MethodBuilder Unshift
    {
        get => Require(_unshift);
        internal set => SetHandle(ref _unshift, value);
    }

    private MethodBuilder? _unshiftDouble;
    public MethodBuilder UnshiftDouble
    {
        get => Require(_unshiftDouble);
        internal set => SetHandle(ref _unshiftDouble, value);
    }

    private MethodBuilder? _unshiftBool;
    public MethodBuilder UnshiftBool
    {
        get => Require(_unshiftBool);
        internal set => SetHandle(ref _unshiftBool, value);
    }

    private MethodBuilder? _unshiftNumber;
    public MethodBuilder UnshiftNumber
    {
        get => Require(_unshiftNumber);
        internal set => SetHandle(ref _unshiftNumber, value);
    }

    private MethodBuilder? _slice;
    public MethodBuilder Slice
    {
        get => Require(_slice);
        internal set => SetHandle(ref _slice, value);
    }

    private MethodBuilder? _sliceNumber;
    public MethodBuilder SliceNumber
    {
        get => Require(_sliceNumber);
        internal set => SetHandle(ref _sliceNumber, value);
    }

    private MethodBuilder? _map;
    public MethodBuilder Map
    {
        get => Require(_map);
        internal set => SetHandle(ref _map, value);
    }

    private MethodBuilder? _mapDirect;
    public MethodBuilder MapDirect
    {
        get => Require(_mapDirect);
        internal set => SetHandle(ref _mapDirect, value);
    }

    private MethodBuilder? _mapDouble;
    public MethodBuilder MapDouble
    {
        get => Require(_mapDouble);
        internal set => SetHandle(ref _mapDouble, value);
    }

    private MethodBuilder? _filterDouble;
    public MethodBuilder FilterDouble
    {
        get => Require(_filterDouble);
        internal set => SetHandle(ref _filterDouble, value);
    }

    private MethodBuilder? _filter;
    public MethodBuilder Filter
    {
        get => Require(_filter);
        internal set => SetHandle(ref _filter, value);
    }

    private MethodBuilder? _filterDirect;
    public MethodBuilder FilterDirect
    {
        get => Require(_filterDirect);
        internal set => SetHandle(ref _filterDirect, value);
    }

    private MethodBuilder? _filterDirectBool;
    public MethodBuilder FilterDirectBool
    {
        get => Require(_filterDirectBool);
        internal set => SetHandle(ref _filterDirectBool, value);
    }

    private MethodBuilder? _forEach;
    public MethodBuilder ForEach
    {
        get => Require(_forEach);
        internal set => SetHandle(ref _forEach, value);
    }

    private MethodBuilder? _forEachDirect;
    public MethodBuilder ForEachDirect
    {
        get => Require(_forEachDirect);
        internal set => SetHandle(ref _forEachDirect, value);
    }

    private MethodBuilder? _push;
    public MethodBuilder Push
    {
        get => Require(_push);
        internal set => SetHandle(ref _push, value);
    }

    private MethodBuilder? _pushOneDiscarded;
    public MethodBuilder PushOneDiscarded
    {
        get => Require(_pushOneDiscarded);
        internal set => SetHandle(ref _pushOneDiscarded, value);
    }

    private MethodBuilder? _pushDouble;
    public MethodBuilder PushDouble
    {
        get => Require(_pushDouble);
        internal set => SetHandle(ref _pushDouble, value);
    }

    private MethodBuilder? _pushBool;
    public MethodBuilder PushBool
    {
        get => Require(_pushBool);
        internal set => SetHandle(ref _pushBool, value);
    }

    private MethodBuilder? _pushProto;
    public MethodBuilder PushProto
    {
        get => Require(_pushProto);
        internal set => SetHandle(ref _pushProto, value);
    }

    private MethodBuilder? _unshiftProto;
    public MethodBuilder UnshiftProto
    {
        get => Require(_unshiftProto);
        internal set => SetHandle(ref _unshiftProto, value);
    }

    private MethodBuilder? _find;
    public MethodBuilder Find
    {
        get => Require(_find);
        internal set => SetHandle(ref _find, value);
    }

    private MethodBuilder? _findDirect;
    public MethodBuilder FindDirect
    {
        get => Require(_findDirect);
        internal set => SetHandle(ref _findDirect, value);
    }

    private MethodBuilder? _findDirectBool;
    public MethodBuilder FindDirectBool
    {
        get => Require(_findDirectBool);
        internal set => SetHandle(ref _findDirectBool, value);
    }

    private MethodBuilder? _findIndex;
    public MethodBuilder FindIndex
    {
        get => Require(_findIndex);
        internal set => SetHandle(ref _findIndex, value);
    }

    private MethodBuilder? _findIndexDirect;
    public MethodBuilder FindIndexDirect
    {
        get => Require(_findIndexDirect);
        internal set => SetHandle(ref _findIndexDirect, value);
    }

    private MethodBuilder? _findIndexDirectBool;
    public MethodBuilder FindIndexDirectBool
    {
        get => Require(_findIndexDirectBool);
        internal set => SetHandle(ref _findIndexDirectBool, value);
    }

    private MethodBuilder? _some;
    public MethodBuilder Some
    {
        get => Require(_some);
        internal set => SetHandle(ref _some, value);
    }

    private MethodBuilder? _someDirect;
    public MethodBuilder SomeDirect
    {
        get => Require(_someDirect);
        internal set => SetHandle(ref _someDirect, value);
    }

    private MethodBuilder? _someDirectBool;
    public MethodBuilder SomeDirectBool
    {
        get => Require(_someDirectBool);
        internal set => SetHandle(ref _someDirectBool, value);
    }

    private MethodBuilder? _every;
    public MethodBuilder Every
    {
        get => Require(_every);
        internal set => SetHandle(ref _every, value);
    }

    private MethodBuilder? _everyDirect;
    public MethodBuilder EveryDirect
    {
        get => Require(_everyDirect);
        internal set => SetHandle(ref _everyDirect, value);
    }

    private MethodBuilder? _everyDirectBool;
    public MethodBuilder EveryDirectBool
    {
        get => Require(_everyDirectBool);
        internal set => SetHandle(ref _everyDirectBool, value);
    }

    private MethodBuilder? _reduce;
    public MethodBuilder Reduce
    {
        get => Require(_reduce);
        internal set => SetHandle(ref _reduce, value);
    }

    private MethodBuilder? _reduceDirect;
    public MethodBuilder ReduceDirect
    {
        get => Require(_reduceDirect);
        internal set => SetHandle(ref _reduceDirect, value);
    }

    private MethodBuilder? _reduceDouble;
    public MethodBuilder ReduceDouble
    {
        get => Require(_reduceDouble);
        internal set => SetHandle(ref _reduceDouble, value);
    }

    private MethodBuilder? _reduceRight;
    public MethodBuilder ReduceRight
    {
        get => Require(_reduceRight);
        internal set => SetHandle(ref _reduceRight, value);
    }

    private MethodBuilder? _includes;
    public MethodBuilder Includes
    {
        get => Require(_includes);
        internal set => SetHandle(ref _includes, value);
    }

    private MethodBuilder? _includesProto;
    public MethodBuilder IncludesProto
    {
        get => Require(_includesProto);
        internal set => SetHandle(ref _includesProto, value);
    }

    private MethodBuilder? _includesDouble;
    public MethodBuilder IncludesDouble
    {
        get => Require(_includesDouble);
        internal set => SetHandle(ref _includesDouble, value);
    }

    private MethodBuilder? _indexOf;
    public MethodBuilder IndexOf
    {
        get => Require(_indexOf);
        internal set => SetHandle(ref _indexOf, value);
    }

    private MethodBuilder? _lastIndexOf;
    public MethodBuilder LastIndexOf
    {
        get => Require(_lastIndexOf);
        internal set => SetHandle(ref _lastIndexOf, value);
    }

    private MethodBuilder? _join;
    public MethodBuilder Join
    {
        get => Require(_join);
        internal set => SetHandle(ref _join, value);
    }

    private MethodBuilder? _concat;
    public MethodBuilder Concat
    {
        get => Require(_concat);
        internal set => SetHandle(ref _concat, value);
    }

    private MethodBuilder? _reverse;
    public MethodBuilder Reverse
    {
        get => Require(_reverse);
        internal set => SetHandle(ref _reverse, value);
    }

    private MethodBuilder? _reverseProto;
    public MethodBuilder ReverseProto
    {
        get => Require(_reverseProto);
        internal set => SetHandle(ref _reverseProto, value);
    }

    private MethodBuilder? _flat;
    public MethodBuilder Flat
    {
        get => Require(_flat);
        internal set => SetHandle(ref _flat, value);
    }

    private MethodBuilder? _flatMap;
    public MethodBuilder FlatMap
    {
        get => Require(_flatMap);
        internal set => SetHandle(ref _flatMap, value);
    }

    private MethodBuilder? _flatHelper;
    public MethodBuilder FlatHelper
    {
        get => Require(_flatHelper);
        internal set => SetHandle(ref _flatHelper, value);
    }

    private MethodBuilder? _sort;
    public MethodBuilder Sort
    {
        get => Require(_sort);
        internal set => SetHandle(ref _sort, value);
    }

    private MethodBuilder? _sortDirect;
    public MethodBuilder SortDirect
    {
        get => Require(_sortDirect);
        internal set => SetHandle(ref _sortDirect, value);
    }

    private MethodBuilder? _sortDirectNumber;
    public MethodBuilder SortDirectNumber
    {
        get => Require(_sortDirectNumber);
        internal set => SetHandle(ref _sortDirectNumber, value);
    }

    private MethodBuilder? _sortNumeric;
    public MethodBuilder SortNumeric
    {
        get => Require(_sortNumeric);
        internal set => SetHandle(ref _sortNumeric, value);
    }

    private MethodBuilder? _sortProto;
    public MethodBuilder SortProto
    {
        get => Require(_sortProto);
        internal set => SetHandle(ref _sortProto, value);
    }

    private MethodBuilder? _sortCanUseDenseFastPath;
    public MethodBuilder SortCanUseDenseFastPath
    {
        get => Require(_sortCanUseDenseFastPath);
        internal set => SetHandle(ref _sortCanUseDenseFastPath, value);
    }

    private MethodBuilder? _toSorted;
    public MethodBuilder ToSorted
    {
        get => Require(_toSorted);
        internal set => SetHandle(ref _toSorted, value);
    }

    private MethodBuilder? _toSortedGeneric;
    public MethodBuilder ToSortedGeneric
    {
        get => Require(_toSortedGeneric);
        internal set => SetHandle(ref _toSortedGeneric, value);
    }

    private MethodBuilder? _materializeForCopy;
    public MethodBuilder MaterializeForCopy
    {
        get => Require(_materializeForCopy);
        internal set => SetHandle(ref _materializeForCopy, value);
    }

    private MethodBuilder? _splice;
    public MethodBuilder Splice
    {
        get => Require(_splice);
        internal set => SetHandle(ref _splice, value);
    }

    private MethodBuilder? _spliceProto;
    public MethodBuilder SpliceProto
    {
        get => Require(_spliceProto);
        internal set => SetHandle(ref _spliceProto, value);
    }

    private MethodBuilder? _toSpliced;
    public MethodBuilder ToSpliced
    {
        get => Require(_toSpliced);
        internal set => SetHandle(ref _toSpliced, value);
    }

    private MethodBuilder? _toSplicedProto;
    public MethodBuilder ToSplicedProto
    {
        get => Require(_toSplicedProto);
        internal set => SetHandle(ref _toSplicedProto, value);
    }

    private MethodBuilder? _findLast;
    public MethodBuilder FindLast
    {
        get => Require(_findLast);
        internal set => SetHandle(ref _findLast, value);
    }

    private MethodBuilder? _findLastIndex;
    public MethodBuilder FindLastIndex
    {
        get => Require(_findLastIndex);
        internal set => SetHandle(ref _findLastIndex, value);
    }

    private MethodBuilder? _toReversed;
    public MethodBuilder ToReversed
    {
        get => Require(_toReversed);
        internal set => SetHandle(ref _toReversed, value);
    }

    private MethodBuilder? _with;
    public MethodBuilder With
    {
        get => Require(_with);
        internal set => SetHandle(ref _with, value);
    }

    private MethodBuilder? _at;
    public MethodBuilder At
    {
        get => Require(_at);
        internal set => SetHandle(ref _at, value);
    }

    private MethodBuilder? _fill;
    public MethodBuilder Fill
    {
        get => Require(_fill);
        internal set => SetHandle(ref _fill, value);
    }

    private MethodBuilder? _fillProto;
    public MethodBuilder FillProto
    {
        get => Require(_fillProto);
        internal set => SetHandle(ref _fillProto, value);
    }

    private MethodBuilder? _copyWithin;
    public MethodBuilder CopyWithin
    {
        get => Require(_copyWithin);
        internal set => SetHandle(ref _copyWithin, value);
    }

    private MethodBuilder? _copyWithinProto;
    public MethodBuilder CopyWithinProto
    {
        get => Require(_copyWithinProto);
        internal set => SetHandle(ref _copyWithinProto, value);
    }

    private MethodBuilder? _entries;
    public MethodBuilder Entries
    {
        get => Require(_entries);
        internal set => SetHandle(ref _entries, value);
    }

    private MethodBuilder? _keys;
    public MethodBuilder Keys
    {
        get => Require(_keys);
        internal set => SetHandle(ref _keys, value);
    }

    private MethodBuilder? _values;
    public MethodBuilder Values
    {
        get => Require(_values);
        internal set => SetHandle(ref _values, value);
    }

    private ConstructorBuilder? _iteratorCtor;
    public ConstructorBuilder IteratorCtor
    {
        get => Require(_iteratorCtor);
        internal set => SetHandle(ref _iteratorCtor, value);
    }

    private MethodBuilder? _materialize;
    public MethodBuilder Materialize
    {
        get => Require(_materialize);
        internal set => SetHandle(ref _materialize, value);
    }

    private MethodBuilder? _getMethod;
    public MethodBuilder GetMethod
    {
        get => Require(_getMethod);
        internal set => SetHandle(ref _getMethod, value);
    }

    // Thread-static "original array-like receiver" slot. The Array.prototype.X.call(receiver, ...)
    // pattern matcher sets it before invoking the runtime helper; EmitCallbackArgsAndInvoke
    // reads it when populating the callback's 4th argument (so the callback sees the ORIGINAL
    // receiver per ECMA-262, not the materialized temp list). Null when no prototype.call
    // context is active — direct `arr.forEach(cb)` calls keep passing the List as the 4th arg.
    private FieldBuilder? _currentReceiverField;
    public FieldBuilder CurrentReceiverField
    {
        get => Require(_currentReceiverField);
        internal set => SetHandle(ref _currentReceiverField, value);
    }

    private MethodBuilder? _destructureSource;
    public MethodBuilder DestructureSource
    {
        get => Require(_destructureSource);
        internal set => SetHandle(ref _destructureSource, value);
    }

    // Lazy-aware materializer used by Array.prototype.* iterator helpers.
    // For receivers whose elements may have descriptor side effects
    // (TSObject / Dictionary), returns a placeholder List&lt;object&gt; sized
    // to length so subsequent LoadArrayLikeElement calls re-read each slot
    // via $Runtime.GetProperty. Eager-receiver branches (List, $Array,
    // string, $Arguments, ObjectArray) delegate to ArrayLikeMaterialize.
    private MethodBuilder? _materializeForIteration;
    public MethodBuilder MaterializeForIteration
    {
        get => Require(_materializeForIteration);
        internal set => SetHandle(ref _materializeForIteration, value);
    }

    // Element reader for iterator helpers. Reads _currentArrayLikeReceiver:
    // if it's a Dict or $Object (lazy-eligible), returns
    // $Runtime.GetProperty(receiver, idx.ToString()); otherwise returns
    // list[idx]. The type check disambiguates lazy iteration from the
    // existing "callback's array-slot" use of _currentArrayLikeReceiver.
    private MethodBuilder? _loadArrayLikeElement;
    public MethodBuilder LoadArrayLikeElement
    {
        get => Require(_loadArrayLikeElement);
        internal set => SetHandle(ref _loadArrayLikeElement, value);
    }

    // ECMA-262 7.3.10 HasProperty for Dict + $Object receivers, used by the
    // iterator-helper element loader to distinguish "absent" from "present
    // but undefined". Walks own dict._fields, own PDS, then the prototype
    // chain (PDSGetPrototype). Does not invoke any get accessors —
    // existence-only check, so set-only accessors and getters that throw
    // don't fire spuriously. Returns a CLR bool.
    private MethodBuilder? _hasArrayLikeProperty;
    public MethodBuilder HasArrayLikeProperty
    {
        get => Require(_hasArrayLikeProperty);
        internal set => SetHandle(ref _hasArrayLikeProperty, value);
    }

    // Thread-static "callback thisArg" for `arr.forEach(cb, thisArg)` and
    // similar Array prototype methods. ArrayEmitter / $BoundArrayMethod sets
    // it when the user passes a thisArg; EmitCallbackArgsAndInvoke reads it
    // as the receiver passed to InvokeMethodValue, then clears it. Null
    // means no thisArg (callback's `this` becomes undefined per spec).
    private FieldBuilder? _callbackThisArgField;
    public FieldBuilder CallbackThisArgField
    {
        get => Require(_callbackThisArgField);
        internal set => SetHandle(ref _callbackThisArgField, value);
    }

    /// <summary>
    /// Array.prototype singleton dictionary populated at cctor time with
    /// <c>$TSFunction</c> wrappers around <c>$Runtime.Array*</c> helpers.
    /// Read by ArrayStaticEmitter when user code does <c>Array.prototype</c>
    /// or <c>Array.prototype.X</c> as a value access (the pattern matcher
    /// in ILEmitter.Calls.cs still handles <c>Array.prototype.X.call(...)</c>
    /// syntactically, so this dict is mostly used for typeof/identity/value
    /// probes — including the Test262 <c>isConstructor</c> harness which
    /// queries <c>Array.prototype.sort</c>.
    /// </summary>
    private FieldBuilder? _prototypeField;
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => SetHandle(ref _prototypeField, value);
    }

    /// <summary>
    /// <c>$Runtime._ArrayPrototypePopulate()</c> — called from cctor's tail
    /// to fill <see cref="PrototypeField"/> with <c>$TSFunction</c>
    /// wrappers around the <c>Array*</c> helpers. Must be wired up after all
    /// the Array helper MethodBuilders are defined.
    /// </summary>
    private MethodBuilder? _prototypePopulateMethod;
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => SetHandle(ref _prototypePopulateMethod, value);
    }

    /// <summary>$Runtime.ArrayProtoToString(this) — ECMA-262 23.1.3.32. Returns join of elements with default separator (",").</summary>
    private MethodBuilder? _protoToStringHelper;
    public MethodBuilder ProtoToStringHelper
    {
        get => Require(_protoToStringHelper);
        internal set => SetHandle(ref _protoToStringHelper, value);
    }

    /// <summary>$Runtime.ArrayProtoToLocaleString(this) — ECMA-262 23.1.3.33. Invokes each present element's live toLocaleString method and joins the results with commas.</summary>
    private MethodBuilder? _protoToLocaleStringHelper;
    public MethodBuilder ProtoToLocaleStringHelper
    {
        get => Require(_protoToLocaleStringHelper);
        internal set => SetHandle(ref _protoToLocaleStringHelper, value);
    }

    // Bound array method for dynamic array property access
    private TypeBuilder? _boundMethodType;
    public TypeBuilder BoundMethodType
    {
        get => Require(_boundMethodType);
        internal set => SetHandle(ref _boundMethodType, value);
    }

    private ConstructorBuilder? _boundMethodCtor;
    public ConstructorBuilder BoundMethodCtor
    {
        get => Require(_boundMethodCtor);
        internal set => SetHandle(ref _boundMethodCtor, value);
    }

    private MethodBuilder? _boundMethodInvoke;
    public MethodBuilder BoundMethodInvoke
    {
        get => Require(_boundMethodInvoke);
        internal set => SetHandle(ref _boundMethodInvoke, value);
    }

    private FieldBuilder? _boundMethodListField;
    public FieldBuilder BoundMethodListField
    {
        get => Require(_boundMethodListField);
        internal set => SetHandle(ref _boundMethodListField, value);
    }

    private FieldBuilder? _boundMethodNameField;
    public FieldBuilder BoundMethodNameField
    {
        get => Require(_boundMethodNameField);
        internal set => SetHandle(ref _boundMethodNameField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Array operation metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Array operation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = SetElement;
        _ = SetElementDouble;
        _ = SetElementBool;
        _ = Create;
        _ = IsArray;
        _ = From;
        _ = FromAdapter;
        _ = Of;
        _ = Constructor;
        _ = ConcatSources;
        _ = Pop;
        _ = PopProto;
        _ = Shift;
        _ = ShiftDouble;
        _ = ShiftBool;
        _ = ShiftProto;
        _ = ShiftNumber;
        _ = Unshift;
        _ = UnshiftDouble;
        _ = UnshiftBool;
        _ = UnshiftNumber;
        _ = Slice;
        _ = SliceNumber;
        _ = Map;
        _ = MapDirect;
        _ = MapDouble;
        _ = FilterDouble;
        _ = Filter;
        _ = FilterDirect;
        _ = FilterDirectBool;
        _ = ForEach;
        _ = ForEachDirect;
        _ = Push;
        _ = PushOneDiscarded;
        _ = PushDouble;
        _ = PushBool;
        _ = PushProto;
        _ = UnshiftProto;
        _ = Find;
        _ = FindDirect;
        _ = FindDirectBool;
        _ = FindIndex;
        _ = FindIndexDirect;
        _ = FindIndexDirectBool;
        _ = Some;
        _ = SomeDirect;
        _ = SomeDirectBool;
        _ = Every;
        _ = EveryDirect;
        _ = EveryDirectBool;
        _ = Reduce;
        _ = ReduceDirect;
        _ = ReduceDouble;
        _ = ReduceRight;
        _ = Includes;
        _ = IncludesProto;
        _ = IncludesDouble;
        _ = IndexOf;
        _ = LastIndexOf;
        _ = Join;
        _ = Concat;
        _ = Reverse;
        _ = ReverseProto;
        _ = Flat;
        _ = FlatMap;
        _ = FlatHelper;
        _ = Sort;
        _ = SortDirect;
        _ = SortDirectNumber;
        _ = SortNumeric;
        _ = SortProto;
        _ = SortCanUseDenseFastPath;
        _ = ToSorted;
        _ = ToSortedGeneric;
        _ = MaterializeForCopy;
        _ = Splice;
        _ = SpliceProto;
        _ = ToSpliced;
        _ = ToSplicedProto;
        _ = FindLast;
        _ = FindLastIndex;
        _ = ToReversed;
        _ = With;
        _ = At;
        _ = Fill;
        _ = FillProto;
        _ = CopyWithin;
        _ = CopyWithinProto;
        _ = Entries;
        _ = Keys;
        _ = Values;
        _ = IteratorCtor;
        _ = Materialize;
        _ = GetMethod;
        _ = CurrentReceiverField;
        _ = DestructureSource;
        _ = MaterializeForIteration;
        _ = LoadArrayLikeElement;
        _ = HasArrayLikeProperty;
        _ = CallbackThisArgField;
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        _ = ProtoToStringHelper;
        _ = ProtoToLocaleStringHelper;
        _ = BoundMethodType;
        _ = BoundMethodCtor;
        _ = BoundMethodInvoke;
        _ = BoundMethodListField;
        _ = BoundMethodNameField;
        IsComplete = true;
    }
}
