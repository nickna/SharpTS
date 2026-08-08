using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript RegExp object members.
/// Includes instance properties (source, flags, global, etc.) and methods (test, exec, toString).
/// </summary>
public static class RegExpBuiltIns
{
    /// <summary>
    /// Resolves a static member on the <c>RegExp</c> constructor (e.g.
    /// <c>RegExp.escape</c>). Returns null for unknown names. Routed here by
    /// <see cref="SharpTSBuiltInConstructor.GetMember"/> via the built-in
    /// namespace registry.
    /// </summary>
    public static ISharpTSCallable? GetStaticMember(string name) => name switch
    {
        "escape" => _escape,
        _ => null
    };

    // ECMA-262 (ES2025) §sec-regexp.escape. minArity 0 so a missing/non-string
    // argument reaches the body and throws the spec TypeError (rather than the
    // runtime arity check rejecting it). Visible `length` is 1 (WithSpecLength).
    private static readonly BuiltInMethod _escape =
        BuiltInMethod.CreateV2("escape", 0, int.MaxValue, static (_, _, args) =>
        {
            // Step 1: If S is not a String, throw a TypeError exception.
            if (args.Length == 0 || args[0].Kind != ValueKind.String)
                throw new ThrowException(new SharpTSTypeError(
                    "RegExp.escape called with a non-string argument"));
            return RuntimeValue.FromString(EscapeString(args[0].AsStringUnsafe()));
        }).WithSpecLength(1).AsNonConstructor();

    /// <summary>
    /// ECMA-262 (ES2025) §sec-regexp.escape EncodeForRegExpEscape applied across
    /// the whole string, including the leading-character rule (a leading ASCII
    /// digit or letter is emitted as <c>\xHH</c> so it can't merge with a
    /// preceding <c>\0</c>/<c>\1</c>/<c>\c</c> escape when the result is spliced
    /// into a larger pattern). Iterates by code point so surrogate pairs are
    /// preserved and lone surrogates are escaped as <c>\uXXXX</c>.
    /// </summary>
    internal static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        bool first = true;
        int i = 0;
        while (i < s.Length)
        {
            char ch = s[i];
            int c;
            int units;
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                c = char.ConvertToUtf32(ch, s[i + 1]);
                units = 2;
            }
            else
            {
                c = ch; // BMP char or lone surrogate value
                units = 1;
            }

