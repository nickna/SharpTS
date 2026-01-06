using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Date object members.
/// Includes static methods (Date.now()) and instance methods (date.getFullYear(), date.setMonth()).
/// </summary>
public static class DateBuiltIns
{
    /// <summary>
    /// Gets a static member (method) from the Date namespace.
    /// </summary>
    public static BuiltInMethod? GetStaticMethod(string name)
    {
        return name switch
        {
            "now" => new BuiltInMethod("now", 0, (_, _, _) => SharpTSDate.Now()),
            _ => null
        };
    }

    /// <summary>
    /// Gets an instance member (method) for a Date object.
    /// </summary>
    public static object? GetMember(SharpTSDate receiver, string name)
    {
        return name switch
        {
            // ========== Getter Methods ==========
            "getTime" => new BuiltInMethod("getTime", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetTime()),

            "getFullYear" => new BuiltInMethod("getFullYear", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetFullYear()),

            "getMonth" => new BuiltInMethod("getMonth", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetMonth()),

            "getDate" => new BuiltInMethod("getDate", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetDate()),

            "getDay" => new BuiltInMethod("getDay", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetDay()),

            "getHours" => new BuiltInMethod("getHours", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetHours()),

            "getMinutes" => new BuiltInMethod("getMinutes", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetMinutes()),

            "getSeconds" => new BuiltInMethod("getSeconds", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetSeconds()),

            "getMilliseconds" => new BuiltInMethod("getMilliseconds", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetMilliseconds()),

            "getTimezoneOffset" => new BuiltInMethod("getTimezoneOffset", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).GetTimezoneOffset()),

            // ========== Setter Methods ==========
            // All setters mutate the date and return the new timestamp

            "setTime" => new BuiltInMethod("setTime", 1, (_, recv, args) =>
                ((SharpTSDate)recv!).SetTime((double)args[0]!)),

            "setFullYear" => new BuiltInMethod("setFullYear", 1, 3, (_, recv, args) =>
                ((SharpTSDate)recv!).SetFullYear(
                    (double)args[0]!,
                    args.Count > 1 && args[1] != null ? (double?)args[1] : null,
                    args.Count > 2 && args[2] != null ? (double?)args[2] : null)),

            "setMonth" => new BuiltInMethod("setMonth", 1, 2, (_, recv, args) =>
                ((SharpTSDate)recv!).SetMonth(
                    (double)args[0]!,
                    args.Count > 1 && args[1] != null ? (double?)args[1] : null)),

            "setDate" => new BuiltInMethod("setDate", 1, (_, recv, args) =>
                ((SharpTSDate)recv!).SetDate((double)args[0]!)),

            "setHours" => new BuiltInMethod("setHours", 1, 4, (_, recv, args) =>
                ((SharpTSDate)recv!).SetHours(
                    (double)args[0]!,
                    args.Count > 1 && args[1] != null ? (double?)args[1] : null,
                    args.Count > 2 && args[2] != null ? (double?)args[2] : null,
                    args.Count > 3 && args[3] != null ? (double?)args[3] : null)),

            "setMinutes" => new BuiltInMethod("setMinutes", 1, 3, (_, recv, args) =>
                ((SharpTSDate)recv!).SetMinutes(
                    (double)args[0]!,
                    args.Count > 1 && args[1] != null ? (double?)args[1] : null,
                    args.Count > 2 && args[2] != null ? (double?)args[2] : null)),

            "setSeconds" => new BuiltInMethod("setSeconds", 1, 2, (_, recv, args) =>
                ((SharpTSDate)recv!).SetSeconds(
                    (double)args[0]!,
                    args.Count > 1 && args[1] != null ? (double?)args[1] : null)),

            "setMilliseconds" => new BuiltInMethod("setMilliseconds", 1, (_, recv, args) =>
                ((SharpTSDate)recv!).SetMilliseconds((double)args[0]!)),

            // ========== Conversion Methods ==========
            "toString" => new BuiltInMethod("toString", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).ToString()),

            "toISOString" => new BuiltInMethod("toISOString", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).ToISOString()),

            "toDateString" => new BuiltInMethod("toDateString", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).ToDateString()),

            "toTimeString" => new BuiltInMethod("toTimeString", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).ToTimeString()),

            "valueOf" => new BuiltInMethod("valueOf", 0, (_, recv, _) =>
                ((SharpTSDate)recv!).ValueOf()),

            _ => null
        };
    }
}
