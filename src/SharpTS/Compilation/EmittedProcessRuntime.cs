using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required process metadata with optional stream singletons and hosted lifecycle declarations.
/// Declarations support forward references; completion validates every handle and freezes writes.
/// </summary>
public sealed class EmittedProcessRuntime
{
    internal EmittedProcessRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _getEnv;
    public MethodBuilder GetEnv
    {
        get => Require(_getEnv);
        internal set => Set(ref _getEnv, value);
    }

    private MethodBuilder? _getArgv;
    public MethodBuilder GetArgv
    {
        get => Require(_getArgv);
        internal set => Set(ref _getArgv, value);
    }

    private MethodBuilder? _hrtime;
    public MethodBuilder Hrtime
    {
        get => Require(_hrtime);
        internal set => Set(ref _hrtime, value);
    }

    private MethodBuilder? _uptime;
    public MethodBuilder Uptime
    {
        get => Require(_uptime);
        internal set => Set(ref _uptime, value);
    }

    /// <summary>Monotonic process-start Stopwatch timestamp captured by the runtime type initializer.</summary>
    private FieldBuilder? _uptimeBaselineField;
    public FieldBuilder UptimeBaselineField
    {
        get => Require(_uptimeBaselineField);
        internal set => Set(ref _uptimeBaselineField, value);
    }

    private MethodBuilder? _memoryUsage;
    public MethodBuilder MemoryUsage
    {
        get => Require(_memoryUsage);
        internal set => Set(ref _memoryUsage, value);
    }

    private MethodBuilder? _getNextTick;
    public MethodBuilder GetNextTick
    {
        get => Require(_getNextTick);
        internal set => Set(ref _getNextTick, value);
    }

    private MethodBuilder? _getEventEmitter;
    public MethodBuilder GetEventEmitter
    {
        get => Require(_getEventEmitter);
        internal set => Set(ref _getEventEmitter, value);
    }

    private MethodBuilder? _getObject;
    public MethodBuilder GetObject
    {
        get => Require(_getObject);
        internal set => Set(ref _getObject, value);
    }

    private MethodBuilder? _exit;
    public MethodBuilder Exit
    {
        get => Require(_exit);
        internal set => Set(ref _exit, value);
    }

    private MethodBuilder? _kill;
    public MethodBuilder Kill
    {
        get => Require(_kill);
        internal set => Set(ref _kill, value);
    }

    private MethodBuilder? _emitWarning;
    public MethodBuilder EmitWarning
    {
        get => Require(_emitWarning);
        internal set => Set(ref _emitWarning, value);
    }

    private MethodBuilder? _runLifecycle;
    public MethodBuilder RunLifecycle
    {
        get => Require(_runLifecycle);
        internal set => Set(ref _runLifecycle, value);
    }

    private MethodBuilder? _registerSignal;
    public MethodBuilder RegisterSignal
    {
        get => Require(_registerSignal);
        internal set => Set(ref _registerSignal, value);
    }

    private MethodBuilder? _dispatchSignal;
    public MethodBuilder DispatchSignal
    {
        get => Require(_dispatchSignal);
        internal set => Set(ref _dispatchSignal, value);
    }

    private MethodBuilder? _emitClosureInvoke;
    public MethodBuilder EmitClosureInvoke
    {
        get => Require(_emitClosureInvoke);
        internal set => Set(ref _emitClosureInvoke, value);
    }

    private MethodBuilder? _getPpid;
    public MethodBuilder GetPpid
    {
        get => Require(_getPpid);
        internal set => Set(ref _getPpid, value);
    }

    private MethodBuilder? _getUid;
    public MethodBuilder GetUid
    {
        get => Require(_getUid);
        internal set => Set(ref _getUid, value);
    }

    private MethodBuilder? _getEuid;
    public MethodBuilder GetEuid
    {
        get => Require(_getEuid);
        internal set => Set(ref _getEuid, value);
    }

    private MethodBuilder? _getGid;
    public MethodBuilder GetGid
    {
        get => Require(_getGid);
        internal set => Set(ref _getGid, value);
    }

    private MethodBuilder? _getEgid;
    public MethodBuilder GetEgid
    {
        get => Require(_getEgid);
        internal set => Set(ref _getEgid, value);
    }

    private MethodBuilder? _getGroups;
    public MethodBuilder GetGroups
    {
        get => Require(_getGroups);
        internal set => Set(ref _getGroups, value);
    }

