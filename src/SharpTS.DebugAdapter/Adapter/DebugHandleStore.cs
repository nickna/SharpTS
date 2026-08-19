using SharpTS.Runtime;
using SharpTS.DebugAdapter.Protocol;

namespace SharpTS.DebugAdapter.Adapter;

internal sealed record DebugScopeHandle(RuntimeEnvironment Environment, IReadOnlySet<string>? Names = null);

internal sealed class DebugHandleStore
{
    private const int MaximumHandles = 10_000;
    private readonly Dictionary<int, object> _handles = [];
    private readonly Dictionary<object, int> _reverse =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private int _nextHandle;

    public int Generation { get; private set; }

    public void Reset(int generation)
    {
        _handles.Clear();
        _reverse.Clear();
        _nextHandle = 0;
        Generation = generation;
    }

    public void Clear()
    {
        _handles.Clear();
        _reverse.Clear();
        Generation = 0;
    }

    public int Add(object value)
    {
        if (_reverse.TryGetValue(value, out int existing))
            return existing;
        if (_handles.Count >= MaximumHandles)
            throw new DapRequestException($"Stopped-state handle limit ({MaximumHandles}) exceeded.");
        int handle = checked(Generation * MaximumHandles + ++_nextHandle);
        _handles.Add(handle, value);
        _reverse.Add(value, handle);
        return handle;
    }

    public T Get<T>(int handle) where T : class
    {
        if (Generation == 0 || !_handles.TryGetValue(handle, out object? value) || value is not T typed)
            throw new DapRequestException("Variable reference is stale or invalid.");
        return typed;
    }
}
