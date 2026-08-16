using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $DiffieHellman class for standalone DH key exchange support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDiffieHellman
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _tsDHTypeBuilder = null!;
    private FieldBuilder _tsDHPrimeField = null!;
    private FieldBuilder _tsDHGeneratorField = null!;
    private FieldBuilder _tsDHPrivateKeyField = null!;
    private FieldBuilder _tsDHPublicKeyField = null!;
    private FieldBuilder _tsDHIsGroupField = null!;

    // Static fields for MODP group primes
    private FieldBuilder _modp1PrimeField = null!;
    private FieldBuilder _modp2PrimeField = null!;
    private FieldBuilder _modp5PrimeField = null!;
    private FieldBuilder _modp14PrimeField = null!;
    private FieldBuilder _modp15PrimeField = null!;
    private FieldBuilder _modp16PrimeField = null!;
    private FieldBuilder _modp17PrimeField = null!;
    private FieldBuilder _modp18PrimeField = null!;

    /// <summary>
    /// Phase 1: Define type, fields, and constructors.
    /// Called before EmitRuntimeClass.
    /// </summary>
    private void EmitTSDHTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $DiffieHellman
        _tsDHTypeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$DiffieHellman",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSDiffieHellmanType = _tsDHTypeBuilder;

        // Instance fields
        _tsDHPrimeField = _tsDHTypeBuilder.DefineField("_prime", typeof(BigInteger), FieldAttributes.Private);
        _tsDHGeneratorField = _tsDHTypeBuilder.DefineField("_generator", typeof(BigInteger), FieldAttributes.Private);
        _tsDHPrivateKeyField = _tsDHTypeBuilder.DefineField("_privateKey", typeof(BigInteger?), FieldAttributes.Private);
        _tsDHPublicKeyField = _tsDHTypeBuilder.DefineField("_publicKey", typeof(BigInteger?), FieldAttributes.Private);
        _tsDHIsGroupField = _tsDHTypeBuilder.DefineField("_isGroup", _types.Boolean, FieldAttributes.Private);

        // Static fields for MODP primes (initialized in static constructor)
        _modp1PrimeField = _tsDHTypeBuilder.DefineField("Modp1Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp2PrimeField = _tsDHTypeBuilder.DefineField("Modp2Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp5PrimeField = _tsDHTypeBuilder.DefineField("Modp5Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp14PrimeField = _tsDHTypeBuilder.DefineField("Modp14Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp15PrimeField = _tsDHTypeBuilder.DefineField("Modp15Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp16PrimeField = _tsDHTypeBuilder.DefineField("Modp16Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp17PrimeField = _tsDHTypeBuilder.DefineField("Modp17Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);
        _modp18PrimeField = _tsDHTypeBuilder.DefineField("Modp18Prime", _types.ByteArray, FieldAttributes.Private | FieldAttributes.Static);

        // Static constructor to initialize MODP primes
        EmitTSDHStaticConstructor(_tsDHTypeBuilder);

        // Helper methods needed by constructors and CryptoCreateDiffieHellman
        // Order matters: BigIntFromBytes first, then IsProbablePrime (uses BigIntFromBytes),
        // then GenerateRandomPrime (uses both)
        EmitTSDHBigIntFromBytes(_tsDHTypeBuilder, runtime);
        EmitTSDHIsProbablePrime(_tsDHTypeBuilder, runtime);
        EmitTSDHGenerateRandomPrime(_tsDHTypeBuilder, runtime);
        // DecodeInput is needed by CryptoCreateDiffieHellman which runs during EmitRuntimeClass
        EmitTSDHDecodeInput(_tsDHTypeBuilder, runtime);

        // Constructors
        EmitTSDHCtorPrimeLength(_tsDHTypeBuilder, runtime);
        EmitTSDHCtorPrimeGenerator(_tsDHTypeBuilder, runtime);
        EmitTSDHCtorGroup(_tsDHTypeBuilder, runtime);

        // Define GetMember signature in Phase 1 so GetProperty can reference it.
        // The IL body is emitted in Phase 2 (EmitTSDHGetMember).
        var getMemberMethod = _tsDHTypeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGetMember = getMemberMethod;
    }

    /// <summary>
    /// Phase 2: Add all methods and finalize type.
    /// Called after EmitRuntimeClass (needs runtime helpers).
    /// </summary>
    private void EmitTSDHFinalize(EmittedRuntime runtime)
    {
        // Helper methods first (EncodeResult is needed by other methods)
        EmitTSDHEncodeResult(_tsDHTypeBuilder, runtime);

        // Methods
        EmitTSDHGenerateKeys(_tsDHTypeBuilder, runtime);
        EmitTSDHComputeSecret(_tsDHTypeBuilder, runtime);
        EmitTSDHGetPrime(_tsDHTypeBuilder, runtime);
        EmitTSDHGetGenerator(_tsDHTypeBuilder, runtime);
        EmitTSDHGetPublicKey(_tsDHTypeBuilder, runtime);
        EmitTSDHGetPrivateKey(_tsDHTypeBuilder, runtime);
        EmitTSDHSetPublicKey(_tsDHTypeBuilder, runtime);
        EmitTSDHSetPrivateKey(_tsDHTypeBuilder, runtime);
        EmitTSDHGetMember(_tsDHTypeBuilder, runtime);
        // Note: DecodeInput, GenerateRandomPrime and IsProbablePrime are emitted in Phase 1

        _tsDHTypeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the static constructor to initialize MODP prime arrays.
    /// </summary>
    private void EmitTSDHStaticConstructor(TypeBuilder typeBuilder)
    {
        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();

        // Initialize each MODP prime with its hex string
        EmitInitModpPrime(il, _modp1PrimeField, Modp1PrimeHex);
        EmitInitModpPrime(il, _modp2PrimeField, Modp2PrimeHex);
        EmitInitModpPrime(il, _modp5PrimeField, Modp5PrimeHex);
        EmitInitModpPrime(il, _modp14PrimeField, Modp14PrimeHex);
        EmitInitModpPrime(il, _modp15PrimeField, Modp15PrimeHex);
        EmitInitModpPrime(il, _modp16PrimeField, Modp16PrimeHex);
        EmitInitModpPrime(il, _modp17PrimeField, Modp17PrimeHex);
        EmitInitModpPrime(il, _modp18PrimeField, Modp18PrimeHex);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits code to initialize a MODP prime field from a hex string.
    /// </summary>
    private void EmitInitModpPrime(ILGenerator il, FieldBuilder field, string hexString)
    {
        il.Emit(OpCodes.Ldstr, hexString);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Stsfld, field);
    }

    /// <summary>
    /// Emits: public static BigInteger BigIntFromBytes(byte[] bytes)
    /// Creates a BigInteger from byte array with isUnsigned=true, isBigEndian=true.
    /// This helper method is needed because BigInteger constructor takes ReadOnlySpan<byte>, not byte[].
    /// </summary>
    private void EmitTSDHBigIntFromBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntFromBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(BigInteger),
            [typeof(byte[])]
        );
        runtime.TSDHBigIntFromBytes = method;

        var il = method.GetILGenerator();

        // Create ReadOnlySpan<byte> from byte[]
        var spanLocal = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
        il.Emit(OpCodes.Ldloca, spanLocal);
        il.Emit(OpCodes.Ldarg_0);  // byte[]
        il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetConstructor([typeof(byte[])])!);

        // Create BigInteger with span, isUnsigned=true, isBigEndian=true
        il.Emit(OpCodes.Ldloc, spanLocal);
        il.Emit(OpCodes.Ldc_I4_1);  // isUnsigned
        il.Emit(OpCodes.Ldc_I4_1);  // isBigEndian
        il.Emit(OpCodes.Newobj, typeof(BigInteger).GetConstructor([typeof(ReadOnlySpan<byte>), typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Ret);
    }

    #region MODP Prime Hex Strings (RFC 2409, RFC 3526)

    private const string Modp1PrimeHex =
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A63A3620FFFFFFFFFFFFFFFF";

    private const string Modp2PrimeHex =
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381" +
        "FFFFFFFFFFFFFFFF";

    private const string Modp5PrimeHex =
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
        "670C354E4ABC9804F1746C08CA237327FFFFFFFFFFFFFFFF";

    private const string Modp14PrimeHex =
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
        "15728E5A8AACAA68FFFFFFFFFFFFFFFF";

    private const string Modp15PrimeHex =
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
        "43DB5BFCE0FD108E4B82D120A93AD2CAFFFFFFFFFFFFFFFF";

    private const string Modp16PrimeHex =
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
        "FFFFFFFFFFFFFFFF";

    private const string Modp17PrimeHex =
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
        "12BF2D5B0B7474D6E694F91E6DCC4024FFFFFFFFFFFFFFFF";

    private const string Modp18PrimeHex =
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
        "60C980DD98EDD3DFFFFFFFFFFFFFFFFF";

    #endregion

    /// <summary>
    /// Emits: public $DiffieHellman(int primeLength)
    /// </summary>
    private void EmitTSDHCtorPrimeLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        runtime.TSDiffieHellmanCtorPrimeLength = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _isGroup = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDHIsGroupField);

        // _generator = 2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Stfld, _tsDHGeneratorField);

        // _prime = GenerateRandomPrime(primeLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // primeLength
        il.Emit(OpCodes.Call, runtime.TSDHGenerateRandomPrime);
        il.Emit(OpCodes.Stfld, _tsDHPrimeField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $DiffieHellman(byte[] prime, byte[]? generator)
    /// </summary>
    private void EmitTSDHCtorPrimeGenerator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ByteArray, _types.ByteArray]
        );
        runtime.TSDiffieHellmanCtorPrimeGenerator = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _isGroup = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDHIsGroupField);

        // _prime = BigIntFromBytes(prime)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // prime bytes
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stfld, _tsDHPrimeField);

        // if (generator != null) _generator = BigIntFromBytes(generator) else _generator = 2
        var generatorNullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);  // generator
        il.Emit(OpCodes.Brfalse, generatorNullLabel);

        // generator != null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);  // generator bytes
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stfld, _tsDHGeneratorField);
        il.Emit(OpCodes.Br, doneLabel);

        // generator == null
        il.MarkLabel(generatorNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Stfld, _tsDHGeneratorField);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $DiffieHellman(string groupName)
    /// </summary>
    private void EmitTSDHCtorGroup(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSDiffieHellmanCtorGroup = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _isGroup = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDHIsGroupField);

        // Normalize group name
        var nameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Local for prime bytes
        var primeLocal = il.DeclareLocal(_types.ByteArray);

        // Switch on group name
        var modp1Label = il.DefineLabel();
        var modp2Label = il.DefineLabel();
        var modp5Label = il.DefineLabel();
        var modp14Label = il.DefineLabel();
        var modp15Label = il.DefineLabel();
        var modp16Label = il.DefineLabel();
        var modp17Label = il.DefineLabel();
        var modp18Label = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var setPrimeLabel = il.DefineLabel();

        // Check each group name
        EmitGroupNameCheck(il, nameLocal, "modp1", modp1Label);
        EmitGroupNameCheck(il, nameLocal, "modp2", modp2Label);
        EmitGroupNameCheck(il, nameLocal, "modp5", modp5Label);
        EmitGroupNameCheck(il, nameLocal, "modp14", modp14Label);
        EmitGroupNameCheck(il, nameLocal, "modp15", modp15Label);
        EmitGroupNameCheck(il, nameLocal, "modp16", modp16Label);
        EmitGroupNameCheck(il, nameLocal, "modp17", modp17Label);
        EmitGroupNameCheck(il, nameLocal, "modp18", modp18Label);
        il.Emit(OpCodes.Br, throwLabel);

        // Load each prime
        EmitLoadModpPrime(il, modp1Label, _modp1PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp2Label, _modp2PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp5Label, _modp5PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp14Label, _modp14PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp15Label, _modp15PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp16Label, _modp16PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp17Label, _modp17PrimeField, primeLocal, setPrimeLabel);
        EmitLoadModpPrime(il, modp18Label, _modp18PrimeField, primeLocal, setPrimeLabel);

        // Throw for unknown group
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Unknown DH group: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        // Set prime and generator
        il.MarkLabel(setPrimeLabel);
        // _prime = new BigInteger(primeLocal, isUnsigned: true, isBigEndian: true)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, primeLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stfld, _tsDHPrimeField);

        // _generator = 2 (all predefined groups use generator 2)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Stfld, _tsDHGeneratorField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitGroupNameCheck(ILGenerator il, LocalBuilder nameLocal, string groupName, Label matchLabel)
    {
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, groupName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, matchLabel);
    }

    private void EmitLoadModpPrime(ILGenerator il, Label matchLabel, FieldBuilder primeField, LocalBuilder primeLocal, Label continueLabel)
    {
        il.MarkLabel(matchLabel);
        il.Emit(OpCodes.Ldsfld, primeField);
        il.Emit(OpCodes.Stloc, primeLocal);
        il.Emit(OpCodes.Br, continueLabel);
    }

    /// <summary>
    /// Emits: public object GenerateKeys(string? encoding)
    /// </summary>
    private void EmitTSDHGenerateKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GenerateKeys",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGenerateKeys = method;

        var il = method.GetILGenerator();

        // int byteCount = (int)((_prime.GetBitLength() + 7) / 8)
        var byteCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrimeField);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("GetBitLength")!);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, byteCountLocal);

        // byte[] privateBytes = RandomNumberGenerator.GetBytes(byteCount)
        var privateBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldloc, byteCountLocal);
        il.Emit(OpCodes.Call, typeof(RandomNumberGenerator).GetMethod("GetBytes", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, privateBytesLocal);

        // _privateKey = new BigInteger(privateBytes, isUnsigned: true, isBigEndian: true)
        var privateKeyLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, privateBytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stloc, privateKeyLocal);

        // _privateKey = _privateKey % (_prime - 1)
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHPrimeField);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("One")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Subtraction", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Modulus", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, privateKeyLocal);

        // if (_privateKey < 2) _privateKey = 2
        var skipAdjustLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_LessThan", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brfalse, skipAdjustLabel);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, privateKeyLocal);
        il.MarkLabel(skipAdjustLabel);

        // Store _privateKey (wrap in Nullable)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Newobj, typeof(BigInteger?).GetConstructor([typeof(BigInteger)])!);
        il.Emit(OpCodes.Stfld, _tsDHPrivateKeyField);

        // _publicKey = BigInteger.ModPow(_generator, _privateKey, _prime)
        var publicKeyLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHGeneratorField);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHPrimeField);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ModPow", [typeof(BigInteger), typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, publicKeyLocal);

        // Store _publicKey
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, publicKeyLocal);
        il.Emit(OpCodes.Newobj, typeof(BigInteger?).GetConstructor([typeof(BigInteger)])!);
        il.Emit(OpCodes.Stfld, _tsDHPublicKeyField);

        // return EncodeResult(_publicKey.ToByteArray(isUnsigned: true, isBigEndian: true), encoding)
        il.Emit(OpCodes.Ldloca, publicKeyLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_1);  // encoding
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object ComputeSecret(object otherPublicKey, string? inputEncoding, string? outputEncoding)
    /// </summary>
    private void EmitTSDHComputeSecret(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ComputeSecret",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.String, _types.String]
        );
        runtime.TSDHComputeSecret = method;

        var il = method.GetILGenerator();

        // if (_privateKey == null) throw
        var hasPrivateKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrivateKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasPrivateKeyLabel);
        il.Emit(OpCodes.Ldstr, "Keys must be generated before computing secret");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(hasPrivateKeyLabel);

        // byte[] otherBytes = DecodeInput(otherPublicKey, inputEncoding)
        var otherBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);  // otherPublicKey
        il.Emit(OpCodes.Ldarg_2);  // inputEncoding
        il.Emit(OpCodes.Call, runtime.TSDHDecodeInput);
        il.Emit(OpCodes.Stloc, otherBytesLocal);

        // BigInteger other = new BigInteger(otherBytes, isUnsigned: true, isBigEndian: true)
        var otherLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, otherBytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stloc, otherLocal);

        // BigInteger secret = BigInteger.ModPow(other, _privateKey.Value, _prime)
        var secretLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrivateKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHPrimeField);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ModPow", [typeof(BigInteger), typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, secretLocal);

        // return EncodeResult(secret.ToByteArray(...), outputEncoding)
        il.Emit(OpCodes.Ldloca, secretLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_3);  // outputEncoding
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetPrime(string? encoding)
    /// </summary>
    private void EmitTSDHGetPrime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrime",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGetPrime = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrimeField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetGenerator(string? encoding)
    /// </summary>
    private void EmitTSDHGetGenerator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetGenerator",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGetGenerator = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHGeneratorField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetPublicKey(string? encoding)
    /// </summary>
    private void EmitTSDHGetPublicKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPublicKey",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGetPublicKey = method;

        var il = method.GetILGenerator();

        // if (_publicKey == null) throw
        var hasKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPublicKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasKeyLabel);
        il.Emit(OpCodes.Ldstr, "Keys have not been generated yet");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(hasKeyLabel);

        // Get value and encode
        var valueLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPublicKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetPrivateKey(string? encoding)
    /// </summary>
    private void EmitTSDHGetPrivateKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrivateKey",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSDHGetPrivateKey = method;

        var il = method.GetILGenerator();

        // if (_privateKey == null) throw
        var hasKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrivateKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasKeyLabel);
        il.Emit(OpCodes.Ldstr, "Keys have not been generated yet");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(hasKeyLabel);

        // Get value and encode
        var valueLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _tsDHPrivateKeyField);
        il.Emit(OpCodes.Call, typeof(BigInteger?).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ToByteArray", [_types.Boolean, _types.Boolean])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TSDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void SetPublicKey(object key, string? encoding)
    /// </summary>
    private void EmitTSDHSetPublicKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPublicKey",
            MethodAttributes.Public,
            null,
            [_types.Object, _types.String]
        );
        runtime.TSDHSetPublicKey = method;

        var il = method.GetILGenerator();

        // if (_isGroup) throw
        var notGroupLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHIsGroupField);
        il.Emit(OpCodes.Brfalse, notGroupLabel);
        il.Emit(OpCodes.Ldstr, "Cannot set keys on a predefined DH group");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notGroupLabel);

        // byte[] keyBytes = DecodeInput(key, encoding)
        var keyBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSDHDecodeInput);
        il.Emit(OpCodes.Stloc, keyBytesLocal);

        // _publicKey = new BigInteger(keyBytes, ...)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyBytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Newobj, typeof(BigInteger?).GetConstructor([typeof(BigInteger)])!);
        il.Emit(OpCodes.Stfld, _tsDHPublicKeyField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void SetPrivateKey(object key, string? encoding)
    /// </summary>
    private void EmitTSDHSetPrivateKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPrivateKey",
            MethodAttributes.Public,
            null,
            [_types.Object, _types.String]
        );
        runtime.TSDHSetPrivateKey = method;

        var il = method.GetILGenerator();

        // if (_isGroup) throw
        var notGroupLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDHIsGroupField);
        il.Emit(OpCodes.Brfalse, notGroupLabel);
        il.Emit(OpCodes.Ldstr, "Cannot set keys on a predefined DH group");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notGroupLabel);

        // byte[] keyBytes = DecodeInput(key, encoding)
        var keyBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSDHDecodeInput);
        il.Emit(OpCodes.Stloc, keyBytesLocal);

        // _privateKey = new BigInteger(keyBytes, ...)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyBytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Newobj, typeof(BigInteger?).GetConstructor([typeof(BigInteger)])!);
        il.Emit(OpCodes.Stfld, _tsDHPrivateKeyField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? GetMember(string name)
    /// Returns bound methods for each DH operation.
    /// </summary>
    private void EmitTSDHGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // MethodBuilder was already defined in EmitTSDHTypeDefinition (Phase 1)
        var method = runtime.TSDHGetMember;

        var il = method.GetILGenerator();

        // For now, just return null - we'll add bound method support later
        // This is similar to what we did for ECDH

        var generateKeysLabel = il.DefineLabel();
        var computeSecretLabel = il.DefineLabel();
        var getPrimeLabel = il.DefineLabel();
        var getGeneratorLabel = il.DefineLabel();
        var getPublicKeyLabel = il.DefineLabel();
        var getPrivateKeyLabel = il.DefineLabel();
        var setPublicKeyLabel = il.DefineLabel();
        var setPrivateKeyLabel = il.DefineLabel();
        var verifyErrorLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check each method name
        EmitDHMemberCheck(il, "generateKeys", generateKeysLabel);
        EmitDHMemberCheck(il, "computeSecret", computeSecretLabel);
        EmitDHMemberCheck(il, "getPrime", getPrimeLabel);
        EmitDHMemberCheck(il, "getGenerator", getGeneratorLabel);
        EmitDHMemberCheck(il, "getPublicKey", getPublicKeyLabel);
        EmitDHMemberCheck(il, "getPrivateKey", getPrivateKeyLabel);
        EmitDHMemberCheck(il, "setPublicKey", setPublicKeyLabel);
        EmitDHMemberCheck(il, "setPrivateKey", setPrivateKeyLabel);
        EmitDHMemberCheck(il, "verifyError", verifyErrorLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        // Return bound methods
        il.MarkLabel(generateKeysLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GenerateKeys");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(computeSecretLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "ComputeSecret");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(getPrimeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetPrime");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(getGeneratorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetGenerator");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(getPublicKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetPublicKey");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(getPrivateKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetPrivateKey");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(setPublicKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "SetPublicKey");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(setPrivateKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "SetPrivateKey");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, runtime.BoundDHMethodCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(verifyErrorLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDHMemberCheck(ILGenerator il, string memberName, Label matchLabel)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, memberName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, matchLabel);
    }

    /// <summary>
    /// Emits: public static object EncodeResult(byte[] bytes, string? encoding)
    /// </summary>
    private void EmitTSDHEncodeResult(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DHEncodeResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ByteArray, _types.String]
        );
        runtime.TSDHEncodeResult = method;

        var il = method.GetILGenerator();

        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var bufferLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, bufferLabel);

        var encodingLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        il.Emit(OpCodes.Br, bufferLabel);

        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] DecodeInput(object input, string? encoding)
    /// </summary>
    private void EmitTSDHDecodeInput(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DHDecodeInput",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.Object, _types.String]
        );
        runtime.TSDHDecodeInput = method;

        var il = method.GetILGenerator();

        var checkBytesLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Check for $Buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, checkBytesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Ret);

        // Check for byte[]
        il.MarkLabel(checkBytesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Ret);

        // Check for string
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, throwLabel);

        var strLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);

        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var utf8Label = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, utf8Label);

        var encodingLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        il.Emit(OpCodes.Br, utf8Label);

        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(utf8Label);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Input must be a Buffer, byte array, or string");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: private static BigInteger GenerateRandomPrime(int bitLength)
    /// </summary>
    private void EmitTSDHGenerateRandomPrime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GenerateRandomPrime",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(BigInteger),
            [_types.Int32]
        );
        runtime.TSDHGenerateRandomPrime = method;

        var il = method.GetILGenerator();

        // int byteCount = (bitLength + 7) / 8
        var byteCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, byteCountLocal);

        // while (true) loop
        var loopStartLabel = il.DefineLabel();
        il.MarkLabel(loopStartLabel);

        // byte[] bytes = RandomNumberGenerator.GetBytes(byteCount)
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldloc, byteCountLocal);
        il.Emit(OpCodes.Call, typeof(RandomNumberGenerator).GetMethod("GetBytes", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // bytes[0] |= 0x80 (set high bit)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x80);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // bytes[^1] |= 0x01 (set low bit - ensure odd)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // BigInteger candidate = new BigInteger(bytes, isUnsigned: true, isBigEndian: true)
        var candidateLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stloc, candidateLocal);

        // if (IsProbablePrime(candidate, 10)) return candidate
        il.Emit(OpCodes.Ldloc, candidateLocal);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Call, runtime.TSDHIsProbablePrime);
        il.Emit(OpCodes.Brfalse, loopStartLabel);

        il.Emit(OpCodes.Ldloc, candidateLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool IsProbablePrime(BigInteger n, int k)
    /// Miller-Rabin primality test.
    /// </summary>
    private void EmitTSDHIsProbablePrime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsProbablePrime",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [typeof(BigInteger), _types.Int32]
        );
        runtime.TSDHIsProbablePrime = method;

        var il = method.GetILGenerator();

        var returnFalseLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var notTwoOrThreeLabel = il.DefineLabel();
        var notEvenLabel = il.DefineLabel();
        var millerRabinLabel = il.DefineLabel();

        // if (n < 2) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_LessThan", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // if (n == 2 || n == 3) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // if (n.IsEven) return false
        il.Emit(OpCodes.Ldarga, 0);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("IsEven")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // Write n-1 as 2^r * d
        // BigInteger d = n - 1
        var dLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("One")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Subtraction", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, dLocal);

        // int r = 0
        var rLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, rLocal);

        // while (d.IsEven) { d >>= 1; r++; }
        var factorLoopLabel = il.DefineLabel();
        var afterFactorLabel = il.DefineLabel();
        il.MarkLabel(factorLoopLabel);
        il.Emit(OpCodes.Ldloca, dLocal);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("IsEven")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, afterFactorLabel);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_RightShift", [typeof(BigInteger), _types.Int32])!);
        il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, rLocal);
        il.Emit(OpCodes.Br, factorLoopLabel);
        il.MarkLabel(afterFactorLabel);

        // int byteCount = n.GetByteCount()
        var byteCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarga, 0);
        il.Emit(OpCodes.Ldc_I4_0);  // isUnsigned = false
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("GetByteCount", [typeof(bool)])!);
        il.Emit(OpCodes.Stloc, byteCountLocal);

        // for (int i = 0; i < k; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var outerLoopLabel = il.DefineLabel();
        var afterOuterLoopLabel = il.DefineLabel();
        il.MarkLabel(outerLoopLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);  // k
        il.Emit(OpCodes.Bge, afterOuterLoopLabel);

        // byte[] aBytes = RandomNumberGenerator.GetBytes(byteCount)
        var aBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldloc, byteCountLocal);
        il.Emit(OpCodes.Call, typeof(RandomNumberGenerator).GetMethod("GetBytes", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, aBytesLocal);

        // BigInteger a = new BigInteger(aBytes, isUnsigned: true, isBigEndian: true)
        var aLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, aBytesLocal);
        il.Emit(OpCodes.Call, runtime.TSDHBigIntFromBytes);
        il.Emit(OpCodes.Stloc, aLocal);

        // a = (a % (n - 4)) + 2  (to get a in [2, n-2])
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Subtraction", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Modulus", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Addition", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, aLocal);

        // BigInteger x = BigInteger.ModPow(a, d, n)
        var xLocal = il.DeclareLocal(typeof(BigInteger));
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldarg_0);  // n
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ModPow", [typeof(BigInteger), typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, xLocal);

        // if (x == 1 || x == n - 1) continue
        var continueOuterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("One")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brtrue, continueOuterLabel);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("One")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Subtraction", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brtrue, continueOuterLabel);

        // bool composite = true
        var compositeLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, compositeLocal);

        // for (int j = 0; j < r - 1; j++)
        var jLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var innerLoopLabel = il.DefineLabel();
        var afterInnerLoopLabel = il.DefineLabel();
        il.MarkLabel(innerLoopLabel);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, afterInnerLoopLabel);

        // x = BigInteger.ModPow(x, 2, n)
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", [_types.Int32])!);
        il.Emit(OpCodes.Ldarg_0);  // n
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("ModPow", [typeof(BigInteger), typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Stloc, xLocal);

        // if (x == n - 1) { composite = false; break; }
        var continueInnerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("One")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Subtraction", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brfalse, continueInnerLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compositeLocal);
        il.Emit(OpCodes.Br, afterInnerLoopLabel);

        il.MarkLabel(continueInnerLabel);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, innerLoopLabel);
        il.MarkLabel(afterInnerLoopLabel);

        // if (composite) return false
        il.Emit(OpCodes.Ldloc, compositeLocal);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        il.MarkLabel(continueOuterLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, outerLoopLabel);
        il.MarkLabel(afterOuterLoopLabel);

        il.Emit(OpCodes.Br, returnTrueLabel);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    // BoundDHMethod type definition and finalization
    private TypeBuilder _boundDHMethodTypeBuilder = null!;
    private FieldBuilder _boundDHMethodDhField = null!;
    private FieldBuilder _boundDHMethodMethodNameField = null!;

    /// <summary>
    /// Phase 1: Define $BoundDHMethod type, fields, and constructor.
    /// </summary>
    private void EmitBoundDHMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _boundDHMethodTypeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundDHMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        _ = _boundDHMethodTypeBuilder;

        _boundDHMethodDhField = _boundDHMethodTypeBuilder.DefineField("_dh", runtime.TSDiffieHellmanType, FieldAttributes.Private);
        _boundDHMethodMethodNameField = _boundDHMethodTypeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Private);
        var minArgsField = _boundDHMethodTypeBuilder.DefineField("_minArgs", _types.Int32, FieldAttributes.Private);
        var maxArgsField = _boundDHMethodTypeBuilder.DefineField("_maxArgs", _types.Int32, FieldAttributes.Private);

        var ctor = _boundDHMethodTypeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSDiffieHellmanType, _types.String, _types.Int32, _types.Int32]
        );
        runtime.BoundDHMethodCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, _boundDHMethodDhField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, _boundDHMethodMethodNameField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_3);
        ctorIl.Emit(OpCodes.Stfld, minArgsField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg, 4);
        ctorIl.Emit(OpCodes.Stfld, maxArgsField);
        ctorIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Phase 2: Add Invoke method and finalize $BoundDHMethod type.
    /// </summary>
    private void EmitBoundDHMethodFinalize(EmittedRuntime runtime)
    {
        var invoke = _boundDHMethodTypeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        _ = invoke;

        var il = invoke.GetILGenerator();

        var methodNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodMethodNameField);
        il.Emit(OpCodes.Stloc, methodNameLocal);

        var generateKeysLabel = il.DefineLabel();
        var computeSecretLabel = il.DefineLabel();
        var getPrimeLabel = il.DefineLabel();
        var getGeneratorLabel = il.DefineLabel();
        var getPublicKeyLabel = il.DefineLabel();
        var getPrivateKeyLabel = il.DefineLabel();
        var setPublicKeyLabel = il.DefineLabel();
        var setPrivateKeyLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitDHInvokeCheck(il, methodNameLocal, "GenerateKeys", generateKeysLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "ComputeSecret", computeSecretLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "GetPrime", getPrimeLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "GetGenerator", getGeneratorLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "GetPublicKey", getPublicKeyLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "GetPrivateKey", getPrivateKeyLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "SetPublicKey", setPublicKeyLabel);
        EmitDHInvokeCheck(il, methodNameLocal, "SetPrivateKey", setPrivateKeyLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        // GenerateKeys(encoding?)
        il.MarkLabel(generateKeysLabel);
        EmitGetArgOrNull(il, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHGenerateKeys);
        il.Emit(OpCodes.Ret);

        // ComputeSecret(other, inputEncoding?, outputEncoding?)
        il.MarkLabel(computeSecretLabel);
        EmitGetArgOrNull(il, 0);
        EmitGetArgOrNull(il, 1);
        EmitGetArgOrNull(il, 2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHComputeSecret);
        il.Emit(OpCodes.Ret);

        // GetPrime(encoding?)
        il.MarkLabel(getPrimeLabel);
        EmitGetArgOrNull(il, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHGetPrime);
        il.Emit(OpCodes.Ret);

        // GetGenerator(encoding?)
        il.MarkLabel(getGeneratorLabel);
        EmitGetArgOrNull(il, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHGetGenerator);
        il.Emit(OpCodes.Ret);

        // GetPublicKey(encoding?)
        il.MarkLabel(getPublicKeyLabel);
        EmitGetArgOrNull(il, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHGetPublicKey);
        il.Emit(OpCodes.Ret);

        // GetPrivateKey(encoding?)
        il.MarkLabel(getPrivateKeyLabel);
        EmitGetArgOrNull(il, 0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHGetPrivateKey);
        il.Emit(OpCodes.Ret);

        // SetPublicKey(key, encoding?)
        il.MarkLabel(setPublicKeyLabel);
        EmitGetArgOrNull(il, 0);
        EmitGetArgOrNull(il, 1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHSetPublicKey);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // SetPrivateKey(key, encoding?)
        il.MarkLabel(setPrivateKeyLabel);
        EmitGetArgOrNull(il, 0);
        EmitGetArgOrNull(il, 1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundDHMethodDhField);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSDHSetPrivateKey);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        _boundDHMethodTypeBuilder.CreateType();
    }

    private void EmitDHInvokeCheck(ILGenerator il, LocalBuilder methodNameLocal, string methodName, Label matchLabel)
    {
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, matchLabel);
    }
}
