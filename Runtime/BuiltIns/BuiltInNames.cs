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

    /// <summary>
    /// Global constructor function names (can be called with or without 'new').
    /// </summary>
    public static readonly string[] GlobalConstructorNames =
    [
        Symbol, BigInt, Date, RegExp,
        Map, Set, WeakMap, WeakSet,
        Array, Object, Number, String, Boolean,
        Promise, ArrayBuffer, SharedArrayBuffer, DataView,
        MessageChannel, EventEmitter,
        TextEncoder, TextDecoder, URL, URLSearchParams
    ];

    // Individual constructor constants
    public const string Symbol = "Symbol";
    public const string BigInt = "BigInt";
    public const string Date = "Date";
    public const string RegExp = "RegExp";
    public const string Map = "Map";
    public const string Set = "Set";
    public const string WeakMap = "WeakMap";
    public const string WeakSet = "WeakSet";
    public const string Array = "Array";
    public const string Object = "Object";
    public const string Number = "Number";
    public const string String = "String";
    public const string Boolean = "Boolean";
    public const string Promise = "Promise";
    public const string ArrayBuffer = "ArrayBuffer";
    public const string SharedArrayBuffer = "SharedArrayBuffer";
    public const string DataView = "DataView";
    public const string MessageChannel = "MessageChannel";
    public const string EventEmitter = "EventEmitter";
    public const string TextEncoder = "TextEncoder";
    public const string TextDecoder = "TextDecoder";
    public const string URL = "URL";
    public const string URLSearchParams = "URLSearchParams";

    #endregion

    #region Global Function Names

    /// <summary>
    /// Global function names (not constructors).
    /// </summary>
    public static readonly string[] GlobalFunctionNames =
    [
        ParseInt, ParseFloat, IsNaN, IsFinite,
        EncodeURI, DecodeURI, EncodeURIComponent, DecodeURIComponent,
        StructuredClone, Fetch,
        SetTimeout, ClearTimeout, SetInterval, ClearInterval,
        SetImmediate, ClearImmediate,
        QueueMicrotask
    ];

    // Individual function constants
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

    #endregion

    #region Built-in Namespace/Singleton Names

    /// <summary>
    /// Built-in namespace singleton names (Math, JSON, console, etc.).
    /// </summary>
    public static readonly string[] NamespaceNames =
    [
        Math, JSON, Console, Process, Reflect, Atomics, Intl
    ];

    // Individual namespace constants
    public const string Math = "Math";
    public const string JSON = "JSON";
    public const string Console = "console";
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

    #endregion
}
