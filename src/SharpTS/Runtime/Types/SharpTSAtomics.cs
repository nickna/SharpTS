using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents JavaScript's Atomics object for atomic operations on SharedArrayBuffer.
/// </summary>
/// <remarks>
/// Provides atomic operations that guarantee sequential consistency across threads.
/// Operations are only valid on Int32Array or BigInt64Array views backed by SharedArrayBuffer.
/// The wait/notify operations implement the futex-like semantics from the ECMAScript spec.
/// </remarks>
public static class SharpTSAtomics
{
    /// <summary>
    /// Tracks waiters for Atomics.wait/notify. Key is (BufferId, ByteOffset).
    /// </summary>
    private static readonly ConcurrentDictionary<(Guid BufferId, int ByteOffset), WaiterList> _waiters = new();

    /// <summary>
    /// Helper class to manage waiters at a specific memory location.
    /// </summary>
    private class WaiterList
    {
        private readonly object _lock = new();
        private readonly List<WaiterEntry> _entries = new();

        public void Add(WaiterEntry entry)
        {
            lock (_lock) _entries.Add(entry);
        }

        public void Remove(WaiterEntry entry)
        {
            lock (_lock) _entries.Remove(entry);
        }

        public int NotifyCount(int count)
        {
            lock (_lock)
            {
                int notified = 0;
                for (int i = 0; i < _entries.Count && (count == int.MaxValue || notified < count); i++)
                {
                    var entry = _entries[i];
                    lock (entry.Lock)
                    {
                        entry.Notified = true;
                        Monitor.Pulse(entry.Lock);
                    }
                    notified++;
                }
                return notified;
            }
        }
    }

    private class WaiterEntry
    {
        public readonly object Lock = new();
        public bool Notified;

        /// <summary>
        /// Set by a worker's CancellationToken when <c>worker.terminate()</c> fires, so a
        /// parked <c>Monitor.Wait</c> wakes and unwinds the worker thread instead of leaking.
        /// </summary>
        public bool Cancelled;
    }

    #region Validation

    private static void ValidateSharedInt32Array(SharpTSTypedArray typedArray, string methodName)
    {
        if (typedArray is not SharpTSInt32Array)
            throw new Exception($"TypeError: Atomics.{methodName} requires an Int32Array");
        if (!typedArray.IsShared)
            throw new Exception($"TypeError: Atomics.{methodName} requires a SharedArrayBuffer-backed array");
    }

    private static void ValidateSharedBigInt64Array(SharpTSTypedArray typedArray, string methodName)
    {
        if (typedArray is not SharpTSBigInt64Array)
            throw new Exception($"TypeError: Atomics.{methodName} requires a BigInt64Array");
        if (!typedArray.IsShared)
            throw new Exception($"TypeError: Atomics.{methodName} requires a SharedArrayBuffer-backed array");
    }

    private static void ValidateIntegerTypedArray(SharpTSTypedArray typedArray, string methodName)
    {
        if (typedArray is SharpTSFloat32Array or SharpTSFloat64Array)
            throw new Exception($"TypeError: Atomics.{methodName} requires an integer TypedArray");
        if (!typedArray.IsShared)
            throw new Exception($"TypeError: Atomics.{methodName} requires a SharedArrayBuffer-backed array");
    }

    private static void ValidateIndex(SharpTSTypedArray typedArray, int index)
    {
        if (index < 0 || index >= typedArray.Length)
            throw new Exception("RangeError: Invalid index for Atomics operation");
    }

    #endregion

    #region Load/Store

    /// <summary>
    /// Atomically loads a value from the specified position.
    /// </summary>
    public static object? Load(SharpTSTypedArray typedArray, int index)
    {
        ValidateIntegerTypedArray(typedArray, "load");
        ValidateIndex(typedArray, index);
        return typedArray.GetVolatile(index);
    }

