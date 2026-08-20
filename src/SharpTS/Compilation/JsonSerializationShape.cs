using System.Globalization;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Closed, immutable description of the statically known JSON portion of a
/// value. Unknown leaves remain generic; record keys and array element shapes
/// let the emitted runtime bypass dynamic object traversal after a complete
/// side-effect-free guard succeeds.
/// </summary>
internal abstract record JsonSerializationShape
{
    internal sealed record Generic : JsonSerializationShape;
    internal sealed record Number : JsonSerializationShape;
    internal sealed record String : JsonSerializationShape;
    internal sealed record Boolean : JsonSerializationShape;
    internal sealed record Array(JsonSerializationShape Element) : JsonSerializationShape;
    internal sealed record Record(IReadOnlyList<(string Key, JsonSerializationShape Value)> Fields)
        : JsonSerializationShape;
}

internal static class JsonSerializationShapeAnalyzer
{
    private const int MaxStaticDepth = 32;

    public static bool TryAnalyze(TypeInfo? type, out JsonSerializationShape shape)
    {
        var active = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);
        shape = Analyze(type, active, 0);
        return shape is JsonSerializationShape.Record or JsonSerializationShape.Array;
    }

    public static bool TryAnalyzeObjectLiteral(
        Expr.ObjectLiteral literal,
        TypeMap? typeMap,
        out JsonSerializationShape.Record shape)
    {
        var fields = new List<(string Key, JsonSerializationShape Value)>(literal.Properties.Count);
        var active = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);
        foreach (var property in literal.Properties)
        {
            if (property.IsSpread || property.Kind is not Expr.ObjectPropertyKind.Value ||
                property.Key is Expr.ComputedKey)
            {
                shape = null!;
                return false;
            }

            string key = property.Key switch
            {
                Expr.IdentifierKey identifier => identifier.Name.Lexeme,
                Expr.LiteralKey literalKey when literalKey.Literal.Type == TokenType.STRING
                    => (string)literalKey.Literal.Literal!,
                Expr.LiteralKey literalKey when literalKey.Literal.Type == TokenType.NUMBER
                    => Convert.ToString(literalKey.Literal.Literal, CultureInfo.InvariantCulture)!,
                _ => ""
            };
            if (key.Length == 0 || key == "toJSON" || IsArrayIndex(key))
            {
                shape = null!;
                return false;
            }

            fields.Add((key, Analyze(typeMap?.Get(property.Value), active, 1)));
        }

        shape = new JsonSerializationShape.Record(fields);
        return true;
    }

    public static string Fingerprint(JsonSerializationShape shape)
    {
        var builder = new System.Text.StringBuilder();
        AppendFingerprint(builder, shape);
        return builder.ToString();
    }

    public static bool IsClosed(JsonSerializationShape shape) => shape switch
    {
        JsonSerializationShape.Generic => false,
        JsonSerializationShape.Array array => IsClosed(array.Element),
        JsonSerializationShape.Record record => record.Fields.All(field => IsClosed(field.Value)),
        _ => true
    };

    private static JsonSerializationShape Analyze(
        TypeInfo? type,
        HashSet<TypeInfo> active,
        int depth)
    {
        if (type is null || depth >= MaxStaticDepth)
            return new JsonSerializationShape.Generic();

        return type switch
        {
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral
                => new JsonSerializationShape.Number(),
            TypeInfo.String or TypeInfo.StringLiteral
                => new JsonSerializationShape.String(),
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral
                => new JsonSerializationShape.Boolean(),
            TypeInfo.Array array => AnalyzeArray(array, active, depth),
            TypeInfo.Record record => AnalyzeRecord(record, active, depth),
            _ => new JsonSerializationShape.Generic()
        };
    }

    private static JsonSerializationShape AnalyzeArray(
        TypeInfo.Array array,
        HashSet<TypeInfo> active,
        int depth)
    {
        if (!active.Add(array)) return new JsonSerializationShape.Generic();
        try
        {
            return new JsonSerializationShape.Array(
                Analyze(array.ElementType, active, depth + 1));
        }
        finally
        {
            active.Remove(array);
        }
    }

    private static JsonSerializationShape AnalyzeRecord(
        TypeInfo.Record record,
        HashSet<TypeInfo> active,
        int depth)
    {
        if (record.HasIndexSignature || record.IsCallable || record.IsConstructable ||
            !active.Add(record))
            return new JsonSerializationShape.Generic();

        try
        {
            var fields = new List<(string Key, JsonSerializationShape Value)>(record.Fields.Count);
            foreach (var field in record.Fields)
            {
                // An own hook must run through the generic serializer. Canonical
                // array-index keys have spec-defined numeric ordering rather than
                // declaration/insertion ordering, so they are conservatively left
                // out of the constant-order path as well.
                if (field.Key == "toJSON" || IsArrayIndex(field.Key))
                    return new JsonSerializationShape.Generic();

                fields.Add((field.Key, Analyze(field.Value, active, depth + 1)));
            }

            return new JsonSerializationShape.Record(fields);
        }
        finally
        {
            active.Remove(record);
        }
    }

    private static bool IsArrayIndex(string key) =>
        uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) &&
        value != uint.MaxValue &&
        value.ToString(CultureInfo.InvariantCulture) == key;

    private static void AppendFingerprint(
        System.Text.StringBuilder builder,
        JsonSerializationShape shape)
    {
        switch (shape)
        {
            case JsonSerializationShape.Generic:
                builder.Append('g');
                break;
            case JsonSerializationShape.Number:
                builder.Append('n');
                break;
            case JsonSerializationShape.String:
                builder.Append('s');
                break;
            case JsonSerializationShape.Boolean:
                builder.Append('b');
                break;
            case JsonSerializationShape.Array array:
                builder.Append('[');
                AppendFingerprint(builder, array.Element);
                builder.Append(']');
                break;
            case JsonSerializationShape.Record record:
                builder.Append('{');
                foreach (var (key, value) in record.Fields)
                {
                    builder.Append(key.Length).Append(':').Append(key).Append('=');
                    AppendFingerprint(builder, value);
                    builder.Append(';');
                }
                builder.Append('}');
                break;
        }
    }
}
