using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional Node crypto metadata for one compilation. Declarations support forward
/// references; completion validates the handles and freezes the component.
/// </summary>
public sealed class EmittedCryptoRuntime
{
    internal EmittedCryptoRuntime() { }

    public bool IsComplete { get; private set; }

    // Crypto module methods
    private MethodBuilder? _createHash;
    public MethodBuilder CreateHash
    {
        get => Require(_createHash);
        internal set => SetHandle(ref _createHash, value);
    }

    private MethodBuilder? _randomBytes;
    public MethodBuilder RandomBytes
    {
        get => Require(_randomBytes);
        internal set => SetHandle(ref _randomBytes, value);
    }

    private MethodBuilder? _randomFillSync;
    public MethodBuilder RandomFillSync
    {
        get => Require(_randomFillSync);
        internal set => SetHandle(ref _randomFillSync, value);
    }

    // $Hash type - emitted for standalone crypto support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHash
    private ConstructorBuilder? _hashCtor;
    public ConstructorBuilder HashCtor
    {
        get => Require(_hashCtor);
        internal set => SetHandle(ref _hashCtor, value);
    }

    // $Hmac type - emitted for standalone crypto support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHmac
    private MethodBuilder? _createHmac;
    public MethodBuilder CreateHmac
    {
        get => Require(_createHmac);
        internal set => SetHandle(ref _createHmac, value);
    }

    private ConstructorBuilder? _hmacCtor;
    public ConstructorBuilder HmacCtor
    {
        get => Require(_hmacCtor);
        internal set => SetHandle(ref _hmacCtor, value);
    }

    // $Cipher type - emitted for standalone crypto support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSCipher
    private MethodBuilder? _createCipheriv;
    public MethodBuilder CreateCipheriv
    {
        get => Require(_createCipheriv);
        internal set => SetHandle(ref _createCipheriv, value);
    }

    private ConstructorBuilder? _cipherCtor;
    public ConstructorBuilder CipherCtor
    {
        get => Require(_cipherCtor);
        internal set => SetHandle(ref _cipherCtor, value);
    }

    // $Decipher type - emitted for standalone crypto support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDecipher
    private MethodBuilder? _createDecipheriv;
    public MethodBuilder CreateDecipheriv
    {
        get => Require(_createDecipheriv);
        internal set => SetHandle(ref _createDecipheriv, value);
    }

    private ConstructorBuilder? _decipherCtor;
    public ConstructorBuilder DecipherCtor
    {
        get => Require(_decipherCtor);
        internal set => SetHandle(ref _decipherCtor, value);
    }

    // PBKDF2 and scrypt key derivation
    private MethodBuilder? _pbkdf2Sync;
    public MethodBuilder Pbkdf2Sync
    {
        get => Require(_pbkdf2Sync);
        internal set => SetHandle(ref _pbkdf2Sync, value);
    }

    private MethodBuilder? _scryptSync;
    public MethodBuilder ScryptSync
    {
        get => Require(_scryptSync);
        internal set => SetHandle(ref _scryptSync, value);
    }

    private MethodBuilder? _scryptDeriveBytes;
    public MethodBuilder ScryptDeriveBytes
    {
        get => Require(_scryptDeriveBytes);
        internal set => SetHandle(ref _scryptDeriveBytes, value);
    }
    // Scrypt helper methods (pure IL implementation)
    private MethodBuilder? _scryptRotateLeft;
    public MethodBuilder ScryptRotateLeft
    {
        get => Require(_scryptRotateLeft);
        internal set => SetHandle(ref _scryptRotateLeft, value);
    }

    private MethodBuilder? _scryptSalsa20Core;
    public MethodBuilder ScryptSalsa20Core
    {
        get => Require(_scryptSalsa20Core);
        internal set => SetHandle(ref _scryptSalsa20Core, value);
    }

    private MethodBuilder? _scryptBlockMix;
    public MethodBuilder ScryptBlockMix
    {
        get => Require(_scryptBlockMix);
        internal set => SetHandle(ref _scryptBlockMix, value);
    }

    private MethodBuilder? _scryptROMix;
    public MethodBuilder ScryptROMix
    {
        get => Require(_scryptROMix);
        internal set => SetHandle(ref _scryptROMix, value);
    }

