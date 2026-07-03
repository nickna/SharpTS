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
    /// Gets the exported types for the fs module (sync, callback, and promise-based APIs).
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
            ["isDirectory"] = new TypeInfo.Function([], BooleanType),
            ["isFile"] = new TypeInfo.Function([], BooleanType),
            ["isSymbolicLink"] = new TypeInfo.Function([], BooleanType),
            ["size"] = numberType
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

            // Write operations - return void. Data may be a string, Buffer, or
            // TypedArray (any); the optional third arg carries the encoding/options.
            ["writeFileSync"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["appendFileSync"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
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
            // Note: In Node.js, readdirSync returns string[] by default, Dirent[] only with { withFileTypes: true }
            // Without function overloading support, we use the common case (string[]) as the return type
            ["readdirSync"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Array(stringType),
                RequiredParams: 1
            ),

            // File info
            ["statSync"] = new TypeInfo.Function([stringType], statsType),
            ["lstatSync"] = new TypeInfo.Function([stringType], statsType),
            // Raw stat records (#977) — the TS Stats class shapes these.
            ["statRaw"] = new TypeInfo.Function([stringType], anyType),
            ["lstatRaw"] = new TypeInfo.Function([stringType], anyType),
            ["fstatRaw"] = new TypeInfo.Function([numberType], anyType),

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
            // Long-tail fd primitives (#976): the TS facade derives fsync/fdatasync,
            // fchmod/fchown/futimes (via fdPath), and statfs from these.
            // fsyncSync(fd) -> void
            ["fsyncSync"] = new TypeInfo.Function([numberType], voidType),
            // fdPath(fd) -> string (the open fd's file path)
            ["fdPath"] = new TypeInfo.Function([numberType], stringType),
            // statfsRaw(path) -> flat record the TS StatFs shapes
            ["statfsRaw"] = new TypeInfo.Function([stringType], anyType),

            // Directory utilities
            // mkdtempSync(prefix) -> string
            ["mkdtempSync"] = new TypeInfo.Function([stringType], stringType),
            // opendirSync(path) -> Dir
            ["opendirSync"] = new TypeInfo.Function([stringType], anyType),

            // Hard links
            // linkSync(existingPath, newPath) -> void
            ["linkSync"] = new TypeInfo.Function([stringType, stringType], voidType),

            // Stream factory methods
            ["createReadStream"] = new TypeInfo.Function(
                [stringType, anyType],
                anyType,
                RequiredParams: 1
            ),
            ["createWriteStream"] = new TypeInfo.Function(
                [stringType, anyType],
                anyType,
                RequiredParams: 1
            ),

            // File watching
            ["watch"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                anyType,
                RequiredParams: 1
            ),
            ["watchFile"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                anyType,
                RequiredParams: 2
            ),
            ["unwatchFile"] = new TypeInfo.Function(
                [stringType, anyType],
                voidType,
                RequiredParams: 1
            ),

            // Constants object
            ["constants"] = constantsType,

            // Callback-based async methods
            // Callback type: (err: Error | null, data?: T) => void
            ["readFile"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["writeFile"] = new TypeInfo.Function(
                [stringType, anyType, anyType, anyType],
                voidType,
                RequiredParams: 3
            ),
            ["appendFile"] = new TypeInfo.Function(
                [stringType, anyType, anyType, anyType],
                voidType,
                RequiredParams: 3
            ),
            ["stat"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["lstat"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["unlink"] = new TypeInfo.Function(
                [stringType, anyType],
                voidType
            ),
            ["mkdir"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["rmdir"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["readdir"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["rename"] = new TypeInfo.Function(
                [stringType, stringType, anyType],
                voidType
            ),
            ["copyFile"] = new TypeInfo.Function(
                [stringType, stringType, anyType, anyType],
                voidType,
                RequiredParams: 3
            ),
            ["access"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["chmod"] = new TypeInfo.Function(
                [stringType, numberType, anyType],
                voidType
            ),
            ["truncate"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["utimes"] = new TypeInfo.Function(
                [stringType, anyType, anyType, anyType],
                voidType
            ),
            ["readlink"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["realpath"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),
            ["symlink"] = new TypeInfo.Function(
                [stringType, stringType, anyType, anyType],
                voidType,
                RequiredParams: 3
            ),
            ["link"] = new TypeInfo.Function(
                [stringType, stringType, anyType],
                voidType
            ),
            ["mkdtemp"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                voidType,
                RequiredParams: 2
            ),

            // fs.promises namespace
            ["promises"] = GetFsPromisesTypes()
        };
    }

    /// <summary>
    /// Gets the type definitions for the fs.promises namespace.
    /// </summary>
    public static TypeInfo.Record GetFsPromisesTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();
        var anyType = new TypeInfo.Any();
        var bufferType = new TypeInfo.Buffer();

        // Promise types
        var promiseVoid = new TypeInfo.Promise(voidType);
        var promiseString = new TypeInfo.Promise(stringType);
        var promiseBuffer = new TypeInfo.Promise(bufferType);
        var promiseBufferOrString = new TypeInfo.Promise(new TypeInfo.Union([bufferType, stringType]));
        var promiseArray = new TypeInfo.Promise(new TypeInfo.Array(stringType));

        // Stats-like type for stat/lstat
        var statsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["isDirectory"] = new TypeInfo.Function([], BooleanType),
            ["isFile"] = new TypeInfo.Function([], BooleanType),
            ["isSymbolicLink"] = new TypeInfo.Function([], BooleanType),
            ["size"] = numberType
        }.ToFrozenDictionary());
        var promiseStats = new TypeInfo.Promise(statsType);

        // Constants type
        var constantsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["F_OK"] = numberType,
            ["R_OK"] = numberType,
            ["W_OK"] = numberType,
            ["X_OK"] = numberType
        }.ToFrozenDictionary());

        return new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["readFile"] = new TypeInfo.Function([stringType, anyType], promiseBufferOrString, RequiredParams: 1),
            ["writeFile"] = new TypeInfo.Function([stringType, anyType, anyType], promiseVoid, RequiredParams: 2),
            ["appendFile"] = new TypeInfo.Function([stringType, anyType, anyType], promiseVoid, RequiredParams: 2),
            ["stat"] = new TypeInfo.Function([stringType, anyType], promiseStats, RequiredParams: 1),
            ["lstat"] = new TypeInfo.Function([stringType, anyType], promiseStats, RequiredParams: 1),
            ["unlink"] = new TypeInfo.Function([stringType], promiseVoid),
            ["mkdir"] = new TypeInfo.Function([stringType, anyType], promiseVoid, RequiredParams: 1),
            ["rmdir"] = new TypeInfo.Function([stringType, anyType], promiseVoid, RequiredParams: 1),
            ["rm"] = new TypeInfo.Function([stringType, anyType], promiseVoid, RequiredParams: 1),
            ["readdir"] = new TypeInfo.Function([stringType, anyType], promiseArray, RequiredParams: 1),
            ["rename"] = new TypeInfo.Function([stringType, stringType], promiseVoid),
            ["copyFile"] = new TypeInfo.Function([stringType, stringType, anyType], promiseVoid, RequiredParams: 2),
            ["access"] = new TypeInfo.Function([stringType, anyType], promiseVoid, RequiredParams: 1),
            ["chmod"] = new TypeInfo.Function([stringType, numberType], promiseVoid),
            ["truncate"] = new TypeInfo.Function([stringType, anyType], promiseVoid, RequiredParams: 1),
            ["utimes"] = new TypeInfo.Function([stringType, anyType, anyType], promiseVoid),
            ["readlink"] = new TypeInfo.Function([stringType, anyType], promiseString, RequiredParams: 1),
            ["realpath"] = new TypeInfo.Function([stringType, anyType], promiseString, RequiredParams: 1),
            ["symlink"] = new TypeInfo.Function([stringType, stringType, anyType], promiseVoid, RequiredParams: 2),
            ["link"] = new TypeInfo.Function([stringType, stringType], promiseVoid),
            ["mkdtemp"] = new TypeInfo.Function([stringType, anyType], promiseString, RequiredParams: 1),
            ["constants"] = constantsType
        }.ToFrozenDictionary());
    }

    /// <summary>
    /// Gets the exported types for the fs/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetFsPromisesModuleTypes()
    {
        var fsPromises = GetFsPromisesTypes();
        return fsPromises.Fields.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    // GetQuerystringModuleTypes removed: the 'querystring' module now lives in
    // stdlib/node/querystring.ts. Its export types are derived from the TS source
    // via normal type inference.

    /// <summary>
    /// Gets the exported types for the process module (also the type surface of
    /// <c>primitive:process</c> consumed by the stdlib facade). The
    /// <c>processObject</c> entry is the full live process object exposed as the
    /// module's default export (epic #1078).
    /// </summary>
    public static Dictionary<string, TypeInfo> GetProcessModuleTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringType = new TypeInfo.String();
        var booleanType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        var voidType = new TypeInfo.Void();
        var anyType = new TypeInfo.Any();

        var surface = new Dictionary<string, TypeInfo>
        {
            // Properties
            ["platform"] = stringType,
            ["arch"] = stringType,
            ["pid"] = numberType,
            ["ppid"] = numberType,
            ["version"] = stringType,
            ["versions"] = anyType,
            ["env"] = new TypeInfo.Record(new Dictionary<string, TypeInfo>().ToFrozenDictionary()), // Record<string, string>
            ["argv"] = new TypeInfo.Array(stringType),
            ["argv0"] = stringType,
            ["execPath"] = stringType,
            ["execArgv"] = new TypeInfo.Array(stringType),
            ["exitCode"] = numberType,
            ["title"] = stringType,
            ["config"] = anyType,
            ["release"] = anyType,
            ["features"] = anyType,
            ["debugPort"] = numberType,
            ["allowedNodeEnvironmentFlags"] = anyType,
            ["stdin"] = anyType,
            ["stdout"] = anyType,
            ["stderr"] = anyType,
            ["report"] = anyType,
            ["throwDeprecation"] = booleanType,
            ["traceDeprecation"] = booleanType,
            ["noDeprecation"] = booleanType,
            ["sourceMapsEnabled"] = booleanType,
            // IPC (present when forked / cluster worker)
            ["connected"] = booleanType,
            ["channel"] = anyType,
            ["send"] = anyType,
            ["disconnect"] = anyType,
            // POSIX identity (undefined on Windows, like Node)
            ["getuid"] = anyType,
            ["geteuid"] = anyType,
            ["getgid"] = anyType,
            ["getegid"] = anyType,
            ["getgroups"] = anyType,
            ["setuid"] = anyType,
            ["setgid"] = anyType,

            // Methods
            ["cwd"] = new TypeInfo.Function([], stringType),
            ["chdir"] = new TypeInfo.Function([stringType], voidType),
            ["exit"] = new TypeInfo.Function([numberType], voidType, RequiredParams: 0),
            // hrtime / memoryUsage carry own members (hrtime.bigint(),
            // memoryUsage.rss()) — typed any (function-with-members shape).
            ["hrtime"] = anyType,
            ["memoryUsage"] = anyType,
            ["uptime"] = new TypeInfo.Function([], numberType),
            ["kill"] = new TypeInfo.Function([numberType, anyType], booleanType, RequiredParams: 1),
            ["abort"] = new TypeInfo.Function([], voidType),
            ["umask"] = new TypeInfo.Function([anyType], numberType, RequiredParams: 0),
            ["cpuUsage"] = new TypeInfo.Function([anyType],
                new TypeInfo.Record(new Dictionary<string, TypeInfo>
                {
                    ["user"] = numberType,
                    ["system"] = numberType
                }.ToFrozenDictionary()),
                RequiredParams: 0),
            ["resourceUsage"] = new TypeInfo.Function([], anyType),
            ["availableMemory"] = new TypeInfo.Function([], numberType),
            ["constrainedMemory"] = new TypeInfo.Function([], numberType),
            ["getActiveResourcesInfo"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["emitWarning"] = new TypeInfo.Function(
                [anyType, anyType, anyType, anyType], voidType, RequiredParams: 1),
            ["setSourceMapsEnabled"] = new TypeInfo.Function([booleanType], voidType),
            // nextTick(callback, ...args) - schedules callback for next tick
            // Use 'any' for callback to allow any function signature
            ["nextTick"] = new TypeInfo.Function(
                [anyType, anyType],
                voidType,
                RequiredParams: 1,
                HasRestParam: true
            ),

            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function(
                [stringType, anyType],
                booleanType,
                RequiredParams: 1,
                HasRestParam: true
            ),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["rawListeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        };

        // The live process object itself (module default export / bare global):
        // same surface as the named exports.
        surface["processObject"] = new TypeInfo.Record(surface.ToFrozenDictionary());
        return surface;
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
        var voidType = new TypeInfo.Void();

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

            // X509Certificate class (#1064) — any-typed so `new crypto.X509Certificate(...)`
            // and instance member access type-check; a refined shape lands with #1065.
            ["X509Certificate"] = anyType
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
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var voidType = new TypeInfo.Void();
        var boolType = BooleanType;
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);

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

        var boolType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

        // The second positional argument of spawn/execFile/fork is either the args array OR
        // (when omitted) the options/callback — Node overloads it. Accept all of them so
        // `spawn(cmd, { shell: true })` and `execFile(file, cb)` type-check (#1022/#1016).
        var argsOrOptions = new TypeInfo.Union([new TypeInfo.Array(stringType), anyType]);

        var childProcessType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["pid"] = numberType,
            ["exitCode"] = new TypeInfo.Union([numberType, new TypeInfo.Null()]),
            ["killed"] = boolType,
            ["stdout"] = anyType,
            ["stderr"] = anyType,
            ["stdin"] = anyType,
            ["connected"] = boolType,
            ["signalCode"] = new TypeInfo.Union([stringType, new TypeInfo.Null()]),
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["kill"] = new TypeInfo.Function([stringType], boolType, RequiredParams: 0),
            ["send"] = new TypeInfo.Function([anyType], boolType),
            ["disconnect"] = new TypeInfo.Function([], new TypeInfo.Void()),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // Sync methods
            ["execSync"] = new TypeInfo.Function([stringType, anyType], stringType, RequiredParams: 1),
            ["spawnSync"] = new TypeInfo.Function(
                [stringType, argsOrOptions, anyType],
                spawnResultType,
                RequiredParams: 1
            ),
            ["execFileSync"] = new TypeInfo.Function(
                [stringType, argsOrOptions, anyType],
                stringType,
                RequiredParams: 1
            ),
            // Async methods
            ["exec"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                childProcessType,
                RequiredParams: 1
            ),
            ["spawn"] = new TypeInfo.Function(
                [stringType, argsOrOptions, anyType],
                childProcessType,
                RequiredParams: 1
            ),
            ["execFile"] = new TypeInfo.Function(
                [stringType, argsOrOptions, anyType, anyType],
                childProcessType,
                RequiredParams: 1
            ),
            ["fork"] = new TypeInfo.Function(
                [stringType, argsOrOptions, anyType],
                childProcessType,
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

        var anyType = new TypeInfo.Any();
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
    /// Gets the exported types for the zlib module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetZlibModuleTypes()
    {
        var bufferType = new TypeInfo.Buffer();
        var anyType = new TypeInfo.Any();
        var inputType = new TypeInfo.Union([bufferType, new TypeInfo.String()]);
        var transformType = anyType; // Transform stream type

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
            ["createUnzip"] = new TypeInfo.Function(
                [anyType], transformType, RequiredParams: 0),

            // Async callback APIs
            ["gzip"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["gunzip"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["deflate"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["inflate"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["deflateRaw"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["inflateRaw"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["brotliCompress"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["brotliDecompress"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),
            ["unzip"] = new TypeInfo.Function(
                [inputType, anyType, anyType], new TypeInfo.Void(), RequiredParams: 2),

            // Checksums (Node 22+): crc32(data[, value]) -> number
            ["crc32"] = new TypeInfo.Function(
                [inputType, new TypeInfo.Primitive(TokenType.TYPE_NUMBER)],
                new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                RequiredParams: 1),

            // Constants and error-code objects
            ["constants"] = anyType,
            ["codes"] = anyType
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
            "zlib" => GetZlibModuleTypes(),
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
            _ => null
        };
    }

    /// <summary>
    /// Types for <c>primitive:tty</c> — just <c>isatty(fd)</c> returning a boolean.
    /// </summary>
    private static Dictionary<string, TypeInfo> GetTtyPrimitiveTypes()
    {
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var booleanType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
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
        var anyType = new TypeInfo.Any();
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
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        return new Dictionary<string, TypeInfo>
        {
            ["now"] = new TypeInfo.Function([], numberType),
        };
    }

    /// <summary>
    /// Gets the exported types for the worker_threads module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetWorkerThreadsModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var boolType = BooleanType;
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            // Constructors — typed as Any so `new wt.X(...)` type-checks and the compiler
            // routes through TryEmitModuleQualifiedConstructor for the actual IL.
            ["Worker"] = anyType,
            ["MessageChannel"] = anyType,
            ["MessagePort"] = anyType,
            ["BroadcastChannel"] = anyType,

            // Thread identity
            ["isMainThread"] = boolType,
            ["threadId"] = numberType,
            ["parentPort"] = anyType,
            ["workerData"] = anyType,

            // Functions / constants
            ["receiveMessageOnPort"] = new TypeInfo.Function([anyType], anyType),
            ["markAsUntransferable"] = new TypeInfo.Function([anyType], voidType),
            ["moveMessagePortToContext"] = new TypeInfo.Function([anyType, anyType], anyType),
            ["getEnvironmentData"] = new TypeInfo.Function([stringType], anyType),
            ["setEnvironmentData"] = new TypeInfo.Function([stringType, anyType], voidType),
            ["SHARE_ENV"] = anyType,
            ["resourceLimits"] = anyType,
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
            // dns.lookup(hostname[, options][, callback]) -> { address, family }
            // (sync direct-return form when no callback; Node form invokes the callback)
            ["lookup"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                lookupResultType,
                RequiredParams: 1
            ),

            // dns.lookupService(address, port[, callback]) -> { hostname, service }
            ["lookupService"] = new TypeInfo.Function(
                [stringType, numberType, anyType],
                lookupServiceResultType,
                RequiredParams: 2
            ),

            // Async callback-based methods
            // dns.resolve(hostname[, rrtype], callback) -> void
            ["resolve"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                new TypeInfo.Void(),
                RequiredParams: 2),
            // dns.resolve4(hostname, callback) -> void
            ["resolve4"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolve6(hostname, callback) -> void
            ["resolve6"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.reverse(ip, callback) -> void
            ["reverse"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveMx(hostname, callback) -> void
            ["resolveMx"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveTxt(hostname, callback) -> void
            ["resolveTxt"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveSrv(hostname, callback) -> void
            ["resolveSrv"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveCname(hostname, callback) -> void
            ["resolveCname"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveNs(hostname, callback) -> void
            ["resolveNs"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveSoa(hostname, callback) -> void
            ["resolveSoa"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolvePtr(hostname, callback) -> void
            ["resolvePtr"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveCaa(hostname, callback) -> void
            ["resolveCaa"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),
            // dns.resolveNaptr(hostname, callback) -> void
            ["resolveNaptr"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Void()),

            // dns.Resolver class constructor
            ["Resolver"] = new TypeInfo.Function([], anyType, RequiredParams: 0),

            // Default lookup result order (#1072)
            ["setDefaultResultOrder"] = new TypeInfo.Function([stringType], new TypeInfo.Void()),
            ["getDefaultResultOrder"] = new TypeInfo.Function([], stringType),

            // dns.promises sub-module
            ["promises"] = anyType,

            // Constants
            ["ADDRCONFIG"] = numberType,
            ["V4MAPPED"] = numberType,
            ["ALL"] = numberType,
            ["NODATA"] = stringType,
            ["FORMERR"] = stringType,
            ["SERVFAIL"] = stringType,
            ["NOTFOUND"] = stringType,
            ["NOTIMP"] = stringType,
            ["REFUSED"] = stringType,
            ["BADQUERY"] = stringType,
            ["BADNAME"] = stringType,
            ["BADFAMILY"] = stringType,
            ["BADRESP"] = stringType,
            ["CONNREFUSED"] = stringType,
            ["TIMEOUT"] = stringType,
            ["EOF"] = stringType,
            ["FILE"] = stringType,
            ["NOMEM"] = stringType,
            ["DESTRUCTION"] = stringType,
            ["BADSTR"] = stringType,
            ["BADFLAGS"] = stringType,
            ["NONAME"] = stringType,
            ["BADHINTS"] = stringType,
            ["NOTINITIALIZED"] = stringType,
            ["LOADIPHLPAPI"] = stringType,
            ["ADDRGETNETWORKPARAMS"] = stringType,
            ["CANCELLED"] = stringType
        };
    }

    /// <summary>
    /// Gets the exported types for the dns/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDnsPromisesModuleTypes()
    {
        var stringType = new TypeInfo.String();
        var anyType = new TypeInfo.Any();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);

        var lookupResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = numberType
        }.ToFrozenDictionary());

        var stringArrayType = new TypeInfo.Array(stringType);

        return new Dictionary<string, TypeInfo>
        {
            ["lookup"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Promise(lookupResultType),
                RequiredParams: 1),
            ["resolve"] = new TypeInfo.Function(
                [stringType, stringType],
                new TypeInfo.Promise(stringArrayType),
                RequiredParams: 1),
            ["resolve4"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolve6"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["reverse"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveMx"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveTxt"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveSrv"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveCname"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveNs"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveSoa"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolvePtr"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveCaa"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveNaptr"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["setDefaultResultOrder"] = new TypeInfo.Function([stringType], new TypeInfo.Void()),
            ["getDefaultResultOrder"] = new TypeInfo.Function([], stringType)
        };
    }

    /// <summary>
    /// Gets the exported types for the net module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetNetModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var voidType = new TypeInfo.Void();
        var booleanType = BooleanType;

        // EventEmitter methods shared by Server and Socket
        var eventEmitterMembers = new Dictionary<string, TypeInfo>
        {
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], booleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        };

        // Socket type
        var socketMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["connect"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["write"] = new TypeInfo.Function([anyType, anyType, anyType], booleanType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["setNoDelay"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["setKeepAlive"] = new TypeInfo.Function([booleanType, numberType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["remoteAddress"] = stringType,
            ["remotePort"] = numberType,
            ["remoteFamily"] = stringType,
            ["localAddress"] = stringType,
            ["localPort"] = numberType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,
            ["connecting"] = booleanType,
            ["destroyed"] = booleanType,
            ["readyState"] = stringType,
            ["writableLength"] = numberType,
            ["writableHighWaterMark"] = numberType,
            ["writableNeedDrain"] = booleanType,
            ["localFamily"] = stringType,
            ["pending"] = booleanType,
            ["allowHalfOpen"] = booleanType
        };
        var socketType = new TypeInfo.Record(socketMembers.ToFrozenDictionary());

        // Server type
        var serverMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["listen"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["getConnections"] = new TypeInfo.Function([anyType], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["listening"] = booleanType,
            ["maxConnections"] = numberType
        };
        var serverType = new TypeInfo.Record(serverMembers.ToFrozenDictionary());

        // net.SocketAddress instance type (#1069)
        var socketAddressType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = stringType,
            ["port"] = numberType,
            ["flowlabel"] = numberType,
            ["toJSON"] = new TypeInfo.Function([], anyType)
        }.ToFrozenDictionary());

        // net.BlockList instance type (#1069)
        var blockListType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["addAddress"] = new TypeInfo.Function([anyType, stringType], voidType, RequiredParams: 1),
            ["addRange"] = new TypeInfo.Function([anyType, anyType, stringType], voidType, RequiredParams: 2),
            ["addSubnet"] = new TypeInfo.Function([anyType, numberType, stringType], voidType, RequiredParams: 2),
            ["check"] = new TypeInfo.Function([anyType, stringType], booleanType, RequiredParams: 1),
            ["rules"] = new TypeInfo.Array(stringType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            // connect(options|port|path[, host][, connectListener]) — three positional args
            ["createConnection"] = new TypeInfo.Function([anyType, anyType, anyType], socketType, RequiredParams: 1),
            ["connect"] = new TypeInfo.Function([anyType, anyType, anyType], socketType, RequiredParams: 1),
            ["isIP"] = new TypeInfo.Function([stringType], numberType),
            ["isIPv4"] = new TypeInfo.Function([stringType], booleanType),
            ["isIPv6"] = new TypeInfo.Function([stringType], booleanType),
            ["Server"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["Socket"] = new TypeInfo.Function([anyType], socketType, RequiredParams: 0),
            ["BlockList"] = new TypeInfo.Function([], blockListType, RequiredParams: 0),
            ["SocketAddress"] = new TypeInfo.Function([anyType], socketAddressType, RequiredParams: 0),
            ["getDefaultAutoSelectFamily"] = new TypeInfo.Function([], booleanType),
            ["setDefaultAutoSelectFamily"] = new TypeInfo.Function([booleanType], voidType),
            ["getDefaultAutoSelectFamilyAttemptTimeout"] = new TypeInfo.Function([], numberType),
            ["setDefaultAutoSelectFamilyAttemptTimeout"] = new TypeInfo.Function([numberType], voidType)
        };
    }

    /// <summary>
    /// Gets the exported types for the tls module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTlsModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var booleanType = BooleanType;

        // EventEmitter methods shared by Server and Socket
        var eventEmitterMembers = new Dictionary<string, TypeInfo>
        {
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], booleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        };

        // TLSSocket type - extends Socket with TLS-specific members
        var tlsSocketMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            // Inherited Socket methods
            ["connect"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["write"] = new TypeInfo.Function([anyType, anyType, anyType], booleanType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["setNoDelay"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["setKeepAlive"] = new TypeInfo.Function([booleanType, numberType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["remoteAddress"] = stringType,
            ["remotePort"] = numberType,
            ["remoteFamily"] = stringType,
            ["localAddress"] = stringType,
            ["localPort"] = numberType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,
            ["connecting"] = booleanType,
            ["destroyed"] = booleanType,
            ["readyState"] = stringType,
            // TLS-specific properties
            ["authorized"] = booleanType,
            ["authorizationError"] = new TypeInfo.Union([stringType, new TypeInfo.Null()]),
            ["encrypted"] = booleanType,
            ["alpnProtocol"] = new TypeInfo.Union([stringType, new TypeInfo.Null()]),
            ["servername"] = new TypeInfo.Union([stringType, new TypeInfo.Undefined()]),
            // TLS-specific methods
            ["getCipher"] = new TypeInfo.Function([], anyType),
            ["getPeerCertificate"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["getProtocol"] = new TypeInfo.Function([], new TypeInfo.Union([stringType, new TypeInfo.Null()])),
            ["renegotiate"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 0),
            // Advanced TLS APIs (throw "not supported" on this runtime — see #1032 SslStream ceilings)
            ["getSession"] = new TypeInfo.Function([], anyType),
            ["setSession"] = new TypeInfo.Function([anyType], anyType),
            ["getTLSTicket"] = new TypeInfo.Function([], anyType),
            ["getPeerFinished"] = new TypeInfo.Function([], anyType),
            ["getFinished"] = new TypeInfo.Function([], anyType),
            ["setMaxSendFragment"] = new TypeInfo.Function([numberType], anyType),
            ["exportKeyingMaterial"] = new TypeInfo.Function([numberType, stringType, anyType], anyType, RequiredParams: 2)
        };
        var tlsSocketType = new TypeInfo.Record(tlsSocketMembers.ToFrozenDictionary());

        // TLS Server type
        var serverMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["listen"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["getConnections"] = new TypeInfo.Function([anyType], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["listening"] = booleanType,
            ["maxConnections"] = numberType
        };
        var serverType = new TypeInfo.Record(serverMembers.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["connect"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], tlsSocketType, RequiredParams: 1),
            ["createSecureContext"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            // checkServerIdentity(host, cert) → Error | undefined
            ["checkServerIdentity"] = new TypeInfo.Function([stringType, anyType],
                new TypeInfo.Union([anyType, new TypeInfo.Undefined()])),
            ["getCiphers"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["rootCertificates"] = new TypeInfo.Array(stringType),
            ["Server"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["TLSSocket"] = new TypeInfo.Function([anyType], tlsSocketType, RequiredParams: 0),
            ["DEFAULT_MIN_VERSION"] = stringType,
            ["DEFAULT_MAX_VERSION"] = stringType
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
            ["getMaxListeners"] = new TypeInfo.Function([], numberType),

            // Server lifecycle (#1045)
            ["closeAllConnections"] = new TypeInfo.Function([], voidType),
            ["closeIdleConnections"] = new TypeInfo.Function([], voidType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 0),
            ["keepAliveTimeout"] = numberType,
            ["headersTimeout"] = numberType,
            ["requestTimeout"] = numberType,
            ["timeout"] = numberType,
            ["maxHeadersCount"] = numberType,
            ["maxRequestsPerSocket"] = numberType
        }.ToFrozenDictionary());

        // STATUS_CODES type - with string index signature for dynamic property access
        var statusCodesType = new TypeInfo.Record(
            new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            StringIndexType: stringType  // Allow any string key to return a string
        );

        // METHODS type - array of strings
        var methodsType = new TypeInfo.Array(stringType);

        // Agent type with full API surface
        var boolType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        var agentType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["keepAlive"] = boolType,
            ["keepAliveMsecs"] = numberType,
            ["maxSockets"] = numberType,
            ["maxTotalSockets"] = numberType,
            ["maxFreeSockets"] = numberType,
            ["timeout"] = numberType,
            ["scheduling"] = stringType,
            ["sockets"] = anyType,
            ["freeSockets"] = anyType,
            ["requests"] = anyType,
            ["destroy"] = new TypeInfo.Function([], voidType),
            ["getName"] = new TypeInfo.Function([anyType], stringType, RequiredParams: 0),
            ["createConnection"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, RequiredParams: 1, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0)
        }.ToFrozenDictionary());

        // Agent constructor type
        var agentConstructorType = new TypeInfo.Function([anyType], agentType, RequiredParams: 0);

        return new Dictionary<string, TypeInfo>
        {
            // createServer accepts an optional options object (https TLS opts: key/cert/ca/pfx/
            // passphrase/SNICallback/ALPNProtocols, #1049) and/or a (req,res) handler.
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            // request/get return a ClientRequest (#1043) typed as any so the writable + event
            // surface (write/end/setHeader/on('response')/...) type-checks without over-constraining.
            ["request"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["get"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["METHODS"] = methodsType,
            ["STATUS_CODES"] = statusCodesType,
            ["globalAgent"] = agentType,
            ["Agent"] = agentConstructorType,
            // Utilities + constants (#1052)
            ["validateHeaderName"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["validateHeaderValue"] = new TypeInfo.Function([stringType, anyType], voidType, RequiredParams: 2),
            ["maxHeaderSize"] = numberType,
            ["setMaxIdleHTTPParsers"] = new TypeInfo.Function([numberType], voidType, RequiredParams: 0)
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
    /// Gets the exported types for the timers/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTimersPromisesModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
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
            ["readableHighWaterMark"] = numberType,
            ["readableEncoding"] = stringType,
            ["readableFlowing"] = new TypeInfo.Union([boolType, new TypeInfo.Null()]),
            ["readableObjectMode"] = boolType,
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
            ["writableCorked"] = numberType,
            ["writableHighWaterMark"] = numberType,
            ["writableObjectMode"] = boolType,

            // Stream path properties (for ReadStream/WriteStream)
            ["path"] = stringType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,

            // Stream utility methods
            ["toArray"] = new TypeInfo.Function([], new TypeInfo.Array(anyType)),
            ["forEach"] = new TypeInfo.Function([anyType], voidType),
            ["map"] = new TypeInfo.Function([anyType], anyType),
            ["filter"] = new TypeInfo.Function([anyType], anyType),

            // Async-iterator helpers (#1025): consuming helpers return Promises,
            // transform-returning helpers (drop/take/flatMap/asIndexedPairs) return a stream.
            ["reduce"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(anyType), RequiredParams: 1),
            ["some"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(boolType)),
            ["every"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(boolType)),
            ["find"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(anyType)),
            ["flatMap"] = new TypeInfo.Function([anyType], anyType),
            ["drop"] = new TypeInfo.Function([numberType], anyType),
            ["take"] = new TypeInfo.Function([numberType], anyType),
            ["asIndexedPairs"] = new TypeInfo.Function([], anyType),

            // Async-iterable surface (#1024): `for await (const x of readable)`.
            ["@@asyncIterator"] = new TypeInfo.Function([], anyType)
        }.ToFrozenDictionary());

        // Readable constructor with static methods
        var readableConstructorType = new TypeInfo.Interface(
            Name: "Readable",
            Members: new Dictionary<string, TypeInfo>
            {
                ["from"] = new TypeInfo.Function([anyType, anyType], streamInstanceType, RequiredParams: 1),
                ["isReadable"] = new TypeInfo.Function([anyType], boolType),
                ["toWeb"] = new TypeInfo.Function([anyType], anyType),     // #1029
                ["fromWeb"] = new TypeInfo.Function([anyType], streamInstanceType) // #1029
            }.ToFrozenDictionary(),
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

        // Writable constructor with static methods
        var writableConstructorType = new TypeInfo.Interface(
            Name: "Writable",
            Members: new Dictionary<string, TypeInfo>
            {
                ["isWritable"] = new TypeInfo.Function([anyType], boolType)
            }.ToFrozenDictionary(),
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
            Members: new Dictionary<string, TypeInfo>
            {
                ["from"] = new TypeInfo.Function([anyType, anyType], streamInstanceType, RequiredParams: 1)
            }.ToFrozenDictionary(),
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

        // finished function type
        var finishedType = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1);

        // pipeline function type (rest params)
        var pipelineType = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 2, HasRestParam: true);

        // addAbortSignal function type
        var addAbortSignalType = new TypeInfo.Function([anyType, anyType], anyType);

        // compose function type (#1028): compose(...streams) → Duplex
        var composeType = new TypeInfo.Function([anyType], streamInstanceType, RequiredParams: 1, HasRestParam: true);

        // #1030 statics
        var isErroredType = new TypeInfo.Function([anyType], boolType);
        var getDefaultHwmType = new TypeInfo.Function([boolType], numberType, RequiredParams: 0);
        var setDefaultHwmType = new TypeInfo.Function([boolType, numberType], voidType);

        // promises sub-module
        var promisesType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["pipeline"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 2, HasRestParam: true),
            ["finished"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 1)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["Readable"] = readableConstructorType,
            ["Writable"] = writableConstructorType,
            ["Duplex"] = duplexConstructorType,
            ["Transform"] = transformConstructorType,
            ["PassThrough"] = passThroughConstructorType,
            ["finished"] = finishedType,
            ["pipeline"] = pipelineType,
            ["addAbortSignal"] = addAbortSignalType,
            ["compose"] = composeType,
            ["isErrored"] = isErroredType,
            ["getDefaultHighWaterMark"] = getDefaultHwmType,
            ["setDefaultHighWaterMark"] = setDefaultHwmType,
            ["promises"] = promisesType
        };
    }

    /// <summary>
    /// Gets the exported types for the stream/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetStreamPromisesModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var voidType = new TypeInfo.Void();

        return new Dictionary<string, TypeInfo>
        {
            ["pipeline"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 2, HasRestParam: true),
            ["finished"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 1)
        };
    }

    /// <summary>
    /// Gets the exported types for the <c>stream/web</c> module (WHATWG Web Streams).
    /// </summary>
    /// <remarks>
    /// All constructors are typed as <c>Any</c> — members are resolved
    /// dynamically at runtime. Matches the Headers/BroadcastChannel pattern.
    /// </remarks>
    public static Dictionary<string, TypeInfo> GetStreamWebModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        return new Dictionary<string, TypeInfo>
        {
            ["ReadableStream"] = anyType,
            ["WritableStream"] = anyType,
            ["TransformStream"] = anyType,
            ["ByteLengthQueuingStrategy"] = anyType,
            ["CountQueuingStrategy"] = anyType,
            ["ReadableStreamDefaultReader"] = anyType,
            ["ReadableStreamDefaultController"] = anyType,
            ["WritableStreamDefaultWriter"] = anyType,
            ["WritableStreamDefaultController"] = anyType,
            ["TransformStreamDefaultController"] = anyType,
        };
    }

    /// <summary>
    /// Gets the exported types for the dgram module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDgramModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var voidType = new TypeInfo.Void();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var boolType = new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

        // Socket instance type (extends EventEmitter)
        var socketType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, RequiredParams: 1, HasRestParam: true),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),

            // Socket methods
            ["bind"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["send"] = new TypeInfo.Function([anyType, anyType, anyType, anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["close"] = new TypeInfo.Function([anyType], voidType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["setBroadcast"] = new TypeInfo.Function([boolType], voidType),
            ["setTTL"] = new TypeInfo.Function([numberType], voidType),
            ["setMulticastTTL"] = new TypeInfo.Function([numberType], voidType),
            ["addMembership"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["dropMembership"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["addSourceSpecificMembership"] = new TypeInfo.Function([stringType, stringType, stringType], voidType, RequiredParams: 2),
            ["dropSourceSpecificMembership"] = new TypeInfo.Function([stringType, stringType, stringType], voidType, RequiredParams: 2),
            ["setMulticastLoopback"] = new TypeInfo.Function([boolType], voidType),
            ["setMulticastInterface"] = new TypeInfo.Function([stringType], voidType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["connect"] = new TypeInfo.Function([numberType, stringType, anyType], voidType, RequiredParams: 1),
            ["disconnect"] = new TypeInfo.Function([], voidType),
            ["remoteAddress"] = new TypeInfo.Function([], anyType),
            ["getRecvBufferSize"] = new TypeInfo.Function([], numberType),
            ["setRecvBufferSize"] = new TypeInfo.Function([numberType], voidType),
            ["getSendBufferSize"] = new TypeInfo.Function([], numberType),
            ["setSendBufferSize"] = new TypeInfo.Function([numberType], voidType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createSocket"] = new TypeInfo.Function([anyType, anyType], socketType, RequiredParams: 1),
            ["Socket"] = new TypeInfo.Function([anyType, anyType], socketType, RequiredParams: 1)
        };
    }

    /// <summary>
    /// Gets the exported types for the cluster module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetClusterModuleTypes()
    {
        var boolType = BooleanType;
        var anyType = new TypeInfo.Any();
        var voidType = new TypeInfo.Void();
        var stringType = new TypeInfo.String();
        var numberType = new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        var stringArrayType = new TypeInfo.Array(stringType);

        // worker.process — the ChildProcess-like handle (#1169; thread-model approximation)
        var workerProcessType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["pid"] = numberType,
            ["connected"] = boolType,
            ["kill"] = new TypeInfo.Function([stringType], boolType, RequiredParams: 0),
            ["send"] = new TypeInfo.Function([anyType], boolType),
            ["disconnect"] = new TypeInfo.Function([], voidType),
            ["stdout"] = anyType,
            ["stderr"] = anyType,
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
        }.ToFrozenDictionary());

        // Worker type
        var workerType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["id"] = numberType,
            ["process"] = workerProcessType,
            ["send"] = new TypeInfo.Function([anyType], boolType),
            ["disconnect"] = new TypeInfo.Function([], voidType),
            ["kill"] = new TypeInfo.Function([stringType], voidType, RequiredParams: 0),
            ["destroy"] = new TypeInfo.Function([stringType], voidType, RequiredParams: 0),
            ["isDead"] = new TypeInfo.Function([], boolType),
            ["isConnected"] = new TypeInfo.Function([], boolType),
            ["exitedAfterDisconnect"] = boolType,
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
        }.ToFrozenDictionary());

        // cluster.settings — normalized by setupPrimary/fork (#1170). Runtime-guaranteed
        // fields (exec/args/execArgv/silent/serialization) plus the stored/echoed ones.
        var settingsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["exec"] = stringType,
            ["args"] = stringArrayType,
            ["execArgv"] = stringArrayType,
            ["silent"] = boolType,
            ["serialization"] = stringType,
            ["cwd"] = stringType,
            ["stdio"] = anyType,
            ["env"] = anyType,
            ["inspectPort"] = numberType,
            ["windowsHide"] = boolType,
        }.ToFrozenDictionary());

        // cluster.workers — live id→Worker map (#1167)
        var workersType = new TypeInfo.Record(
            FrozenDictionary<string, TypeInfo>.Empty,
            StringIndexType: workerType);

        return new Dictionary<string, TypeInfo>
        {
            // Boolean properties
            ["isPrimary"] = boolType,
            ["isWorker"] = boolType,
            ["isMaster"] = boolType,

            // Methods
            ["fork"] = new TypeInfo.Function([anyType], workerType, RequiredParams: 0),
            ["disconnect"] = new TypeInfo.Function([anyType], voidType, RequiredParams: 0),
            ["setupPrimary"] = new TypeInfo.Function([anyType], voidType, RequiredParams: 0),
            ["setupMaster"] = new TypeInfo.Function([anyType], voidType, RequiredParams: 0),

            // Properties
            ["workers"] = workersType,
            ["worker"] = workerType,
            ["settings"] = settingsType,

            // Scheduling policy (#1170)
            ["schedulingPolicy"] = numberType,
            ["SCHED_NONE"] = numberType,
            ["SCHED_RR"] = numberType,

            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["listeners"] = new TypeInfo.Function([stringType], anyType),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], anyType),
        };
    }

    // GetAsyncHooksModuleTypes removed — "async_hooks" is now implemented in
    // stdlib/node/async_hooks.ts; types flow from the TS source. See
    // GetAsyncHooksPrimitiveTypes for primitive:async_hooks.

    public static Dictionary<string, TypeInfo> GetVmModuleTypes()
    {
        var anyType = new TypeInfo.Any();
        var stringType = new TypeInfo.String();
        var boolType = BooleanType;

        // Script instance type
        var scriptType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["runInNewContext"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 0),
            ["runInThisContext"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["runInContext"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["createCachedData"] = new TypeInfo.Function([], anyType, RequiredParams: 0),
            ["cachedData"] = anyType,
            ["cachedDataProduced"] = BooleanType,
            ["cachedDataRejected"] = BooleanType,
            ["sourceMapURL"] = anyType,
        }.ToFrozenDictionary());

        var stringArrayType = new TypeInfo.Array(stringType);

        // vm.Module / SourceTextModule / SyntheticModule instance shape.
        var moduleType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["status"] = stringType,
            ["identifier"] = stringType,
            ["namespace"] = anyType,
            ["dependencySpecifiers"] = stringArrayType,
            ["error"] = anyType,
            ["context"] = anyType,
            ["link"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 1),
            ["evaluate"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["instantiate"] = new TypeInfo.Function([], anyType, RequiredParams: 0),
            ["setExport"] = new TypeInfo.Function([stringType, anyType], anyType, RequiredParams: 2),
            ["createCachedData"] = new TypeInfo.Function([], anyType, RequiredParams: 0),
        }.ToFrozenDictionary());

        // vm.constants — opaque sentinel Symbols used as marker option values.
        var constantsType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["USE_MAIN_CONTEXT_DEFAULT_LOADER"] = anyType,
            ["DONT_CONTEXTIFY"] = anyType,
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["runInNewContext"] = new TypeInfo.Function([stringType, anyType, anyType], anyType, RequiredParams: 1),
            ["runInThisContext"] = new TypeInfo.Function([stringType, anyType], anyType, RequiredParams: 1),
            ["runInContext"] = new TypeInfo.Function([stringType, anyType, anyType], anyType, RequiredParams: 2),
            ["createContext"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 0),
            ["isContext"] = new TypeInfo.Function([anyType], boolType),
            ["compileFunction"] = new TypeInfo.Function([stringType, stringArrayType, anyType], anyType, RequiredParams: 1),
            ["measureMemory"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["constants"] = constantsType,
            ["Script"] = new TypeInfo.Function([stringType, anyType], scriptType, RequiredParams: 1),
            ["SourceTextModule"] = new TypeInfo.Function([stringType, anyType], moduleType, RequiredParams: 1),
            ["SyntheticModule"] = new TypeInfo.Function([stringArrayType, anyType, anyType], moduleType, RequiredParams: 2),
        };
    }

    // GetTtyModuleTypes removed — "tty" is now implemented in stdlib/node/tty.ts;
    // types flow from the TS source. See GetTtyPrimitiveTypes for primitive:tty.
}
