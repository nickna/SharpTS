using SharpTS.Execution;
using SharpTS.Runtime;
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
            .MethodV2("now", 0, (_, _, _) => RuntimeValue.FromNumber(SharpTSDate.Now()))
            // Date.UTC(year, month?, date?, hours?, minutes?, seconds?, ms?) — UTC timestamp (#538)
            .MethodV2("UTC", 1, 7, (_, _, args) => RuntimeValue.FromNumber(SharpTSDate.UTC(
                Interpreter.ToNumber(args[0]),
                args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null,
                args.Length > 3 && args[3].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[3]) : null,
                args.Length > 4 && args[4].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[4]) : null,
                args.Length > 5 && args[5].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[5]) : null,
                args.Length > 6 && args[6].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[6]) : null)))
            // Date.parse(s) — timestamp from a date string, or NaN if unparseable (#538)
            .MethodV2("parse", 1, (_, _, args) => RuntimeValue.FromNumber(SharpTSDate.Parse(args[0].AsString())))
            .Build();

    // Instance method lookup for Date instances
    private static readonly BuiltInTypeMemberLookup<SharpTSDate> _instanceLookup =
        BuiltInTypeBuilder<SharpTSDate>.ForInstanceType()
            // Getter Methods
            .MethodV2("getTime", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetTime()))
            .MethodV2("getFullYear", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetFullYear()))
            .MethodV2("getMonth", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetMonth()))
            .MethodV2("getDate", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetDate()))
            .MethodV2("getDay", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetDay()))
            .MethodV2("getHours", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetHours()))
            .MethodV2("getMinutes", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetMinutes()))
            .MethodV2("getSeconds", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetSeconds()))
            .MethodV2("getMilliseconds", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetMilliseconds()))
            .MethodV2("getTimezoneOffset", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetTimezoneOffset()))
            // UTC Getter Methods
            .MethodV2("getUTCFullYear", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCFullYear()))
            .MethodV2("getUTCMonth", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCMonth()))
            .MethodV2("getUTCDate", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCDate()))
            .MethodV2("getUTCDay", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCDay()))
            .MethodV2("getUTCHours", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCHours()))
            .MethodV2("getUTCMinutes", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCMinutes()))
            .MethodV2("getUTCSeconds", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCSeconds()))
            .MethodV2("getUTCMilliseconds", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetUTCMilliseconds()))
            // Setter Methods
            .MethodV2("setTime", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetTime(Interpreter.ToNumber(args[0]))))
            .MethodV2("setFullYear", 1, 3, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetFullYear(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null)))
            .MethodV2("setMonth", 1, 2, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetMonth(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null)))
            .MethodV2("setDate", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetDate(Interpreter.ToNumber(args[0]))))
            .MethodV2("setHours", 1, 4, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetHours(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null,
                    args.Length > 3 && args[3].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[3]) : null)))
            .MethodV2("setMinutes", 1, 3, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetMinutes(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null)))
            .MethodV2("setSeconds", 1, 2, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetSeconds(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null)))
            .MethodV2("setMilliseconds", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetMilliseconds(Interpreter.ToNumber(args[0]))))
            // UTC Setter Methods
            .MethodV2("setUTCFullYear", 1, 3, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCFullYear(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null)))
            .MethodV2("setUTCMonth", 1, 2, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCMonth(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null)))
            .MethodV2("setUTCDate", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCDate(Interpreter.ToNumber(args[0]))))
            .MethodV2("setUTCHours", 1, 4, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCHours(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null,
                    args.Length > 3 && args[3].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[3]) : null)))
            .MethodV2("setUTCMinutes", 1, 3, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCMinutes(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null,
                    args.Length > 2 && args[2].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[2]) : null)))
            .MethodV2("setUTCSeconds", 1, 2, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCSeconds(
                    Interpreter.ToNumber(args[0]),
                    args.Length > 1 && args[1].Kind != ValueKind.Undefined ? (double?)Interpreter.ToNumber(args[1]) : null)))
            .MethodV2("setUTCMilliseconds", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetUTCMilliseconds(Interpreter.ToNumber(args[0]))))
            // Conversion Methods
            .MethodV2("toString", 0, (_, date, _) => RuntimeValue.FromString(date.ToString()!))
            .MethodV2("toISOString", 0, (_, date, _) => RuntimeValue.FromString(date.ToISOString()))
            .MethodV2("toDateString", 0, (_, date, _) => RuntimeValue.FromString(date.ToDateString()))
            .MethodV2("toTimeString", 0, (_, date, _) => RuntimeValue.FromString(date.ToTimeString()))
            .MethodV2("toUTCString", 0, (_, date, _) => RuntimeValue.FromString(date.ToUTCString()))
            // toLocale* accept optional (locales, options) per lib.es2020.date. With no arguments
            // they use the fast host-culture BCL path; when locale/options are supplied they route
            // through SharpTSIntlDateTimeFormat to honor them (#539).
            .MethodV2("toLocaleDateString", 0, 2, (_, date, args) => RuntimeValue.FromString(
                args.Length == 0
                    ? date.ToLocaleDateString()
                    : SharpTSDate.FormatToLocale(date.GetTime(), SharpTSDate.LocaleKindDate, LocaleArg(args, 0), LocaleArg(args, 1))))
            .MethodV2("toLocaleTimeString", 0, 2, (_, date, args) => RuntimeValue.FromString(
                args.Length == 0
                    ? date.ToLocaleTimeString()
                    : SharpTSDate.FormatToLocale(date.GetTime(), SharpTSDate.LocaleKindTime, LocaleArg(args, 0), LocaleArg(args, 1))))
            .MethodV2("toLocaleString", 0, 2, (_, date, args) => RuntimeValue.FromString(
                args.Length == 0
                    ? date.ToLocaleString()
                    : SharpTSDate.FormatToLocale(date.GetTime(), SharpTSDate.LocaleKindDateTime, LocaleArg(args, 0), LocaleArg(args, 1))))
            // ECMA-262 §21.4.4.37: toJSON returns the ISO string, or null for a non-finite
            // (Invalid) date — guard so we never reach ToISOString's RangeError throw.
            .MethodV2("toJSON", 0, (_, date, _) => double.IsNaN(date.GetTime())
                ? RuntimeValue.Null
                : RuntimeValue.FromString(date.ToISOString()))
            .MethodV2("valueOf", 0, (_, date, _) => RuntimeValue.FromNumber(date.ValueOf()))
            // Legacy methods (ECMA-262 Annex B)
            .MethodV2("getYear", 0, (_, date, _) => RuntimeValue.FromNumber(date.GetYear()))
            .MethodV2("setYear", 1, (_, date, args) =>
                RuntimeValue.FromNumber(date.SetYear(Interpreter.ToNumber(args[0]))))
            .Build();

    /// <summary>
    /// Extracts the locale/options argument at <paramref name="index"/> as a boxed object for
    /// <see cref="SharpTSDate.FormatToLocale"/>, treating absent/undefined as null (#539).
    /// </summary>
    private static object? LocaleArg(ReadOnlySpan<RuntimeValue> args, int index)
        => index < args.Length && args[index].Kind != ValueKind.Undefined ? args[index].ToObject() : null;

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
}
