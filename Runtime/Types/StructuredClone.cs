using System.Numerics;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Implements the structured clone algorithm for serializing values across threads.
/// </summary>
/// <remarks>
/// The structured clone algorithm creates deep copies of values that can be safely
/// transferred between threads. Certain types are passed by reference (SharedArrayBuffer)
/// while others are cloned, and some types cannot be cloned at all (functions, closures).
///
/// This implementation follows the HTML Living Standard's structured clone algorithm
/// with adaptations for SharpTS's type system.
/// </remarks>
public static class StructuredClone
{
    /// <summary>
    /// Exception thrown when a value cannot be cloned.
    /// </summary>
    public class DataCloneError : Exception
    {
        public DataCloneError(string message) : base($"DataCloneError: {message}") { }
    }

    // Objects marked via worker_threads.markAsUntransferable. When such an object appears in a
    // postMessage transfer list it is IGNORED (cloned in the payload instead of transferred),
    // matching Node (#1002). Reference-keyed and weak so marking does not pin the object.
    private static readonly ConditionalWeakTable<object, object> _untransferable = new();

    /// <summary>Marks <paramref name="value"/> so it is never transferred (only cloned).</summary>
    public static void MarkUntransferable(object? value)
    {
        if (value != null)
            _untransferable.AddOrUpdate(value, value);
    }

    /// <summary>Whether <paramref name="value"/> was marked via <see cref="MarkUntransferable"/>.</summary>
    public static bool IsUntransferable(object value) => _untransferable.TryGetValue(value, out _);

    /// <summary>
    /// Clones a value using the structured clone algorithm.
    /// </summary>
    /// <param name="value">The value to clone.</param>
    /// <param name="transfer">
    /// Optional list of transferable objects. Accepts both the interpreter
    /// <see cref="SharpTSArray"/> and a compiled <c>List&lt;object?&gt;</c> (both are
    /// <see cref="IEnumerable{T}"/> of <c>object?</c>), so a transfer list survives
    /// from either runtime (#406).
    /// </param>
    /// <returns>A deep clone of the value.</returns>
    public static object? Clone(object? value, IEnumerable<object?>? transfer = null)
    {
        var cloned = new Dictionary<object, object>();
        var transferred = new HashSet<object>();
        // ArrayBuffers in the transfer list are detached on this (sender) side after the
        // clone — Node neuters a transferred ArrayBuffer (byteLength becomes 0). Collected
        // separately because detach must happen AFTER the payload is copied.
        List<object>? transferredBuffers = null;

        // Process transfer list. Transferables are MessagePort (interpreter
        // SharpTSMessagePort or the emitted compiled $MessagePort, #406) and ArrayBuffer
        // (interpreter SharpTSArrayBuffer or the emitted compiled $ArrayBuffer, #999).
        if (transfer != null)
        {
            foreach (var item in transfer)
            {
                // markAsUntransferable: a marked object in the transfer list is ignored — it is
                // cloned in the payload instead of transferred (Node semantics, #1002).
                if (item != null && IsUntransferable(item))
                {
                    continue;
                }

                if (item is SharpTSMessagePort || CompiledMessagePortBridge.IsEmittedMessagePort(item))
                {
                    transferred.Add(item!);
                }
                else if (item is SharpTSArrayBuffer || item?.GetType().Name == "$ArrayBuffer")
                {
                    transferred.Add(item!);
                    (transferredBuffers ??= new List<object>()).Add(item!);
                }
                else if (item != null)
                {
                    throw new DataCloneError("Only ArrayBuffer and MessagePort objects can be transferred");
                }
            }
        }

        var result = CloneInternal(value, cloned, transferred);

        // Neuter the source ArrayBuffers now that their contents have been copied to the
        // clone. A buffer in the transfer list is detached whether or not it appeared in
        // the payload (Node semantics).
        if (transferredBuffers != null)
        {
            foreach (var buffer in transferredBuffers)
                DetachArrayBuffer(buffer);
        }

        return result;
    }

    /// <summary>
    /// Detaches a transferred ArrayBuffer on the sender side: the interpreter
    /// <see cref="SharpTSArrayBuffer"/> directly, or the emitted compiled <c>$ArrayBuffer</c>
    /// via its <c>Detach()</c> method (reflection keeps this assembly standalone-agnostic).
    /// </summary>
    private static void DetachArrayBuffer(object buffer)
    {
        if (buffer is SharpTSArrayBuffer interpBuffer)
        {
            interpBuffer.Detach();
        }
        else if (buffer.GetType().Name == "$ArrayBuffer")
        {
            buffer.GetType().GetMethod("Detach")?.Invoke(buffer, null);
        }
    }

