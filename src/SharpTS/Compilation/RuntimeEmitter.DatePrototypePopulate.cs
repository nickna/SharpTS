using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// The ECMA-262 §21.4.4 <c>Date.prototype</c> method table: JS name, the
    /// <see cref="EmittedRuntime"/> helper backing it, and its spec <c>length</c>.
    /// </summary>
    /// <remarks>
    /// Kept as data rather than 43 hand-written wiring calls so a new Date helper is one row.
    /// The helpers all take the receiver as their first <c>object</c> parameter, which is the
    /// shape <see cref="EmitWirePrototypeMethod"/> expects.
    /// </remarks>
    private static (string JsName, System.Func<EmittedRuntime, MethodBuilder?> Helper, int Length)[]
        DatePrototypeMethods =>
    [
        ("getTime",              r => r.DateGetTime,              0),
        ("valueOf",              r => r.DateValueOf,              0),
        ("getFullYear",          r => r.DateGetFullYear,          0),
        ("getMonth",             r => r.DateGetMonth,             0),
        ("getDate",              r => r.DateGetDate,              0),
        ("getDay",               r => r.DateGetDay,               0),
        ("getHours",             r => r.DateGetHours,             0),
        ("getMinutes",           r => r.DateGetMinutes,           0),
        ("getSeconds",           r => r.DateGetSeconds,           0),
        ("getMilliseconds",      r => r.DateGetMilliseconds,      0),
        ("getTimezoneOffset",    r => r.DateGetTimezoneOffset,    0),
        ("getUTCFullYear",       r => r.DateGetUTCFullYear,       0),
        ("getUTCMonth",          r => r.DateGetUTCMonth,          0),
        ("getUTCDate",           r => r.DateGetUTCDate,           0),
        ("getUTCDay",            r => r.DateGetUTCDay,            0),
        ("getUTCHours",          r => r.DateGetUTCHours,          0),
        ("getUTCMinutes",        r => r.DateGetUTCMinutes,        0),
        ("getUTCSeconds",        r => r.DateGetUTCSeconds,        0),
        ("getUTCMilliseconds",   r => r.DateGetUTCMilliseconds,   0),
        ("setTime",              r => r.DateSetTime,              1),
        ("setMilliseconds",      r => r.DateSetMilliseconds,      1),
        ("setSeconds",           r => r.DateSetSeconds,           2),
        ("setMinutes",           r => r.DateSetMinutes,           3),
        ("setHours",             r => r.DateSetHours,             4),
        ("setDate",              r => r.DateSetDate,              1),
        ("setMonth",             r => r.DateSetMonth,             2),
        ("setFullYear",          r => r.DateSetFullYear,          3),
        ("setUTCMilliseconds",   r => r.DateSetUTCMilliseconds,   1),
        ("setUTCSeconds",        r => r.DateSetUTCSeconds,        2),
        ("setUTCMinutes",        r => r.DateSetUTCMinutes,        3),
        ("setUTCHours",          r => r.DateSetUTCHours,          4),
        ("setUTCDate",           r => r.DateSetUTCDate,           1),
        ("setUTCMonth",          r => r.DateSetUTCMonth,          2),
        ("setUTCFullYear",       r => r.DateSetUTCFullYear,       3),
        ("toString",             r => r.DateToString,             0),
        ("toISOString",          r => r.DateToISOString,          0),
        ("toDateString",         r => r.DateToDateString,         0),
        ("toTimeString",         r => r.DateToTimeString,         0),
        ("toUTCString",          r => r.DateToUTCString,          0),
        ("toJSON",               r => r.DateToJSON,               1),
        ("toLocaleString",       r => r.DateToLocaleString,       0),
        ("toLocaleDateString",   r => r.DateToLocaleDateString,   0),
        ("toLocaleTimeString",   r => r.DateToLocaleTimeString,   0),
    ];

    private void DefineDatePrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.DatePrototypePopulateMethod = typeBuilder.DefineMethod(
            "_DatePrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    /// <summary>
    /// Populates <see cref="EmittedRuntime.DatePrototypeField"/> with <c>$TSFunction</c>
    /// wrappers over the <c>$Runtime.Date*</c> helpers, plus the <c>constructor</c>
    /// back-reference, each installed as a §17 data property (W:T, E:F, C:T).
    /// <para>
    /// A no-op body when the program never mentions <c>Date</c> — the helpers don't exist
    /// then, and neither can a reference to <c>Date.prototype</c>.
    /// </para>
    /// </summary>
    private void EmitDatePrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = runtime.DatePrototypePopulateMethod.GetILGenerator();

        if (!_features.UsesDate)
        {
            il.Emit(OpCodes.Ret);
            return;
        }

        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.DatePrototypeField);

        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // ECMA-262 §21.4.4.1: Date.prototype.constructor is %Date%. Compiled bare `Date`
        // resolves to the emitted $TSDate type, matching GlobalThisStaticEmitter.
        EmitInstallConstructor(il, runtime, runtime.DatePrototypeField, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, runtime.TSDateType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        foreach (var (jsName, helper, jsLength) in DatePrototypeMethods)
        {
            EmitWirePrototypeMethod(il, runtime, runtime.DatePrototypeField, descLocal,
                setItem, jsName, helper(runtime), jsLength);
        }

        // §21.4.4: Date.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.DatePrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.Emit(OpCodes.Ret);
    }
}
