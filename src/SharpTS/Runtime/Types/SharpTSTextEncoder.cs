using System.Text;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the Web/Node.js TextEncoder API.
/// TextEncoder encodes strings into UTF-8 encoded byte arrays.
/// </summary>
public class SharpTSTextEncoder : ISharpTSPropertyAccessor
{
    private static readonly string[] _propertyNames = ["encoding", "encode", "encodeInto"];
    /// <summary>
    /// The encoding used by this TextEncoder. Always "utf-8".
    /// </summary>
    public string Encoding => "utf-8";

    /// <summary>
    /// Creates a new TextEncoder instance.
    /// TextEncoder only supports UTF-8 encoding.
    /// </summary>
    public SharpTSTextEncoder()
    {
    }

    /// <summary>
    /// Encodes a string into a Uint8Array (Buffer) of UTF-8 bytes.
    /// </summary>
    /// <param name="input">The string to encode.</param>
    /// <returns>A Buffer containing the UTF-8 encoded bytes.</returns>
    public SharpTSBuffer Encode(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? "");
        return new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Encodes a string into an existing Uint8Array (Buffer).
    /// Returns an object with read (number of characters read) and written (number of bytes written).
    /// </summary>
    /// <param name="source">The string to encode.</param>
    /// <param name="destination">The buffer to write into.</param>
    /// <returns>An object with read and written properties.</returns>
    public SharpTSObject EncodeInto(string source, SharpTSBuffer destination)
    {
        source ??= "";
        var encoder = System.Text.Encoding.UTF8.GetEncoder();
        var sourceChars = source.ToCharArray();
        var destBytes = destination.Data;

        int charsUsed;
        int bytesUsed;
        bool completed;

        encoder.Convert(sourceChars, 0, sourceChars.Length, destBytes, 0, destBytes.Length,
            true, out charsUsed, out bytesUsed, out completed);

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["read"] = (double)charsUsed,
            ["written"] = (double)bytesUsed
        });
    }

    /// <summary>
    /// Gets a property value by name (ISharpTSPropertyAccessor).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "encoding" => Encoding,
            "encode" => BuiltInMethod.CreateV2("encode", 0, 1, (_, _, args) =>
            {
                var input = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
                return RuntimeValue.FromBoxed(Encode(input));
            }),
            "encodeInto" => BuiltInMethod.CreateV2("encodeInto", 2, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new Exception("TextEncoder.encodeInto requires 2 arguments");
                var source = args[0].ToObject()?.ToString() ?? "";
                if (args[1].ToObject() is not SharpTSBuffer dest)
                    throw new Exception("TextEncoder.encodeInto: second argument must be a Uint8Array");
                return RuntimeValue.FromBoxed(EncodeInto(source, dest));
            }),
            _ => null
        };
    }

    /// <summary>
    /// Sets a property value by name (ISharpTSPropertyAccessor).
    /// TextEncoder properties are read-only, so this is a no-op.
    /// </summary>
    public void SetProperty(string name, object? value)
    {
        // TextEncoder is immutable, ignore property sets
    }

    /// <summary>
    /// Checks if a property exists (ISharpTSPropertyAccessor).
    /// </summary>
    public bool HasProperty(string name) => name is "encoding" or "encode" or "encodeInto";

    /// <summary>
    /// Gets all property names for iteration (ISharpTSPropertyAccessor).
    /// </summary>
    public IEnumerable<string> PropertyNames => _propertyNames;

    public override string ToString() => "[object TextEncoder]";
}
