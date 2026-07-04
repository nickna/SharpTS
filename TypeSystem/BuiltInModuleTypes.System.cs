using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

// Split out of BuiltInModuleTypes.cs (#1143): fs/process/child_process/worker_threads/cluster/vm module type signatures.
public static partial class BuiltInModuleTypes
{
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
