using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides type signatures for built-in JavaScript types (strings, arrays, errors, etc.).
/// Used by the TypeChecker to validate method calls and property access on built-in types.
/// </summary>
/// <remarks>
/// For Error types, this class provides type information while <see cref="ErrorBuiltIns"/>
/// provides the runtime implementation. These must be kept in sync.
/// </remarks>
public static class BuiltInTypes
{
    private static readonly TypeInfo NumberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    private static readonly TypeInfo StringType = new TypeInfo.String();
    private static readonly TypeInfo BooleanType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
    private static readonly TypeInfo VoidType = new TypeInfo.Void();
    private static readonly TypeInfo AnyType = new TypeInfo.Any();
    private static readonly TypeInfo BigIntType = new TypeInfo.BigInt();

    public static TypeInfo? GetStringMemberType(string name)
    {
        return name switch
        {
            "length" => NumberType,
            "charAt" => new TypeInfo.Function([NumberType], StringType),
            "substring" => new TypeInfo.Function([NumberType, NumberType], StringType, RequiredParams: 1), // end is optional
            "indexOf" => new TypeInfo.Function([StringType], NumberType),
            "toUpperCase" => new TypeInfo.Function([], StringType),
            "toLowerCase" => new TypeInfo.Function([], StringType),
            "trim" => new TypeInfo.Function([], StringType),
            "replace" => new TypeInfo.Function([StringType, StringType], StringType),
            "split" => new TypeInfo.Function([StringType, NumberType], new TypeInfo.Array(StringType), RequiredParams: 1), // limit is optional
            "includes" => new TypeInfo.Function([StringType], BooleanType),
            "startsWith" => new TypeInfo.Function([StringType], BooleanType),
            "endsWith" => new TypeInfo.Function([StringType], BooleanType),
            "slice" => new TypeInfo.Function([NumberType, NumberType], StringType, RequiredParams: 1), // end is optional
            "repeat" => new TypeInfo.Function([NumberType], StringType),
            "padStart" => new TypeInfo.Function([NumberType, StringType], StringType, RequiredParams: 1), // padString is optional
            "padEnd" => new TypeInfo.Function([NumberType, StringType], StringType, RequiredParams: 1), // padString is optional
            "charCodeAt" => new TypeInfo.Function([NumberType], NumberType),
            "concat" => new TypeInfo.Function([new TypeInfo.Array(StringType)], StringType, RequiredParams: 0, HasRestParam: true), // variadic - takes 0 or more string arguments
            "lastIndexOf" => new TypeInfo.Function([StringType], NumberType),
            "trimStart" => new TypeInfo.Function([], StringType),
            "trimEnd" => new TypeInfo.Function([], StringType),
            "replaceAll" => new TypeInfo.Function([StringType, StringType], StringType),
            "at" => new TypeInfo.Function([NumberType], StringType), // returns string | undefined in TS
            _ => null
        };
    }

