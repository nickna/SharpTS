using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript arrays.
/// </summary>
/// <remarks>
/// Wraps a <c>Deque&lt;object?&gt;</c> to represent TypeScript arrays at runtime.
/// Uses a circular buffer for O(1) shift/unshift operations.
/// Provides indexed Get/Set access with bounds checking.
/// Used by <see cref="Interpreter"/> when evaluating array literals and array operations
/// (e.g., indexing, push, pop, map, filter).
/// </remarks>
/// <seealso cref="SharpTSObject"/>
public class SharpTSArray(Deque<object?> elements) : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Array;

    public Deque<object?> Elements { get; } = elements;

    /// <summary>
    /// Creates a SharpTSArray from any enumerable collection.
    /// </summary>
    public SharpTSArray(IEnumerable<object?> elements) : this(new Deque<object?>(elements)) { }

    /// <summary>
    /// Creates an empty SharpTSArray.
    /// </summary>
    public SharpTSArray() : this(new Deque<object?>()) { }

    /// <summary>
    /// Whether this array is frozen (no element additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this array is sealed (no element additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Freezes this array, preventing any element changes.
    /// </summary>
    public void Freeze()
    {
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
    }

    /// <summary>
    /// Seals this array, preventing element additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        IsSealed = true;
    }

    public object? Get(int index)
    {
        if (index < 0 || index >= Elements.Count)
        {
            throw new Exception("Index out of bounds.");
        }
        return Elements[index];
    }

    public void Set(int index, object? value)
    {
        if (IsFrozen)
        {
            // Frozen arrays silently ignore element modifications (JavaScript behavior)
            return;
        }

        if (index < 0 || index >= Elements.Count)
        {
            // Simplified: TS usually expands array, but let's be strict or expandable?
            // List<T> supports expansion via Add, but index access throws if OOB.
            // Let's allow strict bounds for now.
            throw new Exception("Index out of bounds.");
        }
        Elements[index] = value;
    }

    /// <summary>
    /// Sets an element at the given index with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen arrays.
    /// </summary>
    public void SetStrict(int index, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot assign to read only property '{index}' of array");
            }
            return;
        }

        if (index < 0 || index >= Elements.Count)
        {
            throw new Exception("Index out of bounds.");
        }
        Elements[index] = value;
    }

    /// <summary>
    /// Adds an element to the end of the array. Respects frozen/sealed state.
    /// </summary>
    /// <returns>True if the element was added, false if blocked by frozen/sealed state.</returns>
    public bool TryAdd(object? value)
    {
        if (IsFrozen || IsSealed)
        {
            return false;
        }
        Elements.Add(value);
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
            {
                throw new Exception($"TypeError: Cannot add elements to a frozen or sealed array");
            }
            return false;
        }
        Elements.Add(value);
        return true;
    }

    /// <summary>
    /// Removes the last element. Respects frozen/sealed state.
    /// </summary>
    /// <returns>The removed element, or null if blocked or empty.</returns>
    public object? TryPop()
    {
        if (IsFrozen || IsSealed || Elements.Count == 0)
        {
            return null;
        }
        var last = Elements[^1];
        Elements.RemoveAt(Elements.Count - 1);
        return last;
    }

    /// <summary>
    /// Removes the last element with strict mode behavior.
    /// In strict mode, throws TypeError for removals from frozen/sealed arrays.
    /// </summary>
    public object? TryPopStrict(bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode && Elements.Count > 0)
            {
                throw new Exception($"TypeError: Cannot remove elements from a frozen or sealed array");
            }
            return null;
        }
        if (Elements.Count == 0)
        {
            return null;
        }
        var last = Elements[^1];
        Elements.RemoveAt(Elements.Count - 1);
        return last;
    }

    /// <summary>
    /// Removes the first element. Respects frozen/sealed state. O(1) with Deque.
    /// </summary>
    /// <returns>The removed element, or null if blocked or empty.</returns>
    public object? TryShift()
    {
        if (IsFrozen || IsSealed || Elements.Count == 0)
        {
            return null;
        }
        return Elements.RemoveFirst();
    }

    /// <summary>
    /// Removes the first element with strict mode behavior. O(1) with Deque.
    /// In strict mode, throws TypeError for removals from frozen/sealed arrays.
    /// </summary>
    public object? TryShiftStrict(bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode && Elements.Count > 0)
            {
                throw new Exception($"TypeError: Cannot remove elements from a frozen or sealed array");
            }
            return null;
        }
        if (Elements.Count == 0)
        {
            return null;
        }
        return Elements.RemoveFirst();
    }

    /// <summary>
    /// Adds an element to the beginning. Respects frozen/sealed state. O(1) with Deque.
    /// </summary>
    /// <returns>True if the element was added, false if blocked.</returns>
    public bool TryUnshift(object? value)
    {
        if (IsFrozen || IsSealed)
        {
            return false;
        }
        Elements.AddFirst(value);
        return true;
    }

    /// <summary>
    /// Adds an element to the beginning with strict mode behavior. O(1) with Deque.
    /// In strict mode, throws TypeError for additions to frozen/sealed arrays.
    /// </summary>
    public bool TryUnshiftStrict(object? value, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add elements to a frozen or sealed array");
            }
            return false;
        }
        Elements.AddFirst(value);
        return true;
    }

    /// <summary>
    /// Reverses the array in place. Respects frozen state (sealed allows in-place modification).
    /// </summary>
    /// <returns>True if reversed, false if frozen.</returns>
    public bool TryReverse()
    {
        if (IsFrozen)
        {
            return false;
        }
        Elements.Reverse();
        return true;
    }

    /// <summary>
    /// Reverses the array in place with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen arrays.
    /// </summary>
    public bool TryReverseStrict(bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot modify a frozen array");
            }
            return false;
        }
        Elements.Reverse();
        return true;
    }

    public override string ToString() => $"[{string.Join(", ", Elements.Select(e => e?.ToString() ?? "null"))}]";
}
