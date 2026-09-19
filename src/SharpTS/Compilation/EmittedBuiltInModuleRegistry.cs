using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Checked callable-export index for one emitted module.</summary>
/// <remarks>
/// Feature selection reserves all required export keys before any declaration is registered.
/// Entries index family-owned declarations; intentional aliases may share a method.
/// Registration and lookup do not require emitted bodies or completed declaring types.
/// </remarks>
public sealed class EmittedBuiltInModuleRegistry
{
    private readonly Dictionary<(string Module, string Method), MethodBuilder?> _methods = new();
    private ModuleBuilder? _module;

    public bool IsComplete { get; private set; }

    /// <summary>Read-only selected keys, including declarations whose bodies are not emitted yet.</summary>
    public IReadOnlyCollection<(string Module, string Method)> SelectedExports
    {
        get { EnsureStarted(); return _methods.Keys; }
    }

    internal void BeginEmission(ModuleBuilder module, RuntimeFeatureSet features)
    {
        if (_module is not null)
            throw new InvalidOperationException("Built-in module registration has already started.");
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(features);
        _module = module;
        if (features.UsesNodeStreams)
            Select("stream",
                "Duplex", "finished", "PassThrough", "pipeline",
                "Readable", "Transform", "Writable");
        if (features.UsesNodeStreams)
            Select("stream/promises",
                "finished", "pipeline");
        if (features.UsesFs)
            Select("fs/promises",
                "access", "appendFile", "chmod", "copyFile",
                "link", "lstat", "mkdir", "mkdtemp",
                "readdir", "readFile", "readlink", "realpath",
                "rename", "rm", "rmdir", "stat",
                "symlink", "truncate", "unlink", "utimes",
                "writeFile");
        if (features.UsesDns)
            Select("dns",
                "getDefaultResultOrder", "resolve", "resolve4", "resolve6",
                "resolveCaa", "resolveCname", "resolveMx", "resolveNaptr",
                "resolveNs", "resolvePtr", "resolveSoa", "resolveSrv",
                "resolveTxt", "reverse", "setDefaultResultOrder");
        if (features.UsesDns)
            Select("dns/promises",
                "getDefaultResultOrder", "setDefaultResultOrder");
        if (features.UsesFs)
            Select("fs",
                "accessSync", "appendFileSync", "chmodSync", "chownSync",
                "copyFileSync", "createReadStream", "createWriteStream", "existsSync",
                "lchownSync", "lstatSync", "mkdirSync", "readdirSync",
                "readFileSync", "readlinkSync", "realpathSync", "renameSync",
                "rmdirSync", "statSync", "symlinkSync", "truncateSync",
                "unlinkSync", "utimesSync", "writeFileSync");
        if (features.UsesHttp)
            Select("http",
                "createServer", "get", "request");
        if (features.UsesNet)
            Select("primitive:net",
                "createBlockList", "createConnection", "createServer", "createSocket");
        if (features.UsesTls)
            Select("tls",
                "checkServerIdentity", "connect", "createSecureContext", "createServer",
                "getCiphers", "Server", "TLSSocket");
        if (features.UsesDgram)
            Select("dgram",
                "createSocket");
        if (features.UsesCrypto)
            Select("crypto",
                "checkPrime", "checkPrimeSync", "createCipheriv", "createDecipheriv",
                "createDiffieHellman", "createECDH", "createHash", "createHmac",
                "createPrivateKey", "createPublicKey", "createSecretKey", "createSign",
                "createVerify", "diffieHellman", "generateKey", "generateKeyPair",
                "generateKeyPairSync", "generateKeySync", "generatePrime", "generatePrimeSync",
                "getCipherInfo", "getCiphers", "getCurves", "getDiffieHellman",
                "getFips", "getHashes", "getRandomValues", "hash",
                "hkdf", "hkdfSync", "pbkdf2", "pbkdf2Sync",
                "privateDecrypt", "privateEncrypt", "publicDecrypt", "publicEncrypt",
                "randomBytes", "randomFill", "randomInt", "randomUUID",
                "scrypt", "scryptSync", "setFips", "sign",
                "timingSafeEqual", "verify", "X509Certificate");
        if (features.UsesChildProcess)
            Select("child_process",
                "exec", "execFile", "execFileSync", "execSync",
                "fork", "spawn", "spawnSync");
        Select("timers",
            "clearImmediate", "clearInterval", "clearTimeout", "setImmediate",
            "setInterval", "setTimeout");
        if (features.UsesPromise)
            Select("timers/promises",
                "setImmediate", "setInterval", "setTimeout");
        if (features.UsesZlib)
            Select("primitive:zlib",
                "brotliCompressSync", "brotliDecompressSync", "crc32", "createBrotliCompress",
                "createBrotliDecompress", "createDeflate", "createDeflateRaw", "createGunzip",
                "createGzip", "createInflate", "createInflateRaw", "createUnzip",
                "createZstdCompress", "createZstdDecompress", "deflateRawSync", "deflateSync",
                "gunzipSync", "gzipSync", "inflateRawSync", "inflateSync",
                "unzipSync", "zstdCompressSync", "zstdDecompressSync");
        if (features.UsesVm)
            Select("vm",
                "compileFunction", "createContext", "isContext", "measureMemory",
                "runInContext", "runInNewContext", "runInThisContext", "Script");
        if (features.UsesSourceExecution)
            Select("sharpts:execution",
                "configureUntrustedProcess", "runSourceJson");
    }

    private void Select(string module, params string[] names)
    {
        foreach (string name in names)
            _methods.Add((module, name), null);
    }

    /// <summary>Whether this compilation requires the callable export.</summary>
    public bool IsSelected(string module, string name)
    {
        EnsureStarted();
        ValidateKey(module, name);
        return _methods.ContainsKey((module, name));
    }

    /// <summary>Returns null for an unselected export; a selected but undeclared export is an error.</summary>
    public MethodBuilder? GetOptional(string module, string name)
    {
        EnsureStarted();
        ValidateKey(module, name);
        if (!_methods.TryGetValue((module, name), out var method))
            return null;
        return method ?? throw new InvalidOperationException($"Built-in export '{module}.{name}' has not been declared.");
    }

    public MethodBuilder Require(string module, string name) => GetOptional(module, name)
        ?? throw new InvalidOperationException($"Built-in export '{module}.{name}' was not selected for this compilation.");

    internal void Register(string module, string name, MethodBuilder method)
    {
        EnsureMutable();
        ValidateKey(module, name);
        ArgumentNullException.ThrowIfNull(method);
        if (!_methods.TryGetValue((module, name), out var existing))
            throw new InvalidOperationException($"Built-in export '{module}.{name}' was not selected for this compilation.");
        if (existing is not null)
            throw new InvalidOperationException($"Built-in export '{module}.{name}' has already been declared.");
        if (!ReferenceEquals(method.Module, _module))
            throw new ArgumentException("Built-in exports must belong to this compilation's module.", nameof(method));
        _methods[(module, name)] = method;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        foreach (var (module, name) in _methods.Keys)
            _ = Require(module, name);
        IsComplete = true;
    }

    private void EnsureStarted()
    {
        if (_module is null)
            throw new InvalidOperationException("Built-in module registration has not started.");
    }

    private void EnsureMutable()
    {
        EnsureStarted();
        if (IsComplete)
            throw new InvalidOperationException("Built-in module registration is complete.");
    }

    private static void ValidateKey(string module, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(module);
        ArgumentException.ThrowIfNullOrEmpty(name);
    }
}