    private static object? CloneInternal(object? value, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        // Primitives - return as-is
        if (value == null)
            return null;

        if (value is double or string or bool or BigInteger)
            return value;

        // Check for circular reference
        if (cloned.TryGetValue(value, out var existing))
            return existing;

        // Handle different types
        return value switch
        {
            // SharedArrayBuffer - pass by reference (this is the key sharing mechanism!)
            SharpTSSharedArrayBuffer sab => sab,

            // ArrayBuffer (non-shared) - copy the bytes into an independent buffer. When the
            // source is in the transfer list it is detached afterwards by Clone() (#999); the
            // receiver always gets its own copy (no cross-thread aliasing of a mutable buffer).
            SharpTSArrayBuffer arrayBuffer => CloneArrayBuffer(arrayBuffer, cloned),

            // TypedArray - recreate view, share backing if SharedArrayBuffer
            SharpTSTypedArray typedArray => CloneTypedArray(typedArray, cloned, transferred),

            // MessagePort - can be transferred but not cloned
            SharpTSMessagePort port when transferred.Contains(port) => TransferMessagePort(port),
            SharpTSMessagePort => throw new DataCloneError("MessagePort cannot be cloned, only transferred"),

            // Array - deep clone elements (interpreter SharpTSArray)
            SharpTSArray array => CloneArray(array, cloned, transferred),

            // Compiled arrays - List<object?> used in compiled code
            List<object?> list => CloneList(list, cloned, transferred),

            // Object - deep clone properties (interpreter SharpTSObject)
            SharpTSObject obj => CloneObject(obj, cloned, transferred),

            // Compiled objects - Dictionary<string, object?> used in compiled code
            Dictionary<string, object?> dict => CloneDictionary(dict, cloned, transferred),

            // Date - clone value (epoch milliseconds)
            SharpTSDate date => new SharpTSDate(date.GetTime()),

            // RegExp - clone pattern and flags
            SharpTSRegExp regexp => new SharpTSRegExp(regexp.Source, regexp.Flags),

            // Error - clone message and name (both legacy SharpTSError and SharpTSInstance from SharpTSErrorClass)
            SharpTSError error => CloneError(error),
            SharpTSInstance inst when inst.GetClass() is SharpTSErrorClass => CloneErrorInstance(inst),

            // Map - deep clone entries (interpreter SharpTSMap)
            SharpTSMap map => CloneMap(map, cloned, transferred),

            // Compiled Maps - Dictionary<object, object?> used in compiled code
            // Must check type argument to distinguish from string dictionary
            IDictionary<object, object?> objDict when value.GetType().IsGenericType &&
                value.GetType().GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                value.GetType().GetGenericArguments()[0] == typeof(object) =>
                CloneObjectDictionary(objDict, cloned, transferred),

            // Set - deep clone values (interpreter SharpTSSet)
            SharpTSSet set => CloneSet(set, cloned, transferred),

            // Compiled Sets - HashSet<object?> used in compiled code
            HashSet<object?> hashSet => CloneHashSet(hashSet, cloned, transferred),

            // Buffer (ArrayBuffer equivalent) - copy bytes
            SharpTSBuffer buffer => CloneBuffer(buffer),

            // Promise - cannot be cloned
            SharpTSPromise => throw new DataCloneError("Promise cannot be cloned"),

            // Symbol - cannot be cloned (unique identity)
            SharpTSSymbol => throw new DataCloneError("Symbol cannot be cloned"),

            // Function / Closure - cannot be cloned
            SharpTSFunction => throw new DataCloneError("Function cannot be cloned"),

            // Class constructor - cannot be cloned (must come before ISharpTSCallable)
            SharpTSClass => throw new DataCloneError("Class constructors cannot be cloned"),

            // Other callables - cannot be cloned
            ISharpTSCallable => throw new DataCloneError("Function cannot be cloned"),

            // Class instance - cannot be cloned (has methods)
            SharpTSInstance => throw new DataCloneError("Class instances cannot be cloned"),

            // WeakMap/WeakSet - cannot be cloned (weak references)
            SharpTSWeakMap => throw new DataCloneError("WeakMap cannot be cloned"),
            SharpTSWeakSet => throw new DataCloneError("WeakSet cannot be cloned"),

            // Iterators/Generators - cannot be cloned
            SharpTSIterator => throw new DataCloneError("Iterator cannot be cloned"),
            SharpTSGenerator => throw new DataCloneError("Generator cannot be cloned"),

            // Worker - cannot be cloned (must come before SharpTSEventEmitter)
            SharpTSWorker => throw new DataCloneError("Worker cannot be cloned"),

            // EventEmitter - cannot be cloned (has listeners)
            SharpTSEventEmitter => throw new DataCloneError("EventEmitter cannot be cloned"),

            // Emitted $MessagePort from compiled code - transferred (never cloned).
            // When in the transfer set, adopt it into a worker-usable bridge so the
            // worker's interpreter can drive the compiled port; otherwise reject,
            // matching the interpreter SharpTSMessagePort arms above (#406).
            _ when CompiledMessagePortBridge.IsEmittedMessagePort(value) && transferred.Contains(value)
                => CompiledMessagePortBridge.Adopt(value),
            _ when CompiledMessagePortBridge.IsEmittedMessagePort(value)
                => throw new DataCloneError("MessagePort cannot be cloned, only transferred"),

            // Emitted $SharedArrayBuffer type from compiled code - pass by reference
            // Check by type name since we can't reference the dynamically emitted type
            _ when value.GetType().Name == "$SharedArrayBuffer" => value,

            // Emitted $ArrayBuffer type from compiled code - clone the bytes
            _ when value.GetType().Name == "$ArrayBuffer" => CloneEmittedArrayBuffer(value),

            // Unknown type
            _ => throw new DataCloneError($"Cannot clone value of type {value.GetType().Name}")
        };
    }