    // Timing-safe comparison
    private MethodBuilder? _timingSafeEqual;
    public MethodBuilder TimingSafeEqual
    {
        get => Require(_timingSafeEqual);
        internal set => SetHandle(ref _timingSafeEqual, value);
    }

    // $Sign type - emitted for standalone crypto signing support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSSign
    private MethodBuilder? _createSign;
    public MethodBuilder CreateSign
    {
        get => Require(_createSign);
        internal set => SetHandle(ref _createSign, value);
    }

    private ConstructorBuilder? _signCtor;
    public ConstructorBuilder SignCtor
    {
        get => Require(_signCtor);
        internal set => SetHandle(ref _signCtor, value);
    }

    private MethodBuilder? _signDataBytes;
    public MethodBuilder SignDataBytes
    {
        get => Require(_signDataBytes);
        internal set => SetHandle(ref _signDataBytes, value);
    }

    // $Verify type - emitted for standalone crypto verification support
    // NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSVerify
    private MethodBuilder? _createVerify;
    public MethodBuilder CreateVerify
    {
        get => Require(_createVerify);
        internal set => SetHandle(ref _createVerify, value);
    }

    private ConstructorBuilder? _verifyCtor;
    public ConstructorBuilder VerifyCtor
    {
        get => Require(_verifyCtor);
        internal set => SetHandle(ref _verifyCtor, value);
    }

    private MethodBuilder? _verifyDataBytes;
    public MethodBuilder VerifyDataBytes
    {
        get => Require(_verifyDataBytes);
        internal set => SetHandle(ref _verifyDataBytes, value);
    }

    // Crypto info methods (getHashes, getCiphers)
    private MethodBuilder? _getHashes;
    public MethodBuilder GetHashes
    {
        get => Require(_getHashes);
        internal set => SetHandle(ref _getHashes, value);
    }

    private MethodBuilder? _getCiphers;
    public MethodBuilder GetCiphers
    {
        get => Require(_getCiphers);
        internal set => SetHandle(ref _getCiphers, value);
    }

    // Key pair generation
    private MethodBuilder? _generateKeyPairSync;
    public MethodBuilder GenerateKeyPairSync
    {
        get => Require(_generateKeyPairSync);
        internal set => SetHandle(ref _generateKeyPairSync, value);
    }

    // DiffieHellman support
    private MethodBuilder? _createDiffieHellman;
    public MethodBuilder CreateDiffieHellman
    {
        get => Require(_createDiffieHellman);
        internal set => SetHandle(ref _createDiffieHellman, value);
    }

    private MethodBuilder? _getDiffieHellman;
    public MethodBuilder GetDiffieHellman
    {
        get => Require(_getDiffieHellman);
        internal set => SetHandle(ref _getDiffieHellman, value);
    }

    private TypeBuilder? _diffieHellmanType;
    public TypeBuilder DiffieHellmanType
    {
        get => Require(_diffieHellmanType);
        internal set => SetHandle(ref _diffieHellmanType, value);
    }

    private ConstructorBuilder? _diffieHellmanCtorPrimeLength;
    public ConstructorBuilder DiffieHellmanCtorPrimeLength
    {
        get => Require(_diffieHellmanCtorPrimeLength);
        internal set => SetHandle(ref _diffieHellmanCtorPrimeLength, value);
    }

    private ConstructorBuilder? _diffieHellmanCtorPrimeGenerator;
    public ConstructorBuilder DiffieHellmanCtorPrimeGenerator
    {
        get => Require(_diffieHellmanCtorPrimeGenerator);
        internal set => SetHandle(ref _diffieHellmanCtorPrimeGenerator, value);
    }

    private ConstructorBuilder? _diffieHellmanCtorGroup;
    public ConstructorBuilder DiffieHellmanCtorGroup
    {
        get => Require(_diffieHellmanCtorGroup);
        internal set => SetHandle(ref _diffieHellmanCtorGroup, value);
    }

    private MethodBuilder? _dHGenerateKeys;
    public MethodBuilder DHGenerateKeys
    {
        get => Require(_dHGenerateKeys);
        internal set => SetHandle(ref _dHGenerateKeys, value);
    }

