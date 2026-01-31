using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns;

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
    public static string Wait(SharpTSTypedArray typedArray, int index, object? expectedValue, double? timeout = null)
    {
        if (typedArray is SharpTSInt32Array int32Array)
        {
            ValidateSharedInt32Array(typedArray, "wait");
            return WaitInt32(int32Array, index, (int)Convert.ToDouble(expectedValue), timeout);
        }
        else if (typedArray is SharpTSBigInt64Array bigInt64Array)
        {
            ValidateSharedBigInt64Array(typedArray, "wait");
            return WaitBigInt64(bigInt64Array, index, ConvertToBigInt64(expectedValue), timeout);
        }

        throw new Exception("TypeError: Atomics.wait requires an Int32Array or BigInt64Array");
    }

    private static string WaitInt32(SharpTSInt32Array array, int index, int expectedValue, double? timeout)
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

        try
        {
            lock (entry.Lock)
            {
                // Double-check value
                if (Volatile.Read(ref slot) != expectedValue)
                    return "not-equal";

                int timeoutMs = timeout.HasValue && timeout.Value >= 0
                    ? (int)Math.Min(timeout.Value, int.MaxValue)
                    : Timeout.Infinite;

                if (timeoutMs == 0)
                    return "timed-out";

                bool signaled = Monitor.Wait(entry.Lock, timeoutMs);
                return entry.Notified ? "ok" : "timed-out";
            }
        }
        finally
        {
            waiterList.Remove(entry);
        }
    }

    private static string WaitBigInt64(SharpTSBigInt64Array array, int index, long expectedValue, double? timeout)
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

        try
        {
            lock (entry.Lock)
            {
                if (Volatile.Read(ref slot) != expectedValue)
                    return "not-equal";

                int timeoutMs = timeout.HasValue && timeout.Value >= 0
                    ? (int)Math.Min(timeout.Value, int.MaxValue)
                    : Timeout.Infinite;

                if (timeoutMs == 0)
                    return "timed-out";

                bool signaled = Monitor.Wait(entry.Lock, timeoutMs);
                return entry.Notified ? "ok" : "timed-out";
            }
        }
        finally
        {
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
            "load" => new BuiltInMethod("load", 2, (interp, recv, args) =>
            {
                if (args.Count < 2 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.load requires a typed array and index");
                return Load(arr, (int)idx);
            }),

            "store" => new BuiltInMethod("store", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.store requires a typed array, index, and value");
                return Store(arr, (int)idx, args[2]);
            }),

            "add" => new BuiltInMethod("add", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.add requires a typed array, index, and value");
                return Add(arr, (int)idx, args[2]);
            }),

            "sub" => new BuiltInMethod("sub", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.sub requires a typed array, index, and value");
                return Sub(arr, (int)idx, args[2]);
            }),

            "and" => new BuiltInMethod("and", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.and requires a typed array, index, and value");
                return And(arr, (int)idx, args[2]);
            }),

            "or" => new BuiltInMethod("or", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.or requires a typed array, index, and value");
                return Or(arr, (int)idx, args[2]);
            }),

            "xor" => new BuiltInMethod("xor", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.xor requires a typed array, index, and value");
                return Xor(arr, (int)idx, args[2]);
            }),

            "exchange" => new BuiltInMethod("exchange", 3, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.exchange requires a typed array, index, and value");
                return Exchange(arr, (int)idx, args[2]);
            }),

            "compareExchange" => new BuiltInMethod("compareExchange", 4, (interp, recv, args) =>
            {
                if (args.Count < 4 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.compareExchange requires a typed array, index, expected value, and replacement value");
                return CompareExchange(arr, (int)idx, args[2], args[3]);
            }),

            "wait" => new BuiltInMethod("wait", 3, 4, (interp, recv, args) =>
            {
                if (args.Count < 3 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.wait requires a typed array, index, and expected value");
                double? timeout = args.Count > 3 && args[3] is double t ? t : null;
                return Wait(arr, (int)idx, args[2], timeout);
            }),

            "notify" => new BuiltInMethod("notify", 2, 3, (interp, recv, args) =>
            {
                if (args.Count < 2 || args[0] is not SharpTSTypedArray arr || args[1] is not double idx)
                    throw new Exception("Atomics.notify requires a typed array and index");
                int? count = args.Count > 2 && args[2] is double c ? (int)c : null;
                return Notify(arr, (int)idx, count);
            }),

            "isLockFree" => new BuiltInMethod("isLockFree", 1, (interp, recv, args) =>
            {
                if (args.Count < 1 || args[0] is not double size)
                    throw new Exception("Atomics.isLockFree requires a size argument");
                return IsLockFree((int)size);
            }),

            _ => null
        };
    }

    #endregion
}
