namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for iterators returned by Map/Set keys(), values(), entries() methods.
/// </summary>
/// <remarks>
/// Wraps an IEnumerable&lt;object?&gt; to allow iteration via for...of loops.
/// Each call to keys/values/entries creates a new iterator with its own enumeration state.
/// </remarks>
public class SharpTSIterator
{
    private readonly IEnumerable<object?> _source;

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

    public override string ToString() => "[object Iterator]";
}