    private static SharpTSTypedArray CloneTypedArray(SharpTSTypedArray source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        SharpTSTypedArray result;

        if (source.IsShared)
        {
            // For SharedArrayBuffer-backed views, create new view over same buffer.
            // source's own CreateViewFrom picks the concrete element type — the subclass set is
            // the single source of the typeName→constructor mapping (a new element type can no
            // longer silently miss the clone path).
            var sharedBuffer = source.SharedBuffer!;
            result = source.CreateViewFrom(sharedBuffer, source.ByteOffset, source.Length);
        }
        else
        {
            // For regular buffer, copy the data
            var newBuffer = new byte[source.ByteLength];
            Array.Copy(source.Buffer, source.ByteOffset, newBuffer, 0, source.ByteLength);
            result = source.CreateViewFrom(newBuffer, 0, source.Length);
        }

        cloned[source] = result;
        return result;
    }

    private static SharpTSMessagePort TransferMessagePort(SharpTSMessagePort port)
    {
        // A worker transfer hands the SAME port object to the worker's interpreter,
        // which runs on another thread in this process. Neutering it (the browser
        // semantics, where transfer detaches the sender's handle) would make the
        // shared object unusable on the RECEIVING side too, since both sides see
        // one object. Instead mark it and its partner cross-thread: each port then
        // marshals delivery onto its own owner-loop thread and a started port keeps
        // that loop alive (#406).
        //
        // #1002 DECISION: SharpTS does NOT neuter the source MessagePort on transfer.
        // This is an intentional, documented deviation from Node — SharpTS's single-process
        // two-thread model shares one port object across both threads (unlike V8's separate
        // serialize/deserialize), so neutering the sender would neuter the receiver too. The
        // markAsUntransferable + ArrayBuffer-detach paths (#999/#1002) are spec-faithful;
        // only this port-neuter case is the model-dictated exception.
        port.MarkTransferredAcrossThreads();
        return port;
    }

    private static SharpTSArray CloneArray(SharpTSArray source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new SharpTSArray();
        cloned[source] = result;

        foreach (var element in source)
        {
            var clonedElement = CloneInternal(element, cloned, transferred);
            result.Add(clonedElement);
        }

        return result;
    }

    /// <summary>
    /// Clones a List (compiled code array representation).
    /// </summary>
    private static List<object?> CloneList(List<object?> source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new List<object?>(source.Count);
        cloned[source] = result;

        foreach (var element in source)
        {
            var clonedElement = CloneInternal(element, cloned, transferred);
            result.Add(clonedElement);
        }

        return result;
    }

    private static SharpTSObject CloneObject(SharpTSObject source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var fields = new Dictionary<string, object?>();
        var result = new SharpTSObject(fields);
        cloned[source] = result;

        foreach (var (key, value) in source.Fields)
        {
            // Check if value is a function (getters/setters would be caught here)
            if (value is ISharpTSCallable or SharpTSFunction)
            {
                throw new DataCloneError($"Cannot clone object with function property '{key}'");
            }

            fields[key] = CloneInternal(value, cloned, transferred);
        }

        return result;
    }

    /// <summary>
    /// Clones a Dictionary&lt;string, object?&gt; (compiled code object representation).
    /// </summary>
    private static Dictionary<string, object?> CloneDictionary(Dictionary<string, object?> source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new Dictionary<string, object?>(source.Count);
        cloned[source] = result;

        foreach (var (key, value) in source)
        {
            // Check if value is a function
            if (value is ISharpTSCallable or SharpTSFunction)
            {
                throw new DataCloneError($"Cannot clone object with function property '{key}'");
            }

            result[key] = CloneInternal(value, cloned, transferred);
        }

        return result;
    }

