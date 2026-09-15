using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional filesystem watcher types, closures, storage, methods, and factories for one compilation.
/// Declarations support forward calls; completion validates every handle and freezes writes.
/// </summary>
public sealed class EmittedFileSystemWatcherRuntime
{
    internal EmittedFileSystemWatcherRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _watcherType;
    public TypeBuilder WatcherType
    {
        get => Require(_watcherType);
        internal set => Set(ref _watcherType, value);
    }

    private ConstructorBuilder? _watcherCtor;
    public ConstructorBuilder WatcherCtor
    {
        get => Require(_watcherCtor);
        internal set => Set(ref _watcherCtor, value);
    }

    private FieldBuilder? _watcherStorageField;
    public FieldBuilder WatcherStorageField
    {
        get => Require(_watcherStorageField);
        internal set => Set(ref _watcherStorageField, value);
    }

    private FieldBuilder? _watcherClosedField;
    public FieldBuilder WatcherClosedField
    {
        get => Require(_watcherClosedField);
        internal set => Set(ref _watcherClosedField, value);
    }

    private MethodBuilder? _watcherClose;
    public MethodBuilder WatcherClose
    {
        get => Require(_watcherClose);
        internal set => Set(ref _watcherClose, value);
    }

    private MethodBuilder? _watcherOnFsEvent;
    public MethodBuilder WatcherOnFsEvent
    {
        get => Require(_watcherOnFsEvent);
        internal set => Set(ref _watcherOnFsEvent, value);
    }

    private TypeBuilder? _changeClosureType;
    public TypeBuilder ChangeClosureType
    {
        get => Require(_changeClosureType);
        internal set => Set(ref _changeClosureType, value);
    }

    private ConstructorBuilder? _changeClosureCtor;
    public ConstructorBuilder ChangeClosureCtor
    {
        get => Require(_changeClosureCtor);
        internal set => Set(ref _changeClosureCtor, value);
    }

    private FieldBuilder? _changeClosureWatcherField;
    public FieldBuilder ChangeClosureWatcherField
    {
        get => Require(_changeClosureWatcherField);
        internal set => Set(ref _changeClosureWatcherField, value);
    }

    private FieldBuilder? _changeClosureEventTypeField;
    public FieldBuilder ChangeClosureEventTypeField
    {
        get => Require(_changeClosureEventTypeField);
        internal set => Set(ref _changeClosureEventTypeField, value);
    }

    private FieldBuilder? _changeClosureFilenameField;
    public FieldBuilder ChangeClosureFilenameField
    {
        get => Require(_changeClosureFilenameField);
        internal set => Set(ref _changeClosureFilenameField, value);
    }

    private MethodBuilder? _changeClosureRun;
    public MethodBuilder ChangeClosureRun
    {
        get => Require(_changeClosureRun);
        internal set => Set(ref _changeClosureRun, value);
    }

    private TypeBuilder? _statType;
    public TypeBuilder StatType
    {
        get => Require(_statType);
        internal set => Set(ref _statType, value);
    }

    private ConstructorBuilder? _statCtor;
    public ConstructorBuilder StatCtor
    {
        get => Require(_statCtor);
        internal set => Set(ref _statCtor, value);
    }

    private FieldBuilder? _statTimerField;
    public FieldBuilder StatTimerField
    {
        get => Require(_statTimerField);
        internal set => Set(ref _statTimerField, value);
    }

    private FieldBuilder? _statClosedField;
    public FieldBuilder StatClosedField
    {
        get => Require(_statClosedField);
        internal set => Set(ref _statClosedField, value);
    }

    private FieldBuilder? _statFilenameField;
    public FieldBuilder StatFilenameField
    {
        get => Require(_statFilenameField);
        internal set => Set(ref _statFilenameField, value);
    }