    private MethodBuilder? _dHComputeSecret;
    public MethodBuilder DHComputeSecret
    {
        get => Require(_dHComputeSecret);
        internal set => SetHandle(ref _dHComputeSecret, value);
    }

    private MethodBuilder? _dHGetPrime;
    public MethodBuilder DHGetPrime
    {
        get => Require(_dHGetPrime);
        internal set => SetHandle(ref _dHGetPrime, value);
    }

    private MethodBuilder? _dHGetGenerator;
    public MethodBuilder DHGetGenerator
    {
        get => Require(_dHGetGenerator);
        internal set => SetHandle(ref _dHGetGenerator, value);
    }

    private MethodBuilder? _dHGetPublicKey;
    public MethodBuilder DHGetPublicKey
    {
        get => Require(_dHGetPublicKey);
        internal set => SetHandle(ref _dHGetPublicKey, value);
    }

    private MethodBuilder? _dHGetPrivateKey;
    public MethodBuilder DHGetPrivateKey
    {
        get => Require(_dHGetPrivateKey);
        internal set => SetHandle(ref _dHGetPrivateKey, value);
    }

    private MethodBuilder? _dHSetPublicKey;
    public MethodBuilder DHSetPublicKey
    {
        get => Require(_dHSetPublicKey);
        internal set => SetHandle(ref _dHSetPublicKey, value);
    }

    private MethodBuilder? _dHSetPrivateKey;
    public MethodBuilder DHSetPrivateKey
    {
        get => Require(_dHSetPrivateKey);
        internal set => SetHandle(ref _dHSetPrivateKey, value);
    }

    private MethodBuilder? _dHGetMember;
    public MethodBuilder DHGetMember
    {
        get => Require(_dHGetMember);
        internal set => SetHandle(ref _dHGetMember, value);
    }

    private MethodBuilder? _dHEncodeResult;
    public MethodBuilder DHEncodeResult
    {
        get => Require(_dHEncodeResult);
        internal set => SetHandle(ref _dHEncodeResult, value);
    }

    private MethodBuilder? _dHDecodeInput;
    public MethodBuilder DHDecodeInput
    {
        get => Require(_dHDecodeInput);
        internal set => SetHandle(ref _dHDecodeInput, value);
    }

    private MethodBuilder? _dHGenerateRandomPrime;
    public MethodBuilder DHGenerateRandomPrime
    {
        get => Require(_dHGenerateRandomPrime);
        internal set => SetHandle(ref _dHGenerateRandomPrime, value);
    }

    private MethodBuilder? _dHIsProbablePrime;
    public MethodBuilder DHIsProbablePrime
    {
        get => Require(_dHIsProbablePrime);
        internal set => SetHandle(ref _dHIsProbablePrime, value);
    }

    private MethodBuilder? _dHBigIntFromBytes;
    public MethodBuilder DHBigIntFromBytes
    {
        get => Require(_dHBigIntFromBytes);
        internal set => SetHandle(ref _dHBigIntFromBytes, value);
    }

    private ConstructorBuilder? _boundDHMethodCtor;
    public ConstructorBuilder BoundDHMethodCtor
    {
        get => Require(_boundDHMethodCtor);
        internal set => SetHandle(ref _boundDHMethodCtor, value);
    }

    // ECDH support
    private MethodBuilder? _createECDH;
    public MethodBuilder CreateECDH
    {
        get => Require(_createECDH);
        internal set => SetHandle(ref _createECDH, value);
    }

    private TypeBuilder? _eCDHType;
    public TypeBuilder ECDHType
    {
        get => Require(_eCDHType);
        internal set => SetHandle(ref _eCDHType, value);
    }

    private ConstructorBuilder? _eCDHCtor;
    public ConstructorBuilder ECDHCtor
    {
        get => Require(_eCDHCtor);
        internal set => SetHandle(ref _eCDHCtor, value);
    }

    private MethodBuilder? _eCDHGenerateKeys;
    public MethodBuilder ECDHGenerateKeys
    {
        get => Require(_eCDHGenerateKeys);
        internal set => SetHandle(ref _eCDHGenerateKeys, value);
    }

