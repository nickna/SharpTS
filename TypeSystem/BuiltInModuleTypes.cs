using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Defines the type signatures for built-in Node.js-compatible modules.
/// </summary>
public static class BuiltInModuleTypes
{
    private static TypeInfo BooleanType => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

    /// <summary>
    /// Gets the exported types for the path module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetPathModuleTypes()
    {
        var pathObjectType = GetPathObjectType();

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["join"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["resolve"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["basename"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String(),
                RequiredParams: 1  // Second param is optional
            ),
            ["dirname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["extname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["normalize"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["isAbsolute"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                BooleanType
            ),
            ["relative"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["parse"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())
            ),
            ["format"] = new TypeInfo.Function(
                [new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())],
                new TypeInfo.String()
            ),
            // Properties
            ["sep"] = new TypeInfo.String(),
            ["delimiter"] = new TypeInfo.String(),
            // Platform-specific variants
            ["posix"] = pathObjectType,
            ["win32"] = pathObjectType
        };
    }

    /// <summary>
    /// Gets the type for the path.posix and path.win32 objects.
    /// </summary>
    private static TypeInfo.Record GetPathObjectType()
    {
        return new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["join"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["resolve"] = new TypeInfo.Function(
                [new TypeInfo.Any()],
                new TypeInfo.String(),
                HasRestParam: true
            ),
            ["basename"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String(),
                RequiredParams: 1
            ),
            ["dirname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["extname"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["normalize"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["isAbsolute"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                BooleanType
            ),
            ["relative"] = new TypeInfo.Function(
                [new TypeInfo.String(), new TypeInfo.String()],
                new TypeInfo.String()
            ),
            ["parse"] = new TypeInfo.Function(
                [new TypeInfo.String()],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())
            ),
            ["format"] = new TypeInfo.Function(
                [new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["root"] = new TypeInfo.String(),
                    ["dir"] = new TypeInfo.String(),
                    ["base"] = new TypeInfo.String(),
                    ["name"] = new TypeInfo.String(),
                    ["ext"] = new TypeInfo.String()
                }.ToFrozenDictionary())],
                new TypeInfo.String()
            ),
            ["sep"] = new TypeInfo.String(),
            ["delimiter"] = new TypeInfo.String()
        }.ToFrozenDictionary());
    }

    /// <summary>
    /// Gets the exported types for the os module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetOsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);

        return new Dictionary<string, TypeInfo>
        {
            // Methods returning strings
            ["platform"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["arch"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["hostname"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["homedir"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["tmpdir"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["type"] = new TypeInfo.Function([], new TypeInfo.String()),
            ["release"] = new TypeInfo.Function([], new TypeInfo.String()),

            // Methods returning numbers
            ["totalmem"] = new TypeInfo.Function([], numberType),
            ["freemem"] = new TypeInfo.Function([], numberType),

            // Methods returning arrays/objects
            ["cpus"] = new TypeInfo.Function([],
                new TypeInfo.Array(new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["model"] = new TypeInfo.String(),
                    ["speed"] = numberType
                }.ToFrozenDictionary()))
            ),
            ["userInfo"] = new TypeInfo.Function([],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["username"] = new TypeInfo.String(),
                    ["uid"] = numberType,
                    ["gid"] = numberType,
                    ["shell"] = new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Null()]),
                    ["homedir"] = new TypeInfo.String()
                }.ToFrozenDictionary())
            ),

            // loadavg() -> number[] (1, 5, 15 minute load averages)
            ["loadavg"] = new TypeInfo.Function([], new TypeInfo.Array(numberType)),

            // networkInterfaces() -> object with interface names as keys
            ["networkInterfaces"] = new TypeInfo.Function([],
                new TypeInfo.Any()  // Returns dynamic object structure
            ),

            // Properties
            ["EOL"] = new TypeInfo.String()
        };
    }

    /// <summary>
    /// Gets the exported types for the fs module (sync APIs only).
    /// </summary>
    public static Dictionary<string, TypeInfo> GetFsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();
        var anyType = new TypeInfo.Any();

        // Stats-like return type for statSync/lstatSync
        var statsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["isDirectory"] = BooleanType,
            ["isFile"] = BooleanType,
            ["isSymbolicLink"] = BooleanType,
            ["size"] = numberType
        }.ToFrozenDictionary());

        // Dirent-like type for readdirSync with withFileTypes
        var direntType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["name"] = stringType,
            ["isFile"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isDirectory"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isSymbolicLink"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isBlockDevice"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isCharacterDevice"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isFIFO"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)]),
            ["isSocket"] = new TypeInfo.Union([BooleanType, new TypeInfo.Function([], BooleanType)])
        }.ToFrozenDictionary());

        // fs.constants type
        var constantsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["F_OK"] = numberType,
            ["R_OK"] = numberType,
            ["W_OK"] = numberType,
            ["X_OK"] = numberType,
            ["O_RDONLY"] = numberType,
            ["O_WRONLY"] = numberType,
            ["O_RDWR"] = numberType,
            ["O_CREAT"] = numberType,
            ["O_EXCL"] = numberType,
            ["O_TRUNC"] = numberType,
            ["O_APPEND"] = numberType,
            ["COPYFILE_EXCL"] = numberType,
            ["COPYFILE_FICLONE"] = numberType,
            ["COPYFILE_FICLONE_FORCE"] = numberType,
            ["S_IFMT"] = numberType,
            ["S_IFREG"] = numberType,
            ["S_IFDIR"] = numberType,
            ["S_IFCHR"] = numberType,
            ["S_IFBLK"] = numberType,
            ["S_IFIFO"] = numberType,
            ["S_IFLNK"] = numberType,
            ["S_IFSOCK"] = numberType
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // File check - returns false on error (doesn't throw)
            ["existsSync"] = new TypeInfo.Function([stringType], BooleanType),

            // Read file - returns string if encoding provided, Buffer otherwise
            ["readFileSync"] = new TypeInfo.Function(
                [stringType, new TypeInfo.Union([stringType, new TypeInfo.Null()])],
                new TypeInfo.Union([stringType, new TypeInfo.Buffer()]),
                RequiredParams: 1
            ),

            // Write operations - return void
            ["writeFileSync"] = new TypeInfo.Function(
                [stringType, new TypeInfo.Union([stringType, new TypeInfo.Array(numberType)])],
                voidType
            ),
            ["appendFileSync"] = new TypeInfo.Function(
                [stringType, stringType],
                voidType
            ),

            // File/directory deletion
            ["unlinkSync"] = new TypeInfo.Function([stringType], voidType),
            ["rmdirSync"] = new TypeInfo.Function(
                [stringType, anyType],
                voidType,
                RequiredParams: 1
            ),

            // Directory operations
            ["mkdirSync"] = new TypeInfo.Function(
                [stringType, anyType],
                voidType,
                RequiredParams: 1
            ),
            ["readdirSync"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Union([new TypeInfo.Array(stringType), new TypeInfo.Array(direntType)]),
                RequiredParams: 1
            ),

            // File info
            ["statSync"] = new TypeInfo.Function([stringType], statsType),
            ["lstatSync"] = new TypeInfo.Function([stringType], statsType),

            // File move/copy
            ["renameSync"] = new TypeInfo.Function(
                [stringType, stringType],
                voidType
            ),
            ["copyFileSync"] = new TypeInfo.Function(
                [stringType, stringType],
                voidType
            ),

            // Access check - throws if not accessible
            ["accessSync"] = new TypeInfo.Function(
                [stringType, numberType],
                voidType,
                RequiredParams: 1
            ),

            // Change file permissions (Unix-specific, no-op on Windows)
            ["chmodSync"] = new TypeInfo.Function(
                [stringType, numberType],
                voidType
            ),

            // Change file ownership (Unix-specific, throws ENOSYS on Windows)
            ["chownSync"] = new TypeInfo.Function(
                [stringType, numberType, numberType],
                voidType
            ),

            // Change symlink ownership (doesn't follow symlinks)
            ["lchownSync"] = new TypeInfo.Function(
                [stringType, numberType, numberType],
                voidType
            ),

            // Truncate file to specified length
            ["truncateSync"] = new TypeInfo.Function(
                [stringType, numberType],
                voidType,
                RequiredParams: 1
            ),

            // Create symbolic link
            ["symlinkSync"] = new TypeInfo.Function(
                [stringType, stringType, stringType],
                voidType,
                RequiredParams: 2
            ),

            // Read symbolic link target
            ["readlinkSync"] = new TypeInfo.Function([stringType], stringType),

            // Resolve to absolute path (resolving symlinks)
            ["realpathSync"] = new TypeInfo.Function([stringType], stringType),

            // Set file access and modification times
            ["utimesSync"] = new TypeInfo.Function(
                [stringType, numberType, numberType],
                voidType
            ),

            // File descriptor APIs
            // openSync(path, flags, mode?) -> fd (number)
            ["openSync"] = new TypeInfo.Function(
                [stringType, anyType, numberType],
                numberType,
                RequiredParams: 2
            ),
            // closeSync(fd) -> void
            ["closeSync"] = new TypeInfo.Function([numberType], voidType),
            // readSync(fd, buffer, offset, length, position) -> bytesRead
            ["readSync"] = new TypeInfo.Function(
                [numberType, new TypeInfo.Buffer(), numberType, numberType, anyType],
                numberType
            ),
            // writeSync(fd, buffer, offset?, length?, position?) -> bytesWritten
            ["writeSync"] = new TypeInfo.Function(
                [numberType, new TypeInfo.Union([new TypeInfo.Buffer(), stringType]), numberType, numberType, anyType],
                numberType,
                RequiredParams: 2
            ),
            // fstatSync(fd) -> Stats
            ["fstatSync"] = new TypeInfo.Function([numberType], statsType),
            // ftruncateSync(fd, len?) -> void
            ["ftruncateSync"] = new TypeInfo.Function(
                [numberType, numberType],
                voidType,
                RequiredParams: 1
            ),

            // Directory utilities
            // mkdtempSync(prefix) -> string
            ["mkdtempSync"] = new TypeInfo.Function([stringType], stringType),
            // opendirSync(path) -> Dir
            ["opendirSync"] = new TypeInfo.Function([stringType], anyType),

            // Hard links
            // linkSync(existingPath, newPath) -> void
            ["linkSync"] = new TypeInfo.Function([stringType, stringType], voidType),

            // Constants object
            ["constants"] = constantsType
        };
    }

    /// <summary>
    /// Gets the exported types for the querystring module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetQuerystringModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // parse(str, sep?, eq?, options?) -> object
            ["parse"] = new TypeInfo.Function(
                [stringType, stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // stringify(obj, sep?, eq?, options?) -> string
            ["stringify"] = new TypeInfo.Function(
                [anyType, stringType, stringType, anyType],
                stringType,
                RequiredParams: 1
            ),
            // escape(str) -> string
            ["escape"] = new TypeInfo.Function([stringType], stringType),
            // unescape(str) -> string
            ["unescape"] = new TypeInfo.Function([stringType], stringType),
            // decode is alias for parse
            ["decode"] = new TypeInfo.Function(
                [stringType, stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // encode is alias for stringify
            ["encode"] = new TypeInfo.Function(
                [anyType, stringType, stringType, anyType],
                stringType,
                RequiredParams: 1
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the assert module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetAssertModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            // ok(value, message?) -> void
            ["ok"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // strictEqual(actual, expected, message?) -> void
            ["strictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notStrictEqual(actual, expected, message?) -> void
            ["notStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // deepStrictEqual(actual, expected, message?) -> void
            ["deepStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notDeepStrictEqual(actual, expected, message?) -> void
            ["notDeepStrictEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // throws(fn, message?) -> void
            ["throws"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // doesNotThrow(fn, message?) -> void
            ["doesNotThrow"] = new TypeInfo.Function(
                [anyType, stringType],
                voidType,
                RequiredParams: 1
            ),
            // fail(message?) -> void
            ["fail"] = new TypeInfo.Function(
                [stringType],
                voidType,
                RequiredParams: 0
            ),
            // equal(actual, expected, message?) -> void (loose equality)
            ["equal"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            ),
            // notEqual(actual, expected, message?) -> void (loose equality)
            ["notEqual"] = new TypeInfo.Function(
                [anyType, anyType, stringType],
                voidType,
                RequiredParams: 2
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the url module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetUrlModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        // URL class type (simplified - represents the URL constructor/class)
        var urlClassType = new TypeInfo.Any(); // Full class typing would require more infrastructure

        // URLSearchParams class type
        var urlSearchParamsType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // URL class constructor
            ["URL"] = urlClassType,
            // URLSearchParams class constructor
            ["URLSearchParams"] = urlSearchParamsType,
            // parse function (legacy)
            ["parse"] = new TypeInfo.Function(
                [stringType, stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            // format function (legacy)
            ["format"] = new TypeInfo.Function(
                [anyType],
                stringType
            ),
            // resolve function (legacy)
            ["resolve"] = new TypeInfo.Function(
                [stringType, stringType],
                stringType
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the process module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetProcessModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // Properties
            ["platform"] = stringType,
            ["arch"] = stringType,
            ["pid"] = numberType,
            ["version"] = stringType,
            ["env"] = new TypeInfo.Record(new Dictionary<string, TypeInfo>().ToFrozenDictionary()), // Record<string, string>
            ["argv"] = new TypeInfo.Array(stringType),
            ["exitCode"] = numberType,
            ["stdin"] = anyType,
            ["stdout"] = anyType,
            ["stderr"] = anyType,

            // Methods
            ["cwd"] = new TypeInfo.Function([], stringType),
            ["chdir"] = new TypeInfo.Function([stringType], voidType),
            ["exit"] = new TypeInfo.Function([numberType], voidType, RequiredParams: 0),
            ["hrtime"] = new TypeInfo.Function(
                [new TypeInfo.Array(numberType)],
                new TypeInfo.Array(numberType),
                RequiredParams: 0
            ),
            ["uptime"] = new TypeInfo.Function([], numberType),
            ["memoryUsage"] = new TypeInfo.Function([],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["rss"] = numberType,
                    ["heapTotal"] = numberType,
                    ["heapUsed"] = numberType,
                    ["external"] = numberType,
                    ["arrayBuffers"] = numberType
                }.ToFrozenDictionary())
            ),
            // nextTick(callback, ...args) - schedules callback for next tick
            // Use 'any' for callback to allow any function signature
            ["nextTick"] = new TypeInfo.Function(
                [anyType, anyType],
                voidType,
                RequiredParams: 1,
                HasRestParam: true
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the crypto module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetCryptoModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var bufferType = new TypeInfo.Buffer();
        var bufferOrStringType = new TypeInfo.Union([bufferType, stringType]);

        return new Dictionary<string, TypeInfo>
        {
            // Hash methods
            ["createHash"] = new TypeInfo.Function([stringType], anyType), // Returns Hash object
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
                anyType) // Returns KeyObject
        };
    }

    /// <summary>
    /// Gets the exported types for the util module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetUtilModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var boolType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        var voidType = new TypeInfo.Void();

        // TextEncoder type: { encoding: string, encode(string): Uint8Array, encodeInto(string, Uint8Array): { read: number, written: number } }
        var textEncoderType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["encoding"] = stringType,
            ["encode"] = new TypeInfo.Function([stringType], anyType), // Returns Uint8Array
            ["encodeInto"] = new TypeInfo.Function([stringType, anyType], new TypeInfo.Record(new Dictionary<string, TypeInfo>
            {
                ["read"] = new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                ["written"] = new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
            }.ToFrozenDictionary()))
        }.ToFrozenDictionary());

        // TextDecoder type: { encoding: string, fatal: boolean, ignoreBOM: boolean, decode(buffer): string }
        var textDecoderType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["encoding"] = stringType,
            ["fatal"] = boolType,
            ["ignoreBOM"] = boolType,
            ["decode"] = new TypeInfo.Function([anyType], stringType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["format"] = new TypeInfo.Function([anyType], stringType, HasRestParam: true),
            ["inspect"] = new TypeInfo.Function([anyType, anyType], stringType, RequiredParams: 1),
            ["isDeepStrictEqual"] = new TypeInfo.Function([anyType, anyType], boolType),
            ["parseArgs"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["toUSVString"] = new TypeInfo.Function([anyType], stringType),
            ["stripVTControlCharacters"] = new TypeInfo.Function([anyType], stringType),
            ["getSystemErrorName"] = new TypeInfo.Function([new TypeInfo.Primitive(TokenType.TYPE_NUMBER)], stringType),
            ["getSystemErrorMap"] = new TypeInfo.Function([], anyType), // Returns Map<number, [string, string]>
            ["deprecate"] = new TypeInfo.Function([anyType, stringType, anyType], anyType, RequiredParams: 2),
            ["callbackify"] = new TypeInfo.Function([anyType], anyType),
            ["promisify"] = new TypeInfo.Function([anyType], anyType),
            ["inherits"] = new TypeInfo.Function([anyType, anyType], voidType),

            // TextEncoder/TextDecoder constructors - use Interface with constructor signatures
            ["TextEncoder"] = new TypeInfo.Interface(
                Name: "TextEncoder",
                Members: new Dictionary<string, TypeInfo>
                {
                    ["encoding"] = stringType,
                    ["encode"] = new TypeInfo.Function([stringType], anyType),
                    ["encodeInto"] = new TypeInfo.Function([stringType, anyType], anyType)
                }.ToFrozenDictionary(),
                OptionalMembers: FrozenSet<string>.Empty,
                ConstructorSignatures:
                [
                    new TypeInfo.ConstructorSignature(
                        TypeParams: null,
                        ParamTypes: [],
                        ReturnType: textEncoderType) // new TextEncoder()
                ]),
            ["TextDecoder"] = new TypeInfo.Interface(
                Name: "TextDecoder",
                Members: new Dictionary<string, TypeInfo>
                {
                    ["encoding"] = stringType,
                    ["fatal"] = boolType,
                    ["ignoreBOM"] = boolType,
                    ["decode"] = new TypeInfo.Function([anyType], stringType)
                }.ToFrozenDictionary(),
                OptionalMembers: FrozenSet<string>.Empty,
                ConstructorSignatures:
                [
                    new TypeInfo.ConstructorSignature(
                        TypeParams: null,
                        ParamTypes: [],
                        ReturnType: textDecoderType), // new TextDecoder()
                    new TypeInfo.ConstructorSignature(
                        TypeParams: null,
                        ParamTypes: [stringType],
                        ReturnType: textDecoderType), // new TextDecoder(encoding)
                    new TypeInfo.ConstructorSignature(
                        TypeParams: null,
                        ParamTypes: [stringType, anyType],
                        ReturnType: textDecoderType) // new TextDecoder(encoding, options)
                ]),

            // util.types namespace
            ["types"] = new TypeInfo.Record(new Dictionary<string, TypeInfo>
            {
                ["isArray"] = new TypeInfo.Function([anyType], boolType),
                ["isDate"] = new TypeInfo.Function([anyType], boolType),
                ["isFunction"] = new TypeInfo.Function([anyType], boolType),
                ["isNull"] = new TypeInfo.Function([anyType], boolType),
                ["isUndefined"] = new TypeInfo.Function([anyType], boolType),
                ["isPromise"] = new TypeInfo.Function([anyType], boolType),
                ["isRegExp"] = new TypeInfo.Function([anyType], boolType),
                ["isMap"] = new TypeInfo.Function([anyType], boolType),
                ["isSet"] = new TypeInfo.Function([anyType], boolType),
                ["isTypedArray"] = new TypeInfo.Function([anyType], boolType),
                ["isNativeError"] = new TypeInfo.Function([anyType], boolType),
                ["isBoxedPrimitive"] = new TypeInfo.Function([anyType], boolType),
                ["isWeakMap"] = new TypeInfo.Function([anyType], boolType),
                ["isWeakSet"] = new TypeInfo.Function([anyType], boolType),
                ["isArrayBuffer"] = new TypeInfo.Function([anyType], boolType)
            }.ToFrozenDictionary())
        };
    }

    /// <summary>
    /// Gets the exported types for the readline module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetReadlineModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["questionSync"] = new TypeInfo.Function([stringType], stringType),
            ["createInterface"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0)
        };
    }

    /// <summary>
    /// Gets the exported types for the child_process module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetChildProcessModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        var spawnResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["stdout"] = stringType,
            ["stderr"] = stringType,
            ["status"] = numberType,
            ["signal"] = new TypeInfo.Union([stringType, new TypeInfo.Null()])
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // Methods
            ["execSync"] = new TypeInfo.Function([stringType, anyType], stringType, RequiredParams: 1),
            ["spawnSync"] = new TypeInfo.Function(
                [stringType, new TypeInfo.Array(stringType), anyType],
                spawnResultType,
                RequiredParams: 1
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the buffer module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetBufferModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var bufferType = new TypeInfo.Buffer();

        // Buffer constructor type - an object with static methods
        var bufferConstructorType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["from"] = new TypeInfo.Function(
                [new TypeInfo.Union([stringType, new TypeInfo.Array(numberType), bufferType]), stringType],
                bufferType,
                RequiredParams: 1),
            ["alloc"] = new TypeInfo.Function(
                [numberType, new TypeInfo.Any(), stringType],
                bufferType,
                RequiredParams: 1),
            ["allocUnsafe"] = new TypeInfo.Function([numberType], bufferType),
            ["allocUnsafeSlow"] = new TypeInfo.Function([numberType], bufferType),
            ["concat"] = new TypeInfo.Function(
                [new TypeInfo.Array(bufferType), numberType],
                bufferType,
                RequiredParams: 1),
            ["isBuffer"] = new TypeInfo.Function([new TypeInfo.Any()], BooleanType),
            ["byteLength"] = new TypeInfo.Function(
                [new TypeInfo.Union([stringType, bufferType]), stringType],
                numberType,
                RequiredParams: 1),
            ["compare"] = new TypeInfo.Function([bufferType, bufferType], numberType),
            ["isEncoding"] = new TypeInfo.Function([stringType], BooleanType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["Buffer"] = bufferConstructorType
        };
    }

    /// <summary>
    /// Gets the exported types for the zlib module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetZlibModuleTypes()
    {
        var bufferType = new TypeInfo.Buffer();
        var anyType = new TypeInfo.Any();

        return new Dictionary<string, TypeInfo>
        {
            // Gzip methods
            ["gzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["gunzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Deflate methods (with zlib header)
            ["deflateSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["inflateSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // DeflateRaw methods (no header)
            ["deflateRawSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["inflateRawSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Brotli methods
            ["brotliCompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["brotliDecompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Zstd methods
            ["zstdCompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),
            ["zstdDecompressSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Unzip (auto-detect)
            ["unzipSync"] = new TypeInfo.Function(
                [new TypeInfo.Union([bufferType, new TypeInfo.String()]), anyType],
                bufferType,
                RequiredParams: 1
            ),

            // Constants object
            ["constants"] = anyType
        };
    }

    /// <summary>
    /// Gets the exported types for the events module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetEventsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var eventEmitterType = new TypeInfo.EventEmitter();

        // EventEmitter is an interface with a constructor signature
        // This allows both `new EventEmitter()` and `new events.EventEmitter()` to type check
        var eventEmitterConstructorType = new TypeInfo.Interface(
            Name: "EventEmitter",
            Members: new Dictionary<string, TypeInfo>
            {
                // Static property on the constructor
                ["defaultMaxListeners"] = numberType
            }.ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            // Constructor signature: new () => EventEmitter
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: eventEmitterType)
            ]
        );

        return new Dictionary<string, TypeInfo>
        {
            ["EventEmitter"] = eventEmitterConstructorType
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
            "path" => GetPathModuleTypes(),
            "os" => GetOsModuleTypes(),
            "fs" => GetFsModuleTypes(),
            "querystring" => GetQuerystringModuleTypes(),
            "assert" => GetAssertModuleTypes(),
            "url" => GetUrlModuleTypes(),
            "process" => GetProcessModuleTypes(),
            "crypto" => GetCryptoModuleTypes(),
            "util" => GetUtilModuleTypes(),
            "readline" => GetReadlineModuleTypes(),
            "child_process" => GetChildProcessModuleTypes(),
            "buffer" => GetBufferModuleTypes(),
            "zlib" => GetZlibModuleTypes(),
            "events" => GetEventsModuleTypes(),
            "timers" => GetTimersModuleTypes(),
            "string_decoder" => GetStringDecoderModuleTypes(),
            "perf_hooks" => GetPerfHooksModuleTypes(),
            "stream" => GetStreamModuleTypes(),
            "http" => GetHttpModuleTypes(),
            "dns" => GetDnsModuleTypes(),
            _ => null
        };
    }

    /// <summary>
    /// Gets the exported types for the dns module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDnsModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();

        // Result type for lookup: { address: string, family: number }
        var lookupResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = numberType
        }.ToFrozenDictionary());

        // Result type for lookupService: { hostname: string, service: string }
        var lookupServiceResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["hostname"] = stringType,
            ["service"] = stringType
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // dns.lookup(hostname, [options]) -> { address, family }
            ["lookup"] = new TypeInfo.Function(
                [stringType, anyType],
                lookupResultType,
                RequiredParams: 1
            ),

            // dns.lookupService(address, port) -> { hostname, service }
            ["lookupService"] = new TypeInfo.Function(
                [stringType, numberType],
                lookupServiceResultType,
                RequiredParams: 2
            ),

            // Constants
            ["ADDRCONFIG"] = numberType,
            ["V4MAPPED"] = numberType,
            ["ALL"] = numberType
        };
    }

    /// <summary>
    /// Gets the exported types for the http module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetHttpModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var voidType = new TypeInfo.Void();
        var callbackType = new TypeInfo.Function([anyType, anyType], voidType);

        // Server type with full EventEmitter support
        var serverType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // Server-specific methods
            ["listen"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["listening"] = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),

            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], BooleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["rawListeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        }.ToFrozenDictionary());

        // STATUS_CODES type - with string index signature for dynamic property access
        var statusCodesType = new TypeInfo.Record(
            new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            StringIndexType: stringType  // Allow any string key to return a string
        );

        // METHODS type - array of strings
        var methodsType = new TypeInfo.Array(stringType);

        // globalAgent type
        var agentType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["maxSockets"] = numberType,
            ["maxFreeSockets"] = numberType,
            ["keepAlive"] = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            ["keepAliveMsecs"] = numberType
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createServer"] = new TypeInfo.Function([callbackType], serverType, RequiredParams: 0),
            ["request"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["get"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["METHODS"] = methodsType,
            ["STATUS_CODES"] = statusCodesType,
            ["globalAgent"] = agentType
        };
    }

    /// <summary>
    /// Gets the exported types for the timers module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTimersModuleTypes()
    {
        var timeoutType = new TypeInfo.Any(); // Timeout handle type
        var callbackType = new TypeInfo.Function([new TypeInfo.Any()], new TypeInfo.Void(), HasRestParam: true);

        return new Dictionary<string, TypeInfo>
        {
            ["setTimeout"] = new TypeInfo.Function(
                [callbackType, new TypeInfo.Primitive(TokenType.TYPE_NUMBER), new TypeInfo.Any()],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearTimeout"] = new TypeInfo.Function(
                [timeoutType],
                new TypeInfo.Void(),
                RequiredParams: 0
            ),
            ["setInterval"] = new TypeInfo.Function(
                [callbackType, new TypeInfo.Primitive(TokenType.TYPE_NUMBER), new TypeInfo.Any()],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearInterval"] = new TypeInfo.Function(
                [timeoutType],
                new TypeInfo.Void(),
                RequiredParams: 0
            ),
            ["setImmediate"] = new TypeInfo.Function(
                [callbackType, new TypeInfo.Any()],
                timeoutType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["clearImmediate"] = new TypeInfo.Function(
                [timeoutType],
                new TypeInfo.Void(),
                RequiredParams: 0
            )
        };
    }

    /// <summary>
    /// Gets the exported types for the string_decoder module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetStringDecoderModuleTypes()
    {
        // StringDecoder instance type (what new StringDecoder() returns)
        var stringDecoderInstanceType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["encoding"] = new TypeInfo.String(),
            ["write"] = new TypeInfo.Function([new TypeInfo.Any()], new TypeInfo.String()),
            ["end"] = new TypeInfo.Function([new TypeInfo.Any()], new TypeInfo.String(), RequiredParams: 0)
        }.ToFrozenDictionary());

        // StringDecoder is an interface with a constructor signature
        // This allows `new StringDecoder()` to type check
        var stringDecoderConstructorType = new TypeInfo.Interface(
            Name: "StringDecoder",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [new TypeInfo.String()],
                    ReturnType: stringDecoderInstanceType,
                    RequiredParams: 0) // Encoding is optional
            ]
        );

        return new Dictionary<string, TypeInfo>
        {
            ["StringDecoder"] = stringDecoderConstructorType
        };
    }

    /// <summary>
    /// Gets the exported types for the perf_hooks module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetPerfHooksModuleTypes()
    {
        var performanceType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["now"] = new TypeInfo.Function([], new TypeInfo.Primitive(TokenType.TYPE_NUMBER)),
            ["timeOrigin"] = new TypeInfo.Primitive(TokenType.TYPE_NUMBER)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["performance"] = performanceType
        };
    }

    /// <summary>
    /// Gets the exported types for the stream module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetStreamModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var boolType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var voidType = new TypeInfo.Void();

        // Stream instance type (shared members for all stream types)
        var streamInstanceType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType),

            // Readable methods
            ["read"] = new TypeInfo.Function([numberType], anyType, RequiredParams: 0),
            ["push"] = new TypeInfo.Function([anyType], boolType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["unpipe"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["unshift"] = new TypeInfo.Function([anyType], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["isPaused"] = new TypeInfo.Function([], boolType),

            // Readable properties
            ["readable"] = boolType,
            ["readableEnded"] = boolType,
            ["readableLength"] = numberType,
            ["readableEncoding"] = stringType,
            ["destroyed"] = boolType,

            // Writable methods
            ["write"] = new TypeInfo.Function([anyType, stringType, anyType], boolType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, stringType, anyType], anyType, RequiredParams: 0),
            ["cork"] = new TypeInfo.Function([], voidType),
            ["uncork"] = new TypeInfo.Function([], voidType),
            ["setDefaultEncoding"] = new TypeInfo.Function([stringType], anyType),

            // Writable properties
            ["writable"] = boolType,
            ["writableEnded"] = boolType,
            ["writableFinished"] = boolType,
            ["writableLength"] = numberType,
            ["writableCorked"] = numberType
        }.ToFrozenDictionary());

        // Readable constructor
        var readableConstructorType = new TypeInfo.Interface(
            Name: "Readable",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Writable constructor
        var writableConstructorType = new TypeInfo.Interface(
            Name: "Writable",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Duplex constructor
        var duplexConstructorType = new TypeInfo.Interface(
            Name: "Duplex",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Transform constructor
        var transformConstructorType = new TypeInfo.Interface(
            Name: "Transform",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // PassThrough constructor
        var passThroughConstructorType = new TypeInfo.Interface(
            Name: "PassThrough",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        return new Dictionary<string, TypeInfo>
        {
            ["Readable"] = readableConstructorType,
            ["Writable"] = writableConstructorType,
            ["Duplex"] = duplexConstructorType,
            ["Transform"] = transformConstructorType,
            ["PassThrough"] = passThroughConstructorType
        };
    }
}
