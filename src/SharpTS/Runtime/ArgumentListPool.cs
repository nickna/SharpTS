using System.Buffers;

namespace SharpTS.Runtime;

/// <summary>
/// Provides pooled argument lists to reduce allocations during function calls.
/// </summary>
/// <remarks>
/// Function calls are extremely hot paths - every method call, callback, array operation, etc.
/// creates a new List&lt;object?&gt; for arguments. This pool maintains a small set of reusable
/// lists that are rented and returned, significantly reducing GC pressure.
///
/// Thread-safe via thread-local storage - each thread has its own pool instance.
/// </remarks>
public static class ArgumentListPool
{
    // Thread-local pool to avoid contention
    [ThreadStatic]
    private static PooledArgumentList[]? _threadLocalPool;

    // Pool size per thread - small to avoid memory waste
    private const int PoolSize = 8;

    /// <summary>
    /// Rents a pooled argument list. Must be returned via Return() after use.
    /// </summary>
    /// <returns>A pooled argument list that can be used for function arguments.</returns>
    public static PooledArgumentList Rent()
    {
        _threadLocalPool ??= new PooledArgumentList[PoolSize];

        for (int i = 0; i < PoolSize; i++)
        {
            var list = _threadLocalPool[i];
            if (list != null && !list.IsRented)
            {
                list.IsRented = true;
                list.Clear();
                return list;
            }
        }

        // No available pooled list - create new one and try to store it
        var newList = new PooledArgumentList();
        newList.IsRented = true;

        // Find empty slot
        for (int i = 0; i < PoolSize; i++)
        {
            if (_threadLocalPool[i] == null)
            {
                _threadLocalPool[i] = newList;
                break;
            }
        }

        return newList;
    }

    /// <summary>
    /// Returns a pooled argument list back to the pool.
    /// </summary>
    /// <param name="list">The list to return.</param>
    public static void Return(PooledArgumentList list)
    {
        list.IsRented = false;
        list.Clear();
    }
}

/// <summary>
/// A pooled list of arguments that can be reused to avoid allocations.
/// Implements List&lt;object?&gt; semantics for compatibility with ISharpTSCallable.
/// </summary>
public sealed class PooledArgumentList : List<object?>
{
    internal bool IsRented;

    /// <summary>
    /// Creates a pooled argument list with pre-allocated capacity.
    /// </summary>
    public PooledArgumentList() : base(8) // Most calls have < 8 args
    {
    }
}
