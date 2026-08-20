using System.Buffers;
using System.Text;
using System.Text.Json;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// One-pass JSON parser for interpreter runtime values. The parser writes
/// directly into <see cref="SharpTSArray"/> and <see cref="SharpTSObject"/>
/// nodes instead of materializing a <see cref="JsonDocument"/> DOM first.
/// </summary>
internal static class RuntimeJson
{
    private const int MaxCachedPropertyNames = 64;

    /// <summary>
    /// Parses JSON text into SharpTS runtime values. Throws
    /// <see cref="JsonException"/> on invalid input.
    /// </summary>
    public static object? Parse(string text)
    {
        int byteCount = Encoding.UTF8.GetByteCount(text);
        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        try
        {
            int written = Encoding.UTF8.GetBytes(
                text.AsSpan(), rented.AsSpan(0, byteCount));
            var reader = new Utf8JsonReader(
                rented.AsSpan(0, written),
                isFinalBlock: true,
                state: default);
            if (!reader.Read())
                throw new JsonException("Expected a JSON value.");

            var propertyNames = new List<string>(8);
            object? result = ParseValue(ref reader, propertyNames);
            if (reader.Read())
                throw new JsonException("Additional text follows the JSON value.");
            return result;
        }
        finally
        {
            // ArrayPool.Shared has bounded buckets and rejects oversized arrays;
            // the parse never retains workload-sized buffers itself.
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    private static object? ParseValue(
        ref Utf8JsonReader reader,
        List<string> propertyNames)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartObject => ParseObject(ref reader, propertyNames),
            JsonTokenType.StartArray => ParseArray(ref reader, propertyNames),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Unexpected JSON token {reader.TokenType}.")
        };
    }

    private static SharpTSObject ParseObject(
        ref Utf8JsonReader reader,
        List<string> propertyNames)
    {
        var fields = new Dictionary<string, object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a JSON property name.");

            string propertyName = GetCachedPropertyName(
                ref reader, propertyNames);
            if (!reader.Read())
                throw new JsonException("Expected a JSON property value.");

            // Duplicate names overwrite the value but retain the first
            // insertion position, matching JSON.parse and the previous DOM
            // conversion path. "__proto__" remains an ordinary data property.
            fields[propertyName] = ParseValue(ref reader, propertyNames);
        }

        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("Unexpected end of JSON object.");
        return new SharpTSObject(fields);
    }

    private static SharpTSArray ParseArray(
        ref Utf8JsonReader reader,
        List<string> propertyNames)
    {
        var result = new SharpTSArray();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            result.Add(ParseValue(ref reader, propertyNames));

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Unexpected end of JSON array.");
        return result;
    }

    private static string GetCachedPropertyName(
        ref Utf8JsonReader reader,
        List<string> propertyNames)
    {
        foreach (string candidate in propertyNames)
        {
            if (reader.ValueTextEquals(candidate))
                return candidate;
        }

        string propertyName = reader.GetString()
            ?? throw new JsonException("A JSON property name was null.");
        if (propertyNames.Count < MaxCachedPropertyNames)
            propertyNames.Add(propertyName);
        return propertyName;
    }
}
