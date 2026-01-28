using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the Web/Node.js TextDecoder API.
/// TextDecoder decodes byte arrays into strings using a specified encoding.
/// </summary>
public class SharpTSTextDecoder : ISharpTSPropertyAccessor
{
    private static readonly string[] _propertyNames = ["encoding", "fatal", "ignoreBOM", "decode"];

    private readonly Encoding _encoding;
    private readonly string _encodingName;
    private readonly bool _fatal;
    private readonly bool _ignoreBOM;

    /// <summary>
    /// The encoding name used by this TextDecoder.
    /// </summary>
    public string EncodingName => _encodingName;

    /// <summary>
    /// Whether to throw on decoding errors.
    /// </summary>
    public bool Fatal => _fatal;

    /// <summary>
    /// Whether to ignore the byte order mark.
    /// </summary>
    public bool IgnoreBOM => _ignoreBOM;

    /// <summary>
    /// Creates a new TextDecoder with the specified encoding.
    /// </summary>
    /// <param name="encoding">The encoding to use (default: "utf-8").</param>
    /// <param name="fatal">Whether to throw on decoding errors.</param>
    /// <param name="ignoreBOM">Whether to ignore the byte order mark.</param>
    public SharpTSTextDecoder(string encoding = "utf-8", bool fatal = false, bool ignoreBOM = false)
    {
        _encodingName = NormalizeEncoding(encoding);
        _fatal = fatal;
        _ignoreBOM = ignoreBOM;
        _encoding = GetEncoding(_encodingName, fatal);
    }

    private static string NormalizeEncoding(string encoding)
    {
        var normalized = encoding.ToLowerInvariant().Trim();
        return normalized switch
        {
            "utf-8" or "utf8" => "utf-8",
            "utf-16le" or "utf16le" or "ucs-2" or "ucs2" => "utf-16le",
            "utf-16be" or "utf16be" => "utf-16be",
            "latin1" or "latin-1" or "iso-8859-1" or "iso8859-1" => "latin1",
            "ascii" => "ascii",
            "windows-1252" or "cp1252" => "windows-1252",
            _ => normalized
        };
    }

    private static Encoding GetEncoding(string name, bool fatal)
    {
        var fallback = fatal
            ? EncoderFallback.ExceptionFallback
            : EncoderFallback.ReplacementFallback;

        var decoderFallback = fatal
            ? DecoderFallback.ExceptionFallback
            : DecoderFallback.ReplacementFallback;

        return name switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: fatal),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: fatal),
            "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: fatal),
            "latin1" => Encoding.Latin1,
            "ascii" => Encoding.ASCII,
            "windows-1252" => Encoding.GetEncoding(1252),
            _ => throw new Exception($"The encoding label provided ('{name}') is invalid.")
        };
    }

    /// <summary>
    /// Decodes a byte array into a string.
    /// </summary>
    /// <param name="input">The bytes to decode (Buffer or array-like).</param>
    /// <param name="stream">Whether to continue decoding across multiple calls (not fully supported).</param>
    /// <returns>The decoded string.</returns>
    public string Decode(byte[]? input = null, bool stream = false)
    {
        if (input == null || input.Length == 0)
            return "";

        try
        {
            var result = _encoding.GetString(input);

            // Handle BOM stripping if not ignoreBOM
            if (!_ignoreBOM && result.Length > 0 && result[0] == '\uFEFF')
            {
                result = result[1..];
            }

            return result;
        }
        catch (DecoderFallbackException ex)
        {
            throw new Exception($"The encoded data was not valid for encoding {_encodingName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a property value by name (ISharpTSPropertyAccessor).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "encoding" => EncodingName,
            "fatal" => Fatal,
            "ignoreBOM" => IgnoreBOM,
            "decode" => new TextDecoderDecodeMethod(this),
            _ => null
        };
    }

    /// <summary>
    /// Callable wrapper for TextDecoder.decode that works in both interpreter and compiled modes.
    /// Implements ISharpTSCallable for interpreter mode and has Invoke for compiled mode.
    /// </summary>
    public class TextDecoderDecodeMethod : ISharpTSCallable
    {
        private readonly SharpTSTextDecoder _decoder;

        public TextDecoderDecodeMethod(SharpTSTextDecoder decoder)
        {
            _decoder = decoder;
        }

        public int Arity() => 0;

        /// <summary>
        /// Called by interpreter mode via ISharpTSCallable.
        /// </summary>
        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            return DecodeFromArgs(arguments.ToArray());
        }

        /// <summary>
        /// Called by compiled mode via reflection (InvokeValue finds this method).
        /// </summary>
        public object? Invoke(params object?[] args)
        {
            return DecodeFromArgs(args);
        }

        private string DecodeFromArgs(object?[] args)
        {
            byte[]? bytes = null;
            bool stream = false;

            if (args.Length > 0 && args[0] != null)
            {
                bytes = args[0] switch
                {
                    SharpTSBuffer buf => buf.Data,
                    byte[] arr => arr,
                    SharpTSArray tsArr => tsArr.Elements.Select(e => e switch
                    {
                        double d => (byte)(int)d,
                        int i => (byte)i,
                        _ => (byte)0
                    }).ToArray(),
                    _ => throw new Exception($"TextDecoder.decode: first argument must be a BufferSource, got {args[0]?.GetType().Name ?? "null"}")
                };
            }

            if (args.Length > 1 && args[1] is SharpTSObject options)
            {
                var streamVal = options.GetProperty("stream");
                stream = streamVal is true;
            }

            return _decoder.Decode(bytes, stream);
        }

        public override string ToString() => "[Function: decode]";
    }

    /// <summary>
    /// Sets a property value by name (ISharpTSPropertyAccessor).
    /// TextDecoder properties are read-only, so this is a no-op.
    /// </summary>
    public void SetProperty(string name, object? value)
    {
        // TextDecoder is immutable, ignore property sets
    }

    /// <summary>
    /// Checks if a property exists (ISharpTSPropertyAccessor).
    /// </summary>
    public bool HasProperty(string name) => name is "encoding" or "fatal" or "ignoreBOM" or "decode";

    /// <summary>
    /// Gets all property names for iteration (ISharpTSPropertyAccessor).
    /// </summary>
    public IEnumerable<string> PropertyNames => _propertyNames;

    public override string ToString() => "[object TextDecoder]";
}
