using System.Numerics;

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

    /// <summary>
    /// Clones a value using the structured clone algorithm.
    /// </summary>
    /// <param name="value">The value to clone.</param>
    /// <param name="transfer">Optional array of transferable objects.</param>
    /// <returns>A deep clone of the value.</returns>
    public static object? Clone(object? value, SharpTSArray? transfer = null)
    {
        var cloned = new Dictionary<object, object>();
        var transferred = new HashSet<object>();

        // Process transfer list
        if (transfer != null)
        {
            foreach (var item in transfer.Elements)
            {
                if (item is SharpTSMessagePort)
                {
                    transferred.Add(item);
                }
                else if (item != null)
                {
                    // Only MessagePort is transferable in our implementation
                    throw new DataCloneError("Only MessagePort objects can be transferred");
                }
            }
        }

        return CloneInternal(value, cloned, transferred);
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

            // Error - clone message and name
            SharpTSError error => CloneError(error),

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

            // Unknown type
            _ => throw new DataCloneError($"Cannot clone value of type {value.GetType().Name}")
        };
    }

    private static SharpTSTypedArray CloneTypedArray(SharpTSTypedArray source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        SharpTSTypedArray result;

        if (source.IsShared)
        {
            // For SharedArrayBuffer-backed views, create new view over same buffer
            var sharedBuffer = source.SharedBuffer!;
            result = CreateTypedArrayView(source.TypeName, sharedBuffer, source.ByteOffset, source.Length);
        }
        else
        {
            // For regular buffer, copy the data
            var newBuffer = new byte[source.ByteLength];
            Array.Copy(source.Buffer, source.ByteOffset, newBuffer, 0, source.ByteLength);
            result = CreateTypedArrayFromBuffer(source.TypeName, newBuffer, source.Length);
        }

        cloned[source] = result;
        return result;
    }

    private static SharpTSTypedArray CreateTypedArrayView(string typeName, SharpTSSharedArrayBuffer buffer, int byteOffset, int length)
    {
        return typeName switch
        {
            "Int8Array" => new SharpTSInt8Array(buffer, byteOffset, length),
            "Uint8Array" => new SharpTSUint8Array(buffer, byteOffset, length),
            "Uint8ClampedArray" => new SharpTSUint8ClampedArray(buffer, byteOffset, length),
            "Int16Array" => new SharpTSInt16Array(buffer, byteOffset, length),
            "Uint16Array" => new SharpTSUint16Array(buffer, byteOffset, length),
            "Int32Array" => new SharpTSInt32Array(buffer, byteOffset, length),
            "Uint32Array" => new SharpTSUint32Array(buffer, byteOffset, length),
            "Float32Array" => new SharpTSFloat32Array(buffer, byteOffset, length),
            "Float64Array" => new SharpTSFloat64Array(buffer, byteOffset, length),
            "BigInt64Array" => new SharpTSBigInt64Array(buffer, byteOffset, length),
            "BigUint64Array" => new SharpTSBigUint64Array(buffer, byteOffset, length),
            _ => throw new DataCloneError($"Unknown TypedArray type: {typeName}")
        };
    }

    private static SharpTSTypedArray CreateTypedArrayFromBuffer(string typeName, byte[] buffer, int length)
    {
        return typeName switch
        {
            "Int8Array" => new SharpTSInt8Array(buffer, 0, length),
            "Uint8Array" => new SharpTSUint8Array(buffer, 0, length),
            "Uint8ClampedArray" => new SharpTSUint8ClampedArray(buffer, 0, length),
            "Int16Array" => new SharpTSInt16Array(buffer, 0, length),
            "Uint16Array" => new SharpTSUint16Array(buffer, 0, length),
            "Int32Array" => new SharpTSInt32Array(buffer, 0, length),
            "Uint32Array" => new SharpTSUint32Array(buffer, 0, length),
            "Float32Array" => new SharpTSFloat32Array(buffer, 0, length),
            "Float64Array" => new SharpTSFloat64Array(buffer, 0, length),
            "BigInt64Array" => new SharpTSBigInt64Array(buffer, 0, length),
            "BigUint64Array" => new SharpTSBigUint64Array(buffer, 0, length),
            _ => throw new DataCloneError($"Unknown TypedArray type: {typeName}")
        };
    }

    private static SharpTSMessagePort TransferMessagePort(SharpTSMessagePort port)
    {
        // Neutering happens on the sending side; the port is handed to the receiver
        port.Neuter();
        return port;
    }

    private static SharpTSArray CloneArray(SharpTSArray source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new SharpTSArray();
        cloned[source] = result;

        foreach (var element in source.Elements)
        {
            var clonedElement = CloneInternal(element, cloned, transferred);
            result.Elements.Add(clonedElement);
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

    private static SharpTSMap CloneMap(SharpTSMap source, Dictionary<object, object> cloned, HashSet<object> transferred)
    {
        var result = new SharpTSMap();
        cloned[source] = result;

        foreach (var kvp in source.InternalEntries)
        {
            var clonedKey = CloneInternal(kvp.Key, cloned, transferred);
            var clonedValue = CloneInternal(kvp.Value, cloned, transferred);
            if (clonedKey != null)
            {
                result.Set(clonedKey, clonedValue);
            }
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

    /// <summary>
    /// Validates that a value can be passed to a worker (used for compile-time validation).
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value can be cloned, false otherwise.</returns>
    public static bool CanClone(object? value)
    {
        try
        {
            ValidateCloneable(value, new HashSet<object>());
            return true;
        }
        catch (DataCloneError)
        {
            return false;
        }
    }

    private static void ValidateCloneable(object? value, HashSet<object> seen)
    {
        if (value == null || value is double or string or bool or BigInteger)
            return;

        // Prevent infinite recursion
        if (!seen.Add(value))
            return;

        switch (value)
        {
            case SharpTSSharedArrayBuffer:
            case SharpTSDate:
            case SharpTSRegExp:
            case SharpTSError:
            case SharpTSBuffer:
                return;

            case SharpTSTypedArray:
                return;

            case SharpTSArray array:
                foreach (var element in array.Elements)
                    ValidateCloneable(element, seen);
                return;

            case SharpTSObject obj:
                foreach (var (key, propValue) in obj.Fields)
                {
                    if (propValue is ISharpTSCallable or SharpTSFunction)
                        throw new DataCloneError($"Cannot clone object with function property '{key}'");
                    ValidateCloneable(propValue, seen);
                }
                return;

            case SharpTSMap map:
                foreach (var kvp in map.InternalEntries)
                {
                    ValidateCloneable(kvp.Key, seen);
                    ValidateCloneable(kvp.Value, seen);
                }
                return;

            case SharpTSSet set:
                foreach (var setValue in set.InternalValues)
                    ValidateCloneable(setValue, seen);
                return;

            case SharpTSMessagePort:
                throw new DataCloneError("MessagePort must be transferred, not cloned");

            case SharpTSPromise:
                throw new DataCloneError("Promise cannot be cloned");

            case SharpTSSymbol:
                throw new DataCloneError("Symbol cannot be cloned");

            case SharpTSFunction:
                throw new DataCloneError("Function cannot be cloned");

            case SharpTSClass:
                throw new DataCloneError("Class constructors cannot be cloned");

            case ISharpTSCallable:
                throw new DataCloneError("Function cannot be cloned");

            case SharpTSInstance:
                throw new DataCloneError("Class instances cannot be cloned");

            case SharpTSWeakMap:
                throw new DataCloneError("WeakMap cannot be cloned");

            case SharpTSWeakSet:
                throw new DataCloneError("WeakSet cannot be cloned");

            default:
                throw new DataCloneError($"Cannot clone value of type {value.GetType().Name}");
        }
    }
}
