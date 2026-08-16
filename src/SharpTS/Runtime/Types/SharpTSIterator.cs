using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for iterators returned by Map/Set keys(), values(), entries() methods.
/// Also supports ES2025 Iterator Helpers: map, filter, take, drop, flatMap, reduce, toArray, forEach, some, every, find.
/// </summary>
/// <remarks>
/// Wraps an IEnumerable&lt;object?&gt; to allow iteration via for...of loops.
/// Each call to keys/values/entries creates a new iterator with its own enumeration state.
/// The Next()/Return() protocol methods allow manual iteration.
/// </remarks>
public class SharpTSIterator : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Iterator;
    private readonly IEnumerable<object?> _source;
    private IEnumerator<object?>? _enumerator;
    private bool _done;

    public SharpTSIterator(IEnumerable<object?> source)
    {
        _source = source;
    }

    /// <summary>
    /// Gets the underlying enumerable for for...of iteration.
    /// </summary>
    public IEnumerable<object?> Elements => _source;

    /// <summary>
    /// Gets an enumerator for manual iteration.
    /// </summary>
    public IEnumerator<object?> GetEnumerator() => _source.GetEnumerator();

    /// <summary>
    /// Advances the iterator and returns the next result.
    /// </summary>
    public SharpTSIteratorResult Next()
    {
        if (_done)
            return new SharpTSIteratorResult(SharpTSUndefined.Instance, true);

        _enumerator ??= _source.GetEnumerator();
        if (_enumerator.MoveNext())
            return new SharpTSIteratorResult(_enumerator.Current, false);

        _done = true;
        return new SharpTSIteratorResult(SharpTSUndefined.Instance, true);
    }

    /// <summary>
    /// Closes the iterator and returns the given value.
    /// </summary>
    public SharpTSIteratorResult Return(object? value)
    {
        _done = true;
        if (_enumerator is IDisposable d)
            d.Dispose();
        return new SharpTSIteratorResult(value, true);
    }

    public override string ToString() => "[object Iterator]";
}
