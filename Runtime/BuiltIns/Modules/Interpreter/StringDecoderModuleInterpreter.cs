using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'string_decoder' module.
/// </summary>
/// <remarks>
/// Provides the StringDecoder class for decoding Buffer objects into strings
/// while properly handling multi-byte character sequences.
/// </remarks>
public static class StringDecoderModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the string_decoder module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["StringDecoder"] = SharpTSStringDecoderConstructor.Instance
        };
    }
}

/// <summary>
/// Constructor for StringDecoder class.
/// </summary>
public sealed class SharpTSStringDecoderConstructor : ISharpTSCallable
{
    public static readonly SharpTSStringDecoderConstructor Instance = new();

    private SharpTSStringDecoderConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// StringDecoder constructor takes 0-1 argument (optional encoding).
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new StringDecoder instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> args)
    {
        string encoding = "utf8";
        if (args.Count > 0 && args[0] is string enc)
        {
            encoding = enc.ToLowerInvariant();
        }

        return new SharpTSStringDecoder(encoding);
    }

    public override string ToString() => "[Function: StringDecoder]";
}

/// <summary>
/// Runtime representation of a Node.js StringDecoder instance.
/// </summary>
/// <remarks>
/// Handles multi-byte character sequences across Buffer chunks.
/// Supports utf8, utf-8, utf16le, utf-16le, ucs2, ucs-2, base64, latin1, binary, hex encodings.
/// </remarks>
public sealed class SharpTSStringDecoder
{
    private readonly string _encoding;
    private byte[] _pendingBytes;
    private readonly System.Text.Encoding _textEncoding;

    public SharpTSStringDecoder(string encoding)
    {
        // Normalize encoding names
        _encoding = NormalizeEncoding(encoding);
        _pendingBytes = [];
        _textEncoding = GetTextEncoding(_encoding);
    }

    /// <summary>
    /// Gets the encoding this decoder uses.
    /// </summary>
    public string Encoding => _encoding;

    /// <summary>
    /// Decodes a Buffer and returns the decoded string.
    /// Handles partial multi-byte sequences by storing them for the next write.
    /// </summary>
    public string Write(SharpTSBuffer buffer)
    {
        return Write(buffer.Data);
    }

    /// <summary>
    /// Decodes a byte array and returns the decoded string.
    /// </summary>
    public string Write(byte[] bytes)
    {
        // Combine pending bytes with new bytes
        byte[] combined;
        if (_pendingBytes.Length > 0)
        {
            combined = new byte[_pendingBytes.Length + bytes.Length];
            Array.Copy(_pendingBytes, 0, combined, 0, _pendingBytes.Length);
            Array.Copy(bytes, 0, combined, _pendingBytes.Length, bytes.Length);
        }
        else
        {
            combined = bytes;
        }

        if (_encoding == "utf8")
        {
            // For UTF-8, we need to handle partial sequences
            int validEnd = FindValidUtf8End(combined);
            if (validEnd < combined.Length)
            {
                // Store incomplete sequence for next call
                _pendingBytes = new byte[combined.Length - validEnd];
                Array.Copy(combined, validEnd, _pendingBytes, 0, _pendingBytes.Length);
                combined = combined[..validEnd];
            }
            else
            {
                _pendingBytes = [];
            }
        }
        else if (_encoding == "utf16le")
        {
            // UTF-16 requires pairs of bytes
            if (combined.Length % 2 != 0)
            {
                _pendingBytes = [combined[^1]];
                combined = combined[..^1];
            }
            else
            {
                _pendingBytes = [];
            }
        }
        else
        {
            // For single-byte encodings, no partial sequences
            _pendingBytes = [];
        }

        if (combined.Length == 0)
            return string.Empty;

        return _textEncoding.GetString(combined);
    }

    /// <summary>
    /// Returns any remaining input stored in the internal buffer as a string.
    /// </summary>
    public string End()
    {
        if (_pendingBytes.Length == 0)
            return string.Empty;

        // Decode whatever is left, even if incomplete
        var result = _textEncoding.GetString(_pendingBytes);
        _pendingBytes = [];
        return result;
    }

