using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.BuiltIns;

public static class BuiltInTypes
{
    private static readonly TypeInfo NumberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    private static readonly TypeInfo StringType = new TypeInfo.String();
    private static readonly TypeInfo BooleanType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
    private static readonly TypeInfo VoidType = new TypeInfo.Void();
    private static readonly TypeInfo AnyType = new TypeInfo.Any();

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
            "split" => new TypeInfo.Function([StringType], new TypeInfo.Array(StringType)),
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
            "push" => new TypeInfo.Function([elementType], NumberType),
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
                new TypeInfo.Iterator(new TypeInfo.Tuple([keyType, valueType], 2))),
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
                new TypeInfo.Iterator(new TypeInfo.Tuple([elementType, elementType], 2))),
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
}
