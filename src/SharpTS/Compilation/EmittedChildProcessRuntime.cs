using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional child-process declarations and family-specific BCL references for one compilation.
/// Forward declarations are readable before their bodies; completion validates and freezes metadata.
/// </summary>
public sealed class EmittedChildProcessRuntime
{
    internal EmittedChildProcessRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _execSync;
    public MethodBuilder ExecSync
    {
        get => Require(_execSync);
        internal set => Set(ref _execSync, value);
    }

    private MethodBuilder? _spawnSync;
    public MethodBuilder SpawnSync
    {
        get => Require(_spawnSync);
        internal set => Set(ref _spawnSync, value);
    }

    private MethodBuilder? _exec;
    public MethodBuilder Exec
    {
        get => Require(_exec);
        internal set => Set(ref _exec, value);
    }

    private MethodBuilder? _spawn;
    public MethodBuilder Spawn
    {
        get => Require(_spawn);
        internal set => Set(ref _spawn, value);
    }

    private MethodBuilder? _execFileSync;
    public MethodBuilder ExecFileSync
    {
        get => Require(_execFileSync);
        internal set => Set(ref _execFileSync, value);
    }

    private MethodBuilder? _execFile;
    public MethodBuilder ExecFile
    {
        get => Require(_execFile);
        internal set => Set(ref _execFile, value);
    }

    private MethodBuilder? _fork;
    public MethodBuilder Fork
    {
        get => Require(_fork);
        internal set => Set(ref _fork, value);
    }

    private FieldBuilder? _ownedProcessesField;
    public FieldBuilder OwnedProcessesField
    {
        get => Require(_ownedProcessesField);
        internal set => Set(ref _ownedProcessesField, value);
    }

    private FieldBuilder? _ownershipStoppingField;
    public FieldBuilder OwnershipStoppingField
    {
        get => Require(_ownershipStoppingField);
        internal set => Set(ref _ownershipStoppingField, value);
    }

    private MethodBuilder? _registerOwned;
    public MethodBuilder RegisterOwned
    {
        get => Require(_registerOwned);
        internal set => Set(ref _registerOwned, value);
    }

    private MethodBuilder? _unregisterOwned;
    public MethodBuilder UnregisterOwned
    {
        get => Require(_unregisterOwned);
        internal set => Set(ref _unregisterOwned, value);
    }

    private MethodBuilder? _releaseOwned;
    public MethodBuilder ReleaseOwned
    {
        get => Require(_releaseOwned);
        internal set => Set(ref _releaseOwned, value);
    }

    private MethodBuilder? _terminateOwned;
    public MethodBuilder TerminateOwned
    {
        get => Require(_terminateOwned);
        internal set => Set(ref _terminateOwned, value);
    }

    private MethodBuilder? _noOp;
    public MethodBuilder NoOp
    {
        get => Require(_noOp);
        internal set => Set(ref _noOp, value);
    }

    private TypeBuilder? _contextType;
    public TypeBuilder ContextType
    {
        get => Require(_contextType);
        internal set => Set(ref _contextType, value);
    }

    private ConstructorBuilder? _contextCtor;
    public ConstructorBuilder ContextCtor
    {
        get => Require(_contextCtor);
        internal set => Set(ref _contextCtor, value);
    }

    private FieldBuilder? _contextProc;
    public FieldBuilder ContextProc
    {
        get => Require(_contextProc);
        internal set => Set(ref _contextProc, value);
    }

    private FieldBuilder? _contextEmitter;
    public FieldBuilder ContextEmitter
    {
        get => Require(_contextEmitter);
        internal set => Set(ref _contextEmitter, value);
    }

    private FieldBuilder? _contextDict;
    public FieldBuilder ContextDict
    {
        get => Require(_contextDict);
        internal set => Set(ref _contextDict, value);
    }

    private FieldBuilder? _contextCallback;
    public FieldBuilder ContextCallback
    {
        get => Require(_contextCallback);
        internal set => Set(ref _contextCallback, value);
    }

    private FieldBuilder? _contextOptions;
    public FieldBuilder ContextOptions
    {
        get => Require(_contextOptions);
        internal set => Set(ref _contextOptions, value);
    }

