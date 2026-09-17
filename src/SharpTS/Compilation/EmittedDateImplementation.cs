using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Selected Date type, operations and instance-method declarations for one compilation.</summary>
public sealed class EmittedDateImplementation
{
    private static readonly string[] RequiredMethodNames =
    [
        "GetTime",
        "GetFullYear",
        "GetMonth",
        "GetDate",
        "GetDay",
        "GetHours",
        "GetMinutes",
        "GetSeconds",
        "GetMilliseconds",
        "GetTimezoneOffset",
        "GetUTCFullYear",
        "GetUTCMonth",
        "GetUTCDate",
        "GetUTCDay",
        "GetUTCHours",
        "GetUTCMinutes",
        "GetUTCSeconds",
        "GetUTCMilliseconds",
        "GetYear",
        "SetTime",
        "SetFullYear",
        "SetMonth",
        "SetDate",
        "SetHours",
        "SetMinutes",
        "SetSeconds",
        "SetMilliseconds",
        "SetUTCFullYear",
        "SetUTCMonth",
        "SetUTCDate",
        "SetUTCHours",
        "SetUTCMinutes",
        "SetUTCSeconds",
        "SetUTCMilliseconds",
        "SetYear",
        "ToString",
        "ToISOString",
        "ToDateString",
        "ToTimeString",
        "ToUTCString",
        "ToLocaleDateString",
        "ToLocaleTimeString",
        "ToLocaleString",
        "ValueOf",
    ];
    private readonly Dictionary<string, MethodBuilder> _methods = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, MethodBuilder> _methodView;

    internal EmittedDateImplementation() => _methodView = new(_methods);
    public bool IsComplete { get; private set; }
    public IReadOnlyDictionary<string, MethodBuilder> InstanceMethods => _methodView;

    private MethodBuilder? _createFromComponents;
    public MethodBuilder CreateFromComponents
    {
        get => Require(_createFromComponents);
        internal set => SetHandle(ref _createFromComponents, value);
    }

    private MethodBuilder? _createFromValue;
    public MethodBuilder CreateFromValue
    {
        get => Require(_createFromValue);
        internal set => SetHandle(ref _createFromValue, value);
    }

    private MethodBuilder? _createNoArgs;
    public MethodBuilder CreateNoArgs
    {
        get => Require(_createNoArgs);
        internal set => SetHandle(ref _createNoArgs, value);
    }

    private MethodBuilder? _getDate;
    public MethodBuilder GetDate
    {
        get => Require(_getDate);
        internal set => SetHandle(ref _getDate, value);
    }

    private MethodBuilder? _getDay;
    public MethodBuilder GetDay
    {
        get => Require(_getDay);
        internal set => SetHandle(ref _getDay, value);
    }

    private MethodBuilder? _getFullYear;
    public MethodBuilder GetFullYear
    {
        get => Require(_getFullYear);
        internal set => SetHandle(ref _getFullYear, value);
    }

    private MethodBuilder? _getHours;
    public MethodBuilder GetHours
    {
        get => Require(_getHours);
        internal set => SetHandle(ref _getHours, value);
    }

    private MethodBuilder? _getMilliseconds;
    public MethodBuilder GetMilliseconds
    {
        get => Require(_getMilliseconds);
        internal set => SetHandle(ref _getMilliseconds, value);
    }

    private MethodBuilder? _getMinutes;
    public MethodBuilder GetMinutes
    {
        get => Require(_getMinutes);
        internal set => SetHandle(ref _getMinutes, value);
    }

    private MethodBuilder? _getMonth;
    public MethodBuilder GetMonth
    {
        get => Require(_getMonth);
        internal set => SetHandle(ref _getMonth, value);
    }

    private MethodBuilder? _getSeconds;
    public MethodBuilder GetSeconds
    {
        get => Require(_getSeconds);
        internal set => SetHandle(ref _getSeconds, value);
    }

    private MethodBuilder? _getTime;
    public MethodBuilder GetTime
    {
        get => Require(_getTime);
        internal set => SetHandle(ref _getTime, value);
    }

