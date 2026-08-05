using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Defines the type signatures for built-in Node.js-compatible modules.
/// </summary>
public static partial class BuiltInModuleTypes
{
    private static TypeInfo BooleanType => TypeInfo.Primitive.Boolean;

    /// <summary>
    /// Gets the exported types for the os module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetOsModuleTypes()
    {
        var numberType = TypeInfo.Primitive.Number;

        return new Dictionary<string, TypeInfo>
        {
            // Methods returning strings
            ["platform"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["arch"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["hostname"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["homedir"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["tmpdir"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["type"] = new TypeInfo.Function([], TypeInfo.String.Shared),
            ["release"] = new TypeInfo.Function([], TypeInfo.String.Shared),

            // Methods returning numbers
            ["totalmem"] = new TypeInfo.Function([], numberType),
            ["freemem"] = new TypeInfo.Function([], numberType),

            // Methods returning arrays/objects
            ["cpus"] = new TypeInfo.Function([],
                new TypeInfo.Array(new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["model"] = TypeInfo.String.Shared,
                    ["speed"] = numberType
                }.ToFrozenDictionary()))
            ),
            ["userInfo"] = new TypeInfo.Function([],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["username"] = TypeInfo.String.Shared,
                    ["uid"] = numberType,
                    ["gid"] = numberType,
                    ["shell"] = new TypeInfo.Union([TypeInfo.String.Shared, TypeInfo.Null.Shared]),
                    ["homedir"] = TypeInfo.String.Shared
                }.ToFrozenDictionary())
            ),

            // loadavg() -> number[] (1, 5, 15 minute load averages)
            ["loadavg"] = new TypeInfo.Function([], new TypeInfo.Array(numberType)),

            // networkInterfaces() -> object with interface names as keys
            ["networkInterfaces"] = new TypeInfo.Function([],
                TypeInfo.Any.Shared  // Returns dynamic object structure
            ),

            // Properties
            ["EOL"] = TypeInfo.String.Shared
        };
    }

    /// <summary>
    /// Gets the exported types for the crypto module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetCryptoModuleTypes()
    {
        var numberType = TypeInfo.Primitive.Number;
        var stringType = TypeInfo.String.Shared;
        var anyType = TypeInfo.Any.Shared;
        var bufferType = TypeInfo.Buffer.Shared;
        var bufferOrStringType = new TypeInfo.Union([bufferType, stringType]);
        var voidType = TypeInfo.Void.Shared;

        return new Dictionary<string, TypeInfo>
        {
            // Hash methods
            // createHash(algorithm, options?) — options carries outputLength for XOFs
            // like shake128/shake256 (#1059).
            ["createHash"] = new TypeInfo.Function([stringType, anyType], anyType, RequiredParams: 1), // Returns Hash object
            ["createHmac"] = new TypeInfo.Function([stringType, anyType], anyType), // Returns Hmac object

            // Cipher methods
            ["createCipheriv"] = new TypeInfo.Function(
                [stringType, bufferOrStringType, bufferOrStringType],
                anyType), // Returns Cipher object
            ["createDecipheriv"] = new TypeInfo.Function(
                [stringType, bufferOrStringType, bufferOrStringType],
                anyType), // Returns Decipher object

            // Random methods
            ["randomBytes"] = new TypeInfo.Function([numberType], bufferType),
            // randomFillSync(buffer, offset?, size?) -> Buffer
            ["randomFillSync"] = new TypeInfo.Function(
                [bufferType, numberType, numberType],
                bufferType,
                RequiredParams: 1),
            ["randomUUID"] = new TypeInfo.Function([], stringType),
            ["randomInt"] = new TypeInfo.Function([numberType, numberType], numberType, RequiredParams: 1),

            // Key derivation functions
            // pbkdf2Sync(password, salt, iterations, keylen, digest) -> Buffer
            ["pbkdf2Sync"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType, numberType, numberType, stringType],
                bufferType),
            // scryptSync(password, salt, keylen, options?) -> Buffer
            ["scryptSync"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType, numberType, anyType],
                bufferType,
                RequiredParams: 3),

            // Timing-safe comparison
            // timingSafeEqual(a, b) -> boolean
            ["timingSafeEqual"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType],
                BooleanType),

