using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region FinalizationRegistry Support

    /// <summary>
    /// Creates a FinalizationRegistry. Returns a tuple [callback, pendingQueue, entries].
    /// </summary>
    public static object CreateFinalizationRegistry(object? callback)
    {
        if (callback == null)
            throw new Exception("Runtime Error: FinalizationRegistry constructor requires a callback function.");

        var pendingQueue = new ConcurrentQueue<object?>();
        // Store as object[]: [0]=callback, [1]=pendingQueue, [2]=entries list, [3]=lock
        return new object?[] { callback, pendingQueue, new List<object?[]>(), new object() };
    }

    /// <summary>
    /// Registers a target with the FinalizationRegistry.
    /// </summary>
    public static void FinalizationRegistryRegister(object? registry, object? target, object? heldValue, object? token)
    {
        if (target == null)
            throw new Exception("Runtime Error: FinalizationRegistry target cannot be null or undefined.");
        if (target is string or double or bool or int or long or float or decimal)
            throw new Exception("Runtime Error: FinalizationRegistry target must be an object.");

        if (registry is not object?[] reg) return;
        var queue = (ConcurrentQueue<object?>)reg[1]!;
        var entries = (List<object?[]>)reg[2]!;
        var lockObj = reg[3]!;

        var helper = new FinRegGCHelper(heldValue, queue);
        _finRegPokeTable.AddOrUpdate(target, helper);

        lock (lockObj)
        {
            entries.Add([target, heldValue, token, helper]);
        }
    }

    /// <summary>
    /// Unregisters entries matching the given token.
    /// </summary>
    public static bool FinalizationRegistryUnregister(object? registry, object? token)
    {
        if (token == null || registry is not object?[] reg) return false;
        var entries = (List<object?[]>)reg[2]!;
        var lockObj = reg[3]!;

        bool removed = false;
        lock (lockObj)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i][2] != null && ReferenceEquals(entries[i][2], token))
                {
                    ((FinRegGCHelper)entries[i][3]!).Suppress();
                    entries.RemoveAt(i);
                    removed = true;
                }
            }
        }
        return removed;
    }

    private static readonly ConditionalWeakTable<object, FinRegGCHelper> _finRegPokeTable = new();

    internal sealed class FinRegGCHelper
    {
        private readonly object? _heldValue;
        private readonly ConcurrentQueue<object?> _queue;
        private volatile bool _suppressed;

        public FinRegGCHelper(object? heldValue, ConcurrentQueue<object?> queue)
        {
            _heldValue = heldValue;
            _queue = queue;
        }

        public void Suppress() => _suppressed = true;

        ~FinRegGCHelper()
        {
            if (!_suppressed)
            {
                _queue.Enqueue(_heldValue);
            }
        }
    }

    #endregion
}