    /// <summary>
    /// Atomically stores a value at the specified position and returns that value.
    /// </summary>
    public static object? Store(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "store");
        ValidateIndex(typedArray, index);
        typedArray.SetVolatile(index, value);
        return value;
    }

    #endregion

    #region Arithmetic Operations

    /// <summary>
    /// Atomically adds a value and returns the old value.
    /// </summary>
    public static object? Add(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "add");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => AddInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => AddBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => AddGeneric(typedArray, index, value)
        };
    }

    private static double AddInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.Add(ref slot, value) - value; // Return old value
    }

    private static BigInteger AddBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        long oldValue = Interlocked.Add(ref slot, value) - value;
        return new BigInteger(oldValue);
    }

    private static object? AddGeneric(SharpTSTypedArray array, int index, object? value)
    {
        // For other types, use compare-exchange loop
        while (true)
        {
            var oldValue = array.GetVolatile(index);
            var newValue = AddValues(oldValue, value, array);
            var current = array.GetVolatile(index);
            if (Equals(current, oldValue))
            {
                array.SetVolatile(index, newValue);
                return oldValue;
            }
        }
    }

    private static object? AddValues(object? a, object? b, SharpTSTypedArray array)
    {
        return array switch
        {
            SharpTSInt8Array => (double)((sbyte)Convert.ToDouble(a) + (sbyte)Convert.ToDouble(b)),
            SharpTSUint8Array or SharpTSUint8ClampedArray => (double)((byte)Convert.ToDouble(a) + (byte)Convert.ToDouble(b)),
            SharpTSInt16Array => (double)((short)Convert.ToDouble(a) + (short)Convert.ToDouble(b)),
            SharpTSUint16Array => (double)((ushort)Convert.ToDouble(a) + (ushort)Convert.ToDouble(b)),
            SharpTSUint32Array => (double)((uint)Convert.ToDouble(a) + (uint)Convert.ToDouble(b)),
            _ => throw new Exception("Unsupported typed array for Atomics.add")
        };
    }

    /// <summary>
    /// Atomically subtracts a value and returns the old value.
    /// </summary>
    public static object? Sub(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "sub");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => SubInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => SubBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => SubGeneric(typedArray, index, value)
        };
    }

    private static double SubInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.Add(ref slot, -value) + value; // Return old value
    }

    private static BigInteger SubBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        long oldValue = Interlocked.Add(ref slot, -value) + value;
        return new BigInteger(oldValue);
    }

    private static object? SubGeneric(SharpTSTypedArray array, int index, object? value)
    {
        while (true)
        {
            var oldValue = array.GetVolatile(index);
            var newValue = SubValues(oldValue, value, array);
            var current = array.GetVolatile(index);
            if (Equals(current, oldValue))
            {
                array.SetVolatile(index, newValue);
                return oldValue;
            }
        }
    }

    private static object? SubValues(object? a, object? b, SharpTSTypedArray array)
    {
        return array switch
        {
            SharpTSInt8Array => (double)((sbyte)Convert.ToDouble(a) - (sbyte)Convert.ToDouble(b)),
            SharpTSUint8Array or SharpTSUint8ClampedArray => (double)((byte)Convert.ToDouble(a) - (byte)Convert.ToDouble(b)),
            SharpTSInt16Array => (double)((short)Convert.ToDouble(a) - (short)Convert.ToDouble(b)),
            SharpTSUint16Array => (double)((ushort)Convert.ToDouble(a) - (ushort)Convert.ToDouble(b)),
            SharpTSUint32Array => (double)((uint)Convert.ToDouble(a) - (uint)Convert.ToDouble(b)),
            _ => throw new Exception("Unsupported typed array for Atomics.sub")
        };
    }

    #endregion

    #region Bitwise Operations

    /// <summary>
    /// Atomically performs a bitwise AND and returns the old value.
    /// </summary>
    public static object? And(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "and");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => AndInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => AndBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => AndGeneric(typedArray, index, value)
        };
    }

    private static double AndInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.And(ref slot, value);
    }

    private static BigInteger AndBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        return new BigInteger(Interlocked.And(ref slot, value));
    }

    private static object? AndGeneric(SharpTSTypedArray array, int index, object? value)
    {
        while (true)
        {
            var oldValue = array.GetVolatile(index);
            var newValue = AndValues(oldValue, value, array);
            var current = array.GetVolatile(index);
            if (Equals(current, oldValue))
            {
                array.SetVolatile(index, newValue);
                return oldValue;
            }
        }
    }

    private static object? AndValues(object? a, object? b, SharpTSTypedArray array)
    {
        int ia = (int)Convert.ToDouble(a);
        int ib = (int)Convert.ToDouble(b);
        int result = ia & ib;
        return (double)result;
    }

    /// <summary>
    /// Atomically performs a bitwise OR and returns the old value.
    /// </summary>
    public static object? Or(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "or");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => OrInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => OrBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => OrGeneric(typedArray, index, value)
        };
    }

    private static double OrInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.Or(ref slot, value);
    }

    private static BigInteger OrBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        return new BigInteger(Interlocked.Or(ref slot, value));
    }

    private static object? OrGeneric(SharpTSTypedArray array, int index, object? value)
    {
        while (true)
        {
            var oldValue = array.GetVolatile(index);
            var newValue = OrValues(oldValue, value, array);
            var current = array.GetVolatile(index);
            if (Equals(current, oldValue))
            {
                array.SetVolatile(index, newValue);
                return oldValue;
            }
        }
    }

    private static object? OrValues(object? a, object? b, SharpTSTypedArray array)
    {
        int ia = (int)Convert.ToDouble(a);
        int ib = (int)Convert.ToDouble(b);
        int result = ia | ib;
        return (double)result;
    }

    /// <summary>
    /// Atomically performs a bitwise XOR and returns the old value.
    /// </summary>
    public static object? Xor(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "xor");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => XorInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => XorBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => XorGeneric(typedArray, index, value)
        };
    }

    private static double XorInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        int oldValue;
        int newValue;
        do
        {
            oldValue = Volatile.Read(ref slot);
            newValue = oldValue ^ value;
        } while (Interlocked.CompareExchange(ref slot, newValue, oldValue) != oldValue);
        return oldValue;
    }

    private static BigInteger XorBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        long oldValue;
        long newValue;
        do
        {
            oldValue = Volatile.Read(ref slot);
            newValue = oldValue ^ value;
        } while (Interlocked.CompareExchange(ref slot, newValue, oldValue) != oldValue);
        return new BigInteger(oldValue);
    }

    private static object? XorGeneric(SharpTSTypedArray array, int index, object? value)
    {
        while (true)
        {
            var oldValue = array.GetVolatile(index);
            var newValue = XorValues(oldValue, value, array);
            var current = array.GetVolatile(index);
            if (Equals(current, oldValue))
            {
                array.SetVolatile(index, newValue);
                return oldValue;
            }
        }
    }

    private static object? XorValues(object? a, object? b, SharpTSTypedArray array)
    {
        int ia = (int)Convert.ToDouble(a);
        int ib = (int)Convert.ToDouble(b);
        int result = ia ^ ib;
        return (double)result;
    }

    #endregion

    #region Exchange Operations

    /// <summary>
    /// Atomically exchanges a value and returns the old value.
    /// </summary>
    public static object? Exchange(SharpTSTypedArray typedArray, int index, object? value)
    {
        ValidateIntegerTypedArray(typedArray, "exchange");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => ExchangeInt32(int32Array, index, (int)Convert.ToDouble(value)),
            SharpTSBigInt64Array bigInt64Array => ExchangeBigInt64(bigInt64Array, index, ConvertToBigInt64(value)),
            _ => ExchangeGeneric(typedArray, index, value)
        };
    }

    private static double ExchangeInt32(SharpTSInt32Array array, int index, int value)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.Exchange(ref slot, value);
    }

    private static BigInteger ExchangeBigInt64(SharpTSBigInt64Array array, int index, long value)
    {
        ref long slot = ref array.GetRef(index);
        return new BigInteger(Interlocked.Exchange(ref slot, value));
    }

    private static object? ExchangeGeneric(SharpTSTypedArray array, int index, object? value)
    {
        var oldValue = array.GetVolatile(index);
        array.SetVolatile(index, value);
        return oldValue;
    }

    /// <summary>
    /// Atomically compares and exchanges a value. Returns the old value.
    /// </summary>
    public static object? CompareExchange(SharpTSTypedArray typedArray, int index, object? expectedValue, object? replacementValue)
    {
        ValidateIntegerTypedArray(typedArray, "compareExchange");
        ValidateIndex(typedArray, index);

        return typedArray switch
        {
            SharpTSInt32Array int32Array => CompareExchangeInt32(int32Array, index,
                (int)Convert.ToDouble(expectedValue), (int)Convert.ToDouble(replacementValue)),
            SharpTSBigInt64Array bigInt64Array => CompareExchangeBigInt64(bigInt64Array, index,
                ConvertToBigInt64(expectedValue), ConvertToBigInt64(replacementValue)),
            _ => CompareExchangeGeneric(typedArray, index, expectedValue, replacementValue)
        };
    }

    private static double CompareExchangeInt32(SharpTSInt32Array array, int index, int expected, int replacement)
    {
        ref int slot = ref array.GetRef(index);
        return Interlocked.CompareExchange(ref slot, replacement, expected);
    }

    private static BigInteger CompareExchangeBigInt64(SharpTSBigInt64Array array, int index, long expected, long replacement)
    {
        ref long slot = ref array.GetRef(index);
        return new BigInteger(Interlocked.CompareExchange(ref slot, replacement, expected));
    }

    private static object? CompareExchangeGeneric(SharpTSTypedArray array, int index, object? expected, object? replacement)
    {
        var current = array.GetVolatile(index);
        if (Equals(current, expected))
        {
            array.SetVolatile(index, replacement);
        }
        return current;
    }

    #endregion

    #region Wait/Notify

    /// <summary>
    /// Waits until the value at the given position changes from the expected value.
    /// Returns "ok" if notified, "timed-out" if timeout expired, "not-equal" if value doesn't match.
    /// </summary>
    public static string Wait(SharpTSTypedArray typedArray, int index, object? expectedValue, double? timeout = null, CancellationToken cancellationToken = default)
    {
        if (typedArray is SharpTSInt32Array int32Array)
        {
            ValidateSharedInt32Array(typedArray, "wait");
            return WaitInt32(int32Array, index, (int)Convert.ToDouble(expectedValue), timeout, cancellationToken);
        }
        else if (typedArray is SharpTSBigInt64Array bigInt64Array)
        {
            ValidateSharedBigInt64Array(typedArray, "wait");
            return WaitBigInt64(bigInt64Array, index, ConvertToBigInt64(expectedValue), timeout, cancellationToken);
        }

        throw new Exception("TypeError: Atomics.wait requires an Int32Array or BigInt64Array");
    }

    /// <summary>
    /// Registers a cancellation hook that wakes <paramref name="entry"/> when the worker's
    /// token fires (<c>worker.terminate()</c>). Returns a registration the caller disposes.
    /// Safe against the already-cancelled case: the callback runs synchronously here, before
    /// the caller takes <c>entry.Lock</c> / parks, and the caller re-checks <c>Cancelled</c>.
    /// </summary>
    private static CancellationTokenRegistration RegisterWaitCancellation(WaiterEntry entry, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return default;

        return cancellationToken.Register(static state =>
        {
            var e = (WaiterEntry)state!;
            // Monitor.Wait releases e.Lock while parked, so this acquires it, flags the
            // cancellation, and pulses the parked worker awake.
            lock (e.Lock)
            {
                e.Cancelled = true;
                Monitor.Pulse(e.Lock);
            }
        }, entry);
    }

    private static string WaitInt32(SharpTSInt32Array array, int index, int expectedValue, double? timeout, CancellationToken cancellationToken)
    {
        ValidateIndex(array, index);

        // Check if value matches
        ref int slot = ref array.GetRef(index);
        if (Volatile.Read(ref slot) != expectedValue)
            return "not-equal";

        var bufferId = array.SharedBuffer!.BufferId;
        var byteOffset = array.ByteOffset + index * 4;
        var key = (bufferId, byteOffset);

        var waiterList = _waiters.GetOrAdd(key, _ => new WaiterList());
        var entry = new WaiterEntry();
        waiterList.Add(entry);

        var registration = RegisterWaitCancellation(entry, cancellationToken);
        try
        {
            lock (entry.Lock)
            {
                // Double-check value
                if (Volatile.Read(ref slot) != expectedValue)
                    return "not-equal";

                // terminate() may have already fired before we registered / parked.
                if (entry.Cancelled || cancellationToken.IsCancellationRequested)
                    throw new WorkerTerminatedException();

                int timeoutMs = timeout.HasValue && timeout.Value >= 0
                    ? (int)Math.Min(timeout.Value, int.MaxValue)
                    : Timeout.Infinite;

                if (timeoutMs == 0)
                    return "timed-out";

                Monitor.Wait(entry.Lock, timeoutMs);

                if (entry.Cancelled)
                    throw new WorkerTerminatedException();

                return entry.Notified ? "ok" : "timed-out";
            }
        }
        finally
        {
            registration.Dispose();
            waiterList.Remove(entry);
        }
    }

    private static string WaitBigInt64(SharpTSBigInt64Array array, int index, long expectedValue, double? timeout, CancellationToken cancellationToken)
    {
        ValidateIndex(array, index);

        ref long slot = ref array.GetRef(index);
        if (Volatile.Read(ref slot) != expectedValue)
            return "not-equal";

        var bufferId = array.SharedBuffer!.BufferId;
        var byteOffset = array.ByteOffset + index * 8;
        var key = (bufferId, byteOffset);

        var waiterList = _waiters.GetOrAdd(key, _ => new WaiterList());
        var entry = new WaiterEntry();
        waiterList.Add(entry);

        var registration = RegisterWaitCancellation(entry, cancellationToken);
        try
        {
            lock (entry.Lock)
            {
                if (Volatile.Read(ref slot) != expectedValue)
                    return "not-equal";

                if (entry.Cancelled || cancellationToken.IsCancellationRequested)
                    throw new WorkerTerminatedException();

                int timeoutMs = timeout.HasValue && timeout.Value >= 0
                    ? (int)Math.Min(timeout.Value, int.MaxValue)
                    : Timeout.Infinite;

                if (timeoutMs == 0)
                    return "timed-out";

                Monitor.Wait(entry.Lock, timeoutMs);

                if (entry.Cancelled)
                    throw new WorkerTerminatedException();

                return entry.Notified ? "ok" : "timed-out";
            }
        }
        finally
        {
            registration.Dispose();
            waiterList.Remove(entry);
        }
    }

    /// <summary>
    /// Notifies waiters at the specified position. Returns the number of waiters notified.
    /// </summary>
    public static double Notify(SharpTSTypedArray typedArray, int index, int? count = null)
    {
        if (typedArray is not SharpTSInt32Array and not SharpTSBigInt64Array)
            throw new Exception("TypeError: Atomics.notify requires an Int32Array or BigInt64Array");

        if (!typedArray.IsShared)
            return 0; // Non-shared buffers have no waiters

        ValidateIndex(typedArray, index);

        var bufferId = typedArray.SharedBuffer!.BufferId;
        var byteOffset = typedArray.ByteOffset + index * typedArray.BytesPerElement;
        var key = (bufferId, byteOffset);

        if (!_waiters.TryGetValue(key, out var waiterList))
            return 0;

        int notifyCount = count ?? int.MaxValue;
        return waiterList.NotifyCount(notifyCount);
    }

    #endregion

    #region Utility

    /// <summary>
    /// Returns true if Atomics operations will use lock-free implementations for the given typed array.
    /// </summary>
    public static bool IsLockFree(int size)
    {
        // In .NET, Interlocked operations are lock-free for 1, 2, 4, and 8 byte values
        return size is 1 or 2 or 4 or 8;
    }

    private static long ConvertToBigInt64(object? value)
    {
        return value switch
        {
            BigInteger bi => (long)bi,
            double d => (long)d,
            _ => Convert.ToInt64(value)
        };
    }

    #endregion

    #region Member Access

    /// <summary>
    /// Gets a member of the Atomics object.
    /// </summary>
    public static object? GetMember(string name)
    {
        return name switch
        {
            "load" => BuiltInMethod.CreateV2("load", 2, static (_, _, args) =>
            {
                if (args.Length < 2 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.load requires a typed array and index");
                return RuntimeValue.FromBoxed(Load(arr, (int)args[1].AsNumberUnsafe()));
            }),

            "store" => BuiltInMethod.CreateV2("store", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.store requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Store(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "add" => BuiltInMethod.CreateV2("add", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.add requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Add(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "sub" => BuiltInMethod.CreateV2("sub", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.sub requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Sub(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "and" => BuiltInMethod.CreateV2("and", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.and requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(And(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "or" => BuiltInMethod.CreateV2("or", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.or requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Or(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "xor" => BuiltInMethod.CreateV2("xor", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.xor requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Xor(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "exchange" => BuiltInMethod.CreateV2("exchange", 3, static (_, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.exchange requires a typed array, index, and value");
                return RuntimeValue.FromBoxed(Exchange(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject()));
            }),

            "compareExchange" => BuiltInMethod.CreateV2("compareExchange", 4, static (_, _, args) =>
            {
                if (args.Length < 4 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.compareExchange requires a typed array, index, expected value, and replacement value");
                return RuntimeValue.FromBoxed(CompareExchange(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject(), args[3].ToObject()));
            }),

            "wait" => BuiltInMethod.CreateV2("wait", 3, 4, static (interp, _, args) =>
            {
                if (args.Length < 3 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.wait requires a typed array, index, and expected value");
                double? timeout = args.Length > 3 && args[3].IsNumber ? args[3].AsNumberUnsafe() : null;
                // A worker sets its termination token on the interpreter so terminate() can wake
                // a parked wait; on the main thread the token is non-cancelable (a no-op).
                var terminationToken = interp?.WorkerTerminationToken ?? default;
                return RuntimeValue.FromString(Wait(arr, (int)args[1].AsNumberUnsafe(), args[2].ToObject(), timeout, terminationToken));
            }),

            "notify" => BuiltInMethod.CreateV2("notify", 2, 3, static (_, _, args) =>
            {
                if (args.Length < 2 || args[0].ToObject() is not SharpTSTypedArray arr || !args[1].IsNumber)
                    throw new Exception("Atomics.notify requires a typed array and index");
                int? count = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : null;
                return RuntimeValue.FromNumber(Notify(arr, (int)args[1].AsNumberUnsafe(), count));
            }),

            "isLockFree" => BuiltInMethod.CreateV2("isLockFree", 1, static (_, _, args) =>
            {
                if (args.Length < 1 || !args[0].IsNumber)
                    throw new Exception("Atomics.isLockFree requires a size argument");
                return RuntimeValue.FromBoolean(IsLockFree((int)args[0].AsNumberUnsafe()));
            }),

            _ => null
        };
    }

    #endregion
}
