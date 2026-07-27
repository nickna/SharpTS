using System.Text.Json;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Serializes/deserializes SharpTS values for IPC messaging (fork/process.send).
/// Uses JSON over the wire, converting between SharpTS runtime types and JSON.
/// </summary>
public static class IpcSerializer
{
    /// <summary>
    /// Serialize a SharpTS value to a JSON string for IPC transmission.
    /// </summary>
    public static string Serialize(object? value)
    {
        return JsonSerializer.Serialize(ToJsonElement(value));
    }

    /// <summary>
    /// Deserialize a JSON string from IPC into SharpTS runtime values.
    /// </summary>
    public static object? Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        return RuntimeJson.Parse(json);
    }

    private static object? ToJsonElement(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b,
            double d => d,
            string s => s,
            SharpTSArray arr => arr.Select(ToJsonElement).ToList(),
            SharpTSObject obj => obj.Fields.ToDictionary(kv => kv.Key, kv => ToJsonElement(kv.Value)),
            List<object?> list => list.Select(ToJsonElement).ToList(),
            Dictionary<string, object?> dict => dict.ToDictionary(kv => kv.Key, kv => ToJsonElement(kv.Value)),
            _ => value.ToString()
        };
    }

}
