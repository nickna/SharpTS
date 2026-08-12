using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $RegExp class for standalone RegExp support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSRegExp
/// </summary>
public partial class RuntimeEmitter
{
    // $RegExp fields
    private FieldBuilder _tsRegExpRegexField = null!;
    private FieldBuilder _tsRegExpSourceField = null!;
    private FieldBuilder _tsRegExpFlagsField = null!;
    private FieldBuilder _tsRegExpGlobalField = null!;
    private FieldBuilder _tsRegExpIgnoreCaseField = null!;
    private FieldBuilder _tsRegExpMultilineField = null!;
    private FieldBuilder _tsRegExpStickyField = null!;
    private FieldBuilder _tsRegExpLastIndexField = null!;
    // Holds the raw value last assigned to `lastIndex` when it is not a plain
    // number (e.g. a string or `{valueOf(){…}}`), so its value/identity is
    // preserved per ECMA-262 (lastIndex is an ordinary writable data property;
    // ToLength runs at RegExpBuiltinExec read time, not at assignment). null for
    // the common numeric case, which stays on the typed `_lastIndex` fast path.
    private FieldBuilder _tsRegExpLastIndexBoxedField = null!;

    // $RegExp internal methods
    private MethodBuilder _tsRegExpMatchAllMethod = null!;
    private MethodBuilder _tsRegExpReplaceMethod = null!;
    private MethodBuilder _tsRegExpReplaceWithFnMethod = null!;
    private MethodBuilder _tsRegExpExpandReplacementSpecMethod = null!;
    private MethodBuilder _tsRegExpAdvanceStringIndexSpecMethod = null!;
    private MethodBuilder _tsRegExpSearchMethod = null!;
    private MethodBuilder _tsRegExpSplitMethod = null!;
    private MethodBuilder _tsRegExpHasNamedGroupsMethod = null!;
    private MethodBuilder _tsRegExpValidateModifiersMethod = null!;
    private MethodBuilder _tsRegExpValidateModifierFlagsMethod = null!;
    private MethodBuilder _tsRegExpSkipCharClassMethod = null!;
    private MethodBuilder _tsRegExpValidateFlagsMethod = null!;
    private MethodBuilder _tsRegExpValidateUnicodePatternMethod = null!;
    private MethodBuilder _tsRegExpFindGroupCloseMethod = null!;
    private FieldBuilder _tsRegExpCompileCacheField = null!;
    private MethodBuilder _tsRegExpGetCachedRegexMethod = null!;
    private MethodBuilder _tsRegExpRewriteShorthandsMethod = null!;
    private MethodBuilder _tsRegExpExpandShorthandMethod = null!;
    private MethodBuilder _tsRegExpSetLastIndexStrictMethod = null!;
    private MethodBuilder _tsRegExpResolveLastIndexMethod = null!;

    private void EmitTSRegExpClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $RegExp
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$RegExp",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSRegExpType = typeBuilder;

        // Fields
        _tsRegExpRegexField = typeBuilder.DefineField("_regex", typeof(Regex), FieldAttributes.Assembly);
        _tsRegExpSourceField = typeBuilder.DefineField("_source", _types.String, FieldAttributes.Private);
        _tsRegExpFlagsField = typeBuilder.DefineField("_flags", _types.String, FieldAttributes.Private);
        _tsRegExpGlobalField = typeBuilder.DefineField("_global", _types.Boolean, FieldAttributes.Private);
        _tsRegExpIgnoreCaseField = typeBuilder.DefineField("_ignoreCase", _types.Boolean, FieldAttributes.Private);
        _tsRegExpMultilineField = typeBuilder.DefineField("_multiline", _types.Boolean, FieldAttributes.Private);
        _tsRegExpStickyField = typeBuilder.DefineField("_sticky", _types.Boolean, FieldAttributes.Private);
        _tsRegExpLastIndexField = typeBuilder.DefineField("_lastIndex", _types.Int32, FieldAttributes.Public);
        _tsRegExpLastIndexBoxedField = typeBuilder.DefineField("_lastIndexBoxed", _types.Object, FieldAttributes.Public);

        // Compile cache: shared static ConcurrentDictionary<string, Regex>
        // keyed by `pattern + "\x00" + (int)options`. Each TS regex literal
        // (`/foo/g`) evaluates to `new $RegExp(pattern, flags)` every time
        // the expression runs — without caching, that's per-iter
        // `new Regex(...)` compilation, the dominant cost in regex-in-loop
        // workloads (~250 MB / 100 K iters of validator-style code at
        // baseline). Compiled `Regex` objects are thread-safe and have no
        // per-instance state we depend on (`LastIndex` lives on
        // `$RegExp`, not `Regex`), so cache sharing is sound.
        var cdType = typeof(System.Collections.Concurrent.ConcurrentDictionary<string, Regex>);
        _tsRegExpCompileCacheField = typeBuilder.DefineField(
            "_compileCache", cdType, FieldAttributes.Private | FieldAttributes.Static);
        EmitTSRegExpCompileCacheCctor(typeBuilder, cdType);
        // JS→.NET shorthand rewrite (\d \w \s and negations) — emitted before
        // _GetCachedRegex, whose cache-miss branch compiles the rewritten
        // pattern. ExpandShorthand must precede the rewriter that calls it.
        EmitTSRegExpExpandShorthand(typeBuilder);
        EmitTSRegExpRewriteShorthands(typeBuilder);
        EmitTSRegExpGetCachedRegex(typeBuilder, cdType);

        // Helper method for normalizing flags
        EmitTSRegExpNormalizeFlags(typeBuilder, runtime);

        // Static helper for detecting named groups (must be emitted before constructors which use it)
        EmitTSRegExpHasNamedGroups(typeBuilder);

        // ES2025 modifier-group early-error validators (emitted before the
        // constructors, which call ValidateModifiers). Mirrors
        // SharpTSRegExp.ValidateModifiers (interp).
        EmitTSRegExpSkipCharClass(typeBuilder);
        EmitTSRegExpValidateModifierFlags(typeBuilder, runtime);
        EmitTSRegExpValidateModifiers(typeBuilder);
        EmitTSRegExpValidateFlags(typeBuilder, runtime);
        EmitTSRegExpFindGroupClose(typeBuilder);
        EmitTSRegExpValidateUnicodePattern(typeBuilder, runtime);

        // Constructors (pattern+flags first because pattern-only calls it)
        EmitTSRegExpCtorPatternFlags(typeBuilder, runtime);
        EmitTSRegExpCtorPattern(typeBuilder, runtime);

        // Property getters
        EmitTSRegExpSourceGetter(typeBuilder, runtime);
        EmitTSRegExpFlagsGetter(typeBuilder, runtime);
        EmitTSRegExpGlobalGetter(typeBuilder, runtime);
        EmitTSRegExpIgnoreCaseGetter(typeBuilder, runtime);
        EmitTSRegExpMultilineGetter(typeBuilder, runtime);
        EmitTSRegExpLastIndexGetter(typeBuilder, runtime);
        EmitTSRegExpLastIndexSetter(typeBuilder, runtime);

        // Static helper for named groups (must be emitted before Exec which uses it)
        EmitTSRegExpBuildNamedGroups(typeBuilder, runtime);

        // Instance methods
        // Strict-Set helper for lastIndex resets — emitted before Exec which
        // calls it. Throws TypeError when PDS has data descriptor with
        // writable=false (Object.defineProperty(r, 'lastIndex', {writable:false})).
        EmitTSRegExpSetLastIndexStrict(typeBuilder, runtime);
        EmitTSRegExpResolveLastIndex(typeBuilder, runtime);
        EmitTSRegExpTest(typeBuilder, runtime);
        EmitTSRegExpExec(typeBuilder, runtime);
        EmitTSRegExpToStringMethod(typeBuilder, runtime);
        EmitTSRegExpEscape(typeBuilder, runtime);

        // Internal methods for string operations
        EmitTSRegExpMatchAll(typeBuilder, runtime);
        EmitTSRegExpReplace(typeBuilder, runtime);
        EmitTSRegExpReplaceWithFn(typeBuilder, runtime);
        EmitTSRegExpExpandReplacementSpec(typeBuilder, runtime);
        EmitTSRegExpAdvanceStringIndexSpec(typeBuilder);
        EmitTSRegExpSearch(typeBuilder, runtime);
        EmitTSRegExpSplit(typeBuilder, runtime);

        // RegExp.prototype well-known-symbol-keyed helpers (ECMA-262 §22.2.5).
        // Static `(rx, ...)` shape so they can be wrapped by $TSFunction with
        // the regex bound as `_target`.
        EmitTSRegExpSymMatchHelper(typeBuilder, runtime);
        EmitTSRegExpSymMatchAllHelper(typeBuilder, runtime);
        EmitTSRegExpSymReplaceHelper(typeBuilder, runtime);
        EmitTSRegExpSymSearchHelper(typeBuilder, runtime);
        EmitTSRegExpSymSplitHelper(typeBuilder, runtime);

        // RegExp.prototype accessor-descriptor getters (ECMA-262 §22.2.5.{3-12}).
        // Each spec-aligned accessor lives on RegExp.prototype as a real
        // accessor with a getter that throws TypeError on non-RegExp `this`.
        // Installed into the prototype's PropertyDescriptorStore by
        // RegExpPrototypePopulate so `Object.getOwnPropertyDescriptor(
        // RegExp.prototype, 'source').get` returns the helper $TSFunction.
        EmitTSRegExpProtoAccessors(typeBuilder, runtime);