    private MethodBuilder? _setUid;
    public MethodBuilder SetUid
    {
        get => Require(_setUid);
        internal set => Set(ref _setUid, value);
    }

    private MethodBuilder? _setGid;
    public MethodBuilder SetGid
    {
        get => Require(_setGid);
        internal set => Set(ref _setGid, value);
    }

    private MethodBuilder? _getTitle;
    public MethodBuilder GetTitle
    {
        get => Require(_getTitle);
        internal set => Set(ref _getTitle, value);
    }

    private MethodBuilder? _setTitle;
    public MethodBuilder SetTitle
    {
        get => Require(_setTitle);
        internal set => Set(ref _setTitle, value);
    }

    private MethodBuilder? _getVersions;
    public MethodBuilder GetVersions
    {
        get => Require(_getVersions);
        internal set => Set(ref _getVersions, value);
    }

    private MethodBuilder? _getRelease;
    public MethodBuilder GetRelease
    {
        get => Require(_getRelease);
        internal set => Set(ref _getRelease, value);
    }

    private MethodBuilder? _getFeatures;
    public MethodBuilder GetFeatures
    {
        get => Require(_getFeatures);
        internal set => Set(ref _getFeatures, value);
    }

    private MethodBuilder? _getConfig;
    public MethodBuilder GetConfig
    {
        get => Require(_getConfig);
        internal set => Set(ref _getConfig, value);
    }

    private MethodBuilder? _getExecArgv;
    public MethodBuilder GetExecArgv
    {
        get => Require(_getExecArgv);
        internal set => Set(ref _getExecArgv, value);
    }

    private MethodBuilder? _getAllowedFlags;
    public MethodBuilder GetAllowedFlags
    {
        get => Require(_getAllowedFlags);
        internal set => Set(ref _getAllowedFlags, value);
    }

    private MethodBuilder? _cpuUsage;
    public MethodBuilder CpuUsage
    {
        get => Require(_cpuUsage);
        internal set => Set(ref _cpuUsage, value);
    }

    private MethodBuilder? _resourceUsage;
    public MethodBuilder ResourceUsage
    {
        get => Require(_resourceUsage);
        internal set => Set(ref _resourceUsage, value);
    }

    private MethodBuilder? _availableMemory;
    public MethodBuilder AvailableMemory
    {
        get => Require(_availableMemory);
        internal set => Set(ref _availableMemory, value);
    }

    private MethodBuilder? _getActiveResourcesInfo;
    public MethodBuilder GetActiveResourcesInfo
    {
        get => Require(_getActiveResourcesInfo);
        internal set => Set(ref _getActiveResourcesInfo, value);
    }

    private MethodBuilder? _hrtimeBigint;
    public MethodBuilder HrtimeBigint
    {
        get => Require(_hrtimeBigint);
        internal set => Set(ref _hrtimeBigint, value);
    }

    private MethodBuilder? _memoryRss;
    public MethodBuilder MemoryRss
    {
        get => Require(_memoryRss);
        internal set => Set(ref _memoryRss, value);
    }

    private MethodBuilder? _getHrtimeFn;
    public MethodBuilder GetHrtimeFn
    {
        get => Require(_getHrtimeFn);
        internal set => Set(ref _getHrtimeFn, value);
    }

    private MethodBuilder? _getMemoryUsageFn;
    public MethodBuilder GetMemoryUsageFn
    {
        get => Require(_getMemoryUsageFn);
        internal set => Set(ref _getMemoryUsageFn, value);
    }

    private MethodBuilder? _umask;
    public MethodBuilder Umask
    {
        get => Require(_umask);
        internal set => Set(ref _umask, value);
    }

    private MethodBuilder? _getReport;
    public MethodBuilder GetReport
    {
        get => Require(_getReport);
        internal set => Set(ref _getReport, value);
    }

    private MethodBuilder? _stdinRead;
    public MethodBuilder StdinRead
    {
        get => Require(_stdinRead);
        internal set => Set(ref _stdinRead, value);
    }

    private MethodBuilder? _stdinIsTTY;
    public MethodBuilder StdinIsTTY
    {
        get => Require(_stdinIsTTY);
        internal set => Set(ref _stdinIsTTY, value);
    }

    private MethodBuilder? _stdoutWrite;
    public MethodBuilder StdoutWrite
    {
        get => Require(_stdoutWrite);
        internal set => Set(ref _stdoutWrite, value);
    }