    private FieldBuilder? _contextStdout;
    public FieldBuilder ContextStdout
    {
        get => Require(_contextStdout);
        internal set => Set(ref _contextStdout, value);
    }

    private FieldBuilder? _contextStderr;
    public FieldBuilder ContextStderr
    {
        get => Require(_contextStderr);
        internal set => Set(ref _contextStderr, value);
    }

    private FieldBuilder? _contextTimeout;
    public FieldBuilder ContextTimeout
    {
        get => Require(_contextTimeout);
        internal set => Set(ref _contextTimeout, value);
    }

    private FieldBuilder? _contextResStdout;
    public FieldBuilder ContextResStdout
    {
        get => Require(_contextResStdout);
        internal set => Set(ref _contextResStdout, value);
    }

    private FieldBuilder? _contextResStderr;
    public FieldBuilder ContextResStderr
    {
        get => Require(_contextResStderr);
        internal set => Set(ref _contextResStderr, value);
    }

    private FieldBuilder? _contextResCode;
    public FieldBuilder ContextResCode
    {
        get => Require(_contextResCode);
        internal set => Set(ref _contextResCode, value);
    }

    private FieldBuilder? _contextResError;
    public FieldBuilder ContextResError
    {
        get => Require(_contextResError);
        internal set => Set(ref _contextResError, value);
    }

    private FieldBuilder? _contextResKind;
    public FieldBuilder ContextResKind
    {
        get => Require(_contextResKind);
        internal set => Set(ref _contextResKind, value);
    }

    private FieldBuilder? _contextMaxBuffer;
    public FieldBuilder ContextMaxBuffer
    {
        get => Require(_contextMaxBuffer);
        internal set => Set(ref _contextMaxBuffer, value);
    }

    private FieldBuilder? _contextAsBuffer;
    public FieldBuilder ContextAsBuffer
    {
        get => Require(_contextAsBuffer);
        internal set => Set(ref _contextAsBuffer, value);
    }

    private FieldBuilder? _contextEncoding;
    public FieldBuilder ContextEncoding
    {
        get => Require(_contextEncoding);
        internal set => Set(ref _contextEncoding, value);
    }

    private MethodBuilder? _readCappedBytes;
    public MethodBuilder ReadCappedBytes
    {
        get => Require(_readCappedBytes);
        internal set => Set(ref _readCappedBytes, value);
    }

    private MethodBuilder? _decodeOutput;
    public MethodBuilder DecodeOutput
    {
        get => Require(_decodeOutput);
        internal set => Set(ref _decodeOutput, value);
    }

    private FieldBuilder? _contextStdoutRedir;
    public FieldBuilder ContextStdoutRedir
    {
        get => Require(_contextStdoutRedir);
        internal set => Set(ref _contextStdoutRedir, value);
    }

    private FieldBuilder? _contextStderrRedir;
    public FieldBuilder ContextStderrRedir
    {
        get => Require(_contextStderrRedir);
        internal set => Set(ref _contextStderrRedir, value);
    }

    private MethodBuilder? _contextRunCaptured;
    public MethodBuilder ContextRunCaptured
    {
        get => Require(_contextRunCaptured);
        internal set => Set(ref _contextRunCaptured, value);
    }

    private MethodBuilder? _contextEmitCaptured;
    public MethodBuilder ContextEmitCaptured
    {
        get => Require(_contextEmitCaptured);
        internal set => Set(ref _contextEmitCaptured, value);
    }

    private MethodBuilder? _contextRunStreamed;
    public MethodBuilder ContextRunStreamed
    {
        get => Require(_contextRunStreamed);
        internal set => Set(ref _contextRunStreamed, value);
    }

    private MethodBuilder? _contextEmitStreamClose;
    public MethodBuilder ContextEmitStreamClose
    {
        get => Require(_contextEmitStreamClose);
        internal set => Set(ref _contextEmitStreamClose, value);
    }

