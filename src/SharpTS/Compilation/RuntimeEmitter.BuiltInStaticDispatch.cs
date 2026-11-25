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
/// no matching JavaScript members). This helper supplies explicit constants and
/// the same <c>$TSFunction</c> wrappers that the compile-time registry would emit
/// at a bare <c>Array.isArray</c> site.
///
/// Lodash hits this heavily: <c>var Array = context.Array; var isArray = Array.isArray;</c>
/// inside <c>runInContext</c> — without runtime dispatch the local <c>isArray</c>
/// ends up <c>undefined</c> and every internal <c>isArray(x)</c> silently
/// returns wrong values. See issue #63 for the full chain.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly record struct BuiltInStaticDispatchInputs(
        MethodInfo GetOrCreate, EmittedArrayOperationsRuntime Arrays, EmittedNumberRuntime Numbers,
        MethodInfo StringFromCharCode, MethodInfo StringFromCodePoint, MethodInfo StringRaw,
        EmittedObjectKeysRuntime ObjectKeys, EmittedObjectOperationsRuntime ObjectOperations,
        EmittedObjectStateRuntime ObjectState, EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedObjectDescriptorRuntime ObjectDescriptors, MethodInfo ObjectHasOwn,
        Type SymbolType, MethodInfo SymbolFor, MethodInfo SymbolKeyFor,
        EmittedBigIntImplementation? BigInt, EmittedPromiseRuntime? Promise,
        Type ErrorType, MethodInfo ErrorIsError, EmittedDateImplementation? Dates,
        EmittedMapRuntime? Map, EmittedArrayBufferRuntime? ArrayBuffer, EmittedBufferRuntime? Buffer);

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

        var typeOpEq = _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!;
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!;
        var stringOpEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        void EmitTypeGuard(Type targetType, Label skip)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, targetType);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Call, typeOpEq);
            il.Emit(OpCodes.Brfalse, skip);
        }

        // Bracket access, descriptor lookup, and direct dispatch share the wrapper cache.
        void EmitNamedLookup(string memberName, MethodInfo backingMethod, int specLength)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Call, stringOpEq);
            // A short branch here can corrupt the following method token in persisted native output.
            il.Emit(OpCodes.Brfalse, skip);

            var parameterProducer = EmitReflectedMethodLiteral(il, backingMethod);
            il.Emit(OpCodes.Ldstr, memberName);
            il.Emit(OpCodes.Ldc_I4, specLength);
            EmitReflectedFunctionFactory(il, inputs.GetOrCreate, parameterProducer);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }

        void EmitLookup(Type targetType, string memberName, MethodInfo backingMethod, int specLength)
        {
            var skip = il.DefineLabel();
            EmitTypeGuard(targetType, skip);
            EmitNamedLookup(memberName, backingMethod, specLength);
            il.MarkLabel(skip);
        }

        // Array.* — stored-as-value Array reference accessing static members.
        var skipArray = il.DefineLabel();
        EmitTypeGuard(_types.IListOfObject, skipArray);
        EmitNamedLookup("isArray", inputs.Arrays.IsArray, 1);
        EmitNamedLookup("from", inputs.Arrays.FromAdapter, 1);
        EmitNamedLookup("of", inputs.Arrays.Of, 0);
        il.MarkLabel(skipArray);

        if (inputs.ArrayBuffer is { } arrayBuffer)
            EmitLookup(arrayBuffer.Type, "isView", arrayBuffer.IsView, 1);

        if (inputs.Buffer is { } buffer)
        {
            var skipBuffer = il.DefineLabel();
            EmitTypeGuard(buffer.Type, skipBuffer);
            EmitNamedLookup("from", buffer.FromValue, 1);
            EmitNamedLookup("alloc", buffer.AllocValue, 3);
            il.MarkLabel(skipBuffer);
        }

        if (inputs.Map is { } map)
            EmitLookup(_types.DictionaryObjectObject, "groupBy", map.GroupBy, 2);

        // Number.* — bare `Number` identifier now resolves to typeof(double)
        // via issue #62, so the value-form path lands here.
        var skipNumber = il.DefineLabel();
        EmitTypeGuard(_types.Double, skipNumber);
        EmitNamedLookup("isNaN",         inputs.Numbers.IsNaN, 1);
        EmitNamedLookup("isFinite",      inputs.Numbers.IsFinite, 1);
        EmitNamedLookup("isInteger",     inputs.Numbers.IsInteger, 1);
        EmitNamedLookup("isSafeInteger", inputs.Numbers.IsSafeInteger, 1);
        EmitNamedLookup("parseInt",      inputs.Numbers.ParseInt, 2);
        EmitNamedLookup("parseFloat",    inputs.Numbers.ParseFloat, 1);

        // Number's constants are JavaScript own properties, not CLR field names.
        // In particular NaN must remain readable when native CLR probes are skipped.
        void EmitNumberConstant(string name, double value)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, stringOpEq);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitNumberConstant("MAX_VALUE", double.MaxValue);
        EmitNumberConstant("MIN_VALUE", double.Epsilon);
        EmitNumberConstant("NaN", double.NaN);
        EmitNumberConstant("POSITIVE_INFINITY", double.PositiveInfinity);
        EmitNumberConstant("NEGATIVE_INFINITY", double.NegativeInfinity);
        EmitNumberConstant("MAX_SAFE_INTEGER", 9007199254740991.0);
        EmitNumberConstant("MIN_SAFE_INTEGER", -9007199254740991.0);
        EmitNumberConstant("EPSILON", 2.220446049250313e-16);
        il.MarkLabel(skipNumber);

        // String.* — bare `String` identifier resolves to typeof(string).
        var skipString = il.DefineLabel();
        EmitTypeGuard(_types.String, skipString);
        EmitNamedLookup("fromCharCode",  inputs.StringFromCharCode, 1);
        EmitNamedLookup("fromCodePoint", inputs.StringFromCodePoint, 1);
        EmitNamedLookup("raw",           inputs.StringRaw, 1);
        il.MarkLabel(skipString);

        // Object.* — one receiver check for bracket, value-form, and descriptor lookups.
        var skipObject = il.DefineLabel();
        EmitTypeGuard(_types.Object, skipObject);
        EmitNamedLookup("keys",                      inputs.ObjectKeys.Keys, 1);
        EmitNamedLookup("values",                    inputs.ObjectOperations.Values, 1);
        EmitNamedLookup("entries",                   inputs.ObjectOperations.Entries, 1);
        EmitNamedLookup("fromEntries",               inputs.ObjectOperations.FromEntries, 1);
        EmitNamedLookup("freeze",                    inputs.ObjectState.Freeze, 1);
        EmitNamedLookup("seal",                      inputs.ObjectState.Seal, 1);
        EmitNamedLookup("preventExtensions",         inputs.ObjectState.PreventExtensions, 1);
        EmitNamedLookup("getOwnPropertyNames",       inputs.ObjectKeys.Names, 1);
        EmitNamedLookup("getOwnPropertySymbols",     inputs.ObjectKeys.Symbols, 1);
        EmitNamedLookup("getPrototypeOf",            inputs.ObjectPrototypes.GetPrototypeOf, 1);
        EmitNamedLookup("setPrototypeOf",            inputs.ObjectPrototypes.SetPrototypeOf, 2);
        EmitNamedLookup("defineProperty",            inputs.ObjectDescriptors.DefineProperty, 3);
        EmitNamedLookup("defineProperties",          inputs.ObjectDescriptors.DefineProperties, 2);
        EmitNamedLookup("getOwnPropertyDescriptor",  inputs.ObjectDescriptors.GetOwnPropertyDescriptorValueForm, 2);
        EmitNamedLookup("getOwnPropertyDescriptors", inputs.ObjectDescriptors.GetOwnPropertyDescriptors, 1);
        // create routes through the value-form wrapper: reflection dispatch
        // pads the missing props arg with null, which raw ObjectCreate must
        // treat as the explicit-null TypeError case.
        EmitNamedLookup("create",       inputs.ObjectPrototypes.CreateValueForm, 2);
        EmitNamedLookup("assign",       inputs.ObjectOperations.Assign, 2);
        EmitNamedLookup("is",           inputs.ObjectOperations.Is, 2);
        EmitNamedLookup("hasOwn",       inputs.ObjectHasOwn, 2);
        EmitNamedLookup("groupBy",      inputs.ObjectOperations.GroupBy, 2);
        EmitNamedLookup("isExtensible", inputs.ObjectState.IsExtensible, 1);
        EmitNamedLookup("isFrozen",     inputs.ObjectState.IsFrozen, 1);
        EmitNamedLookup("isSealed",     inputs.ObjectState.IsSealed, 1);
        il.MarkLabel(skipObject);

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
            EmitLookup(_types.TaskOfObject, "withResolvers", promise.WithResolversStatic, 0);
            // Guest classes that `extend Promise` derive from the emitted wrapper.
            EmitLookup(promise.Type, "resolve", promise.ResolveStatic, 1);
            EmitLookup(promise.Type, "reject", promise.RejectStatic, 1);
            EmitLookup(promise.Type, "all", promise.AllStatic, 1);
            EmitLookup(promise.Type, "allKeyed", promise.AllKeyedStatic, 1);
            EmitLookup(promise.Type, "race", promise.RaceStatic, 1);
            EmitLookup(promise.Type, "allSettled", promise.AllSettledStatic, 1);
            EmitLookup(promise.Type, "allSettledKeyed", promise.AllSettledKeyedStatic, 1);
            EmitLookup(promise.Type, "any", promise.AnyStatic, 1);
            EmitLookup(promise.Type, "withResolvers", promise.WithResolversStatic, 0);
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
