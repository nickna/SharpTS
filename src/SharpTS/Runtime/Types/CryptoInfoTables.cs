namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared static data for the Node <c>crypto</c> module: <c>crypto.constants</c>,
/// the <c>getCipherInfo</c> table, and the supported-curve list (epic #1054).
/// </summary>
/// <remarks>
/// Single source of truth for BOTH modes: the interpreter builds runtime objects
/// from these tables, and the IL emitters bake the same values into the compiled
/// output at compile time (the tables are read while emitting, so the standalone
/// DLL carries the data with no SharpTS.dll reference).
/// </remarks>
public static class CryptoInfoTables
{
    /// <summary>Numeric entries of <c>crypto.constants</c> (Node 24 / OpenSSL 3 values).</summary>
    public static readonly (string Name, double Value)[] NumericConstants =
    [
        // OpenSSL version placeholder (BCL crypto is not OpenSSL-backed; 0 = unknown)
        ("OPENSSL_VERSION_NUMBER", 0),

        // SSL/TLS operation flags
        ("SSL_OP_ALL", 0x80000BF4),
        ("SSL_OP_ALLOW_NO_DHE_KEX", 0x00000400),
        ("SSL_OP_ALLOW_UNSAFE_LEGACY_RENEGOTIATION", 0x00040000),
        ("SSL_OP_CIPHER_SERVER_PREFERENCE", 0x00400000),
        ("SSL_OP_CISCO_ANYCONNECT", 0x00008000),
        ("SSL_OP_COOKIE_EXCHANGE", 0x00002000),
        ("SSL_OP_CRYPTOPRO_TLSEXT_BUG", 0x80000000),
        ("SSL_OP_DONT_INSERT_EMPTY_FRAGMENTS", 0x00000800),
        ("SSL_OP_LEGACY_SERVER_CONNECT", 0x00000004),
        ("SSL_OP_NO_COMPRESSION", 0x00020000),
        ("SSL_OP_NO_ENCRYPT_THEN_MAC", 0x00080000),
        ("SSL_OP_NO_QUERY_MTU", 0x00001000),
        ("SSL_OP_NO_RENEGOTIATION", 0x40000000),
        ("SSL_OP_NO_SESSION_RESUMPTION_ON_RENEGOTIATION", 0x00010000),
        ("SSL_OP_NO_SSLv2", 0x00000000),
        ("SSL_OP_NO_SSLv3", 0x02000000),
        ("SSL_OP_NO_TICKET", 0x00004000),
        ("SSL_OP_NO_TLSv1", 0x04000000),
        ("SSL_OP_NO_TLSv1_1", 0x10000000),
        ("SSL_OP_NO_TLSv1_2", 0x08000000),
        ("SSL_OP_NO_TLSv1_3", 0x20000000),
        ("SSL_OP_PRIORITIZE_CHACHA", 0x00200000),
        ("SSL_OP_TLS_ROLLBACK_BUG", 0x00800000),

        // Engine methods
        ("ENGINE_METHOD_RSA", 0x0001),
        ("ENGINE_METHOD_DSA", 0x0002),
        ("ENGINE_METHOD_DH", 0x0004),
        ("ENGINE_METHOD_RAND", 0x0008),
        ("ENGINE_METHOD_EC", 0x0800),
        ("ENGINE_METHOD_CIPHERS", 0x0040),
        ("ENGINE_METHOD_DIGESTS", 0x0080),
        ("ENGINE_METHOD_PKEY_METHS", 0x0200),
        ("ENGINE_METHOD_PKEY_ASN1_METHS", 0x0400),
        ("ENGINE_METHOD_ALL", 0xFFFF),
        ("ENGINE_METHOD_NONE", 0x0000),

        // DH check codes
        ("DH_CHECK_P_NOT_SAFE_PRIME", 2),
        ("DH_CHECK_P_NOT_PRIME", 1),
        ("DH_UNABLE_TO_CHECK_GENERATOR", 4),
        ("DH_NOT_SUITABLE_GENERATOR", 8),

        // RSA padding modes
        ("RSA_PKCS1_PADDING", 1),
        ("RSA_SSLV23_PADDING", 2),
        ("RSA_NO_PADDING", 3),
        ("RSA_PKCS1_OAEP_PADDING", 4),
        ("RSA_X931_PADDING", 5),
        ("RSA_PKCS1_PSS_PADDING", 6),

        // PSS salt length selectors
        ("RSA_PSS_SALTLEN_DIGEST", -1),
        ("RSA_PSS_SALTLEN_MAX_SIGN", -2),
        ("RSA_PSS_SALTLEN_AUTO", -2),

        // EC point conversion forms
        ("POINT_CONVERSION_COMPRESSED", 2),
        ("POINT_CONVERSION_UNCOMPRESSED", 4),
        ("POINT_CONVERSION_HYBRID", 6),

        // TLS protocol version numbers
        ("TLS1_VERSION", 0x301),
        ("TLS1_1_VERSION", 0x302),
        ("TLS1_2_VERSION", 0x303),
        ("TLS1_3_VERSION", 0x304),
    ];

    /// <summary>Node's default TLS cipher list (crypto.constants.defaultCoreCipherList).</summary>
    public const string DefaultCoreCipherList =
        "TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_128_GCM_SHA256:" +
        "ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:" +
        "ECDHE-ECDSA-AES256-GCM-SHA384:DHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-SHA256:" +
        "DHE-RSA-AES128-SHA256:ECDHE-RSA-AES256-SHA384:DHE-RSA-AES256-SHA384:" +
        "ECDHE-RSA-AES256-SHA256:DHE-RSA-AES256-SHA256:HIGH:!aNULL:!eNULL:!EXPORT:!DES:!RC4:!MD5:!PSK:!SRP:!CAMELLIA";

    /// <summary>String entries of <c>crypto.constants</c>.</summary>
    public static readonly (string Name, string Value)[] StringConstants =
    [
        ("defaultCoreCipherList", DefaultCoreCipherList),
        ("defaultCipherList", DefaultCoreCipherList),
    ];

    /// <summary>
    /// One row of the <c>getCipherInfo</c> table.
    /// </summary>
    public readonly record struct CipherInfo(string Name, int Nid, int BlockSize, int IvLength, int KeyLength, string Mode);

    /// <summary>
    /// The ciphers SharpTS actually implements (AES-CBC/GCM), with their OpenSSL NIDs.
    /// Keep in sync with <c>getCiphers()</c> and the Cipher/Decipher implementations.
    /// </summary>
    public static readonly CipherInfo[] CipherInfos =
    [
        new("aes-128-cbc", 419, 16, 16, 16, "cbc"),
        new("aes-192-cbc", 423, 16, 16, 24, "cbc"),
        new("aes-256-cbc", 427, 16, 16, 32, "cbc"),
        new("aes-128-gcm", 895, 1, 12, 16, "gcm"),
        new("aes-192-gcm", 898, 1, 12, 24, "gcm"),
        new("aes-256-gcm", 901, 1, 12, 32, "gcm"),
    ];

    /// <summary>
    /// Curve names reported by <c>crypto.getCurves()</c> — the NIST curves the
    /// BCL supports everywhere (matching createECDH / generateKeyPair('ec')).
    /// </summary>
    public static readonly string[] SupportedCurves = ["prime256v1", "secp384r1", "secp521r1"];
}