    private MethodBuilder? _getTimezoneOffset;
    public MethodBuilder GetTimezoneOffset
    {
        get => Require(_getTimezoneOffset);
        internal set => SetHandle(ref _getTimezoneOffset, value);
    }

    private MethodBuilder? _getUTCDate;
    public MethodBuilder GetUTCDate
    {
        get => Require(_getUTCDate);
        internal set => SetHandle(ref _getUTCDate, value);
    }

    private MethodBuilder? _getUTCDay;
    public MethodBuilder GetUTCDay
    {
        get => Require(_getUTCDay);
        internal set => SetHandle(ref _getUTCDay, value);
    }

    private MethodBuilder? _getUTCFullYear;
    public MethodBuilder GetUTCFullYear
    {
        get => Require(_getUTCFullYear);
        internal set => SetHandle(ref _getUTCFullYear, value);
    }

    private MethodBuilder? _getUTCHours;
    public MethodBuilder GetUTCHours
    {
        get => Require(_getUTCHours);
        internal set => SetHandle(ref _getUTCHours, value);
    }

    private MethodBuilder? _getUTCMilliseconds;
    public MethodBuilder GetUTCMilliseconds
    {
        get => Require(_getUTCMilliseconds);
        internal set => SetHandle(ref _getUTCMilliseconds, value);
    }

    private MethodBuilder? _getUTCMinutes;
    public MethodBuilder GetUTCMinutes
    {
        get => Require(_getUTCMinutes);
        internal set => SetHandle(ref _getUTCMinutes, value);
    }

    private MethodBuilder? _getUTCMonth;
    public MethodBuilder GetUTCMonth
    {
        get => Require(_getUTCMonth);
        internal set => SetHandle(ref _getUTCMonth, value);
    }

    private MethodBuilder? _getUTCSeconds;
    public MethodBuilder GetUTCSeconds
    {
        get => Require(_getUTCSeconds);
        internal set => SetHandle(ref _getUTCSeconds, value);
    }

    private MethodBuilder? _getYear;
    public MethodBuilder GetYear
    {
        get => Require(_getYear);
        internal set => SetHandle(ref _getYear, value);
    }

    private MethodBuilder? _now;
    public MethodBuilder Now
    {
        get => Require(_now);
        internal set => SetHandle(ref _now, value);
    }

    private MethodBuilder? _setDate;
    public MethodBuilder SetDate
    {
        get => Require(_setDate);
        internal set => SetHandle(ref _setDate, value);
    }

    private MethodBuilder? _setFullYear;
    public MethodBuilder SetFullYear
    {
        get => Require(_setFullYear);
        internal set => SetHandle(ref _setFullYear, value);
    }

    private MethodBuilder? _setHours;
    public MethodBuilder SetHours
    {
        get => Require(_setHours);
        internal set => SetHandle(ref _setHours, value);
    }

    private MethodBuilder? _setMilliseconds;
    public MethodBuilder SetMilliseconds
    {
        get => Require(_setMilliseconds);
        internal set => SetHandle(ref _setMilliseconds, value);
    }

    private MethodBuilder? _setMinutes;
    public MethodBuilder SetMinutes
    {
        get => Require(_setMinutes);
        internal set => SetHandle(ref _setMinutes, value);
    }

    private MethodBuilder? _setMonth;
    public MethodBuilder SetMonth
    {
        get => Require(_setMonth);
        internal set => SetHandle(ref _setMonth, value);
    }

    private MethodBuilder? _setSeconds;
    public MethodBuilder SetSeconds
    {
        get => Require(_setSeconds);
        internal set => SetHandle(ref _setSeconds, value);
    }

    private MethodBuilder? _setTime;
    public MethodBuilder SetTime
    {
        get => Require(_setTime);
        internal set => SetHandle(ref _setTime, value);
    }

    private MethodBuilder? _setUTCDate;
    public MethodBuilder SetUTCDate
    {
        get => Require(_setUTCDate);
        internal set => SetHandle(ref _setUTCDate, value);
    }