    private FieldBuilder? _statLastSizeField;
    public FieldBuilder StatLastSizeField
    {
        get => Require(_statLastSizeField);
        internal set => Set(ref _statLastSizeField, value);
    }

    private FieldBuilder? _statLastModifiedField;
    public FieldBuilder StatLastModifiedField
    {
        get => Require(_statLastModifiedField);
        internal set => Set(ref _statLastModifiedField, value);
    }

    private MethodBuilder? _statClose;
    public MethodBuilder StatClose
    {
        get => Require(_statClose);
        internal set => Set(ref _statClose, value);
    }

    private MethodBuilder? _statPollCallback;
    public MethodBuilder StatPollCallback
    {
        get => Require(_statPollCallback);
        internal set => Set(ref _statPollCallback, value);
    }

    private TypeBuilder? _pollClosureType;
    public TypeBuilder PollClosureType
    {
        get => Require(_pollClosureType);
        internal set => Set(ref _pollClosureType, value);
    }

    private ConstructorBuilder? _pollClosureCtor;
    public ConstructorBuilder PollClosureCtor
    {
        get => Require(_pollClosureCtor);
        internal set => Set(ref _pollClosureCtor, value);
    }

    private FieldBuilder? _pollClosureWatcherField;
    public FieldBuilder PollClosureWatcherField
    {
        get => Require(_pollClosureWatcherField);
        internal set => Set(ref _pollClosureWatcherField, value);
    }

    private FieldBuilder? _pollClosureCurrentField;
    public FieldBuilder PollClosureCurrentField
    {
        get => Require(_pollClosureCurrentField);
        internal set => Set(ref _pollClosureCurrentField, value);
    }

    private FieldBuilder? _pollClosurePreviousField;
    public FieldBuilder PollClosurePreviousField
    {
        get => Require(_pollClosurePreviousField);
        internal set => Set(ref _pollClosurePreviousField, value);
    }

    private MethodBuilder? _pollClosureRun;
    public MethodBuilder PollClosureRun
    {
        get => Require(_pollClosureRun);
        internal set => Set(ref _pollClosureRun, value);
    }

    private FieldBuilder? _statRegistryField;
    public FieldBuilder StatRegistryField
    {
        get => Require(_statRegistryField);
        internal set => Set(ref _statRegistryField, value);
    }

    private MethodBuilder? _watch;
    public MethodBuilder Watch
    {
        get => Require(_watch);
        internal set => Set(ref _watch, value);
    }

    private MethodBuilder? _watchFile;
    public MethodBuilder WatchFile
    {
        get => Require(_watchFile);
        internal set => Set(ref _watchFile, value);
    }

    private MethodBuilder? _unwatchFile;
    public MethodBuilder UnwatchFile
    {
        get => Require(_unwatchFile);
        internal set => Set(ref _unwatchFile, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Filesystem watcher metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Filesystem watcher metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = WatcherType;
        _ = WatcherCtor;
        _ = WatcherStorageField;
        _ = WatcherClosedField;
        _ = WatcherClose;
        _ = WatcherOnFsEvent;
        _ = ChangeClosureType;
        _ = ChangeClosureCtor;
        _ = ChangeClosureWatcherField;
        _ = ChangeClosureEventTypeField;
        _ = ChangeClosureFilenameField;
        _ = ChangeClosureRun;
        _ = StatType;
        _ = StatCtor;
        _ = StatTimerField;
        _ = StatClosedField;
        _ = StatFilenameField;
        _ = StatLastSizeField;
        _ = StatLastModifiedField;
        _ = StatClose;
        _ = StatPollCallback;
        _ = PollClosureType;
        _ = PollClosureCtor;
        _ = PollClosureWatcherField;
        _ = PollClosureCurrentField;
        _ = PollClosurePreviousField;
        _ = PollClosureRun;
        _ = StatRegistryField;
        _ = Watch;
        _ = WatchFile;
        _ = UnwatchFile;
        IsComplete = true;
    }
}