    private MethodBuilder? _stdoutIsTTY;
    public MethodBuilder StdoutIsTTY
    {
        get => Require(_stdoutIsTTY);
        internal set => Set(ref _stdoutIsTTY, value);
    }

    private MethodBuilder? _stderrWrite;
    public MethodBuilder StderrWrite
    {
        get => Require(_stderrWrite);
        internal set => Set(ref _stderrWrite, value);
    }

    private MethodBuilder? _stderrIsTTY;
    public MethodBuilder StderrIsTTY
    {
        get => Require(_stderrIsTTY);
        internal set => Set(ref _stderrIsTTY, value);
    }

    private MethodBuilder? _getInstance;
    public MethodBuilder GetInstance
    {
        get => Require(_getInstance);
        internal set => Set(ref _getInstance, value);
    }

    private FieldBuilder? _fieldsField;
    public FieldBuilder FieldsField
    {
        get => Require(_fieldsField);
        internal set => Set(ref _fieldsField, value);
    }

    private FieldBuilder? _throwDeprecationField;
    public FieldBuilder ThrowDeprecationField
    {
        get => Require(_throwDeprecationField);
        internal set => Set(ref _throwDeprecationField, value);
    }

    private FieldBuilder? _traceDeprecationField;
    public FieldBuilder TraceDeprecationField
    {
        get => Require(_traceDeprecationField);
        internal set => Set(ref _traceDeprecationField, value);
    }

    private FieldBuilder? _noDeprecationField;
    public FieldBuilder NoDeprecationField
    {
        get => Require(_noDeprecationField);
        internal set => Set(ref _noDeprecationField, value);
    }

    private FieldBuilder? _sourceMapsEnabledField;
    public FieldBuilder SourceMapsEnabledField
    {
        get => Require(_sourceMapsEnabledField);
        internal set => Set(ref _sourceMapsEnabledField, value);
    }

    private FieldBuilder? _umaskField;
    public FieldBuilder UmaskField
    {
        get => Require(_umaskField);
        internal set => Set(ref _umaskField, value);
    }

    private FieldBuilder? _titleField;
    public FieldBuilder TitleField
    {
        get => Require(_titleField);
        internal set => Set(ref _titleField, value);
    }

    private FieldBuilder? _hrtimeFnField;
    public FieldBuilder HrtimeFnField
    {
        get => Require(_hrtimeFnField);
        internal set => Set(ref _hrtimeFnField, value);
    }

    private FieldBuilder? _memoryUsageFnField;
    public FieldBuilder MemoryUsageFnField
    {
        get => Require(_memoryUsageFnField);
        internal set => Set(ref _memoryUsageFnField, value);
    }

    private FieldBuilder? _reportField;
    public FieldBuilder ReportField
    {
        get => Require(_reportField);
        internal set => Set(ref _reportField, value);
    }

    private FieldBuilder? _signalRegistrationsField;
    public FieldBuilder SignalRegistrationsField
    {
        get => Require(_signalRegistrationsField);
        internal set => Set(ref _signalRegistrationsField, value);
    }

    private ConstructorBuilder? _emitClosureCtor;
    public ConstructorBuilder EmitClosureCtor
    {
        get => Require(_emitClosureCtor);
        internal set => Set(ref _emitClosureCtor, value);
    }

    private MethodBuilder? _ntQueryInformationProcess;
    public MethodBuilder NtQueryInformationProcess
    {
        get => Require(_ntQueryInformationProcess);
        internal set => Set(ref _ntQueryInformationProcess, value);
    }

    private MethodBuilder? _posixGetPpid;
    public MethodBuilder PosixGetPpid
    {
        get => Require(_posixGetPpid);
        internal set => Set(ref _posixGetPpid, value);
    }

    private MethodBuilder? _posixGetUid;
    public MethodBuilder PosixGetUid
    {
        get => Require(_posixGetUid);
        internal set => Set(ref _posixGetUid, value);
    }

    private MethodBuilder? _posixGetEuid;
    public MethodBuilder PosixGetEuid
    {
        get => Require(_posixGetEuid);
        internal set => Set(ref _posixGetEuid, value);
    }

    private MethodBuilder? _posixGetGid;
    public MethodBuilder PosixGetGid
    {
        get => Require(_posixGetGid);
        internal set => Set(ref _posixGetGid, value);
    }

