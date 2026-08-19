using System.Text.Json;

namespace SharpTS.DebugAdapter.Protocol;

internal static class DapJson
{
    public static string RequiredString(this JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new DapRequestException($"'{name}' is required and must be a non-empty string.");
        }
        return value.GetString()!;
    }

    public static string? OptionalString(this JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new DapRequestException($"'{name}' must be a string.");
        return value.GetString();
    }

    public static bool OptionalBoolean(this JsonElement arguments, string name, bool defaultValue = false)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new DapRequestException($"'{name}' must be a boolean."),
        };
    }

    public static int RequiredInt32(this JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || !value.TryGetInt32(out int result))
            throw new DapRequestException($"'{name}' is required and must be an integer.");
        return result;
    }

    public static IReadOnlyList<string> OptionalStringArray(this JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new DapRequestException($"'{name}' must be an array of strings.");

        var values = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new DapRequestException($"'{name}' must contain only strings.");
            values.Add(item.GetString()!);
        }
        return values;
    }
}

internal sealed class DapRequestException(string message, int errorId = 1001) : Exception(message)
{
    public int ErrorId { get; } = errorId;
}
