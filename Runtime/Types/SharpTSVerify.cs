using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Verify object for signature verification.
/// </summary>
/// <remarks>
/// Accumulates data and delegates verification to
/// <see cref="CryptoKeyUtil.VerifyData"/>, sharing the key/options/padding core
/// with Sign and the one-shot <c>crypto.verify()</c> (#1055):
/// - verify.update(data) - adds data to be verified
/// - verify.verify(publicKey, signature[, signatureEncoding]) - verifies; the key
///   may be a PEM string, KeyObject, or options object with
///   { key, padding, saltLength, dsaEncoding }
/// </remarks>
public class SharpTSVerify
{
    private readonly string _algorithm;
    private readonly List<byte> _data = new();
    private bool _finalized;

    /// <summary>
    /// Creates a new Verify object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: sha1, sha256, sha384, sha512, or RSA-SHA256 style names</param>
    public SharpTSVerify(string algorithm)
    {
        // Validate eagerly so a bad algorithm fails at createVerify (Node behavior).
        CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "verification");
        _algorithm = algorithm;
        _finalized = false;
    }

    /// <summary>
    /// Updates the verifier with the given data.
    /// </summary>
    public SharpTSVerify Update(string data)
    {
        return Update(Encoding.UTF8.GetBytes(data));
    }

    /// <summary>
    /// Updates the verifier with binary data.
    /// </summary>
    public SharpTSVerify Update(byte[] data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Verify after verify() has been called");

        _data.AddRange(data);
        return this;
    }

    /// <summary>
    /// Verifies the signature against the accumulated data.
    /// </summary>
    /// <param name="key">The public key argument (PEM string, KeyObject, or options object).</param>
    /// <param name="signature">The signature (Buffer, or string in the given encoding).</param>
    /// <param name="signatureEncoding">Encoding of a string signature: "hex" or "base64".</param>
    public bool Verify(object key, object signature, string? signatureEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("verify() has already been called");

        _finalized = true;
        var signatureBytes = CryptoEncoding.FromEncoded(signature, signatureEncoding);
        return CryptoKeyUtil.VerifyData(_algorithm, _data.ToArray(), key, signatureBytes, "verify");
    }

    /// <summary>
    /// Gets a member of this Verify object (for property access).
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
            "verify" => BuiltInMethod.CreateV2("verify", 2, 3, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new ArgumentException("verify() requires public key and signature arguments");

                var signatureEncoding = args.Length > 2 ? args[2].ToObject()?.ToString() : null;
                var key = args[0].ToObject() ?? throw new ArgumentException("verify() key must not be null");
                return RuntimeValue.FromBoxed(Verify(key, args[1].ToObject()!, signatureEncoding));
            }),
            _ => null
        };
    }
}