    private MethodBuilder? _setUTCFullYear;
    public MethodBuilder SetUTCFullYear
    {
        get => Require(_setUTCFullYear);
        internal set => SetHandle(ref _setUTCFullYear, value);
    }

    private MethodBuilder? _setUTCHours;
    public MethodBuilder SetUTCHours
    {
        get => Require(_setUTCHours);
        internal set => SetHandle(ref _setUTCHours, value);
    }

    private MethodBuilder? _setUTCMilliseconds;
    public MethodBuilder SetUTCMilliseconds
    {
        get => Require(_setUTCMilliseconds);
        internal set => SetHandle(ref _setUTCMilliseconds, value);
    }

    private MethodBuilder? _setUTCMinutes;
    public MethodBuilder SetUTCMinutes
    {
        get => Require(_setUTCMinutes);
        internal set => SetHandle(ref _setUTCMinutes, value);
    }

    private MethodBuilder? _setUTCMonth;
    public MethodBuilder SetUTCMonth
    {
        get => Require(_setUTCMonth);
        internal set => SetHandle(ref _setUTCMonth, value);
    }

    private MethodBuilder? _setUTCSeconds;
    public MethodBuilder SetUTCSeconds
    {
        get => Require(_setUTCSeconds);
        internal set => SetHandle(ref _setUTCSeconds, value);
    }

    private MethodBuilder? _setYear;
    public MethodBuilder SetYear
    {
        get => Require(_setYear);
        internal set => SetHandle(ref _setYear, value);
    }

    private MethodBuilder? _toDateString;
    public MethodBuilder ToDateString
    {
        get => Require(_toDateString);
        internal set => SetHandle(ref _toDateString, value);
    }

    private MethodBuilder? _toISOString;
    public MethodBuilder ToISOString
    {
        get => Require(_toISOString);
        internal set => SetHandle(ref _toISOString, value);
    }

    private MethodBuilder? _toJSON;
    public MethodBuilder ToJSON
    {
        get => Require(_toJSON);
        internal set => SetHandle(ref _toJSON, value);
    }

    private MethodBuilder? _toLocaleDateString;
    public MethodBuilder ToLocaleDateString
    {
        get => Require(_toLocaleDateString);
        internal set => SetHandle(ref _toLocaleDateString, value);
    }

    private MethodBuilder? _toLocaleString;
    public MethodBuilder ToLocaleString
    {
        get => Require(_toLocaleString);
        internal set => SetHandle(ref _toLocaleString, value);
    }

    private MethodBuilder? _toLocaleTimeString;
    public MethodBuilder ToLocaleTimeString
    {
        get => Require(_toLocaleTimeString);
        internal set => SetHandle(ref _toLocaleTimeString, value);
    }

    private MethodBuilder? _toLocaleWithOptions;
    /// <summary>
    /// Locale/options wrapper declared whenever Date is selected. Its optional SharpTS runtime
    /// dependency is recorded only by generated call sites that actually pass locale/options.
    /// </summary>
    public MethodBuilder ToLocaleWithOptions
    {
        get => Require(_toLocaleWithOptions);
        internal set => SetHandle(ref _toLocaleWithOptions, value);
    }

    private MethodBuilder? _toStringMethod;
    public MethodBuilder ToStringMethod
    {
        get => Require(_toStringMethod);
        internal set => SetHandle(ref _toStringMethod, value);
    }

    private MethodBuilder? _toTimeString;
    public MethodBuilder ToTimeString
    {
        get => Require(_toTimeString);
        internal set => SetHandle(ref _toTimeString, value);
    }

    private MethodBuilder? _toUTCString;
    public MethodBuilder ToUTCString
    {
        get => Require(_toUTCString);
        internal set => SetHandle(ref _toUTCString, value);
    }

    private MethodBuilder? _valueOf;
    public MethodBuilder ValueOf
    {
        get => Require(_valueOf);
        internal set => SetHandle(ref _valueOf, value);
    }