    /// <summary>
    /// Clones a Dictionary&lt;object, object?&gt; (compiled code Map representation).
    /// </summary>
    private static Dictionary<object, object?> CloneObjectDictionary(IDictionary<object, object?> source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new Dictionary<object, object?>(source.Count);
        cloned[(object)source] = result;

        foreach (var kvp in source)
        {
            var clonedKey = CloneInternal(kvp.Key, cloned, transferred);
            var clonedValue = CloneInternal(kvp.Value, cloned, transferred);
            if (clonedKey != null)
            {
                result[clonedKey] = clonedValue;
            }
        }

        return result;
    }

    /// <summary>
    /// Clones a HashSet&lt;object?&gt; (compiled code Set representation).
    /// </summary>
    private static HashSet<object?> CloneHashSet(HashSet<object?> source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new HashSet<object?>();
        cloned[source] = result;

        foreach (var value in source)
        {
            var clonedValue = CloneInternal(value, cloned, transferred);
            result.Add(clonedValue);
        }

        return result;
    }

    private static SharpTSError CloneError(SharpTSError error)
    {
        // Create new error based on error name
        SharpTSError cloned = error.Name switch
        {
            "TypeError" => new SharpTSTypeError(error.Message),
            "RangeError" => new SharpTSRangeError(error.Message),
            "ReferenceError" => new SharpTSReferenceError(error.Message),
            "SyntaxError" => new SharpTSSyntaxError(error.Message),
            "URIError" => new SharpTSURIError(error.Message),
            "EvalError" => new SharpTSEvalError(error.Message),
            _ => new SharpTSError(error.Message)
        };
        // Preserve the original stack trace
        cloned.Stack = error.Stack;
        return cloned;
    }

    private static SharpTSInstance CloneErrorInstance(SharpTSInstance source)
    {
        var klass = (SharpTSErrorClass)source.GetClass();
        var clone = new SharpTSInstance(klass);
        // Copy all error fields
        foreach (var fieldName in source.GetFieldNames())
            clone.SetRawField(fieldName, source.GetRawField(fieldName));
        return clone;
    }

    private static SharpTSMap CloneMap(SharpTSMap source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new SharpTSMap();
        cloned[source] = result;

        foreach (var kvp in source.InternalEntries)
        {
            var clonedKey = CloneInternal(kvp.Key, cloned, transferred);
            var clonedValue = CloneInternal(kvp.Value, cloned, transferred);
            // A null key is valid in a JS Map (and SharpTSMap.Set normalizes it), so set
            // unconditionally — guarding on non-null would silently drop a null key.
            result.Set(clonedKey, clonedValue);
        }

        return result;
    }

    private static SharpTSSet CloneSet(SharpTSSet source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new SharpTSSet();
        cloned[source] = result;

        foreach (var value in source.InternalValues)
        {
            var clonedValue = CloneInternal(value, cloned, transferred);
            if (clonedValue != null)
            {
                result.Add(clonedValue);
            }
        }

        return result;
    }

    private static SharpTSBuffer CloneBuffer(SharpTSBuffer source)
    {
        var newData = new byte[source.Length];
        Array.Copy(source.Data, newData, source.Length);
        return new SharpTSBuffer(newData);
    }

    private static SharpTSArrayBuffer CloneArrayBuffer(SharpTSArrayBuffer source, Dictionary<object, object> cloned)
    {
        var result = new SharpTSArrayBuffer(source.ByteLength);
        source.AsSpan().CopyTo(result.AsSpan());
        cloned[source] = result;
        return result;
    }

    /// <summary>
    /// Clones an emitted $ArrayBuffer type from compiled code.
    /// Uses reflection to access the buffer and create a new instance.
    /// </summary>
    private static object CloneEmittedArrayBuffer(object source)
    {
        var type = source.GetType();

        // Get ByteLength property
        var byteLengthProp = type.GetProperty("ByteLength");
        var byteLength = (int)(byteLengthProp?.GetValue(source) ?? 0);

        // Get the backing buffer via GetBuffer() method
        var getBufferMethod = type.GetMethod("GetBuffer");
        var sourceBuffer = (byte[]?)getBufferMethod?.Invoke(source, null);

        if (sourceBuffer == null)
            throw new DataCloneError("Cannot clone ArrayBuffer: unable to access backing buffer");

        // Create new instance with same length
        var ctor = type.GetConstructor([typeof(int)]);
        if (ctor == null)
            throw new DataCloneError("Cannot clone ArrayBuffer: constructor not found");

        var result = ctor.Invoke([byteLength]);

        // Copy the data
        var resultBuffer = (byte[]?)getBufferMethod?.Invoke(result, null);
        if (resultBuffer != null && sourceBuffer != null)
        {
            Array.Copy(sourceBuffer, resultBuffer, byteLength);
        }

        return result;
    }

}