            // Signing and verification
            // createSign(algorithm) -> Sign object
            ["createSign"] = new TypeInfo.Function([stringType], anyType),
            // createVerify(algorithm) -> Verify object
            ["createVerify"] = new TypeInfo.Function([stringType], anyType),

            // Discovery functions
            // getHashes() -> string[]
            ["getHashes"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            // getCiphers() -> string[]
            ["getCiphers"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),

            // Key pair generation
            // generateKeyPairSync(type, options?) -> { publicKey, privateKey }
            ["generateKeyPairSync"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["publicKey"] = stringType,
                    ["privateKey"] = stringType
                }.ToFrozenDictionary()),
                RequiredParams: 1),

            // Diffie-Hellman key exchange
            // createDiffieHellman(primeLength) or createDiffieHellman(prime, generator?) -> DiffieHellman object
            ["createDiffieHellman"] = new TypeInfo.Function(
                [new TypeInfo.Union([numberType, bufferOrStringType]), bufferOrStringType],
                anyType,
                RequiredParams: 1),
            // getDiffieHellman(groupName) -> DiffieHellman object
            ["getDiffieHellman"] = new TypeInfo.Function([stringType], anyType),

            // Elliptic curve Diffie-Hellman
            // createECDH(curveName) -> ECDH object
            ["createECDH"] = new TypeInfo.Function([stringType], anyType),

            // RSA encryption/decryption
            // publicEncrypt(key, buffer) -> Buffer
            ["publicEncrypt"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType],
                bufferType),
            // privateDecrypt(key, buffer) -> Buffer
            ["privateDecrypt"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType],
                bufferType),
            // privateEncrypt(key, buffer) -> Buffer (PKCS#1 v1.5)
            ["privateEncrypt"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType],
                bufferType),
            // publicDecrypt(key, buffer) -> Buffer (PKCS#1 v1.5)
            ["publicDecrypt"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType],
                bufferType),

            // HKDF key derivation
            // hkdfSync(digest, ikm, salt, info, keylen) -> Buffer
            ["hkdfSync"] = new TypeInfo.Function(
                [stringType, bufferOrStringType, bufferOrStringType, bufferOrStringType, numberType],
                bufferType),

            // KeyObject factory methods
            // createSecretKey(key, encoding?) -> KeyObject
            ["createSecretKey"] = new TypeInfo.Function(
                [bufferOrStringType, stringType],
                anyType, // Returns KeyObject
                RequiredParams: 1),
            // createPublicKey(key) -> KeyObject
            // Accepts string, Buffer, or object with 'key' property
            ["createPublicKey"] = new TypeInfo.Function(
                [anyType],
                anyType), // Returns KeyObject
            // createPrivateKey(key) -> KeyObject
            // Accepts string, Buffer, or object with 'key' property
            ["createPrivateKey"] = new TypeInfo.Function(
                [anyType],
                anyType), // Returns KeyObject

            // Async (callback-based) key derivation
            // pbkdf2(password, salt, iterations, keylen, digest, callback) -> void
            ["pbkdf2"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType, numberType, numberType, stringType, anyType],
                voidType),
            // scrypt(password, salt, keylen[, options], callback) -> void
            ["scrypt"] = new TypeInfo.Function(
                [bufferOrStringType, bufferOrStringType, numberType, anyType, anyType],
                voidType,
                RequiredParams: 4),
            // generateKeyPair(type[, options], callback) -> void
            ["generateKeyPair"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2),
            // hkdf(digest, ikm, salt, info, keylen, callback) -> void
            ["hkdf"] = new TypeInfo.Function(
                [stringType, bufferOrStringType, bufferOrStringType, bufferOrStringType, numberType, anyType],
                voidType),

            // === Epic #1054 additions ===
            // One-shot digest/sign/verify (#1055)
            ["hash"] = new TypeInfo.Function([stringType, bufferOrStringType, stringType], anyType, RequiredParams: 2),
            ["sign"] = new TypeInfo.Function([anyType, bufferOrStringType, anyType, anyType], anyType, RequiredParams: 3),
            ["verify"] = new TypeInfo.Function([anyType, bufferOrStringType, anyType, bufferOrStringType, anyType], anyType, RequiredParams: 4),
            // crypto.constants (#1056)
            ["constants"] = anyType,
            // Cipher/curve discovery (#1057/#1058)
            ["getCipherInfo"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["getCurves"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            // Small wins (#1058)
            ["randomFill"] = new TypeInfo.Function([bufferType, anyType, anyType, anyType], voidType, RequiredParams: 2),
            ["generateKey"] = new TypeInfo.Function([stringType, anyType, anyType], voidType, RequiredParams: 3),
            ["generateKeySync"] = new TypeInfo.Function([stringType, anyType], anyType),
            // DH/ECDH completeness + FIPS shims (#1060)
            ["diffieHellman"] = new TypeInfo.Function([anyType], bufferType),
            ["createDiffieHellmanGroup"] = new TypeInfo.Function([stringType], anyType),
            ["getFips"] = new TypeInfo.Function([], numberType),
            ["setFips"] = new TypeInfo.Function([BooleanType], voidType),
            ["fips"] = BooleanType,
            ["ECDH"] = anyType,
            // Primes (#1062)
            ["generatePrime"] = new TypeInfo.Function([numberType, anyType, anyType], voidType, RequiredParams: 2),
            ["generatePrimeSync"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["checkPrime"] = new TypeInfo.Function([anyType, anyType, anyType], voidType, RequiredParams: 2),
            ["checkPrimeSync"] = new TypeInfo.Function([anyType, anyType], BooleanType, RequiredParams: 1),
            // X509Certificate class (#1064) — any-typed so `new crypto.X509Certificate(...)`
            // and instance member access type-check; a refined shape lands with #1065.
            ["X509Certificate"] = anyType,
            // WebCrypto (#1063): crypto.webcrypto / crypto.subtle / crypto.getRandomValues.
            // Kept as `any` here; the full SubtleCrypto shape is #1065's type pass.
            ["webcrypto"] = anyType,
            ["subtle"] = anyType,
            ["getRandomValues"] = new TypeInfo.Function([anyType], anyType)
        };
    }

    // GetUtilModuleTypes removed: the 'util' module now lives in
    // stdlib/node/util.ts. Its export types are derived from the TS source
    // at import time by the embedded-stdlib loader, so there is no longer
    // a hand-maintained C# type map.

    /// <summary>
    /// Gets the exported types for the readline module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetReadlineModuleTypes()
    {
        var stringType = TypeInfo.String.Shared;
        var anyType = TypeInfo.Any.Shared;
        var voidType = TypeInfo.Void.Shared;
        var boolType = BooleanType;
        var numberType = TypeInfo.Primitive.Number;

        // Interface type returned by createInterface
        var interfaceType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, RequiredParams: 1, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            // Readline methods
            ["question"] = new TypeInfo.Function([stringType, anyType], voidType),
            ["close"] = new TypeInfo.Function([], anyType),
            ["prompt"] = new TypeInfo.Function([boolType], voidType, RequiredParams: 0),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["write"] = new TypeInfo.Function([stringType], voidType),
            ["setPrompt"] = new TypeInfo.Function([stringType], voidType),
            ["getPrompt"] = new TypeInfo.Function([], stringType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["questionSync"] = new TypeInfo.Function([stringType], stringType),
            ["createInterface"] = new TypeInfo.Function([anyType], interfaceType, RequiredParams: 0)
        };
    }

    /// <summary>
    /// Gets the exported types for the buffer module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetBufferModuleTypes()
    {
        var numberType = TypeInfo.Primitive.Number;
        var stringType = TypeInfo.String.Shared;
        var bufferType = TypeInfo.Buffer.Shared;

        // Buffer constructor type - an object with static methods
        var bufferConstructorType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["from"] = new TypeInfo.Function(
                [new TypeInfo.Union([stringType, new TypeInfo.Array(numberType), bufferType]), stringType],
                bufferType,
                RequiredParams: 1),
            ["alloc"] = new TypeInfo.Function(
                [numberType, TypeInfo.Any.Shared, stringType],
                bufferType,
                RequiredParams: 1),
            ["allocUnsafe"] = new TypeInfo.Function([numberType], bufferType),
            ["allocUnsafeSlow"] = new TypeInfo.Function([numberType], bufferType),
            ["concat"] = new TypeInfo.Function(
                [new TypeInfo.Array(bufferType), numberType],
                bufferType,
                RequiredParams: 1),
            ["isBuffer"] = new TypeInfo.Function([TypeInfo.Any.Shared], BooleanType),
            ["byteLength"] = new TypeInfo.Function(
                [new TypeInfo.Union([stringType, bufferType]), stringType],
                numberType,
                RequiredParams: 1),
            ["compare"] = new TypeInfo.Function([bufferType, bufferType], numberType),
            ["isEncoding"] = new TypeInfo.Function([stringType], BooleanType)
        }.ToFrozenDictionary());

        var anyType = TypeInfo.Any.Shared;
        var byteSource = new TypeInfo.Union([bufferType, new TypeInfo.Array(numberType), stringType]);

        return new Dictionary<string, TypeInfo>
        {
            ["Buffer"] = bufferConstructorType,

            // Blob/File constructors (also globals) + resolveObjectURL
            ["Blob"] = anyType,
            ["File"] = anyType,
            ["resolveObjectURL"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 1),

            // Base64 (also globals)
            ["atob"] = new TypeInfo.Function([stringType], stringType),
            ["btoa"] = new TypeInfo.Function([stringType], stringType),

            // Validation
            ["isUtf8"] = new TypeInfo.Function([byteSource], BooleanType),
            ["isAscii"] = new TypeInfo.Function([byteSource], BooleanType),

            // Encoding conversion
            ["transcode"] = new TypeInfo.Function([byteSource, stringType, stringType], bufferType),

            // Deprecated unsafe allocation
            ["SlowBuffer"] = new TypeInfo.Function([numberType], bufferType),

            // Constants
            ["constants"] = anyType,
            ["kMaxLength"] = numberType,
            ["kStringMaxLength"] = numberType,
            ["INSPECT_MAX_BYTES"] = numberType,
        };
    }

    /// <summary>
    /// Gets the exported types for <c>primitive:zlib</c> — the narrow compression
    /// surface behind the stdlib/node/zlib.ts facade (sync one-shots, streaming
    /// create*, crc32). The facade owns constants/codes and the async forms.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetZlibModuleTypes()
    {
        var bufferType = TypeInfo.Buffer.Shared;
        var anyType = TypeInfo.Any.Shared;
        var inputType = new TypeInfo.Union([bufferType, TypeInfo.String.Shared]);
        var transformType = anyType; // Transform stream type

        return new Dictionary<string, TypeInfo>
        {
            // Gzip methods
            ["gzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["gunzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Deflate methods (with zlib header)
            ["deflateSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["inflateSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // DeflateRaw methods (no header)
            ["deflateRawSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["inflateRawSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Brotli methods
            ["brotliCompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["brotliDecompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Zstd methods
            ["zstdCompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["zstdDecompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Unzip (auto-detect)
            ["unzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, TypeInfo.String.Shared]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Streaming APIs (return Transform streams)
            ["createGzip"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createGunzip"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createDeflate"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createInflate"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createDeflateRaw"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createInflateRaw"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createBrotliCompress"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createBrotliDecompress"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createZstdCompress"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createZstdDecompress"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),
            ["createUnzip"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),

            // Checksums (Node 22+): crc32(data[, value]) -> number
            ["crc32"] = new TypeInfo.Function(
                [inputType, TypeInfo.Primitive.Number],
                TypeInfo.Primitive.Number,
                RequiredParams: 1)
        };
    }

    /// <summary>
    /// Gets the exported types for a built-in module by name.
    /// </summary>
    /// <param name="moduleName">The module name (e.g., "path", "fs", "os").</param>
    /// <returns>The exported types, or null if not a known built-in module.</returns>
    public static Dictionary<string, TypeInfo>? GetModuleTypes(string moduleName)
    {
        return moduleName switch
        {
            // "path" — migrated to stdlib/node/path.ts; types flow from the TS source.
            // "os" — migrated to stdlib/node/os.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:os reuse GetOsModuleTypes via GetPrimitiveTypes.
            // "fs" — migrated to stdlib/node/fs.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:fs reuse GetFsModuleTypes via GetPrimitiveTypes.
            // "fs/promises" — migrated to stdlib/node/fs/promises.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:fs/promises reuse GetFsPromisesModuleTypes via GetPrimitiveTypes.
            // "assert" — migrated to stdlib/node/assert.ts; types flow from the TS source.
            // "url" — migrated to stdlib/node/url.ts; types flow from the TS source.
            // "util" — migrated to stdlib/node/util.ts; types flow from the TS source.
            // "process" — migrated to stdlib/node/process.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:process reuse GetProcessModuleTypes via GetPrimitiveTypes.
            "crypto" => GetCryptoModuleTypes(),
            // "readline" — migrated to stdlib/node/readline.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:readline reuse GetReadlineModuleTypes via GetPrimitiveTypes.
            "child_process" => GetChildProcessModuleTypes(),
            "buffer" => GetBufferModuleTypes(),
            // "zlib" — migrated to stdlib/node/zlib.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:zlib reuse GetZlibModuleTypes via GetPrimitiveTypes.
            // "events" — migrated to stdlib/node/events.ts; types flow from the TS source.
            // "timers" / "timers/promises" — migrated to stdlib/node/timers{,/promises}.ts;
            //   types flow from the TS source. Primitive-layer types reuse the
            //   same shapes via GetPrimitiveTypes (GetTimersModuleTypes stays public).
            // "string_decoder" — migrated to stdlib/node/string_decoder.ts; types flow from the TS source.
            // "perf_hooks" — migrated to stdlib/node/perf_hooks.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:perf are in GetPerfPrimitiveTypes.
            "stream" => GetStreamModuleTypes(),
            "stream/promises" => GetStreamPromisesModuleTypes(),
            "stream/web" => GetStreamWebModuleTypes(),
            "http" => GetHttpModuleTypes(),
            "https" => GetHttpModuleTypes(),
            "dns" => GetDnsModuleTypes(),
            "dns/promises" => GetDnsPromisesModuleTypes(),
            "net" => GetNetModuleTypes(),
            "tls" => GetTlsModuleTypes(),
            "dgram" => GetDgramModuleTypes(),
            "cluster" => GetClusterModuleTypes(),
            "vm" => GetVmModuleTypes(),
            "sharpts:execution" => GetSourceExecutionModuleTypes(),
            // "async_hooks" — migrated to stdlib/node/async_hooks.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:async_hooks are in GetAsyncHooksPrimitiveTypes.
            "worker_threads" => GetWorkerThreadsModuleTypes(),
            // "tty" — migrated to stdlib/node/tty.ts; types flow from the TS source.
            //   Primitive-layer types for primitive:tty are in GetTtyPrimitiveTypes.
            _ => null
        };
    }

    /// <summary>
    /// Gets the exported types for a primitive module (name without the
    /// <c>primitive:</c> prefix). Primitives share type shape with their
    /// matching user-facing module — stdlib TS code targets the same surface
    /// Node's docs describe, just reached through the stdlib-internal specifier.
    /// </summary>
    public static Dictionary<string, TypeInfo>? GetPrimitiveTypes(string primitiveName)
    {
        return primitiveName switch
        {
            "os" => GetOsModuleTypes(),
            "process" => GetProcessModuleTypes(),
            "perf" => GetPerfPrimitiveTypes(),
            "tty" => GetTtyPrimitiveTypes(),
            "async_hooks" => GetAsyncHooksPrimitiveTypes(),
            // Primitive timer types reuse the user-facing module type shapes — the
            // primitive surface matches the Node surface; the TS facade just
            // arity-dispatches around the spread-compiler gap.
            "timers" => GetTimersModuleTypes(),
            "timers/promises" => GetTimersPromisesModuleTypes(),
            // Readline's primitive surface is the full module surface — the TS
            // facade wraps the returned Interface and forwards calls dynamically.
            "readline" => GetReadlineModuleTypes(),
            // Primitive fs types reuse the user-facing module type shapes — the
            // primitive surface matches the Node surface; the TS facade re-exports
            // the sync ops and derives the callback forms from primitive:fs/promises.
            "fs" => GetFsModuleTypes(),
            "fs/promises" => GetFsPromisesModuleTypes(),
            // Primitive zlib types are the narrow compression surface (sync one-shots,
            // streaming create*, crc32); the TS facade owns constants/codes and the
            // async callback forms.
            "zlib" => GetZlibModuleTypes(),
            _ => null
        };
    }

    /// <summary>
    /// Types for <c>primitive:tty</c> — just <c>isatty(fd)</c> returning a boolean.
    /// </summary>
    private static Dictionary<string, TypeInfo> GetTtyPrimitiveTypes()
    {
        var numberType = TypeInfo.Primitive.Number;
        var booleanType = TypeInfo.Primitive.Boolean;
        return new Dictionary<string, TypeInfo>
        {
            ["isatty"] = new TypeInfo.Function([numberType], booleanType),
        };
    }

    /// <summary>
    /// Types for <c>primitive:async_hooks</c> — just <c>create()</c> returning an
    /// opaque AsyncLocalStorage backing instance (typed <c>any</c>; TS wraps it).
    /// </summary>
    private static Dictionary<string, TypeInfo> GetAsyncHooksPrimitiveTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        return new Dictionary<string, TypeInfo>
        {
            ["create"] = new TypeInfo.Function([], anyType),
        };
    }

    /// <summary>
    /// Types for <c>primitive:perf</c> — just <c>now()</c> returning high-res ms.
    /// The full perf_hooks surface (mark, measure, etc.) is typed from the TS source.
    /// </summary>
    private static Dictionary<string, TypeInfo> GetPerfPrimitiveTypes()
    {
        var numberType = TypeInfo.Primitive.Number;
        return new Dictionary<string, TypeInfo>
        {
            ["now"] = new TypeInfo.Function([], numberType),
        };
    }

    /// <summary>
    /// Gets the exported types for the timers module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTimersModuleTypes()
    {
        var timeoutType = TypeInfo.Any.Shared; // Timeout handle type
        var callbackType = new TypeInfo.Function([TypeInfo.Any.Shared], TypeInfo.Void.Shared, HasRestParam: true);

        return new Dictionary<string, TypeInfo>
        {
            ["setTimeout"] = new TypeInfo.Function(
                [callbackType, TypeInfo.Primitive.Number, TypeInfo.Any.Shared],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearTimeout"] = new TypeInfo.Function(
                [timeoutType],
                TypeInfo.Void.Shared,
                RequiredParams: 0
            ),
            ["setInterval"] = new TypeInfo.Function(
                [callbackType, TypeInfo.Primitive.Number, TypeInfo.Any.Shared],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearInterval"] = new TypeInfo.Function(
                [timeoutType],
                TypeInfo.Void.Shared,
                RequiredParams: 0
            ),
            ["setImmediate"] = new TypeInfo.Function(
                [callbackType, TypeInfo.Any.Shared],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearImmediate"] = new TypeInfo.Function(
                [timeoutType],
                TypeInfo.Void.Shared,
                RequiredParams: 0
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the timers/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTimersPromisesModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var numberType = TypeInfo.Primitive.Number;
        var promiseAny = new TypeInfo.Promise(anyType);

        return new Dictionary<string, TypeInfo>
        {
            ["setTimeout"] = new TypeInfo.Function(
                [numberType, anyType, anyType],
                promiseAny,
                RequiredParams: 0
            ),
            ["setImmediate"] = new TypeInfo.Function(
                [anyType, anyType],
                promiseAny,
                RequiredParams: 0
            ),
            ["setInterval"] = new TypeInfo.Function(
                [numberType, anyType, anyType],
                new TypeInfo.AsyncIterable(anyType),
                RequiredParams: 0
            )
        };
    }

    // GetPerfHooksModuleTypes removed — "perf_hooks" is now implemented in
    // stdlib/node/perf_hooks.ts; types flow from the TS source's exports.
    // The narrow primitive surface (just `now()`) is typed in GetPerfPrimitiveTypes.

}
