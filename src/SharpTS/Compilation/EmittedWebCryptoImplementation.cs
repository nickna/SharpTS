using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional WebCrypto declarations for one compilation. Handles are readable after
/// declaration; completion validates and freezes them after body emission and type finalization.
/// </summary>
public sealed class EmittedWebCryptoImplementation
{
    internal EmittedWebCryptoImplementation() { }

    public bool IsComplete { get; private set; }

    // (string) → HashAlgorithmName
    private MethodBuilder? _hashAlgorithm;
    public MethodBuilder HashAlgorithm
    {
        get => Require(_hashAlgorithm);
        internal set => SetHandle(ref _hashAlgorithm, value);
    }

    // (object) → string (lowercase)
    private MethodBuilder? _mapHash;
    public MethodBuilder MapHash
    {
        get => Require(_mapHash);
        internal set => SetHandle(ref _mapHash, value);
    }

    // (object) → string (UPPER)
    private MethodBuilder? _algorithmName;
    public MethodBuilder AlgorithmName
    {
        get => Require(_algorithmName);
        internal set => SetHandle(ref _algorithmName, value);
    }

    // (object, string) → object (undefined→null)
    private MethodBuilder? _parameter;
    public MethodBuilder Parameter
    {
        get => Require(_parameter);
        internal set => SetHandle(ref _parameter, value);
    }

    // (object, int) → int
    private MethodBuilder? _intParameter;
    public MethodBuilder IntParameter
    {
        get => Require(_intParameter);
        internal set => SetHandle(ref _intParameter, value);
    }

    // (object) → byte[]
    private MethodBuilder? _toBytes;
    public MethodBuilder ToBytes
    {
        get => Require(_toBytes);
        internal set => SetHandle(ref _toBytes, value);
    }

    // (byte[]) → object
    private MethodBuilder? _toArrayBuffer;
    public MethodBuilder ToArrayBuffer
    {
        get => Require(_toArrayBuffer);
        internal set => SetHandle(ref _toArrayBuffer, value);
    }

    // (object) → object ($Promise)
    private MethodBuilder? _resolved;
    public MethodBuilder Resolved
    {
        get => Require(_resolved);
        internal set => SetHandle(ref _resolved, value);
    }

    // (Exception) → object (rejected $Promise)
    private MethodBuilder? _rejected;
    public MethodBuilder Rejected
    {
        get => Require(_rejected);
        internal set => SetHandle(ref _rejected, value);
    }

    // (string) → object (throws)
    private MethodBuilder? _throw;
    public MethodBuilder Throw
    {
        get => Require(_throw);
        internal set => SetHandle(ref _throw, value);
    }

    // (string, byte[]) → byte[]
    private MethodBuilder? _digest;
    public MethodBuilder Digest
    {
        get => Require(_digest);
        internal set => SetHandle(ref _digest, value);
    }

    // (string, byte[], byte[]) → byte[]
    private MethodBuilder? _hmac;
    public MethodBuilder Hmac
    {
        get => Require(_hmac);
        internal set => SetHandle(ref _hmac, value);
    }

    // (string) → int
    private MethodBuilder? _digestLen;
    public MethodBuilder DigestLen
    {
        get => Require(_digestLen);
        internal set => SetHandle(ref _digestLen, value);
    }

    // (byte[], byte[], byte[]?, int, byte[], bool) → byte[]
    private MethodBuilder? _aesGcm;
    public MethodBuilder AesGcm
    {
        get => Require(_aesGcm);
        internal set => SetHandle(ref _aesGcm, value);
    }

    // (byte[], byte[], byte[], bool) → byte[]
    private MethodBuilder? _aesCbc;
    public MethodBuilder AesCbc
    {
        get => Require(_aesCbc);
        internal set => SetHandle(ref _aesCbc, value);
    }

    // (byte[], bool, string, byte[], bool) → byte[]
    private MethodBuilder? _rsaOaep;
    public MethodBuilder RsaOaep
    {
        get => Require(_rsaOaep);
        internal set => SetHandle(ref _rsaOaep, value);
    }

    // (byte[], bool, string, bool, byte[], byte[]?) → object
    private MethodBuilder? _rsaSignVerify;
    public MethodBuilder RsaSignVerify
    {
        get => Require(_rsaSignVerify);
        internal set => SetHandle(ref _rsaSignVerify, value);
    }

    // (byte[], bool, string, byte[], byte[]?) → object
    private MethodBuilder? _ecdsaSignVerify;
    public MethodBuilder EcdsaSignVerify
    {
        get => Require(_ecdsaSignVerify);
        internal set => SetHandle(ref _ecdsaSignVerify, value);
    }

