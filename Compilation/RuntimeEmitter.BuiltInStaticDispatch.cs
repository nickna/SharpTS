using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits <c>$Runtime.LookupBuiltInStaticMember</c>, a runtime helper that
/// mirrors the compile-time static emitter registry (<c>ArrayStaticEmitter</c>,
/// <c>NumberStaticEmitter</c>, <c>StringStaticEmitter</c>, <c>MathStaticEmitter</c>)
/// for the value-form access path. When a built-in type constructor is stored
/// in a variable — e.g. <c>var A = Array</c> → a <c>System.Type</c> token —
/// and then accessed for a static member (<c>A.isArray</c>), the generic
/// <c>$Runtime.GetProperty</c> Type branch reflects for real .NET statics and
/// finds nothing (<c>IList&lt;object&gt;</c>, <c>double</c>, <c>string</c> have
/// no matching static methods). This helper supplies the same <c>$TSFunction</c>
/// wrapper that the compile-time registry would have emitted at the bare
/// <c>Array.isArray</c> site.
///
/// Lodash hits this heavily: <c>var Array = context.Array; var isArray = Array.isArray;</c>
/// inside <c>runInContext</c> — without runtime dispatch the local <c>isArray</c>
/// ends up <c>undefined</c> and every internal <c>isArray(x)</c> silently
/// returns wrong values. See issue #63 for the full chain.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Defines the <c>LookupBuiltInStaticMember</c> <see cref="MethodBuilder"/>
    /// without writing its body. Must be called early in runtime emission —
    /// before <c>EmitGetProperty</c> — because <c>GetProperty</c>'s Type
    /// branch emits a call to this method. The body is filled in later by
    /// <see cref="EmitLookupBuiltInStaticMemberBody"/>, after all the backing
    /// static runtime methods (<c>IsArray</c>, <c>NumberIs*</c>, <c>StringFrom*</c>)
    /// have been emitted.
    /// </summary>
    private void DefineLookupBuiltInStaticMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.LookupBuiltInStaticMember = typeBuilder.DefineMethod(
            "LookupBuiltInStaticMember",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type, _types.String]
        );
    }

    /// <summary>
    /// Emits the body of <c>LookupBuiltInStaticMember</c>. Depends on the
    /// backing runtime methods being defined, so must run after
    /// <c>EmitIsArray</c>, <c>EmitNumberMethods</c>, <c>EmitStringFromCharCode</c>,
    /// <c>EmitStringFromCodePoint</c>, and <c>EmitTSFunctionClass</c>.
    /// </summary>
    private void EmitLookupBuiltInStaticMemberBody(EmittedRuntime runtime)
    {
        var method = runtime.LookupBuiltInStaticMember;
        var il = method.GetILGenerator();
        var notFoundLabel = il.DefineLabel();

        // Pre-compute the MethodInfo object for op_Equality on System.Type and
        // GetTypeFromHandle on RuntimeTypeHandle — both called many times below.
        var strEquals = _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!;
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!;
        var stringOpEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        // Emit one branch per (Type, name, runtimeMethod, specLength) tuple. Uses
        // TSFunctionGetOrCreate so the returned wrapper has stable identity:
        // bracket access (`Array["isArray"]`) and direct dispatch (`Array.isArray`)
        // both hit the same MethodInfo-keyed cache → `===` holds. test262
        // gOPD identity tests + verifyProperty(Object.assign) rely on this.
        void EmitLookup(Type targetType, string memberName, MethodInfo backingMethod, int specLength)
        {
            var skipLabel = il.DefineLabel();

            // Type check
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, targetType);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, skipLabel);

            // Name check
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Call, stringOpEq);
            il.Emit(OpCodes.Brfalse, skipLabel);

            // Match — return TSFunctionGetOrCreate(method, name, length) so the
            // wrapper identity matches the syntactic-dispatch path.
            _types.EmitLoadMethodInfo(il, backingMethod);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Ldc_I4, specLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        // Array.* — stored-as-value Array reference accessing static members.
        EmitLookup(_types.IListOfObject, "isArray", runtime.IsArray, 1);

        // Number.* — bare `Number` identifier now resolves to typeof(double)
        // via issue #62, so the value-form path lands here.
        EmitLookup(_types.Double, "isNaN",         runtime.NumberIsNaN, 1);
        EmitLookup(_types.Double, "isFinite",      runtime.NumberIsFinite, 1);
        EmitLookup(_types.Double, "isInteger",     runtime.NumberIsInteger, 1);
        EmitLookup(_types.Double, "isSafeInteger", runtime.NumberIsSafeInteger, 1);

        // String.* — bare `String` identifier resolves to typeof(string).
        EmitLookup(_types.String, "fromCharCode",  runtime.StringFromCharCode, 1);
        EmitLookup(_types.String, "fromCodePoint", runtime.StringFromCodePoint, 1);

        // Object.* — bracket-form access (`Object["assign"]`) and value-form
        // access (`let f = Object; f.assign`) both land here. Routes through
        // TSFunctionGetOrCreate (not the bare ctor below) so identity matches
        // the syntactic `Object.assign` dispatch — test262 `desc.value ===
        // Object.X` and `Object.X === Object.X` rely on identity stability.
        void EmitObjectMethodLookup(string memberName, MethodInfo backingMethod, int specLength)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Call, stringOpEq);
            il.Emit(OpCodes.Brfalse, skipLabel);

            // TSFunctionGetOrCreate(MethodInfo, name, length) — identity-stable
            // wrapper (same MethodInfo key → same wrapper instance).
            _types.EmitLoadMethodInfo(il, backingMethod);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Ldc_I4, specLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        EmitObjectMethodLookup("keys",                    runtime.GetKeys, 1);
        EmitObjectMethodLookup("values",                  runtime.GetValues, 1);
        EmitObjectMethodLookup("entries",                 runtime.GetEntries, 1);
        EmitObjectMethodLookup("fromEntries",             runtime.ObjectFromEntries, 1);
        EmitObjectMethodLookup("freeze",                  runtime.ObjectFreeze, 1);
        EmitObjectMethodLookup("seal",                    runtime.ObjectSeal, 1);
        EmitObjectMethodLookup("preventExtensions",       runtime.ObjectPreventExtensions, 1);
        EmitObjectMethodLookup("getOwnPropertyNames",     runtime.GetOwnPropertyNames, 1);
        EmitObjectMethodLookup("getOwnPropertySymbols",   runtime.GetOwnPropertySymbols, 1);
        EmitObjectMethodLookup("getPrototypeOf",          runtime.ObjectGetPrototypeOf, 1);
        EmitObjectMethodLookup("setPrototypeOf",          runtime.ObjectSetPrototypeOf, 2);
        EmitObjectMethodLookup("defineProperty",          runtime.ObjectDefineProperty, 3);
        EmitObjectMethodLookup("defineProperties",        runtime.ObjectDefineProperties, 2);
        EmitObjectMethodLookup("getOwnPropertyDescriptor",  runtime.ObjectGetOwnPropertyDescriptor, 2);
        EmitObjectMethodLookup("getOwnPropertyDescriptors", runtime.ObjectGetOwnPropertyDescriptors, 1);
        // create routes through the value-form wrapper: reflection dispatch
        // pads the missing props arg with null, which raw ObjectCreate must
        // treat as the explicit-null TypeError case.
        EmitObjectMethodLookup("create",                  runtime.ObjectCreateValueForm, 2);
        EmitObjectMethodLookup("assign",                  runtime.ObjectAssign, 2);
        EmitObjectMethodLookup("is",                      runtime.ObjectIs, 2);
        EmitObjectMethodLookup("hasOwn",                  runtime.ObjectHasOwn, 2);
        EmitObjectMethodLookup("groupBy",                 runtime.ObjectGroupBy, 2);
        EmitObjectMethodLookup("isExtensible",            runtime.ObjectIsExtensible, 1);
        EmitObjectMethodLookup("isFrozen",                runtime.ObjectIsFrozen, 1);
        EmitObjectMethodLookup("isSealed",                runtime.ObjectIsSealed, 1);

        // Symbol.* (#234) — bare `Symbol` resolves to the $TSSymbol Type token.
        // Well-known symbols (iterator, species, …) are public static FIELDS
        // carrying their JS names, so GetProperty's static-field probe resolves
        // them before this table is consulted. Only the static methods need
        // entries: their .NET names are For/KeyFor, which the case-sensitive
        // static-method probe misses.
        EmitLookup(runtime.TSSymbolType, "for", runtime.SymbolFor, 1);
        EmitLookup(runtime.TSSymbolType, "keyFor", runtime.SymbolKeyFor, 1);

        // Date.* — bare `Date` resolves to the $TSDate Type token. The static
        // is .NET-cased ("Now"), so the case-sensitive static-method probe in
        // GetProperty misses it; route through $Runtime.DateNow so value-form
        // dispatch (`var nativeNow = Date.now;` — lodash's shortOut idiom)
        // matches the syntactic Date.now() path, virtual timers included.
        // Null when UsesDate is off — Date can't be referenced then anyway.
        if (runtime.DateNow != null)
            EmitLookup(runtime.TSDateType, "now", runtime.DateNow, 0);
        // Date.UTC / Date.parse value-form (`const f = Date.UTC; f(...)`) — #538. The wrapper
        // packs the JS args into the backing methods' object[] / object parameter.
        if (runtime.TSDateUTCStatic != null)
            EmitLookup(runtime.TSDateType, "UTC", runtime.TSDateUTCStatic, 7);
        if (runtime.TSDateParseStatic != null)
            EmitLookup(runtime.TSDateType, "parse", runtime.TSDateParseStatic, 1);

        // Math.* deliberately not handled here — bare `Math` emits the null
        // pseudo-variable (not a Type token), so its value-form access goes
        // through MathStaticEmitter.TryEmitStaticPropertyGet at compile time.
        // If a `var m = Math; m.floor(x)` pattern surfaces in real code, add
        // a separate dispatch keyed off the null receiver.

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
