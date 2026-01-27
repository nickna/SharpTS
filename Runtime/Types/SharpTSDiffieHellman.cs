using System.Numerics;
using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible DiffieHellman object for key exchange.
/// </summary>
/// <remarks>
/// Provides Diffie-Hellman key exchange functionality:
/// - generateKeys() - generates public/private key pair
/// - computeSecret() - computes shared secret with another party's public key
/// - getPrime() - returns the prime number
/// - getGenerator() - returns the generator
/// - getPublicKey() / getPrivateKey() - returns the keys
/// - setPublicKey() / setPrivateKey() - sets the keys (not allowed for predefined groups)
/// </remarks>
public class SharpTSDiffieHellman
{
    private readonly BigInteger _prime;
    private readonly BigInteger _generator;
    private BigInteger? _privateKey;
    private BigInteger? _publicKey;
    private readonly bool _isGroup;

    #region Predefined Group Primes (RFC 2409, RFC 3526)

    // MODP 1 - 768 bits (RFC 2409)
    private static readonly byte[] Modp1Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A63A3620FFFFFFFFFFFFFFFF");

    // MODP 2 - 1024 bits (RFC 2409)
    private static readonly byte[] Modp2Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381" +
        "FFFFFFFFFFFFFFFF");

    // MODP 5 - 1536 bits (RFC 3526)
    private static readonly byte[] Modp5Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA237327FFFFFFFFFFFFFFFF");

    // MODP 14 - 2048 bits (RFC 3526)
    private static readonly byte[] Modp14Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AACAA68FFFFFFFFFFFFFFFF");

    // MODP 15 - 3072 bits (RFC 3526)
    private static readonly byte[] Modp15Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AAAC42DAD33170D04507A33A85521ABDF1CBA64" +
        "ECFB850458DBEF0A8AEA71575D060C7DB3970F85A6E1E4C7" +
        "ABF5AE8CDB0933D71E8C94E04A25619DCEE3D2261AD2EE6B" +
        "F12FFA06D98A0864D87602733EC86A64521F2B18177B200C" +
        "BBE117577A615D6C770988C0BAD946E208E24FA074E5AB31" +
        "43DB5BFCE0FD108E4B82D120A93AD2CAFFFFFFFFFFFFFFFF");

    // MODP 16 - 4096 bits (RFC 3526)
    private static readonly byte[] Modp16Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AAAC42DAD33170D04507A33A85521ABDF1CBA64" +
        "ECFB850458DBEF0A8AEA71575D060C7DB3970F85A6E1E4C7" +
        "ABF5AE8CDB0933D71E8C94E04A25619DCEE3D2261AD2EE6B" +
        "F12FFA06D98A0864D87602733EC86A64521F2B18177B200C" +
        "BBE117577A615D6C770988C0BAD946E208E24FA074E5AB31" +
        "43DB5BFCE0FD108E4B82D120A92108011A723C12A787E6D7" +
        "88719A10BDBA5B2699C327186AF4E23C1A946834B6150BDA" +
        "2583E9CA2AD44CE8DBBBC2DB04DE8EF92E8EFC141FBECAA6" +
        "287C59474E6BC05D99B2964FA090C3A2233BA186515BE7ED" +
        "1F612970CEE2D7AFB81BDD762170481CD0069127D5B05AA9" +
        "93B4EA988D8FDDC186FFB7DC90A6C08F4DF435C934063199" +
        "FFFFFFFFFFFFFFFF");

    // MODP 17 - 6144 bits (RFC 3526)
    private static readonly byte[] Modp17Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AAAC42DAD33170D04507A33A85521ABDF1CBA64" +
        "ECFB850458DBEF0A8AEA71575D060C7DB3970F85A6E1E4C7" +
        "ABF5AE8CDB0933D71E8C94E04A25619DCEE3D2261AD2EE6B" +
        "F12FFA06D98A0864D87602733EC86A64521F2B18177B200C" +
        "BBE117577A615D6C770988C0BAD946E208E24FA074E5AB31" +
        "43DB5BFCE0FD108E4B82D120A92108011A723C12A787E6D7" +
        "88719A10BDBA5B2699C327186AF4E23C1A946834B6150BDA" +
        "2583E9CA2AD44CE8DBBBC2DB04DE8EF92E8EFC141FBECAA6" +
        "287C59474E6BC05D99B2964FA090C3A2233BA186515BE7ED" +
        "1F612970CEE2D7AFB81BDD762170481CD0069127D5B05AA9" +
        "93B4EA988D8FDDC186FFB7DC90A6C08F4DF435C934028492" +
        "36C3FAB4D27C7026C1D4DCB2602646DEC9751E763DBA37BD" +
        "F8FF9406AD9E530EE5DB382F413001AEB06A53ED9027D831" +
        "179727B0865A8918DA3EDBEBCF9B14ED44CE6CBACED4BB1B" +
        "DB7F1447E6CC254B332051512BD7AF426FB8F401378CD2BF" +
        "5983CA01C64B92ECF032EA15D1721D03F482D7CE6E74FEF6" +
        "D55E702F46980C82B5A84031900B1C9E59E7C97FBEC7E8F3" +
        "23A97A7E36CC88BE0F1D45B7FF585AC54BD407B22B4154AA" +
        "CC8F6D7EBF48E1D814CC5ED20F8037E0A79715EEF29BE328" +
        "06A1D58BB7C5DA76F550AA3D8A1FBFF0EB19CCB1A313D55C" +
        "DA56C9EC2EF29632387FE8D76E3C0468043E8F663F4860EE" +
        "12BF2D5B0B7474D6E694F91E6DCC4024FFFFFFFFFFFFFFFF");

    // MODP 18 - 8192 bits (RFC 3526)
    private static readonly byte[] Modp18Prime = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA18217C32905E462E36CE3B" +
        "E39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9" +
        "DE2BCBF6955817183995497CEA956AE515D2261898FA0510" +
        "15728E5A8AAAC42DAD33170D04507A33A85521ABDF1CBA64" +
        "ECFB850458DBEF0A8AEA71575D060C7DB3970F85A6E1E4C7" +
        "ABF5AE8CDB0933D71E8C94E04A25619DCEE3D2261AD2EE6B" +
        "F12FFA06D98A0864D87602733EC86A64521F2B18177B200C" +
        "BBE117577A615D6C770988C0BAD946E208E24FA074E5AB31" +
        "43DB5BFCE0FD108E4B82D120A92108011A723C12A787E6D7" +
        "88719A10BDBA5B2699C327186AF4E23C1A946834B6150BDA" +
        "2583E9CA2AD44CE8DBBBC2DB04DE8EF92E8EFC141FBECAA6" +
        "287C59474E6BC05D99B2964FA090C3A2233BA186515BE7ED" +
        "1F612970CEE2D7AFB81BDD762170481CD0069127D5B05AA9" +
        "93B4EA988D8FDDC186FFB7DC90A6C08F4DF435C934028492" +
        "36C3FAB4D27C7026C1D4DCB2602646DEC9751E763DBA37BD" +
        "F8FF9406AD9E530EE5DB382F413001AEB06A53ED9027D831" +
        "179727B0865A8918DA3EDBEBCF9B14ED44CE6CBACED4BB1B" +
        "DB7F1447E6CC254B332051512BD7AF426FB8F401378CD2BF" +
        "5983CA01C64B92ECF032EA15D1721D03F482D7CE6E74FEF6" +
        "D55E702F46980C82B5A84031900B1C9E59E7C97FBEC7E8F3" +
        "23A97A7E36CC88BE0F1D45B7FF585AC54BD407B22B4154AA" +
        "CC8F6D7EBF48E1D814CC5ED20F8037E0A79715EEF29BE328" +
        "06A1D58BB7C5DA76F550AA3D8A1FBFF0EB19CCB1A313D55C" +
        "DA56C9EC2EF29632387FE8D76E3C0468043E8F663F4860EE" +
        "12BF2D5B0B7474D6E694F91E6DBE115974A3926F12FEE5E4" +
        "38777CB6A932DF8CD8BEC4D073B931BA3BC832B68D9DD300" +
        "741FA7BF8AFC47ED2576F6936BA424663AAB639C5AE4F568" +
        "3423B4742BF1C978238F16CBE39D652DE3FDB8BEFC848AD9" +
        "22222E04A4037C0713EB57A81A23F0C73473FC646CEA306B" +
        "4BCBC8862F8385DDFA9D4B7FA2C087E879683303ED5BDD3A" +
        "062B3CF5B3A278A66D2A13F83F44F82DDF310EE074AB6A36" +
        "4597E899A0255DC164F31CC50846851DF9AB48195DED7EA1" +
        "B1D510BD7EE74D73FAF36BC31ECFA268359046F4EB879F92" +
        "4009438B481C6CD7889A002ED5EE382BC9190DA6FC026E47" +
        "9558E4475677E9AA9E3050E2765694DFC81F56E880B96E71" +
        "60C980DD98EDD3DFFFFFFFFFFFFFFFFF");

    #endregion

    /// <summary>
    /// Predefined Diffie-Hellman groups (RFC 2409, RFC 3526).
    /// NOTE: This dictionary MUST be defined AFTER the prime byte arrays above
    /// due to static field initialization order in C#.
    /// </summary>
    private static readonly Dictionary<string, (byte[] Prime, int Generator)> Groups = new()
    {
        ["modp1"] = (Modp1Prime, 2),   // 768-bit (RFC 2409)
        ["modp2"] = (Modp2Prime, 2),   // 1024-bit (RFC 2409)
        ["modp5"] = (Modp5Prime, 2),   // 1536-bit (RFC 3526)
        ["modp14"] = (Modp14Prime, 2), // 2048-bit (RFC 3526)
        ["modp15"] = (Modp15Prime, 2), // 3072-bit (RFC 3526)
        ["modp16"] = (Modp16Prime, 2), // 4096-bit (RFC 3526)
        ["modp17"] = (Modp17Prime, 2), // 6144-bit (RFC 3526)
        ["modp18"] = (Modp18Prime, 2), // 8192-bit (RFC 3526)
    };

    /// <summary>
    /// Creates a DiffieHellman object with a random prime of the specified bit length.
    /// </summary>
    public SharpTSDiffieHellman(int primeLength)
    {
        _isGroup = false;
        _generator = 2;
        _prime = GenerateRandomPrime(primeLength);
    }

    /// <summary>
    /// Creates a DiffieHellman object with the specified prime and generator.
    /// </summary>
    public SharpTSDiffieHellman(byte[] prime, byte[]? generator)
    {
        _isGroup = false;
        _prime = new BigInteger(prime, isUnsigned: true, isBigEndian: true);
        _generator = generator != null
            ? new BigInteger(generator, isUnsigned: true, isBigEndian: true)
            : 2;
    }

    /// <summary>
    /// Creates a DiffieHellman object with a predefined group.
    /// </summary>
    public SharpTSDiffieHellman(string groupName, bool isGroup = true)
    {
        _isGroup = isGroup;
        var name = groupName.ToLowerInvariant();
        if (!Groups.TryGetValue(name, out var group))
            throw new ArgumentException($"Unknown DH group: {groupName}");

        _prime = new BigInteger(group.Prime, isUnsigned: true, isBigEndian: true);
        _generator = group.Generator;
    }

    /// <summary>
    /// Generates and returns the public key.
    /// </summary>
    public object GenerateKeys(string? encoding = null)
    {
        // Generate a random private key
        int byteCount = (int)((_prime.GetBitLength() + 7) / 8);
        var privateBytes = RandomNumberGenerator.GetBytes(byteCount);
        _privateKey = new BigInteger(privateBytes, isUnsigned: true, isBigEndian: true);

        // Ensure private key is less than prime
        _privateKey = _privateKey % (_prime - 1);
        if (_privateKey < 2) _privateKey = 2;

        // Compute public key: g^x mod p
        _publicKey = BigInteger.ModPow(_generator, _privateKey.Value, _prime);

        return EncodeResult(_publicKey.Value.ToByteArray(isUnsigned: true, isBigEndian: true), encoding);
    }

    /// <summary>
    /// Computes the shared secret using the other party's public key.
    /// </summary>
    public object ComputeSecret(object otherPublicKey, string? inputEncoding = null, string? outputEncoding = null)
    {
        if (_privateKey == null)
            throw new InvalidOperationException("Keys must be generated before computing secret");

        var otherBytes = DecodeInput(otherPublicKey, inputEncoding);
        var other = new BigInteger(otherBytes, isUnsigned: true, isBigEndian: true);

        // Compute shared secret: other^x mod p
        var secret = BigInteger.ModPow(other, _privateKey.Value, _prime);
        return EncodeResult(secret.ToByteArray(isUnsigned: true, isBigEndian: true), outputEncoding);
    }

    /// <summary>
    /// Returns the prime used for key exchange.
    /// </summary>
    public object GetPrime(string? encoding = null)
    {
        return EncodeResult(_prime.ToByteArray(isUnsigned: true, isBigEndian: true), encoding);
    }

    /// <summary>
    /// Returns the generator used for key exchange.
    /// </summary>
    public object GetGenerator(string? encoding = null)
    {
        return EncodeResult(_generator.ToByteArray(isUnsigned: true, isBigEndian: true), encoding);
    }

    /// <summary>
    /// Returns the public key.
    /// </summary>
    public object GetPublicKey(string? encoding = null)
    {
        if (_publicKey == null)
            throw new InvalidOperationException("Keys have not been generated yet");
        return EncodeResult(_publicKey.Value.ToByteArray(isUnsigned: true, isBigEndian: true), encoding);
    }

    /// <summary>
    /// Returns the private key.
    /// </summary>
    public object GetPrivateKey(string? encoding = null)
    {
        if (_privateKey == null)
            throw new InvalidOperationException("Keys have not been generated yet");
        return EncodeResult(_privateKey.Value.ToByteArray(isUnsigned: true, isBigEndian: true), encoding);
    }

    /// <summary>
    /// Sets the public key (not allowed for predefined groups).
    /// </summary>
    public void SetPublicKey(object key, string? encoding = null)
    {
        if (_isGroup)
            throw new InvalidOperationException("Cannot set keys on a predefined DH group");
        var keyBytes = DecodeInput(key, encoding);
        _publicKey = new BigInteger(keyBytes, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Sets the private key (not allowed for predefined groups).
    /// </summary>
    public void SetPrivateKey(object key, string? encoding = null)
    {
        if (_isGroup)
            throw new InvalidOperationException("Cannot set keys on a predefined DH group");
        var keyBytes = DecodeInput(key, encoding);
        _privateKey = new BigInteger(keyBytes, isUnsigned: true, isBigEndian: true);
    }

    /// <summary>
    /// Returns 0 if the parameters are valid.
    /// </summary>
    public double VerifyError => 0;

    private static object EncodeResult(byte[] bytes, string? encoding)
    {
        return encoding?.ToLowerInvariant() switch
        {
            "hex" => Convert.ToHexString(bytes).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(bytes),
            _ => new SharpTSBuffer(bytes)
        };
    }

    private static byte[] DecodeInput(object input, string? encoding)
    {
        if (input is SharpTSBuffer buffer)
            return buffer.Data;
        if (input is byte[] bytes)
            return bytes;
        if (input is string str)
        {
            return encoding?.ToLowerInvariant() switch
            {
                "hex" => Convert.FromHexString(str),
                "base64" => Convert.FromBase64String(str),
                _ => System.Text.Encoding.UTF8.GetBytes(str)
            };
        }
        throw new ArgumentException("Input must be a Buffer, byte array, or string");
    }

    private static BigInteger GenerateRandomPrime(int bitLength)
    {
        // Generate a random probable prime
        int byteCount = (bitLength + 7) / 8;
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(byteCount);
            // Set the high bit to ensure correct bit length
            bytes[0] |= 0x80;
            // Set the low bit to ensure odd number
            bytes[^1] |= 0x01;

            var candidate = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
            if (IsProbablePrime(candidate, 10))
                return candidate;
        }
    }

    private static bool IsProbablePrime(BigInteger n, int k)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n.IsEven) return false;

        // Write n-1 as 2^r * d
        var d = n - 1;
        int r = 0;
        while (d.IsEven)
        {
            d >>= 1;
            r++;
        }

        // Miller-Rabin primality test
        int byteCount = n.GetByteCount();
        for (int i = 0; i < k; i++)
        {
            var aBytes = RandomNumberGenerator.GetBytes(byteCount);
            var a = new BigInteger(aBytes, isUnsigned: true, isBigEndian: true);
            a = (a % (n - 4)) + 2; // a in [2, n-2]

            var x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1)
                continue;

            bool composite = true;
            for (int j = 0; j < r - 1; j++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1)
                {
                    composite = false;
                    break;
                }
            }

            if (composite)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets a member of this DiffieHellman object.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "generateKeys" => new BuiltInMethod("generateKeys", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return GenerateKeys(encoding);
            }),
            "computeSecret" => new BuiltInMethod("computeSecret", 1, 3, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("computeSecret requires a public key argument");
                var inputEncoding = args.Count > 1 ? args[1]?.ToString() : null;
                var outputEncoding = args.Count > 2 ? args[2]?.ToString() : null;
                return ComputeSecret(args[0]!, inputEncoding, outputEncoding);
            }),
            "getPrime" => new BuiltInMethod("getPrime", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return GetPrime(encoding);
            }),
            "getGenerator" => new BuiltInMethod("getGenerator", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return GetGenerator(encoding);
            }),
            "getPublicKey" => new BuiltInMethod("getPublicKey", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return GetPublicKey(encoding);
            }),
            "getPrivateKey" => new BuiltInMethod("getPrivateKey", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return GetPrivateKey(encoding);
            }),
            "setPublicKey" => new BuiltInMethod("setPublicKey", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("setPublicKey requires a key argument");
                var encoding = args.Count > 1 ? args[1]?.ToString() : null;
                SetPublicKey(args[0]!, encoding);
                return null;
            }),
            "setPrivateKey" => new BuiltInMethod("setPrivateKey", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("setPrivateKey requires a key argument");
                var encoding = args.Count > 1 ? args[1]?.ToString() : null;
                SetPrivateKey(args[0]!, encoding);
                return null;
            }),
            "verifyError" => VerifyError,
            _ => null
        };
    }
}
