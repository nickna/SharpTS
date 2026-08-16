namespace SharpTS.Runtime.Types;

/// <summary>
/// Process-global store backing <c>worker_threads.getEnvironmentData</c> /
/// <c>setEnvironmentData</c>.
/// </summary>
/// <remarks>
/// Node keeps an internal environment-data Map keyed by arbitrary values, independent of
/// <c>process.env</c>. SharpTS previously routed these to <c>process.env</c>
/// (<c>Environment.Get/SetEnvironmentVariable</c>), which only supports string keys/values
/// and leaks into the OS environment. This store is a real per-process Map keyed by arbitrary
/// values; because a worker child runs in the same process under its own interpreter, a value
/// set on the parent is visible to the worker through this shared store (Node snapshots a copy
/// into each worker at spawn — SharpTS shares the store live, an observable equivalence for
/// data set before the worker reads it).
///
/// Values are deep-copied on set (structured clone) so a stored object is independent of the
/// caller's reference, matching Node. Setting a value of <c>null</c>/<c>undefined</c> removes
/// the key (Node deletes it), so an absent key reads back as <c>undefined</c>.
/// </remarks>
public static class WorkerEnvironmentData
{
    private static readonly object _gate = new();
    private static readonly Dictionary<object, object?> _data = new();

    // Stand-in for a null/undefined key, which a Dictionary cannot store directly.
    private static readonly object _nullKey = new();

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>. A null value removes the
    /// key (Node treats <c>setEnvironmentData(key, undefined)</c> as a delete).
    /// </summary>
    public static void Set(object? key, object? value)
    {
        var k = key ?? _nullKey;
        lock (_gate)
        {
            if (value is null)
                _data.Remove(k);
            else
                _data[k] = StructuredClone.Clone(value);
        }
    }

    /// <summary>
    /// Reads the value stored under <paramref name="key"/>. Returns true and the value when
    /// present; false (and null) when absent, so callers can surface <c>undefined</c>.
    /// </summary>
    public static bool TryGet(object? key, out object? value)
    {
        var k = key ?? _nullKey;
        lock (_gate)
            return _data.TryGetValue(k, out value);
    }

    /// <summary>
    /// Reads the value stored under <paramref name="key"/>, or null when absent. Convenience
    /// for the compiled emit path, which has no out-parameter calling convention.
    /// </summary>
    public static object? Get(object? key)
    {
        var k = key ?? _nullKey;
        lock (_gate)
            return _data.TryGetValue(k, out var value) ? value : null;
    }
}
