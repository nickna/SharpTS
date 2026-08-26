using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace SharpTS.Compilation;

/// <summary>
/// RegExp-related runtime emission methods.
/// These are $Runtime wrapper methods that delegate to the emitted $RegExp type.
/// </summary>
public partial class RuntimeEmitter
{
    private LocalBuilder EmitStringSymbolDispatchPreamble(
        ILGenerator il,
        EmittedRuntime runtime,
        FieldBuilder symbol,
        params int[] argumentIndexes)
    {
        var invokedLocal = il.DeclareLocal(_types.Boolean);
        var hasOwnNativeSymbolLocal = il.DeclareLocal(_types.Boolean);
        var resultLocal = il.DeclareLocal(_types.Object);
        var continueLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, symbol);
        il.Emit(OpCodes.Ldc_I4, argumentIndexes.Length);
        il.Emit(OpCodes.Newarr, _types.Object);
        for (var i = 0; i < argumentIndexes.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, argumentIndexes[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldloca, invokedLocal);
        il.Emit(OpCodes.Ldloca, hasOwnNativeSymbolLocal);
        il.Emit(OpCodes.Call, runtime.StringTryInvokeSymbolMethod);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, invokedLocal);
        il.Emit(OpCodes.Brfalse, continueLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(continueLabel);
        return hasOwnNativeSymbolLocal;
    }

