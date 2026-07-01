using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Cipher object for symmetric encryption.
/// </summary>
/// <remarks>
/// Supports AES encryption in CBC and GCM modes:
/// - cipher.update(data, inputEncoding?, outputEncoding?) - encrypts data
/// - cipher.final(outputEncoding?) - finalizes encryption
/// - cipher.setAutoPadding(autoPadding) - sets padding mode (CBC only)
/// - cipher.getAuthTag() - returns auth tag (GCM only)
/// - cipher.setAAD(buffer) - sets additional authenticated data (GCM only)
///
/// The shared encrypt/decrypt machinery lives in <see cref="SharpTSAesBase"/>.
/// </remarks>
public class SharpTSCipher : SharpTSAesBase
{
    /// <summary>
    /// Creates a new Cipher object for the specified algorithm.
    /// </summary>
    /// <param name="algorithm">Algorithm name: aes-128-cbc, aes-192-cbc, aes-256-cbc, aes-128-gcm, aes-192-gcm, aes-256-gcm</param>
    /// <param name="key">Encryption key as byte array</param>
    /// <param name="iv">Initialization vector as byte array</param>
    public SharpTSCipher(string algorithm, byte[] key, byte[] iv)
        : base(algorithm, key, iv, forEncryption: true)
    {
    }

    protected override string Noun => "Cipher";

    // Encryption processes every complete block immediately.
    protected override bool ReserveFinalBlock => false;

    // Ciphertext is never meaningfully decoded as UTF-8.
    protected override bool DecodeOutputAsUtf8 => false;

    protected override byte[] FinalizeGcm()
    {
        var plaintext = _gcmBuffer.ToArray();
        var ciphertext = new byte[plaintext.Length];
        _authTag = new byte[AesGcm.TagByteSizes.MaxSize];

        if (_aad != null)
            _aesGcm!.Encrypt(_iv, plaintext, ciphertext, _authTag, _aad);
        else
            _aesGcm!.Encrypt(_iv, plaintext, ciphertext, _authTag);

        return ciphertext;
    }

    /// <summary>
    /// Sets whether to use auto-padding (CBC mode only).
    /// </summary>
    /// <returns>This cipher for chaining</returns>
    public SharpTSCipher SetAutoPadding(bool autoPadding)
    {
        SetAutoPaddingCore(autoPadding);
        return this;
    }

    /// <summary>
    /// Gets the authentication tag (GCM mode only, after final()).
    /// </summary>
    /// <returns>Authentication tag as Buffer</returns>
    public SharpTSBuffer GetAuthTag()
    {
        if (!_isGcm)
            throw new InvalidOperationException("getAuthTag is only available for GCM mode ciphers");

        if (!_finalized || _authTag == null)
            throw new InvalidOperationException("getAuthTag must be called after final()");

        return new SharpTSBuffer(_authTag);
    }

    /// <summary>
    /// Sets additional authenticated data (GCM mode only, before final()).
    /// </summary>
    /// <returns>This cipher for chaining</returns>
    public SharpTSCipher SetAAD(object aad)
    {
        SetAADCore(aad);
        return this;
    }

    /// <summary>
    /// Gets a member of this cipher object (for property access).
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "getAuthTag" => BuiltInMethod.CreateV2("getAuthTag", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(GetAuthTag());
            }),
            _ => base.GetMember(name)
        };
    }
}
