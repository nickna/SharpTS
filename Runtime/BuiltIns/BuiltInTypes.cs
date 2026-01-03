using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.BuiltIns;

public static class BuiltInTypes
{
    private static readonly TypeInfo NumberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    private static readonly TypeInfo StringType = new TypeInfo.Primitive(TokenType.TYPE_STRING);
    private static readonly TypeInfo BooleanType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
    private static readonly TypeInfo VoidType = new TypeInfo.Void();
    private static readonly TypeInfo AnyType = new TypeInfo.Any();

    public static TypeInfo? GetStringMemberType(string name)
    {
        return name switch
        {
            "length" => NumberType,
            "charAt" => new TypeInfo.Function([NumberType], StringType),
            "substring" => new TypeInfo.Function([NumberType], StringType), // end is optional
            "indexOf" => new TypeInfo.Function([StringType], NumberType),
            "toUpperCase" => new TypeInfo.Function([], StringType),
            "toLowerCase" => new TypeInfo.Function([], StringType),
            "trim" => new TypeInfo.Function([], StringType),
            "replace" => new TypeInfo.Function([StringType, StringType], StringType),
            "split" => new TypeInfo.Function([StringType], new TypeInfo.Array(StringType)),
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
            "slice" => new TypeInfo.Function([], new TypeInfo.Array(elementType)), // start/end are optional
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
                [new TypeInfo.Function([AnyType, elementType], AnyType)],
                AnyType),
            "includes" => new TypeInfo.Function([elementType], BooleanType),
            "indexOf" => new TypeInfo.Function([elementType], NumberType),
            "join" => new TypeInfo.Function([], StringType),  // separator is optional
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
}