    private MethodBuilder? _eCDHComputeSecret;
    public MethodBuilder ECDHComputeSecret
    {
        get => Require(_eCDHComputeSecret);
        internal set => SetHandle(ref _eCDHComputeSecret, value);
    }

    private MethodBuilder? _eCDHGetPublicKey;
    public MethodBuilder ECDHGetPublicKey
    {
        get => Require(_eCDHGetPublicKey);
        internal set => SetHandle(ref _eCDHGetPublicKey, value);
    }

    private MethodBuilder? _eCDHGetPrivateKey;
    public MethodBuilder ECDHGetPrivateKey
    {
        get => Require(_eCDHGetPrivateKey);
        internal set => SetHandle(ref _eCDHGetPrivateKey, value);
    }

    private MethodBuilder? _eCDHSetPrivateKey;
    public MethodBuilder ECDHSetPrivateKey
    {
        get => Require(_eCDHSetPrivateKey);
        internal set => SetHandle(ref _eCDHSetPrivateKey, value);
    }

    private MethodBuilder? _eCDHGetMember;
    public MethodBuilder ECDHGetMember
    {
        get => Require(_eCDHGetMember);
        internal set => SetHandle(ref _eCDHGetMember, value);
    }

    private MethodBuilder? _eCDHEncodeResult;
    public MethodBuilder ECDHEncodeResult
    {
        get => Require(_eCDHEncodeResult);
        internal set => SetHandle(ref _eCDHEncodeResult, value);
    }

    private MethodBuilder? _eCDHDecodeInput;
    public MethodBuilder ECDHDecodeInput
    {
        get => Require(_eCDHDecodeInput);
        internal set => SetHandle(ref _eCDHDecodeInput, value);
    }

    private MethodBuilder? _ecdhDecompressY;
    public MethodBuilder EcdhDecompressY
    {
        get => Require(_ecdhDecompressY);
        internal set => SetHandle(ref _ecdhDecompressY, value);
    }

    private MethodBuilder? _ecdhConvertKey;
    public MethodBuilder EcdhConvertKey
    {
        get => Require(_ecdhConvertKey);
        internal set => SetHandle(ref _ecdhConvertKey, value);
    }

    private MethodBuilder? _eCDHComputeSecretHelper;
    public MethodBuilder ECDHComputeSecretHelper
    {
        get => Require(_eCDHComputeSecretHelper);
        internal set => SetHandle(ref _eCDHComputeSecretHelper, value);
    }

    private ConstructorBuilder? _boundECDHMethodCtor;
    public ConstructorBuilder BoundECDHMethodCtor
    {
        get => Require(_boundECDHMethodCtor);
        internal set => SetHandle(ref _boundECDHMethodCtor, value);
    }

    // RSA encryption/decryption
    private MethodBuilder? _publicEncrypt;
    public MethodBuilder PublicEncrypt
    {
        get => Require(_publicEncrypt);
        internal set => SetHandle(ref _publicEncrypt, value);
    }

    private MethodBuilder? _privateDecrypt;
    public MethodBuilder PrivateDecrypt
    {
        get => Require(_privateDecrypt);
        internal set => SetHandle(ref _privateDecrypt, value);
    }

    private MethodBuilder? _privateEncrypt;
    public MethodBuilder PrivateEncrypt
    {
        get => Require(_privateEncrypt);
        internal set => SetHandle(ref _privateEncrypt, value);
    }

    private MethodBuilder? _publicDecrypt;
    public MethodBuilder PublicDecrypt
    {
        get => Require(_publicDecrypt);
        internal set => SetHandle(ref _publicDecrypt, value);
    }

    // RSA helpers (standalone - no SharpTS.dll dependency)
    private MethodBuilder? _extractKeyPem;
    public MethodBuilder ExtractKeyPem
    {
        get => Require(_extractKeyPem);
        internal set => SetHandle(ref _extractKeyPem, value);
    }

    private MethodBuilder? _rsaEncryptRaw;
    public MethodBuilder RsaEncryptRaw
    {
        get => Require(_rsaEncryptRaw);
        internal set => SetHandle(ref _rsaEncryptRaw, value);
    }

    private MethodBuilder? _rsaDecryptRaw;
    public MethodBuilder RsaDecryptRaw
    {
        get => Require(_rsaDecryptRaw);
        internal set => SetHandle(ref _rsaDecryptRaw, value);
    }

