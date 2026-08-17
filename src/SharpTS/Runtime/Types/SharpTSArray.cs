using System.Collections;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript arrays — dual-mode dense/sparse storage per ECMA-262.
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage model (issue #73 Stage B, 2026-04-22).</b>
/// The array carries an explicit <see cref="Length"/> that is independent of physical
/// slot count. Two backing stores cooperate:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>_dense</c> — a <see cref="Deque{T}"/> holding the contiguous prefix
///     <c>[0, _dense.Count)</c>. Used by typical array-as-list workloads (push,
///     pop, map, forEach on small-to-medium arrays). O(1) unshift thanks to the
///     circular buffer.
///   </description></item>
///   <item><description>
///     <c>_sparse</c> — a <see cref="Dictionary{TKey, TValue}"/> keyed on
///     <see cref="uint"/> index. Activated when an assignment would require
///     padding more than <see cref="SparseThreshold"/> slots past the current
///     length (e.g. <c>a[2**31] = 1</c>). When active, indices beyond
///     <c>_dense.Count</c> live here; absent dictionary entries are holes.
///   </description></item>
/// </list>
/// <para>
/// When <c>_sparse == null</c> the array is purely dense and
/// <c>_dense.Count == Length</c>. When <c>_sparse != null</c> the dense prefix
/// may still hold the low indices (cheap to preserve; no up-front migration
/// cost); any index <c>&gt;= _dense.Count</c> is looked up in the dictionary.
/// </para>
/// <para>
/// <b>Holes vs. explicit undefined.</b> This stage conflates them: reads from
/// unwritten positions return <see cref="SharpTSUndefined"/>.<c>Instance</c>,
/// and dense extension pads with <c>Undefined</c>. ECMA-262 actually requires
/// <c>forEach</c> to skip holes and <c>hasOwnProperty(i)</c> to return false
/// for them — a correctness gap tracked for Stage C. Stage B delivers the
/// scaling property (no OOM on sparse writes) without changing existing
/// hole-vs-undefined behavior.
/// </para>
/// <para>
/// <b>Structural mutations on sparse arrays.</b> <see cref="Insert"/>,
/// <see cref="RemoveAt"/>, <see cref="AddFirst"/>, <see cref="ReverseInPlace"/>,
/// and similar shift-everything operations call <see cref="MaterializeDense"/>
/// first. This is O(Length) in the worst case and defeats the point of sparse
/// storage for that particular operation; realistic code rarely mixes huge
/// indices with splice-style edits. Stage C may add specialized paths.
/// </para>
/// </remarks>
/// <seealso cref="SharpTSObject"/>
public class SharpTSArray : ITypeCategorized, IReadOnlyList<object?>
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Array;

    /// <summary>
    /// Hole size (in slots) beyond which <see cref="Set"/> transitions from
    /// dense padding to sparse dictionary storage. A conservative value (1024):
    /// small enough that malicious inputs like <c>a[2**31] = 1</c> cannot allocate
    /// billions of undefined slots; large enough that typical growth patterns
    /// (e.g. repeatedly writing a[length]) stay on the dense fast path.
    /// </summary>
    private const int SparseThreshold = 1024;

    private readonly Deque<object?> _dense;
    private Dictionary<uint, object?>? _sparse;
    private object? _explicitPrototype;

    /// <summary>
    /// Whether Object.setPrototypeOf has replaced this array's intrinsic
    /// Array.prototype link. Kept separate from the value so an explicit null
    /// prototype remains distinguishable from the default realm prototype.
    /// </summary>
    internal bool HasExplicitPrototype { get; private set; }

    /// <summary>The explicitly assigned [[Prototype]], including null.</summary>
    internal object? ExplicitPrototype => _explicitPrototype;

    /// <summary>Replaces this array exotic object's [[Prototype]] link.</summary>
    internal void SetExplicitPrototype(object? prototype)
    {
        _explicitPrototype = prototype;
        HasExplicitPrototype = true;
    }

    /// <summary>
    /// Full JS array length — up to <see cref="MaxLength"/> = 2^32 - 1 per ECMA-262.
    /// Stored as <c>long</c> so arithmetic doesn't overflow; all arithmetic uses
    /// the long path. Public <see cref="Length"/> clamps to <see cref="int.MaxValue"/>
    /// for C# callers that assume int; <see cref="LongLength"/> exposes the true
    /// value (used by the JS <c>length</c> property accessor).
    /// </summary>
    private long _length;
    private bool _lengthWritable = true;

    /// <summary>
    /// Read-only view over the dense prefix. Present for compatibility with
    /// older callers and tests — normal code should iterate the array directly
    /// or use <see cref="Length"/> / the indexer. Does NOT represent the full
    /// array in sparse mode: only the contiguous prefix that hasn't been
    /// sparse-promoted lives here.
    /// </summary>
    internal Deque<object?> Elements => _dense;

    /// <summary>Creates an empty array.</summary>
    public SharpTSArray() : this(new Deque<object?>()) { }

    /// <summary>Creates an array from a deque (the deque becomes the dense backing).</summary>
    public SharpTSArray(Deque<object?> elements)
    {
        _dense = elements;
        _length = elements.Count;
    }

    /// <summary>Creates an array from any enumerable (copies into a new deque).</summary>
    public SharpTSArray(IEnumerable<object?> elements) : this(new Deque<object?>(elements)) { }

    /// <summary>
    /// ECMA-262 array length clamped to <see cref="int.MaxValue"/>.
    /// Most C# callers iterate <c>for (int i = 0; i &lt; arr.Length; i++)</c> and
    /// assume int; keeping the signature int preserves source compatibility.
    /// For arrays whose true length exceeds <see cref="int.MaxValue"/>, use
    /// <see cref="LongLength"/> instead.
    /// </summary>
    public int Length => _length > int.MaxValue ? int.MaxValue : (int)_length;

    /// <summary>
    /// True ECMA-262 array length — up to 2^32 - 1. The JS <c>length</c> property
    /// accessor reads through this so <c>arr.length === 4294967295</c> works for
    /// arrays that use the full uint32 range.
    /// </summary>
    public long LongLength => _length;

    /// <summary>
    /// Collection count. Clamped to <see cref="int.MaxValue"/> for
    /// <see cref="IReadOnlyCollection{T}"/> compatibility. Matches <see cref="Length"/>.
    /// </summary>
    public int Count => Length;

    /// <summary>
    /// Indexed access to an existing slot. Throws <see cref="ArgumentOutOfRangeException"/>
    /// for out-of-range reads and writes — use <see cref="Get(long)"/> / <see cref="Set(long, object?)"/>
    /// for the JS-semantic variants (undefined on OOB read, extend on OOB write).
    /// Returns <see cref="SharpTSUndefined"/>.<c>Instance</c> for holes (user-facing);
    /// use <see cref="GetRaw(long)"/> to see <see cref="ArrayHole"/>.<c>Instance</c>.
    /// <c>int</c> and <c>long</c> overloads are provided — most call sites pass
    /// <c>int</c> and widen implicitly; the <c>long</c> path is what the interpreter's
    /// index resolver uses when a JS literal like <c>a[2147483648]</c> exceeds int range.
    /// </summary>
    public object? this[long index]
    {
        get
        {
            if ((ulong)index >= (ulong)_length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return UnholeForRead(GetCore(index));
        }
        set
        {
            if ((ulong)index >= (ulong)_length)
                throw new ArgumentOutOfRangeException(nameof(index));
            SetCore(index, value);
        }
    }

    /// <summary>int-indexed indexer — widens to the long path.</summary>
    public object? this[int index]
    {
        get => this[(long)index];
        set => this[(long)index] = value;
    }

    /// <summary>
    /// Reads the slot at <paramref name="index"/> WITHOUT converting holes to undefined.
    /// Returns <see cref="ArrayHole"/>.<c>Instance</c> for holes (index in range but
    /// not written), or <see cref="SharpTSUndefined"/>.<c>Instance</c> for out-of-range
    /// indices. Built-in array methods that distinguish holes from explicit undefined
    /// (forEach skips, map preserves, indexOf skips, includes does not) must use this.
    /// </summary>
    public object? GetRaw(long index)
    {
        if ((ulong)index >= (ulong)_length) return SharpTSUndefined.Instance;
        return GetCore(index);
    }

    /// <summary>int-indexed GetRaw — widens to long.</summary>
    public object? GetRaw(int index) => GetRaw((long)index);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="index"/> is a present (non-hole) slot.
    /// Equivalent to ECMA-262 <c>HasProperty</c> for numeric indices: <c>(String(i)) in arr</c>.
    /// </summary>
    public bool HasIndex(long index)
    {
        if ((ulong)index >= (ulong)_length) return false;
        if (index <= uint.MaxValue
            && (_indexAccessors?.ContainsKey((uint)index) ?? false))
            return true;
        int denseCount = _dense.Count;
        if (_sparse == null) return index < denseCount && _dense[(int)index] is not ArrayHole;
        if (index < denseCount) return _dense[(int)index] is not ArrayHole;
        if (index > uint.MaxValue) return false;
        return _sparse.ContainsKey((uint)index);
    }

    /// <summary>int-indexed HasIndex — widens to long.</summary>
    public bool HasIndex(int index) => HasIndex((long)index);

    /// <summary>
    /// Makes <paramref name="index"/> a hole (ECMA-262 <c>delete arr[i]</c>).
    /// Length is unchanged. No-op for out-of-range indices or frozen arrays.
    /// </summary>
    public void DeleteAt(long index)
    {
        if (IsFrozen) return;
        if ((ulong)index >= (ulong)_length) return;
        if (index <= uint.MaxValue)
            _indexAccessors?.Remove((uint)index);
        if (_sparse == null || index < _dense.Count)
            _dense[(int)index] = ArrayHole.Instance;
        else if (index <= uint.MaxValue)
            _sparse.Remove((uint)index);
    }

    /// <summary>int-indexed DeleteAt — widens to long.</summary>
    public void DeleteAt(int index) => DeleteAt((long)index);

    /// <summary>Reads the slot at the given index without mutating length or storage mode.
    /// Returns <see cref="ArrayHole"/>.<c>Instance</c> for holes (positions not written).
    /// Callers that want JS-spec user-facing behavior (holes read as undefined) should
    /// use <see cref="Get(long)"/> or convert via <see cref="UnholeForRead(object?)"/>.</summary>
    private object? GetCore(long index)
    {
        if (_sparse == null || index < _dense.Count)
            return _dense[(int)index];
        if (index > uint.MaxValue) return ArrayHole.Instance;
        return _sparse.TryGetValue((uint)index, out var v) ? v : ArrayHole.Instance;
    }

    /// <summary>
    /// Converts an <see cref="ArrayHole"/> to <see cref="SharpTSUndefined"/>.<c>Instance</c>
    /// for user-facing reads. Holes are observable as undefined at the language level
    /// (<c>arr[i] === undefined</c> for a hole, spread fills holes with undefined, etc.),
    /// so boundary helpers must strip the internal sentinel before returning.
    /// </summary>
    private static object? UnholeForRead(object? value)
        => value is ArrayHole ? SharpTSUndefined.Instance : value;

    /// <summary>Writes the slot at the given index without mutating length or transitioning.</summary>
    private void SetCore(long index, object? value)
    {
        if (_sparse == null || index < _dense.Count)
            _dense[(int)index] = value;
        else
            _sparse[(uint)index] = value;  // index <= uint.MaxValue guaranteed by MaxLength cap
    }

    /// <inheritdoc />
    /// <remarks>
    /// User-facing iteration: holes are yielded as <see cref="SharpTSUndefined"/>.<c>Instance</c>
    /// so <c>for-of</c>, <c>[...arr]</c>, and LINQ see undefined (matches ECMA-262 iterator
    /// protocol behavior of <c>values()</c>). Built-ins that need to skip holes — forEach,
    /// filter, reduce, etc. — must NOT use this enumerator; they iterate indices and check
    /// <see cref="HasIndex(int)"/> themselves.
    /// </remarks>
    public IEnumerator<object?> GetEnumerator()
    {
        // %ArrayIteratorPrototype%.next reads the array's current length on
        // every step. Mutations during iteration are therefore observable:
        // shrinking stops the iterator early, while appended elements can be
        // visited. Do not snapshot _dense.Count/_length here.
        for (long i = 0; i < _length; i++)
        {
            if (i < _dense.Count)
                yield return UnholeForRead(_dense[(int)i]);
            else if (_sparse != null && i <= uint.MaxValue
                && _sparse.TryGetValue((uint)i, out var value))
                yield return UnholeForRead(value);
            else
                yield return SharpTSUndefined.Instance;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // -----------------------------------------------------------------------
    // Mutation helpers — the encapsulation API added in Stage A. All respect
    // the dual-mode storage and where applicable fall back to MaterializeDense
    // for shift-style operations.
    // -----------------------------------------------------------------------

    /// <summary>Appends an element. Does not check frozen/sealed state.</summary>
    public void Add(object? value)
    {
        if (_sparse != null)
        {
            // Appending on a sparse array: write directly to the dict at _length.
            _sparse[(uint)_length] = value;
            _length++;
            return;
        }
        _dense.Add(value);
        _length = _dense.Count;
    }

    /// <summary>Appends many elements. Does not check frozen/sealed state.</summary>
    public void AddRange(IEnumerable<object?> values)
    {
        if (_sparse != null)
        {
            foreach (var v in values)
            {
                _sparse[(uint)_length] = v;
                _length++;
            }
            return;
        }
        _dense.AddRange(values);
        _length = _dense.Count;
    }

    /// <summary>Prepends an element (O(1) in dense mode via Deque).</summary>
    public void AddFirst(object? value)
    {
        MaterializeDense();
        _dense.AddFirst(value);
        _length = _dense.Count;
    }

    /// <summary>Inserts at an index, shifting later elements right.</summary>
    public void Insert(int index, object? value)
    {
        MaterializeDense();
        _dense.Insert(index, value);
        _length = _dense.Count;
    }

    /// <summary>Inserts many elements at an index.</summary>
    public void InsertRange(int index, IEnumerable<object?> values)
    {
        MaterializeDense();
        _dense.InsertRange(index, values);
        _length = _dense.Count;
    }

    /// <summary>Removes and returns the last element. A hole reads as undefined.</summary>
    public object? RemoveLast()
    {
        if (_length == 0)
            throw new InvalidOperationException("Array is empty.");
        long last = _length - 1;
        object? result;
        if (_sparse != null && last >= _dense.Count)
        {
            uint key = (uint)last;  // safe: last <= MaxWriteIndex < uint.MaxValue
            if (!_sparse.TryGetValue(key, out result))
                result = SharpTSUndefined.Instance;
            else
                _sparse.Remove(key);
        }
        else
        {
            result = _dense[(int)last];
            _dense.RemoveAt((int)last);
        }
        _length--;
        TryCollapseSparse();
        return UnholeForRead(result);
    }

    /// <summary>Removes and returns the first element (O(1) in dense mode). A hole reads as undefined.</summary>
    public object? RemoveFirst()
    {
        MaterializeDense();
        var result = _dense.RemoveFirst();
        _length = _dense.Count;
        return UnholeForRead(result);
    }

    /// <summary>Removes the element at the given index.</summary>
    public void RemoveAt(int index)
    {
        MaterializeDense();
        _dense.RemoveAt(index);
        _length = _dense.Count;
    }

    /// <summary>Removes a contiguous range of elements.</summary>
    public void RemoveRange(int index, int count)
    {
        MaterializeDense();
        _dense.RemoveRange(index, count);
        _length = _dense.Count;
    }

    /// <summary>Clears all elements.</summary>
    public void Clear()
    {
        _dense.Clear();
        _sparse = null;
        _length = 0;
    }

    /// <summary>Reverses in place.</summary>
    public void ReverseInPlace()
    {
        MaterializeDense();
        _dense.Reverse();
    }

    /// <summary>Returns a new <see cref="List{T}"/> containing the given slice.</summary>
    public List<object?> GetRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > _length)
            throw new ArgumentOutOfRangeException();
        if (_sparse == null && index + count <= _dense.Count)
            return _dense.GetRange(index, count);
        // Mixed or purely-sparse slice — build by iterating.
        var result = new List<object?>(count);
        for (int i = 0; i < count; i++)
            result.Add(GetCore(index + i));
        return result;
    }

    /// <summary>Returns the last element without removing it (holes read as undefined).</summary>
    public object? PeekLast() => _length == 0 ? throw new InvalidOperationException("Array is empty.") : UnholeForRead(GetCore(_length - 1));

    /// <summary>Returns the first element without removing it (holes read as undefined).</summary>
    public object? PeekFirst() => _length == 0 ? throw new InvalidOperationException("Array is empty.") : UnholeForRead(GetCore(0));

    /// <summary>Returns true if the element is present (reference/Equals match).</summary>
    public bool ContainsElement(object? item) => IndexOfElement(item) >= 0;

    /// <summary>Returns the first index of the element, or -1 if not found.</summary>
    public int IndexOfElement(object? item)
    {
        long limit = _length > int.MaxValue ? int.MaxValue : _length;
        for (long i = 0; i < limit; i++)
        {
            if (Equals(GetCore(i), item))
                return (int)i;
        }
        return -1;
    }

    /// <summary>
    /// Flattens the sparse tail into the dense backing so shift-style operations
    /// can run against a contiguous buffer. O(Length) in the worst case; used by
    /// Insert / RemoveAt / Reverse / AddFirst on sparse arrays.
    /// </summary>
    private void MaterializeDense()
    {
        if (_sparse == null)
            return;
        // Materialization copies every sparse entry into the dense prefix. If
        // _length exceeds int.MaxValue we can't represent that as a dense Deque.
        // Structural mutations (Insert / RemoveAt / AddFirst / Reverse) that
        // require a contiguous buffer are unsupported for such arrays; throw a
        // clear RangeError rather than silently allocating 2B+ object slots.
        if (_length > int.MaxValue)
            throw new Exception("RangeError: Array operation requires materializing a sparse array whose length exceeds int.MaxValue.");
        while (_dense.Count < _length)
        {
            int i = _dense.Count;
            if (_sparse.TryGetValue((uint)i, out var v))
                _dense.Add(v);
            else
                _dense.Add(ArrayHole.Instance);  // Preserve hole identity into dense.
        }
        _sparse = null;
    }

    /// <summary>
    /// If the sparse dictionary is no longer needed (empty OR fully covered by
    /// the dense prefix), release it. Called after operations that shrink length.
    /// </summary>
    private void TryCollapseSparse()
    {
        if (_sparse == null) return;
        if (_sparse.Count == 0 || _length <= _dense.Count)
            _sparse = null;
    }

    // -----------------------------------------------------------------------
    // Frozen / sealed / extensible state
    // -----------------------------------------------------------------------

    /// <summary>
    /// Whether this array is frozen (no element additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this array is sealed (no element additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Whether this array is extensible (can have new elements/properties added).
    /// </summary>
    public bool IsExtensible { get; private set; } = true;

    /// <summary>Whether the array exotic length property accepts assignment.</summary>
    internal bool IsLengthWritable => _lengthWritable;

    /// <summary>
    /// Freezes this array, preventing any element changes.
    /// </summary>
    public void Freeze()
    {
        SetNamedPropertyIntegrityLevel(frozen: true);
        _symbolProperties?.Freeze();
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
        IsExtensible = false; // Frozen implies non-extensible
    }

    /// <summary>
    /// Seals this array, preventing element additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        SetNamedPropertyIntegrityLevel(frozen: false);
        _symbolProperties?.Seal();
        IsSealed = true;
        IsExtensible = false;
    }

    /// <summary>
    /// Prevents adding new elements/properties to this array.
    /// </summary>
    public void PreventExtensions()
    {
        _symbolProperties?.PreventExtensions();
        IsExtensible = false;
    }

    // -----------------------------------------------------------------------
    // JS-semantic Get / Set (out-of-range: undefined read, extending write)
    // -----------------------------------------------------------------------

    /// <summary>
    /// JS-semantic read for user-facing code. Returns <see cref="SharpTSUndefined"/>.<c>Instance</c>
    /// for out-of-range indices AND for holes — matching the observable behavior
    /// of <c>arr[i]</c> at the language level. Built-ins that need to distinguish
    /// holes (forEach, indexOf, etc.) should use <see cref="GetRaw(long)"/> plus
    /// <see cref="HasIndex(long)"/>.
    /// </summary>
    public object? Get(long index)
    {
        if ((ulong)index >= (ulong)_length)
            return SharpTSUndefined.Instance;
        return UnholeForRead(GetCore(index));
    }

    /// <summary>int-indexed Get — widens to long.</summary>
    public object? Get(int index) => Get((long)index);

    public RuntimeValue GetRV(long index) => RuntimeValue.FromBoxed(Get(index));
    public RuntimeValue GetRV(int index) => GetRV((long)index);

    /// <summary>
    /// Upper bound on an array index: ECMA-262 allows writable indices up to
    /// <c>2^32 - 2</c> (the resulting length would be <c>2^32 - 1</c>, the
    /// spec's <c>Array.length</c> maximum). Writes past this throw RangeError.
    /// </summary>
    internal const long MaxWriteIndex = (long)uint.MaxValue - 1;

    /// <summary>
    /// Upper bound on <see cref="LongLength"/>: <c>2^32 - 1</c> per ECMA-262.
    /// </summary>
    internal const long MaxLength = (long)uint.MaxValue;

    /// <summary>
    /// JS-semantic write. Assignments beyond the current length extend the array;
    /// intermediate positions become holes (currently rendered as undefined on read).
    /// Transitions to sparse storage if the growth would exceed
    /// <see cref="SparseThreshold"/> slots.
    /// </summary>
    public void Set(long index, object? value)
        => SetStrict(index, value, strictMode: false);

    /// <summary>int-indexed Set — widens to long.</summary>
    public void Set(int index, object? value) => Set((long)index, value);

    /// <summary>
    /// JS-semantic write, strict-mode variant. Throws TypeError for writes to
    /// frozen or non-extensible arrays instead of silently no-op'ing.
    /// </summary>
    public void SetStrict(long index, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Cannot assign to read only property '{index}' of array"));
            return;
        }
        if (index < 0) throw new Exception("RangeError: Index out of bounds.");
        if (index > MaxWriteIndex)
            throw new Exception($"RangeError: Array index {index} exceeds ECMA-262 uint32 maximum.");

        string key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (GetOwnPropertyDescriptor(key) is { Writable: false })
        {
            if (strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Cannot assign to read only property '{key}' of array"));
            return;
        }

        if (index >= _length && !_lengthWritable)
        {
            if (strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    "Cannot extend an array with a non-writable length"));
            return;
        }

        if (index >= _length && !IsExtensible)
        {
            if (strictMode)
                throw new ThrowException(new SharpTSTypeError(
                    $"Cannot add property {index}, object is not extensible"));
            return;
        }

        SetCoreWithExtend(index, value);
    }

    /// <summary>int-indexed SetStrict — widens to long.</summary>
    public void SetStrict(int index, object? value, bool strictMode)
        => SetStrict((long)index, value, strictMode);

    /// <summary>
    /// Shared storage-aware write path. Handles the dense fast path, sparse
    /// transition on large holes, and writes within an already-sparse array.
    /// </summary>
    private void SetCoreWithExtend(long index, object? value)
    {
        if (_sparse != null)
        {
            SetCore(index, value);
            if (index >= _length) _length = index + 1;
            return;
        }

        // Pure-dense path.
        if (index < _length)
        {
            _dense[(int)index] = value;
            return;
        }

        long growth = index + 1 - _length;
        if (growth <= SparseThreshold && index + 1 <= int.MaxValue)
        {
            // Pad intermediate positions with ArrayHole, not Undefined —
            // per ECMA-262, a[5] = 1 on an empty array creates holes at 0..4
            // that forEach skips, hasOwnProperty rejects, etc.
            while (_dense.Count <= index)
                _dense.Add(ArrayHole.Instance);
            _dense[(int)index] = value;
            _length = _dense.Count;
            return;
        }

        // Transition to sparse: keep existing dense prefix, put the new write
        // (and any future high-index writes) into the dictionary.
        _sparse = new Dictionary<uint, object?> { [(uint)index] = value };
        _length = index + 1;
    }

    /// <summary>
    /// Implements <c>array.length = N</c>. Truncates the array when N is less
    /// than the current length (entries at index ≥ N are dropped) or extends
    /// with holes when N is greater. Respects frozen state.
    /// </summary>
    public void SetLength(long newLength)
    {
        if (IsFrozen || !_lengthWritable) return;
        if (newLength < 0) throw new ThrowException(new SharpTSRangeError("Invalid array length."));
        if (newLength > MaxLength)
            throw new Exception($"RangeError: Array length {newLength} exceeds ECMA-262 uint32 maximum.");

        if (newLength == _length) return;

        if (newLength < _length)
        {
            // ArraySetLength deletes own indices from oldLen-1 downward. A
            // non-configurable index blocks further truncation and leaves the
            // final length at index+1, while already-deleted higher entries
            // stay deleted (ECMA-262 10.4.2.4 steps 16-17).
            long effectiveLength = newLength;
            if (_descriptors != null)
            {
                foreach (var pair in _descriptors)
                {
                    if (!pair.Value.HasExplicitDescriptor || pair.Value.Configurable)
                        continue;
                    if (!uint.TryParse(pair.Key,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out uint index)
                        || index == uint.MaxValue
                        || index < newLength
                        || !HasIndex(index))
                    {
                        continue;
                    }
                    effectiveLength = Math.Max(effectiveLength, (long)index + 1);
                }
            }

            // Drop entries at indices >= the length that the descending
            // deletion process was able to reach.
            if (_sparse != null)
            {
                List<uint>? toRemove = null;
                foreach (var key in _sparse.Keys)
                {
                    if ((long)key >= effectiveLength)
                    {
                        toRemove ??= [];
                        toRemove.Add(key);
                    }
                }
                if (toRemove != null)
                {
                    foreach (var k in toRemove) _sparse.Remove(k);
                }
            }
            if (_indexAccessors != null)
            {
                foreach (var key in _indexAccessors.Keys.Where(
                    key => key >= effectiveLength).ToArray())
                    _indexAccessors.Remove(key);
            }
            if (_descriptors != null)
            {
                foreach (var key in _descriptors.Keys.Where(key =>
                {
                    return uint.TryParse(key,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out uint index)
                        && index != uint.MaxValue
                        && index >= effectiveLength;
                }).ToArray())
                {
                    _descriptors.Remove(key);
                }
            }
            while (_dense.Count > effectiveLength)
                _dense.RemoveAt(_dense.Count - 1);
            _length = effectiveLength;
            TryCollapseSparse();
            return;
        }

        // Extend: grow length. If we're already sparse, just bump _length and let
        // reads return undefined. If dense and growth is small, pad with undefined
        // (matches existing behavior of conflating holes with undefined). Large
        // growth transitions to sparse and creates a true hole tail.
        long growth = newLength - _length;
        if (_sparse != null || growth > SparseThreshold || newLength > int.MaxValue)
        {
            _sparse ??= new Dictionary<uint, object?>();
            _length = newLength;
            return;
        }

        // Pad with ArrayHole — `a.length = N` (N > length) creates holes, not undefined.
        while (_dense.Count < newLength)
            _dense.Add(ArrayHole.Instance);
        _length = _dense.Count;
    }

    /// <summary>int-based SetLength — widens to long.</summary>
    public void SetLength(int newLength) => SetLength((long)newLength);

    // -----------------------------------------------------------------------
    // Legacy Try* mutators — same semantics as before, now routed through the
    // dual-mode helpers so they stay correct on sparse arrays.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Adds an element to the end of the array. Respects frozen/sealed state.
    /// </summary>
    /// <returns>True if the element was added, false if blocked by frozen/sealed state.</returns>
    public bool TryAdd(object? value)
    {
        if (IsFrozen || IsSealed) return false;
        Add(value);
        return true;
    }

    /// <summary>
    /// Adds an element to the end of the array with strict mode behavior.
    /// In strict mode, throws TypeError for additions to frozen/sealed arrays.
    /// </summary>
    public bool TryAddStrict(object? value, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
                throw new Exception($"TypeError: Cannot add elements to a frozen or sealed array");
            return false;
        }
        Add(value);
        return true;
    }

    /// <summary>
    /// Removes the last element. Respects frozen/sealed state.
    /// </summary>
    /// <returns>The removed element, or null if blocked or empty.</returns>
    public object? TryPop()
    {
        if (IsFrozen || IsSealed || _length == 0) return null;
        return RemoveLast();
    }

    /// <summary>
    /// Removes the last element with strict mode behavior.
    /// In strict mode, throws TypeError for removals from frozen/sealed arrays.
    /// </summary>
    public object? TryPopStrict(bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode && _length > 0)
                throw new Exception($"TypeError: Cannot remove elements from a frozen or sealed array");
            return null;
        }
        if (_length == 0) return null;
        return RemoveLast();
    }

    /// <summary>
    /// Removes the first element. Respects frozen/sealed state. O(1) with Deque (dense only).
    /// </summary>
    /// <returns>The removed element, or null if blocked or empty.</returns>
    public object? TryShift()
    {
        if (IsFrozen || IsSealed || _length == 0) return null;
        return RemoveFirst();
    }

    /// <summary>
    /// Removes the first element with strict mode behavior.
    /// </summary>
    public object? TryShiftStrict(bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode && _length > 0)
                throw new Exception($"TypeError: Cannot remove elements from a frozen or sealed array");
            return null;
        }
        if (_length == 0) return null;
        return RemoveFirst();
    }

    /// <summary>
    /// Adds an element to the beginning. Respects frozen/sealed state.
    /// </summary>
    public bool TryUnshift(object? value)
    {
        if (IsFrozen || IsSealed) return false;
        AddFirst(value);
        return true;
    }

    /// <summary>
    /// Adds an element to the beginning with strict mode behavior.
    /// </summary>
    public bool TryUnshiftStrict(object? value, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
                throw new Exception($"TypeError: Cannot add elements to a frozen or sealed array");
            return false;
        }
        AddFirst(value);
        return true;
    }

    /// <summary>
    /// Reverses the array in place. Respects frozen state.
    /// </summary>
    public bool TryReverse()
    {
        if (IsFrozen) return false;
        ReverseInPlace();
        return true;
    }

    /// <summary>
    /// Reverses the array in place with strict mode behavior.
    /// </summary>
    public bool TryReverseStrict(bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
                throw new Exception($"TypeError: Cannot modify a frozen array");
            return false;
        }
        ReverseInPlace();
        return true;
    }

    // -----------------------------------------------------------------------
    // Named properties and descriptors (unchanged from Stage A).
    // -----------------------------------------------------------------------

    private Dictionary<string, object?>? _namedProperties;
    private Dictionary<string, (ISharpTSCallable? Get, ISharpTSCallable? Set)>? _namedAccessors;
    private Dictionary<string, PropertyDescriptorFlags>? _descriptors;
    private Dictionary<uint, (ISharpTSCallable? Get, ISharpTSCallable? Set)>? _indexAccessors;
    private SharpTSObject? _symbolProperties;

    private SharpTSObject SymbolProperties => _symbolProperties ??= new SharpTSObject([]);

    internal IEnumerable<SharpTSSymbol> GetSymbolPropertyNames()
        => _symbolProperties?.GetSymbolPropertyNames() ?? [];

    internal bool HasSymbolProperty(SharpTSSymbol symbol)
        => _symbolProperties?.HasSymbolProperty(symbol) ?? false;

    internal object? GetBySymbol(SharpTSSymbol symbol)
        => _symbolProperties?.GetBySymbol(symbol);

    internal bool TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_symbolProperties is not null)
            return _symbolProperties.TryGetSymbolAccessor(symbol, out getter, out setter);
        getter = null;
        setter = null;
        return false;
    }

    internal void SetBySymbol(SharpTSSymbol symbol, object? value)
    {
        if (IsFrozen || !IsExtensible && !HasSymbolProperty(symbol)) return;
        SymbolProperties.SetBySymbol(symbol, value);
    }

    internal void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode)
    {
        if (!IsExtensible && !HasSymbolProperty(symbol))
        {
            if (strictMode)
                throw StrictModeErrors.TypeError(
                    "Cannot add symbol property to a non-extensible array");
            return;
        }
        SymbolProperties.SetBySymbolStrict(symbol, value, strictMode);
    }

    internal bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
    {
        if (!HasSymbolProperty(symbol)) return true;
        return SymbolProperties.DeleteBySymbolStrict(symbol, strictMode);
    }

    internal bool DefineProperty(
        SharpTSSymbol symbol, SharpTSPropertyDescriptor descriptor)
    {
        if (!IsExtensible && !HasSymbolProperty(symbol)) return false;
        return SymbolProperties.DefineProperty(symbol, descriptor);
    }

    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(SharpTSSymbol symbol)
        => _symbolProperties?.GetOwnPropertyDescriptor(symbol);

    internal bool TryGetIndexAccessor(
        long index, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (index >= 0 && index <= uint.MaxValue
            && _indexAccessors?.TryGetValue((uint)index, out var pair) == true)
        {
            getter = pair.Get;
            setter = pair.Set;
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>
    /// Gets a named property value from the array.
    /// </summary>
    public object? GetNamedProperty(string name)
    {
        if (_namedProperties?.TryGetValue(name, out var value) == true)
            return value;
        return null;
    }

    internal bool TryGetNamedAccessor(
        string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_namedAccessors?.TryGetValue(name, out var pair) == true)
        {
            getter = pair.Get;
            setter = pair.Set;
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>
    /// Checks if a named property exists on the array.
    /// </summary>
    public bool HasNamedProperty(string name)
        => (_namedProperties?.ContainsKey(name) ?? false)
            || (_namedAccessors?.ContainsKey(name) ?? false);

    internal IEnumerable<string> NamedPropertyNames
    {
        get
        {
            if (_namedProperties is not null)
                foreach (string key in _namedProperties.Keys)
                    yield return key;
            if (_namedAccessors is not null)
                foreach (string key in _namedAccessors.Keys)
                    yield return key;
        }
    }

    /// <summary>
    /// Checks the array's own properties, including its non-enumerable length
    /// property, present numeric indices, and user-defined named properties.
    /// </summary>
    internal bool HasOwnProperty(string name)
    {
        if (name == "length") return true;
        if (uint.TryParse(name, out uint index) && index < uint.MaxValue)
            return HasIndex(index)
                || (_indexAccessors?.ContainsKey(index) ?? false)
                || HasNamedProperty(name);
        return HasNamedProperty(name);
    }

    /// <summary>
    /// Enumerates this array's own enumerable string keys, honoring explicit
    /// descriptors on both indexed and named properties.
    /// </summary>
    internal IEnumerable<string> OwnEnumerableKeys()
    {
        foreach (string key in OwnStringKeys())
        {
            if (key == "length") continue;
            if (_descriptors?.TryGetValue(key, out var flags) != true || flags.Enumerable)
                yield return key;
        }
    }

    /// <summary>
    /// Enumerates all own string keys in Array [[OwnPropertyKeys]] order without
    /// scanning the potentially huge sparse length range.
    /// </summary>
    internal IEnumerable<string> OwnStringKeys()
    {
        int denseLimit = (int)Math.Min(_length, _dense.Count);
        for (int index = 0; index < denseLimit; index++)
        {
            if (_dense[index] is not ArrayHole
                || (_indexAccessors?.ContainsKey((uint)index) ?? false))
                yield return index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        if (_sparse is not null || _indexAccessors is not null)
        {
            var remainingIndices = new SortedSet<uint>();
            if (_sparse is not null)
            {
                foreach (uint index in _sparse.Keys)
                    if (index >= denseLimit && index < _length)
                        remainingIndices.Add(index);
            }
            if (_indexAccessors is not null)
            {
                foreach (uint index in _indexAccessors.Keys)
                    if (index >= denseLimit && index < _length)
                        remainingIndices.Add(index);
            }
            foreach (uint index in remainingIndices)
                yield return index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        yield return "length";
        foreach (string key in NamedPropertyNames)
            yield return key;
    }

    internal bool IsPropertyEnumerable(string name)
        => HasOwnProperty(name)
            && (_descriptors?.TryGetValue(name, out var flags) != true || flags.Enumerable);

    /// <summary>
    /// Sets a named property value on the array.
    /// </summary>
    public void SetNamedProperty(string name, object? value)
    {
        if (IsFrozen) return;
        if (!IsExtensible && !HasNamedProperty(name)) return;

        _namedProperties ??= new Dictionary<string, object?>();
        _namedProperties[name] = value;
    }

    /// <summary>
    /// Deletes an own indexed or named property while enforcing its
    /// configurable descriptor flag.
    /// </summary>
    internal bool DeletePropertyStrict(string name, bool strictMode)
    {
        var descriptor = GetOwnPropertyDescriptor(name);
        if (descriptor is null) return true;
        if (!descriptor.Configurable)
        {
            if (strictMode)
            {
                throw new ThrowException(new SharpTSTypeError(
                    $"Cannot delete property '{name}' of array"));
            }
            return false;
        }

        if (uint.TryParse(name, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out uint index)
            && index < uint.MaxValue)
        {
            DeleteAt(index);
        }
        else
        {
            _namedProperties?.Remove(name);
            _namedAccessors?.Remove(name);
        }
        _descriptors?.Remove(name);
        return true;
    }

    /// <summary>
    /// Applies ordinary SetIntegrityLevel descriptor changes to named expandos.
    /// Array indices and length retain their separate array-exotic handling.
    /// </summary>
    private void SetNamedPropertyIntegrityLevel(bool frozen)
    {
        if (_namedProperties is null && _namedAccessors is null) return;
        _descriptors ??= [];
        foreach (var name in NamedPropertyNames)
        {
            PropertyDescriptorFlags current = PropertyDescriptorFlags.Default;
            if (_descriptors.TryGetValue(name, out var stored))
                current = stored;
            _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
                writable: frozen ? false : current.Writable,
                enumerable: current.Enumerable,
                configurable: false);
        }
    }

    /// <summary>
    /// Defines or modifies a property with the given descriptor.
    /// For arrays, this supports both numeric indices and named properties.
    /// </summary>
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        if (IsFrozen) return false;

        // ArraySetLength (ECMA-262 §10.4.2.4). The length property is a
        // non-enumerable, non-configurable data property whose value controls
        // the array's indexed storage rather than an ordinary named expando.
        if (name == "length")
        {
            if (descriptor.HasGet || descriptor.HasSet
                || (descriptor.HasEnumerable && descriptor.Enumerable)
                || (descriptor.HasConfigurable && descriptor.Configurable)
                || (!_lengthWritable && descriptor.HasWritable && descriptor.Writable))
            {
                return false;
            }

            if (descriptor.HasValue)
            {
                if (descriptor.Value is not double length
                    || double.IsNaN(length)
                    || double.IsInfinity(length)
                    || length < 0
                    || length > MaxLength
                    || Math.Truncate(length) != length)
                {
                    throw new ThrowException(new SharpTSRangeError("Invalid array length."));
                }

                if (!_lengthWritable && (long)length != _length)
                    return false;
                SetLength((long)length);
            }

            if (descriptor.HasWritable && !descriptor.Writable)
                _lengthWritable = false;
            return true;
        }

        // Numeric index path — accept full uint32 range per ECMA-262.
        if (uint.TryParse(name, out uint uindex) && uindex < uint.MaxValue)
        {
            long index = uindex;
            bool hasExisting = HasIndex(index);
            bool existingIsAccessor = TryGetIndexAccessor(index, out var existingGetter, out var existingSetter);
            bool descriptorIsAccessor = descriptor.HasGet || descriptor.HasSet;
            bool descriptorIsData = descriptor.HasValue || descriptor.HasWritable;

            PropertyDescriptorFlags existingFlags = PropertyDescriptorFlags.Default;
            if (hasExisting && _descriptors?.TryGetValue(name, out existingFlags) != true)
                existingFlags = PropertyDescriptorFlags.Default;

            // ValidateAndApplyPropertyDescriptor (§10.1.6.3): a
            // non-configurable index cannot become configurable, change
            // enumerability/kind, replace an accessor, or relax/change a
            // non-writable data property.
            if (hasExisting && existingFlags.HasExplicitDescriptor && !existingFlags.Configurable)
            {
                if ((descriptor.HasConfigurable && descriptor.Configurable)
                    || (descriptor.HasEnumerable
                        && descriptor.Enumerable != existingFlags.Enumerable))
                {
                    return false;
                }

                if (existingIsAccessor)
                {
                    if (descriptorIsData
                        || (descriptor.HasGet
                            && !SameValue(descriptor.Get, existingGetter))
                        || (descriptor.HasSet
                            && !SameValue(descriptor.Set, existingSetter)))
                    {
                        return false;
                    }
                }
                else
                {
                    if (descriptorIsAccessor)
                        return false;
                    if (!existingFlags.Writable
                        && ((descriptor.HasWritable && descriptor.Writable)
                            || (descriptor.HasValue
                                && !SameValue(descriptor.Value, UnholeForRead(GetCore(index))))))
                    {
                        return false;
                    }
                }
            }

            if (!IsExtensible && !hasExisting)
                return false;

            // Omitted attributes preserve an existing property's values. They
            // default to false only when this operation creates a new property
            // or changes between data and accessor kinds.
            bool preservesKind = hasExisting
                && (!descriptorIsAccessor && !descriptorIsData
                    || descriptorIsAccessor == existingIsAccessor);
            bool writable = descriptor.HasWritable
                ? descriptor.Writable
                : preservesKind ? existingFlags.Writable : false;
            bool enumerable = descriptor.HasEnumerable
                ? descriptor.Enumerable
                : hasExisting ? existingFlags.Enumerable : false;
            bool configurable = descriptor.HasConfigurable
                ? descriptor.Configurable
                : hasExisting ? existingFlags.Configurable : false;

            bool becomesAccessor = descriptorIsAccessor
                || (!descriptorIsData && existingIsAccessor);

            if (!hasExisting && index >= _length)
            {
                // Extending an array with an accessor still advances length, but
                // the indexed data slot itself remains a hole.
                SetCoreWithExtend(index, becomesAccessor
                    ? ArrayHole.Instance
                    : descriptor.HasValue ? descriptor.Value : SharpTSUndefined.Instance);
            }

            if (becomesAccessor)
            {
                _indexAccessors ??= [];
                _indexAccessors[uindex] = (
                    descriptor.HasGet ? descriptor.Get : existingIsAccessor ? existingGetter : null,
                    descriptor.HasSet ? descriptor.Set : existingIsAccessor ? existingSetter : null);
                // Accessors have no data value at the same index.
                if (_sparse == null || index < _dense.Count)
                    _dense[(int)index] = ArrayHole.Instance;
                else
                    _sparse.Remove(uindex);
            }
            else
            {
                _indexAccessors?.Remove(uindex);
                if (descriptor.HasValue)
                    SetCore(index, descriptor.Value);
                else if (!hasExisting || existingIsAccessor)
                    SetCore(index, SharpTSUndefined.Instance);
            }

            _descriptors ??= new Dictionary<string, PropertyDescriptorFlags>();
            _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
                writable,
                enumerable,
                configurable);
            return true;
        }

        // Named-property path
        bool hasNamedData = _namedProperties?.ContainsKey(name) ?? false;
        bool hasNamedAccessor = _namedAccessors?.ContainsKey(name) ?? false;
        bool hasNamedProperty = hasNamedData || hasNamedAccessor;
        (ISharpTSCallable? Get, ISharpTSCallable? Set) existingNamedAccessor = default;
        _namedAccessors?.TryGetValue(name, out existingNamedAccessor);
        bool namedDescriptorIsAccessor = descriptor.HasGet || descriptor.HasSet;
        bool namedDescriptorIsData = descriptor.HasValue || descriptor.HasWritable;
        PropertyDescriptorFlags namedFlags = PropertyDescriptorFlags.Default;
        if (hasNamedProperty && _descriptors?.TryGetValue(name, out namedFlags) != true)
            namedFlags = PropertyDescriptorFlags.Default;

        // Named properties use ordinary descriptor validation, including
        // preserving accessor kind and getter/setter identity on arrays and
        // arguments objects.
        if (hasNamedProperty && namedFlags.HasExplicitDescriptor && !namedFlags.Configurable)
        {
            if ((descriptor.HasConfigurable && descriptor.Configurable)
                || (descriptor.HasEnumerable
                    && descriptor.Enumerable != namedFlags.Enumerable))
            {
                return false;
            }

            if (hasNamedAccessor)
            {
                if (namedDescriptorIsData
                    || (descriptor.HasGet
                        && !SameValue(descriptor.Get, existingNamedAccessor.Get))
                    || (descriptor.HasSet
                        && !SameValue(descriptor.Set, existingNamedAccessor.Set)))
                {
                    return false;
                }
            }
            else if (namedDescriptorIsAccessor
                || (!namedFlags.Writable
                    && ((descriptor.HasWritable && descriptor.Writable)
                        || (descriptor.HasValue
                            && !SameValue(descriptor.Value, _namedProperties![name])))))
            {
                return false;
            }
        }

        if (!IsExtensible && !hasNamedProperty)
            return false;

        bool preservesNamedKind = hasNamedProperty
            && (!namedDescriptorIsAccessor && !namedDescriptorIsData
                || namedDescriptorIsAccessor == hasNamedAccessor);
        bool becomesNamedAccessor = namedDescriptorIsAccessor
            || (!namedDescriptorIsData && hasNamedAccessor);

        if (becomesNamedAccessor)
        {
            _namedAccessors ??= [];
            _namedAccessors[name] = (
                descriptor.HasGet ? descriptor.Get : hasNamedAccessor ? existingNamedAccessor.Get : null,
                descriptor.HasSet ? descriptor.Set : hasNamedAccessor ? existingNamedAccessor.Set : null);
            _namedProperties?.Remove(name);
        }
        else
        {
            _namedAccessors?.Remove(name);
            _namedProperties ??= [];
            if (descriptor.HasValue)
                _namedProperties[name] = descriptor.Value;
            else if (!hasNamedData || hasNamedAccessor)
                _namedProperties[name] = SharpTSUndefined.Instance;
        }

        _descriptors ??= new Dictionary<string, PropertyDescriptorFlags>();
        _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
            descriptor.HasWritable
                ? descriptor.Writable
                : preservesNamedKind && !becomesNamedAccessor && namedFlags.Writable,
            descriptor.HasEnumerable ? descriptor.Enumerable : hasNamedProperty && namedFlags.Enumerable,
            descriptor.HasConfigurable ? descriptor.Configurable : hasNamedProperty && namedFlags.Configurable);
        return true;
    }

    /// <summary>
    /// Gets the property descriptor for the given property name.
    /// Returns null if the property doesn't exist.
    /// </summary>
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name == "length")
        {
            return new SharpTSPropertyDescriptor
            {
                Value = (double)_length,  // full long → double (accurate to 2^53)
                HasValue = true,
                Writable = _lengthWritable,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = false,
                HasConfigurable = true,
            };
        }

        if (uint.TryParse(name, out uint uindex) && (long)uindex < _length)
        {
            long index = uindex;
            if (TryGetIndexAccessor(index, out var getter, out var setter))
            {
                PropertyDescriptorFlags accessorFlags = default;
                if (_descriptors?.TryGetValue(name, out accessorFlags) != true)
                    accessorFlags = PropertyDescriptorFlags.Default;
                return new SharpTSPropertyDescriptor
                {
                    Get = getter,
                    Set = setter,
                    RawGet = getter,
                    RawSet = setter,
                    HasGet = true,
                    HasSet = true,
                    Enumerable = accessorFlags.Enumerable,
                    HasEnumerable = true,
                    Configurable = accessorFlags.Configurable,
                    HasConfigurable = true,
                };
            }
            // Holes have no own property descriptor — ECMA-262 HasOwnProperty
            // returns false, and Object.getOwnPropertyDescriptor returns undefined.
            if (!HasIndex(index))
                return null;

            PropertyDescriptorFlags flags = default;
            if (_descriptors?.TryGetValue(name, out flags) != true)
                flags = PropertyDescriptorFlags.Default;

            return new SharpTSPropertyDescriptor
            {
                Value = UnholeForRead(GetCore(index)),
                HasValue = true,
                Writable = flags.Writable,
                HasWritable = true,
                Enumerable = flags.Enumerable,
                HasEnumerable = true,
                Configurable = flags.Configurable,
                HasConfigurable = true,
            };
        }

        if (_namedAccessors?.TryGetValue(name, out var accessor) == true)
        {
            PropertyDescriptorFlags flags = default;
            if (_descriptors?.TryGetValue(name, out flags) != true)
                flags = PropertyDescriptorFlags.Default;

            return new SharpTSPropertyDescriptor
            {
                Get = accessor.Get,
                Set = accessor.Set,
                RawGet = accessor.Get,
                RawSet = accessor.Set,
                HasGet = true,
                HasSet = true,
                Enumerable = flags.Enumerable,
                HasEnumerable = true,
                Configurable = flags.Configurable,
                HasConfigurable = true,
            };
        }

        if (_namedProperties?.TryGetValue(name, out var value) == true)
        {
            PropertyDescriptorFlags flags = default;
            if (_descriptors?.TryGetValue(name, out flags) != true)
                flags = PropertyDescriptorFlags.Default;

            return new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = flags.Writable,
                HasWritable = true,
                Enumerable = flags.Enumerable,
                HasEnumerable = true,
                Configurable = flags.Configurable,
                HasConfigurable = true,
            };
        }

        return null;
    }

    /// <summary>
    /// ECMA-262 SameValue comparison used by descriptor validation.
    /// </summary>
    private static bool SameValue(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is double ld && right is double rd)
        {
            if (double.IsNaN(ld) && double.IsNaN(rd)) return true;
            if (ld == 0 && rd == 0)
                return BitConverter.DoubleToInt64Bits(ld)
                    == BitConverter.DoubleToInt64Bits(rd);
            return ld.Equals(rd);
        }
        return left?.Equals(right) == true;
    }

    public override string ToString()
    {
        // Render holes as "undefined" for debug; public-facing toString/join
        // (which renders holes as empty string) live in the array built-ins.
        // Cap rendering at int.MaxValue entries to avoid pathological ToString on
        // huge sparse arrays — debug output doesn't need spec fidelity past that.
        var sb = new System.Text.StringBuilder("[");
        long limit = _length > int.MaxValue ? int.MaxValue : _length;
        for (long i = 0; i < limit; i++)
        {
            if (i > 0) sb.Append(", ");
            var raw = GetCore(i);
            sb.Append(raw is ArrayHole ? "undefined" : raw?.ToString() ?? "null");
        }
        if (_length > int.MaxValue) sb.Append(", ... (truncated)");
        sb.Append(']');
        return sb.ToString();
    }
}
