using System.Text.Json;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// Single authoritative conversion from <see cref="JsonElement"/> trees to SharpTS runtime
/// values (<see cref="SharpTSArray"/> / <see cref="SharpTSObject"/>, doubles, strings, bools,
/// null). Shared by JSON.parse, fetch Request/Response bodies, and IPC deserialization.
/// Exception translation stays at each caller (JSON.parse throws its syntax error; body
/// readers reject their promise) — callers wrap <see cref="Parse"/> in their own try/catch.
/// The compiled-standalone converter in Compilation/RuntimeTypes.Json.cs is intentionally
/// separate: it produces List/Dictionary shapes and must not depend on SharpTS runtime types.
/// </summary>
internal static class RuntimeJson
{
    /// <summary>Parses JSON text into SharpTS runtime values. Throws <see cref="JsonException"/> on invalid input.</summary>
    public static object? Parse(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return FromElement(doc.RootElement);
    }

    /// <summary>Recursively converts a <see cref="JsonElement"/> to SharpTS runtime values.</summary>
    public static object? FromElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => new SharpTSArray(
                element.EnumerateArray().Select(FromElement).ToList()),
            JsonValueKind.Object => FromObject(element),
            _ => null
        };
    }

    private static SharpTSObject FromObject(JsonElement element)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            // JSON permits duplicate names. JSON.parse keeps the last value,
            // including for "__proto__", which is an ordinary data property
            // here rather than object-literal prototype syntax.
            fields[property.Name] = FromElement(property.Value);
        }

        return new SharpTSObject(fields);
    }
}
