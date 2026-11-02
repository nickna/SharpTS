using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional VM declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedVmRuntime
{
    internal EmittedVmRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _runInNewContext;
    public MethodBuilder RunInNewContext
    {
        get => Require(_runInNewContext);
        internal set => Set(ref _runInNewContext, value);
    }

    private MethodBuilder? _runInThisContext;
    public MethodBuilder RunInThisContext
    {
        get => Require(_runInThisContext);
        internal set => Set(ref _runInThisContext, value);
    }

    private MethodBuilder? _runInContext;
    public MethodBuilder RunInContext
    {
        get => Require(_runInContext);
        internal set => Set(ref _runInContext, value);
    }

    private MethodBuilder? _createContext;
    public MethodBuilder CreateContext
    {
        get => Require(_createContext);
        internal set => Set(ref _createContext, value);
    }

    private MethodBuilder? _isContext;
    public MethodBuilder IsContext
    {
        get => Require(_isContext);
        internal set => Set(ref _isContext, value);
    }

    private MethodBuilder? _getScriptConstructor;
    public MethodBuilder GetScriptConstructor
    {
        get => Require(_getScriptConstructor);
        internal set => Set(ref _getScriptConstructor, value);
    }

    private MethodBuilder? _newScript;
    public MethodBuilder NewScript
    {
        get => Require(_newScript);
        internal set => Set(ref _newScript, value);
    }

    private MethodBuilder? _compileFunction;
    public MethodBuilder CompileFunction
    {
        get => Require(_compileFunction);
        internal set => Set(ref _compileFunction, value);
    }

    private MethodBuilder? _getConstants;
    public MethodBuilder GetConstants
    {
        get => Require(_getConstants);
        internal set => Set(ref _getConstants, value);
    }

    private MethodBuilder? _measureMemory;
    public MethodBuilder MeasureMemory
    {
        get => Require(_measureMemory);
        internal set => Set(ref _measureMemory, value);
    }

    private MethodBuilder? _newSourceTextModule;
    public MethodBuilder NewSourceTextModule
    {
        get => Require(_newSourceTextModule);
        internal set => Set(ref _newSourceTextModule, value);
    }

    private MethodBuilder? _newSyntheticModule;
    public MethodBuilder NewSyntheticModule
    {
        get => Require(_newSyntheticModule);
        internal set => Set(ref _newSyntheticModule, value);
    }

    private MethodBuilder? _normalizeValue;
    public MethodBuilder NormalizeValue
    {
        get => Require(_normalizeValue);
        internal set => Set(ref _normalizeValue, value);
    }

    private MethodBuilder? _restoreValue;
    public MethodBuilder RestoreValue
    {
        get => Require(_restoreValue);
        internal set => Set(ref _restoreValue, value);
    }

    private MethodBuilder? _isPromiseRejectionHandled;
    internal MethodBuilder IsPromiseRejectionHandled
    {
        get => Require(_isPromiseRejectionHandled);
        set => Set(ref _isPromiseRejectionHandled, value);
    }

    private MethodBuilder? _markPromiseRejectionHandled;
    internal MethodBuilder MarkPromiseRejectionHandled
    {
        get => Require(_markPromiseRejectionHandled);
        set => Set(ref _markPromiseRejectionHandled, value);
    }

    private MethodBuilder? _isErrorValue;
    public MethodBuilder IsErrorValue
    {
        get => Require(_isErrorValue);
        internal set => Set(ref _isErrorValue, value);
    }

    private MethodBuilder? _readErrorProperty;
    public MethodBuilder ReadErrorProperty
    {
        get => Require(_readErrorProperty);
        internal set => Set(ref _readErrorProperty, value);
    }

    private MethodBuilder? _getExecutionHost;
    public MethodBuilder GetExecutionHost
    {
        get => Require(_getExecutionHost);
        internal set => Set(ref _getExecutionHost, value);
    }

    private MethodBuilder? _disposeExecutionHost;
    public MethodBuilder DisposeExecutionHost
    {
        get => Require(_disposeExecutionHost);
        internal set => Set(ref _disposeExecutionHost, value);
    }

    private MethodBuilder? _queueAfterCheckpoint;
    public MethodBuilder QueueAfterCheckpoint
    {
        get => Require(_queueAfterCheckpoint);
        internal set => Set(ref _queueAfterCheckpoint, value);
    }

    private MethodBuilder? _enterExecutionScope;
    internal MethodBuilder EnterExecutionScope
    {
        get => Require(_enterExecutionScope);
        set => Set(ref _enterExecutionScope, value);
    }

    private MethodBuilder? _convertException;
    internal MethodBuilder ConvertException
    {
        get => Require(_convertException);
        set => Set(ref _convertException, value);
    }

    private MethodBuilder? _isRelatedValue;
    internal MethodBuilder IsRelatedValue
    {
        get => Require(_isRelatedValue);
        set => Set(ref _isRelatedValue, value);
    }
    private MethodBuilder? _isMappedValue;
    internal MethodBuilder IsMappedValue
    {
        get => Require(_isMappedValue);
        set => Set(ref _isMappedValue, value);
    }

    private MethodBuilder? _readMappedProperty;
    internal MethodBuilder ReadMappedProperty
    {
        get => Require(_readMappedProperty);
        set => Set(ref _readMappedProperty, value);
    }

    private MethodBuilder? _mappedPrototype;
    internal MethodBuilder MappedPrototype
    {
        get => Require(_mappedPrototype);
        set => Set(ref _mappedPrototype, value);
    }

    private MethodBuilder? _toStringValue;
    internal MethodBuilder ToStringValue
    {
        get => Require(_toStringValue);
        set => Set(ref _toStringValue, value);
    }

    private MethodBuilder? _recordSourceObject;
    internal MethodBuilder RecordSourceObject
    {
        get => Require(_recordSourceObject);
        set => Set(ref _recordSourceObject, value);
    }

    private FieldBuilder? _afterCheckpointQueue;
    internal FieldBuilder AfterCheckpointQueue
    {
        get => Require(_afterCheckpointQueue);
        set => Set(ref _afterCheckpointQueue, value);
    }
    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"VM metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("VM metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ToStringValue;
        _ = RecordSourceObject;
        _ = RunInNewContext;
        _ = RunInThisContext;
        _ = RunInContext;
        _ = CreateContext;
        _ = IsContext;
        _ = GetScriptConstructor;
        _ = NewScript;
        _ = CompileFunction;
        _ = GetConstants;
        _ = MeasureMemory;
        _ = NewSourceTextModule;
        _ = NewSyntheticModule;
        _ = NormalizeValue;
        _ = RestoreValue;
        _ = IsPromiseRejectionHandled;
        _ = MarkPromiseRejectionHandled;
        _ = IsErrorValue;
        _ = ReadErrorProperty;
        _ = GetExecutionHost;
        _ = DisposeExecutionHost;
        _ = QueueAfterCheckpoint;
        _ = AfterCheckpointQueue;
        _ = EnterExecutionScope;
        _ = ConvertException;
        _ = IsMappedValue;
        _ = IsRelatedValue;
        _ = ReadMappedProperty;
        _ = MappedPrototype;
        IsComplete = true;
    }
}