    private MethodBuilder? _posixGetEgid;
    public MethodBuilder PosixGetEgid
    {
        get => Require(_posixGetEgid);
        internal set => Set(ref _posixGetEgid, value);
    }

    private MethodBuilder? _posixGetGroups;
    public MethodBuilder PosixGetGroups
    {
        get => Require(_posixGetGroups);
        internal set => Set(ref _posixGetGroups, value);
    }

    private MethodBuilder? _posixSetUid;
    public MethodBuilder PosixSetUid
    {
        get => Require(_posixSetUid);
        internal set => Set(ref _posixSetUid, value);
    }

    private MethodBuilder? _posixSetGid;
    public MethodBuilder PosixSetGid
    {
        get => Require(_posixSetGid);
        internal set => Set(ref _posixSetGid, value);
    }

    public EmittedProcessStreamRuntime? Streams { get; private set; }

    public EmittedProcessStreamRuntime RequireStreams() => Streams
        ?? throw new InvalidOperationException("Process stream singletons were not enabled for this compilation.");

    internal void BeginStreamsEmission()
    {
        EnsureMutable();
        if (Streams is not null)
            throw new InvalidOperationException("Process stream singletons emission has already started.");
        Streams = new EmittedProcessStreamRuntime();
    }

    public EmittedHostedProcessRuntime? Hosted { get; private set; }

    public EmittedHostedProcessRuntime RequireHosted() => Hosted
        ?? throw new InvalidOperationException("Hosted process lifecycle were not enabled for this compilation.");

    internal void BeginHostedEmission()
    {
        EnsureMutable();
        if (Hosted is not null)
            throw new InvalidOperationException("Hosted process lifecycle emission has already started.");
        Hosted = new EmittedHostedProcessRuntime();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Process metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Process metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = GetEnv;
        _ = GetArgv;
        _ = Hrtime;
        _ = Uptime;
        _ = UptimeBaselineField;
        _ = MemoryUsage;
        _ = GetNextTick;
        _ = GetEventEmitter;
        _ = GetObject;
        _ = Exit;
        _ = Kill;
        _ = EmitWarning;
        _ = RunLifecycle;
        _ = RegisterSignal;
        _ = DispatchSignal;
        _ = EmitClosureInvoke;
        _ = GetPpid;
        _ = GetUid;
        _ = GetEuid;
        _ = GetGid;
        _ = GetEgid;
        _ = GetGroups;
        _ = SetUid;
        _ = SetGid;
        _ = GetTitle;
        _ = SetTitle;
        _ = GetVersions;
        _ = GetRelease;
        _ = GetFeatures;
        _ = GetConfig;
        _ = GetExecArgv;
        _ = GetAllowedFlags;
        _ = CpuUsage;
        _ = ResourceUsage;
        _ = AvailableMemory;
        _ = GetActiveResourcesInfo;
        _ = HrtimeBigint;
        _ = MemoryRss;
        _ = GetHrtimeFn;
        _ = GetMemoryUsageFn;
        _ = Umask;
        _ = GetReport;
        _ = StdinRead;
        _ = StdinIsTTY;
        _ = StdoutWrite;
        _ = StdoutIsTTY;
        _ = StderrWrite;
        _ = StderrIsTTY;
        _ = GetInstance;
        _ = FieldsField;
        _ = ThrowDeprecationField;
        _ = TraceDeprecationField;
        _ = NoDeprecationField;
        _ = SourceMapsEnabledField;
        _ = UmaskField;
        _ = TitleField;
        _ = HrtimeFnField;
        _ = MemoryUsageFnField;
        _ = ReportField;
        _ = SignalRegistrationsField;
        _ = EmitClosureCtor;
        _ = NtQueryInformationProcess;
        _ = PosixGetPpid;
        _ = PosixGetUid;
        _ = PosixGetEuid;
        _ = PosixGetGid;
        _ = PosixGetEgid;
        _ = PosixGetGroups;
        _ = PosixSetUid;
        _ = PosixSetGid;
        Streams?.ValidateDeclarations();
        Hosted?.ValidateDeclarations();
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        // Validate both optional groups before freezing either, so completion remains retryable.
        if (Streams is { IsComplete: false }) Streams.CompleteEmission();
        if (Hosted is { IsComplete: false }) Hosted.CompleteEmission();
        IsComplete = true;
    }
}
