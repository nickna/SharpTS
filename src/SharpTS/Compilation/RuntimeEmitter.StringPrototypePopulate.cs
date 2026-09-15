using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Peer metadata is scoped to population until descriptor/function/symbol/RegExp families own their inputs.
    private readonly record struct StringPrototypeInputs(
        Type DescriptorType, PrototypeDescriptorInputs Descriptors, MethodInfo FunctionGetOrCreate,
        ConstructorInfo FunctionCtorWithCache, MethodInfo GetSymbolDictionary, FieldInfo SymbolIterator,
        FieldInfo ObjectPrototype, MethodInfo SetPrototype);

    private readonly record struct StringPrototypeRegExpInputs(
        MethodBuilder Match, MethodBuilder MatchAll, MethodBuilder Search, MethodBuilder ReplaceAll, MethodBuilder Split);

    /// <summary>
    /// Emits a static method that populates the <c>String.prototype</c>
    /// singleton dictionary (<see cref="EmittedStringRuntime.PrototypeField"/>)
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
    private void DefineStringPrototypePopulateShell(TypeBuilder typeBuilder, EmittedStringRuntime strings)
    {
        strings.PrototypePopulateMethod = typeBuilder.DefineMethod(
            "_StringPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitStringPrototypePopulate(EmittedStringRuntime strings, StringPrototypeInputs peers, StringPrototypeRegExpInputs? regExp)
    {
        var method = strings.PrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, strings.PrototypeField);

        var strDescLocal = il.DeclareLocal(peers.DescriptorType);

        // ECMA-262 22.1.3 String.prototype.constructor === String. Compiled
        // bare `String` resolves to typeof(string).
        EmitInstallConstructorDescriptor(il, peers.Descriptors, strings.PrototypeField, strDescLocal, setItem, () =>
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
            => EmitWirePrototypeMethodDescriptor(il, peers.Descriptors, peers.FunctionGetOrCreate, strings.PrototypeField, strDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("charAt",         strings.CharAt,         1);
        Wire("charCodeAt",     strings.CharCodeAt,     1);
        Wire("codePointAt",    strings.CodePointAt,    1);
        Wire("substring",      strings.Substring,      2);
        Wire("substr",         strings.Substr,         2);
        // indexOf slot uses the from-variant so wrapper / any-typed receivers
        // routed through the prototype dispatch respect the optional second
        // argument (`pos`). The single-arg StringIndexOf form drops it
        // silently. ECMA-262 22.1.3.10 step 5: ToIntegerOrInfinity(undefined) = 0,
        // which $TSFunction CoercePrimitiveArgs delivers via ToNumber → NaN
        // and StringIndexOfFrom's NaN-clamps-to-0 path.
        Wire("indexOf",        strings.IndexOfFrom,    1);
        Wire("lastIndexOf",    strings.LastIndexOf,    1);
        Wire("toUpperCase",    strings.ToUpperCase,    0);
        Wire("toLowerCase",    strings.ToLowerCase,    0);
        Wire("trim",           strings.Trim,           0);
        Wire("trimStart",      strings.TrimStart,      0);
        Wire("trimEnd",        strings.TrimEnd,        0);
        Wire("replace",        strings.Replace,        2);
        // Use the regex-aware entry point here as well as at direct string
        // call sites. It preserves borrowed primitive receivers until after
        // @@replace dispatch and implements the RegExp/global checks.
        Wire("replaceAll",     regExp?.ReplaceAll, 2);
        // split slot uses the regex-aware + limit-aware proto helper. The basic
        // StringSplit (string,string → list, no limit, no regex) is no longer
        // wired since wrapper / any-typed receivers reach the prototype slot
        // and need both behaviors that match the inline StringEmitter path.
        Wire("split",          regExp?.Split,     2);
        Wire("includes",       strings.Includes,       1);
        Wire("startsWith",     strings.StartsWith,     1);
        Wire("endsWith",       strings.EndsWith,       1);
        Wire("slice",          strings.Slice,          2);
        Wire("repeat",         strings.Repeat,         1);
        Wire("padStart",       strings.PadStart,       1);
        Wire("padEnd",         strings.PadEnd,         1);
        Wire("concat",         strings.Concat,         1);
        Wire("at",             strings.At,             1);
        Wire("normalize",      strings.Normalize,      0);
        Wire("localeCompare",  strings.LocaleCompare,  1);

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
        Wire("match",                regExp?.Match,            1);
        Wire("matchAll",             regExp?.MatchAll,         1);
        Wire("search",               regExp?.Search,           1);
        // Issue #91: spec-correct thisStringValue extraction so wrapper
        // receivers (`new String("x")`) return the underlying primitive
        // and non-string-like receivers throw TypeError per ECMA-262 22.1.3.27.
        Wire("toString",             strings.ProtoToStringHelper,    0);
        Wire("valueOf",              strings.ProtoToStringHelper,    0);
        // ECMA-262 22.1.3.21/22 RequireObjectCoercible(this) is the first step.
        // Wire to the strict StringToLowerCase / StringToUpperCase variants so
        // borrowed-method calls on null/undefined throw TypeError instead of
        // returning empty string. We don't actually localize (no Intl in the
        // standalone DLL), so toLocaleX === toX is acceptable per spec note.
        Wire("toLocaleLowerCase",    strings.ToLowerCase,            0);
        Wire("toLocaleUpperCase",    strings.ToUpperCase,            0);
        Wire("isWellFormed",         strings.IsWellFormed,           0);
        Wire("toWellFormed",         strings.ToWellFormed,           0);

        // ECMA-262 §22.1.3.28: String.prototype[@@iterator] is a real
        // symbol-keyed built-in function named "[Symbol.iterator]".
        var iteratorFnLocal = il.DeclareLocal(_types.Object);
        var iteratorDescLocal = il.DeclareLocal(peers.DescriptorType);
        il.Emit(OpCodes.Ldnull);
        _types.EmitLoadMethodInfo(il, strings.Iterator);
        il.Emit(OpCodes.Ldstr, "[Symbol.iterator]");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, peers.FunctionCtorWithCache);
        il.Emit(OpCodes.Stloc, iteratorFnLocal);
        il.Emit(OpCodes.Newobj, peers.Descriptors.Ctor);
        il.Emit(OpCodes.Stloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldloc, iteratorFnLocal);
        il.Emit(OpCodes.Callvirt, peers.Descriptors.ValueSetter);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, peers.Descriptors.EnumerableSetter);
        il.Emit(OpCodes.Ldsfld, strings.PrototypeField);
        il.Emit(OpCodes.Call, peers.GetSymbolDictionary);
        il.Emit(OpCodes.Ldsfld, peers.SymbolIterator);
        il.Emit(OpCodes.Ldloc, iteratorDescLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        // Per ECMA-262 §22.1.3 String.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, strings.PrototypeField);
        il.Emit(OpCodes.Ldsfld, peers.ObjectPrototype);
        il.Emit(OpCodes.Call, peers.SetPrototype);

        il.Emit(OpCodes.Ret);
    }
}