    // Key pair generation helpers (standalone)
    private MethodBuilder? _getOptionInt;
    public MethodBuilder GetOptionInt
    {
        get => Require(_getOptionInt);
        internal set => SetHandle(ref _getOptionInt, value);
    }

    private MethodBuilder? _getOptionString;
    public MethodBuilder GetOptionString
    {
        get => Require(_getOptionString);
        internal set => SetHandle(ref _getOptionString, value);
    }

    private MethodBuilder? _generateRsaKeyPairRaw;
    public MethodBuilder GenerateRsaKeyPairRaw
    {
        get => Require(_generateRsaKeyPairRaw);
        internal set => SetHandle(ref _generateRsaKeyPairRaw, value);
    }

    private MethodBuilder? _generateEcKeyPairRaw;
    public MethodBuilder GenerateEcKeyPairRaw
    {
        get => Require(_generateEcKeyPairRaw);
        internal set => SetHandle(ref _generateEcKeyPairRaw, value);
    }

    // HKDF key derivation
    private MethodBuilder? _hkdfSync;
    public MethodBuilder HkdfSync
    {
        get => Require(_hkdfSync);
        internal set => SetHandle(ref _hkdfSync, value);
    }

    // KeyObject support
    private MethodBuilder? _createSecretKey;
    public MethodBuilder CreateSecretKey
    {
        get => Require(_createSecretKey);
        internal set => SetHandle(ref _createSecretKey, value);
    }

    private MethodBuilder? _createPublicKey;
    public MethodBuilder CreatePublicKey
    {
        get => Require(_createPublicKey);
        internal set => SetHandle(ref _createPublicKey, value);
    }

    private MethodBuilder? _createPrivateKey;
    public MethodBuilder CreatePrivateKey
    {
        get => Require(_createPrivateKey);
        internal set => SetHandle(ref _createPrivateKey, value);
    }

    private ConstructorBuilder? _keyObjectCtorSecret;
    public ConstructorBuilder KeyObjectCtorSecret
    {
        get => Require(_keyObjectCtorSecret);
        internal set => SetHandle(ref _keyObjectCtorSecret, value);
    }

    private ConstructorBuilder? _keyObjectCtorAsym;
    public ConstructorBuilder KeyObjectCtorAsym
    {
        get => Require(_keyObjectCtorAsym);
        internal set => SetHandle(ref _keyObjectCtorAsym, value);
    }

    private ConstructorBuilder? _keyObjectCtorRsa;
    public ConstructorBuilder KeyObjectCtorRsa
    {
        get => Require(_keyObjectCtorRsa);
        internal set => SetHandle(ref _keyObjectCtorRsa, value);
    }

    private ConstructorBuilder? _keyObjectCtorEc;
    public ConstructorBuilder KeyObjectCtorEc
    {
        get => Require(_keyObjectCtorEc);
        internal set => SetHandle(ref _keyObjectCtorEc, value);
    }

    private MethodBuilder? _keyObjectToPublicKey;
    public MethodBuilder KeyObjectToPublicKey
    {
        get => Require(_keyObjectToPublicKey);
        internal set => SetHandle(ref _keyObjectToPublicKey, value);
    }

    private MethodBuilder? _keyObjectImportJwk;
    public MethodBuilder KeyObjectImportJwk
    {
        get => Require(_keyObjectImportJwk);
        internal set => SetHandle(ref _keyObjectImportJwk, value);
    }

    private MethodBuilder? _keyObjectImportDer;
    public MethodBuilder KeyObjectImportDer
    {
        get => Require(_keyObjectImportDer);
        internal set => SetHandle(ref _keyObjectImportDer, value);
    }

    private MethodBuilder? _keyObjectGetOption;
    public MethodBuilder KeyObjectGetOption
    {
        get => Require(_keyObjectGetOption);
        internal set => SetHandle(ref _keyObjectGetOption, value);
    }

    private MethodBuilder? _keyObjectDeriveSecret;
    public MethodBuilder KeyObjectDeriveSecret
    {
        get => Require(_keyObjectDeriveSecret);
        internal set => SetHandle(ref _keyObjectDeriveSecret, value);
    }

