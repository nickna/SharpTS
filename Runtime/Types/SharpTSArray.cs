namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript arrays.
/// </summary>
/// <remarks>
/// Wraps a <c>List&lt;object?&gt;</c> to represent TypeScript arrays at runtime.
/// Provides indexed Get/Set access with bounds checking.
/// Used by <see cref="Interpreter"/> when evaluating array literals and array operations
/// (e.g., indexing, push, pop, map, filter).
/// </remarks>
/// <seealso cref="SharpTSObject"/>
public class SharpTSArray(List<object?> elements)
{
    public List<object?> Elements { get; } = elements;

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
         if (index < 0 || index >= Elements.Count)
        {
            // Simplified: TS usually expands array, but let's be strict or expandable?
            // List<T> supports expansion via Add, but index access throws if OOB.
            // Let's allow strict bounds for now.
            throw new Exception("Index out of bounds.");
        }
        Elements[index] = value;
    }

    public override string ToString() => $"[{string.Join(", ", Elements.Select(e => e?.ToString() ?? "null"))}]";
}
