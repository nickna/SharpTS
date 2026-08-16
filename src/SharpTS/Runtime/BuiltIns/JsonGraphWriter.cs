using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Serializes the plain object graphs the runtime's converters produce (null / bool / double /
/// string / arrays / string-keyed dictionaries, with ToString for anything else) using
/// <see cref="Utf8JsonWriter"/> directly. Replaces <c>JsonSerializer.Serialize(object?)</c> at
/// the fetch/Response/IPC call sites (#1324 Phase 1): serializing an <c>object</c> graph is the
/// worst case for the reflection resolver under trimming and Native AOT, while the writer is
/// reflection-free. Output bytes are identical — same default encoder, same number formatting.
/// </summary>
internal static class JsonGraphWriter
{
    internal static string Write(object? value, bool indented = false)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            WriteValue(writer, value);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case Dictionary<string, object?> obj: // the converters' shape — most common, matched first
                writer.WriteStartObject();
                foreach (var (key, val) in obj)
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, val);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IDictionary dict: // other string-keyed dictionaries (e.g. tsconfig paths)
                writer.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    writer.WritePropertyName(entry.Key.ToString() ?? "");
                    WriteValue(writer, entry.Value);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable seq: // arrays and typed lists (string is matched above)
                writer.WriteStartArray();
                foreach (var element in seq)
                    WriteValue(writer, element);
                writer.WriteEndArray();
                break;
            default:
                // The converters stringify unknown leaves before reaching here; this matches
                // JsonSerializer's behavior for the same pre-converted graphs.
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
