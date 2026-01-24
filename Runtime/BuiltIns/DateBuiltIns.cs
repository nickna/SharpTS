using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Date object members.
/// Includes static methods (Date.now()) and instance methods (date.getFullYear(), date.setMonth()).
/// </summary>
public static class DateBuiltIns
{
    // Static method lookup for Date namespace
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .Method("now", 0, (_, _) => SharpTSDate.Now())
            .Build();

    // Instance method lookup for Date instances
    private static readonly BuiltInTypeMemberLookup<SharpTSDate> _instanceLookup =
        BuiltInTypeBuilder<SharpTSDate>.ForInstanceType()
            // Getter Methods
            .Method("getTime", 0, GetTime)
            .Method("getFullYear", 0, GetFullYear)
            .Method("getMonth", 0, GetMonth)
            .Method("getDate", 0, GetDate)
            .Method("getDay", 0, GetDay)
            .Method("getHours", 0, GetHours)
            .Method("getMinutes", 0, GetMinutes)
            .Method("getSeconds", 0, GetSeconds)
            .Method("getMilliseconds", 0, GetMilliseconds)
            .Method("getTimezoneOffset", 0, GetTimezoneOffset)
            // Setter Methods
            .Method("setTime", 1, SetTime)
            .Method("setFullYear", 1, 3, SetFullYear)
            .Method("setMonth", 1, 2, SetMonth)
            .Method("setDate", 1, SetDate)
            .Method("setHours", 1, 4, SetHours)
            .Method("setMinutes", 1, 3, SetMinutes)
            .Method("setSeconds", 1, 2, SetSeconds)
            .Method("setMilliseconds", 1, SetMilliseconds)
            // Conversion Methods
            .Method("toString", 0, ToString)
            .Method("toISOString", 0, ToISOString)
            .Method("toDateString", 0, ToDateString)
            .Method("toTimeString", 0, ToTimeString)
            .Method("valueOf", 0, ValueOf)
            .Build();

    /// <summary>
    /// Gets a static member (method) from the Date namespace.
    /// </summary>
    public static BuiltInMethod? GetStaticMethod(string name)
        => _staticLookup.GetMember(name) as BuiltInMethod;

    /// <summary>
    /// Gets an instance member (method) for a Date object.
    /// </summary>
    public static object? GetMember(SharpTSDate receiver, string name)
        => _instanceLookup.GetMember(receiver, name);

    // Getter method implementations
    private static object? GetTime(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetTime();

    private static object? GetFullYear(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetFullYear();

    private static object? GetMonth(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetMonth();

    private static object? GetDate(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetDate();

    private static object? GetDay(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetDay();

    private static object? GetHours(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetHours();

    private static object? GetMinutes(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetMinutes();

    private static object? GetSeconds(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetSeconds();

    private static object? GetMilliseconds(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetMilliseconds();

    private static object? GetTimezoneOffset(Interpreter _, SharpTSDate date, List<object?> args)
        => date.GetTimezoneOffset();

    // Setter method implementations
    private static object? SetTime(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetTime((double)args[0]!);

    private static object? SetFullYear(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetFullYear(
            (double)args[0]!,
            args.Count > 1 && args[1] != null ? (double?)args[1] : null,
            args.Count > 2 && args[2] != null ? (double?)args[2] : null);

    private static object? SetMonth(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetMonth(
            (double)args[0]!,
            args.Count > 1 && args[1] != null ? (double?)args[1] : null);

    private static object? SetDate(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetDate((double)args[0]!);

    private static object? SetHours(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetHours(
            (double)args[0]!,
            args.Count > 1 && args[1] != null ? (double?)args[1] : null,
            args.Count > 2 && args[2] != null ? (double?)args[2] : null,
            args.Count > 3 && args[3] != null ? (double?)args[3] : null);

    private static object? SetMinutes(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetMinutes(
            (double)args[0]!,
            args.Count > 1 && args[1] != null ? (double?)args[1] : null,
            args.Count > 2 && args[2] != null ? (double?)args[2] : null);

    private static object? SetSeconds(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetSeconds(
            (double)args[0]!,
            args.Count > 1 && args[1] != null ? (double?)args[1] : null);

    private static object? SetMilliseconds(Interpreter _, SharpTSDate date, List<object?> args)
        => date.SetMilliseconds((double)args[0]!);

    // Conversion method implementations
    private static object? ToString(Interpreter _, SharpTSDate date, List<object?> args)
        => date.ToString();

    private static object? ToISOString(Interpreter _, SharpTSDate date, List<object?> args)
        => date.ToISOString();

    private static object? ToDateString(Interpreter _, SharpTSDate date, List<object?> args)
        => date.ToDateString();

    private static object? ToTimeString(Interpreter _, SharpTSDate date, List<object?> args)
        => date.ToTimeString();

    private static object? ValueOf(Interpreter _, SharpTSDate date, List<object?> args)
        => date.ValueOf();
}
