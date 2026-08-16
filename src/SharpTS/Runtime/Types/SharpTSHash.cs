using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Hash object for cryptographic hashing.
/// </summary>
/// <remarks>
/// Accumulates updated data and computes the digest one-shot when
/// <c>digest()</c> is called. Buffering (rather than an incremental hash)
/// is what makes <c>hash.copy()</c> (#1058) and the XOF hashes with a
/// caller-chosen <c>outputLength</c> (#1062) expressible on the BCL:
/// - hash.update(data[, inputEncoding]) - adds data to the hash
/// - hash.digest(encoding?) - returns the hash value
/// - hash.copy(options?) - clones the hash's mid-stream state
/// NOTE: Must stay in sync with the emitted $Hash (Compilation/RuntimeEmitter.TSHash.cs).
/// </remarks>
public class SharpTSHash
{
    private readonly string _algorithm;
    private readonly int _outputLength;
    private readonly MemoryStream _data = new();
    private bool _finalized;

    /// <summary>
    /// Creates a new hash object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: md5, sha1, sha256/384/512, sha3-256/384/512, shake128/256</param>
    /// <param name="outputLength">Digest length in bytes for XOF hashes (shake128/shake256); -1 = algorithm default.</param>
    public SharpTSHash(string algorithm, int outputLength = -1)
    {
        _algorithm = CryptoAlgorithms.ValidateHashName(algorithm, context: "hash");
        _outputLength = outputLength;
        _finalized = false;
    }

    /// <summary>Clone constructor backing <c>hash.copy()</c>.</summary>
    private SharpTSHash(SharpTSHash other, int outputLength)
    {
        _algorithm = other._algorithm;
        _outputLength = outputLength;
        other._data.WriteTo(_data);
        _finalized = false;
    }

    /// <summary>
    /// Updates the hash with the given data.
    /// </summary>
    /// <param name="data">The data to add to the hash.</param>
    /// <returns>This hash object for chaining.</returns>
    public SharpTSHash Update(string data)
    {
        return Update(System.Text.Encoding.UTF8.GetBytes(data));
    }

    /// <summary>
    /// Updates the hash with binary data.
    /// </summary>
    public SharpTSHash Update(byte[] data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update hash after digest() has been called");

        _data.Write(data, 0, data.Length);
        return this;
    }

    /// <summary>
    /// Finalizes the hash and returns the digest.
    /// </summary>
    /// <param name="encoding">The output encoding: "hex", "base64", "base64url", or null for a Buffer.</param>
    /// <returns>The hash digest as a string or Buffer.</returns>
    public object Digest(string? encoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("digest() has already been called");

        _finalized = true;
        var hashBytes = CryptoAlgorithms.OneShotHash(_algorithm, _data.ToArray(), _outputLength);

        return CryptoEncoding.ToBufferOrString(hashBytes, encoding);
    }

    /// <summary>
    /// Clones the hash's current (mid-stream) state — Node's <c>hash.copy([options])</c>.
    /// </summary>
    /// <param name="outputLength">Optional XOF output length for the clone; -1 inherits this hash's.</param>
    public SharpTSHash Copy(int outputLength = -1)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot copy hash after digest() has been called");

        return new SharpTSHash(this, outputLength >= 0 ? outputLength : _outputLength);
    }

    /// <summary>
    /// Gets a member of this hash object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => BuiltInMethod.CreateV2("update", 1, 2, (_, _, args) =>
            {
                if (args.Length > 0)
                {
                    if (args[0].IsString)
                    {
                        var inputEncoding = args.Length > 1 ? args[1].ToObject() as string : null;
                        return RuntimeValue.FromObject(Update(CryptoEncoding.FromEncoded(args[0].AsStringUnsafe(), inputEncoding)));
                    }
                    if (args[0].ToObject() is SharpTSBuffer buf)
                        return RuntimeValue.FromObject(Update(buf.Data));
                }
                return RuntimeValue.FromObject(this);
            }),
            "digest" => BuiltInMethod.CreateV2("digest", 0, 1, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(Digest(encoding));
            }),
            "copy" => BuiltInMethod.CreateV2("copy", 0, 1, (_, _, args) =>
            {
                int outputLength = -1;
                if (args.Length > 0 && args[0].ToObject() is SharpTSObject opts &&
                    opts.Fields.TryGetValue("outputLength", out var ol) && ol is double d)
                    outputLength = (int)d;
                return RuntimeValue.FromObject(Copy(outputLength));
            }),
            _ => null
        };
    }
}