    // ============================================================
    // crypto epic #1054 — $CryptoPrimitives shared helpers and the
    // options-aware sign/verify cores (#1055/#1056/#1057/#1058/#1062).
    // ============================================================
    private MethodBuilder? _validateHashName;
    public MethodBuilder ValidateHashName
    {
        get => Require(_validateHashName);
        internal set => SetHandle(ref _validateHashName, value);
    }

    private MethodBuilder? _hashData;
    public MethodBuilder HashData
    {
        get => Require(_hashData);
        internal set => SetHandle(ref _hashData, value);
    }

    private MethodBuilder? _signHashName;
    public MethodBuilder SignHashName
    {
        get => Require(_signHashName);
        internal set => SetHandle(ref _signHashName, value);
    }

    private MethodBuilder? _encodeBytes;
    public MethodBuilder EncodeBytes
    {
        get => Require(_encodeBytes);
        internal set => SetHandle(ref _encodeBytes, value);
    }

    private MethodBuilder? _bytesFromAny;
    public MethodBuilder BytesFromAny
    {
        get => Require(_bytesFromAny);
        internal set => SetHandle(ref _bytesFromAny, value);
    }
    // One-shot sign/verify/hash cores on $Runtime (#1055)
    private MethodBuilder? _signDataEx;
    public MethodBuilder SignDataEx
    {
        get => Require(_signDataEx);
        internal set => SetHandle(ref _signDataEx, value);
    }

    private MethodBuilder? _verifyDataEx;
    public MethodBuilder VerifyDataEx
    {
        get => Require(_verifyDataEx);
        internal set => SetHandle(ref _verifyDataEx, value);
    }

    private MethodBuilder? _hashOneShot;
    public MethodBuilder HashOneShot
    {
        get => Require(_hashOneShot);
        internal set => SetHandle(ref _hashOneShot, value);
    }

    private MethodBuilder? _keyToPem;
    public MethodBuilder KeyToPem
    {
        get => Require(_keyToPem);
        internal set => SetHandle(ref _keyToPem, value);
    }
    // crypto.constants / getCipherInfo / getCurves (#1056/#1057/#1058)
    private MethodBuilder? _getConstants;
    public MethodBuilder GetConstants
    {
        get => Require(_getConstants);
        internal set => SetHandle(ref _getConstants, value);
    }

    private MethodBuilder? _getCipherInfo;
    public MethodBuilder GetCipherInfo
    {
        get => Require(_getCipherInfo);
        internal set => SetHandle(ref _getCipherInfo, value);
    }

    private MethodBuilder? _getCurves;
    public MethodBuilder GetCurves
    {
        get => Require(_getCurves);
        internal set => SetHandle(ref _getCurves, value);
    }
    // Primes (#1062) — Miller-Rabin over System.Numerics.BigInteger, pure IL
    private MethodBuilder? _isProbablyPrime;
    public MethodBuilder IsProbablyPrime
    {
        get => Require(_isProbablyPrime);
        internal set => SetHandle(ref _isProbablyPrime, value);
    }

    private MethodBuilder? _generatePrimeCore;
    public MethodBuilder GeneratePrimeCore
    {
        get => Require(_generatePrimeCore);
        internal set => SetHandle(ref _generatePrimeCore, value);
    }

    private MethodBuilder? _generatePrimeSyncObj;
    public MethodBuilder GeneratePrimeSyncObj
    {
        get => Require(_generatePrimeSyncObj);
        internal set => SetHandle(ref _generatePrimeSyncObj, value);
    }

    private MethodBuilder? _checkPrimeSyncObj;
    public MethodBuilder CheckPrimeSyncObj
    {
        get => Require(_checkPrimeSyncObj);
        internal set => SetHandle(ref _checkPrimeSyncObj, value);
    }

    // crypto KeyObject/ECDH completeness (#1059/#1060)
    /// <summary>Encodes an ECPoint per Node point-conversion format (uncompressed/compressed/hybrid).</summary>
    private MethodBuilder? _ecdhEncodePoint;
    public MethodBuilder EcdhEncodePoint
    {
        get => Require(_ecdhEncodePoint);
        internal set => SetHandle(ref _ecdhEncodePoint, value);
    }

