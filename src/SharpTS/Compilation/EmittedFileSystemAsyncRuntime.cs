using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional filesystem async operations, background dispatch, and Promise-wrapper
/// declarations for one compilation. Completion validates and freezes every handle
/// and registry entry without changing guest scheduling or I/O behavior.
/// </summary>
public sealed class EmittedFileSystemAsyncRuntime
{
    internal EmittedFileSystemAsyncRuntime()
    {
        PromisesWrapperMethods = new ReadOnlyDictionary<string, MethodBuilder>(_promiseWrappers);
    }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _readFile;
    public MethodBuilder ReadFile
    {
        get => Require(_readFile);
        internal set => Set(ref _readFile, value);
    }

    private MethodBuilder? _writeFile;
    public MethodBuilder WriteFile
    {
        get => Require(_writeFile);
        internal set => Set(ref _writeFile, value);
    }

    private MethodBuilder? _appendFile;
    public MethodBuilder AppendFile
    {
        get => Require(_appendFile);
        internal set => Set(ref _appendFile, value);
    }

    private MethodBuilder? _stat;
    public MethodBuilder Stat
    {
        get => Require(_stat);
        internal set => Set(ref _stat, value);
    }

    private MethodBuilder? _lstat;
    public MethodBuilder Lstat
    {
        get => Require(_lstat);
        internal set => Set(ref _lstat, value);
    }

    private MethodBuilder? _unlink;
    public MethodBuilder Unlink
    {
        get => Require(_unlink);
        internal set => Set(ref _unlink, value);
    }

    private MethodBuilder? _mkdir;
    public MethodBuilder Mkdir
    {
        get => Require(_mkdir);
        internal set => Set(ref _mkdir, value);
    }

    private MethodBuilder? _rmdir;
    public MethodBuilder Rmdir
    {
        get => Require(_rmdir);
        internal set => Set(ref _rmdir, value);
    }

    private MethodBuilder? _rm;
    public MethodBuilder Rm
    {
        get => Require(_rm);
        internal set => Set(ref _rm, value);
    }

    private MethodBuilder? _readdir;
    public MethodBuilder Readdir
    {
        get => Require(_readdir);
        internal set => Set(ref _readdir, value);
    }

    private MethodBuilder? _rename;
    public MethodBuilder Rename
    {
        get => Require(_rename);
        internal set => Set(ref _rename, value);
    }

    private MethodBuilder? _copyFile;
    public MethodBuilder CopyFile
    {
        get => Require(_copyFile);
        internal set => Set(ref _copyFile, value);
    }

    private MethodBuilder? _access;
    public MethodBuilder Access
    {
        get => Require(_access);
        internal set => Set(ref _access, value);
    }

    private MethodBuilder? _chmod;
    public MethodBuilder Chmod
    {
        get => Require(_chmod);
        internal set => Set(ref _chmod, value);
    }

    private MethodBuilder? _truncate;
    public MethodBuilder Truncate
    {
        get => Require(_truncate);
        internal set => Set(ref _truncate, value);
    }

    private MethodBuilder? _utimes;
    public MethodBuilder Utimes
    {
        get => Require(_utimes);
        internal set => Set(ref _utimes, value);
    }

    private MethodBuilder? _readlink;
    public MethodBuilder Readlink
    {
        get => Require(_readlink);
        internal set => Set(ref _readlink, value);
    }

    private MethodBuilder? _realpath;
    public MethodBuilder Realpath
    {
        get => Require(_realpath);
        internal set => Set(ref _realpath, value);
    }

    private MethodBuilder? _symlink;
    public MethodBuilder Symlink
    {
        get => Require(_symlink);
        internal set => Set(ref _symlink, value);
    }

    private MethodBuilder? _link;
    public MethodBuilder Link
    {
        get => Require(_link);
        internal set => Set(ref _link, value);
    }

    private MethodBuilder? _mkdtemp;
    public MethodBuilder Mkdtemp
    {
        get => Require(_mkdtemp);
        internal set => Set(ref _mkdtemp, value);
    }

    private MethodBuilder? _getPromisesNamespace;
    public MethodBuilder GetPromisesNamespace
    {
        get => Require(_getPromisesNamespace);
        internal set => Set(ref _getPromisesNamespace, value);
    }

    private ConstructorBuilder? _opCtor;
    public ConstructorBuilder OpCtor
    {
        get => Require(_opCtor);
        internal set => Set(ref _opCtor, value);
    }

    private MethodBuilder? _opWorker;
    public MethodBuilder OpWorker
    {
        get => Require(_opWorker);
        internal set => Set(ref _opWorker, value);
    }

    private MethodBuilder? _runAsync;
    public MethodBuilder RunAsync
    {
        get => Require(_runAsync);
        internal set => Set(ref _runAsync, value);
    }

    private MethodBuilder? _unref;
    public MethodBuilder Unref
    {
        get => Require(_unref);
        internal set => Set(ref _unref, value);
    }

    private static readonly string[] RequiredWrapperNames =
    [
        "readFile", "writeFile", "appendFile", "stat", "lstat",
        "unlink", "mkdir", "rmdir", "rm", "readdir",
        "rename", "copyFile", "access", "chmod", "truncate",
        "utimes", "readlink", "realpath", "symlink", "link",
        "mkdtemp",
    ];

    private readonly Dictionary<string, MethodBuilder> _promiseWrappers = new(StringComparer.Ordinal);

    /// <summary>A live read-only view in declaration order; declarations may precede bodies.</summary>
    public IReadOnlyDictionary<string, MethodBuilder> PromisesWrapperMethods { get; }

    public MethodBuilder RequirePromiseWrapper(string name) =>
        _promiseWrappers.TryGetValue(name, out var method) ? method
            : throw new InvalidOperationException($"Filesystem promise wrapper '{name}' has not been declared.");

    internal void RegisterPromiseWrapper(string name, MethodBuilder method)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(method);
        if (!RequiredWrapperNames.Contains(name))
            throw new ArgumentException($"Unknown filesystem promise wrapper '{name}'.", nameof(name));
        _promiseWrappers.Add(name, method);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Filesystem async metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Filesystem async metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ReadFile;
        _ = WriteFile;
        _ = AppendFile;
        _ = Stat;
        _ = Lstat;
        _ = Unlink;
        _ = Mkdir;
        _ = Rmdir;
        _ = Rm;
        _ = Readdir;
        _ = Rename;
        _ = CopyFile;
        _ = Access;
        _ = Chmod;
        _ = Truncate;
        _ = Utimes;
        _ = Readlink;
        _ = Realpath;
        _ = Symlink;
        _ = Link;
        _ = Mkdtemp;
        _ = GetPromisesNamespace;
        _ = OpCtor;
        _ = OpWorker;
        _ = RunAsync;
        _ = Unref;
        foreach (string name in RequiredWrapperNames)
            _ = RequirePromiseWrapper(name);
        IsComplete = true;
    }
}