    private MethodBuilder? _contextPumpStdout;
    public MethodBuilder ContextPumpStdout
    {
        get => Require(_contextPumpStdout);
        internal set => Set(ref _contextPumpStdout, value);
    }

    private MethodBuilder? _contextPumpStderr;
    public MethodBuilder ContextPumpStderr
    {
        get => Require(_contextPumpStderr);
        internal set => Set(ref _contextPumpStderr, value);
    }

    private MethodBuilder? _contextKill;
    public MethodBuilder ContextKill
    {
        get => Require(_contextKill);
        internal set => Set(ref _contextKill, value);
    }

    private MethodBuilder? _contextSend;
    public MethodBuilder ContextSend
    {
        get => Require(_contextSend);
        internal set => Set(ref _contextSend, value);
    }

    private MethodBuilder? _contextDisconnect;
    public MethodBuilder ContextDisconnect
    {
        get => Require(_contextDisconnect);
        internal set => Set(ref _contextDisconnect, value);
    }

    private MethodBuilder? _contextRef;
    public MethodBuilder ContextRef
    {
        get => Require(_contextRef);
        internal set => Set(ref _contextRef, value);
    }

    private MethodBuilder? _contextStdinWrite;
    public MethodBuilder ContextStdinWrite
    {
        get => Require(_contextStdinWrite);
        internal set => Set(ref _contextStdinWrite, value);
    }

    private MethodBuilder? _contextStdinEnd;
    public MethodBuilder ContextStdinEnd
    {
        get => Require(_contextStdinEnd);
        internal set => Set(ref _contextStdinEnd, value);
    }

    private MethodBuilder? _runAsync;
    public MethodBuilder RunAsync
    {
        get => Require(_runAsync);
        internal set => Set(ref _runAsync, value);
    }

    private MethodBuilder? _configureSpawn;
    public MethodBuilder ConfigureSpawn
    {
        get => Require(_configureSpawn);
        internal set => Set(ref _configureSpawn, value);
    }

    private MethodBuilder? _stdioMode;
    public MethodBuilder StdioMode
    {
        get => Require(_stdioMode);
        internal set => Set(ref _stdioMode, value);
    }

    private MethodBuilder? _spawnError;
    public MethodBuilder SpawnError
    {
        get => Require(_spawnError);
        internal set => Set(ref _spawnError, value);
    }

    private MethodBuilder? _contextRunSpawnError;
    public MethodBuilder ContextRunSpawnError
    {
        get => Require(_contextRunSpawnError);
        internal set => Set(ref _contextRunSpawnError, value);
    }

    private TypeBuilder? _pushType;
    public TypeBuilder PushType
    {
        get => Require(_pushType);
        internal set => Set(ref _pushType, value);
    }

    private ConstructorBuilder? _pushCtor;
    public ConstructorBuilder PushCtor
    {
        get => Require(_pushCtor);
        internal set => Set(ref _pushCtor, value);
    }

    private MethodBuilder? _pushRun;
    public MethodBuilder PushRun
    {
        get => Require(_pushRun);
        internal set => Set(ref _pushRun, value);
    }

    private FieldBuilder? _pushStream;
    public FieldBuilder PushStream
    {
        get => Require(_pushStream);
        internal set => Set(ref _pushStream, value);
    }

    private FieldBuilder? _pushChunk;
    public FieldBuilder PushChunk
    {
        get => Require(_pushChunk);
        internal set => Set(ref _pushChunk, value);
    }

    private MethodInfo? _processStart;
    public MethodInfo ProcessStart
    {
        get => Require(_processStart);
        internal set => Set(ref _processStart, value);
    }

    private MethodInfo? _processIdGet;
    public MethodInfo ProcessIdGet
    {
        get => Require(_processIdGet);
        internal set => Set(ref _processIdGet, value);
    }

    private MethodInfo? _processStdoutGet;
    public MethodInfo ProcessStdoutGet
    {
        get => Require(_processStdoutGet);
        internal set => Set(ref _processStdoutGet, value);
    }

    private MethodInfo? _processStderrGet;
    public MethodInfo ProcessStderrGet
    {
        get => Require(_processStderrGet);
        internal set => Set(ref _processStderrGet, value);
    }