    private ConstructorBuilder? _x509CertificateCtor;
    public ConstructorBuilder X509CertificateCtor
    {
        get => Require(_x509CertificateCtor);
        internal set => SetHandle(ref _x509CertificateCtor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Crypto metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"Crypto metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Crypto metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateHash;
        _ = RandomBytes;
        _ = RandomFillSync;
        _ = HashCtor;
        _ = CreateHmac;
        _ = HmacCtor;
        _ = CreateCipheriv;
        _ = CipherCtor;
        _ = CreateDecipheriv;
        _ = DecipherCtor;
        _ = Pbkdf2Sync;
        _ = ScryptSync;
        _ = ScryptDeriveBytes;
        _ = ScryptRotateLeft;
        _ = ScryptSalsa20Core;
        _ = ScryptBlockMix;
        _ = ScryptROMix;
        _ = TimingSafeEqual;
        _ = CreateSign;
        _ = SignCtor;
        _ = SignDataBytes;
        _ = CreateVerify;
        _ = VerifyCtor;
        _ = VerifyDataBytes;
        _ = GetHashes;
        _ = GetCiphers;
        _ = GenerateKeyPairSync;
        _ = CreateDiffieHellman;
        _ = GetDiffieHellman;
        _ = DiffieHellmanType;
        _ = DiffieHellmanCtorPrimeLength;
        _ = DiffieHellmanCtorPrimeGenerator;
        _ = DiffieHellmanCtorGroup;
        _ = DHGenerateKeys;
        _ = DHComputeSecret;
        _ = DHGetPrime;
        _ = DHGetGenerator;
        _ = DHGetPublicKey;
        _ = DHGetPrivateKey;
        _ = DHSetPublicKey;
        _ = DHSetPrivateKey;
        _ = DHGetMember;
        _ = DHEncodeResult;
        _ = DHDecodeInput;
        _ = DHGenerateRandomPrime;
        _ = DHIsProbablePrime;
        _ = DHBigIntFromBytes;
        _ = BoundDHMethodCtor;
        _ = CreateECDH;
        _ = ECDHType;
        _ = ECDHCtor;
        _ = ECDHGenerateKeys;
        _ = ECDHComputeSecret;
        _ = ECDHGetPublicKey;
        _ = ECDHGetPrivateKey;
        _ = ECDHSetPrivateKey;
        _ = ECDHGetMember;
        _ = ECDHEncodeResult;
        _ = ECDHDecodeInput;
        _ = EcdhDecompressY;
        _ = EcdhConvertKey;
        _ = ECDHComputeSecretHelper;
        _ = BoundECDHMethodCtor;
        _ = PublicEncrypt;
        _ = PrivateDecrypt;
        _ = PrivateEncrypt;
        _ = PublicDecrypt;
        _ = ExtractKeyPem;
        _ = RsaEncryptRaw;
        _ = RsaDecryptRaw;
        _ = GetOptionInt;
        _ = GetOptionString;
        _ = GenerateRsaKeyPairRaw;
        _ = GenerateEcKeyPairRaw;
        _ = HkdfSync;
        _ = CreateSecretKey;
        _ = CreatePublicKey;
        _ = CreatePrivateKey;
        _ = KeyObjectCtorSecret;
        _ = KeyObjectCtorAsym;
        _ = KeyObjectCtorRsa;
        _ = KeyObjectCtorEc;
        _ = KeyObjectToPublicKey;
        _ = KeyObjectImportJwk;
        _ = KeyObjectImportDer;
        _ = KeyObjectGetOption;
        _ = KeyObjectDeriveSecret;
        _ = ValidateHashName;
        _ = HashData;
        _ = SignHashName;
        _ = EncodeBytes;
        _ = BytesFromAny;
        _ = SignDataEx;
        _ = VerifyDataEx;
        _ = HashOneShot;
        _ = KeyToPem;
        _ = GetConstants;
        _ = GetCipherInfo;
        _ = GetCurves;
        _ = IsProbablyPrime;
        _ = GeneratePrimeCore;
        _ = GeneratePrimeSyncObj;
        _ = CheckPrimeSyncObj;
        _ = EcdhEncodePoint;
        _ = X509CertificateCtor;
        IsComplete = true;
    }
}