            // Step 4.a: leading ASCII digit/letter → \xHH.
            if (first && IsAsciiAlphaNumeric(c))
            {
                sb.Append("\\x");
                AppendHex2(sb, c);
            }
            else
            {
                AppendEncodedCodePoint(sb, s, i, c, units);
            }
            first = false;
            i += units;
        }
        return sb.ToString();
    }

    private static bool IsAsciiAlphaNumeric(int c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    // SyntaxCharacter :: one of ^ $ \ . * + ? ( ) [ ] { } | (and SOLIDUS / is
    // handled alongside per step 1). otherPunctuators per the spec table,
    // including the QUOTATION MARK (0x0022).
    private const string SyntaxChars = "^$\\.*+?()[]{}|";
    private const string OtherPunctuators = ",-=<>#&!%:;@~'`\"";

    /// <summary>EncodeForRegExpEscape(c) — appends the encoded form of a single
    /// code point. <paramref name="i"/>/<paramref name="units"/> locate the
    /// original UTF-16 code unit(s) in <paramref name="s"/> for literal/unicode
    /// emission.</summary>
    private static void AppendEncodedCodePoint(StringBuilder sb, string s, int i, int c, int units)
    {
        // Step 1: SyntaxCharacter or '/' → REVERSE SOLIDUS + the character.
        if (c <= 0xFFFF && (c == '/' || SyntaxChars.IndexOf((char)c) >= 0))
        {
            sb.Append('\\').Append((char)c);
            return;
        }
        // Step 2: ControlEscape table.
        switch (c)
        {
            case '\t': sb.Append("\\t"); return;
            case '\n': sb.Append("\\n"); return;
            case '\v': sb.Append("\\v"); return;
            case '\f': sb.Append("\\f"); return;
            case '\r': sb.Append("\\r"); return;
        }
        // Step 5: otherPunctuators / WhiteSpace / LineTerminator / lone surrogate.
        bool needsHex =
            (c <= 0xFFFF && OtherPunctuators.IndexOf((char)c) >= 0)
            || IsEsWhiteSpaceOrLineTerminator(c)
            || (c >= 0xD800 && c <= 0xDFFF);
        if (needsHex)
        {
            if (c <= 0xFF)
            {
                sb.Append("\\x");
                AppendHex2(sb, c);
            }
            else
            {
                // UnicodeEscape each UTF-16 code unit of the original source.
                for (int k = 0; k < units; k++)
                {
                    sb.Append("\\u");
                    AppendHex4(sb, s[i + k]);
                }
            }
            return;
        }
        // Step 6: otherwise emit the code point unchanged.
        for (int k = 0; k < units; k++)
            sb.Append(s[i + k]);
    }

    // ES WhiteSpace ∪ LineTerminator, minus the chars already handled by the
    // ControlEscape table (\t \n \v \f \r). SpaceSeparator (Zs) covers
    // 0x20/0xA0/0x1680/0x2000-200A/0x202F/0x205F/0x3000; ZWNBSP (0xFEFF) is
    // category Format and the two line separators (0x2028/0x2029) are added
    // explicitly.
    private static bool IsEsWhiteSpaceOrLineTerminator(int c)
    {
        if (c == 0xFEFF || c == 0x2028 || c == 0x2029) return true;
        if (c > 0xFFFF) return false;
        return char.GetUnicodeCategory((char)c)
            == System.Globalization.UnicodeCategory.SpaceSeparator;
    }

    private const string HexDigits = "0123456789abcdef";

    private static void AppendHex2(StringBuilder sb, int c) =>
        sb.Append(HexDigits[(c >> 4) & 0xF]).Append(HexDigits[c & 0xF]);

    private static void AppendHex4(StringBuilder sb, int c) =>
        sb.Append(HexDigits[(c >> 12) & 0xF]).Append(HexDigits[(c >> 8) & 0xF])
          .Append(HexDigits[(c >> 4) & 0xF]).Append(HexDigits[c & 0xF]);

    /// <summary>
    /// Gets an instance member (property or method) for a RegExp object.
    /// Pass <paramref name="interpreter"/> when the caller has one available
    /// — it's required to invoke user-installed accessor getters
    /// (Object.defineProperty path, ECMA-262 §22.2). Without it, the legacy
    /// path returns the bound built-in slot.
    /// </summary>
    public static object? GetMember(SharpTSRegExp receiver, string name, Interpreter? interpreter = null)
    {
        // User-installed accessor wins over the built-in slot — ECMA-262
        // declares flags/global/unicode as configurable, so a
        // throwing getter installed via defineProperty MUST fire and
        // propagate. Without an interpreter we can't invoke it; legacy
        // call sites without the parameter fall through to the built-in.
        if (interpreter != null
            && receiver.TryGetAccessor(name, out var userGetter, out _)
            && userGetter != null)
        {
            return userGetter.Call(interpreter, []);
        }

        // User-set DATA properties (set via `r.foo = x` after
        // `Object.defineProperty(r, 'foo', {writable:true})`, or via
        // `Object.defineProperty(r, 'foo', {value:x})`) shadow the
        // configurable built-in accessor slots — that's the path test262's
        // coerce-global.js / Symbol.replace coerce-flags.js etc. exercise.
        // Without this, redefining `r.global = false` is silently ignored
        // and replace/match/etc. still see the original 'g' flag.
        // Object.prototype unbound methods stored as user values
        // (`__re.toString = Object.prototype.toString`) need to be rebound
        // to the receiver so the subsequent `__re.toString()` call sees
        // `__re` as `this` — the interpreter's call dispatch doesn't bind
        // `this` for plain method-call syntax on user-stored callables.
        if (receiver.TryGetProperty(name, out var userOwnVal))
        {
            if (userOwnVal is SharpTSObjectUnboundMethod ub)
                return ub.BindTo(receiver);
            return userOwnVal;
        }

        // ECMA-262 §22.2.5.3 `flags` is a dynamic getter that ToBooleans each
        // individual flag property (Get(R, "global"), Get(R, "ignoreCase"), …)
        // and concatenates the chars in canonical order. User code can shadow
        // any single flag with a data property, and `flags` must reflect
        // that. With an Interpreter we can dispatch via Get; without it we
        // fall back to the internal slot.
        if (name == "flags" && interpreter != null)
            return BuildFlagsString(interpreter, receiver);

        var builtIn = name switch
        {
            // ========== Properties ==========
            "source" => (object?)receiver.Source,
            "flags" => receiver.Flags,
            "global" => receiver.Global,
            "ignoreCase" => receiver.IgnoreCase,
            "multiline" => receiver.Multiline,
            "dotAll" => receiver.Flags.Contains('s'),
            "sticky" => receiver.Sticky,
            "unicode" => receiver.Unicode,
            "unicodeSets" => receiver.Flags.Contains('v'),
            "hasIndices" => receiver.Flags.Contains('d'),
            "lastIndex" => receiver.TryGetProperty("lastIndex", out var lastIndex)
                ? lastIndex
                : 0d,

            // ========== Methods ==========
            "test" => _protoTest,
            "exec" => _protoExec,
            "toString" => _protoToString,

            _ => null
        };
        return builtIn;
    }

    /// <summary>
    /// Sets an instance member (property) for a RegExp object.
    /// Only lastIndex is writable.
    /// </summary>
    /// <returns>True if the property was set, false if the property is not writable.</returns>
    public static bool SetMember(
        SharpTSRegExp receiver, string name, object? value, bool strictMode = false)
    {
        if (name == "lastIndex")
        {
            // lastIndex is an ordinary data property: assignment stores the
            // value as-is. RegExpBuiltinExec performs ToLength when it reads
            // the property, so object valueOf/toString hooks run per exec.
            receiver.SetPropertyStrict(name, value, strictMode);
            return true;
        }
        return false;
    }

    // ECMA-262 §22.2.5 well-known-symbol-keyed methods on RegExp.prototype.
    // Hoisted as static singletons so:
    //   1. `RegExp.prototype[Symbol.match]` and `r[Symbol.match]` reference
    //      the *same* function value (spec requires this; tests check via ===).
    //   2. The function metadata (name, length) is set up once.
    //   3. Property-descriptor introspection on RegExp.prototype works through
    //      SharpTSObject's symbol-keyed storage.
    // Spec lengths: match/matchAll/search = 1; replace/split = 2.

    // minArity is 0 here so test262 patterns like
    // `RegExp.prototype[Symbol.search].call(fakeRe)` (no string arg) reach
    // the body and ToString-coerce undefined to "undefined". JS-spec
    // `length` is still 1/2 (set via WithSpecLength below) — the runtime
    // arity check only governs argument-count rejection, not the visible
    // .length value used by isConstructor / verifyProperty introspection.
    private static readonly BuiltInMethod _symbolMatch =
        BuiltInMethod.CreateV2("[Symbol.match]", 0, int.MaxValue, SymbolMatchImpl).WithSpecLength(1).AsNonConstructor();

    private static readonly BuiltInMethod _symbolMatchAll =
        BuiltInMethod.CreateV2("[Symbol.matchAll]", 0, int.MaxValue, SymbolMatchAllImpl).WithSpecLength(1).AsNonConstructor();

    private static readonly BuiltInMethod _symbolReplace =
        BuiltInMethod.CreateV2("[Symbol.replace]", 0, int.MaxValue, SymbolReplaceImpl).WithSpecLength(2).AsNonConstructor();

    private static readonly BuiltInMethod _symbolSearch =
        BuiltInMethod.CreateV2("[Symbol.search]", 0, int.MaxValue, SymbolSearchImpl).WithSpecLength(1).AsNonConstructor();

    private static readonly BuiltInMethod _symbolSplit =
        BuiltInMethod.CreateV2("[Symbol.split]", 0, int.MaxValue, SymbolSplitImpl).WithSpecLength(2).AsNonConstructor();

    /// <summary>
    /// Builds a fresh per-realm RegExp.prototype object. Each Interpreter
    /// holds its own copy so realm-local mutations like
    /// `delete RegExp.prototype[Symbol.split]` (test262 propertyHelper's
    /// verifyProperty) and `Object.defineProperty(RegExp.prototype, …)` don't
    /// leak across Interpreter instances. Holds the five well-known-symbol
    /// protocol methods plus the named instance methods (`exec`, `test`,
    /// `toString`) that ECMA-262 §22.2.5 declares introspectable on the
    /// prototype itself.
    /// </summary>
    public static SharpTSObject BuildPrototype()
    {
        // BuiltInMethod carries mutable function-object metadata (`name` and
        // `length` can be deleted). Keep those mutations realm-local along
        // with the prototype object itself instead of sharing the static
        // registration instances across persistent Test262 worker realms.
        static BuiltInMethod RealmLocal(BuiltInMethod method) => method.Bind(null);

        var proto = new SharpTSObject(new Dictionary<string, object?>());
        void DefineSymbolMethod(SharpTSSymbol symbol, BuiltInMethod method) =>
            proto.DefineProperty(symbol, new SharpTSPropertyDescriptor
            {
                Value = RealmLocal(method),
                HasValue = true,
                Writable = true,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = true,
                HasConfigurable = true,
            });
        DefineSymbolMethod(SharpTSSymbol.Match, _symbolMatch);
        DefineSymbolMethod(SharpTSSymbol.MatchAll, _symbolMatchAll);
        DefineSymbolMethod(SharpTSSymbol.Replace, _symbolReplace);
        DefineSymbolMethod(SharpTSSymbol.Search, _symbolSearch);
        DefineSymbolMethod(SharpTSSymbol.Split, _symbolSplit);
        // ECMA-262 §22.2.5: instance-method slots that are introspectable on
        // the prototype itself (`RegExp.prototype.exec`, `.test`, `.toString`)
        // — propertyHelper.js's verifyNotWritable runs against them, and
        // `Function.prototype.call.bind(RegExp.prototype.exec)` is a real
        // pattern. These are the unbound prototype methods; user code rebinds
        // via `.call(re, str)` which routes through `BuiltInMethod.Bind`.
        void DefineMethod(string name, object value) =>
            proto.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = true,
                Enumerable = false,
                Configurable = true,
            });
        DefineMethod("exec", RealmLocal(_protoExec));
        DefineMethod("test", RealmLocal(_protoTest));
        DefineMethod("toString", RealmLocal(_protoToString));
        // ECMA-262 §22.2.6.1: RegExp.prototype.constructor is the RegExp
        // constructor. Wiring it here makes `RegExp.prototype.constructor ===
        // RegExp` hold and gives property-descriptor introspection
        // (propertyHelper verifyProperty) something to read. The same
        // singleton is returned for instance `.constructor` access (see
        // Interpreter EvaluateGetOnRegExp).
        if (Execution.Interpreter.RegExpConstructorObject is { } rxCtor)
            DefineMethod("constructor", rxCtor);
        // ECMA-262 §22.2.5: the flag/source accessors live on the prototype as
        // accessor properties { enumerable: false, configurable: true } so
        // descriptor introspection works (getOwnPropertyDescriptor returns a
        // callable `get`, propertyIsEnumerable is false, the property is
        // deletable). Instance `re.flags`/`re.global`/… continue to resolve
        // through EvaluateGetOnRegExp; these are for prototype/introspection use.
        void DefineAccessor(string name, BuiltInMethod getter) =>
            proto.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Get = getter,
                Set = null,
                Enumerable = false,
                Configurable = true,
            });
        DefineAccessor("flags", RealmLocal(_protoFlagsGetter));
        DefineAccessor("source", RealmLocal(_protoSourceGetter));
        DefineAccessor("global", RealmLocal(_protoGlobalGetter));
        DefineAccessor("ignoreCase", RealmLocal(_protoIgnoreCaseGetter));
        DefineAccessor("multiline", RealmLocal(_protoMultilineGetter));
        DefineAccessor("dotAll", RealmLocal(_protoDotAllGetter));
        DefineAccessor("sticky", RealmLocal(_protoStickyGetter));
        DefineAccessor("unicode", RealmLocal(_protoUnicodeGetter));
        DefineAccessor("unicodeSets", RealmLocal(_protoUnicodeSetsGetter));
        DefineAccessor("hasIndices", RealmLocal(_protoHasIndicesGetter));
        return proto;
    }

    private static readonly BuiltInMethod _protoExec =
        BuiltInMethod.CreateV2("exec", 0, int.MaxValue, static (interp, recv, args) =>
        {
            if (recv.ToObject() is not SharpTSRegExp regex)
                throw new ThrowException(new SharpTSTypeError(
                    "RegExp.prototype.exec called on non-RegExp"));
            var argument = args.Length > 0
                ? args[0].ToObject()
                : SharpTSUndefined.Instance;
            string input = interp.ToStringForBuiltInArgument(argument);
            return RuntimeValue.FromObject(RegExpBuiltinExec(interp, regex, input));
        }).WithSpecLength(1).AsNonConstructor();

    private static readonly BuiltInMethod _protoTest =
        BuiltInMethod.CreateV2("test", 0, int.MaxValue, static (interp, recv, args) =>
        {
            if (recv.ToObject() is not SharpTSRegExp regex)
                throw new ThrowException(new SharpTSTypeError(
                    "RegExp.prototype.test called on non-RegExp"));
            var argument = args.Length > 0
                ? args[0].ToObject()
                : SharpTSUndefined.Instance;
            string input = interp.ToStringForBuiltInArgument(argument);
            return RuntimeValue.FromBoolean(RegExpExec(interp, regex, input) != null);
        }).WithSpecLength(1).AsNonConstructor();

    private static readonly BuiltInMethod _protoToString =
        BuiltInMethod.CreateV2("toString", 0, static (_, recv, _) =>
        {
            if (recv.ToObject() is not SharpTSRegExp regex)
                throw new ThrowException(new SharpTSTypeError(
                    "RegExp.prototype.toString called on non-RegExp"));
            return RuntimeValue.FromString(regex.ToString());
        }).WithSpecLength(0).AsNonConstructor();

    // ECMA-262 §22.2.5.3 `get RegExp.prototype.flags` — a GENERIC accessor: it
    // requires only that `this` be an Object (not necessarily a RegExp) and
    // builds the flag string by reading each flag via Get + ToBoolean. Exposed
    // on the prototype so `Object.getOwnPropertyDescriptor(RegExp.prototype,
    // "flags").get.call(plainObj)` works (the flags/coercion-* tests).
    private static readonly BuiltInMethod _protoFlagsGetter =
        BuiltInMethod.CreateV2("get flags", 0, static (interp, recvV, _) =>
        {
            var recv = recvV.ToObject();
            RequireObject(recv, ".flags");
            return RuntimeValue.FromString(BuildFlagsString(interp, recv));
        }).WithSpecLength(0);

    /// <summary>
    /// Builds a per-flag accessor getter (§22.2.5.4/.6/.8/.10/.12/.14): on a
    /// real RegExp returns whether <paramref name="flagChar"/> is set; on
    /// %RegExp.prototype% returns undefined; otherwise throws TypeError. Unlike
    /// the generic <c>flags</c> getter these require an actual RegExp/prototype
    /// <c>this</c>.
    /// </summary>
    private static BuiltInMethod MakeProtoFlagGetter(string accessor, char flagChar) =>
        BuiltInMethod.CreateV2("get " + accessor, 0, (interp, recvV, _) =>
        {
            var recv = recvV.ToObject();
            if (recv is SharpTSRegExp rx) return RuntimeValue.FromBoolean(rx.Flags.Contains(flagChar));
            RequireObject(recv, "." + accessor);
            if (ReferenceEquals(recv, interp.GetRegExpPrototype())) return RuntimeValue.Undefined;
            throw new ThrowException(new SharpTSTypeError(
                $"RegExp.prototype.{accessor} getter called on non-RegExp"));
        }).WithSpecLength(0);

    private static readonly BuiltInMethod _protoGlobalGetter = MakeProtoFlagGetter("global", 'g');
    private static readonly BuiltInMethod _protoIgnoreCaseGetter = MakeProtoFlagGetter("ignoreCase", 'i');
    private static readonly BuiltInMethod _protoMultilineGetter = MakeProtoFlagGetter("multiline", 'm');
    private static readonly BuiltInMethod _protoDotAllGetter = MakeProtoFlagGetter("dotAll", 's');
    private static readonly BuiltInMethod _protoStickyGetter = MakeProtoFlagGetter("sticky", 'y');
    private static readonly BuiltInMethod _protoUnicodeGetter = MakeProtoFlagGetter("unicode", 'u');
    private static readonly BuiltInMethod _protoUnicodeSetsGetter = MakeProtoFlagGetter("unicodeSets", 'v');
    private static readonly BuiltInMethod _protoHasIndicesGetter = MakeProtoFlagGetter("hasIndices", 'd');

    /// <summary>
    /// §22.2.5.12 <c>get RegExp.prototype.source</c>: a real RegExp returns its
    /// source; %RegExp.prototype% returns "(?:)"; otherwise TypeError.
    /// </summary>
    private static readonly BuiltInMethod _protoSourceGetter =
        BuiltInMethod.CreateV2("get source", 0, static (interp, recvV, _) =>
        {
            var recv = recvV.ToObject();
            if (recv is SharpTSRegExp rx) return RuntimeValue.FromString(rx.Source);
            RequireObject(recv, ".source");
            if (ReferenceEquals(recv, interp.GetRegExpPrototype())) return RuntimeValue.FromString("(?:)");
            throw new ThrowException(new SharpTSTypeError(
                "RegExp.prototype.source getter called on non-RegExp"));
        }).WithSpecLength(0);

    /// <summary>
    /// Resolves a well-known symbol on the RegExp prototype to the corresponding
    /// callable per ECMA-262 §22.2.5 (@@match, @@matchAll, @@replace, @@search, @@split).
    /// Returns null for symbols not present on the prototype. The returned
    /// method is bound to the supplied receiver so `regex[Symbol.match]('s')`
    /// dispatches with `regex` as `this`.
    /// </summary>
    public static object? GetSymbolMember(SharpTSRegExp receiver, SharpTSSymbol symbol)
    {
        var unbound = GetSymbolMethodUnbound(symbol);
        return unbound?.Bind(receiver);
    }

    /// <summary>
    /// Returns the unbound singleton callable for a well-known RegExp symbol,
    /// or null if the symbol isn't one of the five protocol methods. Used by
    /// the prototype object so accessing `RegExp.prototype[Symbol.match]`
    /// returns the same function value across calls (spec invariant).
    /// </summary>
    public static BuiltInMethod? GetSymbolMethodUnbound(SharpTSSymbol symbol)
    {
        if (ReferenceEquals(symbol, SharpTSSymbol.Match)) return _symbolMatch;
        if (ReferenceEquals(symbol, SharpTSSymbol.MatchAll)) return _symbolMatchAll;
        if (ReferenceEquals(symbol, SharpTSSymbol.Replace)) return _symbolReplace;
        if (ReferenceEquals(symbol, SharpTSSymbol.Search)) return _symbolSearch;
        if (ReferenceEquals(symbol, SharpTSSymbol.Split)) return _symbolSplit;
        return null;
    }

    // ===== ECMA-262 §22.2.5 abstract-operation helpers =====
    //
    // Each protocol method below now follows the spec algorithm: it accepts
    // ANY object as `this` (the test262 tests pass plain objects pretending
    // to be regex via `RegExp.prototype[Symbol.X].call(fakeRe, str)`) and
    // reads/writes flags, lastIndex, exec, etc. via Get/Set so user-defined
    // getters/setters (Object.defineProperty path) fire and propagate
    // their thrown errors. The previous implementations cast `recv` to
    // SharpTSRegExp directly, bypassing user overrides.

    /// <summary>
    /// ECMA-262 §22.2.5.2.1 RegExpExec(R, S) — invokes R.exec(S) if R has a
    /// callable `exec` property; otherwise falls back to RegExpBuiltinExec
    /// (only valid when R is a real SharpTSRegExp). Throws TypeError if
    /// exec returns a non-object non-null value.
    /// </summary>
    private static object? RegExpExec(Interpreter interp, object? rx, string s)
    {
        var exec = interp.GetProperty(rx, "exec");
        if (exec is ISharpTSCallable callable)
        {
            var result = FunctionBuiltIns.CallWithThis(interp, callable, rx, [s]);
            if (result is null || result is SharpTSUndefined) return null;
            // Per spec, exec must return Object or Null. Anything else → TypeError.
            if (result is not (SharpTSObject or SharpTSArray or SharpTSInstance))
                throw new ThrowException(new SharpTSTypeError(
                    "RegExp.prototype exec returned a non-object"));
            return result;
        }
        // No callable exec → fall back to the built-in matcher; only valid
        // when receiver is an actual RegExp (otherwise spec throws).
        if (rx is SharpTSRegExp regex)
        {
            return RegExpBuiltinExec(interp, regex, s);
        }
        throw new ThrowException(new SharpTSTypeError(
            "RegExp.prototype method called on non-RegExp without a callable exec"));
    }

    /// <summary>
    /// ECMA-262 §22.2.5.2.2 RegExpBuiltinExec for interpreter RegExp objects.
    /// The observable lastIndex operations deliberately use the descriptor
    /// store: its raw value is ToLength-coerced once on every execution, and
    /// global/sticky write-back uses Throw=true semantics.
    /// </summary>
    private static SharpTSArray? RegExpBuiltinExec(
        Interpreter interp, SharpTSRegExp regex, string input)
    {
        object? rawLastIndex = interp.GetPropertyValue(regex, "lastIndex");
        int lastIndex = ToLengthAsInt(interp, rawLastIndex);
        bool useLastIndex = regex.Global || regex.Sticky;
        int startIndex = useLastIndex ? lastIndex : 0;

        if (startIndex > input.Length)
        {
            if (useLastIndex) SetLastIndexOrThrow(interp, regex, 0);
            return null;
        }

        var match = regex.InternalRegex.Match(input, startIndex);
        if (regex.Sticky && match.Success && match.Index != startIndex)
            match = System.Text.RegularExpressions.Match.Empty;

        if (!match.Success)
        {
            if (useLastIndex) SetLastIndexOrThrow(interp, regex, 0);
            return null;
        }

        if (useLastIndex)
            SetLastIndexOrThrow(interp, regex, match.Index + match.Length);

        return regex.CreateExecResult(match, input);
    }

    private static int ToLengthAsInt(Interpreter interp, object? value)
    {
        double number = interp.ToNumberWithPrimitive(value);
        if (double.IsNaN(number) || number <= 0) return 0;
        if (double.IsPositiveInfinity(number) || number >= int.MaxValue)
            return int.MaxValue;
        return (int)Math.Truncate(number);
    }

    private static void SetLastIndexOrThrow(
        Interpreter interp, SharpTSRegExp regex, int value)
    {
        var descriptor = regex.GetOwnPropertyDescriptor("lastIndex");
        if (descriptor is { HasGet: false, HasSet: false, Writable: false })
        {
            throw new ThrowException(new SharpTSTypeError(
                "Cannot assign to read only property 'lastIndex'"));
        }
        if (descriptor is { HasSet: true, Set: null })
        {
            throw new ThrowException(new SharpTSTypeError(
                "Cannot set property 'lastIndex' which has only a getter"));
        }
        interp.SetProperty(regex, "lastIndex", (double)value);
    }

    /// <summary>
    /// Throws TypeError if <paramref name="rx"/> is not an Object per
    /// ECMA-262 §22.2.5 step 2. Strings/numbers/booleans/null/undefined are
    /// rejected; objects (including plain SharpTSObject, SharpTSRegExp, etc.)
    /// pass.
    /// </summary>
    private static void RequireObject(object? rx, string siteName)
    {
        // ECMA-262 "Type(R) is not Object" — reject every primitive kind
        // (number forms, string, boolean, null/undefined, Symbol, BigInt).
        if (rx is null or SharpTSUndefined or bool or double or int or long
            or float or decimal or char or string or SharpTSSymbol
            or SharpTSBigInt or System.Numerics.BigInteger)
            throw new ThrowException(new SharpTSTypeError(
                $"RegExp.prototype{siteName} called on non-object"));
    }

    /// <summary>
    /// Coerces <paramref name="value"/> to a JS-spec string. Wraps the
    /// interpreter's Stringify with explicit handling for the args[0] empty
    /// case (treated as `undefined`).
    /// </summary>
    private static string ToStr(Interpreter interp, object? value)
        => interp.ToStringForBuiltInArgument(value);

    /// <summary>
    /// ECMA-262 §22.2.4.1 RegExp(pattern, flags). Runs with interpreter access
    /// so the brand-aware steps work: IsRegExp (§22.2.7.2, reads
    /// <c>Get(pattern, @@match)</c>), the call-form same-constructor identity
    /// short-circuit (step 4.b — returns <paramref name="pattern"/> unchanged),
    /// and regexp-like <c>source</c>/<c>flags</c> extraction via <c>Get</c>
    /// (honoring user getters and propagating their throws, <c>source</c>
    /// before <c>flags</c>). <paramref name="isCallForm"/> is true for the
    /// <c>RegExp(...)</c> call form (NewTarget undefined); false for
    /// <c>new RegExp(...)</c>. Mirrors the compiled <c>RegExpFromArgs</c> /
    /// <c>BuiltInConstructorHandler.EmitRegExp</c>.
    /// </summary>
    public static object? ConstructRegExp(Interpreter interp, IReadOnlyList<object?> args, bool isCallForm)
    {
        object? pattern = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        object? flags = args.Count > 1 ? args[1] : SharpTSUndefined.Instance;
        bool flagsUndefined = flags is SharpTSUndefined;

        bool patternIsRegExp = IsRegExp(interp, pattern);

        // Step 4.b (call form only): if pattern is regexp-like, flags is
        // undefined, and SameValue(RegExp, Get(pattern, "constructor")) — return
        // the input object itself. The constructor Get can throw (a user getter).
        if (isCallForm && patternIsRegExp && flagsUndefined)
        {
            var patternConstructor = interp.GetProperty(pattern, "constructor");
            if (ReferenceEquals(patternConstructor, Execution.Interpreter.RegExpConstructorObject))
                return pattern;
        }

        string p, f;
        if (pattern is SharpTSRegExp realRx)
        {
            // pattern has [[RegExpMatcher]] — read its internal slots directly.
            p = realRx.Source;
            f = flagsUndefined ? realRx.Flags : ToStr(interp, flags);
        }
        else if (patternIsRegExp)
        {
            // Step 6: regexp-like — Get(source), then Get(flags) only when no
            // flags arg was supplied. `source` is read before `flags` (spec
            // order); both Gets may invoke user getters and propagate throws.
            p = ToStr(interp, interp.GetProperty(pattern, "source"));
            f = flagsUndefined ? ToStr(interp, interp.GetProperty(pattern, "flags")) : ToStr(interp, flags);
        }
        else
        {
            // Step 7: ordinary coercion — only `undefined` becomes "" (not the
            // literal "undefined"); everything else is ToString'd.
            p = pattern is SharpTSUndefined ? "" : ToStr(interp, pattern);
            f = flagsUndefined ? "" : ToStr(interp, flags);
        }
        return new SharpTSRegExp(p, f);
    }

    /// <summary>
    /// ECMA-262 §22.2.7.2 IsRegExp(argument): true when <paramref name="argument"/>
    /// is an object whose <c>@@match</c> is truthy, or (when <c>@@match</c> is
    /// absent) a real RegExp. A real regex with <c>re[@@match] = false</c> is
    /// NOT regexp-like.
    /// </summary>
    private static bool IsRegExp(Interpreter interp, object? argument)
    {
        if (argument is not (SharpTSObject or SharpTSRegExp or SharpTSInstance
            or SharpTSArray or ISharpTSCallable))
            return false;
        object? matcher = GetMatchMember(argument);
        if (matcher is not (null or SharpTSUndefined))
            return Compilation.RuntimeTypes.IsTruthy(matcher);
        return argument is SharpTSRegExp;
    }

    /// <summary>
    /// Reads <c>Get(argument, @@match)</c> — a user-set own symbol property
    /// wins over the inherited RegExp.prototype[@@match] method.
    /// </summary>
    private static object? GetMatchMember(object? argument)
    {
        switch (argument)
        {
            case SharpTSRegExp rx:
                return rx.TryGetSymbolProperty(SharpTSSymbol.Match, out var ov)
                    ? ov : GetSymbolMember(rx, SharpTSSymbol.Match);
            case SharpTSObject o:
                return o.GetBySymbol(SharpTSSymbol.Match);
            case SharpTSInstance inst:
                return inst.GetBySymbol(SharpTSSymbol.Match);
            default:
                return null;
        }
    }

    /// <summary>
    /// ECMA-262 §22.2.5.3 RegExp.prototype.get flags: build the canonical
    /// flags string by ToBoolean'ing each per-flag property via Get. Routes
    /// through the user-overridable property pipeline so that
    /// <c>r.global = false</c> after a writable redefine drops 'g', etc.
    /// </summary>
    private static string BuildFlagsString(Interpreter interp, object? receiver)
    {
        var sb = new System.Text.StringBuilder(8);
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "hasIndices"))) sb.Append('d');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "global"))) sb.Append('g');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "ignoreCase"))) sb.Append('i');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "multiline"))) sb.Append('m');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "dotAll"))) sb.Append('s');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "unicode"))) sb.Append('u');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "unicodeSets"))) sb.Append('v');
        if (Compilation.RuntimeTypes.IsTruthy(interp.GetProperty(receiver, "sticky"))) sb.Append('y');
        return sb.ToString();
    }

    /// <summary>
    /// ECMA-262 §7.1.20 ToLength as int. NaN/negative → 0; clamped to int.MaxValue.
    /// </summary>
    private static int ToLengthAsInt(object? value)
    {
        double d = value switch
        {
            null => 0,
            SharpTSUndefined => double.NaN,
            double dv => dv,
            int iv => iv,
            long lv => lv,
            bool bv => bv ? 1 : 0,
            string sv => double.TryParse(sv, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
            _ => double.NaN
        };
        if (double.IsNaN(d) || d <= 0) return 0;
        if (d > int.MaxValue) return int.MaxValue;
        return (int)d;
    }

    /// <summary>
    /// ECMA-262 §22.2.5.7 RegExp.prototype [@@match].
    /// </summary>
    private static RuntimeValue SymbolMatchImpl(Interpreter interp, RuntimeValue recvV, ReadOnlySpan<RuntimeValue> args)
    {
        var recv = recvV.ToObject();
        RequireObject(recv, "[Symbol.match]");

        // 3. Let S be ? ToString(string).
        var s = ToStr(interp, args.Length > 0 ? args[0].ToObject() : null);

        // 4. Let flags be ? ToString(? Get(rx, "flags")).
        var flags = ToStr(interp, interp.GetProperty(recv, "flags"));

        // 5. If flags does not contain "g", return ? RegExpExec(rx, S).
        if (!flags.Contains('g'))
            return RuntimeValue.FromBoxed(RegExpExec(interp, recv, s));

        // 6.a fullUnicode = flags contains "u"
        bool fullUnicode = flags.Contains('u');

        // 6.b Set(rx, "lastIndex", 0, true).
        interp.SetProperty(recv, "lastIndex", 0.0);

        // 6.c-e Loop, accumulate matched strings.
        var results = new List<object?>();
        while (true)
        {
            var result = RegExpExec(interp, recv, s);
            if (result is null)
            {
                if (results.Count == 0) return RuntimeValue.Null;
                return RuntimeValue.FromObject(new SharpTSArray(results));
            }

            // matchStr = ToString(Get(result, "0"))
            var matchStr = ToStr(interp, interp.GetProperty(result, "0"));
            results.Add(matchStr);

            // If matchStr is empty, advance lastIndex to avoid infinite loop.
            if (matchStr.Length == 0)
            {
                var thisIndex = ToLengthAsInt(interp.GetProperty(recv, "lastIndex"));
                int nextIndex = AdvanceStringIndex(s, thisIndex, fullUnicode);
                interp.SetProperty(recv, "lastIndex", (double)nextIndex);
            }
        }
    }

    /// <summary>
    /// ECMA-262 §22.2.5.11 RegExp.prototype [@@search].
    /// </summary>
    private static RuntimeValue SymbolSearchImpl(Interpreter interp, RuntimeValue recvV, ReadOnlySpan<RuntimeValue> args)
    {
        var recv = recvV.ToObject();
        RequireObject(recv, "[Symbol.search]");
        var s = interp.ToStringForBuiltInArgument(
            args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);

        // Save lastIndex, set to 0, run RegExpExec, restore lastIndex.
        var previousLastIndex = interp.GetProperty(recv, "lastIndex");
        if (!SameValue(previousLastIndex, 0.0))
            interp.SetProperty(recv, "lastIndex", 0.0);

        var result = RegExpExec(interp, recv, s);

        var currentLastIndex = interp.GetProperty(recv, "lastIndex");
        if (!SameValue(currentLastIndex, previousLastIndex))
            interp.SetProperty(recv, "lastIndex", previousLastIndex);

        if (result is null) return RuntimeValue.FromNumber(-1.0);
        var index = interp.GetProperty(result, "index");
        return RuntimeValue.FromBoxed(index ?? 0.0);
    }

    /// <summary>
    /// ECMA-262 §22.2.5.10 RegExp.prototype [@@replace] — simplified
    /// implementation supporting string + callable replacement values.
    /// Reads flags via Get; routes through RegExpExec so user-installed
    /// `exec` overrides participate.
    /// </summary>
    private static RuntimeValue SymbolReplaceImpl(Interpreter interp, RuntimeValue recvV, ReadOnlySpan<RuntimeValue> args)
    {
        var recv = recvV.ToObject();
        RequireObject(recv, "[Symbol.replace]");
        var s = interp.ToStringForBuiltInArgument(
            args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
        var replaceValue = args.Length > 1
            ? args[1].ToObject()
            : SharpTSUndefined.Instance;

        // Read flags via Get so user getters fire.
        var flags = ToStr(interp, interp.GetProperty(recv, "flags"));
        bool global = flags.Contains('g');
        bool fullUnicode = global && flags.Contains('u');

        // Reset lastIndex when global.
        if (global)
            interp.SetProperty(recv, "lastIndex", 0.0);

        // Collect all match results so we can replace bottom-up.
        var matches = new List<object>();
        while (true)
        {
            var result = RegExpExec(interp, recv, s);
            if (result is null) break;
            matches.Add(result);
            if (!global) break;

            var matchStr = ToStr(interp, interp.GetProperty(result, "0"));
            if (matchStr.Length == 0)
            {
                var thisIndex = ToLengthAsInt(interp.GetProperty(recv, "lastIndex"));
                int nextIndex = AdvanceStringIndex(s, thisIndex, fullUnicode);
                interp.SetProperty(recv, "lastIndex", (double)nextIndex);
            }
        }

        if (matches.Count == 0) return RuntimeValue.FromString(s);

        // Build the result string with replacements.
        bool isCallable = replaceValue is ISharpTSCallable;
        string replaceStr = isCallable
            ? ""
            : interp.ToStringForBuiltInArgument(replaceValue);
        var sb = new System.Text.StringBuilder();
        int nextSourcePosition = 0;
        foreach (var match in matches)
        {
            var matchStr = ToStr(interp, interp.GetProperty(match, "0"));
            double rawPosition = interp.ToNumberWithPrimitive(
                interp.GetProperty(match, "index"));
            int position = double.IsNaN(rawPosition) || rawPosition <= 0
                ? 0
                : double.IsPositiveInfinity(rawPosition) || rawPosition >= s.Length
                    ? s.Length
                    : (int)Math.Truncate(rawPosition);

            int resultLength = ToLengthAsInt(
                interp, interp.GetProperty(match, "length"));
            var captures = new List<object?>(Math.Max(0, resultLength - 1));
            for (int i = 1; i < resultLength; i++)
            {
                object? capture = interp.GetProperty(match, i.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                captures.Add(capture is SharpTSUndefined
                    ? SharpTSUndefined.Instance
                    : ToStr(interp, capture));
            }

            string replacement;
            if (isCallable)
            {
                // Build args: [matched, ...captures, position, string].
                var callArgs = new List<object?> { matchStr };
                callArgs.AddRange(captures);
                callArgs.Add((double)position);
                callArgs.Add(s);
                var fnResult = FunctionBuiltIns.CallWithThis(
                    interp, (ISharpTSCallable)replaceValue!,
                    SharpTSUndefined.Instance, callArgs);
                replacement = ToStr(interp, fnResult);
            }
            else
            {
                replacement = ExpandReplacement(
                    interp, replaceStr, s, matchStr, position, captures);
            }

            // Append the un-modified slice + replacement.
            if (position >= nextSourcePosition)
            {
                sb.Append(s, nextSourcePosition, position - nextSourcePosition);
                sb.Append(replacement);
                nextSourcePosition = position + matchStr.Length;
            }
        }
        if (nextSourcePosition < s.Length)
            sb.Append(s, nextSourcePosition, s.Length - nextSourcePosition);
        return RuntimeValue.FromString(sb.ToString());
    }

    private static string ExpandReplacement(
        Interpreter interp,
        string replacement,
        string input,
        string matched,
        int position,
        List<object?> captures)
    {
        var result = new System.Text.StringBuilder(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                result.Append(replacement[i]);
                continue;
            }

            char next = replacement[i + 1];
            switch (next)
            {
                case '$':
                    result.Append('$');
                    i++;
                    continue;
                case '&':
                    result.Append(matched);
                    i++;
                    continue;
                case '`':
                    result.Append(input, 0, position);
                    i++;
                    continue;
                case '\'':
                    int tailStart = position + matched.Length;
                    result.Append(input, tailStart, input.Length - tailStart);
                    i++;
                    continue;
            }

            if (next == '0'
                && i + 2 < replacement.Length
                && replacement[i + 2] is >= '1' and <= '9')
            {
                int leadingZeroCapture = replacement[i + 2] - '0';
                if (leadingZeroCapture <= captures.Count)
                {
                    object? leadingCaptureValue = captures[leadingZeroCapture - 1];
                    if (leadingCaptureValue is not (null or SharpTSUndefined))
                        result.Append(ToStr(interp, leadingCaptureValue));
                    i += 2;
                    continue;
                }
            }

            if (next is < '1' or > '9')
            {
                result.Append('$');
                continue;
            }

            int captureNumber = next - '0';
            int consumedDigits = 1;
            if (i + 2 < replacement.Length
                && replacement[i + 2] is >= '0' and <= '9')
            {
                int twoDigit = captureNumber * 10 + replacement[i + 2] - '0';
                if (twoDigit <= captures.Count)
                {
                    captureNumber = twoDigit;
                    consumedDigits = 2;
                }
            }

            if (captureNumber > captures.Count)
            {
                result.Append('$');
                continue;
            }

            object? capture = captures[captureNumber - 1];
            if (capture is not (null or SharpTSUndefined))
                result.Append(ToStr(interp, capture));
            i += consumedDigits;
        }
        return result.ToString();
    }

    /// <summary>
    /// ECMA-262 §22.2.5.13 RegExp.prototype [@@split]. Constructs a
    /// sticky-flagged splitter via SpeciesConstructor, then iterates
    /// matches via that splitter (so user-installed Symbol.species or
    /// constructor.exec participates).
    /// </summary>
    private static RuntimeValue SymbolSplitImpl(Interpreter interp, RuntimeValue recvV, ReadOnlySpan<RuntimeValue> args)
    {
        var recv = recvV.ToObject();
        RequireObject(recv, "[Symbol.split]");
        var s = ToStr(interp, args.Length > 0 ? args[0].ToObject() : null);
        var limitArg = args.Length > 1 ? args[1].ToObject() : null;

        // §22.2.5.13 step 4: C = SpeciesConstructor(rx, %RegExp%).
        var speciesCtor = SpeciesConstructor(interp, recv);

        // Step 5: flags = ToString(? Get(rx, "flags")).
        var flags = ToStr(interp, interp.GetProperty(recv, "flags"));
        bool fullUnicode = flags.Contains('u');

        // Step 7-8: newFlags = "y" in flags ? flags : flags+"y".
        string newFlags = flags.Contains('y') ? flags : flags + "y";

        // Step 9: splitter = ? Construct(C, « rx, newFlags »).
        // When the user didn't override constructor / species, fall back to
        // %RegExp% — synthesize a fresh sticky-flagged regex from recv's
        // source. The matching loop relies on sticky semantics (match must
        // start at exactly lastIndex) to scan strictly forward.
        object? splitter;
        if (speciesCtor != null)
        {
            splitter = interp.Construct(speciesCtor, [recv, newFlags]);
        }
        else if (recv is SharpTSRegExp rxInstance)
        {
            splitter = new SharpTSRegExp(rxInstance.Source, newFlags);
        }
        else
        {
            // Non-RegExp `this` without species ctor — nothing to construct.
            // Fall through with recv; the matching loop still routes through
            // RegExpExec for any user-installed exec on it.
            splitter = recv;
        }

        // Step 12: lim = ToUint32(limit).
        long limit = limitArg switch
        {
            null or SharpTSUndefined => uint.MaxValue,
            double d => double.IsNaN(d) ? 0 : (long)((uint)d),
            _ => 0,
        };
        if (limit == 0) return RuntimeValue.FromObject(new SharpTSArray(new List<object?>()));

        var arr = new List<object?>();
        if (s.Length == 0)
        {
            // Empty input string: if splitter matches empty → return []; else [""].
            interp.SetProperty(splitter, "lastIndex", 0.0);
            var result = RegExpExec(interp, splitter, s);
            if (result is null) arr.Add("");
            return RuntimeValue.FromObject(new SharpTSArray(arr));
        }

        int p = 0;     // position in S where the next non-matched segment starts
        int q = 0;     // current scan position

        while (q < s.Length)
        {
            interp.SetProperty(splitter, "lastIndex", (double)q);
            var z = RegExpExec(interp, splitter, s);
            if (z is null)
            {
                q = AdvanceStringIndex(s, q, fullUnicode);
                continue;
            }

            // Read post-match lastIndex. With sticky support in SharpTSRegExp
            // (`y` flag preserved + Exec verifies match position == LastIndex),
            // a successful exec advances lastIndex by the match length. For
            // non-sticky receivers (user-installed exec on a plain object),
            // the user is responsible for advancing lastIndex.
            int e = ToLengthAsInt(interp.GetProperty(splitter, "lastIndex"));
            e = Math.Min(e, s.Length);
            if (e == p)
            {
                q = AdvanceStringIndex(s, q, fullUnicode);
                continue;
            }

            // Add matched segment to output.
            arr.Add(s.Substring(p, q - p));
            if (arr.Count >= limit) return RuntimeValue.FromObject(new SharpTSArray(arr));

            // Add capture groups.
            if (z is SharpTSArray zArr)
            {
                for (int i = 1; i < zArr.Length; i++)
                {
                    arr.Add(zArr[i]);
                    if (arr.Count >= limit) return RuntimeValue.FromObject(new SharpTSArray(arr));
                }
            }

            p = e;
            q = e;
        }

        arr.Add(s.Substring(p));
        return RuntimeValue.FromObject(new SharpTSArray(arr));
    }

    /// <summary>
    /// ECMA-262 §10.2.5 SpeciesConstructor(O, defaultConstructor) — looks
    /// up the species constructor for <paramref name="O"/>. Returns null
    /// when the user didn't override (the caller should fall back to the
    /// default %RegExp%; we represent "use default" as null and let the
    /// caller decide whether to construct a fresh regex via the built-in
    /// factory or skip construction entirely).
    /// </summary>
    private static object? SpeciesConstructor(Interpreter interp, object? O)
    {
        // Step 2: Let C be ? Get(O, "constructor").
        var c = interp.GetProperty(O, "constructor");

        // Step 3: If C is undefined, return defaultConstructor (we signal
        // "use default" with null).
        if (c is null or SharpTSUndefined) return null;

        // Step 4: If Type(C) is not Object, throw TypeError.
        if (c is bool or double or string)
            throw new ThrowException(new SharpTSTypeError(
                "constructor must be an object"));

        // Step 5: Let S be ? Get(C, @@species).
        // For symbol-keyed lookup we go through the symbol-dict mechanism on
        // the constructor object; SharpTSObject / Function / etc. expose
        // GetBySymbol or accept defineProperty for symbol keys. Symbol-keyed
        // accessors (`Object.defineProperty(fn, Symbol.species, {get: ...})`)
        // win over data values — test262's species-ctor-species-get-err.js
        // installs a throwing getter that this lookup must propagate.
        var species = c switch
        {
            SharpTSObject sObj => sObj.GetBySymbol(SharpTSSymbol.Species),
            SharpTSInstance inst => inst.GetBySymbol(SharpTSSymbol.Species),
            SharpTSFunction fn => GetSymbolPropertyFromCallable(interp, fn, SharpTSSymbol.Species),
            SharpTSArrowFunction arr => GetSymbolPropertyFromCallable(interp, arr, SharpTSSymbol.Species),
            _ => null
        };

        // Step 6-7: undefined / null → return defaultConstructor.
        if (species is null or SharpTSUndefined) return null;

        // Step 8: IsConstructor(S) → return S. We don't have IsConstructor
        // implemented, so accept any callable (matches most test262 patterns
        // — they install plain functions as species).
        if (species is ISharpTSCallable) return species;

        throw new ThrowException(new SharpTSTypeError("species is not a constructor"));
    }

    /// <summary>
    /// Reads a symbol-keyed property from a callable, honoring accessors
    /// installed via <c>Object.defineProperty(fn, sym, {get, set})</c>.
    /// Throwing getters propagate back to the caller (test262
    /// species-ctor-species-get-err.js depends on this).
    /// </summary>
    private static object? GetSymbolPropertyFromCallable(Interpreter interp, object obj, SharpTSSymbol symbol)
    {
        if (obj is SharpTSFunction fn)
        {
            if (fn.TryGetSymbolAccessor(symbol, out var getter, out _) && getter != null)
                return getter.Call(interp, []);
            if (fn.TryGetSymbolProperty(symbol, out var v)) return v;
        }
        if (obj is SharpTSArrowFunction arr)
        {
            if (arr.TryGetSymbolAccessor(symbol, out var arrGetter, out _) && arrGetter != null)
                return arrGetter.Call(interp, []);
            if (arr.TryGetSymbolProperty(symbol, out var v2)) return v2;
        }
        return null;
    }

    /// <summary>
    /// ECMA-262 §22.2.5.8 RegExp.prototype [@@matchAll]. Simplified — returns
    /// a $Array of match results. Doesn't yet honor SpeciesConstructor
    /// (callers' tests cover that separately); reads flags via Get for
    /// user-getter propagation.
    /// </summary>
    private static RuntimeValue SymbolMatchAllImpl(Interpreter interp, RuntimeValue recvV, ReadOnlySpan<RuntimeValue> args)
    {
        var recv = recvV.ToObject();
        RequireObject(recv, "[Symbol.matchAll]");
        var s = ToStr(interp, args.Length > 0 ? args[0].ToObject() : null);

        var flags = ToStr(interp, interp.GetProperty(recv, "flags"));
        bool fullUnicode = flags.Contains('u');

        // Reset lastIndex when global; for non-global, the per-spec sequence
        // would species-construct a global splitter, but our simplified
        // implementation just iterates from current lastIndex (or 0 if none).
        if (flags.Contains('g'))
            interp.SetProperty(recv, "lastIndex", 0.0);

        // Detect the underlying-regex/global mismatch: if `flags` claims
        // 'g' but the actual SharpTSRegExp's internal [[Global]] bit is
        // false (e.g. user did `Object.defineProperty(re, 'flags',
        // {value:'g'})` on `/\w/`), our exec ignores lastIndex so we'd loop
        // forever on the same match. In that case we re-implement the loop
        // in terms of string-slicing rather than lastIndex, which exec is
        // required to respect by spec but our internal matcher does not
        // when its construction-time [[Global]] is false.
        bool flagsClaimsGlobal = flags.Contains('g');
        bool underlyingGlobal = recv is SharpTSRegExp recvRx && recvRx.Global;
        bool slicePath = flagsClaimsGlobal && !underlyingGlobal;

        var results = new List<object?>();
        int sliceOffset = 0;
        while (true)
        {
            string searchStr = slicePath ? s.Substring(sliceOffset) : s;
            var match = RegExpExec(interp, recv, searchStr);
            if (match is null) break;

            if (slicePath)
            {
                // Adjust match.index from slice-relative to absolute.
                var localIdx = ToLengthAsInt(interp.GetProperty(match, "index"));
                interp.SetProperty(match, "index", (double)(localIdx + sliceOffset));
            }
            results.Add(match);

            if (!flagsClaimsGlobal) break;

            var matchStr = ToStr(interp, interp.GetProperty(match, "0"));
            if (matchStr.Length == 0)
            {
                var thisIndex = ToLengthAsInt(interp.GetProperty(recv, "lastIndex"));
                int nextIndex = AdvanceStringIndex(s, thisIndex, fullUnicode);
                interp.SetProperty(recv, "lastIndex", (double)nextIndex);
                if (slicePath) sliceOffset = nextIndex;
            }
            else if (slicePath)
            {
                var absIdx = ToLengthAsInt(interp.GetProperty(match, "index"));
                int matchEnd = absIdx + matchStr.Length;
                if (matchEnd <= sliceOffset) break;
                sliceOffset = matchEnd;
                interp.SetProperty(recv, "lastIndex", (double)matchEnd);
            }
        }
        return RuntimeValue.FromObject(new SharpTSIterator(results));
    }

    /// <summary>
    /// ECMA-262 §22.2.5.2.3 AdvanceStringIndex(S, index, unicode) — when
    /// unicode + the code unit at index is a high surrogate paired with the
    /// next being a low surrogate, advance by 2; otherwise by 1.
    /// </summary>
    private static int AdvanceStringIndex(string s, int index, bool unicode)
    {
        if (!unicode || index + 1 >= s.Length) return index + 1;
        int first = s[index];
        if (first < 0xD800 || first > 0xDBFF) return index + 1;
        int second = s[index + 1];
        if (second < 0xDC00 || second > 0xDFFF) return index + 1;
        return index + 2;
    }

    /// <summary>
    /// ECMA-262 §7.2.10 SameValue(x, y) — numeric SameValue (handles +0/-0
    /// and NaN) plus reference equality for objects / strings / etc.
    /// </summary>
    private static bool SameValue(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is double da && b is double db)
        {
            if (double.IsNaN(da) && double.IsNaN(db)) return true;
            if (da == 0 && db == 0)
                return BitConverter.DoubleToInt64Bits(da)
                    == BitConverter.DoubleToInt64Bits(db);
            return da.Equals(db);
        }
        return Equals(a, b);
    }
}