    private MethodInfo? _processExitCodeGet;
    public MethodInfo ProcessExitCodeGet
    {
        get => Require(_processExitCodeGet);
        internal set => Set(ref _processExitCodeGet, value);
    }

    private MethodInfo? _processHasExitedGet;
    public MethodInfo ProcessHasExitedGet
    {
        get => Require(_processHasExitedGet);
        internal set => Set(ref _processHasExitedGet, value);
    }

    private MethodInfo? _processWaitForExit;
    public MethodInfo ProcessWaitForExit
    {
        get => Require(_processWaitForExit);
        internal set => Set(ref _processWaitForExit, value);
    }

    private MethodInfo? _processWaitForExitMs;
    public MethodInfo ProcessWaitForExitMs
    {
        get => Require(_processWaitForExitMs);
        internal set => Set(ref _processWaitForExitMs, value);
    }

    private MethodInfo? _processKillTree;
    public MethodInfo ProcessKillTree
    {
        get => Require(_processKillTree);
        internal set => Set(ref _processKillTree, value);
    }

    private MethodInfo? _exceptionMessageGet;
    public MethodInfo ExceptionMessageGet
    {
        get => Require(_exceptionMessageGet);
        internal set => Set(ref _exceptionMessageGet, value);
    }

    private MethodInfo? _setDictionaryItem;
    public MethodInfo SetDictionaryItem
    {
        get => Require(_setDictionaryItem);
        internal set => Set(ref _setDictionaryItem, value);
    }

    private MethodInfo? _getMethodFromHandle;
    public MethodInfo GetMethodFromHandle
    {
        get => Require(_getMethodFromHandle);
        internal set => Set(ref _getMethodFromHandle, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Child-process metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Child-process metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ExecSync;
        _ = SpawnSync;
        _ = Exec;
        _ = Spawn;
        _ = ExecFileSync;
        _ = ExecFile;
        _ = Fork;
        _ = OwnedProcessesField;
        _ = OwnershipStoppingField;
        _ = RegisterOwned;
        _ = UnregisterOwned;
        _ = ReleaseOwned;
        _ = TerminateOwned;
        _ = NoOp;
        _ = ContextType;
        _ = ContextCtor;
        _ = ContextProc;
        _ = ContextEmitter;
        _ = ContextDict;
        _ = ContextCallback;
        _ = ContextOptions;
        _ = ContextStdout;
        _ = ContextStderr;
        _ = ContextTimeout;
        _ = ContextResStdout;
        _ = ContextResStderr;
        _ = ContextResCode;
        _ = ContextResError;
        _ = ContextResKind;
        _ = ContextMaxBuffer;
        _ = ContextAsBuffer;
        _ = ContextEncoding;
        _ = ReadCappedBytes;
        _ = DecodeOutput;
        _ = ContextStdoutRedir;
        _ = ContextStderrRedir;
        _ = ContextRunCaptured;
        _ = ContextEmitCaptured;
        _ = ContextRunStreamed;
        _ = ContextEmitStreamClose;
        _ = ContextPumpStdout;
        _ = ContextPumpStderr;
        _ = ContextKill;
        _ = ContextSend;
        _ = ContextDisconnect;
        _ = ContextRef;
        _ = ContextStdinWrite;
        _ = ContextStdinEnd;
        _ = RunAsync;
        _ = ConfigureSpawn;
        _ = StdioMode;
        _ = SpawnError;
        _ = ContextRunSpawnError;
        _ = PushType;
        _ = PushCtor;
        _ = PushRun;
        _ = PushStream;
        _ = PushChunk;
        _ = ProcessStart;
        _ = ProcessIdGet;
        _ = ProcessStdoutGet;
        _ = ProcessStderrGet;
        _ = ProcessExitCodeGet;
        _ = ProcessHasExitedGet;
        _ = ProcessWaitForExit;
        _ = ProcessWaitForExitMs;
        _ = ProcessKillTree;
        _ = ExceptionMessageGet;
        _ = SetDictionaryItem;
        _ = GetMethodFromHandle;
        IsComplete = true;
    }
}
