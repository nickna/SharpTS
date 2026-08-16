namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Centralized constants for all built-in JavaScript type and function names.
/// Use these constants instead of hardcoded strings throughout the codebase.
/// </summary>
/// <remarks>
/// This class provides a single source of truth for magic strings used across:
/// - TypeChecker (type validation)
/// - Interpreter (runtime execution)
/// - ILEmitter (IL compilation)
/// </remarks>
public static class BuiltInNames
{
    #region TypedArray Names

    /// <summary>
    /// All JavaScript TypedArray type names.
    /// </summary>
    public static readonly string[] TypedArrayNames =
    [
        Int8Array, Uint8Array, Uint8ClampedArray,
        Int16Array, Uint16Array,
        Int32Array, Uint32Array,
        Float32Array, Float64Array,
        BigInt64Array, BigUint64Array
    ];

    /// <summary>
    /// HashSet for O(1) TypedArray name lookup.
    /// </summary>
    public static readonly HashSet<string> TypedArrayNameSet =
        new(TypedArrayNames, StringComparer.Ordinal);

    // Individual TypedArray constants
    public const string Int8Array = "Int8Array";
    public const string Uint8Array = "Uint8Array";
    public const string Uint8ClampedArray = "Uint8ClampedArray";
    public const string Int16Array = "Int16Array";
    public const string Uint16Array = "Uint16Array";
    public const string Int32Array = "Int32Array";
    public const string Uint32Array = "Uint32Array";
    public const string Float32Array = "Float32Array";
    public const string Float64Array = "Float64Array";
    public const string BigInt64Array = "BigInt64Array";
    public const string BigUint64Array = "BigUint64Array";

    /// <summary>
    /// Checks if a name is a built-in TypedArray type name.
    /// </summary>
    public static bool IsTypedArrayName(string name) => TypedArrayNameSet.Contains(name);

    #endregion

    #region Error Type Names

    /// <summary>
    /// All JavaScript Error type names.
    /// Delegates to ErrorBuiltIns.TypeNames for single source of truth.
    /// </summary>
    public static HashSet<string> ErrorTypeNames => ErrorBuiltIns.TypeNames;

    // Individual Error type constants
    public const string Error = "Error";
    public const string TypeError = "TypeError";
    public const string RangeError = "RangeError";
    public const string ReferenceError = "ReferenceError";
    public const string SyntaxError = "SyntaxError";
    public const string URIError = "URIError";
    public const string EvalError = "EvalError";
    public const string AggregateError = "AggregateError";

    /// <summary>
    /// Checks if a name is a built-in Error type name.
    /// </summary>
    public static bool IsErrorTypeName(string name) => ErrorBuiltIns.IsErrorTypeName(name);

    #endregion

    #region Global Constructor Names

    // Individual constructor constants
    public const string Request = "Request";
    public const string Response = "Response";
    public const string Symbol = "Symbol";
    public const string BigInt = "BigInt";
    public const string Date = "Date";
    public const string RegExp = "RegExp";
    public const string Map = "Map";
    public const string Set = "Set";
    public const string WeakMap = "WeakMap";
    public const string WeakSet = "WeakSet";
    public const string WeakRef = "WeakRef";
    public const string FinalizationRegistry = "FinalizationRegistry";
    public const string Proxy = "Proxy";
    public const string Array = "Array";
    public const string Object = "Object";
    public const string Number = "Number";
    public const string String = "String";
    public const string Boolean = "Boolean";
    public const string Function = "Function";
    public const string Promise = "Promise";
    public const string ArrayBuffer = "ArrayBuffer";
    public const string SharedArrayBuffer = "SharedArrayBuffer";
    public const string DataView = "DataView";
    public const string MessageChannel = "MessageChannel";
    public const string EventEmitter = "EventEmitter";
    public const string TextEncoder = "TextEncoder";
    public const string TextDecoder = "TextDecoder";
    public const string AbortController = "AbortController";
    public const string AbortSignal = "AbortSignal";
    public const string BroadcastChannel = "BroadcastChannel";
    public const string Headers = "Headers";
    // URL / URLSearchParams — migrated to stdlib/node/url.ts; no built-in name.
    public const string ReadableStream = "ReadableStream";
    public const string WritableStream = "WritableStream";
    public const string TransformStream = "TransformStream";
    public const string ByteLengthQueuingStrategy = "ByteLengthQueuingStrategy";
    public const string CountQueuingStrategy = "CountQueuingStrategy";
    public const string Blob = "Blob";
    public const string File = "File";
    public const string ReadableStreamDefaultReader = "ReadableStreamDefaultReader";
    public const string WritableStreamDefaultWriter = "WritableStreamDefaultWriter";
    public const string ReadableStreamDefaultController = "ReadableStreamDefaultController";
    public const string WritableStreamDefaultController = "WritableStreamDefaultController";
    public const string TransformStreamDefaultController = "TransformStreamDefaultController";

    #endregion

    #region Global Function Names

    // Individual function constants
    public const string Eval = "eval";
    public const string ParseInt = "parseInt";
    public const string ParseFloat = "parseFloat";
    public const string IsNaN = "isNaN";
    public const string IsFinite = "isFinite";
    public const string EncodeURI = "encodeURI";
    public const string DecodeURI = "decodeURI";
    public const string EncodeURIComponent = "encodeURIComponent";
    public const string DecodeURIComponent = "decodeURIComponent";
    public const string StructuredClone = "structuredClone";
    public const string Fetch = "fetch";
    public const string SetTimeout = "setTimeout";
    public const string ClearTimeout = "clearTimeout";
    public const string SetInterval = "setInterval";
    public const string ClearInterval = "clearInterval";
    public const string SetImmediate = "setImmediate";
    public const string ClearImmediate = "clearImmediate";
    public const string QueueMicrotask = "queueMicrotask";
    public const string Atob = "atob";
    public const string Btoa = "btoa";
    public const string Require = "require";

    #endregion

    #region Built-in Namespace/Singleton Names

    // Individual namespace constants
    public const string Math = "Math";
    public const string JSON = "JSON";
    public const string Console = "console";
    public const string ConsolePrefix = "console.";
    public const string Process = "process";
    public const string Reflect = "Reflect";
    public const string Atomics = "Atomics";
    public const string Intl = "Intl";

    #endregion

    #region Special Names

    /// <summary>
    /// Special global identifiers.
    /// </summary>
    public const string GlobalThis = "globalThis";
    public const string Undefined = "undefined";
    public const string NaN = "NaN";
    public const string Infinity = "Infinity";

    /// <summary>
    /// Internal helper function names used by the compiler/interpreter.
    /// </summary>
    public const string ObjectRest = "__objectRest";

    /// <summary>
    /// Normalizes an array-destructuring source through the iterator protocol.
    /// Array binding patterns (<c>const [a, b] = src</c>) desugar to positional
    /// index access, which only works for index-addressable sources. This helper
    /// wraps the source so non-indexable iterables (generators, Set, Map, objects
    /// with <c>[Symbol.iterator]</c>) are materialized into an array first, matching
    /// JS's iterator-protocol semantics (#685). Index-addressable sources
    /// (arrays, strings, tuples) pass through unchanged to keep the fast path.
    /// </summary>
    public const string ArrayDestructure = "__arrayDestructure";

    #endregion
}
