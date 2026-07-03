using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Sign object for cryptographic signing.
/// </summary>
/// <remarks>
/// Accumulates data and delegates the final signature to
/// <see cref="CryptoKeyUtil.SignData"/>, so the streaming form and the one-shot
/// <c>crypto.sign()</c> (#1055) share one key/options/padding core:
/// - sign.update(data) - adds data to be signed
/// - sign.sign(privateKey[, encoding]) - signs; the key may be a PEM string,
///   KeyObject, or options object with { key, padding, saltLength, dsaEncoding }
/// </remarks>
public class SharpTSSign
{
    private readonly string _algorithm;
    private readonly List<byte> _data = new();
    private bool _finalized;

    /// <summary>
    /// Creates a new Sign object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: sha1, sha256, sha384, sha512, or RSA-SHA256 style names</param>
    public SharpTSSign(string algorithm)
    {
        // Validate eagerly so a bad algorithm fails at createSign (Node behavior).
        CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "signing");
        _algorithm = algorithm;
        _finalized = false;
    }

    /// <summary>
    /// Updates the signer with the given data.
    /// </summary>
    /// <param name="data">The data to add for signing.</param>
    /// <returns>This Sign object for chaining.</returns>
    public SharpTSSign Update(string data)
    {
        return Update(Encoding.UTF8.GetBytes(data));
    }

    /// <summary>
    /// Updates the signer with binary data.
    /// </summary>
    public SharpTSSign Update(byte[] data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Sign after sign() has been called");

        _data.AddRange(data);
        return this;
    }

    /// <summary>
    /// Signs the accumulated data using the provided private key (PEM string,
    /// KeyObject, or options object).
    /// </summary>
    /// <param name="key">The private key argument.</param>
    /// <param name="encoding">Output encoding: "hex", "base64", or null for Buffer</param>
    /// <returns>The signature as a string or Buffer.</returns>
    public object Sign(object key, string? encoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("sign() has already been called");

        _finalized = true;
        var signature = CryptoKeyUtil.SignData(_algorithm, _data.ToArray(), key, "sign");
        return CryptoEncoding.ToBufferOrString(signature, encoding);
    }

    /// <summary>
    /// Gets a member of this Sign object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => BuiltInMethod.CreateV2("update", 1, (_, _, args) =>
            {
                if (args.Length > 0)
                {
                    if (args[0].IsString)
                        return RuntimeValue.FromBoxed(Update(args[0].AsStringUnsafe()));
                    if (args[0].ToObject() is SharpTSBuffer buf)
                        return RuntimeValue.FromBoxed(Update(buf.Data));
                }
                return RuntimeValue.FromObject(this);
            }),
            "sign" => BuiltInMethod.CreateV2("sign", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("sign() requires a private key argument");

                var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                var key = args[0].ToObject() ?? throw new ArgumentException("sign() key must not be null");
                return RuntimeValue.FromBoxed(Sign(key, encoding));
            }),
            _ => null
        };
    }
}