    /// <summary>
    /// Returns any remaining input stored in the internal buffer as a string,
    /// after appending the provided buffer.
    /// </summary>
    public string End(SharpTSBuffer buffer)
    {
        return Write(buffer) + End();
    }

    /// <summary>
    /// Finds the end of valid UTF-8 sequences, leaving incomplete sequences at the end.
    /// </summary>
    private static int FindValidUtf8End(byte[] bytes)
    {
        if (bytes.Length == 0)
            return 0;

        // Check the last few bytes for incomplete multi-byte sequences
        // UTF-8 sequences are 1-4 bytes
        for (int i = Math.Min(3, bytes.Length - 1); i >= 0; i--)
        {
            int pos = bytes.Length - 1 - i;
            byte b = bytes[pos];

            if ((b & 0x80) == 0)
            {
                // Single byte character (0xxxxxxx)
                return bytes.Length;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                // Start of 2-byte sequence (110xxxxx)
                // Need 1 more byte
                if (bytes.Length - pos >= 2)
                    return bytes.Length;
                return pos;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                // Start of 3-byte sequence (1110xxxx)
                // Need 2 more bytes
                if (bytes.Length - pos >= 3)
                    return bytes.Length;
                return pos;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                // Start of 4-byte sequence (11110xxx)
                // Need 3 more bytes
                if (bytes.Length - pos >= 4)
                    return bytes.Length;
                return pos;
            }
            else if ((b & 0xC0) == 0x80)
            {
                // Continuation byte (10xxxxxx) - keep looking for start
                continue;
            }
        }

        return bytes.Length;
    }

    private static string NormalizeEncoding(string encoding)
    {
        return encoding.ToLowerInvariant().Replace("-", "") switch
        {
            "utf8" => "utf8",
            "utf16le" or "ucs2" => "utf16le",
            "latin1" or "binary" => "latin1",
            "base64" => "base64",
            "hex" => "hex",
            "ascii" => "ascii",
            _ => "utf8" // Default to utf8
        };
    }

    private static System.Text.Encoding GetTextEncoding(string encoding)
    {
        return encoding switch
        {
            "utf8" => System.Text.Encoding.UTF8,
            "utf16le" => System.Text.Encoding.Unicode,
            "latin1" => System.Text.Encoding.Latin1,
            "ascii" => System.Text.Encoding.ASCII,
            _ => System.Text.Encoding.UTF8
        };
    }

    /// <summary>
    /// Gets a member (method or property) from the StringDecoder instance.
    /// </summary>
    public static object? GetMember(SharpTSStringDecoder decoder, string name)
    {
        return name switch
        {
            "encoding" => decoder.Encoding,
            "write" => new BuiltInMethod("write", 1, 1, (_, receiver, args) =>
            {
                if (receiver is not SharpTSStringDecoder dec)
                    throw new Exception("Runtime Error: write called on non-StringDecoder");

                if (args.Count == 0)
                    return string.Empty;

                if (args[0] is SharpTSBuffer buffer)
                    return dec.Write(buffer);

                if (args[0] is string str)
                    return str; // Passthrough for strings

                throw new Exception("Runtime Error: StringDecoder.write requires a Buffer argument");
            }),
            "end" => new BuiltInMethod("end", 0, 1, (_, receiver, args) =>
            {
                if (receiver is not SharpTSStringDecoder dec)
                    throw new Exception("Runtime Error: end called on non-StringDecoder");

                if (args.Count > 0 && args[0] is SharpTSBuffer buffer)
                    return dec.End(buffer);

                return dec.End();
            }),
            "text" => new BuiltInMethod("text", 1, 2, (_, receiver, args) =>
            {
                // Alias for write + end behavior
                if (receiver is not SharpTSStringDecoder dec)
                    throw new Exception("Runtime Error: text called on non-StringDecoder");

                if (args.Count == 0)
                    return string.Empty;

                if (args[0] is SharpTSBuffer buffer)
                {
                    int end = buffer.Length;
                    if (args.Count > 1 && args[1] is double endArg)
                        end = (int)endArg;

                    var bytes = buffer.Data[..end];
                    return dec.Write(bytes);
                }

                throw new Exception("Runtime Error: StringDecoder.text requires a Buffer argument");
            }),
            _ => null
        };
    }
}
