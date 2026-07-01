using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Decipher object for symmetric decryption.
/// </summary>
/// <remarks>
/// Supports AES decryption in CBC and GCM modes:
/// - decipher.update(data, inputEncoding?, outputEncoding?) - decrypts data
/// - decipher.final(outputEncoding?) - finalizes decryption
/// - decipher.setAutoPadding(autoPadding) - sets padding mode (CBC only)
/// - decipher.setAuthTag(buffer) - sets auth tag for verification (GCM only)
/// - decipher.setAAD(buffer) - sets additional authenticated data (GCM only)
///
/// The shared encrypt/decrypt machinery lives in <see cref="SharpTSAesBase"/>.
/// </remarks>
public class SharpTSDecipher : SharpTSAesBase
{
    /// <summary>
    /// Creates a new Decipher object for the specified algorithm.
    /// </summary>
    /// <param name="algorithm">Algorithm name: aes-128-cbc, aes-192-cbc, aes-256-cbc, aes-128-gcm, aes-192-gcm, aes-256-gcm</param>
    /// <param name="key">Decryption key as byte array</param>
    /// <param name="iv">Initialization vector as byte array</param>
    public SharpTSDecipher(string algorithm, byte[] key, byte[] iv)
        : base(algorithm, key, iv, forEncryption: false)
    {
    }

    protected override string Noun => "Decipher";

    // CBC decryption must keep the last block buffered so final() can strip padding.
    protected override bool ReserveFinalBlock => true;

    // Decrypted plaintext may be rendered as a UTF-8 string.
    protected override bool DecodeOutputAsUtf8 => true;

    protected override byte[] FinalizeGcm()
    {
        if (_authTag == null)
            throw new InvalidOperationException("Authentication tag must be set using setAuthTag() before calling final() for GCM mode");

        var ciphertext = _gcmBuffer.ToArray();
        var plaintext = new byte[ciphertext.Length];

        try
        {
            if (_aad != null)
                _aesGcm!.Decrypt(_iv, ciphertext, _authTag, plaintext, _aad);
            else
                _aesGcm!.Decrypt(_iv, ciphertext, _authTag, plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new CryptographicException("Unsupported state or unable to authenticate data");
        }

        return plaintext;
    }

    /// <summary>
    /// Sets whether to use auto-padding (CBC mode only).
    /// </summary>
    /// <returns>This decipher for chaining</returns>
    public SharpTSDecipher SetAutoPadding(bool autoPadding)
    {
        SetAutoPaddingCore(autoPadding);
        return this;
    }

    /// <summary>
    /// Sets the authentication tag for verification (GCM mode only, before final()).
    /// </summary>
    /// <param name="tag">Authentication tag as Buffer</param>
    /// <returns>This decipher for chaining</returns>
    public SharpTSDecipher SetAuthTag(object tag)
    {
        if (!_isGcm)
            throw new InvalidOperationException("setAuthTag is only available for GCM mode ciphers");

        if (_finalized)
            throw new InvalidOperationException("setAuthTag must be called before final()");

        _authTag = CryptoEncoding.FromEncoded(tag, null);
        return this;
    }

    /// <summary>
    /// Sets additional authenticated data (GCM mode only, before final()).
    /// </summary>
    /// <returns>This decipher for chaining</returns>
    public SharpTSDecipher SetAAD(object aad)
    {
        SetAADCore(aad);
        return this;
    }

    /// <summary>
    /// Gets a member of this decipher object (for property access).
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "setAuthTag" => BuiltInMethod.CreateV2("setAuthTag", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("decipher.setAuthTag requires tag argument");
                return RuntimeValue.FromObject(SetAuthTag(args[0].ToObject()!));
            }),
            _ => base.GetMember(name)
        };
    }
}