    public static TypeInfo? GetArrayMemberType(string name, TypeInfo elementType)
    {
        return name switch
        {
            "length" => NumberType,
            "push" => new TypeInfo.Function([new TypeInfo.Array(elementType)], NumberType, RequiredParams: 1, HasRestParam: true), // variadic: push(...items: T[])
            "pop" => new TypeInfo.Function([], elementType),
            "shift" => new TypeInfo.Function([], elementType),
            "unshift" => new TypeInfo.Function([elementType], NumberType),
            "slice" => new TypeInfo.Function([NumberType, NumberType], new TypeInfo.Array(elementType), RequiredParams: 0), // start/end are optional
            "map" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], AnyType)], // callback with just element param
                new TypeInfo.Array(AnyType)),
            "filter" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                new TypeInfo.Array(elementType)),
            "forEach" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], VoidType)],
                VoidType),
            "find" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                elementType),
            "findIndex" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                NumberType),
            "some" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                BooleanType),
            "every" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                BooleanType),
            "reduce" => new TypeInfo.Function(
                [new TypeInfo.Function([AnyType, elementType], AnyType), AnyType],
                AnyType, RequiredParams: 1), // initialValue is optional
            "includes" => new TypeInfo.Function([elementType], BooleanType),
            "indexOf" => new TypeInfo.Function([elementType], NumberType),
            "join" => new TypeInfo.Function([StringType], StringType, RequiredParams: 0),  // separator is optional
            "concat" => new TypeInfo.Function(
                [new TypeInfo.Array(elementType)],
                new TypeInfo.Array(elementType)),
            "reverse" => new TypeInfo.Function([], new TypeInfo.Array(elementType)),
            "flat" => new TypeInfo.Function([NumberType], new TypeInfo.Array(AnyType), RequiredParams: 0), // depth is optional, defaults to 1
            "flatMap" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType, NumberType, new TypeInfo.Array(elementType)], AnyType)],
                new TypeInfo.Array(AnyType)),
            "sort" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType, elementType], NumberType)],
                new TypeInfo.Array(elementType),
                RequiredParams: 0),
            "toSorted" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType, elementType], NumberType)],
                new TypeInfo.Array(elementType),
                RequiredParams: 0),
            "splice" => new TypeInfo.Function(
                [NumberType, NumberType, new TypeInfo.Array(elementType)],
                new TypeInfo.Array(elementType),
                RequiredParams: 0,
                HasRestParam: true),
            "toSpliced" => new TypeInfo.Function(
                [NumberType, NumberType, new TypeInfo.Array(elementType)],
                new TypeInfo.Array(elementType),
                RequiredParams: 0,
                HasRestParam: true),
            "findLast" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                elementType),
            "findLastIndex" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType], BooleanType)],
                NumberType),
            "toReversed" => new TypeInfo.Function([], new TypeInfo.Array(elementType)),
            "with" => new TypeInfo.Function([NumberType, elementType], new TypeInfo.Array(elementType)),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static methods on the Object namespace
    /// </summary>
    public static TypeInfo? GetObjectStaticMethodType(string name)
    {
        return name switch
        {
            "keys" => new TypeInfo.Function([AnyType], new TypeInfo.Array(StringType)),
            "values" => new TypeInfo.Function([AnyType], new TypeInfo.Array(AnyType)),
            "entries" => new TypeInfo.Function([AnyType], new TypeInfo.Array(AnyType)),
            "fromEntries" => new TypeInfo.Function([AnyType], AnyType),
            "hasOwn" => new TypeInfo.Function([AnyType, StringType], BooleanType),
            "assign" => new TypeInfo.Function([AnyType], AnyType),  // target, ...sources
            "freeze" => new TypeInfo.Function([AnyType], AnyType),  // Returns the frozen object
            "seal" => new TypeInfo.Function([AnyType], AnyType),    // Returns the sealed object
            "isFrozen" => new TypeInfo.Function([AnyType], BooleanType),
            "isSealed" => new TypeInfo.Function([AnyType], BooleanType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static methods on the Array namespace
    /// </summary>
    public static TypeInfo? GetArrayStaticMethodType(string name)
    {
        return name switch
        {
            "isArray" => new TypeInfo.Function([AnyType], BooleanType),
            "from" => new TypeInfo.Function(
                [AnyType, new TypeInfo.Function([AnyType, NumberType], AnyType)],
                new TypeInfo.Array(AnyType),
                RequiredParams: 1),  // mapFn is optional
            "of" => new TypeInfo.Function(
                [new TypeInfo.Array(AnyType)],  // rest parameter
                new TypeInfo.Array(AnyType),
                RequiredParams: 0,
                HasRestParam: true),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static methods on the JSON namespace
    /// </summary>
    public static TypeInfo? GetJSONStaticMethodType(string name)
    {
        return name switch
        {
            "parse" => new TypeInfo.Function([StringType], AnyType),
            "stringify" => new TypeInfo.Function([AnyType], StringType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static members (properties and methods) on the Number namespace
    /// </summary>
    public static TypeInfo? GetNumberStaticMemberType(string name)
    {
        return name switch
        {
            // Static properties (constants)
            "MAX_VALUE" => NumberType,
            "MIN_VALUE" => NumberType,
            "NaN" => NumberType,
            "POSITIVE_INFINITY" => NumberType,
            "NEGATIVE_INFINITY" => NumberType,
            "MAX_SAFE_INTEGER" => NumberType,
            "MIN_SAFE_INTEGER" => NumberType,
            "EPSILON" => NumberType,

            // Static methods
            "parseInt" => new TypeInfo.Function([StringType], NumberType),    // radix is optional
            "parseFloat" => new TypeInfo.Function([StringType], NumberType),
            "isNaN" => new TypeInfo.Function([AnyType], BooleanType),
            "isFinite" => new TypeInfo.Function([AnyType], BooleanType),
            "isInteger" => new TypeInfo.Function([AnyType], BooleanType),
            "isSafeInteger" => new TypeInfo.Function([AnyType], BooleanType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on number primitives (e.g., (123).toFixed(2))
    /// </summary>
    public static TypeInfo? GetNumberInstanceMemberType(string name)
    {
        return name switch
        {
            "toFixed" => new TypeInfo.Function([], StringType),       // digits is optional
            "toPrecision" => new TypeInfo.Function([], StringType),   // precision is optional
            "toExponential" => new TypeInfo.Function([], StringType), // fractionDigits is optional
            "toString" => new TypeInfo.Function([], StringType),      // radix is optional
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static methods on the Date namespace
    /// </summary>
    public static TypeInfo? GetDateStaticMemberType(string name)
    {
        return name switch
        {
            "now" => new TypeInfo.Function([], NumberType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on Date objects
    /// </summary>
    public static TypeInfo? GetDateInstanceMemberType(string name)
    {
        return name switch
        {
            // Getters - all return number
            "getTime" => new TypeInfo.Function([], NumberType),
            "getFullYear" => new TypeInfo.Function([], NumberType),
            "getMonth" => new TypeInfo.Function([], NumberType),
            "getDate" => new TypeInfo.Function([], NumberType),
            "getDay" => new TypeInfo.Function([], NumberType),
            "getHours" => new TypeInfo.Function([], NumberType),
            "getMinutes" => new TypeInfo.Function([], NumberType),
            "getSeconds" => new TypeInfo.Function([], NumberType),
            "getMilliseconds" => new TypeInfo.Function([], NumberType),
            "getTimezoneOffset" => new TypeInfo.Function([], NumberType),

            // Setters - all return number (the new timestamp)
            "setTime" => new TypeInfo.Function([NumberType], NumberType),
            "setFullYear" => new TypeInfo.Function([NumberType], NumberType),  // month, date optional
            "setMonth" => new TypeInfo.Function([NumberType], NumberType),     // date optional
            "setDate" => new TypeInfo.Function([NumberType], NumberType),
            "setHours" => new TypeInfo.Function([NumberType], NumberType),     // min, sec, ms optional
            "setMinutes" => new TypeInfo.Function([NumberType], NumberType),   // sec, ms optional
            "setSeconds" => new TypeInfo.Function([NumberType], NumberType),   // ms optional
            "setMilliseconds" => new TypeInfo.Function([NumberType], NumberType),

            // Conversion methods
            "toString" => new TypeInfo.Function([], StringType),
            "toISOString" => new TypeInfo.Function([], StringType),
            "toDateString" => new TypeInfo.Function([], StringType),
            "toTimeString" => new TypeInfo.Function([], StringType),
            "valueOf" => new TypeInfo.Function([], NumberType),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance members on RegExp objects
    /// </summary>
    public static TypeInfo? GetRegExpMemberType(string name)
    {
        // exec() returns array with index/input properties, or null
        var execResultType = new TypeInfo.Union([
            new TypeInfo.Array(StringType),
            new TypeInfo.Null()
        ]);

        return name switch
        {
            // Properties (read-only except lastIndex)
            "source" => StringType,
            "flags" => StringType,
            "global" => BooleanType,
            "ignoreCase" => BooleanType,
            "multiline" => BooleanType,
            "lastIndex" => NumberType,

            // Methods
            "test" => new TypeInfo.Function([StringType], BooleanType),
            "exec" => new TypeInfo.Function([StringType], execResultType),
            "toString" => new TypeInfo.Function([], StringType),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance members on Error objects.
    /// All error types share the same structure: name, message, stack properties and toString() method.
    /// </summary>
    /// <param name="name">The member name to look up</param>
    /// <param name="errorTypeName">The specific error type name (e.g., "Error", "TypeError")</param>
    /// <remarks>
    /// Runtime implementation is in <see cref="ErrorBuiltIns.GetMember"/>.
    /// Property mutability is controlled by <see cref="ErrorBuiltIns.CanSetProperty"/>.
    /// Valid error type names are defined in <see cref="ErrorBuiltIns.TypeNames"/>.
    /// </remarks>
    public static TypeInfo? GetErrorMemberType(string name, string errorTypeName)
    {
        return name switch
        {
            // Properties - all string
            "name" => StringType,
            "message" => StringType,
            "stack" => StringType,

            // Methods
            "toString" => new TypeInfo.Function([], StringType),

            // AggregateError has an additional "errors" property
            "errors" when errorTypeName == "AggregateError" =>
                new TypeInfo.Array(new TypeInfo.Error()),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on Map objects
    /// </summary>
    public static TypeInfo? GetMapMemberType(string name, TypeInfo keyType, TypeInfo valueType)
    {
        return name switch
        {
            "size" => NumberType,
            "get" => new TypeInfo.Function([keyType],
                new TypeInfo.Union([valueType, new TypeInfo.Null()])),
            "set" => new TypeInfo.Function([keyType, valueType],
                new TypeInfo.Map(keyType, valueType)),  // Returns this for chaining
            "has" => new TypeInfo.Function([keyType], BooleanType),
            "delete" => new TypeInfo.Function([keyType], BooleanType),
            "clear" => new TypeInfo.Function([], VoidType),
            "keys" => new TypeInfo.Function([],
                new TypeInfo.Iterator(keyType)),
            "values" => new TypeInfo.Function([],
                new TypeInfo.Iterator(valueType)),
            "entries" => new TypeInfo.Function([],
                new TypeInfo.Iterator(TypeInfo.Tuple.FromTypes([keyType, valueType], 2))),
            "forEach" => new TypeInfo.Function(
                [new TypeInfo.Function([valueType, keyType], VoidType)],
                VoidType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on Set objects
    /// </summary>
    public static TypeInfo? GetSetMemberType(string name, TypeInfo elementType)
    {
        var setType = new TypeInfo.Set(elementType);

        return name switch
        {
            "size" => NumberType,
            "add" => new TypeInfo.Function([elementType], setType),  // Returns this for chaining
            "has" => new TypeInfo.Function([elementType], BooleanType),
            "delete" => new TypeInfo.Function([elementType], BooleanType),
            "clear" => new TypeInfo.Function([], VoidType),
            "keys" => new TypeInfo.Function([],
                new TypeInfo.Iterator(elementType)),  // Same as values() for Set
            "values" => new TypeInfo.Function([],
                new TypeInfo.Iterator(elementType)),
            "entries" => new TypeInfo.Function([],
                new TypeInfo.Iterator(TypeInfo.Tuple.FromTypes([elementType, elementType], 2))),
            "forEach" => new TypeInfo.Function(
                [new TypeInfo.Function([elementType, elementType], VoidType)],
                VoidType),

            // ES2025 Set Operations
            "union" => new TypeInfo.Function([setType], setType),
            "intersection" => new TypeInfo.Function([setType], setType),
            "difference" => new TypeInfo.Function([setType], setType),
            "symmetricDifference" => new TypeInfo.Function([setType], setType),
            "isSubsetOf" => new TypeInfo.Function([setType], BooleanType),
            "isSupersetOf" => new TypeInfo.Function([setType], BooleanType),
            "isDisjointFrom" => new TypeInfo.Function([setType], BooleanType),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on WeakMap objects.
    /// WeakMap has no size property and no iteration methods.
    /// </summary>
    public static TypeInfo? GetWeakMapMemberType(string name, TypeInfo keyType, TypeInfo valueType)
    {
        return name switch
        {
            "get" => new TypeInfo.Function([keyType],
                new TypeInfo.Union([valueType, new TypeInfo.Null()])),
            "set" => new TypeInfo.Function([keyType, valueType],
                new TypeInfo.WeakMap(keyType, valueType)),  // Returns this for chaining
            "has" => new TypeInfo.Function([keyType], BooleanType),
            "delete" => new TypeInfo.Function([keyType], BooleanType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance methods on WeakSet objects.
    /// WeakSet has no size property and no iteration methods.
    /// </summary>
    public static TypeInfo? GetWeakSetMemberType(string name, TypeInfo elementType)
    {
        return name switch
        {
            "add" => new TypeInfo.Function([elementType],
                new TypeInfo.WeakSet(elementType)),  // Returns this for chaining
            "has" => new TypeInfo.Function([elementType], BooleanType),
            "delete" => new TypeInfo.Function([elementType], BooleanType),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static members on the Symbol namespace
    /// </summary>
    public static TypeInfo? GetSymbolStaticMemberType(string name)
    {
        var symbolType = new TypeInfo.Symbol();

        return name switch
        {
            // Well-known symbols (properties returning symbol)
            "iterator" => symbolType,
            "asyncIterator" => symbolType,
            "toStringTag" => symbolType,
            "hasInstance" => symbolType,
            "isConcatSpreadable" => symbolType,
            "toPrimitive" => symbolType,
            "species" => symbolType,
            "unscopables" => symbolType,

            // Static methods
            "for" => new TypeInfo.Function([StringType], symbolType),
            "keyFor" => new TypeInfo.Function([symbolType],
                new TypeInfo.Union([StringType, new TypeInfo.Null()])),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for global timer functions (setTimeout, clearTimeout).
    /// </summary>
    public static TypeInfo? GetGlobalTimerFunctionType(string name)
    {
        var timeoutType = new TypeInfo.Timeout();

        return name switch
        {
            // setTimeout(callback: () => void, ms?: number, ...args: any[]): Timeout
            "setTimeout" => new TypeInfo.Function(
                [new TypeInfo.Function([], VoidType), NumberType],
                timeoutType,
                RequiredParams: 1  // callback is required, delay is optional (defaults to 0)
            ),

            // clearTimeout(handle?: Timeout): void
            "clearTimeout" => new TypeInfo.Function(
                [new TypeInfo.Union([timeoutType, new TypeInfo.Null(), new TypeInfo.Undefined()])],
                VoidType,
                RequiredParams: 0  // handle is optional (safe to call with null/undefined)
            ),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance members on Timeout objects.
    /// </summary>
    public static TypeInfo? GetTimeoutMemberType(string name)
    {
        var timeoutType = new TypeInfo.Timeout();

        return name switch
        {
            // ref(): Timeout - marks timeout as keeping program alive, returns this
            "ref" => new TypeInfo.Function([], timeoutType),

            // unref(): Timeout - marks timeout as NOT keeping program alive, returns this
            "unref" => new TypeInfo.Function([], timeoutType),

            // hasRef: boolean (property)
            "hasRef" => BooleanType,

            // toString(): string - inherited from Object
            "toString" => new TypeInfo.Function([], StringType),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance members on Function objects (bind, call, apply, length, name).
    /// </summary>
    /// <param name="name">The member name to look up</param>
    /// <param name="funcType">The function type being accessed</param>
    public static TypeInfo? GetFunctionMemberType(string name, TypeInfo funcType)
    {
        var returnType = funcType is TypeInfo.Function f ? f.ReturnType : AnyType;

        // For bind(), return a function that accepts any number of args and returns the original return type
        // This is permissive because we can't track bound arguments at compile time
        var boundFunctionType = new TypeInfo.Function(
            [new TypeInfo.Array(AnyType)],  // rest param for any args
            returnType,
            RequiredParams: 0,
            HasRestParam: true);

        return name switch
        {
            "length" => NumberType,
            "name" => StringType,
            "bind" => new TypeInfo.Function(
                [AnyType],               // thisArg, followed by optional bound args
                boundFunctionType,       // Returns a permissive function type
                RequiredParams: 0,
                HasRestParam: true),
            "call" => new TypeInfo.Function(
                [AnyType],               // thisArg, followed by spread args
                returnType,              // Returns the function's return type
                RequiredParams: 0,
                HasRestParam: true),
            "apply" => new TypeInfo.Function(
                [AnyType, new TypeInfo.Union([new TypeInfo.Array(AnyType), new TypeInfo.Null()])],
                returnType,              // Returns the function's return type
                RequiredParams: 0),
            _ => null
        };
    }

    /// <summary>
    /// Type signatures for instance members on Buffer objects.
    /// </summary>
    public static TypeInfo? GetBufferMemberType(string name)
    {
        var bufferType = new TypeInfo.Buffer();

        return name switch
        {
            // Properties
            "length" => NumberType,

            // Methods
            "toString" => new TypeInfo.Function([StringType], StringType, RequiredParams: 0), // encoding optional
            "slice" => new TypeInfo.Function([NumberType, NumberType], bufferType, RequiredParams: 0), // start, end optional
            "copy" => new TypeInfo.Function(
                [bufferType, NumberType, NumberType, NumberType],
                NumberType,
                RequiredParams: 1), // target required, others optional
            "compare" => new TypeInfo.Function([bufferType], NumberType),
            "equals" => new TypeInfo.Function([bufferType], BooleanType),
            "fill" => new TypeInfo.Function(
                [AnyType, NumberType, NumberType, StringType],
                bufferType,
                RequiredParams: 1), // value required, others optional
            "write" => new TypeInfo.Function(
                [StringType, NumberType, NumberType, StringType],
                NumberType,
                RequiredParams: 1), // data required, others optional
            "readUInt8" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "writeUInt8" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "toJSON" => new TypeInfo.Function([], AnyType),

            // Multi-byte reads
            "readInt8" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readUInt16LE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readUInt16BE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readUInt32LE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readUInt32BE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readInt16LE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readInt16BE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readInt32LE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readInt32BE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readFloatLE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readFloatBE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readDoubleLE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readDoubleBE" => new TypeInfo.Function([NumberType], NumberType, RequiredParams: 0),
            "readBigInt64LE" => new TypeInfo.Function([NumberType], BigIntType, RequiredParams: 0),
            "readBigInt64BE" => new TypeInfo.Function([NumberType], BigIntType, RequiredParams: 0),
            "readBigUInt64LE" => new TypeInfo.Function([NumberType], BigIntType, RequiredParams: 0),
            "readBigUInt64BE" => new TypeInfo.Function([NumberType], BigIntType, RequiredParams: 0),

            // Multi-byte writes
            "writeInt8" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeUInt16LE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeUInt16BE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeUInt32LE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeUInt32BE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeInt16LE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeInt16BE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeInt32LE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeInt32BE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeFloatLE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeFloatBE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeDoubleLE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeDoubleBE" => new TypeInfo.Function([NumberType, NumberType], NumberType, RequiredParams: 1),
            "writeBigInt64LE" => new TypeInfo.Function([BigIntType, NumberType], NumberType, RequiredParams: 1),
            "writeBigInt64BE" => new TypeInfo.Function([BigIntType, NumberType], NumberType, RequiredParams: 1),
            "writeBigUInt64LE" => new TypeInfo.Function([BigIntType, NumberType], NumberType, RequiredParams: 1),
            "writeBigUInt64BE" => new TypeInfo.Function([BigIntType, NumberType], NumberType, RequiredParams: 1),

            // Search methods
            "indexOf" => new TypeInfo.Function([AnyType, NumberType, StringType], NumberType, RequiredParams: 1),
            "includes" => new TypeInfo.Function([AnyType, NumberType, StringType], BooleanType, RequiredParams: 1),

            // Swap methods
            "swap16" => new TypeInfo.Function([], bufferType),
            "swap32" => new TypeInfo.Function([], bufferType),
            "swap64" => new TypeInfo.Function([], bufferType),

            _ => null
        };
    }

    /// <summary>
    /// Type signatures for static methods on the Buffer namespace.
    /// </summary>
    public static TypeInfo? GetBufferStaticMethodType(string name)
    {
        var bufferType = new TypeInfo.Buffer();

        return name switch
        {
            "from" => new TypeInfo.Function(
                [new TypeInfo.Union([StringType, new TypeInfo.Array(NumberType), bufferType]), StringType],
                bufferType,
                RequiredParams: 1), // data required, encoding optional
            "alloc" => new TypeInfo.Function(
                [NumberType, AnyType, StringType],
                bufferType,
                RequiredParams: 1), // size required, fill and encoding optional
            "allocUnsafe" => new TypeInfo.Function([NumberType], bufferType),
            "allocUnsafeSlow" => new TypeInfo.Function([NumberType], bufferType),
            "concat" => new TypeInfo.Function(
                [new TypeInfo.Array(bufferType), NumberType],
                bufferType,
                RequiredParams: 1), // list required, totalLength optional
            "isBuffer" => new TypeInfo.Function([AnyType], BooleanType),
            "byteLength" => new TypeInfo.Function(
                [new TypeInfo.Union([StringType, bufferType]), StringType],
                NumberType,
                RequiredParams: 1), // string required, encoding optional
            "compare" => new TypeInfo.Function([bufferType, bufferType], NumberType),
            "isEncoding" => new TypeInfo.Function([StringType], BooleanType),

            _ => null
        };
    }
}