    private void EmitRegExpMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitRegExpCoerceArg(typeBuilder, runtime);
        EmitCreateRegExpWithFlags(typeBuilder, runtime);
        EmitRegExpFromArgs(typeBuilder, runtime);
        EmitRegExpTest(typeBuilder, runtime);
        EmitRegExpExec(typeBuilder, runtime);
        EmitRegExpGetSource(typeBuilder, runtime);
        EmitRegExpGetFlags(typeBuilder, runtime);
        EmitRegExpGetGlobal(typeBuilder, runtime);
        EmitRegExpGetIgnoreCase(typeBuilder, runtime);
        EmitRegExpGetMultiline(typeBuilder, runtime);
        runtime.RegExpGetSticky = EmitRegExpGetFlagBool(typeBuilder, runtime, "RegExpGetSticky", 'y');
        runtime.RegExpGetUnicode = EmitRegExpGetFlagBool(typeBuilder, runtime, "RegExpGetUnicode", 'u');
        runtime.RegExpGetDotAll = EmitRegExpGetFlagBool(typeBuilder, runtime, "RegExpGetDotAll", 's');
        runtime.RegExpGetHasIndices = EmitRegExpGetFlagBool(typeBuilder, runtime, "RegExpGetHasIndices", 'd');
        runtime.RegExpGetUnicodeSets = EmitRegExpGetFlagBool(typeBuilder, runtime, "RegExpGetUnicodeSets", 'v');
        EmitRegExpGetLastIndex(typeBuilder, runtime);
        EmitRegExpSetLastIndex(typeBuilder, runtime);
        EmitStringMatchRegExp(typeBuilder, runtime);
        EmitStringMatchAllRegExp(typeBuilder, runtime);
        // WithFunction first: StringReplaceRegExp delegates to it for callable
        // replacements, so its MethodBuilder must be assigned beforehand.
        EmitStringReplaceWithFunction(typeBuilder, runtime);
        EmitStableRegExpReplace(typeBuilder, runtime);
        EmitStringReplaceRegExp(typeBuilder, runtime);
        EmitStringReplaceAllRegExp(typeBuilder, runtime);
        EmitStringSearchRegExp(typeBuilder, runtime);
        EmitStringSplitRegExp(typeBuilder, runtime);
        EmitStringSplitProto(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the allocation-light path for a stable intrinsic regex literal and
    /// primitive replacement string. Substitution tokens retain the complete
    /// RegExp @@replace algorithm; ordinary replacement text can call the typed
    /// regex helper directly without symbol lookup or argument-array creation.
    /// </summary>
    private void EmitStableRegExpReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StableRegExpReplace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, runtime.TSRegExpType, _types.String, _types.Boolean]);
        runtime.StableRegExpReplace = method;

        var il = method.GetILGenerator();
        var ordinaryReplacementLabel = il.DefineLabel();

        // JavaScript substitution tokens need the spec path, particularly
        // $<name>, whose spelling differs from .NET's ${name} syntax.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Brfalse, ordinaryReplacementLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSRegExpSymReplaceHelper);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ordinaryReplacementLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.TSRegExpReplaceMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Coerces a RegExp constructor argument to its spec-compliant string form.
    /// ECMA-262 22.2.3.1 RegExp(pattern, flags): if either argument is undefined,
    /// substitute "" (the empty string); null remains the ordinary JS value
    /// and therefore stringifies to "null"; otherwise invoke the standard
    /// ToString protocol. Without this, `new RegExp(undefined)` would compile
    /// the literal /undefined/ pattern instead of the empty pattern /(?:)/,
    /// failing String.prototype.match Sputnik tests.
    /// </summary>
    private void EmitRegExpCoerceArg(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpCoerceArg",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpCoerceArg = method;

        var il = method.GetILGenerator();

        var notUndefLabel = il.DefineLabel();

        // $Undefined.Instance → ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notUndefLabel);

        // Otherwise: ECMA-262 §7.1.17 ToString. Use ToJsString (not Stringify)
        // so boxed-primitive wrappers (new Object("gi"), Object.assign("gi"))
        // unwrap to their __primitiveValue. Pre-fix Stringify fell through to
        // .ToString() which on a $Object returned "[object Object]"; tests like
        // S15.10.4.1_A8_T9 (`new RegExp(1, new Object("gi"))`) regressed when
        // Object("gi") started returning a wrapper instead of the raw string.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Runtime helper for String.prototype.replaceAll with optional RegExp pattern.
    /// Compiled signature: (object receiver, object pattern, object replacement) -> object.
    /// Dispatches to $RegExp.Replace for global-regex patterns, otherwise falls
    /// back to C#'s String.Replace (full-string all-occurrences semantics).
    /// </summary>
    private void EmitStringReplaceAllRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplaceAllRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.StringReplaceAllRegExp = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var stringLocal = il.DeclareLocal(_types.String);
        var replacementLocal = il.DeclareLocal(_types.String);
        var searchLocal = il.DeclareLocal(_types.String);
        var effectivePatternLocal = il.DeclareLocal(_types.Object);
        var stringCoercionLabel = il.DefineLabel();
        var stringPathLabel = il.DefineLabel();
        var returnOriginalLabel = il.DefineLabel();

        // RequireObjectCoercible(this) precedes every other observable step,
        // but ToString(this) occurs only after a custom @@replace method has
        // had the opportunity to run with the original receiver value.
        var receiverPresentLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, receiverPresentLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.replaceAll called on null or undefined");
        il.MarkLabel(receiverPresentLabel);
        var receiverDefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, receiverDefinedLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.replaceAll called on null or undefined");
        il.MarkLabel(receiverDefinedLabel);

        // IsRegExp(searchValue) requires a global RegExp before @@replace is
        // retrieved. Preserve that observable ordering for native RegExp
        // values, then use the shared object-only GetMethod dispatch.
        var symbolDispatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, symbolDispatchLabel);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Brtrue, symbolDispatchLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.replaceAll called with a non-global RegExp argument");
        il.MarkLabel(symbolDispatchLabel);

        var hasOwnNativeReplaceLocal = EmitStringSymbolDispatchPreamble(il, runtime, runtime.SymbolReplace, 0, 2);

        // No custom @@replace method handled the operation. Only now perform
        // the spec's ToString(O), retaining its abrupt-completion ordering
        // relative to replaceValue coercion.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, stringLocal);

        // For the ordinary string-search fallback, ToString(searchValue)
        // precedes both IsCallable(replaceValue) and ToString(replaceValue).
        // Preserve the already-coerced search string for the functional path
        // as well, so observable coercion occurs exactly once.
        var nativePatternLabel = il.DefineLabel();
        var patternReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, stringCoercionLabel);
        il.Emit(OpCodes.Ldloc, hasOwnNativeReplaceLocal);
        il.Emit(OpCodes.Brfalse, nativePatternLabel);
        il.Emit(OpCodes.Br, stringCoercionLabel);

        il.MarkLabel(stringCoercionLabel);
        // From this point the operation is ordinary string search, even when
        // the original value was a RegExp with an own nullish @@replace.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Stloc, effectivePatternLocal);
        il.Emit(OpCodes.Br, patternReadyLabel);

        il.MarkLabel(nativePatternLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, effectivePatternLocal);
        il.MarkLabel(patternReadyLabel);

        // Only the built-in fallback decides whether replaceValue is callable;
        // custom @@replace methods above receive the original value unchanged.
        var nonCallableReplacementLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, nonCallableReplacementLabel);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, effectivePatternLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.StringReplaceWithFunction);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonCallableReplacementLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replacementLocal);

        // Native patterns retain the RegExp path; string fallback patterns
        // were already coerced above and have a null regexpLocal.
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, stringPathLabel);

        // return regexp.Replace(str, replacement, regexp._global) — Replace
        // walks all matches for global regexes. String.prototype.replace
        // doesn't go through Symbol.replace's spec-aligned flags chain, so
        // pass the typed `_global` field directly (no user PDS override
        // expected through this path).
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Call, _tsRegExpReplaceMethod);
        il.Emit(OpCodes.Ret);

        // String pattern path: search = ToString(pattern). Route values
        // through the full JavaScript
        // coercion protocol so valueOf/toString side effects and abrupt
        // completions are observable.
        // The string-coercion path above performed ToString(searchValue) before
        // replacement coercion. Arrive here with searchLocal ready.
        il.MarkLabel(stringPathLabel);
        // String-search replacement has no capture list or namedCaptures.
        // .NET Regex.Replace would otherwise consume $1..$99 and $<...>
        // tokens using its own capture syntax. Escape just those dollar
        // prefixes while retaining the four substitutions shared with
        // GetSubstitution ($$, $&, $`, $').
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Ldstr, @"\$(?=[0-9<])");
        il.Emit(OpCodes.Ldstr, "$$$$");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, replacementLocal);

        // ECMA-262 22.1.3.20: empty search string inserts replacement at
        // every position 0..length (one between each char + start + end).
        // E.g. "a".replaceAll("","_") → "_a_". Pre-fix returned the original
        // string unchanged. Build manually via StringBuilder.
        var emptySearchLabelRA = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptySearchLabelRA);

        // return Regex.Replace(str, Regex.Escape(search), replacement) so
        // ECMA-262 GetSubstitution Table 53 symbols ($$ → $, $& → matched,
        // $` → pre-match, $' → post-match) are honoured. .NET's static
        // Regex.Replace evaluates these in the replacement string for any
        // regex match (including a literal-string-escaped pattern).
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Escape", [_types.String])!);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptySearchLabelRA);
        // Empty-search padding: emit replacement before/after each char.
        var sbLocalRA = il.DeclareLocal(_types.StringBuilder);
        var iLocalRA = il.DeclareLocal(_types.Int32);
        var loopStartRA = il.DefineLabel();
        var loopEndRA = il.DefineLabel();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocalRA);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocalRA);

        il.MarkLabel(loopStartRA);
        il.Emit(OpCodes.Ldloc, iLocalRA);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bgt, loopEndRA);
        il.Emit(OpCodes.Ldloc, sbLocalRA);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        var skipCharRA = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocalRA);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, skipCharRA);
        il.Emit(OpCodes.Ldloc, sbLocalRA);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, iLocalRA);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipCharRA);
        il.Emit(OpCodes.Ldloc, iLocalRA);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocalRA);
        il.Emit(OpCodes.Br, loopStartRA);
        il.MarkLabel(loopEndRA);
        il.Emit(OpCodes.Ldloc, sbLocalRA);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnOriginalLabel);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateRegExpWithFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateRegExpWithFlags",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.String]
        );
        runtime.CreateRegExpWithFlags = method;

        var il = method.GetILGenerator();
        // return new $RegExp(pattern, flags)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSRegExpCtorPatternFlags);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ECMA-262 §22.2.4.1 RegExp(pattern, flags) — boxed-arg entry point.
    /// Handles the spec branch where pattern is itself a RegExp:
    /// <list type="bullet">
    /// <item>pattern is $RegExp + flags undefined → copy source AND flags.</item>
    /// <item>pattern is $RegExp + flags supplied → copy source, ToString(flags).</item>
    /// <item>pattern is undefined/null → P = ""; F = ToString(flags) or "".</item>
    /// <item>otherwise → P = ToString(pattern); F = ToString(flags) or "".</item>
    /// </list>
    /// The previous EmitNewRegExpConstructor stringified the pattern first
    /// (so `new RegExp(otherRegex)` produced source="/otherSource/" instead
    /// of copying the source slot), which test262's S15.10.4.1_A1_T4.js
    /// caught once \$RegExp surface slots started returning real values.
    /// </summary>
    private void EmitRegExpFromArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpFromArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.RegExpFromArgs = method;

        var il = method.GetILGenerator();
        var srcLocal = il.DeclareLocal(_types.String);
        var flagsLocal = il.DeclareLocal(_types.String);
        var rxLocal = il.DeclareLocal(runtime.TSRegExpType);

        var patternIsRegExpLabel = il.DefineLabel();
        var patternNotRegExpLabel = il.DefineLabel();
        var flagsResolvedLabel = il.DefineLabel();

        // var rx = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brtrue, patternIsRegExpLabel);

        // ECMA-262 §22.2.4.1: a non-RegExp object whose [Symbol.match] is truthy
        // (IsRegExp) is "regexp-like" — read its `source`/`flags` via Get (so
        // getters fire and throw-ordering is source-before-flags) instead of
        // ToString-ing the object to "[object Object]". test262 from-regexp-like*.
        var skipRegexLike = il.DefineLabel();
        var objFlagsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, skipRegexLike);                 // null → not regexp-like
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, skipRegexLike);                  // string → not regexp-like
        il.Emit(OpCodes.Ldarg_0);                                // receiver
        il.Emit(OpCodes.Ldsfld, runtime.SymbolMatch);            // Symbol.match (symbol object)
        il.Emit(OpCodes.Call, runtime.GetIndex);                 // pattern[Symbol.match]
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, skipRegexLike);
        // src = ToJsString(Get(pattern, "source"))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "source");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, srcLocal);
        // flags = arg1 undefined ? ToJsString(Get(pattern,"flags")) : RegExpCoerceArg(arg1)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, objFlagsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, objFlagsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.RegExpCoerceArg);
        il.Emit(OpCodes.Stloc, flagsLocal);
        il.Emit(OpCodes.Br, flagsResolvedLabel);
        il.MarkLabel(objFlagsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, flagsLocal);
        il.Emit(OpCodes.Br, flagsResolvedLabel);
        il.MarkLabel(skipRegexLike);

        // Non-RegExp: src = RegExpCoerceArg(pattern)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.RegExpCoerceArg);
        il.Emit(OpCodes.Stloc, srcLocal);
        il.Emit(OpCodes.Br, patternNotRegExpLabel);

        il.MarkLabel(patternIsRegExpLabel);
        // src = rx.Source
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        il.Emit(OpCodes.Stloc, srcLocal);

        // If flags arg is null/undefined: use rx.Flags
        il.Emit(OpCodes.Ldarg_1);
        var hasFlagsArgLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, useRxFlagsLabelDecl(out var useRxFlagsLabel));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, useRxFlagsLabel);
        // Flags supplied — ToString
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.RegExpCoerceArg);
        il.Emit(OpCodes.Stloc, flagsLocal);
        il.Emit(OpCodes.Br, flagsResolvedLabel);

        il.MarkLabel(useRxFlagsLabel);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Stloc, flagsLocal);
        il.Emit(OpCodes.Br, flagsResolvedLabel);

        il.MarkLabel(patternNotRegExpLabel);
        // flags = RegExpCoerceArg(arg1)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.RegExpCoerceArg);
        il.Emit(OpCodes.Stloc, flagsLocal);

        il.MarkLabel(flagsResolvedLabel);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, flagsLocal);
        il.Emit(OpCodes.Call, runtime.CreateRegExpWithFlags);
        il.Emit(OpCodes.Ret);

        Label useRxFlagsLabelDecl(out Label l)
        {
            l = il.DefineLabel();
            return l;
        }
    }

    private void EmitRegExpTest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpTest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.RegExpTest = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        // var regexp = regex as $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp == null) return false
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        // RegExp.prototype.test delegates to RegExpBuiltinExec. Use the same
        // strict lastIndex write path as exec so a failed global/sticky match
        // throws when lastIndex was made non-writable.
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpExecMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpExec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpExec",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.RegExpExec = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var receiverLocal = il.DeclareLocal(_types.Object);
        var inputLocal = il.DeclareLocal(_types.String);
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, receiverLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, inputLocal);

        // var regexp = regex as $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // A non-$RegExp receiver still participates in the abstract
        // RegExpExec operation: Get(exec), call it with the receiver as this,
        // and validate the returned value. RegExp symbol protocols species-
        // construct precisely these ordinary splitter objects.
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        // return regexp.Exec(input)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpExecMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        EmitRegExpExecSlow(il, runtime, receiverLocal, inputLocal, resultLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpGetSource(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetSource",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpGetSource = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpGetFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetFlags",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpGetFlags = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpGetGlobal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetGlobal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetGlobal = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpGetIgnoreCase(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetIgnoreCase",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetIgnoreCase = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpIgnoreCaseGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpGetMultiline(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetMultiline",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetMultiline = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpMultilineGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a flag-bit getter (sticky/unicode/dotAll/hasIndices/unicodeSets)
    /// that reads the $RegExp flags string and tests for
    /// <paramref name="flagChar"/>. These flags have no dedicated instance
    /// getter on $RegExp (unlike global/ignoreCase/multiline), so the wrapper
    /// derives them from the flags string the same way the prototype
    /// accessors do. Non-RegExp receivers return false, matching the other
    /// RegExpGet* wrappers.
    /// </summary>
    private MethodBuilder EmitRegExpGetFlagBool(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, char flagChar)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Ldc_I4, (int)flagChar);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.Char));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitRegExpGetLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetLastIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.RegExpGetLastIndex = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpSetLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpSetLastIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Double]
        );
        runtime.RegExpSetLastIndex = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexSetter);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringMatchRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringMatch(string str, object? pattern) -> object?
        // If pattern is $RegExp with global flag, return all matches as array
        // If pattern is $RegExp without global flag, return exec result
        // If pattern is string, return simple string match
        var method = typeBuilder.DefineMethod(
            "StringMatchRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]
        );
        runtime.StringMatchRegExp = method;

        var il = method.GetILGenerator();
        EmitStringSymbolDispatchPreamble(il, runtime, runtime.SymbolMatch, 0);
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var globalMatchLabel = il.DefineLabel();
        var globalMatchLabelEntryFromCoerced = il.DefineLabel();
        var matchesLocal = il.DeclareLocal(_types.ListOfObject);

        // ECMA-262 22.1.3.13 String.prototype.match: when pattern is undefined,
        // RegExpCreate coerces to /(?:)/ and the exec path returns the
        // spec-compliant result object with index/input/length. Pre-fix, undefined
        // fell through to the string-fallback which returned a bare [match]
        // array missing index/input/length. Null is NOT special-cased — per spec
        // ToString(null) = "null", so match(null) searches for the literal "null"
        // substring (the string-pattern fallback below handles that correctly).
        var notUndefPatternLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefPatternLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Newobj, runtime.TSRegExpCtorPattern);
        il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Br, globalMatchLabelEntryFromCoerced);
        il.MarkLabel(notUndefPatternLabel);

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp != null)
        il.MarkLabel(globalMatchLabelEntryFromCoerced);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, isStringPatternLabel);

        // if (regexp.Global)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Brtrue, globalMatchLabel);

        // Non-global: return regexp.Exec(str)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpExecMethod);
        il.Emit(OpCodes.Ret);

        // Global match: get all matches and return as array
        il.MarkLabel(globalMatchLabel);

        // var matches = regexp.MatchAll(str)  // List<object?> of full-match substrings
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpMatchAllMethod);
        il.Emit(OpCodes.Stloc, matchesLocal);

        // if (matches.Count == 0) return null
        var hasMatchesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, matchesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasMatchesLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasMatchesLabel);

        // MatchAll already returns List<object?>, so hand it straight to the
        // $Array ctor with no intermediate copy. return new $Array(matches)
        il.Emit(OpCodes.Ldloc, matchesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        // RegExpCreate(pattern, undefined), then Invoke(rx, @@match, « str »).
        // This is observably different from a literal substring search: object
        // patterns are ToString-coerced into regex source, and replacements of
        // RegExp.prototype[@@match] receive the newly-created RegExp.
        il.MarkLabel(isStringPatternLabel);
        var createdMatcherLocal = il.DeclareLocal(_types.Object);
        var createdMethodLocal = il.DeclareLocal(_types.Object);
        var createdArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.RegExpFromArgs);
        il.Emit(OpCodes.Stloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolMatch);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, createdMethodLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, createdArgsLocal);
        il.Emit(OpCodes.Ldloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldloc, createdMethodLocal);
        il.Emit(OpCodes.Ldloc, createdArgsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringMatchAllRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // The public two-argument wrapper enters the full String#matchAll
        // protocol. RegExp.prototype[@@matchAll] calls the prepared core after
        // it has already completed SpeciesConstructor/flags construction; that
        // path must not re-read global/unicode from the constructed matcher.
        var coreMethod = typeBuilder.DefineMethod(
            "StringMatchAllRegExpPrepared",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Boolean, _types.Boolean]
        );
        runtime.StringMatchAllRegExpPrepared = coreMethod;

        var method = typeBuilder.DefineMethod(
            "StringMatchAllRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        method.SetCustomAttribute(runtime.PadUndefinedAttrCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.StringMatchAllRegExp = method;

        var wrapperIL = method.GetILGenerator();
        wrapperIL.Emit(OpCodes.Ldarg_0);
        wrapperIL.Emit(OpCodes.Ldarg_1);
        wrapperIL.Emit(OpCodes.Ldc_I4_0);
        wrapperIL.Emit(OpCodes.Ldc_I4_1);
        wrapperIL.Emit(OpCodes.Call, coreMethod);
        wrapperIL.Emit(OpCodes.Ret);

        // StringMatchAllPrepared(object receiver, object? pattern, bool prepared) -> object?
        // Builds $Object match results directly, accessing $RegExp._regex field.
        // Uses index-based iteration (MatchCollection[i]) to avoid try/finally complexity.
        var il = coreMethod.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var regexLocal = il.DeclareLocal(typeof(Regex));
        var stringLocal = il.DeclareLocal(_types.String);
        var matcherLocal = il.DeclareLocal(_types.Object);
        var matcherFunctionLocal = il.DeclareLocal(runtime.TSFunctionType);
        var matcherArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var symbolRawLocal = il.DeclareLocal(_types.Object);
        var symbolDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var symbolGetterLocal = il.DeclareLocal(_types.Object);
        var sourceLocal = il.DeclareLocal(_types.String);
        var flagsLocal = il.DeclareLocal(_types.String);
        var flagsValueLocal = il.DeclareLocal(_types.Object);
        var patternTypeLocal = il.DeclareLocal(_types.String);
        var patternIsObjectLocal = il.DeclareLocal(_types.Boolean);
        var patternIsRegExpLocal = il.DeclareLocal(_types.Boolean);
        var globalLocal = il.DeclareLocal(_types.Boolean);
        var buildResultLabel = il.DefineLabel();
        var fallbackCreateLabel = il.DefineLabel();

        // RequireObjectCoercible(this).
        var receiverOkLabel = il.DefineLabel();
        var receiverThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, receiverThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, receiverOkLabel);
        il.MarkLabel(receiverThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.matchAll called on null or undefined");
        il.MarkLabel(receiverOkLabel);

        // Preserve the native brand when present. RegExpCreate below replaces
        // this local with the newly-created guest RegExp on the fallback path.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // String.prototype.matchAll's ordinary fallback always creates a
        // global matcher. RegExp.prototype[@@matchAll] supplies the global
        // bit derived from the ORIGINAL receiver's flags; a custom species
        // result may have different flags and must not change that decision.
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, globalLocal);
        var preparedMatcherLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        var notPreparedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notPreparedLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, globalLocal);
        il.Emit(OpCodes.Br, preparedMatcherLabel);
        il.MarkLabel(notPreparedLabel);

        // ES2026 String.prototype.matchAll only performs IsRegExp/GetMethod
        // when regexp is an Object. In particular, primitive Boolean/Number/
        // String/BigInt values must not consult their prototypes' symbol keys.
        var patternClassificationDone = il.DefineLabel();
        var patternIsObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, patternClassificationDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, patternClassificationDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Stloc, patternTypeLocal);
        il.Emit(OpCodes.Ldloc, patternTypeLocal);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, patternIsObject);
        il.Emit(OpCodes.Ldloc, patternTypeLocal);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, patternClassificationDone);
        il.MarkLabel(patternIsObject);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, patternIsObjectLocal);
        il.MarkLabel(patternClassificationDone);

        // IsRegExp(pattern): an explicit @@match value controls the result;
        // otherwise the internal $RegExp brand does. Only regexp-like Objects
        // participate in the mandatory observable global-flags validation.
        var isRegExpReady = il.DefineLabel();
        var useNativeBrand = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, patternIsObjectLocal);
        il.Emit(OpCodes.Brfalse, isRegExpReady);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolMatch);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, matcherLocal);
        EmitObserveRegExpPrototypeOverride(runtime.SymbolMatch, runtime.TSRegExpSymMatchHelper,
            loadReceiver: () => il.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Ldloc, matcherLocal);
        il.Emit(OpCodes.Brfalse, useNativeBrand);
        il.Emit(OpCodes.Ldloc, matcherLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, useNativeBrand);
        il.Emit(OpCodes.Ldloc, matcherLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, patternIsRegExpLocal);
        il.Emit(OpCodes.Br, isRegExpReady);
        il.MarkLabel(useNativeBrand);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, patternIsRegExpLocal);
        il.MarkLabel(isRegExpReady);

        var flagsValidated = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, patternIsRegExpLocal);
        il.Emit(OpCodes.Brfalse, flagsValidated);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, flagsValueLocal);

        // The compiled RegExp property fast path intentionally handles its
        // intrinsic accessors directly. String#matchAll additionally needs to
        // observe a user replacement of RegExp.prototype.flags, without
        // perturbing the other RegExp algorithms that depend on that proven
        // fast path. An own flags descriptor already won in GetProperty above.
        var flagsOverrideDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, flagsOverrideDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, flagsOverrideDone);

        il.Emit(OpCodes.Call, runtime.RegExpPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Brfalse, flagsOverrideDone);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, symbolGetterLocal);
        var flagsDataDescriptor = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Brfalse, flagsDataDescriptor);

        // Leave the intrinsic getter on the existing fast path; only invoke a
        // genuinely replaced accessor with the original RegExp as `this`.
        var invokeFlagsGetter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, matcherFunctionLocal);
        il.Emit(OpCodes.Ldloc, matcherFunctionLocal);
        il.Emit(OpCodes.Brfalse, invokeFlagsGetter);
        il.Emit(OpCodes.Ldloc, matcherFunctionLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionGetMethodInfo);
        _types.EmitLoadMethodInfo(il, runtime.TSRegExpProtoGetFlags);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brtrue, flagsOverrideDone);
        il.MarkLabel(invokeFlagsGetter);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, symbolGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, flagsValueLocal);
        il.Emit(OpCodes.Br, flagsOverrideDone);

        il.MarkLabel(flagsDataDescriptor);
        var flagsDataValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, flagsDataValue);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, flagsValueLocal);
        il.Emit(OpCodes.Br, flagsOverrideDone);
        il.MarkLabel(flagsDataValue);
        il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, flagsValueLocal);
        il.MarkLabel(flagsOverrideDone);

        il.Emit(OpCodes.Ldloc, flagsValueLocal);
        il.Emit(OpCodes.Dup);
        var flagsPresent = il.DefineLabel();
        var flagsThrow = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, flagsThrow);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, flagsPresent);
        il.MarkLabel(flagsThrow);
        il.Emit(OpCodes.Pop);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp flags are null or undefined");
        il.MarkLabel(flagsPresent);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, flagsLocal);
        il.Emit(OpCodes.Ldloc, flagsLocal);
        il.Emit(OpCodes.Ldstr, "g");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.String));
        var isGlobal = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, isGlobal);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.matchAll called with a non-global RegExp argument");
        il.MarkLabel(isGlobal);
        il.MarkLabel(flagsValidated);

        // GetMethod(pattern, @@matchAll). A missing method falls through to
        // RegExpCreate. The intrinsic helper is retained on the rich-result
        // fast path; every user override is invoked with the original receiver
        // value (before ToString(this), as required by the observable order).
        il.Emit(OpCodes.Ldloc, patternIsObjectLocal);
        il.Emit(OpCodes.Brfalse, fallbackCreateLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolMatchAll);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, matcherLocal);
        EmitObserveRegExpPrototypeOverride(runtime.SymbolMatchAll, runtime.TSRegExpSymMatchAllHelper,
            loadReceiver: () => il.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Ldloc, matcherLocal);
        il.Emit(OpCodes.Brfalse, fallbackCreateLabel);
        il.Emit(OpCodes.Ldloc, matcherLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, fallbackCreateLabel);
        var standardOriginalMatcher = il.DefineLabel();
        EmitInvokeMatchAllUnlessStandard(standardOriginalMatcher, loadReceiver: () => il.Emit(OpCodes.Ldarg_1),
            loadArgument: () => il.Emit(OpCodes.Ldarg_0));
        il.MarkLabel(standardOriginalMatcher);

        // The standard intrinsic can only take this fast path for a genuinely
        // native receiver. Assigning it to an ordinary object must still call
        // it and let the RegExp receiver guard throw.
        var standardReceiverOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brtrue, standardReceiverOk);
        EmitInvokeCurrentMatcher(loadReceiver: () => il.Emit(OpCodes.Ldarg_1),
            loadArgument: () => il.Emit(OpCodes.Ldarg_0));
        il.MarkLabel(standardReceiverOk);
        il.MarkLabel(preparedMatcherLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, stringLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Stloc, regexLocal);
        il.Emit(OpCodes.Br, buildResultLabel);

        // S = ToString(O), then rx = RegExpCreate(pattern, "g"). Undefined is
        // the empty pattern; a native RegExp contributes its source; every
        // other value follows ToString. Construct a real guest $RegExp so an
        // overridden RegExp.prototype[@@matchAll] observes the correct `this`.
        il.MarkLabel(fallbackCreateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, stringLocal);
        var sourceReadyLabel = il.DefineLabel();
        var sourceCoerceLabel = il.DefineLabel();
        var sourceUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, sourceUndefinedLabel);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        il.Emit(OpCodes.Stloc, sourceLocal);
        il.Emit(OpCodes.Br, sourceReadyLabel);
        il.MarkLabel(sourceUndefinedLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, sourceCoerceLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, sourceLocal);
        il.Emit(OpCodes.Br, sourceReadyLabel);
        il.MarkLabel(sourceCoerceLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, sourceLocal);
        il.MarkLabel(sourceReadyLabel);
        il.Emit(OpCodes.Ldloc, sourceLocal);
        il.Emit(OpCodes.Ldstr, "g");
        il.Emit(OpCodes.Newobj, runtime.TSRegExpCtorPatternFlags);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Stloc, regexLocal);

        // Invoke(rx, @@matchAll, « S »). GetIndex now walks the actual
        // RegExp.prototype symbol dictionary, so prototype overrides win while
        // the intrinsic helper remains eligible for the rich-result fast path.
        EmitResolveRegExpPrototypeSymbol(runtime.SymbolMatchAll,
            loadReceiver: () => il.Emit(OpCodes.Ldloc, regexpLocal));
        EmitInvokeMatchAllUnlessStandard(buildResultLabel,
            loadReceiver: () => il.Emit(OpCodes.Ldloc, regexpLocal),
            loadArgument: () => il.Emit(OpCodes.Ldloc, stringLocal));

        // Common path: use index-based iteration over MatchCollection
        il.MarkLabel(buildResultLabel);

        void EmitInvokeMatchAllUnlessStandard(
            Label standardLabel,
            Action loadReceiver,
            Action loadArgument)
        {
            var invokeLabel = il.DefineLabel();
            EmitBranchIfMatcherWraps(runtime.TSRegExpSymMatchAllHelper, standardLabel);
            il.Emit(OpCodes.Br, invokeLabel);
            il.MarkLabel(invokeLabel);
            EmitInvokeCurrentMatcher(loadReceiver, loadArgument);
        }

        void EmitBranchIfMatcherWraps(MethodBuilder helper, Label matchLabel)
        {
            var notFunctionLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, matcherLocal);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Stloc, matcherFunctionLocal);
            il.Emit(OpCodes.Ldloc, matcherFunctionLocal);
            il.Emit(OpCodes.Brfalse, notFunctionLabel);
            il.Emit(OpCodes.Ldloc, matcherFunctionLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionGetMethodInfo);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
            il.Emit(OpCodes.Brtrue, matchLabel);
            il.MarkLabel(notFunctionLabel);
        }

        void EmitObserveRegExpPrototypeOverride(
            FieldBuilder symbol,
            MethodBuilder intrinsic,
            Action loadReceiver)
        {
            var doneLabel = il.DefineLabel();
            var intrinsicLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, regexpLocal);
            il.Emit(OpCodes.Brfalse, doneLabel);
            EmitBranchIfMatcherWraps(intrinsic, intrinsicLabel);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(intrinsicLabel);
            // An own symbol value wins even when it happens to be the intrinsic
            // function itself. Only an inherited synthesized intrinsic should
            // be replaced by the current RegExp.prototype descriptor.
            loadReceiver();
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, symbolDictLocal);
            il.Emit(OpCodes.Ldloc, symbolDictLocal);
            il.Emit(OpCodes.Ldsfld, symbol);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
            il.Emit(OpCodes.Brtrue, doneLabel);
            EmitResolveRegExpPrototypeSymbol(symbol, loadReceiver);
            il.MarkLabel(doneLabel);
        }

        void EmitResolveRegExpPrototypeSymbol(FieldBuilder symbol, Action loadReceiver)
        {
            il.Emit(OpCodes.Call, runtime.RegExpPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
            il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
            il.Emit(OpCodes.Stloc, symbolDictLocal);
            il.Emit(OpCodes.Ldloc, symbolDictLocal);
            il.Emit(OpCodes.Ldsfld, symbol);
            il.Emit(OpCodes.Ldloca, symbolRawLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
            var foundLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, foundLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Stloc, matcherLocal);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(foundLabel);
            il.Emit(OpCodes.Ldloc, symbolRawLocal);
            il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Stloc, symbolDescriptorLocal);
            var rawValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
            il.Emit(OpCodes.Brfalse, rawValueLabel);
            il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, symbolGetterLocal);
            var dataDescriptorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symbolGetterLocal);
            il.Emit(OpCodes.Brfalse, dataDescriptorLabel);
            loadReceiver();
            il.Emit(OpCodes.Ldloc, symbolGetterLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, matcherLocal);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(dataDescriptorLabel);
            var descriptorValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, descriptorValueLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Stloc, matcherLocal);
            il.Emit(OpCodes.Br, doneLabel);
            il.MarkLabel(descriptorValueLabel);
            il.Emit(OpCodes.Ldloc, symbolDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, matcherLocal);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(rawValueLabel);
            il.Emit(OpCodes.Ldloc, symbolRawLocal);
            il.Emit(OpCodes.Stloc, matcherLocal);
            il.MarkLabel(doneLabel);
        }

        void EmitInvokeCurrentMatcher(Action loadReceiver, Action loadArgument)
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            loadArgument();
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Stloc, matcherArgsLocal);
            loadReceiver();
            il.Emit(OpCodes.Ldloc, matcherLocal);
            il.Emit(OpCodes.Ldloc, matcherArgsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Ret);
        }

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var matchCollLocal = il.DeclareLocal(typeof(MatchCollection));
        var matchLocal = il.DeclareLocal(typeof(Match));
        var matchElementsLocal = il.DeclareLocal(_types.ListOfObject);
        var matchArrayLocal = il.DeclareLocal(runtime.TSArrayType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var groupIndexLocal = il.DeclareLocal(_types.Int32);
        var groupLocal = il.DeclareLocal(typeof(Group));
        var startIndexLocal = il.DeclareLocal(_types.Int32);

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var groupLoopStartLabel = il.DefineLabel();
        var groupLoopEndLabel = il.DefineLabel();

        // var result = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Start from the matcher lastIndex captured by @@matchAll. The matcher
        // is a fresh intrinsic/custom-species RegExp, so reading it here does
        // not re-read the original receiver whose value was already cached.
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, startIndexLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var startReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, startReadyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startIndexLocal);
        il.MarkLabel(startReadyLabel);

        // var matchColl = regex.Matches(str, startIndex)
        il.Emit(OpCodes.Ldloc, regexLocal);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Matches", [_types.String, _types.Int32])!);
        il.Emit(OpCodes.Stloc, matchCollLocal);

        // var count = matchColl.Count
        il.Emit(OpCodes.Ldloc, matchCollLocal);
        il.Emit(OpCodes.Callvirt, typeof(MatchCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // for (int i = 0; i < count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // var match = matchColl[i]
        il.Emit(OpCodes.Ldloc, matchCollLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(MatchCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, matchLocal);

        // A RegExp match result is an Array exotic object, not a plain object.
        // Populate elements 0..n from the CLR GroupCollection, then attach the
        // non-index `index`, `input`, and `groups` properties through ordinary
        // Set so Array.prototype methods and Test262's compareArray both work.
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, matchElementsLocal);

        // for (int gi = 0; gi < match.Groups.Count; gi++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, groupIndexLocal);

        il.MarkLabel(groupLoopStartLabel);
        il.Emit(OpCodes.Ldloc, groupIndexLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, groupLoopEndLabel);

        // var group = match.Groups[gi]
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, groupIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, groupLocal);

        // matchElements.Add(group.Success ? group.Value : undefined)
        var groupMissingLabel = il.DefineLabel();
        var groupDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, matchElementsLocal);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, groupMissingLabel);

        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, groupDoneLabel);

        il.MarkLabel(groupMissingLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);

        il.MarkLabel(groupDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, groupIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, groupIndexLocal);
        il.Emit(OpCodes.Br, groupLoopStartLabel);

        il.MarkLabel(groupLoopEndLabel);

        il.Emit(OpCodes.Ldloc, matchElementsLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, matchArrayLocal);

        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ldstr, "input");
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ldstr, "groups");
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Call, runtime.BuildNamedGroups);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // result.Add(matchArray)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Ldloc, globalLocal);
        il.Emit(OpCodes.Brfalse, loopEndLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // %RegExpStringIteratorPrototype% is represented by the runtime's
        // stateful IEnumerator<object> bridge. It supports next(), for-of,
        // spread, and Array.from without exposing Array-only properties.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringReplaceRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringReplace(string str, object? pattern, object replacement) -> object
        // ECMA-262 22.1.3.18: ToString(searchValue) (step 4) happens BEFORE
        // ToString(replaceValue) (step 5). The helper performs both coercions
        // here in this order so a throwing toString on either argument
        // propagates with the correct exception identity.
        var method = typeBuilder.DefineMethod(
            "StringReplaceRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]
        );
        runtime.StringReplaceRegExp = method;

        var il = method.GetILGenerator();
        EmitStringSymbolDispatchPreamble(il, runtime, runtime.SymbolReplace, 0, 2);
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var searchLocal = il.DeclareLocal(_types.String);
        var replacementLocal = il.DeclareLocal(_types.String);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var notFoundLabel = il.DefineLabel();

        // ECMA-262 22.1.3.18 step 3: when replaceValue is callable, the
        // per-match substitution invokes it with (matched, ...captures, position,
        // string) rather than ToString-coercing it. StringReplaceWithFunction
        // implements that for both regex and string patterns. Without this the
        // function was ToJsString'd to "function () {...}" and spliced in
        // literally (e.g. `s.replace(/re/, fn)` yielded "[Function] ..."). The
        // typed string path already routes callables correctly; this covers the
        // any-typed dynamic path that Test262 .js sources take.
        var notCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.StringReplaceWithFunction);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notCallableLabel);

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp != null)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, isStringPatternLabel);

        // RegExp pattern: ToJsString the replacement, then call regexp.Replace.
        // Pass typed `_global` directly — String.prototype.replace doesn't
        // observe user PDS overrides on `r.global`.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replacementLocal);

        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Call, _tsRegExpReplaceMethod);
        il.Emit(OpCodes.Ret);

        // String pattern fallback
        il.MarkLabel(isStringPatternLabel);

        // Step 4: ToJsString(searchValue) FIRST.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);

        // Step 5: ToJsString(replaceValue) AFTER the search has been coerced.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replacementLocal);

        // var idx = str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", [_types.String])!);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (idx < 0) return str
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notFoundLabel);

        // return str.Substring(0, idx) + replacement + str.Substring(idx + search.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, replacementLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.StringReplaceWithFunction(string str, object pattern, object func, bool replaceAll) → string</c>.
    /// ECMA-262 22.1.3.18 step 3: when replaceValue is callable, the per-match
    /// substitution is `Call(func, undefined, [matched, ..., position, str])`.
    /// We pass `(matched, position, str)` for the string-pattern case (no
    /// captures), and the proper [m, c1, c2, ..., position, str] for the
    /// regex-pattern case.
    /// </summary>
    private void EmitStringReplaceWithFunction(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplaceWithFunction",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object, _types.Object, _types.Boolean]);
        runtime.StringReplaceWithFunction = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var searchLocal = il.DeclareLocal(_types.String);
        var stringPatternLabel = il.DefineLabel();
        var notFoundLabel = il.DefineLabel();
        var regexLoopSetupLabel = il.DefineLabel();

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, stringPatternLabel);

        // RegExp path: use the underlying System.Text.RegularExpressions.Regex
        // with a MatchEvaluator delegate. We can't easily emit a delegate, so
        // fall back to a manual loop.
        var regexLocal = il.DeclareLocal(typeof(Regex));
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Stloc, regexLocal);

        // Track Global flag — without it, only the first match is replaced.
        var isGlobalLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, isGlobalLocal);

        il.MarkLabel(regexLoopSetupLabel);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        var posLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, posLocal);

        var matchLocal = il.DeclareLocal(typeof(Match));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // match = regex.Match(str, posLocal)
        il.Emit(OpCodes.Ldloc, regexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Match", [_types.String, _types.Int32])!);
        il.Emit(OpCodes.Stloc, matchLocal);

        // if (!match.Success) break
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // sb.Append(str, posLocal, match.Index - posLocal)
        var matchIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, matchIdxLocal);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Ldloc, matchIdxLocal);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Pop);

        // ECMA-262 22.1.3.18 functional replacement args:
        //   [matched, p1, p2, ..., pN, position, string]
        // where pK is the K-th capture group (or undefined if not matched).
        // .NET Regex's Match.Groups[0] is the whole match; Groups[1..N] are
        // captures. Args length = Groups.Count + 2.
        //
        // Without capture-group forwarding the npm `debug` formatter (which
        // does `format.replace(re, function(match, format){...})`) gets `format`
        // bound to the position number rather than the captured group string.
        var groupsLocal = il.DeclareLocal(typeof(GroupCollection));
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, groupsLocal);

        var groupCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, groupsLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, groupCountLocal);

        var argsArrLocal = il.DeclareLocal(_types.ObjectArray);
        // new object[groupCount + 2]
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsArrLocal);

        // for (int gi = 0; gi < groupCount; gi++)
        //   args[gi] = groups[gi].Success ? groups[gi].Value : $Undefined.Instance;
        var gIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, gIdxLocal);

        var groupLoopStart = il.DefineLabel();
        var groupLoopEnd = il.DefineLabel();
        il.MarkLabel(groupLoopStart);
        il.Emit(OpCodes.Ldloc, gIdxLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Bge, groupLoopEnd);

        // group = groups[gi]
        var groupLocal = il.DeclareLocal(typeof(Group));
        il.Emit(OpCodes.Ldloc, groupsLocal);
        il.Emit(OpCodes.Ldloc, gIdxLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, groupLocal);

        // args[gi] = group.Success ? group.Value : $Undefined.Instance
        il.Emit(OpCodes.Ldloc, argsArrLocal);
        il.Emit(OpCodes.Ldloc, gIdxLocal);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        var groupSuccessLabel = il.DefineLabel();
        var groupStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, groupSuccessLabel);
        // !Success: load undefined
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, groupStoreLabel);
        il.MarkLabel(groupSuccessLabel);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.MarkLabel(groupStoreLabel);
        il.Emit(OpCodes.Stelem_Ref);

        // gi++
        il.Emit(OpCodes.Ldloc, gIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, gIdxLocal);
        il.Emit(OpCodes.Br, groupLoopStart);

        il.MarkLabel(groupLoopEnd);

        // args[groupCount] = (double)matchIdx
        il.Emit(OpCodes.Ldloc, argsArrLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldloc, matchIdxLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // args[groupCount + 1] = str
        il.Emit(OpCodes.Ldloc, argsArrLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // result = $Runtime.InvokeMethodValue(undefined, func, args)
        var resultObjLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, argsArrLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, resultObjLocal);

        // sb.Append(ToJsString(result))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, resultObjLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        // posLocal = match.Index + match.Length
        il.Emit(OpCodes.Ldloc, matchIdxLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, posLocal);

        // Empty match → retain the source code unit while advancing. At the
        // terminal empty match, finish after the replacement to avoid
        // re-entering Regex.Match at the same end position.
        var advanceDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, advanceDoneLabel);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, posLocal);
        il.MarkLabel(advanceDoneLabel);

        // Stop after first match if !global
        il.Emit(OpCodes.Ldloc, isGlobalLocal);
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // sb.Append(str.Substring(posLocal))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, posLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        // String pattern path
        il.MarkLabel(stringPatternLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);

        // replaceAll with a string search reuses the regex loop with an escaped
        // literal pattern. That loop supplies the required callback arguments
        // and invokes the replacer once per match position.
        var replaceFirstStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, replaceFirstStringLabel);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Call, typeof(Regex).GetMethod("Escape", [_types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(Regex), _types.String));
        il.Emit(OpCodes.Stloc, regexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isGlobalLocal);
        il.Emit(OpCodes.Br, regexLoopSetupLabel);
        il.MarkLabel(replaceFirstStringLabel);

        // var idx = str.IndexOf(search)
        var idxLocal2 = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", [_types.String])!);
        il.Emit(OpCodes.Stloc, idxLocal2);

        // if (idx < 0) return str
        il.Emit(OpCodes.Ldloc, idxLocal2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notFoundLabel);

        // Call func(matched, idx, str)
        var argsStrLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, idxLocal2);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsStrLocal);

        var resultStrLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, argsStrLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, resultStrLocal);

        // return prefix + ToJsString(result) + suffix
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, idxLocal2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, resultStrLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idxLocal2);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSearchRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSearch(string str, object? pattern) -> object (index or a
        // custom @@search return value)
        var method = typeBuilder.DefineMethod(
            "StringSearchRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]
        );
        runtime.StringSearchRegExp = method;

        var il = method.GetILGenerator();
        EmitStringSymbolDispatchPreamble(il, runtime, runtime.SymbolSearch, 0);
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp != null)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, isStringPatternLabel);

        // return (double)regexp.Search(str)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpSearchMethod);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // RegExpCreate(pattern, undefined), then Invoke(rx, @@search, « str »).
        il.MarkLabel(isStringPatternLabel);
        var createdMatcherLocal = il.DeclareLocal(_types.Object);
        var createdMethodLocal = il.DeclareLocal(_types.Object);
        var createdArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.RegExpFromArgs);
        il.Emit(OpCodes.Stloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolSearch);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, createdMethodLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, createdArgsLocal);
        il.Emit(OpCodes.Ldloc, createdMatcherLocal);
        il.Emit(OpCodes.Ldloc, createdMethodLocal);
        il.Emit(OpCodes.Ldloc, createdArgsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSplitRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSplit(string str, object? separator) -> List<object?>
        var method = typeBuilder.DefineMethod(
            "StringSplitRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.String, _types.Object]
        );
        runtime.StringSplitRegExp = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var sepLocal = il.DeclareLocal(_types.String);
        var partsLocal = il.DeclareLocal(typeof(string[]));
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // ECMA-262 22.1.3.21 step 4: if separator is undefined, return [str].
        // Note: only $Undefined.Instance — null is a distinct JS value that
        // ToString-coerces to "null" per ECMA-262 7.1.17 ToString. Previously
        // null also short-circuited here, causing `"anullb".split(null)` to
        // return `["anullb"]` instead of the spec-correct `["a", "b"]`. The
        // no-args dispatch sites (StringEmitter.EmitSplit /
        // ILEmitter.Calls.StringMethods.cs split case) now push
        // UndefinedInstance instead of "" when no separator was provided, so
        // that the spec-correct undefined-arm fires through this branch.
        var sepNotUndefSingletonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, sepNotUndefSingletonLabel);
        // separator is $Undefined: return [str]
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(sepNotUndefSingletonLabel);

        // var regexp = separator as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp != null)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, isStringPatternLabel);

        // var parts = regexp.Split(str)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpSplitMethod);
        il.Emit(OpCodes.Stloc, partsLocal);

        // Convert to List<object?>
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEndLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // String pattern fallback — coerce via ECMA-262 ToString protocol.
        il.MarkLabel(isStringPatternLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, sepLocal);

        // Handle empty separator: split into characters
        var nonEmptySepLabel = il.DefineLabel();
        var splitDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sepLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, nonEmptySepLabel);

        // Empty separator: split into characters
        // result = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        var charLoopStartLabel = il.DefineLabel();
        var charLoopEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(charLoopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, charLoopEndLabel);

        // result.Add(Char.ToString(str[i]))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, charLoopStartLabel);

        il.MarkLabel(charLoopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Non-empty separator: use string.Split
        il.MarkLabel(nonEmptySepLabel);

        // parts = str.Split(sep, StringSplitOptions.None)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, sepLocal);
        il.Emit(OpCodes.Ldc_I4, (int)StringSplitOptions.None);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Split", _types.String, _types.StringSplitOptions));
        il.Emit(OpCodes.Stloc, partsLocal);

        // Convert to List<object?>
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        var strLoopStartLabel = il.DefineLabel();
        var strLoopEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(strLoopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, strLoopEndLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, strLoopStartLabel);

        il.MarkLabel(strLoopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// String.prototype.split slot helper: <c>(string str, object separator,
    /// object limit) -&gt; object</c>. Implements the observable @@split dispatch
    /// and ECMA-262 22.1.3.21 limit coercion order. Receiver coercion
    /// (wrapper → primitive) is handled upstream by <c>$TSFunction.CoercePrimitiveArgs</c>
    /// via the <c>__this</c> param-name convention. Mirrors the inline trim
    /// logic in <c>StringEmitter.EmitSplit</c> so prototype-slot dispatch (used
    /// for wrapper / any-typed receivers) matches the typed fast path.
    /// </summary>
    private void EmitStringSplitProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSplitProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        // Unlike most built-ins, split must distinguish an omitted limit from
        // explicit null. Reuse the JS undefined-padding marker so reflective
        // prototype calls retain that distinction.
        method.SetCustomAttribute(runtime.PadUndefinedAttrCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.StringSplitProto = method;

        var il = method.GetILGenerator();

        // RequireObjectCoercible(this) precedes even @@split lookup.
        var receiverOkLabel = il.DefineLabel();
        var receiverThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, receiverThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, receiverOkLabel);
        il.MarkLabel(receiverThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.split called on null or undefined");
        il.MarkLabel(receiverOkLabel);

        // GetMethod(separator, @@split) precedes ToString(this) and limit
        // coercion. A custom method receives the original string and limit and
        // may return any value.
        EmitStringSymbolDispatchPreamble(il, runtime, runtime.SymbolSplit, 0, 2);

        var stringLocal = il.DeclareLocal(_types.String);
        var separatorLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var limitDouble = il.DeclareLocal(_types.Double);
        var numberLocal = il.DeclareLocal(_types.Double);
        var defaultLimitLabel = il.DefineLabel();
        var coerceLimitLabel = il.DefineLabel();
        var zeroLimitLabel = il.DefineLabel();
        var normalizeModuloLabel = il.DefineLabel();
        var limitReadyLabel = il.DefineLabel();
        var nonNegativeLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // Only the built-in fallback coerces the receiver. A custom @@split
        // above receives the original value and can return without observing
        // receiver.toString.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, stringLocal);

        // The inherited native RegExp @@split is intentionally skipped by the
        // generic dispatch preamble; route it into the full protocol helper
        // now, preserving the original limit value and its observable order.
        var nonRegExpSeparatorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Brfalse, nonRegExpSeparatorLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSRegExpSymSplitHelper);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonRegExpSeparatorLabel);

        // lim = limit === undefined ? 2^32-1 : ToUint32(limit).
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, coerceLimitLabel);

        il.MarkLabel(defaultLimitLabel);
        il.Emit(OpCodes.Ldc_R8, 4294967295.0);
        il.Emit(OpCodes.Stloc, limitDouble);
        il.Emit(OpCodes.Br, limitReadyLabel);

        il.MarkLabel(coerceLimitLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, numberLocal);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, zeroLimitLabel);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, zeroLimitLabel);
        il.MarkLabel(normalizeModuloLabel);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Truncate", _types.Double));
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, limitDouble);
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bge, nonNegativeLabel);
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, limitDouble);
        il.Emit(OpCodes.Br, nonNegativeLabel);

        il.MarkLabel(zeroLimitLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, limitDouble);
        il.MarkLabel(nonNegativeLabel);
        il.MarkLabel(limitReadyLabel);

        // For the built-in string algorithm, ToString(separator) precedes the
        // lim==0 bailout. Undefined remains the sentinel handled by
        // StringSplitRegExp's [S] arm.
        var separatorReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var coerceSeparatorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, coerceSeparatorLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, separatorLocal);
        il.Emit(OpCodes.Br, separatorReadyLabel);
        il.MarkLabel(coerceSeparatorLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, separatorLocal);
        il.MarkLabel(separatorReadyLabel);

        // A zero limit bails out before separator ToString, but only after a
        // custom @@split had its chance above.
        var performSplitLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, performSplitLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(performSplitLabel);
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldloc, separatorLocal);
        il.Emit(OpCodes.Call, runtime.StringSplitRegExp);
        il.Emit(OpCodes.Stloc, resultLocal);

        // The list count is bounded by Int32, while lim retains the full
        // uint32 range, so compare as doubles and convert only when trimming.
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Bge, returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
