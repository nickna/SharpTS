using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required console declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedConsoleRuntime
{
    internal EmittedConsoleRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _log;
    public MethodBuilder Log
    {
        get => Require(_log);
        internal set => Set(ref _log, value);
    }

    private MethodBuilder? _logMultiple;
    public MethodBuilder LogMultiple
    {
        get => Require(_logMultiple);
        internal set => Set(ref _logMultiple, value);
    }

    private MethodBuilder? _joinWithStringify;
    public MethodBuilder JoinWithStringify
    {
        get => Require(_joinWithStringify);
        internal set => Set(ref _joinWithStringify, value);
    }

    private MethodBuilder? _error;
    public MethodBuilder Error
    {
        get => Require(_error);
        internal set => Set(ref _error, value);
    }

    private MethodBuilder? _errorMultiple;
    public MethodBuilder ErrorMultiple
    {
        get => Require(_errorMultiple);
        internal set => Set(ref _errorMultiple, value);
    }

    private MethodBuilder? _warn;
    public MethodBuilder Warn
    {
        get => Require(_warn);
        internal set => Set(ref _warn, value);
    }

    private MethodBuilder? _warnMultiple;
    public MethodBuilder WarnMultiple
    {
        get => Require(_warnMultiple);
        internal set => Set(ref _warnMultiple, value);
    }

    private MethodBuilder? _clear;
    public MethodBuilder Clear
    {
        get => Require(_clear);
        internal set => Set(ref _clear, value);
    }

    private MethodBuilder? _time;
    public MethodBuilder Time
    {
        get => Require(_time);
        internal set => Set(ref _time, value);
    }

    private MethodBuilder? _timeEnd;
    public MethodBuilder TimeEnd
    {
        get => Require(_timeEnd);
        internal set => Set(ref _timeEnd, value);
    }

    private MethodBuilder? _timeLog;
    public MethodBuilder TimeLog
    {
        get => Require(_timeLog);
        internal set => Set(ref _timeLog, value);
    }

    private MethodBuilder? _assert;
    public MethodBuilder Assert
    {
        get => Require(_assert);
        internal set => Set(ref _assert, value);
    }

    private MethodBuilder? _assertMultiple;
    public MethodBuilder AssertMultiple
    {
        get => Require(_assertMultiple);
        internal set => Set(ref _assertMultiple, value);
    }

    private MethodBuilder? _count;
    public MethodBuilder Count
    {
        get => Require(_count);
        internal set => Set(ref _count, value);
    }

    private MethodBuilder? _countReset;
    public MethodBuilder CountReset
    {
        get => Require(_countReset);
        internal set => Set(ref _countReset, value);
    }

    private MethodBuilder? _table;
    public MethodBuilder Table
    {
        get => Require(_table);
        internal set => Set(ref _table, value);
    }

    private MethodBuilder? _dir;
    public MethodBuilder Dir
    {
        get => Require(_dir);
        internal set => Set(ref _dir, value);
    }

    private MethodBuilder? _group;
    public MethodBuilder Group
    {
        get => Require(_group);
        internal set => Set(ref _group, value);
    }

    private MethodBuilder? _groupMultiple;
    public MethodBuilder GroupMultiple
    {
        get => Require(_groupMultiple);
        internal set => Set(ref _groupMultiple, value);
    }

    private MethodBuilder? _groupEnd;
    public MethodBuilder GroupEnd
    {
        get => Require(_groupEnd);
        internal set => Set(ref _groupEnd, value);
    }

    private MethodBuilder? _trace;
    public MethodBuilder Trace
    {
        get => Require(_trace);
        internal set => Set(ref _trace, value);
    }

    private MethodBuilder? _traceMultiple;
    public MethodBuilder TraceMultiple
    {
        get => Require(_traceMultiple);
        internal set => Set(ref _traceMultiple, value);
    }

    private FieldBuilder? _groupLevelField;
    public FieldBuilder GroupLevelField
    {
        get => Require(_groupLevelField);
        internal set => Set(ref _groupLevelField, value);
    }

    private MethodBuilder? _hasFormatSpecifiers;
    public MethodBuilder HasFormatSpecifiers
    {
        get => Require(_hasFormatSpecifiers);
        internal set => Set(ref _hasFormatSpecifiers, value);
    }

    private MethodBuilder? _formatArgs;
    public MethodBuilder FormatArgs
    {
        get => Require(_formatArgs);
        internal set => Set(ref _formatArgs, value);
    }

    private MethodBuilder? _getIndent;
    public MethodBuilder GetIndent
    {
        get => Require(_getIndent);
        internal set => Set(ref _getIndent, value);
    }

    private MethodBuilder? _formatSingleArg;
    public MethodBuilder FormatSingleArg
    {
        get => Require(_formatSingleArg);
        internal set => Set(ref _formatSingleArg, value);
    }

    private MethodBuilder? _formatAsInteger;
    public MethodBuilder FormatAsInteger
    {
        get => Require(_formatAsInteger);
        internal set => Set(ref _formatAsInteger, value);
    }

    private MethodBuilder? _formatAsFloat;
    public MethodBuilder FormatAsFloat
    {
        get => Require(_formatAsFloat);
        internal set => Set(ref _formatAsFloat, value);
    }

    private MethodBuilder? _formatAsJson;
    public MethodBuilder FormatAsJson
    {
        get => Require(_formatAsJson);
        internal set => Set(ref _formatAsJson, value);
    }

    private FieldBuilder? _timersField;
    public FieldBuilder TimersField
    {
        get => Require(_timersField);
        internal set => Set(ref _timersField, value);
    }

    private FieldBuilder? _countsField;
    public FieldBuilder CountsField
    {
        get => Require(_countsField);
        internal set => Set(ref _countsField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Console metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Console metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Log;
        _ = LogMultiple;
        _ = JoinWithStringify;
        _ = Error;
        _ = ErrorMultiple;
        _ = Warn;
        _ = WarnMultiple;
        _ = Clear;
        _ = Time;
        _ = TimeEnd;
        _ = TimeLog;
        _ = Assert;
        _ = AssertMultiple;
        _ = Count;
        _ = CountReset;
        _ = Table;
        _ = Dir;
        _ = Group;
        _ = GroupMultiple;
        _ = GroupEnd;
        _ = Trace;
        _ = TraceMultiple;
        _ = GroupLevelField;
        _ = HasFormatSpecifiers;
        _ = FormatArgs;
        _ = GetIndent;
        _ = FormatSingleArg;
        _ = FormatAsInteger;
        _ = FormatAsFloat;
        _ = FormatAsJson;
        _ = TimersField;
        _ = CountsField;
        IsComplete = true;
    }
}