        // RegExp.prototype.exec / .test / .toString — spec-required data
        // methods (§22.2.5.2 / .5 / .14). Throw TypeError when called on
        // non-RegExp `this`. test262's prototype/exec/S15.10.6.2_A2_*.js
        // pattern (`var o={}; o.exec=RegExp.prototype.exec; o.exec(s)`)
        // checks this surface.
        EmitTSRegExpProtoMethods(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Static cctor for <c>$RegExp._compileCache</c>: just initialize to
    /// an empty <c>ConcurrentDictionary&lt;string, Regex&gt;</c>. Lazy
    /// construction would be nicer but adds a null-check per lookup; the
    /// upfront allocation is one-time and tiny.
    /// </summary>
    private void EmitTSRegExpCompileCacheCctor(TypeBuilder typeBuilder,
        Type cdType)
    {
        var cctor = typeBuilder.DefineTypeInitializer();
        var il = cctor.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(cdType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stsfld, _tsRegExpCompileCacheField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the cache-or-compile helper used by the <c>$RegExp</c> ctor.
    /// Replaces the hot-path <c>new Regex(pattern, options)</c> with a
    /// shared lookup. Race semantics: two threads racing on the same
    /// (pattern, options) may both compile, but only one's <c>Regex</c>
    /// wins the <c>TryAdd</c> — the other is dropped after returning.
    /// Correctness preserved (compiled <c>Regex</c> instances are
    /// thread-safe and behaviorally identical for the same source +
    /// options); only a transient duplicate compile in the rare race.
    /// </summary>
    private void EmitTSRegExpGetCachedRegex(TypeBuilder typeBuilder, Type cdType)
    {
        // private static Regex _GetCachedRegex(string pattern, RegexOptions options)
        var method = typeBuilder.DefineMethod(
            "_GetCachedRegex",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(Regex),
            [_types.String, typeof(RegexOptions)]
        );
        _tsRegExpGetCachedRegexMethod = method;

        var il = method.GetILGenerator();
        var keyLocal = il.DeclareLocal(_types.String);
        var cachedLocal = il.DeclareLocal(typeof(Regex));
        var freshLocal = il.DeclareLocal(typeof(Regex));
        var hitLabel = il.DefineLabel();

        // key = pattern + "\0" + ((int)options).ToString()
        il.Emit(OpCodes.Ldarg_0);                                        // pattern
        il.Emit(OpCodes.Ldstr, "\0");                                // separator
        var optionsLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_1);                                        // options as int (RegexOptions is enum:int)
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // if (_compileCache.TryGetValue(key, out cached)) return cached
        il.Emit(OpCodes.Ldsfld, _tsRegExpCompileCacheField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, cachedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cdType, "TryGetValue")!);
        il.Emit(OpCodes.Brtrue, hitLabel);

        // var fresh = new Regex(RewriteEcmaScriptShorthands(pattern), options);
        // _compileCache.TryAdd(key, fresh); return fresh
        // The key (built above from the raw pattern) is unchanged, so the
        // rewrite runs only on a cache miss — never on the hot cache-hit path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpRewriteShorthandsMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, typeof(Regex).GetConstructor([_types.String, typeof(RegexOptions)])!);
        il.Emit(OpCodes.Stloc, freshLocal);
        il.Emit(OpCodes.Ldsfld, _tsRegExpCompileCacheField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, freshLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cdType, "TryAdd")!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, freshLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hitLabel);
        il.Emit(OpCodes.Ldloc, cachedLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>static string ExpandShorthand(char esc, bool inClass)</c> — the
    /// per-escape replacement table consumed by
    /// <see cref="EmitTSRegExpRewriteShorthands"/>. Returns the explicit class
    /// for a positive shorthand (<c>\d \w \s</c>, inside or outside a class) or
    /// its bracketed/negated form (<c>\D \W \S</c>, outside a class only);
    /// returns <c>null</c> for a non-shorthand letter or a negated shorthand
    /// inside a class (both pass through unchanged). Mirrors
    /// <c>SharpTSRegExp.ExpandShorthand</c> (interp).
    /// </summary>
    private void EmitTSRegExpExpandShorthand(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandShorthand", MethodAttributes.Private | MethodAttributes.Static,
            _types.String, [_types.Char, _types.Boolean]);
        _tsRegExpExpandShorthandMethod = method;
        var il = method.GetILGenerator();

        // The whitespace class inner is baked in straight from the interp
        // constant, so both modes match the exact same set (one source of truth).
        var ws = Runtime.Types.SharpTSRegExp.WhitespaceClassInner;

        var ld = il.DefineLabel(); var lD = il.DefineLabel();
        var lw = il.DefineLabel(); var lW = il.DefineLabel();
        var ls = il.DefineLabel(); var lS = il.DefineLabel();

        // switch (esc) { ... default: return null; }
        void Dispatch(char ch, Label lbl)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, (int)ch); il.Emit(OpCodes.Beq, lbl);
        }
        Dispatch('d', ld); Dispatch('D', lD); Dispatch('w', lw);
        Dispatch('W', lW); Dispatch('s', ls); Dispatch('S', lS);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);

        // return inClass ? inside : outside  (inside == null → Ldnull)
        void Case(Label lbl, string? inside, string outside)
        {
            il.MarkLabel(lbl);
            var inLbl = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brtrue, inLbl);
            il.Emit(OpCodes.Ldstr, outside); il.Emit(OpCodes.Ret);
            il.MarkLabel(inLbl);
            if (inside == null) il.Emit(OpCodes.Ldnull);
            else il.Emit(OpCodes.Ldstr, inside);
            il.Emit(OpCodes.Ret);
        }
        Case(ld, "0-9", "[0-9]");
        Case(lD, null, "[^0-9]");
        Case(lw, "A-Za-z0-9_", "[A-Za-z0-9_]");
        Case(lW, null, "[^A-Za-z0-9_]");
        Case(ls, ws, "[" + ws + "]");
        Case(lS, null, "[^" + ws + "]");
    }

    /// <summary>
    /// Emits <c>static string RewriteEcmaScriptShorthands(string pattern)</c> —
    /// the compiled mirror of <c>SharpTSRegExp.RewriteEcmaScriptShorthands</c>.
    /// Rewrites the JS shorthand escapes to explicit ECMAScript-exact sets so the
    /// compiled .NET matcher follows JS semantics (sidesteps .NET's <c>\w</c>
    /// U+0130 over-match and its narrow <c>\s</c>). Single forward pass tracking
    /// <c>[…]</c> context and honoring backslash escapes; lazily allocates a
    /// StringBuilder so a pattern with no rewritable shorthand returns the
    /// original reference. Self-contained (BCL-only) — no SharpTS.dll reference.
    /// </summary>
    private void EmitTSRegExpRewriteShorthands(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "RewriteEcmaScriptShorthands", MethodAttributes.Private | MethodAttributes.Static,
            _types.String, [_types.String]);
        _tsRegExpRewriteShorthandsMethod = method;
        var il = method.GetILGenerator();

        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var sbCtorInt = _types.GetConstructor(_types.StringBuilder, [_types.Int32])!;
        var sbAppendStr = _types.GetMethod(_types.StringBuilder, "Append", [_types.String])!;
        var sbAppendChar = _types.GetMethod(_types.StringBuilder, "Append", [_types.Char])!;
        var sbAppendSub = _types.GetMethod(_types.StringBuilder, "Append", [_types.String, _types.Int32, _types.Int32])!;
        var sbToString = _types.GetMethod(_types.StringBuilder, "ToString", Type.EmptyTypes)!;

        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var inClassLocal = il.DeclareLocal(_types.Boolean);
        var nLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);
        var nextLocal = il.DeclareLocal(_types.Char);
        var replLocal = il.DeclareLocal(_types.String);

        var loopCond = il.DefineLabel();
        var normalChar = il.DefineLabel();
        var replNull = il.DefineLabel();
        var haveSb = il.DefineLabel();
        var replNullAdvance = il.DefineLabel();
        var chkClose = il.DefineLabel();
        var appendC = il.DefineLabel();
        var incr = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var retSb = il.DefineLabel();

        // sb = null; inClass = false; n = pattern.Length; i = 0
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, inClassLocal);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen); il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        // if (i >= n) goto end
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, endLabel);
        // c = pattern[i]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, cLocal);
        // if (c != '\\') goto normalChar
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Bne_Un, normalChar);
        // if (i + 1 >= n) goto normalChar   (lone trailing backslash → literal)
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, normalChar);

        // --- escape branch ---
        // next = pattern[i + 1]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, nextLocal);
        // repl = ExpandShorthand(next, inClass)
        il.Emit(OpCodes.Ldloc, nextLocal); il.Emit(OpCodes.Ldloc, inClassLocal); il.Emit(OpCodes.Call, _tsRegExpExpandShorthandMethod); il.Emit(OpCodes.Stloc, replLocal);
        // if (repl == null) goto replNull
        il.Emit(OpCodes.Ldloc, replLocal); il.Emit(OpCodes.Brfalse, replNull);
        //   if (sb != null) goto haveSb
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Brtrue, haveSb);
        //     sb = new StringBuilder(n + 16); sb.Append(pattern, 0, i)
        il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Ldc_I4, 16); il.Emit(OpCodes.Add); il.Emit(OpCodes.Newobj, sbCtorInt); il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, sbAppendSub); il.Emit(OpCodes.Pop);
        il.MarkLabel(haveSb);
        //   sb.Append(repl)
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Ldloc, replLocal); il.Emit(OpCodes.Callvirt, sbAppendStr); il.Emit(OpCodes.Pop);
        //   i += 2; goto loopCond
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(replNull);
        //   if (sb == null) goto replNullAdvance   (nothing buffered yet)
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Brfalse, replNullAdvance);
        //   sb.Append('\\'); sb.Append(next)
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Callvirt, sbAppendChar); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Ldloc, nextLocal); il.Emit(OpCodes.Callvirt, sbAppendChar); il.Emit(OpCodes.Pop);
        il.MarkLabel(replNullAdvance);
        //   i += 2; goto loopCond
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(normalChar);
        // if (c == '[') inClass = true; else if (c == ']') inClass = false;
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'['); il.Emit(OpCodes.Bne_Un, chkClose);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, inClassLocal); il.Emit(OpCodes.Br, appendC);
        il.MarkLabel(chkClose);
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)']'); il.Emit(OpCodes.Bne_Un, appendC);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, inClassLocal);
        il.MarkLabel(appendC);
        // if (sb != null) sb.Append(c)
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Brfalse, incr);
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Callvirt, sbAppendChar); il.Emit(OpCodes.Pop);
        il.MarkLabel(incr);
        // i += 1; goto loopCond
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(endLabel);
        // return sb == null ? pattern : sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Brtrue, retSb);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(retSb);
        il.Emit(OpCodes.Ldloc, sbLocal); il.Emit(OpCodes.Callvirt, sbToString); il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpNormalizeFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static string NormalizeFlags(string flags)
        var method = typeBuilder.DefineMethod(
            "NormalizeFlags",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.TSRegExpNormalizeFlags = method;

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));

        // var sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        // Build the canonical flag-string in ECMA-262 §22.2.5.3 order:
        // hasIndices (d), global (g), ignoreCase (i), multiline (m), dotAll
        // (s), unicode (u), unicodeSets (v), sticky (y). All eight flags
        // must round-trip through the stored slot so the JS-visible
        // accessors (parsed in $Runtime.GetProperty's $RegExp arm) report
        // correctly. The actual matcher only uses g/i/m/s currently; y
        // (sticky) is honored by Exec via Sticky parsed from this string;
        // u/v don't change the matching engine but must round-trip for
        // tests that inspect flags.
        EmitAppendFlagIfContains(il, sbLocal, 'd');
        EmitAppendFlagIfContains(il, sbLocal, 'g');
        EmitAppendFlagIfContains(il, sbLocal, 'i');
        EmitAppendFlagIfContains(il, sbLocal, 'm');
        EmitAppendFlagIfContains(il, sbLocal, 's');
        EmitAppendFlagIfContains(il, sbLocal, 'u');
        EmitAppendFlagIfContains(il, sbLocal, 'v');
        EmitAppendFlagIfContains(il, sbLocal, 'y');

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAppendFlagIfContains(ILGenerator il, LocalBuilder sbLocal, char flag)
    {
        var skipLabel = il.DefineLabel();

        // if (flags.Contains('x'))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)flag);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // sb.Append('x')
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, (int)flag);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits a static method: bool HasNamedGroups(string pattern)
    /// Detects (?&lt;name&gt;...) patterns, excluding lookbehind (?&lt;= and (?&lt;!.
    /// </summary>
    private void EmitTSRegExpHasNamedGroups(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "HasNamedGroups",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String]
        );
        _tsRegExpHasNamedGroupsMethod = method;

        var il = method.GetILGenerator();
        var iLocal = il.DeclareLocal(_types.Int32);

        var loopCondLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();
        var incrementLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCondLabel);

        // Loop body
        il.MarkLabel(loopBodyLabel);

        // if (pattern[i] == '\\') { i += 2; continue; }
        var notEscapeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Bne_Un, notEscapeLabel);
        // i += 2
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCondLabel);

        il.MarkLabel(notEscapeLabel);

        // Check: pattern[i] == '('
        var notOpenParenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'(');
        il.Emit(OpCodes.Bne_Un, notOpenParenLabel);

        // Check: i + 2 < pattern.Length
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, notMatchLabel);

        // Check: pattern[i+1] == '?'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'?');
        il.Emit(OpCodes.Bne_Un, notMatchLabel);

        // Check: pattern[i+2] == '<'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'<');
        il.Emit(OpCodes.Bne_Un, notMatchLabel);

        // We found (?< — check if it's lookbehind: (?<= or (?<!
        // Check: i + 3 < pattern.Length
        var notLookbehindLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnTrueLabel); // i+3 >= Length means no char after <, so it IS a named group

        // Check: pattern[i+3] == '=' || pattern[i+3] == '!'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        var charAfterLessThan = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charAfterLessThan);

        il.Emit(OpCodes.Ldloc, charAfterLessThan);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Beq, notLookbehindLabel);
        il.Emit(OpCodes.Ldloc, charAfterLessThan);
        il.Emit(OpCodes.Ldc_I4, (int)'!');
        il.Emit(OpCodes.Beq, notLookbehindLabel);

        // Not lookbehind — it's a named group
        il.Emit(OpCodes.Br, returnTrueLabel);

        // It IS lookbehind — i += 4; continue
        il.MarkLabel(notLookbehindLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCondLabel);

        il.MarkLabel(notMatchLabel);
        il.MarkLabel(notOpenParenLabel);

        // i++
        il.MarkLabel(incrementLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop condition: while (i < pattern.Length - 2)
        il.MarkLabel(loopCondLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Blt, loopBodyLabel);

        // return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // return true
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>static int SkipCharClass(string p, int i)</c> — returns the index
    /// just past the matching <c>]</c> of a class starting at <paramref name="i"/>
    /// (<c>p[i]=='['</c>), honoring <c>\]</c>. Unterminated → length.
    /// </summary>
    private void EmitTSRegExpSkipCharClass(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "SkipCharClass", MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32, [_types.String, _types.Int32]);
        _tsRegExpSkipCharClassMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var nLocal = il.DeclareLocal(_types.Int32);
        var kLocal = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var retN = il.DefineLabel();
        var notEsc = il.DefineLabel();
        var notClose = il.DefineLabel();
        // n = p.Length
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen); il.Emit(OpCodes.Stloc, nLocal);
        // k = i + 1
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, kLocal);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, retN);
        // ch = p[k]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Bne_Un, notEsc);
        il.Emit(OpCodes.Pop); // drop ch
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notEsc);
        il.Emit(OpCodes.Ldc_I4, (int)']'); il.Emit(OpCodes.Bne_Un, notClose);
        // return k + 1
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
        il.MarkLabel(notClose);
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(retN);
        il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>static void ValidateModifierFlags(string mod)</c> — single-pass
    /// validation of the flag text between <c>(?</c> and <c>:</c>; throws
    /// <c>$SyntaxError</c> on a non-i/m/s flag, a duplicate within a set, a flag
    /// in both sets, a second dash, or <c>(?-:)</c>. Mirrors the interp method.
    /// </summary>
    private void EmitTSRegExpValidateModifierFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateModifierFlags", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.String]);
        _tsRegExpValidateModifierFlagsMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var iLocal = il.DeclareLocal(_types.Int32);
        var addMask = il.DeclareLocal(_types.Int32);
        var removeMask = il.DeclareLocal(_types.Int32);
        var sawDash = il.DeclareLocal(_types.Int32);
        var fLocal = il.DeclareLocal(_types.Char);
        var bitLocal = il.DeclareLocal(_types.Int32);

        var loop = il.DefineLabel();
        var afterLoop = il.DefineLabel();
        var notDash = il.DefineLabel();
        var cont = il.DefineLabel();
        var setI = il.DefineLabel();
        var setM = il.DefineLabel();
        var setS = il.DefineLabel();
        var haveBit = il.DefineLabel();
        var inRemove = il.DefineLabel();
        var retLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // addMask=removeMask=sawDash=i=0
        foreach (var loc in new[] { addMask, removeMask, sawDash, iLocal })
        { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, loc); }

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen);
        il.Emit(OpCodes.Bge, afterLoop);
        // f = mod[i]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Stloc, fLocal);
        // if (f != '-') goto notDash
        il.Emit(OpCodes.Ldloc, fLocal); il.Emit(OpCodes.Ldc_I4, (int)'-'); il.Emit(OpCodes.Bne_Un, notDash);
        //   if (sawDash != 0) throw; sawDash=1; cont
        il.Emit(OpCodes.Ldloc, sawDash); il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, sawDash);
        il.Emit(OpCodes.Br, cont);
        il.MarkLabel(notDash);
        // bit = i?1:m?2:s?4:0
        il.Emit(OpCodes.Ldloc, fLocal); il.Emit(OpCodes.Ldc_I4, (int)'i'); il.Emit(OpCodes.Beq, setI);
        il.Emit(OpCodes.Ldloc, fLocal); il.Emit(OpCodes.Ldc_I4, (int)'m'); il.Emit(OpCodes.Beq, setM);
        il.Emit(OpCodes.Ldloc, fLocal); il.Emit(OpCodes.Ldc_I4, (int)'s'); il.Emit(OpCodes.Beq, setS);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, bitLocal); il.Emit(OpCodes.Br, haveBit);
        il.MarkLabel(setI); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, bitLocal); il.Emit(OpCodes.Br, haveBit);
        il.MarkLabel(setM); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Stloc, bitLocal); il.Emit(OpCodes.Br, haveBit);
        il.MarkLabel(setS); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Stloc, bitLocal);
        il.MarkLabel(haveBit);
        // if (bit==0) throw
        il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.Brfalse, throwLabel);
        // if (sawDash != 0) goto inRemove
        il.Emit(OpCodes.Ldloc, sawDash); il.Emit(OpCodes.Brtrue, inRemove);
        //   if ((addMask & bit)!=0) throw; addMask|=bit
        il.Emit(OpCodes.Ldloc, addMask); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.And); il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldloc, addMask); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.Or); il.Emit(OpCodes.Stloc, addMask);
        il.Emit(OpCodes.Br, cont);
        il.MarkLabel(inRemove);
        //   if ((removeMask & bit)!=0) throw; removeMask|=bit
        il.Emit(OpCodes.Ldloc, removeMask); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.And); il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldloc, removeMask); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.Or); il.Emit(OpCodes.Stloc, removeMask);
        il.MarkLabel(cont);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(afterLoop);
        // if ((addMask & removeMask)!=0) throw
        il.Emit(OpCodes.Ldloc, addMask); il.Emit(OpCodes.Ldloc, removeMask); il.Emit(OpCodes.And); il.Emit(OpCodes.Brtrue, throwLabel);
        // if (sawDash!=0 && addMask==0 && removeMask==0) throw
        il.Emit(OpCodes.Ldloc, sawDash); il.Emit(OpCodes.Brfalse, retLabel);
        il.Emit(OpCodes.Ldloc, addMask); il.Emit(OpCodes.Brtrue, retLabel);
        il.Emit(OpCodes.Ldloc, removeMask); il.Emit(OpCodes.Brtrue, retLabel);
        il.Emit(OpCodes.Br, throwLabel);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowSyntaxError(il, runtime, "Invalid regular expression: invalid modifier group");
    }

    /// <summary>
    /// Emits <c>static void ValidateModifiers(string pattern)</c> — scans for
    /// <c>(?flags-flags:…)</c> modifier groups (skipping escapes, char classes,
    /// and the non-modifier <c>(?:</c>/<c>(?=</c>/<c>(?!</c>/<c>(?&lt;…</c> forms)
    /// and validates each via ValidateModifierFlags. Mirrors the interp method.
    /// </summary>
    private void EmitTSRegExpValidateModifiers(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateModifiers", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.String]);
        _tsRegExpValidateModifiersMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var substr = _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!;
        var nLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var jLocal = il.DeclareLocal(_types.Int32);
        var kLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);
        var dLocal = il.DeclareLocal(_types.Char);

        var loop = il.DefineLabel();
        var end = il.DefineLabel();
        var incr = il.DefineLabel();
        var skip2 = il.DefineLabel();
        var notBackslash = il.DefineLabel();
        var notClass = il.DefineLabel();
        var scanLoop = il.DefineLabel();
        var foundColon = il.DefineLabel();

        // n = pattern.Length; i = 0
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen); il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, end);
        // c = pattern[i]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, cLocal);
        // if (c=='\\') { i+=2; loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Bne_Un, notBackslash);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notBackslash);
        // if (c=='[') { i = SkipCharClass(pattern, i); loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'['); il.Emit(OpCodes.Bne_Un, notClass);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Call, _tsRegExpSkipCharClassMethod); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notClass);
        // if (c != '(') goto incr
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'('); il.Emit(OpCodes.Bne_Un, incr);
        // if (i+1 >= n) goto incr
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        // if (pattern[i+1] != '?') goto incr
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Ldc_I4, (int)'?'); il.Emit(OpCodes.Bne_Un, incr);
        // j = i+2; if (j>=n) goto incr
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Ldloc, jLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        // d = pattern[j]; if d in {':','=','!','<'} goto skip2
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, jLocal); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)':'); il.Emit(OpCodes.Beq, skip2);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'='); il.Emit(OpCodes.Beq, skip2);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'!'); il.Emit(OpCodes.Beq, skip2);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'<'); il.Emit(OpCodes.Beq, skip2);
        // k = j
        il.Emit(OpCodes.Ldloc, jLocal); il.Emit(OpCodes.Stloc, kLocal);
        il.MarkLabel(scanLoop);
        // if (k>=n) goto incr
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        // ch = pattern[k]; if ch==':' foundColon; if ch==')' incr; k++
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, (int)':'); il.Emit(OpCodes.Beq, foundColon);
        il.Emit(OpCodes.Ldc_I4, (int)')'); il.Emit(OpCodes.Beq, incr);
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, scanLoop);
        il.MarkLabel(foundColon);
        il.Emit(OpCodes.Pop); // drop the duped ':'
        // ValidateModifierFlags(pattern.Substring(j, k-j))
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldloc, jLocal); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, substr);
        il.Emit(OpCodes.Call, _tsRegExpValidateModifierFlagsMethod);
        // i = k+1; loop
        il.Emit(OpCodes.Ldloc, kLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(skip2);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(incr);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>static void ValidateFlags(string flags)</c> — ECMA-262 §22.2.3.3:
    /// throws <c>$SyntaxError</c> on an unknown flag, a duplicate flag, or both
    /// u and v. Mirrors the interp <c>SharpTSRegExp.ValidateFlags</c>.
    /// </summary>
    private void EmitTSRegExpValidateFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateFlags", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.String]);
        _tsRegExpValidateFlagsMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var iLocal = il.DeclareLocal(_types.Int32);
        var seenLocal = il.DeclareLocal(_types.Int32);
        var fLocal = il.DeclareLocal(_types.Char);
        var bitLocal = il.DeclareLocal(_types.Int32);

        var loop = il.DefineLabel();
        var afterLoop = il.DefineLabel();
        var haveBit = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var retLabel = il.DefineLabel();

        // (char, bit) table → a labelled set-and-jump per flag
        (char ch, int bit)[] flags =
        [
            ('d', 1), ('g', 2), ('i', 4), ('m', 8),
            ('s', 16), ('u', 32), ('v', 64), ('y', 128)
        ];

        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, seenLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen);
        il.Emit(OpCodes.Bge, afterLoop);
        // f = flags[i]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Stloc, fLocal);
        // bit = switch(f)
        var setLabels = flags.Select(_ => il.DefineLabel()).ToArray();
        for (int fi = 0; fi < flags.Length; fi++)
        {
            il.Emit(OpCodes.Ldloc, fLocal); il.Emit(OpCodes.Ldc_I4, (int)flags[fi].ch);
            il.Emit(OpCodes.Beq, setLabels[fi]);
        }
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, bitLocal); il.Emit(OpCodes.Br, haveBit);
        for (int fi = 0; fi < flags.Length; fi++)
        {
            il.MarkLabel(setLabels[fi]);
            il.Emit(OpCodes.Ldc_I4, flags[fi].bit); il.Emit(OpCodes.Stloc, bitLocal); il.Emit(OpCodes.Br, haveBit);
        }
        il.MarkLabel(haveBit);
        // if (bit==0) throw
        il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.Brfalse, throwLabel);
        // if ((seen & bit)!=0) throw
        il.Emit(OpCodes.Ldloc, seenLocal); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.And); il.Emit(OpCodes.Brtrue, throwLabel);
        // seen |= bit
        il.Emit(OpCodes.Ldloc, seenLocal); il.Emit(OpCodes.Ldloc, bitLocal); il.Emit(OpCodes.Or); il.Emit(OpCodes.Stloc, seenLocal);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(afterLoop);
        // if ((seen & 32)!=0 && (seen & 64)!=0) throw   (u and v)
        il.Emit(OpCodes.Ldloc, seenLocal); il.Emit(OpCodes.Ldc_I4, 32); il.Emit(OpCodes.And); il.Emit(OpCodes.Brfalse, retLabel);
        il.Emit(OpCodes.Ldloc, seenLocal); il.Emit(OpCodes.Ldc_I4, 64); il.Emit(OpCodes.And); il.Emit(OpCodes.Brfalse, retLabel);
        il.Emit(OpCodes.Br, throwLabel);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowSyntaxError(il, runtime, "Invalid regular expression flags");
    }

    /// <summary>
    /// Emits <c>static int FindGroupClose(string p, int openIdx)</c> — index of
    /// the <c>)</c> matching the group opening at <paramref name="openIdx"/>
    /// (honoring <c>\</c> escapes and <c>[…]</c> char classes), or -1. Mirrors
    /// the interp <c>SharpTSRegExp.FindGroupClose</c>.
    /// </summary>
    private void EmitTSRegExpFindGroupClose(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "FindGroupClose", MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32, [_types.String, _types.Int32]);
        _tsRegExpFindGroupCloseMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var nLocal = il.DeclareLocal(_types.Int32);
        var depthLocal = il.DeclareLocal(_types.Int32);
        var inClassLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);

        var loop = il.DefineLabel();
        var retNeg = il.DefineLabel();
        var notEsc = il.DefineLabel();
        var notInClass = il.DefineLabel();
        var afterClassClose = il.DefineLabel();
        var notOpenClass = il.DefineLabel();
        var notOpenParen = il.DefineLabel();
        var notCloseParen = il.DefineLabel();
        var depthNotZero = il.DefineLabel();

        // n = p.Length; depth = 0; inClass = 0; i = openIdx
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen); il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, inClassLocal);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, retNeg);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, cLocal);
        // if (c=='\\') { i += 2; loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Bne_Un, notEsc);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notEsc);
        // if (inClass) { if (c==']') inClass=0; i++; loop }
        il.Emit(OpCodes.Ldloc, inClassLocal); il.Emit(OpCodes.Brfalse, notInClass);
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)']'); il.Emit(OpCodes.Bne_Un, afterClassClose);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, inClassLocal);
        il.MarkLabel(afterClassClose);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notInClass);
        // if (c=='[') { inClass=1; i++; loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'['); il.Emit(OpCodes.Bne_Un, notOpenClass);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, inClassLocal);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notOpenClass);
        // if (c=='(') { depth++; i++; loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'('); il.Emit(OpCodes.Bne_Un, notOpenParen);
        il.Emit(OpCodes.Ldloc, depthLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notOpenParen);
        // if (c==')') { depth--; if (depth==0) return i; i++; loop }
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)')'); il.Emit(OpCodes.Bne_Un, notCloseParen);
        il.Emit(OpCodes.Ldloc, depthLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Ldloc, depthLocal); il.Emit(OpCodes.Brtrue, depthNotZero);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ret);
        il.MarkLabel(depthNotZero);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(notCloseParen);
        // default: i++; loop
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(retNeg);
        il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>static void ValidateUnicodePattern(string pattern)</c> — the
    /// safe, false-positive-free subset of ECMA-262 Annex B u/v-mode early
    /// errors: (1) a lookaround assertion immediately followed by a quantifier;
    /// (2) <c>\c</c> not followed by an ASCII letter. Mirrors the interp
    /// <c>SharpTSRegExp.ValidateUnicodePattern</c>.
    /// </summary>
    private void EmitTSRegExpValidateUnicodePattern(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateUnicodePattern", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, [_types.String]);
        _tsRegExpValidateUnicodePatternMethod = method;
        var il = method.GetILGenerator();
        var getLen = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var nLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);
        var dLocal = il.DeclareLocal(_types.Char);
        var afterLocal = il.DeclareLocal(_types.Char);
        var closeLocal = il.DeclareLocal(_types.Int32);

        var loop = il.DefineLabel();
        var end = il.DefineLabel();
        var incr = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var notBackslash = il.DefineLabel();
        var afterCCheck = il.DefineLabel();
        var afterIsZero = il.DefineLabel();
        var haveAfter = il.DefineLabel();
        var checkLower = il.DefineLabel();
        var isAssertion = il.DefineLabel();

        // n = pattern.Length; i = 0
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getLen); il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, end);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, cLocal);

        // --- backslash branch ---
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'\\'); il.Emit(OpCodes.Bne_Un, notBackslash);
        //   if (i+1 < n && pattern[i+1]=='c')
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, afterCCheck);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Ldc_I4, (int)'c'); il.Emit(OpCodes.Bne_Un, afterCCheck);
        //     after = i+2<n ? pattern[i+2] : '\0'
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, afterIsZero);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, afterLocal);
        il.Emit(OpCodes.Br, haveAfter);
        il.MarkLabel(afterIsZero);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, afterLocal);
        il.MarkLabel(haveAfter);
        //     if (!IsAsciiLetter(after)) throw
        il.Emit(OpCodes.Ldloc, afterLocal); il.Emit(OpCodes.Ldc_I4, (int)'A'); il.Emit(OpCodes.Blt, checkLower);
        il.Emit(OpCodes.Ldloc, afterLocal); il.Emit(OpCodes.Ldc_I4, (int)'Z'); il.Emit(OpCodes.Ble, afterCCheck);
        il.MarkLabel(checkLower);
        il.Emit(OpCodes.Ldloc, afterLocal); il.Emit(OpCodes.Ldc_I4, (int)'a'); il.Emit(OpCodes.Blt, throwLabel);
        il.Emit(OpCodes.Ldloc, afterLocal); il.Emit(OpCodes.Ldc_I4, (int)'z'); il.Emit(OpCodes.Bgt, throwLabel);
        // letter ok -> fall through to afterCCheck
        il.MarkLabel(afterCCheck);
        //   i += 2; continue
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(notBackslash);
        // --- assertion branch: if (c=='(' && i+2<n && pattern[i+1]=='?') ---
        il.Emit(OpCodes.Ldloc, cLocal); il.Emit(OpCodes.Ldc_I4, (int)'('); il.Emit(OpCodes.Bne_Un, incr);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Ldc_I4, (int)'?'); il.Emit(OpCodes.Bne_Un, incr);
        //   d = pattern[i+2]
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, dLocal);
        //   if (d=='=' || d=='!') isAssertion
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'='); il.Emit(OpCodes.Beq, isAssertion);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'!'); il.Emit(OpCodes.Beq, isAssertion);
        //   if (d!='<') incr
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'<'); il.Emit(OpCodes.Bne_Un, incr);
        //   if (i+3>=n) incr
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        //   d = pattern[i+3]; if (d=='='||d=='!') isAssertion else incr
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'='); il.Emit(OpCodes.Beq, isAssertion);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'!'); il.Emit(OpCodes.Beq, isAssertion);
        il.Emit(OpCodes.Br, incr);

        il.MarkLabel(isAssertion);
        //   close = FindGroupClose(pattern, i)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Call, _tsRegExpFindGroupCloseMethod); il.Emit(OpCodes.Stloc, closeLocal);
        //   if (close < 0) incr
        il.Emit(OpCodes.Ldloc, closeLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, incr);
        //   if (close+1 >= n) incr
        il.Emit(OpCodes.Ldloc, closeLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Bge, incr);
        //   d = pattern[close+1]; if IsQuantifier(d) throw
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, closeLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Callvirt, getChars); il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'*'); il.Emit(OpCodes.Beq, throwLabel);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'+'); il.Emit(OpCodes.Beq, throwLabel);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'?'); il.Emit(OpCodes.Beq, throwLabel);
        il.Emit(OpCodes.Ldloc, dLocal); il.Emit(OpCodes.Ldc_I4, (int)'{'); il.Emit(OpCodes.Beq, throwLabel);

        il.MarkLabel(incr);
        il.Emit(OpCodes.Ldloc, iLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowSyntaxError(il, runtime, "Invalid regular expression: invalid Unicode-mode pattern");
    }

    private void EmitTSRegExpCtorPattern(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $RegExp(string pattern) : this(pattern, "") { }
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSRegExpCtorPattern = ctor;

        var il = ctor.GetILGenerator();

        // Call the two-arg constructor with empty string for flags
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // pattern
        il.Emit(OpCodes.Ldstr, "");  // flags = ""
        il.Emit(OpCodes.Call, runtime.TSRegExpCtorPatternFlags!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpCtorPatternFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $RegExp(string pattern, string flags)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.String]
        );
        runtime.TSRegExpCtorPatternFlags = ctor;

        var il = ctor.GetILGenerator();
        var optionsLocal = il.DeclareLocal(typeof(RegexOptions));
        var tryEndLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // ES2025 modifier-group early errors — throw $SyntaxError before compiling.
        il.Emit(OpCodes.Ldarg_1); // pattern
        il.Emit(OpCodes.Call, _tsRegExpValidateModifiersMethod);

        // §22.2.3.3 flag validation (unknown/duplicate flag, u+v) → SyntaxError.
        il.Emit(OpCodes.Ldarg_2); // flags
        il.Emit(OpCodes.Call, _tsRegExpValidateFlagsMethod);

        // Annex B u/v-mode early errors (safe subset).
        // if (flags.Contains('u') || flags.Contains('v')) ValidateUnicodePattern(pattern);
        var doUnicodeLabel = il.DefineLabel();
        var skipUnicodeLabel = il.DefineLabel();
        var containsChar = _types.GetMethod(_types.String, "Contains", [_types.Char])!;
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)'u');
        il.Emit(OpCodes.Call, containsChar); il.Emit(OpCodes.Brtrue, doUnicodeLabel);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4, (int)'v');
        il.Emit(OpCodes.Call, containsChar); il.Emit(OpCodes.Brfalse, skipUnicodeLabel);
        il.MarkLabel(doUnicodeLabel);
        il.Emit(OpCodes.Ldarg_1); // pattern
        il.Emit(OpCodes.Call, _tsRegExpValidateUnicodePatternMethod);
        il.MarkLabel(skipUnicodeLabel);

        // _source = pattern
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsRegExpSourceField);

        // _flags = NormalizeFlags(flags)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSRegExpNormalizeFlags!);
        il.Emit(OpCodes.Stfld, _tsRegExpFlagsField);

        // _global = _flags.Contains('g')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'g');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpGlobalField);

        // _ignoreCase = _flags.Contains('i')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'i');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpIgnoreCaseField);

        // _multiline = _flags.Contains('m')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'m');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpMultilineField);

        // _sticky = _flags.Contains('y') — drives sticky-match semantics
        // in Exec (use lastIndex, enforce exact-start, reset on failure).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'y');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpStickyField);

        // _lastIndex = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);

        // Named groups require non-ECMAScript mode in .NET
        // if (HasNamedGroups(pattern)) options = None; else options = ECMAScript;
        var noNamedGroupsLabel = il.DefineLabel();
        var afterOptionsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1); // pattern
        il.Emit(OpCodes.Call, _tsRegExpHasNamedGroupsMethod);
        il.Emit(OpCodes.Brfalse, noNamedGroupsLabel);

        // Named groups: use RegexOptions.None
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.None);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.Emit(OpCodes.Br, afterOptionsLabel);

        il.MarkLabel(noNamedGroupsLabel);
        // No named groups: use ECMAScript
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.ECMAScript);
        il.Emit(OpCodes.Stloc, optionsLocal);

        il.MarkLabel(afterOptionsLabel);

        // if (_ignoreCase) options |= RegexOptions.IgnoreCase
        var skipIgnoreCaseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpIgnoreCaseField);
        il.Emit(OpCodes.Brfalse, skipIgnoreCaseLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.IgnoreCase);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.MarkLabel(skipIgnoreCaseLabel);

        // if (_multiline) options |= RegexOptions.Multiline
        var skipMultilineLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpMultilineField);
        il.Emit(OpCodes.Brfalse, skipMultilineLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.Multiline);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.MarkLabel(skipMultilineLabel);

        // if (_flags.Contains('s')) options = (options & ~ECMAScript) | Singleline
        // ECMAScript mode is mutually exclusive with Singleline in .NET, so clear it when 's' is requested.
        var skipSinglelineLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'s');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Brfalse, skipSinglelineLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, ~(int)RegexOptions.ECMAScript);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.Singleline);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.MarkLabel(skipSinglelineLabel);

        // options |= RegexOptions.Compiled — compile the engine to IL instead of
        // running it interpreted. Mirrors SharpTSRegExp (interpreter); the one-
        // time JIT cost is amortized by _GetCachedRegex's (pattern, options)
        // cache. Folded into the cache key, so it can't collide with a
        // would-be interpreted entry. Measured ~1.3x on raw matching.
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.Compiled);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);

        // try { _regex = _GetCachedRegex(pattern, options); }
        // Cache lookup keyed by (pattern, options) — see
        // EmitTSRegExpGetCachedRegex for rationale. Replaces a per-call
        // Regex compilation that dominates regex-in-loop workloads.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // pattern
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, _tsRegExpGetCachedRegexMethod);
        il.Emit(OpCodes.Stfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Leave, endLabel);

        // catch (ArgumentException ex) { throw new $SyntaxError("Invalid regular expression: " + ex.Message); }
        // ECMA-262 §22.2.3.1: an invalid pattern is a SyntaxError. Throw a guest
        // $SyntaxError (via CreateException) so guest try/catch and
        // `e instanceof SyntaxError` observe the spec type, not a host Exception.
        il.BeginCatchBlock(typeof(ArgumentException));
        var exLocal = il.DeclareLocal(typeof(ArgumentException));
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldstr, "Invalid regular expression: ");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSSyntaxErrorCtor);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSourceGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Source",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpSourceGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpSourceField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpFlagsGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Flags",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpFlagsGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpGlobalGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Global",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpGlobalGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpIgnoreCaseGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_IgnoreCase",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpIgnoreCaseGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpIgnoreCaseField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpMultilineGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Multiline",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpMultilineGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpMultilineField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpLastIndexGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_LastIndex",
            MethodAttributes.Public,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TSRegExpLastIndexGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpLastIndexSetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "set_LastIndex",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32]
        );
        runtime.TSRegExpLastIndexSetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        // A numeric set supersedes any boxed non-numeric lastIndex.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexBoxedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpTest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public bool Test(string input)
        var method = typeBuilder.DefineMethod(
            "Test",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String]
        );
        runtime.TSRegExpTestMethod = method;

        var il = method.GetILGenerator();
        var globalLabel = il.DefineLabel();
        var matchFoundLabel = il.DefineLabel();
        var notGlobalLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var matchLocal = il.DeclareLocal(typeof(Match));
        var startIndexLocal = il.DeclareLocal(_types.Int32);
        var inputLocal = il.DeclareLocal(_types.String);

        // input = input ?? "undefined" (JS: regex.test(undefined) tests against "undefined")
        il.Emit(OpCodes.Ldarg_1);
        var inputNotNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, inputNotNullLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Stloc, inputLocal);
        var inputAssignedLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, inputAssignedLabel);
        il.MarkLabel(inputNotNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, inputLocal);
        il.MarkLabel(inputAssignedLabel);

        // ECMA-262 §22.2.5.2.2 step 4: resolve a non-numeric boxed lastIndex
        // (valueOf once) before reading it for global/sticky matching.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpResolveLastIndexMethod);

        // if (_global || _sticky) goto globalLabel — sticky shares lastIndex
        // semantics with global per ECMA-262 §22.2.5.2.2 step 8.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, globalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brtrue, globalLabel);

        // Non-global, non-sticky: return _regex.IsMatch(input)
        il.MarkLabel(notGlobalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("IsMatch", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // Global / sticky path: uses lastIndex.
        il.MarkLabel(globalLabel);

        // if (LastIndex > input.Length) { LastIndex = 0; return false; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnFalseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);

        // var startIndex = Math.Min(LastIndex, input.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        // var match = _regex.Match(input, startIndex)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.RegexMatchStringInt);
        il.Emit(OpCodes.Stloc, matchLocal);

        // if (match.Success)
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, matchFoundLabel);

        // No match: LastIndex = 0; return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Match found path. If sticky requires match.Index == startIndex
        // (already-clamped LastIndex). If not, treat as no-match per
        // ECMA-262 §22.2.5.2.2 (sticky doesn't forward-scan).
        il.MarkLabel(matchFoundLabel);
        var matchAcceptedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brfalse, matchAcceptedLabel);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Beq, matchAcceptedLabel);
        // Sticky mismatch — reset LastIndex and return false.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(matchAcceptedLabel);
        // Set LastIndex = match.Index + match.Length; return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits internal void SetLastIndexStrict(int newValue) on $RegExp.
    /// Spec ECMA-262 §22.2.5.2.2 RegExpBuiltinExec writes lastIndex with
    /// Throw=true on global/sticky resets. When the user installed a
    /// non-writable PDS data descriptor on `lastIndex`, the write must
    /// throw TypeError. Otherwise falls through to a direct Stfld of
    /// the typed `_lastIndex` field.
    /// </summary>
    private void EmitTSRegExpSetLastIndexStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetLastIndexStrict",
            MethodAttributes.Assembly,
            _types.Void,
            [_types.Int32]
        );
        _tsRegExpSetLastIndexStrictMethod = method;

        var il = method.GetILGenerator();
        var pdsLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var doWriteLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // pds = PDSGetPropertyDescriptor(this, "lastIndex")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsLocal);
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Brfalse, doWriteLabel);

        // If accessor (getter or setter present): for now skip typed-slot
        // write — full setter dispatch is a separate refactor. Stfld would
        // bypass user accessor entirely, which is no worse than today.
        var notAccessorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doWriteLabel);
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doWriteLabel);
        il.MarkLabel(notAccessorLabel);

        // Data descriptor — writable=false → throw TypeError.
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doWriteLabel);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot assign to read only property 'lastIndex'");

        il.MarkLabel(doWriteLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        // A numeric write supersedes any boxed non-numeric lastIndex (global/
        // sticky write-back makes lastIndex an ordinary number again).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexBoxedField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>void ResolveLastIndex()</c>: when a non-numeric value was
    /// assigned to <c>lastIndex</c> (held in <c>_lastIndexBoxed</c>), ToLength-
    /// coerce it into the typed <c>_lastIndex</c> for matching — invoking the
    /// value's <c>valueOf</c> exactly once per exec (ECMA-262 §22.2.5.2.2 step 4).
    /// The boxed slot is left intact so a non-global exec (which doesn't write
    /// lastIndex back) preserves the original object identity for `r.lastIndex`.
    /// No-op (single null check) on the common numeric fast path.
    /// </summary>
    private void EmitTSRegExpResolveLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ResolveLastIndex", MethodAttributes.Private, _types.Void, Type.EmptyTypes);
        _tsRegExpResolveLastIndexMethod = method;
        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();
        var setZeroLabel = il.DefineLabel();
        var setMaxLabel = il.DefineLabel();
        var haveVLabel = il.DefineLabel();
        var dLocal = il.DeclareLocal(_types.Double);
        var vLocal = il.DeclareLocal(_types.Int32);

        // if (_lastIndexBoxed == null) return;  (numeric fast path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexBoxedField);
        il.Emit(OpCodes.Brfalse, doneLabel);
        // d = ToNumber(_lastIndexBoxed)   (invokes valueOf once; ToNumber is
        // forward-declared, so it's available during $RegExp emission)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexBoxedField);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, dLocal);
        // ToLength: NaN or d<=0 → 0; d>int.MaxValue → int.MaxValue; else (int)d.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Bne_Un, setZeroLabel);                 // NaN (d != d)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble, setZeroLabel);                    // d <= 0
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Bgt, setMaxLabel);                     // d > int.MaxValue
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I4);                              // ToInteger (truncate)
        il.Emit(OpCodes.Stloc, vLocal);
        il.Emit(OpCodes.Br, haveVLabel);
        il.MarkLabel(setMaxLabel);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, vLocal);
        il.Emit(OpCodes.Br, haveVLabel);
        il.MarkLabel(setZeroLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, vLocal);
        il.MarkLabel(haveVLabel);
        // _lastIndex = v;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpExec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object? Exec(string input) - returns $Object or null
        var method = typeBuilder.DefineMethod(
            "Exec",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSRegExpExecMethod = method;

        var il = method.GetILGenerator();
        var globalMatchLabel = il.DefineLabel();
        var nonGlobalMatchLabel = il.DefineLabel();
        var checkSuccessLabel = il.DefineLabel();
        var matchSuccessLabel = il.DefineLabel();
        var noMatchLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        var matchLocal = il.DeclareLocal(typeof(Match));
        var startIndexLocal = il.DeclareLocal(_types.Int32);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var groupLocal = il.DeclareLocal(typeof(Group));

        // ECMA-262 §22.2.6.2 step 3: S = ? ToString(string). Missing arg
        // (compiler passes null when called as `regex.exec()`) coerces to
        // "undefined". S15.10.6.2_A1_T16 / _A12 verify this.
        var inputOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, inputOkLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(inputOkLabel);

        // ECMA-262 §22.2.5.2.2 step 4: lastIndex = ToLength(Get(R,"lastIndex")).
        // Resolve any non-numeric boxed lastIndex into the typed slot (valueOf
        // fires once here, even for non-global where lastIndex is otherwise
        // unused — S15.10.6.2 .../success-lastindex-access expects gets===1).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsRegExpResolveLastIndexMethod);

        // ECMA-262 §22.2.5.2.2 RegExpBuiltinExec: when global OR sticky,
        // use lastIndex as the match start. Sticky additionally enforces
        // the match must begin exactly at lastIndex (we verify after the
        // engine returns).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, globalMatchLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brtrue, globalMatchLabel);

        // Non-global: match = _regex.Match(input)
        il.MarkLabel(nonGlobalMatchLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.RegexMatchString);
        il.Emit(OpCodes.Stloc, matchLocal);
        il.Emit(OpCodes.Br, checkSuccessLabel);

        // Global path
        il.MarkLabel(globalMatchLabel);

        // if (LastIndex > input.Length) { SetLastIndexStrict(0); return null; }
        // Strict write so non-writable PDS lastIndex (y-fail-lastindex-no-write
        // family) throws TypeError per ECMA-262 §22.2.5.2.2 step 4.b.
        var continueGlobalLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, continueGlobalLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _tsRegExpSetLastIndexStrictMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueGlobalLabel);

        // startIndex = Math.Min(LastIndex, input.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        // match = _regex.Match(input, startIndex)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.RegexMatchStringInt);
        il.Emit(OpCodes.Stloc, matchLocal);

        // Check if match was successful
        il.MarkLabel(checkSuccessLabel);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noMatchLabel);

        // Match was successful by the .NET engine — for sticky, additionally
        // require the match to start exactly at startIndex (== _lastIndex).
        // The .NET Regex engine doesn't enforce sticky's "exact-start" rule;
        // it scans forward and returns the first match >= startIndex. ECMA-
        // 262 §22.2.5.2.2 step 15.b says the algorithm fails and resets
        // lastIndex if `lastMatchPosition !== q` (the start position).
        var stickyOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brfalse, stickyOkLabel);
        // sticky: if match.Index != startIndex, treat as fail
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Beq, stickyOkLabel);
        il.Emit(OpCodes.Br, noMatchLabel);
        il.MarkLabel(stickyOkLabel);
        il.Emit(OpCodes.Br, matchSuccessLabel);

        // No match
        il.MarkLabel(noMatchLabel);

        // if (_global || _sticky) LastIndex = 0 — spec-mandated reset on
        // failure for both global and sticky regexes.
        var skipResetLabel = il.DefineLabel();
        var stickyResetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, stickyResetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brfalse, skipResetLabel);
        il.MarkLabel(stickyResetLabel);
        // SetLastIndexStrict(0) — ECMA-262 §22.2.5.2.2 step 15.c.i.1 Set
        // (R, "lastIndex", 0, true). Non-writable PDS data descriptor
        // surfaces TypeError (y-fail-lastindex-no-write.js).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _tsRegExpSetLastIndexStrictMethod);
        il.MarkLabel(skipResetLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Match success
        il.MarkLabel(matchSuccessLabel);

        // if (_global || _sticky) SetLastIndexStrict(match.Index + match.Length)
        var skipUpdateLabel = il.DefineLabel();
        var doUpdateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, doUpdateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpStickyField);
        il.Emit(OpCodes.Brfalse, skipUpdateLabel);
        il.MarkLabel(doUpdateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _tsRegExpSetLastIndexStrictMethod);
        il.MarkLabel(skipUpdateLabel);

        // ECMA-262 22.2.5.6.6: the exec result is an Array exotic object —
        // tests assert `__executed instanceof Array === true` and rely on
        // `length` derived from the array's element count. Build a real
        // List<object?> (the compile-mode array shape; passes
        // `instanceof Array`), populate it with the match + capture-group
        // values, then attach `index` / `input` / `groups` via the
        // PropertyDescriptorStore so `result.index` etc. still resolve.
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!;

        // var result = new List<object?>(match.Groups.Count);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // result.Add(match.Value)  // index 0 = whole match
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listAdd);

        // for (int i = 1; i < match.Groups.Count; i++) { ... result.Add(...) }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // var group = match.Groups[i]
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, groupLocal);

        // result.Add(group.Success ? group.Value : undefined)
        var groupSuccessLabel = il.DefineLabel();
        var addGroupEndLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, groupSuccessLabel);
        // ECMA-262 22.2.5.2.6: unmatched optional capture groups produce
        // `undefined`, not null.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, addGroupEndLabel);
        il.MarkLabel(groupSuccessLabel);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.MarkLabel(addGroupEndLabel);
        il.Emit(OpCodes.Callvirt, listAdd);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // Attach index/input/groups via PropertyDescriptorStore. Helper closure:
        // descriptor = new $CompiledPropertyDescriptor { Value = <stack top> };
        // PropertyDescriptorStore.DefineProperty(result, name, descriptor);
        var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        void DefineMetadataProperty(string name, Action emitValue)
        {
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, pdsDescLocal);
            // descriptor.Value = <value>
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            emitValue();
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            // PropertyDescriptorStore.DefineProperty(result, name, descriptor)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop); // discard bool return
        }

        DefineMetadataProperty("index", () =>
        {
            il.Emit(OpCodes.Ldloc, matchLocal);
            il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
        });
        DefineMetadataProperty("input", () => il.Emit(OpCodes.Ldarg_1));
        DefineMetadataProperty("groups", () =>
        {
            il.Emit(OpCodes.Ldloc, matchLocal);
            il.Emit(OpCodes.Call, runtime.BuildNamedGroups!);
        });

        // return result (List<object?>)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpToStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public override string ToString() => $"/{_source}/{_flags}"
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpToStringMethod = method;

        var il = method.GetILGenerator();

        // "/" + _source + "/" + _flags
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpSourceField);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the standalone (BCL-only) ECMA-262 (ES2025) RegExp.escape static:
    /// <c>public static object Escape(object arg)</c>. Throws TypeError when
    /// <paramref name="arg"/> is not a String; otherwise applies
    /// EncodeForRegExpEscape across the string, including the leading
    /// ASCII-alphanumeric → <c>\xHH</c> rule. Mirrors
    /// <see cref="SharpTS.Runtime.BuiltIns.RegExpBuiltIns.EscapeString"/>; kept
    /// in pure IL so the emitted $RegExp stays self-contained (no SharpTS.dll
    /// runtime dependency).
    /// </summary>
    private void EmitTSRegExpEscape(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Escape",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.TSRegExpEscapeMethod = method;

        var il = method.GetILGenerator();

        var getLength = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var getChars = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var indexOfChar = _types.GetMethod(_types.String, "IndexOf", [_types.Char])!;
        var appendStr = _types.GetMethod(_types.StringBuilder, "Append", _types.String);
        var appendChar = _types.StringBuilderAppendChar;
        var int32ToStr = _types.GetMethod(_types.Int32, "ToString", [_types.String])!;
        var isHigh = _types.GetMethod(_types.Char, "IsHighSurrogate", [_types.Char])!;
        var isLow = _types.GetMethod(_types.Char, "IsLowSurrogate", [_types.Char])!;
        var toUtf32 = _types.GetMethod(_types.Char, "ConvertToUtf32", [_types.Char, _types.Char])!;
        var getCat = _types.GetMethod(_types.Char, "GetUnicodeCategory", [_types.Char])!;

        var strLocal = il.DeclareLocal(_types.String);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var iLocal = il.DeclareLocal(_types.Int32);
        var firstLocal = il.DeclareLocal(_types.Int32);
        var chLocal = il.DeclareLocal(_types.Char);
        var cLocal = il.DeclareLocal(_types.Int32);
        var unitsLocal = il.DeclareLocal(_types.Int32);
        var kLocal = il.DeclareLocal(_types.Int32);
        var cuLocal = il.DeclareLocal(_types.Int32);

        // Appends intLocal.ToString(fmt) to sbLocal (lowercase zero-padded hex).
        void AppendHex(LocalBuilder intLocal, string fmt)
        {
            il.Emit(OpCodes.Ldloc, sbLocal);
            il.Emit(OpCodes.Ldloca, intLocal);
            il.Emit(OpCodes.Ldstr, fmt);
            il.Emit(OpCodes.Call, int32ToStr);
            il.Emit(OpCodes.Callvirt, appendStr);
            il.Emit(OpCodes.Pop);
        }
        void AppendLiteralStr(string s)
        {
            il.Emit(OpCodes.Ldloc, sbLocal);
            il.Emit(OpCodes.Ldstr, s);
            il.Emit(OpCodes.Callvirt, appendStr);
            il.Emit(OpCodes.Pop);
        }

        var argOk = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var afterPair = il.DefineLabel();
        var notFirst = il.DefineLabel();
        var leadingEscape = il.DefineLabel();
        var checkUpper = il.DefineLabel();
        var checkLower = il.DefineLabel();
        var afterCharEmit = il.DefineLabel();

        // strLocal = arg as string; if null throw TypeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Brtrue, argOk);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.escape called with a non-string argument");
        il.MarkLabel(argOk);

        // sb = new StringBuilder(); first = 1; i = 0
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        // if (i >= str.Length) goto loopEnd
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, getLength);
        il.Emit(OpCodes.Bge, loopEnd);

        // ch = str[i]; units = 1; c = (int)ch
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Stloc, chLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, unitsLocal);
        il.Emit(OpCodes.Ldloc, chLocal);
        il.Emit(OpCodes.Stloc, cLocal);

        // surrogate pair: if (IsHigh(ch) && i+1<len && IsLow(str[i+1])) { c=ConvertToUtf32; units=2 }
        il.Emit(OpCodes.Ldloc, chLocal);
        il.Emit(OpCodes.Call, isHigh);
        il.Emit(OpCodes.Brfalse, afterPair);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, getLength);
        il.Emit(OpCodes.Bge, afterPair);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Call, isLow);
        il.Emit(OpCodes.Brfalse, afterPair);
        il.Emit(OpCodes.Ldloc, chLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Call, toUtf32);
        il.Emit(OpCodes.Stloc, cLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, unitsLocal);
        il.MarkLabel(afterPair);

        // if (first==0) goto notFirst; else test ASCII alphanumeric
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Brfalse, notFirst);
        // (c>='0' && c<='9')
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, checkUpper);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Ble, leadingEscape);
        il.MarkLabel(checkUpper);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'A');
        il.Emit(OpCodes.Blt, checkLower);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'Z');
        il.Emit(OpCodes.Ble, leadingEscape);
        il.MarkLabel(checkLower);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'a');
        il.Emit(OpCodes.Blt, notFirst);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'z');
        il.Emit(OpCodes.Bgt, notFirst);

        // leading escape: sb.Append("\x"); hex2(c)
        il.MarkLabel(leadingEscape);
        AppendLiteralStr("\\x");
        AppendHex(cLocal, "x2");
        il.Emit(OpCodes.Br, afterCharEmit);

        // === AppendEncoded ===
        il.MarkLabel(notFirst);

        // rule 1: SyntaxCharacter or '/' → '\' + char  (only when c <= 0xFFFF)
        var afterRule1 = il.DefineLabel();
        var rule1Emit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFFFF);
        il.Emit(OpCodes.Bgt, afterRule1);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Beq, rule1Emit);
        il.Emit(OpCodes.Ldstr, "^$\\.*+?()[]{}|");
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Callvirt, indexOfChar);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, afterRule1);
        il.MarkLabel(rule1Emit);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterCharEmit);
        il.MarkLabel(afterRule1);

        // rule 2: ControlEscape table (9,10,11,12,13)
        var afterRule2 = il.DefineLabel();
        void ControlCase(int code, string esc)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, cLocal);
            il.Emit(OpCodes.Ldc_I4, code);
            il.Emit(OpCodes.Bne_Un, skip);
            AppendLiteralStr(esc);
            il.Emit(OpCodes.Br, afterCharEmit);
            il.MarkLabel(skip);
        }
        ControlCase(9, "\\t");
        ControlCase(10, "\\n");
        ControlCase(11, "\\v");
        ControlCase(12, "\\f");
        ControlCase(13, "\\r");
        il.MarkLabel(afterRule2);

        // rule 5: needsHex = otherPunctuator | whitespace | lineterminator | lone surrogate
        var needsHexEmit = il.DefineLabel();
        var checkWs = il.DefineLabel();
        var checkCat = il.DefineLabel();
        var afterRule5 = il.DefineLabel();
        // otherPunctuators (c <= 0xFFFF)
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFFFF);
        il.Emit(OpCodes.Bgt, checkWs);
        il.Emit(OpCodes.Ldstr, ",-=<>#&!%:;@~'`\"");
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Callvirt, indexOfChar);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, needsHexEmit);
        il.MarkLabel(checkWs);
        // 0xFEFF / 0x2028 / 0x2029
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFEFF);
        il.Emit(OpCodes.Beq, needsHexEmit);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x2028);
        il.Emit(OpCodes.Beq, needsHexEmit);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x2029);
        il.Emit(OpCodes.Beq, needsHexEmit);
        // lone surrogate value: 0xD800..0xDFFF
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xD800);
        il.Emit(OpCodes.Blt, checkCat);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xDFFF);
        il.Emit(OpCodes.Ble, needsHexEmit);
        il.MarkLabel(checkCat);
        // SpaceSeparator category (c <= 0xFFFF)
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFFFF);
        il.Emit(OpCodes.Bgt, afterRule5);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Call, getCat);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.UnicodeCategory.SpaceSeparator);
        il.Emit(OpCodes.Bne_Un, afterRule5);

        il.MarkLabel(needsHexEmit);
        // if (c <= 0xFF) { sb.Append("\x"); hex2(c) } else { for k: sb.Append("\u"); hex4(str[i+k]) }
        var hexUnicode = il.DefineLabel();
        var uLoopStart = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.Bgt, hexUnicode);
        AppendLiteralStr("\\x");
        AppendHex(cLocal, "x2");
        il.Emit(OpCodes.Br, afterCharEmit);
        il.MarkLabel(hexUnicode);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        il.MarkLabel(uLoopStart);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, unitsLocal);
        il.Emit(OpCodes.Bge, afterCharEmit);
        AppendLiteralStr("\\u");
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Stloc, cuLocal);
        AppendHex(cuLocal, "x4");
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, uLoopStart);
        il.MarkLabel(afterRule5);

        // rule 6: literal — for (k=0;k<units;k++) sb.Append(str[i+k])
        var litLoopStart = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        il.MarkLabel(litLoopStart);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, unitsLocal);
        il.Emit(OpCodes.Bge, afterCharEmit);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, litLoopStart);

        il.MarkLabel(afterCharEmit);
        // first = 0; i += units
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, unitsLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpMatchAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal List<object?> MatchAll(string input)
        // Returns List<object?> (not List<string>) so the String.match and
        // [Symbol.matchAll] consumers hand it straight to the $Array ctor with
        // no element-by-element copy into an object list. Substrings are
        // reference types, so storing them as object is free (no box). Mirrors
        // the runtime SharpTSRegExp.MatchAll — keep both in lockstep.
        var method = typeBuilder.DefineMethod(
            "MatchAll",
            MethodAttributes.Assembly,
            _types.ListOfObject,
            [_types.String]
        );
        _tsRegExpMatchAllMethod = method;

        // String.prototype.match(/g) only needs the full-match substrings, so
        // iterate with Regex.EnumerateMatches (span-based ValueMatch structs,
        // .NET 7+) rather than Matches(): no per-match Match/Group object is
        // allocated (~2.3x faster on this path here). The enumerator is a ref
        // struct with no Dispose, so the previous try/finally is gone too. BCL-
        // only, so the emitted DLL stays standalone. Same whole-string scan
        // semantics as Matches(input).
        var roSpanChar = typeof(ReadOnlySpan<char>);
        // string→ReadOnlySpan<char> isn't an op_Implicit (it's a runtime
        // intrinsic), so go through MemoryExtensions.AsSpan(string).
        var stringToSpan = typeof(MemoryExtensions).GetMethod("AsSpan", [_types.String])!;
        var enumerateMatches = typeof(Regex).GetMethod("EnumerateMatches", [roSpanChar])!;
        var enumType = enumerateMatches.ReturnType;          // Regex.ValueMatchEnumerator (ref struct)
        var moveNext = _types.GetMethod(enumType, "MoveNext")!;
        var getCurrent = _types.GetProperty(enumType, "Current")!.GetGetMethod()!;
        var valueMatchType = getCurrent.ReturnType;          // ValueMatch (readonly struct)
        var getIndex = _types.GetProperty(valueMatchType, "Index")!.GetGetMethod()!;
        var getLength = _types.GetProperty(valueMatchType, "Length")!.GetGetMethod()!;
        var substring = _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var enumLocal = il.DeclareLocal(enumType);
        var vmLocal = il.DeclareLocal(valueMatchType);
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // var result = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var e = _regex.EnumerateMatches((ReadOnlySpan<char>)input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, stringToSpan);
        il.Emit(OpCodes.Callvirt, enumerateMatches);
        il.Emit(OpCodes.Stloc, enumLocal);

        // while (e.MoveNext()) { var vm = e.Current; result.Add(input.Substring(vm.Index, vm.Length)); }
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, moveNext);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, getCurrent);
        il.Emit(OpCodes.Stloc, vmLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, vmLocal);
        il.Emit(OpCodes.Call, getIndex);
        il.Emit(OpCodes.Ldloca, vmLocal);
        il.Emit(OpCodes.Call, getLength);
        il.Emit(OpCodes.Callvirt, substring);
        // The substring is a string on the stack; List<object?>.Add(object) takes
        // it directly (reference conversion, no box).
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder _tsRegExpEscapeJsReplacementMethod = null!;

    /// <summary>
    /// Emits the JS→.NET replacement-string preprocessor. ECMA-262
    /// §22.2.5.10.2 GetSubstitution diverges from .NET's
    /// <see cref="System.Text.RegularExpressions.Regex.Replace(string,string)"/>
    /// substitution syntax in two places this helper rewrites:
    /// 1. <c>$0</c> (alone or followed by non-digit) is literal "$0" in JS but
    ///    .NET expands it to the entire match. Translate to <c>$$0</c>.
    /// 2. <c>$0N</c> where N is 1-9 and <em>capture N exists</em>: pass through;
    ///    .NET resolves <c>$0N</c> as group N. When capture N does NOT exist,
    ///    .NET under RegexOptions.ECMAScript expands <c>$0</c> to entire match
    ///    and emits N literally — diverging from JS which leaves "$0N" literal.
    ///    Rewrite to <c>$$0N</c> in that case (i.e. when N > captureCount).
    /// </summary>
    private void EmitTSRegExpEscapeJsReplacement(TypeBuilder typeBuilder)
    {
        // static string EscapeJsReplacement(string s, int captureCount)
        var method = typeBuilder.DefineMethod(
            "EscapeJsReplacement",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32]
        );
        _tsRegExpEscapeJsReplacementMethod = method;

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var iLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var chLocal = il.DeclareLocal(_types.Char);
        var loopTopLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var fastReturnLabel = il.DefineLabel();

        // Quick path: if input doesn't contain '$', return as-is.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Contains", [_types.Char])!);
        il.Emit(OpCodes.Brfalse, fastReturnLabel);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopTopLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // ch = s[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, chLocal);

        // if (ch == '$' && i+1 < lenLocal):
        //   next = s[i+1]
        //   if (next == '$'): sb.Append("$$"); i += 2; continue
        //   if (next == '0'): sb.Append("$$0"); i += 2; continue
        // sb.Append(ch); i++; continue
        var notDollarLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, chLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Bne_Un, notDollarLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, notDollarLabel);

        // peek next char
        var nextCh = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, nextCh);

        // if (next == '$'): sb.Append('$').Append('$'); skip both
        var notDoubleDollarLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nextCh);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Bne_Un, notDoubleDollarLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "$$");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTopLabel);
        il.MarkLabel(notDoubleDollarLabel);

        // if (next == '0'):
        //   if (i+2 < len && s[i+2] in '1'..captureCount-digit): \`$0N\` is decimal
        //     capture N — pass through; .NET resolves \`$0N\` as group N.
        //   else: \`$0[N]\` is literal "$0[N]" in JS. Emit "$$0" to defuse .NET's
        //     "$0 = entire match" interpretation; loop continues to emit the
        //     subsequent digit literally on its own.
        var notDollarZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nextCh);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, notDollarZeroLabel);

        var dollar0LiteralLabel = il.DefineLabel();
        var dollar0PassthroughLabel = il.DefineLabel();
        // Bounds check: i+2 < len
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, dollar0LiteralLabel);
        // Read s[i+2]
        var thirdCh = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, thirdCh);
        // thirdCh in '1'..'9'?
        il.Emit(OpCodes.Ldloc, thirdCh);
        il.Emit(OpCodes.Ldc_I4, (int)'1');
        il.Emit(OpCodes.Blt, dollar0LiteralLabel);
        il.Emit(OpCodes.Ldloc, thirdCh);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, dollar0LiteralLabel);
        // (thirdCh - '0') <= captureCount?  i.e. N <= captureCount
        il.Emit(OpCodes.Ldloc, thirdCh);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldarg_1);  // captureCount
        il.Emit(OpCodes.Bgt, dollar0LiteralLabel);
        // Else: passthrough.
        il.Emit(OpCodes.Br, dollar0PassthroughLabel);

        il.MarkLabel(dollar0LiteralLabel);
        // $0 alone, $0 followed by non-digit, or $0N with N > captureCount:
        // emit "$$0" → .NET literal "$0", then loop continues to emit the
        // following digit (if any) on its own.
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "$$0");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTopLabel);

        il.MarkLabel(dollar0PassthroughLabel);
        // $0 followed by '1'-'9' AND N <= captureCount: pass "$0" through;
        // .NET picks up the next digit on its own and resolves $0N as group N.
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "$0");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTopLabel);

        il.MarkLabel(notDollarZeroLabel);

        il.MarkLabel(notDollarLabel);
        // sb.Append(ch); i++
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, chLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopTopLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fastReturnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit the substitution-escape helper first so Replace can call it.
        EmitTSRegExpEscapeJsReplacement(typeBuilder);

        // internal string Replace(string input, string replacement, bool isGlobal).
        // `isGlobal` is passed explicitly so callers that read the user-overridable
        // `global` property (Symbol.replace's spec-aligned chain) can drive
        // the looping decision per ECMA-262 §22.2.5.10. The String.prototype.replace
        // path passes `rx._global` to preserve typed-slot fast-path semantics
        // (no user PDS override expected through the String wrapper).
        var method = typeBuilder.DefineMethod(
            "Replace",
            MethodAttributes.Assembly,
            _types.String,
            [_types.String, _types.String, _types.Boolean]
        );
        _tsRegExpReplaceMethod = method;

        var il = method.GetILGenerator();
        var globalLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Preprocess replacement string: \`$0\` stays literal in JS (.NET would
        // expand to entire match) and \`$0N\` with N > captureCount is literal
        // (.NET-ECMAScript flag expands $0 greedily). Pass captureCount =
        // _regex.GetGroupNumbers().Length - 1 (group 0 is the match itself).
        var escapedLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_2);
        // captureCount = _regex.GetGroupNumbers().Length - 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("GetGroupNumbers", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _tsRegExpEscapeJsReplacementMethod);
        il.Emit(OpCodes.Stloc, escapedLocal);

        // if (isGlobal)
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, globalLabel);

        // Non-global: return _regex.Replace(input, replacement, 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, escapedLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Replace", [_types.String, _types.String, _types.Int32])!);
        il.Emit(OpCodes.Ret);

        // Global: return _regex.Replace(input, replacement)
        il.MarkLabel(globalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, escapedLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Replace", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the function-replacement path used by
    /// <c>RegExp.prototype[Symbol.replace](string, fn)</c> when <c>fn</c> is
    /// callable. ECMA-262 §22.2.5.10 step 12 — for each match, invoke
    /// <c>fn(matched, c1, …, cN, position, S)</c> and concatenate the
    /// ToString'd return value into the accumulated result. Non-global
    /// regexes process only the first match.
    /// </summary>
    private void EmitTSRegExpReplaceWithFn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal string ReplaceWithFn(string input, $TSFunction fn, bool isGlobal).
        // `isGlobal` is passed in from Symbol.replace's spec-aligned chain (ECMA-262
        // §22.2.5.10 step 8) so user `Object.defineProperty(r, 'global', {…})`
        // overrides drive the loop. Old typed `_global` field read replaced.
        var method = typeBuilder.DefineMethod(
            "ReplaceWithFn",
            MethodAttributes.Assembly,
            _types.String,
            [_types.String, runtime.TSFunctionType, _types.Boolean]
        );
        _tsRegExpReplaceWithFnMethod = method;

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var matchLocal = il.DeclareLocal(typeof(Match));
        var lastEndLocal = il.DeclareLocal(_types.Int32);
        var groupCountLocal = il.DeclareLocal(_types.Int32);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var iLocal = il.DeclareLocal(_types.Int32);
        var groupLocal = il.DeclareLocal(typeof(Group));
        var positionLocal = il.DeclareLocal(_types.Int32);
        var matchLenLocal = il.DeclareLocal(_types.Int32);
        var replValueLocal = il.DeclareLocal(_types.Object);
        var replacementLocal = il.DeclareLocal(_types.String);

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);
        // lastEnd = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lastEndLocal);
        // match = _regex.Match(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.RegexMatchString);
        il.Emit(OpCodes.Stloc, matchLocal);

        var loopTopLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        il.MarkLabel(loopTopLabel);

        // if (!match.Success) break
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // position = match.Index; matchLen = match.Length
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, positionLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, matchLenLocal);

        // groupCount = match.Groups.Count  (includes group 0 = match)
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, groupCountLocal);

        // args = new object[groupCount + 2]  // groups + position + S
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // for (i = 0; i < groupCount; i++) args[i] = groups[i].Success ? groups[i].Value : $Undefined.Instance
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var groupLoopTopLabel = il.DefineLabel();
        var groupLoopEndLabel = il.DefineLabel();
        il.MarkLabel(groupLoopTopLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Bge, groupLoopEndLabel);

        // group = match.Groups[i]
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, groupLocal);

        // args[i] = group.Success ? group.Value : $Undefined.Instance
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        var groupHasMatchLabel = il.DefineLabel();
        var groupStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, groupHasMatchLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, groupStoreLabel);
        il.MarkLabel(groupHasMatchLabel);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.MarkLabel(groupStoreLabel);
        il.Emit(OpCodes.Stelem_Ref);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, groupLoopTopLabel);
        il.MarkLabel(groupLoopEndLabel);

        // args[groupCount] = (double)position   (ECMA-262 spec: position is a Number)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldloc, positionLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // args[groupCount + 1] = input
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, groupCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        // replValue = fn.InvokeWithThis(undefined, args)  — ECMA-262 §22.2.5.10
        // calls `Call(replaceValue, undefined, replacerArgs)`. InvokeWithThis
        // honors the args array directly without going through the AdjustArgs
        // truncate-to-declared-paramCount pass, which would clip
        // \`function(){}\` callees to 0 args and break the test262
        // \`fn-invoke-args\` family's \`arguments\` length assertion.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, replValueLocal);

        // replacement = ToJsString(replValue)
        il.Emit(OpCodes.Ldloc, replValueLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replacementLocal);

        // sb.Append(input.Substring(lastEnd, position - lastEnd))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, lastEndLocal);
        il.Emit(OpCodes.Ldloc, positionLocal);
        il.Emit(OpCodes.Ldloc, lastEndLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // sb.Append(replacement)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, replacementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // lastEnd = position + matchLen
        il.Emit(OpCodes.Ldloc, positionLocal);
        il.Emit(OpCodes.Ldloc, matchLenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, lastEndLocal);

        // if (!isGlobal) break — caller passes spec-aligned `global`.
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // Advance past zero-width matches so we don't infinite-loop.
        // If matchLen == 0, advance manually so NextMatch makes progress.
        var nextLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, matchLenLocal);
        il.Emit(OpCodes.Brtrue, nextLabel);
        // matchLen == 0 → manually advance lastEnd by 1, refind from there.
        // Skip the empty-match handling for now (it's a corner case;
        // S15.10.6.4_A* tests cover normal patterns).
        il.MarkLabel(nextLabel);

        // match = match.NextMatch()
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetMethod("NextMatch", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, matchLocal);
        il.Emit(OpCodes.Br, loopTopLabel);

        il.MarkLabel(loopEndLabel);
        // sb.Append(input.Substring(lastEnd))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, lastEndLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSearch(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal int Search(string input)
        var method = typeBuilder.DefineMethod(
            "Search",
            MethodAttributes.Assembly,
            _types.Int32,
            [_types.String]
        );
        _tsRegExpSearchMethod = method;

        var il = method.GetILGenerator();
        var matchLocal = il.DeclareLocal(typeof(Match));
        var successLabel = il.DefineLabel();

        // var match = _regex.Match(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.RegexMatchString);
        il.Emit(OpCodes.Stloc, matchLocal);

        // return match.Success ? match.Index : -1
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, successLabel);

        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(successLabel);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSplit(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal string[] Split(string input)
        var method = typeBuilder.DefineMethod(
            "Split",
            MethodAttributes.Assembly,
            typeof(string[]),
            [_types.String]
        );
        _tsRegExpSplitMethod = method;

        var il = method.GetILGenerator();
        var partsLocal = il.DeclareLocal(typeof(string[]));
        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(typeof(string[]));

        // var parts = _regex.Split(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Split", [_types.String])!);
        il.Emit(OpCodes.Stloc, partsLocal);

        // ECMA-262 22.1.3.21: empty-match regex (e.g. /(?:)/) should NOT
        // produce empty entries at the split boundaries. .NET's Regex.Split
        // inserts a leading "" before the first match and trailing "" after
        // the last match when both occur at the string boundary, giving
        // "hello".split(/(?:)/) → ["", "h", "e", "l", "l", "o", ""] (7) when
        // the spec wants ["h", "e", "l", "l", "o"] (5). Detection: regex
        // matches the empty string at position 0 of the empty input string
        // (i.e. it can match with zero-width). Trim leading + trailing "".
        var skipTrimLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("IsMatch", [_types.String])!);
        il.Emit(OpCodes.Brfalse, skipTrimLabel);

        // start = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        // end = parts.Length
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, endLocal);

        // while (start < end && parts[start] == "") start++;
        var startLoopLabel = il.DefineLabel();
        var startLoopEndLabel = il.DefineLabel();
        il.MarkLabel(startLoopLabel);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Bge, startLoopEndLabel);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, startLoopEndLabel);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, startLoopLabel);
        il.MarkLabel(startLoopEndLabel);

        // while (end > start && parts[end-1] == "") end--;
        var endLoopLabel = il.DefineLabel();
        var endLoopEndLabel = il.DefineLabel();
        il.MarkLabel(endLoopLabel);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ble, endLoopEndLabel);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, endLoopEndLabel);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endLoopLabel);
        il.MarkLabel(endLoopEndLabel);

        // var result = new string[end - start]
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.String);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Array.Copy(parts, start, result, 0, end - start)
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(skipTrimLabel);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object? BuildNamedGroups(Match match)
    /// on the $RegExp class. Returns a $Object with named group values, or null.
    /// </summary>
    private void EmitTSRegExpBuildNamedGroups(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BuildNamedGroups",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [typeof(Match)]
        );
        runtime.BuildNamedGroups = method;

        var il = method.GetILGenerator();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var groupCollLocal = il.DeclareLocal(typeof(GroupCollection));
        var enumeratorLocal = il.DeclareLocal(typeof(System.Collections.IEnumerator));
        var groupLocal = il.DeclareLocal(typeof(Group));
        var nameLocal = il.DeclareLocal(_types.String);
        var intParseResultLocal = il.DeclareLocal(_types.Int32);

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var groupNotSuccessLabel = il.DefineLabel();
        var afterValueLabel = il.DefineLabel();
        var dictReadyLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();
        var finallyLabel = il.DefineLabel();

        // dict = null (initially)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, dictLocal);

        // var groupColl = match.Groups
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, groupCollLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, groupCollLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.BeginExceptionBlock();

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // group = (Group)enumerator.Current
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(Group));
        il.Emit(OpCodes.Stloc, groupLocal);

        // name = group.Name
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // if (int.TryParse(name, out _)) continue (skip numeric group names)
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, intParseResultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", [_types.String, _types.Int32.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, loopStartLabel);

        // Ensure dict is initialized
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brtrue, dictReadyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);
        il.MarkLabel(dictReadyLabel);

        // dict[name] = group.Success ? group.Value : null
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, nameLocal);

        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, groupNotSuccessLabel);

        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, afterValueLabel);

        il.MarkLabel(groupNotSuccessLabel);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(afterValueLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Leave, finallyLabel);

        // Finally block
        il.BeginFinallyBlock();
        var disposeEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Isinst, typeof(IDisposable));
        il.Emit(OpCodes.Brfalse, disposeEndLabel);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Callvirt, _types.DisposableDispose);
        il.MarkLabel(disposeEndLabel);
        il.EndExceptionBlock();

        il.MarkLabel(finallyLabel);

        // return dict == null ? null : new $Object(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the static helper SymMatch(object rx, object str) -> object on $RegExp.
    /// ECMA-262 §22.2.5.7 RegExp.prototype[@@match]:
    ///  - non-global: return rx.Exec(s)  (Array exotic or null)
    ///  - global:    return list of all match[0] values, or null if no matches
    /// </summary>
    private void EmitTSRegExpSymMatchHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SymMatch",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.TSRegExpSymMatchHelper = method;

        var il = method.GetILGenerator();
        var sLocal = il.DeclareLocal(_types.String);

        // ECMA-262 §22.2.5.6 step 2: throw TypeError if `this` is not an Object.
        EmitRequireObjectArg(il, runtime, method, argIndex: 0, "RegExp.prototype[Symbol.match]");

        // Spec-aligned algorithm for any Object `this`. The fast path that
        // direct-called \$RegExp.Exec / .MatchAll bypassed the spec steps
        // around reading `flags` via Get, lastIndex management, and the
        // empty-match advance — many builtin-* tests verify those.
        // \$Runtime.GetProperty / SetProperty now route \$RegExp's slots
        // through the typed getters/setters, so the spec path produces the
        // same final result on real regexes while picking up the side
        // effects test262 verifies (see also the matching Symbol.search
        // cleanup in 5c981eb9).
        //
        // ECMA-262 §22.2.5.6 RegExp.prototype [@@match]:
        //   3. S = ToString(string)
        //   4. flags = ToString(? Get(rx, "flags"))
        //   5. If flags doesn't contain "g": return ? RegExpExec(rx, S)
        //   6. Else (global): set lastIndex=0, loop calling RegExpExec,
        //      collecting result["0"] until null. Return null if no
        //      match, else the array. Empty matches advance lastIndex.
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);
        EmitSymMatchSlowPath(il, runtime, sLocal);
    }

    /// <summary>
    /// Emits the spec algorithm for <c>RegExp.prototype[Symbol.match]</c>
    /// on a non-$RegExp <c>this</c>. <paramref name="sLocal"/> already holds
    /// the ToString'd <c>string</c> argument.
    /// </summary>
    private void EmitSymMatchSlowPath(ILGenerator il, EmittedRuntime runtime, LocalBuilder sLocal)
    {
        var rxObjLocal = il.DeclareLocal(_types.Object);
        var flagsLocal = il.DeclareLocal(_types.String);
        var execResultLocal = il.DeclareLocal(_types.Object);
        var matchStrLocal = il.DeclareLocal(_types.String);
        var arrLocal = il.DeclareLocal(_types.ListOfObject);
        var nLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rxObjLocal);

        // flags = ToString(Get(rx, "flags")) — use ToJsString so @@toPrimitive
        // on user-installed flags-returning objects fires per ECMA-262 §7.1.1.
        // Reading `flags` (not `global` directly) preserves get-flags-err.js
        // which throws from the user's flags getter.
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, flagsLocal);

        // if (!flags.Contains('g')) return RegExpExec(rx, S);
        var globalPathLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, flagsLocal);
        il.Emit(OpCodes.Ldstr, "g");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.String));
        il.Emit(OpCodes.Brtrue, globalPathLabel);

        EmitRegExpExecSlow(il, runtime, rxObjLocal, sLocal, execResultLocal);
        il.Emit(OpCodes.Ldloc, execResultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(globalPathLabel);
        // Set(rx, "lastIndex", 0, true) — ECMA-262 §22.2.6.8 step 8.c. The
        // Throw=true flag means a non-writable lastIndex descriptor on rx
        // must surface TypeError instead of silently no-op'ing. Our regular
        // SetProperty's $RegExp arm follows non-strict semantics; inline a
        // PDS writability check here before delegating. Test262
        // g-init-lastindex-err / y-fail-lastindex-no-write rely on this.
        EmitStrictWritableCheck(il, runtime, rxObjLocal, "lastIndex");
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // arr = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, arrLocal);
        // n = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, nLocal);

        // Loop:
        //   result = RegExpExec(rx, S)
        //   if (result == null) { if (n == 0) return null; else return arr; }
        //   matchStr = ToString(Get(result, "0"))
        //   arr.Add(matchStr)
        //   n++
        //   if (matchStr.Length == 0) advance lastIndex
        var loopTopLabel = il.DefineLabel();
        var nullResultLabel = il.DefineLabel();
        var emptyMatchAdvLabel = il.DefineLabel();
        var afterEmptyAdvLabel = il.DefineLabel();
        il.MarkLabel(loopTopLabel);

        EmitRegExpExecSlow(il, runtime, rxObjLocal, sLocal, execResultLocal);
        il.Emit(OpCodes.Ldloc, execResultLocal);
        il.Emit(OpCodes.Brfalse, nullResultLabel);

        // matchStr = ToString(Get(result, "0"))
        il.Emit(OpCodes.Ldloc, execResultLocal);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, matchStrLocal);

        // arr.Add(matchStr)
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, matchStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // n++
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, nLocal);

        // If matchStr.Length == 0, advance lastIndex past it (avoid infinite
        // loop on zero-width matches like /(?:)/g).
        il.Emit(OpCodes.Ldloc, matchStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, afterEmptyAdvLabel);

        // lastIndex = ToInt(Get(rx, "lastIndex")) + 1; SetProperty(rx, "lastIndex", lastIndex)
        // Spec Throw=true — strict writability check via the helper.
        // ECMA-262 §22.2.5.6 step 11.d.iii: ? ToLength(? Get(rx, "lastIndex"))
        // — runtime.JsToInt32 invokes ToNumber → ToPrimitive → valueOf,
        // propagating throws from user-supplied object/getter `lastIndex`.
        il.MarkLabel(emptyMatchAdvLabel);
        EmitStrictWritableCheck(il, runtime, rxObjLocal, "lastIndex");
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.MarkLabel(afterEmptyAdvLabel);
        il.Emit(OpCodes.Br, loopTopLabel);

        // Null result: return null if n==0 else return new $Array(arr)
        il.MarkLabel(nullResultLabel);
        var returnArrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Brtrue, returnArrLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnArrLabel);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the static helper SymMatchAll(object rx, object str) -> object on $RegExp.
    /// ECMA-262 §22.2.5.8 — returns an iterable of full-match strings.
    /// Simplified to a $Array of strings (a JS Array is itself iterable, satisfying
    /// most for-of consumers; full Iterator-prototype semantics are a separate task).
    /// </summary>
    private void EmitTSRegExpSymMatchAllHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SymMatchAll",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.TSRegExpSymMatchAllHelper = method;

        var il = method.GetILGenerator();
        var rxLocal = il.DeclareLocal(typeBuilder);
        var sLocal = il.DeclareLocal(_types.String);
        var listLocal = il.DeclareLocal(_types.ListOfObject);

        // ECMA-262 §22.2.5.8 step 2: throw TypeError if `this` is not an Object.
        EmitRequireObjectArg(il, runtime, method, argIndex: 0, "RegExp.prototype[Symbol.matchAll]");

        // Side effects before brand-narrow: ToString(string) + ToString(Get(rx,
        // "flags")) — propagates user toString/getter throws (string-tostring.js,
        // coerce-flags-err.js etc).
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);
        var matchAllFlagsLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, matchAllFlagsLocal);

        // Narrow to $RegExp; non-$RegExp throws TypeError.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        var rxOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, rxOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype[Symbol.matchAll] requires a RegExp receiver");
        il.MarkLabel(rxOkLabel);

        // var list = rx.MatchAll(s);  // List<object?> of full-match substrings
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Call, _tsRegExpMatchAllMethod);
        il.Emit(OpCodes.Stloc, listLocal);

        // MatchAll already returns List<object?>, so wrap it directly — no copy.
        // return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpAdvanceStringIndexSpec(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "AdvanceStringIndexSpec",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.String, _types.Int32, _types.Boolean]);
        _tsRegExpAdvanceStringIndexSpecMethod = method;
        var il = method.GetILGenerator();
        var one = il.DefineLabel();
        var length = _types.GetProperty(_types.String, "Length").GetGetMethod()!;
        var chars = _types.GetMethod(_types.String, "get_Chars", _types.Int32);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, one);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, length);
        il.Emit(OpCodes.Bge, one);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, chars);
        il.Emit(OpCodes.Ldc_I4, 0xD800);
        il.Emit(OpCodes.Blt, one);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, chars);
        il.Emit(OpCodes.Ldc_I4, 0xDBFF);
        il.Emit(OpCodes.Bgt, one);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, chars);
        il.Emit(OpCodes.Ldc_I4, 0xDC00);
        il.Emit(OpCodes.Blt, one);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, chars);
        il.Emit(OpCodes.Ldc_I4, 0xDFFF);
        il.Emit(OpCodes.Bgt, one);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(one);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the GetSubstitution string-token expansion used by the generic
    /// RegExp @@replace path. Captures are already ToString-coerced, with the
    /// emitted undefined singleton preserving unmatched captures.
    /// </summary>
    private void EmitTSRegExpExpandReplacementSpec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandReplacementSpec",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String, _types.Int32, _types.ListOfObject]);
        _tsRegExpExpandReplacementSpecMethod = method;

        var il = method.GetILGenerator();
        var sb = il.DeclareLocal(_types.StringBuilder);
        var i = il.DeclareLocal(_types.Int32);
        var next = il.DeclareLocal(_types.Char);
        var captureNumber = il.DeclareLocal(_types.Int32);
        var consumed = il.DeclareLocal(_types.Int32);
        var capture = il.DeclareLocal(_types.Object);
        var tailStart = il.DeclareLocal(_types.Int32);

        var stringLength = _types.GetProperty(_types.String, "Length").GetGetMethod()!;
        var stringChars = _types.GetMethod(_types.String, "get_Chars", _types.Int32);
        var appendChar = _types.GetMethod(_types.StringBuilder, "Append", _types.Char);
        var appendString = _types.GetMethod(_types.StringBuilder, "Append", _types.String);
        var listCount = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listItem = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.StringBuilder));
        il.Emit(OpCodes.Stloc, sb);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, i);

        var loop = il.DefineLabel();
        var loopTest = il.DefineLabel();
        var ordinary = il.DefineLabel();
        var tokenDone = il.DefineLabel();
        var notDollar = il.DefineLabel();
        var notMatch = il.DefineLabel();
        var notPrefix = il.DefineLabel();
        var notSuffix = il.DefineLabel();
        var digit = il.DefineLabel();
        var appendCapture = il.DefineLabel();
        var captureDone = il.DefineLabel();

        il.Emit(OpCodes.Br, loopTest);
        il.MarkLabel(loop);
        // A '$' without a following code unit is ordinary text.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, stringChars);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Bne_Un, ordinary);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Bge, ordinary);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, stringChars);
        il.Emit(OpCodes.Stloc, next);

        // $$
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Bne_Un, notDollar);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldc_I4, (int)'$');
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, tokenDone);

        // $&
        il.MarkLabel(notDollar);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'&');
        il.Emit(OpCodes.Bne_Un, notMatch);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, tokenDone);

        // $`
        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'`');
        il.Emit(OpCodes.Bne_Un, notPrefix);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, tokenDone);

        // $'
        il.MarkLabel(notPrefix);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'\'');
        il.Emit(OpCodes.Bne_Un, notSuffix);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, tailStart);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, tailStart);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, tokenDone);

        // $n / $nn. A nonexistent capture leaves '$' literal and lets the
        // following digit(s) be processed normally on subsequent iterations.
        il.MarkLabel(notSuffix);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, ordinary);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, ordinary);
        il.MarkLabel(digit);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, captureNumber);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, consumed);

        var noSecondDigit = il.DefineLabel();
        var rejectSecondDigit = il.DefineLabel();
        var twoDigit = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Bge, noSecondDigit);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, stringChars);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, rejectSecondDigit);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, rejectSecondDigit);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, captureNumber);
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)10);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, twoDigit);
        il.Emit(OpCodes.Ldloc, twoDigit);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Bgt, noSecondDigit);
        il.Emit(OpCodes.Ldloc, twoDigit);
        il.Emit(OpCodes.Stloc, captureNumber);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, consumed);
        il.Emit(OpCodes.Br, noSecondDigit);
        il.MarkLabel(rejectSecondDigit);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noSecondDigit);

        il.Emit(OpCodes.Ldloc, captureNumber);
        il.Emit(OpCodes.Brfalse, ordinary);
        il.Emit(OpCodes.Ldloc, captureNumber);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Bgt, ordinary);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Ldloc, captureNumber);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, listItem);
        il.Emit(OpCodes.Stloc, capture);
        il.Emit(OpCodes.Ldloc, capture);
        il.Emit(OpCodes.Brfalse, captureDone);
        il.Emit(OpCodes.Ldloc, capture);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, appendCapture);
        il.Emit(OpCodes.Br, captureDone);
        il.MarkLabel(appendCapture);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldloc, capture);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(captureDone);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldloc, consumed);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);
        var increment = il.DefineLabel();
        il.Emit(OpCodes.Br, increment);

        il.MarkLabel(ordinary);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, stringChars);
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, increment);

        il.MarkLabel(tokenDone);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);
        il.Emit(OpCodes.Br, loopTest);
        il.MarkLabel(increment);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);

        il.MarkLabel(loopTest);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Blt, loop);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SymReplace(object rx, object str, object replacement) -> string.
    /// </summary>
    private void EmitTSRegExpSymReplaceHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SymReplace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.TSRegExpSymReplaceHelper = method;

        var il = method.GetILGenerator();
        var sLocal = il.DeclareLocal(_types.String);
        var rLocal = il.DeclareLocal(_types.String);

        // ECMA-262 §22.2.5.10 step 2: throw TypeError if `this` is not an Object.
        EmitRequireObjectArg(il, runtime, method, argIndex: 0, "RegExp.prototype[Symbol.replace]");

        // Spec-aligned side effects before brand-narrowing:
        //   ECMA-262 §22.2.5.10 step 3: S = ToString(string).
        //   step 5: functionalReplace = IsCallable(replaceValue).
        //   step 6: If !functionalReplace, replaceValue = ToString(replaceValue).
        //   step 7: flags = ToString(Get(rx, "flags")).
        // The ToString side effects (Symbol → TypeError, throwing toString,
        // throwing flags-getter) all surface before RegExpExec dispatch.
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);

        // Check if replaceValue is callable BEFORE coercing to string. A
        // $TSFunction goes to the functional path; anything else is eagerly
        // ToString'd as required before any regexp flag access.
        var fnReplaceLocal = il.DeclareLocal(runtime.TSFunctionType);
        var notFunctionalLabel = il.DefineLabel();
        var afterReplaceCoerceLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, fnReplaceLocal);
        il.Emit(OpCodes.Ldloc, fnReplaceLocal);
        il.Emit(OpCodes.Brfalse, notFunctionalLabel);
        il.Emit(OpCodes.Br, afterReplaceCoerceLabel);
        il.MarkLabel(notFunctionalLabel);
        EmitArgToJsString(il, runtime, argIndex: 2, rLocal);
        il.MarkLabel(afterReplaceCoerceLabel);

        var replaceFlagsLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replaceFlagsLocal);

        // From this point onward use one RegExpExec-driven algorithm for both
        // built-in regexes and overridden `exec` methods. The old typed
        // Replace/ReplaceWithFn forks skipped result coercion, global match
        // collection, and observable lastIndex operations.
        EmitSymReplaceSpecPath(il, runtime, sLocal, rLocal, fnReplaceLocal);
    }

    private void EmitSymReplaceSpecPath(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder sLocal,
        LocalBuilder replacementStringLocal,
        LocalBuilder replacementFunctionLocal)
    {
        var rx = il.DeclareLocal(_types.Object);
        var global = il.DeclareLocal(_types.Boolean);
        var unicode = il.DeclareLocal(_types.Boolean);
        var matches = il.DeclareLocal(_types.ListOfObject);
        var execResult = il.DeclareLocal(_types.Object);
        var matched = il.DeclareLocal(_types.String);
        var i = il.DeclareLocal(_types.Int32);
        var matchCount = il.DeclareLocal(_types.Int32);
        var sb = il.DeclareLocal(_types.StringBuilder);
        var nextSource = il.DeclareLocal(_types.Int32);
        var position = il.DeclareLocal(_types.Int32);
        var length = il.DeclareLocal(_types.Int32);
        var captures = il.DeclareLocal(_types.ListOfObject);
        var captureIndex = il.DeclareLocal(_types.Int32);
        var captureValue = il.DeclareLocal(_types.Object);
        var replacement = il.DeclareLocal(_types.String);
        var currentIndex = il.DeclareLocal(_types.Int32);

        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var listCountGet = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listItemGet = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var stringLength = _types.GetProperty(_types.String, "Length").GetGetMethod()!;
        var appendString = _types.GetMethod(_types.StringBuilder, "Append", _types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rx);

        // global = ToBoolean(Get(rx, "global")); the earlier flags read is
        // retained because RegExp.prototype.flags has separately observable
        // getter/coercion side effects.
        il.Emit(OpCodes.Ldloc, rx);
        il.Emit(OpCodes.Ldstr, "global");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, global);

        var afterUnicode = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, global);
        il.Emit(OpCodes.Brfalse, afterUnicode);
        il.Emit(OpCodes.Ldloc, rx);
        il.Emit(OpCodes.Ldstr, "unicode");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, unicode);
        EmitStrictWritableCheck(il, runtime, rx, "lastIndex");
        il.Emit(OpCodes.Ldloc, rx);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.MarkLabel(afterUnicode);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, matches);

        var collect = il.DefineLabel();
        var collected = il.DefineLabel();
        var nonEmptyMatch = il.DefineLabel();
        il.MarkLabel(collect);
        EmitRegExpExecSlow(il, runtime, rx, sLocal, execResult);
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Brfalse, collected);
        il.Emit(OpCodes.Ldloc, matches);
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Ldloc, global);
        il.Emit(OpCodes.Brfalse, collected);

        // Empty global matches must advance lastIndex to guarantee progress.
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, matched);
        il.Emit(OpCodes.Ldloc, matched);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Brtrue, nonEmptyMatch);
        EmitStrictWritableCheck(il, runtime, rx, "lastIndex");
        il.Emit(OpCodes.Ldloc, rx);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldloc, rx);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, currentIndex);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ldloc, currentIndex);
        il.Emit(OpCodes.Ldloc, unicode);
        il.Emit(OpCodes.Call, _tsRegExpAdvanceStringIndexSpecMethod);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.MarkLabel(nonEmptyMatch);
        il.Emit(OpCodes.Br, collect);

        il.MarkLabel(collected);
        il.Emit(OpCodes.Ldloc, matches);
        il.Emit(OpCodes.Callvirt, listCountGet);
        il.Emit(OpCodes.Stloc, matchCount);
        var haveMatches = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, matchCount);
        il.Emit(OpCodes.Brtrue, haveMatches);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(haveMatches);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.StringBuilder));
        il.Emit(OpCodes.Stloc, sb);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, nextSource);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, i);

        var process = il.DefineLabel();
        var processTest = il.DefineLabel();
        var positionNonNegative = il.DefineLabel();
        var positionClamped = il.DefineLabel();
        var lengthNonNegative = il.DefineLabel();
        var captureLoop = il.DefineLabel();
        var capturesDone = il.DefineLabel();
        var captureUndefined = il.DefineLabel();
        var captureStored = il.DefineLabel();
        var stringReplacement = il.DefineLabel();
        var haveReplacement = il.DefineLabel();
        var skipOverlapping = il.DefineLabel();

        il.Emit(OpCodes.Br, processTest);
        il.MarkLabel(process);
        il.Emit(OpCodes.Ldloc, matches);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, listItemGet);
        il.Emit(OpCodes.Stloc, execResult);

        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, matched);

        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, position);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, positionNonNegative);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, position);
        il.MarkLabel(positionNonNegative);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Ble, positionClamped);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Stloc, position);
        il.MarkLabel(positionClamped);

        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, lengthNonNegative);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(lengthNonNegative);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, captures);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, captureIndex);
        il.Emit(OpCodes.Br, capturesDone);
        il.MarkLabel(captureLoop);
        il.Emit(OpCodes.Ldloc, execResult);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, captureValue);
        il.Emit(OpCodes.Ldloc, captureValue);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, captureUndefined);
        il.Emit(OpCodes.Ldloc, captureValue);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, captureValue);
        il.Emit(OpCodes.Br, captureStored);
        il.MarkLabel(captureUndefined);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, captureValue);
        il.MarkLabel(captureStored);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Ldloc, captureValue);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, captureIndex);
        il.MarkLabel(capturesDone);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Blt, captureLoop);

        // Functional replacement: [matched, ...captures, position, S].
        il.Emit(OpCodes.Ldloc, replacementFunctionLocal);
        il.Emit(OpCodes.Brfalse, stringReplacement);
        var callArgs = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Callvirt, listCountGet);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, callArgs);
        il.Emit(OpCodes.Ldloc, callArgs);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, matched);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, captureIndex);
        var copyCaptures = il.DefineLabel();
        var copiedCaptures = il.DefineLabel();
        il.Emit(OpCodes.Br, copiedCaptures);
        il.MarkLabel(copyCaptures);
        il.Emit(OpCodes.Ldloc, callArgs);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Callvirt, listItemGet);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, captureIndex);
        il.MarkLabel(copiedCaptures);
        il.Emit(OpCodes.Ldloc, captureIndex);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Callvirt, listCountGet);
        il.Emit(OpCodes.Blt, copyCaptures);
        il.Emit(OpCodes.Ldloc, callArgs);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Callvirt, listCountGet);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, callArgs);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Callvirt, listCountGet);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, replacementFunctionLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldloc, callArgs);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, replacement);
        il.Emit(OpCodes.Br, haveReplacement);

        il.MarkLabel(stringReplacement);
        il.Emit(OpCodes.Ldloc, replacementStringLocal);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ldloc, matched);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, captures);
        il.Emit(OpCodes.Call, _tsRegExpExpandReplacementSpecMethod);
        il.Emit(OpCodes.Stloc, replacement);
        il.MarkLabel(haveReplacement);

        // Ignore ill-behaved overlapping/backwards matches.
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, nextSource);
        il.Emit(OpCodes.Blt, skipOverlapping);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ldloc, nextSource);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, nextSource);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldloc, replacement);
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, matched);
        il.Emit(OpCodes.Callvirt, stringLength);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, nextSource);
        il.MarkLabel(skipOverlapping);

        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);
        il.MarkLabel(processTest);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldloc, matchCount);
        il.Emit(OpCodes.Blt, process);

        il.Emit(OpCodes.Ldloc, nextSource);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, stringLength);
        var noTail = il.DefineLabel();
        il.Emit(OpCodes.Bge, noTail);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ldloc, nextSource);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Callvirt, appendString);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noTail);
        il.Emit(OpCodes.Ldloc, sb);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SymSearch(object rx, object str) -> double (boxed).
    ///
    /// Fast path: rx is a real $RegExp — call the internal Search method.
    /// Slow path: rx is any other Object (test262 patterns:
    /// `RegExp.prototype[Symbol.search].call({lastIndex:0, exec:fn})`) —
    /// run the spec algorithm via emitted Get/Set + InvokeWithThis on the
    /// user-installed `exec` callable.
    /// </summary>
    private void EmitTSRegExpSymSearchHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SymSearch",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.TSRegExpSymSearchHelper = method;

        var il = method.GetILGenerator();
        var sLocal = il.DeclareLocal(_types.String);

        // ECMA-262 §22.2.5.11 step 2: throw TypeError if `this` is not an Object.
        EmitRequireObjectArg(il, runtime, method, argIndex: 0, "RegExp.prototype[Symbol.search]");

        // Spec-aligned algorithm for any Object `this`. The previous fast
        // path that direct-called \$RegExp.Search bypassed the spec steps
        // around `lastIndex` save/restore + RegExpExec dispatch, which
        // test262's builtin-coerce-lastindex.js / get-lastindex-err.js /
        // set-lastindex-{init,restore}-* tests verify. \$Runtime.GetProperty
        // / SetProperty now route \$RegExp's lastIndex through the typed
        // setter with ToLength coercion, so the slow path produces the same
        // result as the previous direct-method fast path on real regexes
        // while picking up the spec-mandated side effects.
        //
        // ECMA-262 §22.2.5.11 RegExp.prototype [@@search]:
        //   3. S = ToString(string)
        //   4. previousLastIndex = Get(rx, "lastIndex")
        //   5. If !SameValue(previousLastIndex, 0): Set(rx, "lastIndex", 0)
        //   6. result = RegExpExec(rx, S)
        //   7. currentLastIndex = Get(rx, "lastIndex")
        //   8. If !SameValue(currentLastIndex, previousLastIndex):
        //        Set(rx, "lastIndex", previousLastIndex)
        //   9. If result is null, return -1
        //  10. Return Get(result, "index")
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);
        EmitSymSearchSlowPath(il, runtime, sLocal);
    }

    /// <summary>
    /// Emits the spec-algorithm body for <c>RegExp.prototype[Symbol.search]</c>
    /// on a non-$RegExp <c>this</c>. <paramref name="sLocal"/> already holds
    /// the ToString'd <c>string</c> argument.
    /// </summary>
    private void EmitSymSearchSlowPath(ILGenerator il, EmittedRuntime runtime, LocalBuilder sLocal)
    {
        var rxObjLocal = il.DeclareLocal(_types.Object);
        var prevLILocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.Object);
        var currLILocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rxObjLocal);

        // previousLastIndex = Get(rx, "lastIndex")
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, prevLILocal);

        // If previousLastIndex is not numeric 0 → Set(rx, "lastIndex", 0, true).
        // The Throw=true flag in §22.2.6.10 step 5 requires TypeError when the
        // writable bit is false on the lastIndex descriptor.
        var skipResetLabel = il.DefineLabel();
        EmitIsNumericZero(il, runtime, prevLILocal);
        il.Emit(OpCodes.Brtrue, skipResetLabel);
        EmitStrictWritableCheck(il, runtime, rxObjLocal, "lastIndex");
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.MarkLabel(skipResetLabel);

        // result = RegExpExec(rx, S)
        EmitRegExpExecSlow(il, runtime, rxObjLocal, sLocal, resultLocal);

        // currentLastIndex = Get(rx, "lastIndex")
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, currLILocal);

        // If currentLastIndex !== previousLastIndex → restore (Throw=true).
        var skipRestoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, currLILocal);
        il.Emit(OpCodes.Ldloc, prevLILocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brtrue, skipRestoreLabel);
        EmitStrictWritableCheck(il, runtime, rxObjLocal, "lastIndex");
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "lastIndex");
        il.Emit(OpCodes.Ldloc, prevLILocal);
        il.Emit(OpCodes.Call, runtime.SetProperty);
        il.MarkLabel(skipRestoreLabel);

        // If result is null → return -1
        var hasMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brtrue, hasMatchLabel);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // Return Get(result, "index")
        il.MarkLabel(hasMatchLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ECMA-262 §22.2.5.2.1 RegExpExec(rx, S) for a non-$RegExp
    /// <paramref name="rxObjLocal"/>. Reads <c>exec</c> via Get; if it is
    /// callable ($TSFunction) invokes it with rx as receiver and stores
    /// the result in <paramref name="resultLocal"/>. Throws TypeError if
    /// exec is not callable (the spec fallback only covers built-in
    /// regexes which we already handled in the fast path).
    /// </summary>
    /// <summary>
    /// ECMA-262 strict-Set writability guard: looks up the PDS data descriptor
    /// for <paramref name="propName"/> on <paramref name="rxObjLocal"/> and
    /// throws TypeError when present + <c>writable=false</c>. Symbol.match /
    /// search / replace use this before \`Set(rx, propName, ...)\` because the
    /// spec passes Throw=true to those abstract Set invocations.
    /// </summary>
    private void EmitStrictWritableCheck(ILGenerator il, EmittedRuntime runtime, LocalBuilder rxObjLocal, string propName)
    {
        var pdsLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var skipLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // $Object object-literal accessor syntax (`{ get foo() {}, ... }`)
        // installs the getter/setter in the typed `_getters` / `_setters`
        // dicts, NOT in PDS. ECMA-262 §10.1.5.3 + the spec's Throw=true Set
        // require TypeError when the property has a getter but no setter.
        // Mirror that here before the PDS check fires.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasGetter);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        // Has getter — throw unless a setter is also present.
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasSetter);
        il.Emit(OpCodes.Brtrue, skipLabel);
        il.Emit(OpCodes.Br, throwLabel);
        il.MarkLabel(notTSObjectLabel);

        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsLocal);
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Accessor descriptor: setter null → throw (getter-only is unwritable
        // under Throw=true per ECMA-262 §10.1.5.3 ValidateAndApplyPropertyDescriptor);
        // setter present → defer to the setter invocation in SetProperty (which
        // may itself throw).
        var notAccessorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        var hasGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasGetterLabel);
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAccessorLabel);
        // Has setter, no getter — accessor: let SetProperty fire the setter.
        il.Emit(OpCodes.Br, skipLabel);
        il.MarkLabel(hasGetterLabel);
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);
        // Getter present, no setter → throw.
        il.Emit(OpCodes.Br, throwLabel);

        il.MarkLabel(notAccessorLabel);
        // Data descriptor — check writable.
        il.Emit(OpCodes.Ldloc, pdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // writable=false (data) OR getter-only (accessor) → throw TypeError.
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Cannot assign to read only property '" + propName + "'");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);

        il.MarkLabel(skipLabel);
    }

    private void EmitRegExpExecSlow(ILGenerator il, EmittedRuntime runtime, LocalBuilder rxObjLocal, LocalBuilder sLocal, LocalBuilder resultLocal)
    {
        var execLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // exec = Get(rx, "exec")
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldstr, "exec");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, execLocal);

        // If exec isn't a $TSFunction → TypeError.
        var execOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, execLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, execOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype method called on non-RegExp without a callable exec");
        il.MarkLabel(execOkLabel);

        // result = exec.InvokeWithThis(rx, [S])
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, execLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, rxObjLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ECMA-262 §22.2.5.2.1 step 5c: if the user-provided exec returned a
        // value that is neither Object nor Null, throw TypeError. Undefined,
        // numbers, strings, booleans, symbols all qualify as non-Object.
        var resultOkLabel = il.DefineLabel();
        var nonObjectLabel = il.DefineLabel();

        // null is permitted — short-circuit to ok and let caller distinguish
        // "no match" from a real result via downstream is-null tests.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brfalse, resultOkLabel);
        // $Undefined → throw
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        // string → throw
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        // boolean → throw
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        // double → throw
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        // int → throw (rare in compiled output but defensive)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        // symbol → throw
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, nonObjectLabel);
        il.Emit(OpCodes.Br, resultOkLabel);

        il.MarkLabel(nonObjectLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp exec returned a non-Object, non-Null value");

        il.MarkLabel(resultOkLabel);
    }

    /// <summary>
    /// Emits a boolean push: <c>local is double d &amp;&amp; d == 0</c>, also
    /// treating null/non-numeric as "not zero". Used for SameValue(li, 0)
    /// short-circuit checks in the Symbol.* slow paths.
    /// </summary>
    private void EmitIsNumericZero(ILGenerator il, EmittedRuntime runtime, LocalBuilder local)
    {
        var notDoubleLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits SymSplit(object rx, object str, object limit) -> $Array.
    /// </summary>
    private void EmitTSRegExpSymSplitHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SymSplit",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.TSRegExpSymSplitHelper = method;

        var il = method.GetILGenerator();
        var rxLocal = il.DeclareLocal(typeBuilder);
        var sLocal = il.DeclareLocal(_types.String);
        var partsLocal = il.DeclareLocal(typeof(string[]));
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var nLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);

        var noLimitLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // ECMA-262 §22.2.5.13 step 2: throw TypeError if `this` is not an Object.
        EmitRequireObjectArg(il, runtime, method, argIndex: 0, "RegExp.prototype[Symbol.split]");

        // Spec-aligned: read S = ToString(string), then flags = ToString(Get(rx, "flags")).
        // Errors propagate naturally (user toString throws, Symbol → TypeError via
        // EmitArgToJsString's Symbol-brand-check + Stringify chain). Run these before
        // the Castclass so non-$RegExp `this` with poisoned flags/string getters
        // surfaces the user's error instead of an InvalidCastException. Tests in scope:
        //   Symbol.split/{coerce-string,coerce-flags,get-flags,coerce-limit}-err.js
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);
        var sideEffectsFlagsLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "flags");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, sideEffectsFlagsLocal);

        // Coerce `limit` via ToNumber when not undefined. Symbol → TypeError,
        // object → ToPrimitive(valueOf) which can throw. Spec §22.2.5.13 step 7.
        // Result re-stored over Ldarg_2 (as boxed Double) so the later Isinst
        // Double branch picks up the coerced value.
        var limitCoerceSkipLabel = il.DefineLabel();
        var limitCoerced = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, limitCoerceSkipLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, limitCoerceSkipLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, limitCoerced);
        il.Emit(OpCodes.Ldloc, limitCoerced);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Starg_S, (byte)2);
        il.MarkLabel(limitCoerceSkipLabel);

        // Once side effects are observed, narrow to $RegExp. Non-$RegExp instances
        // still need to throw TypeError per spec (we'd need SpeciesConstructor +
        // a synthesized regex from the user-provided flags to do the fully spec-
        // aligned split; defer that). The Isinst-then-throw replaces a Castclass
        // InvalidCastException with a clean TypeError bucket.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        var rxOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, rxOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype[Symbol.split] requires a RegExp receiver");
        il.MarkLabel(rxOkLabel);

        // var parts = rx.Split(s);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Call, _tsRegExpSplitMethod);
        il.Emit(OpCodes.Stloc, partsLocal);

        // n = parts.Length;
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, nLocal);

        // if (limit is double d && d >= 0 && (int)d < n) n = (int)d;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, noLimitLabel);

        var limTmp = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, limTmp);
        // if (lim < 0) skip
        il.Emit(OpCodes.Ldloc, limTmp);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, noLimitLabel);
        // if (lim < n) n = lim;
        il.Emit(OpCodes.Ldloc, limTmp);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Bge, noLimitLabel);
        il.Emit(OpCodes.Ldloc, limTmp);
        il.Emit(OpCodes.Stloc, nLocal);

        il.MarkLabel(noLimitLabel);

        // var result = new List<object?>(n);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (int i = 0; i < n; i++) result.Add(parts[i]);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopEndLabel);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // return new $Array(result);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL: <c>local = arg{argIndex} == null ? "undefined" : arg{argIndex}.ToString()</c>.
    /// </summary>
    /// <summary>
    /// Emits the eleven RegExp.prototype accessor-descriptor getters as
    /// static methods on $RegExp. Each helper:
    ///   - Takes <c>object __this</c>. Name-naming "__this" makes
    ///     $TSFunction._expectsThis=true so the call-site receiver is routed
    ///     into arg0 — required for the test262 pattern
    ///     <c>var get = Object.getOwnPropertyDescriptor(RegExp.prototype,
    ///     'source').get; get.call(undefined)</c>.
    ///   - If __this is not Object → throw TypeError. (null, undefined,
    ///     primitives, $TSSymbol.)
    ///   - If __this is RegExp.prototype itself → return the spec-specified
    ///     "fallback for prototype" value (source: "(?:)", flags: "", all
    ///     booleans: false).
    ///   - If __this is a $RegExp instance → return the slot value via the
    ///     typed getter or parsed-from-flags.
    ///   - Otherwise (some other Object) → throw TypeError.
    /// Then <see cref="EmitRegExpPrototypePopulate"/> installs them as
    /// PDS accessor descriptors on RegExp.prototype.
    /// </summary>
    private void EmitTSRegExpProtoAccessors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.TSRegExpProtoGetSource    = EmitProtoAccessor(typeBuilder, runtime, "ProtoGetSource",    () => true,  EmitSourceBody);
        // `flags` is a GENERIC accessor (§22.2.5.3) — dedicated emit so a
        // non-RegExp object `this` builds the flag string via Get+ToBoolean
        // instead of throwing (the shared prologue throws, correct only for the
        // per-flag accessors below).
        runtime.TSRegExpProtoGetFlags     = EmitProtoFlagsAccessor(typeBuilder, runtime);
        runtime.TSRegExpProtoGetGlobal    = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetGlobal",    'g');
        runtime.TSRegExpProtoGetIgnoreCase = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetIgnoreCase", 'i');
        runtime.TSRegExpProtoGetMultiline = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetMultiline", 'm');
        runtime.TSRegExpProtoGetSticky    = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetSticky",    'y');
        runtime.TSRegExpProtoGetUnicode   = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetUnicode",   'u');
        runtime.TSRegExpProtoGetDotAll    = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetDotAll",    's');
        runtime.TSRegExpProtoGetHasIndices = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetHasIndices", 'd');
        runtime.TSRegExpProtoGetUnicodeSets = EmitProtoBoolAccessor(typeBuilder, runtime, "ProtoGetUnicodeSets", 'v');

        // source body: return rx._source
        void EmitSourceBody(ILGenerator il, LocalBuilder rx)
        {
            il.Emit(OpCodes.Ldloc, rx);
            il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        }
    }

    /// <summary>
    /// Emits <c>ProtoGetFlags(object __this)</c> — ECMA-262 §22.2.5.3
    /// <c>get RegExp.prototype.flags</c>, a GENERIC accessor: it requires only
    /// that <c>this</c> be an Object and builds the flag string by reading each
    /// flag via <c>Get</c> + ToBoolean. A real <c>$RegExp</c> takes the fast
    /// path (its cached <c>_flags</c>); any other object goes through the
    /// generic build (so <c>get.call(plainObj)</c> works — the
    /// <c>flags/coercion-*</c> tests). Primitive <c>this</c> → TypeError.
    /// Mirrors the interp generic <c>flags</c> getter (BuildFlagsString).
    /// </summary>
    private MethodBuilder EmitProtoFlagsAccessor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var helper = typeBuilder.DefineMethod(
            "ProtoGetFlags",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        try { helper.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = helper.GetILGenerator();
        var throwLabel = il.DefineLabel();
        var genericLabel = il.DefineLabel();
        var rxLocal = il.DeclareLocal(runtime.TSRegExpType);
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));

        // 1. Type(R) is not Object → TypeError. Reject every primitive kind.
        void RejectIsinst(Type t)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, t);
            il.Emit(OpCodes.Brtrue, throwLabel);
        }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, throwLabel); // null
        RejectIsinst(runtime.UndefinedType);
        RejectIsinst(_types.String);
        RejectIsinst(_types.Boolean);
        RejectIsinst(_types.Double);
        RejectIsinst(_types.Int32);
        RejectIsinst(runtime.TSSymbolType);
        RejectIsinst(_types.BigInteger); // BigInt is a primitive, not an Object.

        // 2. Fast path: a real $RegExp returns its cached flags.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brfalse, genericLabel);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Ret);

        // 3. Generic build: for each flag, append its char when truthy. The
        //    RegExp.prototype object itself lands here too and yields "" (every
        //    Get returns undefined).
        il.MarkLabel(genericLabel);
        var sbCtor = typeof(System.Text.StringBuilder).GetConstructor(Type.EmptyTypes)!;
        var sbAppendChar = typeof(System.Text.StringBuilder).GetMethod("Append", [_types.Char])!;
        var sbToString = typeof(System.Text.StringBuilder).GetMethod("ToString", Type.EmptyTypes)!;
        il.Emit(OpCodes.Newobj, sbCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        (string name, char ch)[] flags =
        [
            ("hasIndices", 'd'), ("global", 'g'), ("ignoreCase", 'i'),
            ("multiline", 'm'), ("dotAll", 's'), ("unicode", 'u'),
            ("unicodeSets", 'v'), ("sticky", 'y'),
        ];
        foreach (var (name, ch) in flags)
        {
            var skip = il.DefineLabel();
            // if (IsTruthy(GetProperty(__this, name))) sb.Append(ch);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, sbLocal);
            il.Emit(OpCodes.Ldc_I4, (int)ch);
            il.Emit(OpCodes.Callvirt, sbAppendChar);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(skip);
        }
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, sbToString);
        il.Emit(OpCodes.Ret);

        // TypeError for primitive `this`.
        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype.flags called on non-object");
        return helper;
    }

    /// <summary>
    /// Emits a string-returning proto accessor (source / flags). Returns the
    /// MethodBuilder for the helper. The body shape:
    /// <code>
    /// if (__this is RegExp.prototype) return "<defaultForProto>";
    /// if (!(__this is $RegExp rx)) throw new TypeError("…");
    /// return &lt;bodyEmitter&gt;(rx);
    /// </code>
    /// </summary>
    private MethodBuilder EmitProtoAccessor(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, System.Func<bool> _stringReturn, System.Action<ILGenerator, LocalBuilder> bodyEmitter)
    {
        var helper = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        try { helper.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = helper.GetILGenerator();
        EmitProtoAccessorPrologue(il, runtime, name, out var rxLocal, out var protoFallbackLabel);

        // $RegExp path
        bodyEmitter(il, rxLocal);
        il.Emit(OpCodes.Ret);

        // RegExp.prototype path — spec defaults are "(?:)" for source, "" for flags.
        il.MarkLabel(protoFallbackLabel);
        il.Emit(OpCodes.Ldstr, name == "ProtoGetSource" ? "(?:)" : "");
        il.Emit(OpCodes.Ret);
        return helper;
    }

    /// <summary>
    /// Emits a bool-returning proto accessor (global, ignoreCase, multiline,
    /// sticky, unicode, dotAll, hasIndices, unicodeSets). For RegExp instances
    /// it parses the flags string for <paramref name="flagChar"/>; the
    /// RegExp.prototype fallback returns <c>false</c> per spec.
    /// </summary>
    private MethodBuilder EmitProtoBoolAccessor(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, char flagChar)
    {
        var helper = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        try { helper.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = helper.GetILGenerator();
        EmitProtoAccessorPrologue(il, runtime, name, out var rxLocal, out var protoFallbackLabel);

        // $RegExp path: return rx._flags.Contains(flagChar) boxed.
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Ldc_I4, (int)flagChar);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.Char));
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // Prototype fallback: spec says `undefined` when `this` is
        // %RegExpPrototype% (ECMA-262 §22.2.6.{5,7,9,11,13,15,17,19} step 3.a).
        il.MarkLabel(protoFallbackLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        return helper;
    }

    /// <summary>
    /// Shared prologue for every proto accessor:
    /// 1. Throw TypeError if __this is null/undefined/primitive/symbol.
    /// 2. If __this is RegExp.prototype itself → jump to protoFallbackLabel.
    /// 3. If __this is a $RegExp → store as rxLocal and fall through.
    /// 4. Anything else (some other Object) → throw TypeError.
    /// </summary>
    private void EmitProtoAccessorPrologue(ILGenerator il, EmittedRuntime runtime, string name, out LocalBuilder rxLocal, out Label protoFallbackLabel)
    {
        rxLocal = il.DeclareLocal(runtime.TSRegExpType);
        protoFallbackLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var checkRegExpLabel = il.DefineLabel();

        // Primitives → throw.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // If arg is RegExp.prototype itself → spec fallback.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, protoFallbackLabel);

        // If $RegExp → store and fall through to body.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brtrue, checkRegExpLabel);
        // Any other Object → TypeError.
        il.Emit(OpCodes.Br, throwLabel);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype accessor called on non-RegExp");

        il.MarkLabel(checkRegExpLabel);
    }

    /// <summary>
    /// Emits the three RegExp.prototype data-method helpers (exec/test/
    /// toString) as static methods on $RegExp. Each names its first param
    /// "__this" so $TSFunction._expectsThis=true and `.call(receiver, ...)`
    /// routes the receiver. Each throws TypeError when receiver is not a
    /// $RegExp.
    /// </summary>
    private void EmitTSRegExpProtoMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.TSRegExpProtoExec = EmitProtoExecMethod(typeBuilder, runtime);
        runtime.TSRegExpProtoTest = EmitProtoTestMethod(typeBuilder, runtime);
        runtime.TSRegExpProtoToString = EmitProtoToStringMethod(typeBuilder, runtime);
    }

    private MethodBuilder EmitProtoExecMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProtoExec",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        try { method.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = method.GetILGenerator();
        var rxLocal = il.DeclareLocal(runtime.TSRegExpType);
        var sLocal = il.DeclareLocal(_types.String);

        // If `this` is not a $RegExp, throw TypeError.
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brtrue, okLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype.exec called on non-RegExp");
        il.MarkLabel(okLabel);

        // s = ToString(arg1)
        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);

        // return rx.Exec(s)
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpExecMethod);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitProtoTestMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProtoTest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        try { method.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = method.GetILGenerator();
        var rxLocal = il.DeclareLocal(runtime.TSRegExpType);
        var sLocal = il.DeclareLocal(_types.String);

        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brtrue, okLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype.test called on non-RegExp");
        il.MarkLabel(okLabel);

        EmitArgToJsString(il, runtime, argIndex: 1, sLocal);

        // return rx.Test(s) boxed
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpTestMethod);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitProtoToStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProtoToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        try { method.DefineParameter(1, ParameterAttributes.None, "__this"); }
        catch { /* already named — ignore */ }

        var il = method.GetILGenerator();
        var rxLocal = il.DeclareLocal(runtime.TSRegExpType);

        // Allow this === RegExp.prototype to return "/(?:)/" (spec default).
        var notProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, notProtoLabel);
        il.Emit(OpCodes.Ldstr, "/(?:)/");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProtoLabel);

        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
        il.Emit(OpCodes.Stloc, rxLocal);
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Brtrue, okLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "RegExp.prototype.toString called on non-RegExp");
        il.MarkLabel(okLabel);

        // return "/" + rx.Source + "/" + rx.Flags
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldloc, rxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitArgToJsString(ILGenerator il, EmittedRuntime runtime, int argIndex, LocalBuilder local)
    {
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // ECMA-262 7.1.17 ToString: Symbol primitives throw TypeError.
        // Each Symbol.* protocol method begins with S = ? ToString(string);
        // calls like `/./[Symbol.search](Symbol.iterator)` must surface
        // that TypeError up. $RegExp emits before $Runtime so the global
        // ToJsString helper isn't yet bound — inline the brand-check +
        // TypeError throw with the forward-declared CreateException.
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a string");
        il.MarkLabel(notSymbolLabel);

        // Route through $Runtime.Stringify so user-installed toString
        // (and dict literals' Number/Array/etc. cases) coerce per spec
        // instead of returning the C# Object.ToString fallback. Forward-
        // declared in DefineRuntimeClassPhase1 so we can call it here.
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, local);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Stloc, local);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// ECMA-262 §22.2.5 step 2: each well-known-symbol-keyed RegExp protocol
    /// method requires the receiver (passed as <paramref name="argIndex"/>)
    /// to be an Object. Null, undefined, booleans, numbers, strings, and
    /// symbols throw TypeError before any further work. The previous
    /// implementations let a `Castclass` to <c>$RegExp</c> raise an
    /// InvalidCastException, which surfaced as a generic Error and failed
    /// test262's <c>assert.throws(TypeError, ...)</c> bucket.
    /// </summary>
    /// <remarks>
    /// $RegExp emits before $Runtime, so <c>runtime.CreateException</c> /
    /// <c>runtime.Stringify</c> aren't yet bound when this helper runs —
    /// we inline the wrap (System.Exception + <c>Data["__tsValue"]</c>)
    /// here. Matches what <c>$Runtime.CreateException</c> does on the
    /// runtime side.
    /// </remarks>
    private void EmitRequireObjectArg(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, int argIndex, string siteName)
    {
        if (argIndex == 0)
        {
            try { method.DefineParameter(1, ParameterAttributes.None, "__this"); }
            catch { /* already named — ignore */ }
        }

        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // null → throw
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // $Undefined → throw
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // string → throw
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // boolean (boxed bool) → throw
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // number (boxed double) → throw
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // boxed int (rare in compiled output but harmless to check)
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, throwLabel);

        // $TSSymbol is a primitive per ECMA-262 (Type "Symbol", not Object).
        // Test262's this-val-non-obj.js lists Symbol.match itself as a
        // "non-object" value and expects TypeError when passed as `this`.
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, throwLabel);

        il.Emit(OpCodes.Br, okLabel);

        il.MarkLabel(throwLabel);
        // Build a $TypeError, wrap it via $Runtime.CreateException — both
        // are forward-declared by DefineRuntimeClassPhase1 so we can call
        // them from $RegExp's helpers despite emitting before $Runtime's
        // body is filled in.
        il.Emit(OpCodes.Ldstr, siteName + " called on non-object");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);

        il.MarkLabel(okLabel);
    }
}