    // (byte[], byte[], int, string, int) → byte[]
    private MethodBuilder? _pbkdf2;
    public MethodBuilder Pbkdf2
    {
        get => Require(_pbkdf2);
        internal set => SetHandle(ref _pbkdf2, value);
    }

    // (string, byte[], int, byte[], byte[]) → byte[]
    private MethodBuilder? _hkdf;
    public MethodBuilder Hkdf
    {
        get => Require(_hkdf);
        internal set => SetHandle(ref _hkdf, value);
    }

    // (byte[], byte[], int) → byte[]
    private MethodBuilder? _ecdhDerive;
    public MethodBuilder EcdhDerive
    {
        get => Require(_ecdhDerive);
        internal set => SetHandle(ref _ecdhDerive, value);
    }

    // (int) → object[] { spki, pkcs8 }
    private MethodBuilder? _genRsa;
    public MethodBuilder GenRsa
    {
        get => Require(_genRsa);
        internal set => SetHandle(ref _genRsa, value);
    }

    // (string) → object[] { spki, pkcs8 }
    private MethodBuilder? _genEc;
    public MethodBuilder GenEc
    {
        get => Require(_genEc);
        internal set => SetHandle(ref _genEc, value);
    }

    // (string canonical) → ECCurve
    private MethodBuilder? _curve;
    public MethodBuilder Curve
    {
        get => Require(_curve);
        internal set => SetHandle(ref _curve, value);
    }

    // (object) → string ("P-256"...)
    private MethodBuilder? _canonicalCurve;
    public MethodBuilder CanonicalCurve
    {
        get => Require(_canonicalCurve);
        internal set => SetHandle(ref _canonicalCurve, value);
    }

    // (byte[], string) → byte[]
    private MethodBuilder? _ecRawToSpki;
    public MethodBuilder EcRawToSpki
    {
        get => Require(_ecRawToSpki);
        internal set => SetHandle(ref _ecRawToSpki, value);
    }

    // (byte[]) → byte[]
    private MethodBuilder? _ecSpkiToRaw;
    public MethodBuilder EcSpkiToRaw
    {
        get => Require(_ecSpkiToRaw);
        internal set => SetHandle(ref _ecSpkiToRaw, value);
    }

    // (byte[], bool) → int (KeySize)
    private MethodBuilder? _importRsaCheck;
    public MethodBuilder ImportRsaCheck
    {
        get => Require(_importRsaCheck);
        internal set => SetHandle(ref _importRsaCheck, value);
    }

    // (byte[], bool) → void
    private MethodBuilder? _importEcCheck;
    public MethodBuilder ImportEcCheck
    {
        get => Require(_importEcCheck);
        internal set => SetHandle(ref _importEcCheck, value);
    }

    // (byte[]) → string
    private MethodBuilder? _base64Url;
    public MethodBuilder Base64Url
    {
        get => Require(_base64Url);
        internal set => SetHandle(ref _base64Url, value);
    }

    private TypeBuilder? _cryptoKeyType;
    public TypeBuilder CryptoKeyType
    {
        get => Require(_cryptoKeyType);
        internal set => SetHandle(ref _cryptoKeyType, value);
    }

    private ConstructorBuilder? _cryptoKeyCtor;
    public ConstructorBuilder CryptoKeyCtor
    {
        get => Require(_cryptoKeyCtor);
        internal set => SetHandle(ref _cryptoKeyCtor, value);
    }

    // "secret" | "public" | "private"
    private FieldBuilder? _keyKindField;
    public FieldBuilder KeyKindField
    {
        get => Require(_keyKindField);
        internal set => SetHandle(ref _keyKindField, value);
    }

    private FieldBuilder? _keyExtractableField;
    public FieldBuilder KeyExtractableField
    {
        get => Require(_keyExtractableField);
        internal set => SetHandle(ref _keyExtractableField, value);
    }

    // Dictionary<string, object>
    private FieldBuilder? _keyAlgorithmField;
    public FieldBuilder KeyAlgorithmField
    {
        get => Require(_keyAlgorithmField);
        internal set => SetHandle(ref _keyAlgorithmField, value);
    }

    // caller-provided usages value
    private FieldBuilder? _keyUsagesField;
    public FieldBuilder KeyUsagesField
    {
        get => Require(_keyUsagesField);
        internal set => SetHandle(ref _keyUsagesField, value);
    }

