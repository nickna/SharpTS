using System.Collections.Immutable;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Immutable Date prototype wiring definitions. Selectors resolve declarations from
/// the supplied compilation owner; the catalog never retains generated handles.
/// </summary>
internal static class DatePrototypeEmitCatalog
{
    /// <summary>
    /// The ECMA-262 §21.4.4 <c>Date.prototype</c> method table: JS name, the
    /// <see cref="EmittedDateImplementation"/> helper backing it, and its spec <c>length</c>.
    /// </summary>
    /// <remarks>
    /// Kept as data rather than 43 hand-written wiring calls so a new Date helper is one row.
    /// The helpers all take the receiver as their first <c>object</c> parameter, which is the
    /// shape the prototype wiring code expects.
    /// </remarks>
    internal static ImmutableArray<(string JsName, Func<EmittedDateImplementation, MethodBuilder> Helper, int Length)>
        Methods { get; } =
    [
        ("getTime",              static r => r.GetTime,              0),
        ("valueOf",              static r => r.ValueOf,              0),
        ("getFullYear",          static r => r.GetFullYear,          0),
        ("getMonth",             static r => r.GetMonth,             0),
        ("getDate",              static r => r.GetDate,              0),
        ("getDay",               static r => r.GetDay,               0),
        ("getHours",             static r => r.GetHours,             0),
        ("getMinutes",           static r => r.GetMinutes,           0),
        ("getSeconds",           static r => r.GetSeconds,           0),
        ("getMilliseconds",      static r => r.GetMilliseconds,      0),
        ("getTimezoneOffset",    static r => r.GetTimezoneOffset,    0),
        ("getUTCFullYear",       static r => r.GetUTCFullYear,       0),
        ("getUTCMonth",          static r => r.GetUTCMonth,          0),
        ("getUTCDate",           static r => r.GetUTCDate,           0),
        ("getUTCDay",            static r => r.GetUTCDay,            0),
        ("getUTCHours",          static r => r.GetUTCHours,          0),
        ("getUTCMinutes",        static r => r.GetUTCMinutes,        0),
        ("getUTCSeconds",        static r => r.GetUTCSeconds,        0),
        ("getUTCMilliseconds",   static r => r.GetUTCMilliseconds,   0),
        ("setTime",              static r => r.SetTime,              1),
        ("setMilliseconds",      static r => r.SetMilliseconds,      1),
        ("setSeconds",           static r => r.SetSeconds,           2),
        ("setMinutes",           static r => r.SetMinutes,           3),
        ("setHours",             static r => r.SetHours,             4),
        ("setDate",              static r => r.SetDate,              1),
        ("setMonth",             static r => r.SetMonth,             2),
        ("setFullYear",          static r => r.SetFullYear,          3),
        ("setUTCMilliseconds",   static r => r.SetUTCMilliseconds,   1),
        ("setUTCSeconds",        static r => r.SetUTCSeconds,        2),
        ("setUTCMinutes",        static r => r.SetUTCMinutes,        3),
        ("setUTCHours",          static r => r.SetUTCHours,          4),
        ("setUTCDate",           static r => r.SetUTCDate,           1),
        ("setUTCMonth",          static r => r.SetUTCMonth,          2),
        ("setUTCFullYear",       static r => r.SetUTCFullYear,       3),
        ("toString",             static r => r.ToStringMethod,             0),
        ("toISOString",          static r => r.ToISOString,          0),
        ("toDateString",         static r => r.ToDateString,         0),
        ("toTimeString",         static r => r.ToTimeString,         0),
        ("toUTCString",          static r => r.ToUTCString,          0),
        ("toJSON",               static r => r.ToJSON,               1),
        ("toLocaleString",       static r => r.ToLocaleString,       0),
        ("toLocaleDateString",   static r => r.ToLocaleDateString,   0),
        ("toLocaleTimeString",   static r => r.ToLocaleTimeString,   0),
    ];
}
