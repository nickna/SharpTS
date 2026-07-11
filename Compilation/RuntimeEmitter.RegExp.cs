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
    private void EmitRegExpMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitRegExpCoerceArg(typeBuilder, runtime);
        EmitCreateRegExpWithFlags(typeBuilder, runtime);
        EmitRegExpFromArgs(typeBuilder, runtime);
        EmitRegExpTest(typeBuilder, runtime);
        EmitRegExpExec(typeBuilder, runtime);
        EmitRegExpToString(typeBuilder, runtime);
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
        EmitStringReplaceRegExp(typeBuilder, runtime);
        EmitStringReplaceAllRegExp(typeBuilder, runtime);
        EmitStringSearchRegExp(typeBuilder, runtime);
        EmitStringSplitRegExp(typeBuilder, runtime);
        EmitStringSplitProto(typeBuilder, runtime);
    }

    /// <summary>
    /// Coerces a RegExp constructor argument to its spec-compliant string form.
    /// ECMA-262 22.2.3.1 RegExp(pattern, flags): if either argument is undefined,
    /// substitute "" (the empty string); otherwise invoke the standard
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

        var notNullLabel = il.DefineLabel();
        var notUndefLabel = il.DefineLabel();

        // null → ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNullLabel);

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
    /// Compiled signature: (string str, object pattern, string replacement) -> string.
    /// Dispatches to $RegExp.Replace for global-regex patterns, otherwise falls
    /// back to C#'s String.Replace (full-string all-occurrences semantics).
    /// </summary>
    private void EmitStringReplaceAllRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplaceAllRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object, _types.String]
        );
        runtime.StringReplaceAllRegExp = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var searchLocal = il.DeclareLocal(_types.String);
        var stringPathLabel = il.DefineLabel();
        var patternNullLabel = il.DefineLabel();
        var afterSearchLabel = il.DefineLabel();
        var returnOriginalLabel = il.DefineLabel();

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, stringPathLabel);

        // return regexp.Replace(str, replacement, regexp._global) — Replace
        // walks all matches for global regexes. String.prototype.replace
        // doesn't go through Symbol.replace's spec-aligned flags chain, so
        // pass the typed `_global` field directly (no user PDS override
        // expected through this path).
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Call, _tsRegExpReplaceMethod);
        il.Emit(OpCodes.Ret);

        // String pattern path: search = pattern?.ToString() ?? ""
        il.MarkLabel(stringPathLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, patternNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, afterSearchLabel);
        il.MarkLabel(patternNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(afterSearchLabel);
        il.Emit(OpCodes.Stloc, searchLocal);

        // ECMA-262 22.1.3.20: empty search string inserts replacement at
        // every position 0..length (one between each char + start + end).
        // E.g. "a".replaceAll("","_") → "_a_". Pre-fix returned the original
        // string unchanged. Build manually via StringBuilder.
        var emptySearchLabelRA = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptySearchLabelRA);

        // return Regex.Replace(str, Regex.Escape(search), replacement) so
        // ECMA-262 GetSubstitution Table 53 symbols ($$ → $, $& → matched,
        // $` → pre-match, $' → post-match) are honoured. .NET's static
        // Regex.Replace evaluates these in the replacement string for any
        // regex match (including a literal-string-escaped pattern).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Escape", [_types.String])!);
        il.Emit(OpCodes.Ldarg_2);
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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bgt, loopEndRA);
        il.Emit(OpCodes.Ldloc, sbLocalRA);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        var skipCharRA = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocalRA);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, skipCharRA);
        il.Emit(OpCodes.Ldloc, sbLocalRA);
        il.Emit(OpCodes.Ldarg_0);
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
        il.Emit(OpCodes.Ldarg_0);
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

        // return regexp.Test(input)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpTestMethod);
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

        // var regexp = regex as $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp == null) return null
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        // return regexp.Exec(input)
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpExecMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRegExpToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpToString = method;

        var il = method.GetILGenerator();
        var notRegExpLabel = il.DefineLabel();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);

        // var regexp = regex as $RegExp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp == null) return "/(?:)/"
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, notRegExpLabel);

        // return regexp.ToString()
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpToStringMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notRegExpLabel);
        il.Emit(OpCodes.Ldstr, "/(?:)/");
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
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var globalMatchLabel = il.DefineLabel();
        var globalMatchLabelEntryFromCoerced = il.DefineLabel();
        var searchLocal = il.DeclareLocal(_types.String);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var notFoundLabel = il.DefineLabel();
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
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasMatchesLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasMatchesLabel);

        // MatchAll already returns List<object?>, so hand it straight to the
        // $Array ctor with no intermediate copy. return new $Array(matches)
        il.Emit(OpCodes.Ldloc, matchesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        // String pattern fallback
        il.MarkLabel(isStringPatternLabel);

        // ECMA-262 ToString protocol — handles objects with custom toString.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);

        // var idx = str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("IndexOf", [_types.String])!);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (idx < 0) return null
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notFoundLabel);

        // ECMA-262 22.2.5.4 (and 21.2.5.7 for non-global match) — the result
        // array carries `index` and `input` properties. The plain $Array we
        // returned previously was missing those, so test262 tests that read
        // `m.index` / `m.input` got null. Build the array, then attach the
        // properties via $Runtime.SetProperty so they appear as own props.
        var matchArrayLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, matchArrayLocal);

        // matchArray.index = (double)idx
        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // matchArray.input = str
        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ldstr, "input");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Ldloc, matchArrayLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringMatchAllRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringMatchAll(string str, object? pattern) -> object?
        // Builds $Object match results directly, accessing $RegExp._regex field.
        // Uses index-based iteration (MatchCollection[i]) to avoid try/finally complexity.
        var method = typeBuilder.DefineMethod(
            "StringMatchAllRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]
        );
        runtime.StringMatchAllRegExp = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var regexLocal = il.DeclareLocal(typeof(Regex));

        // var regexp = pattern as $RegExp
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, regexpLocal);

        // if (regexp == null) goto stringPattern
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Brfalse, isStringPatternLabel);

        // if (!regexp.Global) throw TypeError
        var isGlobalLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
        il.Emit(OpCodes.Brtrue, isGlobalLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "String.prototype.matchAll called with a non-global RegExp argument");

        il.MarkLabel(isGlobalLabel);

        // regexLocal = regexp._regex
        il.Emit(OpCodes.Ldloc, regexpLocal);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Stloc, regexLocal);

        var buildResultLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, buildResultLabel);

        // String pattern fallback
        il.MarkLabel(isStringPatternLabel);

        var escapedLocal = il.DeclareLocal(_types.String);
        // ECMA-262 ToString protocol — handles objects with custom toString.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, typeof(Regex).GetMethod("Escape", [_types.String])!);
        il.Emit(OpCodes.Stloc, escapedLocal);

        il.Emit(OpCodes.Ldloc, escapedLocal);
        il.Emit(OpCodes.Newobj, typeof(Regex).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Stloc, regexLocal);

        // Common path: use index-based iteration over MatchCollection
        il.MarkLabel(buildResultLabel);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var matchCollLocal = il.DeclareLocal(typeof(MatchCollection));
        var matchLocal = il.DeclareLocal(typeof(Match));
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var groupIndexLocal = il.DeclareLocal(_types.Int32);
        var groupLocal = il.DeclareLocal(typeof(Group));

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var groupLoopStartLabel = il.DefineLabel();
        var groupLoopEndLabel = il.DefineLabel();

        // var result = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var matchColl = regex.Matches(str)
        il.Emit(OpCodes.Ldloc, regexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Matches", [_types.String])!);
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

        // var fields = new Dictionary<string, object?>()
        var dictSetItem = _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!;
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // fields["0"] = match.Value
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // fields["index"] = (double)match.Index
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // fields["input"] = str (arg_0)
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldstr, "input");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // fields["groups"] = BuildNamedGroups(match)
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldstr, "groups");
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Call, runtime.BuildNamedGroups);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // for (int gi = 1; gi < match.Groups.Count; gi++)
        il.Emit(OpCodes.Ldc_I4_1);
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

        // fields[gi.ToString()] = group.Success ? group.Value : null
        var groupSuccessLabel = il.DefineLabel();
        var groupDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloca, groupIndexLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);

        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, groupSuccessLabel);

        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, groupDoneLabel);

        il.MarkLabel(groupSuccessLabel);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(groupDoneLabel);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        il.Emit(OpCodes.Ldloc, groupIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, groupIndexLocal);
        il.Emit(OpCodes.Br, groupLoopStartLabel);

        il.MarkLabel(groupLoopEndLabel);

        // result.Add(new $Object(fields))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // return new $Array(result)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringReplaceRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringReplace(string str, object? pattern, object replacement) -> string
        // ECMA-262 22.1.3.18: ToString(searchValue) (step 4) happens BEFORE
        // ToString(replaceValue) (step 5). The helper performs both coercions
        // here in this order so a throwing toString on either argument
        // propagates with the correct exception identity.
        var method = typeBuilder.DefineMethod(
            "StringReplaceRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object, _types.Object]
        );
        runtime.StringReplaceRegExp = method;

        var il = method.GetILGenerator();
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("IndexOf", [_types.String])!);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (idx < 0) return str
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notFoundLabel);

        // return str.Substring(0, idx) + replacement + str.Substring(idx + search.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, replacementLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);

        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.StringReplaceWithFunction(string str, object pattern, object func) → string</c>.
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
            [_types.String, _types.Object, _types.Object]);
        runtime.StringReplaceWithFunction = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var stringPatternLabel = il.DefineLabel();
        var notFoundLabel = il.DefineLabel();

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
        il.Emit(OpCodes.Stloc, isGlobalLocal);

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
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String, _types.Int32, _types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        // posLocal = match.Index + match.Length
        il.Emit(OpCodes.Ldloc, matchIdxLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, posLocal);

        // Empty match → advance by 1 to avoid infinite loop
        var advanceDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, advanceDoneLabel);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        // String pattern path
        il.MarkLabel(stringPatternLabel);
        var searchLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);

        // var idx = str.IndexOf(search)
        var idxLocal2 = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("IndexOf", [_types.String])!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, resultStrLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idxLocal2);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);

        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSearchRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSearch(string str, object? pattern) -> double (index or -1)
        var method = typeBuilder.DefineMethod(
            "StringSearchRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Object]
        );
        runtime.StringSearchRegExp = method;

        var il = method.GetILGenerator();
        var regexpLocal = il.DeclareLocal(runtime.TSRegExpType);
        var isStringPatternLabel = il.DefineLabel();
        var searchLocal = il.DeclareLocal(_types.String);

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
        il.Emit(OpCodes.Ret);

        // String pattern fallback — coerce via ECMA-262 ToString protocol so
        // objects with custom toString return the user-defined string.
        il.MarkLabel(isStringPatternLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, searchLocal);

        // return (double)str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("IndexOf", [_types.String])!);
        il.Emit(OpCodes.Conv_R8);
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
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
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
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

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
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, nonEmptySepLabel);

        // Empty separator: split into characters
        // result = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        var charLoopStartLabel = il.DefineLabel();
        var charLoopEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(charLoopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, charLoopEndLabel);

        // result.Add(Char.ToString(str[i]))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

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
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

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
    /// object limit) -&gt; List&lt;object&gt;</c>. Wraps <c>StringSplitRegExp</c>
    /// with the ECMA-262 22.1.3.21 step 6 limit truncation. Receiver coercion
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
            _types.ListOfObject,
            [_types.String, _types.Object, _types.Object]);
        runtime.StringSplitProto = method;

        var il = method.GetILGenerator();

        // result = StringSplitRegExp(str, separator)
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.StringSplitRegExp);
        il.Emit(OpCodes.Stloc, resultLocal);

        // If limit is null or $Undefined, return result as-is.
        var coerceLimitLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, returnResultLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnResultLabel);

        il.MarkLabel(coerceLimitLabel);
        // limitDouble = ToNumber(limit)
        var limitDouble = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, limitDouble);

        // if (!IsFinite(limit)) limitInt = int.MaxValue
        var limitInt = il.DeclareLocal(_types.Int32);
        var notInfLabel = il.DefineLabel();
        var clampDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, notInfLabel);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, limitInt);
        il.Emit(OpCodes.Br, clampDoneLabel);
        il.MarkLabel(notInfLabel);
        il.Emit(OpCodes.Ldloc, limitDouble);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, limitInt);
        il.MarkLabel(clampDoneLabel);

        // if (limitInt < 0) limitInt = 0
        var nonNegLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, limitInt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, nonNegLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, limitInt);
        il.MarkLabel(nonNegLabel);

        // if (limitInt < result.Count) result = result.GetRange(0, limitInt)
        il.Emit(OpCodes.Ldloc, limitInt);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, limitInt);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("GetRange", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