    private ConstructorBuilder? _componentsConstructor;
    public ConstructorBuilder ComponentsConstructor
    {
        get => Require(_componentsConstructor);
        internal set => SetHandle(ref _componentsConstructor, value);
    }

    private ConstructorBuilder? _millisecondsConstructor;
    public ConstructorBuilder MillisecondsConstructor
    {
        get => Require(_millisecondsConstructor);
        internal set => SetHandle(ref _millisecondsConstructor, value);
    }

    private ConstructorBuilder? _noArgsConstructor;
    public ConstructorBuilder NoArgsConstructor
    {
        get => Require(_noArgsConstructor);
        internal set => SetHandle(ref _noArgsConstructor, value);
    }

    private ConstructorBuilder? _stringConstructor;
    public ConstructorBuilder StringConstructor
    {
        get => Require(_stringConstructor);
        internal set => SetHandle(ref _stringConstructor, value);
    }

    private MethodBuilder? _staticNow;
    public MethodBuilder StaticNow
    {
        get => Require(_staticNow);
        internal set => SetHandle(ref _staticNow, value);
    }

    private MethodBuilder? _staticParse;
    public MethodBuilder StaticParse
    {
        get => Require(_staticParse);
        internal set => SetHandle(ref _staticParse, value);
    }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private MethodBuilder? _staticUTC;
    public MethodBuilder StaticUTC
    {
        get => Require(_staticUTC);
        internal set => SetHandle(ref _staticUTC, value);
    }

    public MethodBuilder GetInstanceMethod(string name) => _methods.TryGetValue(name, out var method)
        ? method
        : throw new InvalidOperationException("Date instance method '" + name + "' has not been declared.");

    internal void DeclareInstanceMethod(string name, MethodBuilder method)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(method);
        if (!RequiredMethodNames.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException("Unknown Date instance method: " + name, nameof(name));
        if (!_methods.TryAdd(name, method))
            throw new InvalidOperationException("Date instance method '" + name + "' has already been declared.");
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Date implementation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Date implementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateFromComponents;
        _ = CreateFromValue;
        _ = CreateNoArgs;
        _ = GetDate;
        _ = GetDay;
        _ = GetFullYear;
        _ = GetHours;
        _ = GetMilliseconds;
        _ = GetMinutes;
        _ = GetMonth;
        _ = GetSeconds;
        _ = GetTime;
        _ = GetTimezoneOffset;
        _ = GetUTCDate;
        _ = GetUTCDay;
        _ = GetUTCFullYear;
        _ = GetUTCHours;
        _ = GetUTCMilliseconds;
        _ = GetUTCMinutes;
        _ = GetUTCMonth;
        _ = GetUTCSeconds;
        _ = GetYear;
        _ = Now;
        _ = SetDate;
        _ = SetFullYear;
        _ = SetHours;
        _ = SetMilliseconds;
        _ = SetMinutes;
        _ = SetMonth;
        _ = SetSeconds;
        _ = SetTime;
        _ = SetUTCDate;
        _ = SetUTCFullYear;
        _ = SetUTCHours;
        _ = SetUTCMilliseconds;
        _ = SetUTCMinutes;
        _ = SetUTCMonth;
        _ = SetUTCSeconds;
        _ = SetYear;
        _ = ToDateString;
        _ = ToISOString;
        _ = ToJSON;
        _ = ToLocaleDateString;
        _ = ToLocaleString;
        _ = ToLocaleTimeString;
        _ = ToLocaleWithOptions;
        _ = ToStringMethod;
        _ = ToTimeString;
        _ = ToUTCString;
        _ = ValueOf;
        _ = ComponentsConstructor;
        _ = MillisecondsConstructor;
        _ = NoArgsConstructor;
        _ = StringConstructor;
        _ = StaticNow;
        _ = StaticParse;
        _ = Type;
        _ = StaticUTC;
        foreach (string name in RequiredMethodNames) _ = GetInstanceMethod(name);
        IsComplete = true;
    }
}