    // raw secret / PKCS#8 / SPKI bytes
    private FieldBuilder? _keyMaterialField;
    public FieldBuilder KeyMaterialField
    {
        get => Require(_keyMaterialField);
        internal set => SetHandle(ref _keyMaterialField, value);
    }

    // UPPER WebCrypto name
    private FieldBuilder? _keyAlgoNameField;
    public FieldBuilder KeyAlgoNameField
    {
        get => Require(_keyAlgoNameField);
        internal set => SetHandle(ref _keyAlgoNameField, value);
    }

    // lowercase digest or null
    private FieldBuilder? _keyHashField;
    public FieldBuilder KeyHashField
    {
        get => Require(_keyHashField);
        internal set => SetHandle(ref _keyHashField, value);
    }

    // canonical curve or null
    private FieldBuilder? _keyCurveField;
    public FieldBuilder KeyCurveField
    {
        get => Require(_keyCurveField);
        internal set => SetHandle(ref _keyCurveField, value);
    }

    private ConstructorBuilder? _subtleCtor;
    public ConstructorBuilder SubtleCtor
    {
        get => Require(_subtleCtor);
        internal set => SetHandle(ref _subtleCtor, value);
    }

    private MethodBuilder? _subtleGenerateKeyCore;
    public MethodBuilder SubtleGenerateKeyCore
    {
        get => Require(_subtleGenerateKeyCore);
        internal set => SetHandle(ref _subtleGenerateKeyCore, value);
    }

    private MethodBuilder? _subtleImportKeyCore;
    public MethodBuilder SubtleImportKeyCore
    {
        get => Require(_subtleImportKeyCore);
        internal set => SetHandle(ref _subtleImportKeyCore, value);
    }

    private MethodBuilder? _subtleExportKeyCore;
    public MethodBuilder SubtleExportKeyCore
    {
        get => Require(_subtleExportKeyCore);
        internal set => SetHandle(ref _subtleExportKeyCore, value);
    }

    private MethodBuilder? _subtleEncDecCore;
    public MethodBuilder SubtleEncDecCore
    {
        get => Require(_subtleEncDecCore);
        internal set => SetHandle(ref _subtleEncDecCore, value);
    }

    private MethodBuilder? _subtleSignVerifyCore;
    public MethodBuilder SubtleSignVerifyCore
    {
        get => Require(_subtleSignVerifyCore);
        internal set => SetHandle(ref _subtleSignVerifyCore, value);
    }

    private MethodBuilder? _subtleDeriveBitsCore;
    public MethodBuilder SubtleDeriveBitsCore
    {
        get => Require(_subtleDeriveBitsCore);
        internal set => SetHandle(ref _subtleDeriveBitsCore, value);
    }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private MethodBuilder? _getRandomValues;
    public MethodBuilder GetRandomValues
    {
        get => Require(_getRandomValues);
        internal set => SetHandle(ref _getRandomValues, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"WebCrypto implementation metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("WebCrypto implementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = HashAlgorithm;
        _ = MapHash;
        _ = AlgorithmName;
        _ = Parameter;
        _ = IntParameter;
        _ = ToBytes;
        _ = ToArrayBuffer;
        _ = Resolved;
        _ = Rejected;
        _ = Throw;
        _ = Digest;
        _ = Hmac;
        _ = DigestLen;
        _ = AesGcm;
        _ = AesCbc;
        _ = RsaOaep;
        _ = RsaSignVerify;
        _ = EcdsaSignVerify;
        _ = Pbkdf2;
        _ = Hkdf;
        _ = EcdhDerive;
        _ = GenRsa;
        _ = GenEc;
        _ = Curve;
        _ = CanonicalCurve;
        _ = EcRawToSpki;
        _ = EcSpkiToRaw;
        _ = ImportRsaCheck;
        _ = ImportEcCheck;
        _ = Base64Url;
        _ = CryptoKeyType;
        _ = CryptoKeyCtor;
        _ = KeyKindField;
        _ = KeyExtractableField;
        _ = KeyAlgorithmField;
        _ = KeyUsagesField;
        _ = KeyMaterialField;
        _ = KeyAlgoNameField;
        _ = KeyHashField;
        _ = KeyCurveField;
        _ = SubtleCtor;
        _ = SubtleGenerateKeyCore;
        _ = SubtleImportKeyCore;
        _ = SubtleExportKeyCore;
        _ = SubtleEncDecCore;
        _ = SubtleSignVerifyCore;
        _ = SubtleDeriveBitsCore;
        _ = Type;
        _ = GetRandomValues;
        IsComplete = true;
    }
}
