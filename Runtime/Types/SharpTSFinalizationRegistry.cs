using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript FinalizationRegistry.
/// Invokes a cleanup callback when registered objects are garbage collected.
/// </summary>
public class SharpTSFinalizationRegistry : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.FinalizationRegistry;

    private readonly ISharpTSCallable _cleanupCallback;
    private readonly ConcurrentQueue<object?> _pendingCleanups = new();
    private readonly List<RegistrationEntry> _entries = [];
    private readonly object _lock = new();

    public SharpTSFinalizationRegistry(ISharpTSCallable cleanupCallback)
    {
        _cleanupCallback = cleanupCallback;
    }

    /// <summary>
    /// Registers a target object for cleanup notification.
    /// </summary>
    public void Register(object target, object? heldValue, object? unregisterToken)
    {
        ValidateTarget(target);

        var entry = new RegistrationEntry(target, heldValue, unregisterToken, _pendingCleanups);
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Unregisters all entries matching the given token.
    /// Returns true if any entries were removed.
    /// </summary>
    public bool Unregister(object? token)
    {
        if (token == null)
            return false;

        bool removed = false;
        lock (_lock)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Token != null && ReferenceEquals(_entries[i].Token, token))
                {
                    _entries[i].Suppress();
                    _entries.RemoveAt(i);
                    removed = true;
                }
            }
        }
        return removed;
    }

    /// <summary>
    /// Drains pending cleanups by invoking the cleanup callback for each.
    /// Called from the event loop tick.
    /// </summary>
    public void DrainCleanups(Execution.Interpreter? interpreter)
    {
        while (_pendingCleanups.TryDequeue(out var heldValue))
        {
            try
            {
                _cleanupCallback.CallBoxed(interpreter!, [heldValue]);
            }
            catch
            {
                // Cleanup callbacks should not throw
            }
        }
    }

    /// <summary>
    /// Test seam: enqueues a heldValue exactly as a GC-triggered finalizer would,
    /// so the event-loop drain wiring can be exercised deterministically (real GC
    /// timing is unobservable from a test).
    /// </summary>
    internal void EnqueueCleanupForTest(object? heldValue) => _pendingCleanups.Enqueue(heldValue);

    /// <summary>
    /// Returns true if there are pending cleanups.
    /// </summary>
    public bool HasPendingCleanups => !_pendingCleanups.IsEmpty;

    private static void ValidateTarget(object? target)
    {
        if (target == null)
            throw new Exception("Runtime Error: FinalizationRegistry target cannot be null or undefined.");
        if (target is string or double or bool or int or long or float or decimal)
            throw new Exception("Runtime Error: FinalizationRegistry target must be an object.");
    }

    public override string ToString() => "FinalizationRegistry {}";

    /// <summary>
    /// Helper class that triggers cleanup when the target is garbage collected.
    /// </summary>
    private sealed class RegistrationEntry
    {
        private readonly WeakReference<object> _target;
        private readonly object? _heldValue;
        private readonly ConcurrentQueue<object?> _queue;

        public object? Token { get; }

        public RegistrationEntry(object target, object? heldValue, object? token, ConcurrentQueue<object?> queue)
        {
            _target = new WeakReference<object>(target);
            _heldValue = heldValue;
            Token = token;
            _queue = queue;

            // Use ConditionalWeakTable to ensure our destructor-bearing helper
            // is released when target is collected
            _pokeHelper = new GCPokeHelper(heldValue, queue);
            _pokeTable.AddOrUpdate(target, _pokeHelper);
        }

        private readonly GCPokeHelper _pokeHelper;
        private static readonly ConditionalWeakTable<object, GCPokeHelper> _pokeTable = new();

        public void Suppress()
        {
            _pokeHelper.Suppress();
            if (_target.TryGetTarget(out var target))
            {
                _pokeTable.Remove(target);
            }
        }
    }

    /// <summary>
    /// Helper attached to the target via ConditionalWeakTable.
    /// When the target is GC'd, this object's finalizer enqueues the heldValue.
    /// </summary>
    private sealed class GCPokeHelper
    {
        private readonly object? _heldValue;
        private readonly ConcurrentQueue<object?> _queue;
        private volatile bool _suppressed;

        public GCPokeHelper(object? heldValue, ConcurrentQueue<object?> queue)
        {
            _heldValue = heldValue;
            _queue = queue;
        }

        public void Suppress() => _suppressed = true;

        ~GCPokeHelper()
        {
            if (!_suppressed)
            {
                _queue.Enqueue(_heldValue);
            }
        }
    }
}
