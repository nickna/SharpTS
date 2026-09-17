using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct DatePrototypePopulateInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        FieldBuilder ObjectPrototypeField,
        MethodBuilder TSFunctionGetOrCreate
    );

    /// <summary>
    /// The ECMA-262 §21.4.4 <c>Date.prototype</c> method table: JS name, the
    /// <see cref="EmittedDateImplementation"/> helper backing it, and its spec <c>length</c>.
    /// </summary>
    /// <remarks>
    /// Kept as data rather than 43 hand-written wiring calls so a new Date helper is one row.
    /// The helpers all take the receiver as their first <c>object</c> parameter, which is the
    /// shape <see cref="EmitWirePrototypeMethod"/> expects.
    /// </remarks>
    private static (string JsName, System.Func<EmittedDateImplementation, MethodBuilder> Helper, int Length)[]
        DatePrototypeMethods =>
    [
        ("getTime",              r => r.GetTime,              0),
        ("valueOf",              r => r.ValueOf,              0),
        ("getFullYear",          r => r.GetFullYear,          0),
        ("getMonth",             r => r.GetMonth,             0),
        ("getDate",              r => r.GetDate,              0),
        ("getDay",               r => r.GetDay,               0),
        ("getHours",             r => r.GetHours,             0),
        ("getMinutes",           r => r.GetMinutes,           0),
        ("getSeconds",           r => r.GetSeconds,           0),
        ("getMilliseconds",      r => r.GetMilliseconds,      0),
        ("getTimezoneOffset",    r => r.GetTimezoneOffset,    0),
        ("getUTCFullYear",       r => r.GetUTCFullYear,       0),
        ("getUTCMonth",          r => r.GetUTCMonth,          0),
        ("getUTCDate",           r => r.GetUTCDate,           0),
        ("getUTCDay",            r => r.GetUTCDay,            0),
        ("getUTCHours",          r => r.GetUTCHours,          0),
        ("getUTCMinutes",        r => r.GetUTCMinutes,        0),
        ("getUTCSeconds",        r => r.GetUTCSeconds,        0),
        ("getUTCMilliseconds",   r => r.GetUTCMilliseconds,   0),
        ("setTime",              r => r.SetTime,              1),
        ("setMilliseconds",      r => r.SetMilliseconds,      1),
        ("setSeconds",           r => r.SetSeconds,           2),
        ("setMinutes",           r => r.SetMinutes,           3),
        ("setHours",             r => r.SetHours,             4),
        ("setDate",              r => r.SetDate,              1),
        ("setMonth",             r => r.SetMonth,             2),
        ("setFullYear",          r => r.SetFullYear,          3),
        ("setUTCMilliseconds",   r => r.SetUTCMilliseconds,   1),
        ("setUTCSeconds",        r => r.SetUTCSeconds,        2),
        ("setUTCMinutes",        r => r.SetUTCMinutes,        3),
        ("setUTCHours",          r => r.SetUTCHours,          4),
        ("setUTCDate",           r => r.SetUTCDate,           1),
        ("setUTCMonth",          r => r.SetUTCMonth,          2),
        ("setUTCFullYear",       r => r.SetUTCFullYear,       3),
        ("toString",             r => r.ToStringMethod,             0),
        ("toISOString",          r => r.ToISOString,          0),
        ("toDateString",         r => r.ToDateString,         0),
        ("toTimeString",         r => r.ToTimeString,         0),
        ("toUTCString",          r => r.ToUTCString,          0),
        ("toJSON",               r => r.ToJSON,               1),
        ("toLocaleString",       r => r.ToLocaleString,       0),
        ("toLocaleDateString",   r => r.ToLocaleDateString,   0),
        ("toLocaleTimeString",   r => r.ToLocaleTimeString,   0),
    ];

    private void DefineDatePrototypePopulateShell(TypeBuilder typeBuilder, EmittedDateRuntime dates)
    {
        dates.PopulatePrototype = typeBuilder.DefineMethod(
            "_DatePrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    /// <summary>
    /// Populates <see cref="EmittedDateRuntime.Prototype"/> with <c>$TSFunction</c>
    /// wrappers over the <c>$Runtime.Date*</c> helpers, plus the <c>constructor</c>
    /// back-reference, each installed as a §17 data property (W:T, E:F, C:T).
    /// <para>
    /// A no-op body when the program never mentions <c>Date</c> — the helpers don't exist
    /// then, and neither can a reference to <c>Date.prototype</c>.
    /// </para>
    /// </summary>
    private void EmitDatePrototypePopulate(TypeBuilder typeBuilder, EmittedDateRuntime dates, DatePrototypePopulateInputs inputs)
    {
        var il = dates.PopulatePrototype.GetILGenerator();

        if (dates.Implementation is not { } date)
        {
            il.Emit(OpCodes.Ret);
            dates.MarkPrototypeBodyEmitted();
            return;
        }

        var descriptors = new PrototypeDescriptorInputs(
            inputs.DescriptorStorage.DescriptorConstructor, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!,
            inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!, inputs.DescriptorStorage.DefineProperty);

        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, dates.Prototype);

        var descLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        // ECMA-262 §21.4.4.1: Date.prototype.constructor is %Date%. Compiled bare `Date`
        // resolves to the emitted $TSDate type, matching GlobalThisStaticEmitter.
        EmitInstallConstructorDescriptor(il, descriptors, dates.Prototype, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, date.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        foreach (var (jsName, helper, jsLength) in DatePrototypeMethods)
        {
            EmitWirePrototypeMethodDescriptor(il, descriptors, inputs.TSFunctionGetOrCreate, dates.Prototype, descLocal,
                setItem, jsName, helper(date), jsLength);
        }

        // §21.4.4: Date.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, dates.Prototype);
        il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypeField);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ret);
        dates.MarkPrototypeBodyEmitted();
    }
}
