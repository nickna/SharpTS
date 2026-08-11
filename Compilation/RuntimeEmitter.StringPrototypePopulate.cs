using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a static method that populates the <c>String.prototype</c>
    /// singleton dictionary (<see cref="EmittedRuntime.StringPrototypeField"/>)
    /// with <c>$TSFunction</c> wrappers around the <c>$Runtime.String*</c>
    /// helpers. Mirrors <see cref="EmitArrayPrototypePopulate"/>.
    /// </summary>
    /// <remarks>
    /// Must be emitted AFTER all <c>EmitString*</c> helpers so the wrapped
    /// MethodBuilders are non-null. Most Test262 tests don't directly invoke
    /// the wrappers — they probe via <c>typeof String.prototype.X</c> /
    /// <c>isConstructor(String.prototype.X)</c>. Direct method calls on
    /// strings (<c>"abc".substring(1)</c>) flow through the type-checked
    /// dispatch path and bypass these wrappers.
    /// </remarks>
    /// <summary>
    /// Pre-defines the populate MethodBuilder shell so callers (e.g.
    /// $Runtime.GetProperty's Type-prototype branch) can reference it
    /// before the body is emitted.
    /// </summary>
    private void DefineStringPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.StringPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_StringPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitStringPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.StringPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.StringPrototypeField);

        var strDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // ECMA-262 22.1.3 String.prototype.constructor === String. Compiled
        // bare `String` resolves to typeof(string).
        EmitInstallConstructor(il, runtime, runtime.StringPrototypeField, strDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.String);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Wire with explicit JS-spec name + length via TSFunctionCtorWithCache.
        // Length is the user-callable arg count per ECMA-262 (e.g. substring
        // = 2, even though the underlying StringSubstring takes 3 .NET params
        // because the receiver is the first arg). Without this cache, Test262
        // `String.prototype.substring.length === 2` returns 3.
        // Built-in §17 attrs: W:T, E:F, C:T. Install a PDS data descriptor
        // alongside the dict store so gOPD reports the spec attributes.
        void Wire(string jsName, MethodBuilder? helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.StringPrototypeField, strDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("charAt",         runtime.StringCharAt,         1);
        Wire("charCodeAt",     runtime.StringCharCodeAt,     1);
        Wire("codePointAt",    runtime.StringCodePointAt,    1);
        Wire("substring",      runtime.StringSubstring,      2);
        Wire("substr",         runtime.StringSubstr,         2);
        // indexOf slot uses the from-variant so wrapper / any-typed receivers
        // routed through the prototype dispatch respect the optional second
        // argument (`pos`). The single-arg StringIndexOf form drops it
        // silently. ECMA-262 22.1.3.10 step 5: ToIntegerOrInfinity(undefined) = 0,
        // which $TSFunction CoercePrimitiveArgs delivers via ToNumber → NaN
        // and StringIndexOfFrom's NaN-clamps-to-0 path.
        Wire("indexOf",        runtime.StringIndexOfFrom,    1);
        Wire("lastIndexOf",    runtime.StringLastIndexOf,    1);
        Wire("toUpperCase",    runtime.StringToUpperCase,    0);
        Wire("toLowerCase",    runtime.StringToLowerCase,    0);
        Wire("trim",           runtime.StringTrim,           0);
        Wire("trimStart",      runtime.StringTrimStart,      0);
        Wire("trimEnd",        runtime.StringTrimEnd,        0);
        Wire("replace",        runtime.StringReplace,        2);
        Wire("replaceAll",     runtime.StringReplaceAll,     2);
        // split slot uses the regex-aware + limit-aware proto helper. The basic
        // StringSplit (string,string → list, no limit, no regex) is no longer
        // wired since wrapper / any-typed receivers reach the prototype slot
        // and need both behaviors that match the inline StringEmitter path.
        Wire("split",          runtime.StringSplitProto,     2);
        Wire("includes",       runtime.StringIncludes,       1);
        Wire("startsWith",     runtime.StringStartsWith,     1);
        Wire("endsWith",       runtime.StringEndsWith,       1);
        Wire("slice",          runtime.StringSlice,          2);
        Wire("repeat",         runtime.StringRepeat,         1);
        Wire("padStart",       runtime.StringPadStart,       1);
        Wire("padEnd",         runtime.StringPadEnd,         1);
        Wire("concat",         runtime.StringConcat,         1);
        Wire("at",             runtime.StringAt,             1);
        Wire("normalize",      runtime.StringNormalize,      0);
        Wire("localeCompare",  runtime.StringLocaleCompare,  1);

        // match/matchAll/search wired to the regex-aware helpers used by the
        // inline StringEmitter path. RequireObjectCoercible(this) is enforced
        // by $TSFunction.CoercePrimitiveArgs for any helper whose first param
        // is named "__this" with type string — Wire renames param 1 to
        // "__this" above, so borrowed-method calls of the form
        // `String.prototype.search.call(null, ...)` still throw TypeError.
        // Wrapper receivers (`new String("x").search(...)`) are unwrapped
        // through the same path via ToJsString reading __primitiveValue.
        // Pre-fix these slots were wired to _StringPrototypeStrictStub which
        // ignored arguments and returned the receiver string, regressing 45
        // Test262 tests once `new String(...)` started producing wrappers.
        Wire("match",                runtime.StringMatchRegExp,            1);
        Wire("matchAll",             runtime.StringMatchAllRegExp,         1);
        Wire("search",               runtime.StringSearchRegExp,           1);
        // Issue #91: spec-correct thisStringValue extraction so wrapper
        // receivers (`new String("x")`) return the underlying primitive
        // and non-string-like receivers throw TypeError per ECMA-262 22.1.3.27.
        Wire("toString",             runtime.StringProtoToStringHelper,    0);
        Wire("valueOf",              runtime.StringProtoToStringHelper,    0);
        // ECMA-262 22.1.3.21/22 RequireObjectCoercible(this) is the first step.
        // Wire to the strict StringToLowerCase / StringToUpperCase variants so
        // borrowed-method calls on null/undefined throw TypeError instead of
        // returning empty string. We don't actually localize (no Intl in the
        // standalone DLL), so toLocaleX === toX is acceptable per spec note.
        Wire("toLocaleLowerCase",    runtime.StringToLowerCase,            0);
        Wire("toLocaleUpperCase",    runtime.StringToUpperCase,            0);
        Wire("isWellFormed",         runtime.StringIsWellFormed,           0);
        Wire("toWellFormed",         runtime.StringToWellFormed,           0);

        // ECMA-262 §22.1.3.28: String.prototype[@@iterator] is a real
        // symbol-keyed built-in function named "[Symbol.iterator]".
        var iteratorFnLocal = il.DeclareLocal(_types.Object);
        var iteratorDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldnull);
        _types.EmitLoadMethodInfo(il, runtime.StringIterator);
        il.Emit(OpCodes.Ldstr, "[Symbol.iterator]");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Stloc, iteratorFnLocal);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldloc, iteratorFnLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        // Per ECMA-262 §22.1.3 String.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.Emit(OpCodes.Ret);
    }
}
