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
    private readonly record struct BuiltInStaticDispatchInputs(
        MethodInfo GetOrCreate, MethodInfo IsArray, EmittedNumberRuntime Numbers,
        MethodInfo StringFromCharCode, MethodInfo StringFromCodePoint, MethodInfo StringRaw,
        EmittedObjectKeysRuntime ObjectKeys, EmittedObjectOperationsRuntime ObjectOperations,
        EmittedObjectStateRuntime ObjectState, EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedObjectDescriptorRuntime ObjectDescriptors, MethodInfo ObjectHasOwn,
        Type SymbolType, MethodInfo SymbolFor, MethodInfo SymbolKeyFor,
        EmittedBigIntImplementation? BigInt, EmittedPromiseRuntime? Promise,
        Type ErrorType, MethodInfo ErrorIsError, EmittedDateImplementation? Dates);

    /// <summary>
    /// Defines the <c>LookupBuiltInStaticMember</c> <see cref="MethodBuilder"/>
    /// without writing its body. Must be called early in runtime emission —
    /// before <c>EmitGetProperty</c> — because <c>GetProperty</c>'s Type
    /// branch emits a call to this method. The body is filled in later by
    /// <see cref="EmitLookupBuiltInStaticMemberBody"/>, after all the backing
    /// static runtime methods (<c>IsArray</c>, <c>NumberIs*</c>, <c>StringFrom*</c>)
    /// have been emitted.
    /// </summary>
    private void DefineLookupBuiltInStaticMember(TypeBuilder typeBuilder, EmittedBuiltInStaticDispatchRuntime statics)
    {
        statics.Lookup = typeBuilder.DefineMethod(
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
    private void EmitLookupBuiltInStaticMemberBody(
        EmittedBuiltInStaticDispatchRuntime statics, BuiltInStaticDispatchInputs inputs)
    {
        var method = statics.Lookup;
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
            il.Emit(OpCodes.Call, inputs.GetOrCreate);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        // Array.* — stored-as-value Array reference accessing static members.
        EmitLookup(_types.IListOfObject, "isArray", inputs.IsArray, 1);

        // Number.* — bare `Number` identifier now resolves to typeof(double)
        // via issue #62, so the value-form path lands here.
        EmitLookup(_types.Double, "isNaN",         inputs.Numbers.IsNaN, 1);
        EmitLookup(_types.Double, "isFinite",      inputs.Numbers.IsFinite, 1);
        EmitLookup(_types.Double, "isInteger",     inputs.Numbers.IsInteger, 1);
        EmitLookup(_types.Double, "isSafeInteger", inputs.Numbers.IsSafeInteger, 1);

        // String.* — bare `String` identifier resolves to typeof(string).
        EmitLookup(_types.String, "fromCharCode",  inputs.StringFromCharCode, 1);
        EmitLookup(_types.String, "fromCodePoint", inputs.StringFromCodePoint, 1);
        EmitLookup(_types.String, "raw",           inputs.StringRaw, 1);

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
            il.Emit(OpCodes.Call, inputs.GetOrCreate);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        EmitObjectMethodLookup("keys",                    inputs.ObjectKeys.Keys, 1);
        EmitObjectMethodLookup("values",                  inputs.ObjectOperations.Values, 1);
        EmitObjectMethodLookup("entries",                 inputs.ObjectOperations.Entries, 1);
        EmitObjectMethodLookup("fromEntries",             inputs.ObjectOperations.FromEntries, 1);
        EmitObjectMethodLookup("freeze",                  inputs.ObjectState.Freeze, 1);
        EmitObjectMethodLookup("seal",                    inputs.ObjectState.Seal, 1);
        EmitObjectMethodLookup("preventExtensions",       inputs.ObjectState.PreventExtensions, 1);
        EmitObjectMethodLookup("getOwnPropertyNames",     inputs.ObjectKeys.Names, 1);
        EmitObjectMethodLookup("getOwnPropertySymbols",   inputs.ObjectKeys.Symbols, 1);
        EmitObjectMethodLookup("getPrototypeOf",          inputs.ObjectPrototypes.GetPrototypeOf, 1);
        EmitObjectMethodLookup("setPrototypeOf",          inputs.ObjectPrototypes.SetPrototypeOf, 2);
        EmitObjectMethodLookup("defineProperty",          inputs.ObjectDescriptors.DefineProperty, 3);
        EmitObjectMethodLookup("defineProperties",        inputs.ObjectDescriptors.DefineProperties, 2);
        EmitObjectMethodLookup("getOwnPropertyDescriptor",  inputs.ObjectDescriptors.GetOwnPropertyDescriptor, 2);
        EmitObjectMethodLookup("getOwnPropertyDescriptors", inputs.ObjectDescriptors.GetOwnPropertyDescriptors, 1);
        // create routes through the value-form wrapper: reflection dispatch
        // pads the missing props arg with null, which raw ObjectCreate must
        // treat as the explicit-null TypeError case.
        EmitObjectMethodLookup("create",                  inputs.ObjectPrototypes.CreateValueForm, 2);
        EmitObjectMethodLookup("assign",                  inputs.ObjectOperations.Assign, 2);
        EmitObjectMethodLookup("is",                      inputs.ObjectOperations.Is, 2);
        EmitObjectMethodLookup("hasOwn",                  inputs.ObjectHasOwn, 2);
        EmitObjectMethodLookup("groupBy",                 inputs.ObjectOperations.GroupBy, 2);
        EmitObjectMethodLookup("isExtensible",            inputs.ObjectState.IsExtensible, 1);
        EmitObjectMethodLookup("isFrozen",                inputs.ObjectState.IsFrozen, 1);
        EmitObjectMethodLookup("isSealed",                inputs.ObjectState.IsSealed, 1);

        // Symbol.* (#234) — bare `Symbol` resolves to the $TSSymbol Type token.
        // Well-known symbols (iterator, species, …) are public static FIELDS
        // carrying their JS names, so GetProperty's static-field probe resolves
        // them before this table is consulted. Only the static methods need
        // entries: their .NET names are For/KeyFor, which the case-sensitive
        // static-method probe misses.
        EmitLookup(inputs.SymbolType, "for", inputs.SymbolFor, 1);
        EmitLookup(inputs.SymbolType, "keyFor", inputs.SymbolKeyFor, 1);

        // BigInt.asIntN/asUintN — BigInt resolves to System.Numerics.BigInteger
        // in emitted value form; the BCL type has no JavaScript truncation APIs.
        if (inputs.BigInt is { } bigInt)
        {
            EmitLookup(_types.BigInteger, "asIntN", bigInt.AsIntN, 2);
            EmitLookup(_types.BigInteger, "asUintN", bigInt.AsUintN, 2);
        }

        // Promise.* and Error.isError are emitted on $Runtime rather than as
        // CLR statics on their constructor Type tokens. Route value-form and
        // descriptor reads through the same identity-cached wrappers used by
        // their compile-time static emitters.
        if (inputs.Promise is { } promise)
        {
            EmitLookup(_types.TaskOfObject, "resolve", promise.ResolveStatic, 1);
            EmitLookup(_types.TaskOfObject, "reject", promise.RejectStatic, 1);
            EmitLookup(_types.TaskOfObject, "all", promise.AllStatic, 1);
            EmitLookup(_types.TaskOfObject, "allKeyed", promise.AllKeyedStatic, 1);
            EmitLookup(_types.TaskOfObject, "race", promise.RaceStatic, 1);
            EmitLookup(_types.TaskOfObject, "allSettled", promise.AllSettledStatic, 1);
            EmitLookup(_types.TaskOfObject, "allSettledKeyed", promise.AllSettledKeyedStatic, 1);
            EmitLookup(_types.TaskOfObject, "any", promise.AnyStatic, 1);
            // Guest classes that `extend Promise` derive from the emitted wrapper.
            EmitLookup(promise.Type, "resolve", promise.ResolveStatic, 1);
            EmitLookup(promise.Type, "reject", promise.RejectStatic, 1);
            EmitLookup(promise.Type, "all", promise.AllStatic, 1);
            EmitLookup(promise.Type, "allKeyed", promise.AllKeyedStatic, 1);
            EmitLookup(promise.Type, "race", promise.RaceStatic, 1);
            EmitLookup(promise.Type, "allSettled", promise.AllSettledStatic, 1);
            EmitLookup(promise.Type, "allSettledKeyed", promise.AllSettledKeyedStatic, 1);
            EmitLookup(promise.Type, "any", promise.AnyStatic, 1);
        }
        EmitLookup(inputs.ErrorType, "isError", inputs.ErrorIsError, 1);

        // Date.* — bare `Date` resolves to the $TSDate Type token. The static
        // is .NET-cased ("Now"), so the case-sensitive static-method probe in
        // GetProperty misses it; route through $Runtime.DateNow so value-form
        // dispatch (`var nativeNow = Date.now;` — lodash's shortOut idiom)
        // matches the syntactic Date.now() path, virtual timers included.
        // Null when UsesDate is off — Date can't be referenced then anyway.
        if (inputs.Dates != null)
            EmitLookup(inputs.Dates.Type, "now", inputs.Dates.Now, 0);
        // Date.UTC / Date.parse value-form (`const f = Date.UTC; f(...)`) — #538. The wrapper
        // packs the JS args into the backing methods' object[] / object parameter.
        if (inputs.Dates != null)
            EmitLookup(inputs.Dates.Type, "UTC", inputs.Dates.StaticUTC, 7);
        if (inputs.Dates != null)
            EmitLookup(inputs.Dates.Type, "parse", inputs.Dates.StaticParse, 1);

        // Math.* deliberately not handled here — bare `Math` emits the null
        // pseudo-variable (not a Type token), so its value-form access goes
        // through MathStaticEmitter.TryEmitStaticPropertyGet at compile time.
        // If a `var m = Math; m.floor(x)` pattern surfaces in real code, add
        // a separate dispatch keyed off the null receiver.

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        statics.MarkLookupBodyEmitted();
    }
}
