using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in implementations for Worker Threads API.
/// </summary>
/// <remarks>
/// Provides constructors and methods for:
/// - Worker: Executes scripts in separate threads
/// - SharedArrayBuffer: Shared memory across threads
/// - Atomics: Atomic operations on shared memory
/// - MessageChannel/MessagePort: Communication channels
/// - TypedArrays: Views over buffers
/// </remarks>
public static class WorkerBuiltIns
{
    /// <summary>
    /// Gets the worker_threads module exports object.
    /// </summary>
    public static SharpTSObject GetWorkerThreadsModule()
    {
        return WorkerThreads.CreateModuleExports();
    }

    /// <summary>
    /// Gets a member from the Atomics global object.
    /// </summary>
    public static object? GetAtomicsMember(string name)
    {
        return SharpTSAtomics.GetMember(name);
    }

    /// <summary>
    /// Creates a SharedArrayBuffer constructor function.
    /// </summary>
    public static ISharpTSCallable SharedArrayBufferConstructor => new SharedArrayBufferConstructorImpl();

    /// <summary>
    /// Creates a MessageChannel constructor function.
    /// </summary>
    public static ISharpTSCallable MessageChannelConstructor => new MessageChannelConstructor();

    /// <summary>
    /// Creates typed array constructor functions.
    /// </summary>
    public static ISharpTSCallable GetTypedArrayConstructor(string typeName) => typeName switch
    {
        "Int8Array" => new TypedArrayConstructorImpl<SharpTSInt8Array>(
            len => new SharpTSInt8Array(len),
            (buf, off, len) => new SharpTSInt8Array(buf, off, len)),
        "Uint8Array" => new TypedArrayConstructorImpl<SharpTSUint8Array>(
            len => new SharpTSUint8Array(len),
            (buf, off, len) => new SharpTSUint8Array(buf, off, len)),
        "Uint8ClampedArray" => new TypedArrayConstructorImpl<SharpTSUint8ClampedArray>(
            len => new SharpTSUint8ClampedArray(len),
            (buf, off, len) => new SharpTSUint8ClampedArray(buf, off, len)),
        "Int16Array" => new TypedArrayConstructorImpl<SharpTSInt16Array>(
            len => new SharpTSInt16Array(len),
            (buf, off, len) => new SharpTSInt16Array(buf, off, len)),
        "Uint16Array" => new TypedArrayConstructorImpl<SharpTSUint16Array>(
            len => new SharpTSUint16Array(len),
            (buf, off, len) => new SharpTSUint16Array(buf, off, len)),
        "Int32Array" => new TypedArrayConstructorImpl<SharpTSInt32Array>(
            len => new SharpTSInt32Array(len),
            (buf, off, len) => new SharpTSInt32Array(buf, off, len)),
        "Uint32Array" => new TypedArrayConstructorImpl<SharpTSUint32Array>(
            len => new SharpTSUint32Array(len),
            (buf, off, len) => new SharpTSUint32Array(buf, off, len)),
        "Float32Array" => new TypedArrayConstructorImpl<SharpTSFloat32Array>(
            len => new SharpTSFloat32Array(len),
            (buf, off, len) => new SharpTSFloat32Array(buf, off, len)),
        "Float64Array" => new TypedArrayConstructorImpl<SharpTSFloat64Array>(
            len => new SharpTSFloat64Array(len),
            (buf, off, len) => new SharpTSFloat64Array(buf, off, len)),
        "BigInt64Array" => new TypedArrayConstructorImpl<SharpTSBigInt64Array>(
            len => new SharpTSBigInt64Array(len),
            (buf, off, len) => new SharpTSBigInt64Array(buf, off, len)),
        "BigUint64Array" => new TypedArrayConstructorImpl<SharpTSBigUint64Array>(
            len => new SharpTSBigUint64Array(len),
            (buf, off, len) => new SharpTSBigUint64Array(buf, off, len)),
        _ => throw new Exception($"Unknown TypedArray type: {typeName}")
    };

    /// <summary>
    /// Singleton Atomics object for global access.
    /// </summary>
    public static readonly AtomicsSingleton Atomics = new();
}

/// <summary>
/// SharedArrayBuffer constructor implementation.
/// </summary>
internal class SharedArrayBufferConstructorImpl : ISharpTSCallable
{
    public int Arity() => 1;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0 || arguments[0] is not double length)
            throw new Exception("SharedArrayBuffer constructor requires a length argument");

        return new SharpTSSharedArrayBuffer((int)length);
    }

    public object? GetProperty(string name)
    {
        return name switch
        {
            "prototype" => null,
            _ => null
        };
    }
}

/// <summary>
/// Generic TypedArray constructor implementation.
/// </summary>
internal class TypedArrayConstructorImpl<T> : ISharpTSCallable where T : SharpTSTypedArray
{
    private readonly Func<int, T> _createFromLength;
    private readonly Func<SharpTSSharedArrayBuffer, int, int?, T> _createFromSharedBuffer;

    public TypedArrayConstructorImpl(
        Func<int, T> createFromLength,
        Func<SharpTSSharedArrayBuffer, int, int?, T> createFromSharedBuffer)
    {
        _createFromLength = createFromLength;
        _createFromSharedBuffer = createFromSharedBuffer;
    }

    public int Arity() => 1;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0)
            throw new Exception("TypedArray constructor requires at least one argument");

        // new Int32Array(length)
        if (arguments[0] is double length)
        {
            return _createFromLength((int)length);
        }

        // new Int32Array(sharedArrayBuffer, byteOffset?, length?)
        if (arguments[0] is SharpTSSharedArrayBuffer sab)
        {
            int byteOffset = arguments.Count > 1 && arguments[1] is double bo ? (int)bo : 0;
            int? len = arguments.Count > 2 && arguments[2] is double l ? (int)l : null;
            return _createFromSharedBuffer(sab, byteOffset, len);
        }

        // new Int32Array(array) - copy from another array
        if (arguments[0] is SharpTSArray arr)
        {
            var result = _createFromLength(arr.Elements.Count);
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                result[i] = arr.Elements[i];
            }
            return result;
        }

        // new Int32Array(typedArray) - copy from another typed array
        if (arguments[0] is SharpTSTypedArray other)
        {
            var result = _createFromLength(other.Length);
            for (int i = 0; i < other.Length; i++)
            {
                result[i] = other[i];
            }
            return result;
        }

        throw new Exception("Invalid arguments for TypedArray constructor");
    }

    public object? GetProperty(string name)
    {
        return name switch
        {
            "BYTES_PER_ELEMENT" => GetBytesPerElement(),
            "prototype" => null,
            _ => null
        };
    }

    private double GetBytesPerElement()
    {
        // Create a temporary instance to get BytesPerElement
        var temp = _createFromLength(0);
        return temp.BytesPerElement;
    }
}

/// <summary>
/// Atomics singleton object for global namespace.
/// </summary>
public class AtomicsSingleton
{
    public object? GetMember(string name) => SharpTSAtomics.GetMember(name);

    public override string ToString() => "Atomics";
}
