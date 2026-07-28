using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript RegExp objects.
/// </summary>
/// <remarks>
/// Wraps .NET System.Text.RegularExpressions.Regex with JavaScript semantics.
/// Supports global (g), ignoreCase (i), and multiline (m) flags.
/// Maintains lastIndex for global matching.
/// </remarks>
public class SharpTSRegExp : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.RegExp;

    /// <summary>
    /// Immutable, fully-validated template for a (pattern, flags) pair: the
    /// compiled .NET <c>Regex</c> plus the derived flag state. Everything here
    /// is a pure function of (pattern, flags), so it is computed once per
    /// distinct literal and shared across every construction.
    /// </summary>
    private readonly record struct RegExpTemplate(
        Regex Regex, string Flags, bool Global, bool IgnoreCase, bool Multiline);

    /// <summary>
    /// Template cache keyed by (pattern, flags). A regex literal in TS source
    /// like <c>/foo/g</c> evaluates to <c>new SharpTSRegExp("foo", "g")</c>
    /// every time the expression runs — in a tight loop that's per-iter flag
    /// validation, normalization (a <c>StringBuilder</c>), two full pattern
    /// scans (<see cref="ValidateModifiers"/>, <see cref="HasNamedGroups"/>),
    /// options derivation, and a <c>Regex</c> compile. All of it is a pure
    /// function of (pattern, flags), so the cache collapses repeat
    /// constructions to a single dictionary lookup plus field copies
    /// (~85 ns → a few ns for the hot literal here).
    ///
    /// Why a tuple key works: ECMAScript's regex semantics here are fully
    /// captured by (source, flags) — no instance-specific state lives on the
    /// .NET <c>Regex</c> object. Per-instance <c>LastIndex</c> stays on
    /// <c>SharpTSRegExp</c> (ECMA-262 makes each literal evaluation a fresh
    /// object), so sharing the immutable template is safe. An invalid pattern
    /// or flags throws inside the factory and is not cached, so the
    /// spec-required SyntaxError still fires on every construction. Cache size
    /// is bounded by the set of distinct regex literals, which is finite.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Pattern, string Flags), RegExpTemplate> _templateCache = new();

    private readonly Regex _regex;
    private readonly string _source;
    private readonly string _flags;
    private readonly bool _global;
    private readonly bool _ignoreCase;
    private readonly bool _multiline;
    // JS: RegExp instances are objects and support arbitrary property assignment
    // (e.g. minimatch's `Object.assign(new RegExp(...), { _src, _glob })`).
    // internal: emitted $RegExp has its own storage; runtime↔emitted parity
    // tests track public methods only (see RuntimeTypeSyncTests).
    private readonly SharpTSObject _properties = new([]);
    // ECMA-262 §22.2 defines `flags`/`global`/`unicode`/`lastIndex` as
    // configurable getters on RegExp.prototype — user code can override them
    // via `Object.defineProperty(rx, "flags", {get: ...})`. Without per-
    // instance accessor storage the override is silently ignored: tests that
    // install throwing getters (test262 .../coerce-global.js,
    // .../get-flags-throws.js, etc.) bypass the user code entirely. Data and
    // accessor properties share the descriptor-aware _properties store.
    // Symbol-keyed own properties. ECMA-262 lets a regex carry symbol keys
    // (e.g. `re[Symbol.match] = false`), and IsRegExp (§22.2.7.2) reads
    // `Get(re, @@match)` — a user override must win over the inherited
    // RegExp.prototype[@@match] method. Lazily allocated; kept internal so the
    // runtime↔emitted public-method parity test (RuntimeTypeSyncTests) isn't
    // affected (the emitted $RegExp has its own symbol storage).
    private Dictionary<SharpTSSymbol, object?>? _symbolProps;

    internal bool TryGetSymbolProperty(SharpTSSymbol symbol, out object? value)
    {
        if (_symbolProps != null && _symbolProps.TryGetValue(symbol, out value))
            return true;
        value = null;
        return false;
    }

    internal void SetBySymbol(SharpTSSymbol symbol, object? value)
    {
        _symbolProps ??= [];
        _symbolProps[symbol] = value;
    }

    internal bool HasSymbolProperty(SharpTSSymbol symbol) =>
        _symbolProps != null && _symbolProps.ContainsKey(symbol);

    internal bool TryGetProperty(string name, out object? value)
    {
        var descriptor = _properties.GetOwnPropertyDescriptor(name);
        if (descriptor is { HasGet: false, HasSet: false })
        {
            value = _properties.GetProperty(name);
            return true;
        }
        value = null;
        return false;
    }

    internal void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    internal bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _properties.DefineProperty(name, descriptor);

    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.GetOwnPropertyDescriptor(name);

    internal bool HasOwnProperty(string name) => _properties.HasProperty(name);

    internal bool DeleteProperty(string name) => _properties.DeleteProperty(name);

    internal IEnumerable<string> OwnEnumerableKeys() => _properties.OwnEnumerableKeys();

    /// <summary>
    /// Registers an accessor pair from <c>Object.defineProperty</c>. Either
    /// half may be null (one-sided accessor). A subsequent definition on
    /// the same key replaces the previous one — JS allows redefining
    /// configurable accessors, and §22.2 leaves these slots configurable.
    /// </summary>
    public void DefineAccessor(string name, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _properties.DefineProperty(name, new SharpTSPropertyDescriptor
        {
            Get = getter,
            Set = setter,
            HasGet = true,
            HasSet = true,
            Enumerable = false,
            Configurable = true,
            HasEnumerable = true,
            HasConfigurable = true,
        });
    }

    /// <summary>
    /// Looks up a user-installed accessor for <paramref name="name"/>.
    /// Returns true and the (getter, setter) pair when one is registered.
    /// </summary>
    public bool TryGetAccessor(string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        var descriptor = _properties.GetOwnPropertyDescriptor(name);
        if (descriptor is { HasGet: true } or { HasSet: true })
        {
            getter = descriptor.Get;
            setter = descriptor.Set;
            return true;
        }
        getter = null;
        setter = null;
        return false;
    }

    /// <summary>
    /// The index at which to start the next match (used with global flag).
    /// </summary>
    public int LastIndex { get; set; }

    /// <summary>
    /// The pattern string of the regular expression.
    /// </summary>
    public string Source => _source;

    /// <summary>
    /// The flags string of the regular expression.
    /// </summary>
    public string Flags => _flags;

    /// <summary>
    /// Whether the global (g) flag is set.
    /// </summary>
    public bool Global => _global;

    /// <summary>
    /// Whether the ignoreCase (i) flag is set.
    /// </summary>
    public bool IgnoreCase => _ignoreCase;

    /// <summary>
    /// Whether the multiline (m) flag is set.
    /// </summary>
    public bool Multiline => _multiline;

    /// <summary>
    /// Whether the sticky (y) flag is set. ECMA-262 §22.2.6: sticky matches
    /// must start at exactly <see cref="LastIndex"/>; failure resets
    /// LastIndex to 0. Used by the SpeciesConstructor protocol in
    /// <c>RegExp.prototype[@@split]</c> (which constructs a sticky-flagged
    /// splitter to scan strictly forward).
    /// </summary>
    public bool Sticky => _flags.Contains('y');

    /// <summary>
    /// Whether the unicode (u) flag is set.
    /// </summary>
    public bool Unicode => _flags.Contains('u');

    /// <summary>
    /// Creates a RegExp with the specified pattern and optional flags.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="flags">The flags string (g, i, m).</param>
    public SharpTSRegExp(string pattern, string flags = "")
    {
        _source = pattern;
        // All the per-construction work — flag validation/normalization, the
        // modifier/unicode/named-group pattern scans, options derivation, and
        // the Regex compile — is a pure function of (pattern, flags), so it is
        // computed once by BuildTemplate and cached. A repeat construction of
        // the same literal (the hot loop case) is now a dictionary lookup plus
        // field copies. LastIndex is per-instance (each literal evaluation is a
        // fresh object per ECMA-262), so only the immutable template is shared.
        var template = _templateCache.GetOrAdd((pattern, flags),
            static key => BuildTemplate(key.Pattern, key.Flags));
        _regex = template.Regex;
        _flags = template.Flags;
        _global = template.Global;
        _ignoreCase = template.IgnoreCase;
        _multiline = template.Multiline;
        LastIndex = 0;
    }

    /// <summary>
    /// Validates, normalizes, and compiles a (pattern, flags) pair into an
    /// immutable <see cref="RegExpTemplate"/>. Runs only on a cache miss (once
    /// per distinct literal). Validation ordering matches ECMA-262 throw
    /// ordering exactly — flags first (§22.2.3.3), then modifier groups, then
    /// the Annex B u/v-mode subset, then the pattern compile — so an invalid
    /// literal raises the same SyntaxError it did when this lived inline in the
    /// constructor. A throw here propagates out of <c>GetOrAdd</c> and is not
    /// cached, so the SyntaxError fires on every construction, as required.
    /// </summary>
    private static RegExpTemplate BuildTemplate(string pattern, string flags)
    {
        // ECMA-262 §22.2.3.3: each flag must be one of d/g/i/m/s/u/v/y, with no
        // duplicates and not both u and v. NormalizeFlags silently drops invalid
        // flags, so validate the raw string first and throw SyntaxError.
        ValidateFlags(flags);
        string norm = NormalizeFlags(flags);
        bool ignoreCase = norm.Contains('i');
        bool multiline = norm.Contains('m');

        // ES2025 modifier-group early errors (e.g. (?i-i:), (?ii:), (?-:)) — .NET
        // accepts several of these, so validate up front and throw SyntaxError.
        ValidateModifiers(pattern);

        // ECMA-262 Annex B distinguishes Unicode (`u`/`v`) mode, where several
        // forms .NET tolerates are SyntaxErrors. We validate the clearly-invalid,
        // false-positive-free subset here (others are a deferred follow-up).
        if (norm.Contains('u') || norm.Contains('v'))
            ValidateUnicodePattern(pattern);

        // Named groups ((?<name>...)) are not supported in ECMAScript mode in .NET.
        // Detect them and fall back to non-ECMAScript mode.
        // .NET also rejects combining ECMAScript with Singleline, so drop ECMAScript
        // whenever the 's' (dotAll) flag is set.
        bool useEcmaScript = !HasNamedGroups(pattern) && !norm.Contains('s');
        var options = useEcmaScript ? RegexOptions.ECMAScript : RegexOptions.None;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        if (multiline) options |= RegexOptions.Multiline;
        if (norm.Contains('s')) options |= RegexOptions.Singleline;
        // Compile the engine to IL rather than running it interpreted. The
        // one-time JIT cost is paid once per distinct literal (this factory runs
        // on a cache miss only) and every subsequent Match/Matches/Replace runs
        // the compiled matcher. Measured ~1.3x on raw matching.
        options |= RegexOptions.Compiled;

        try
        {
            // Rewrite the JS shorthand escapes (\d \w \s and negations) to
            // explicit ECMAScript-exact sets before compiling — see
            // RewriteEcmaScriptShorthands. Runs once per distinct literal.
            var regex = new Regex(RewriteEcmaScriptShorthands(pattern), options);
            return new RegExpTemplate(regex, norm, norm.Contains('g'), ignoreCase, multiline);
        }
        catch (ArgumentException ex)
        {
            // .NET wraps the underlying RegexParseException in ArgumentException.
            // ECMA-262 §22.2.3.1 mandates a SyntaxError for an invalid pattern, so
            // surface it as a guest-catchable SharpTSSyntaxError (so
            // `e instanceof SyntaxError` and `assert.throws(SyntaxError, ...)`
            // hold) rather than a bare host Exception.
            throw new Exceptions.ThrowException(
                new SharpTSSyntaxError($"Invalid regular expression: {ex.Message}"));
        }
    }

    // ECMA-262 §22.2.2.10 WhiteSpace + LineTerminator — the exact set JS `\s`
    // matches: [ \t\n\v\f\r] plus the Unicode space separators (Zs) and the
    // line/paragraph separators. Written as the *inner* of a character class so
    // it can be spliced into `[…]` (positive) or `[^…]` (negated). The emitted
    // `$RegExp` mirror bakes this exact value in via Ldstr (RuntimeEmitter.TSRegExp.cs),
    // so there is a single source of truth for the set.
    internal const string WhitespaceClassInner =
        @"\t\n\x0B\f\r\x20\xA0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF";

    /// <summary>
    /// Rewrites the ECMAScript shorthand class escapes (<c>\d \D \w \W \s \S</c>)
    /// to explicit character sets before the pattern is handed to .NET, so the
    /// compiled matcher follows JS semantics rather than .NET's. This sidesteps
    /// two divergences in <see cref="RegexOptions.ECMAScript"/> mode:
    /// <list type="bullet">
    /// <item>.NET's <c>\w</c> matches U+0130 (İ); JS's does not.</item>
    /// <item>.NET's <c>\s</c> is just <c>[ \t\n\v\f\r]</c> — missing the Unicode
    /// WhiteSpace/LineTerminator chars JS includes (U+00A0, U+1680,
    /// U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000, U+FEFF).</item>
    /// </list>
    /// In the non-ECMAScript fallback (named groups / <c>s</c> flag) the same
    /// shorthands are Unicode-broad in .NET, so pinning them to the JS sets there
    /// is also a fix, not a regression. The rewrite is context-aware: the
    /// positive forms expand both inside and outside <c>[…]</c> classes; the
    /// negated forms (<c>\D \W \S</c>) expand only *outside* a class — a negated
    /// shorthand inside a class can't be expressed without engine-level class
    /// nesting, so those pass through unchanged (tracked by #749). Backslash
    /// escapes are respected, so <c>\\d</c>, <c>\cd</c>, backreferences, etc. are
    /// left untouched. <c>iu</c>-mode case-folding edge cases (K U+212A, ſ U+017F
    /// folding into <c>\w</c>) remain out of scope.
    /// The RegExp's <c>source</c> keeps the original pattern; only the internal
    /// matcher sees the rewrite.
    /// </summary>
    internal static string RewriteEcmaScriptShorthands(string pattern)
    {
        StringBuilder? sb = null;
        bool inClass = false;
        int n = pattern.Length;
        for (int i = 0; i < n; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < n)
            {
                char next = pattern[i + 1];
                string? repl = ExpandShorthand(next, inClass);
                if (repl != null)
                {
                    // Lazily allocate, back-filling the untouched prefix.
                    if (sb == null)
                    {
                        sb = new StringBuilder(n + 16);
                        sb.Append(pattern, 0, i);
                    }
                    sb.Append(repl);
                }
                else
                {
                    // Not a rewritable shorthand (or a negated form inside a
                    // class) — copy the escape pair through verbatim.
                    sb?.Append('\\').Append(next);
                }
                i++; // also consume the escaped char
                continue;
            }
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            sb?.Append(c);
        }
        return sb == null ? pattern : sb.ToString();
    }

    /// <summary>
    /// Returns the explicit replacement for a shorthand escape letter, or
    /// <c>null</c> when the letter is not a shorthand or is a negated shorthand
    /// inside a character class (which must pass through unchanged).
    /// </summary>
    private static string? ExpandShorthand(char esc, bool inClass) => esc switch
    {
        'd' => inClass ? "0-9" : "[0-9]",
        'D' => inClass ? null : "[^0-9]",
        'w' => inClass ? "A-Za-z0-9_" : "[A-Za-z0-9_]",
        'W' => inClass ? null : "[^A-Za-z0-9_]",
        's' => inClass ? WhitespaceClassInner : "[" + WhitespaceClassInner + "]",
        'S' => inClass ? null : "[^" + WhitespaceClassInner + "]",
        _ => null,
    };

    /// <summary>
    /// ECMA-262 §22.2.3.3 flag validation: throws SyntaxError if a flag is not
    /// one of d/g/i/m/s/u/v/y, a flag repeats, or both u and v are present.
    /// Kept in sync with the emitted <c>$RegExp.ValidateFlags</c> (compiled).
    /// </summary>
    private static void ValidateFlags(string flags)
    {
        int seen = 0;
        foreach (char f in flags)
        {
            int bit = f switch
            {
                'd' => 1, 'g' => 2, 'i' => 4, 'm' => 8,
                's' => 16, 'u' => 32, 'v' => 64, 'y' => 128,
                _ => 0
            };
            if (bit == 0) ThrowFlagsSyntax();          // unknown flag
            if ((seen & bit) != 0) ThrowFlagsSyntax(); // duplicate flag
            seen |= bit;
        }
        if ((seen & 32) != 0 && (seen & 64) != 0) ThrowFlagsSyntax(); // u and v
    }

    private static void ThrowFlagsSyntax() =>
        throw new Exceptions.ThrowException(new SharpTSSyntaxError(
            "Invalid regular expression flags"));

    /// <summary>
    /// Normalize and deduplicate flags, preserving only valid flags.
    /// </summary>
    private static string NormalizeFlags(string flags)
    {
        var sb = new StringBuilder();
        if (flags.Contains('g')) sb.Append('g');
        if (flags.Contains('i')) sb.Append('i');
        if (flags.Contains('m')) sb.Append('m');
        if (flags.Contains('s')) sb.Append('s');
        if (flags.Contains('u')) sb.Append('u');
        // y (sticky) — required for the SpeciesConstructor protocol in
        // RegExp.prototype[@@split] to work correctly. The matcher honors
        // sticky in `Exec` by using .NET's startat-aware Match overload.
        if (flags.Contains('y')) sb.Append('y');
        if (flags.Contains('d')) sb.Append('d');
        // v (Unicode-Sets, ES2024) is preserved through normalization so
        // `re.flags` round-trips and test262 generated CharacterClassEscapes
        // tests don't ParseError. Full v-mode character-class extensions
        // aren't yet implemented; runtime behavior under v matches plain
        // mode for now.
        if (flags.Contains('v')) sb.Append('v');
        return sb.ToString();
    }

    /// <summary>
    /// ECMA-262 (ES2025) modifier-group early errors. A modifier group
    /// <c>(?addFlags-removeFlags:…)</c> (or <c>(?addFlags:…)</c>) is a SyntaxError
    /// when any flag is not one of i/m/s, a flag repeats within addFlags or within
    /// removeFlags, a flag appears in both sets, or both sets are empty with a dash
    /// (<c>(?-:…)</c>). .NET's engine accepts several of these, so validate up
    /// front and throw a guest SyntaxError. Valid groups (and the non-modifier
    /// <c>(?:</c>, <c>(?=</c>, <c>(?!</c>, <c>(?&lt;…</c> forms) pass through.
    /// Kept in sync with the emitted <c>$RegExp.ValidateModifiers</c> (compiled).
    /// </summary>
    internal static void ValidateModifiers(string pattern)
    {
        int n = pattern.Length;
        int i = 0;
        while (i < n)
        {
            char c = pattern[i];
            if (c == '\\') { i += 2; continue; }          // skip escaped char
            if (c == '[') { i = SkipCharClass(pattern, i); continue; } // class chars are literal
            if (c == '(' && i + 1 < n && pattern[i + 1] == '?')
            {
                int j = i + 2;
                if (j >= n) { i++; continue; }
                char d = pattern[j];
                // Non-modifier (?…) constructs.
                if (d == ':' || d == '=' || d == '!' || d == '<') { i += 2; continue; }
                // Candidate modifier: scan flag chars up to ':' (else not a modifier).
                int k = j;
                while (k < n && pattern[k] != ':' && pattern[k] != ')') k++;
                if (k >= n || pattern[k] != ':') { i++; continue; }
                ValidateModifierFlags(pattern.Substring(j, k - j));
                i = k + 1;
                continue;
            }
            i++;
        }
    }

    /// <summary>Returns the index just past the matching <c>]</c> of a character
    /// class beginning at <paramref name="i"/> (<c>p[i] == '['</c>), honoring
    /// <c>\]</c>. Unterminated → end of string (.NET surfaces the error).</summary>
    private static int SkipCharClass(string p, int i)
    {
        int n = p.Length, k = i + 1;
        while (k < n)
        {
            if (p[k] == '\\') { k += 2; continue; }
            if (p[k] == ']') return k + 1;
            k++;
        }
        return n;
    }

    /// <summary>Validates the flag text between <c>(?</c> and <c>:</c> (e.g.
    /// "i", "ims", "i-m", "-i", "-", "i-i") in a single pass; throws SyntaxError
    /// on an early error. Single-pass (no substrings) so the emitted IL mirror
    /// stays simple.</summary>
    private static void ValidateModifierFlags(string mod)
    {
        int addMask = 0, removeMask = 0;
        bool sawDash = false;
        foreach (char f in mod)
        {
            if (f == '-')
            {
                if (sawDash) ThrowModifierSyntax();   // a second dash
                sawDash = true;
                continue;
            }
            int bit = f switch { 'i' => 1, 'm' => 2, 's' => 4, _ => 0 };
            if (bit == 0) ThrowModifierSyntax();      // not an i/m/s flag
            if (!sawDash)
            {
                if ((addMask & bit) != 0) ThrowModifierSyntax();    // duplicate in addFlags
                addMask |= bit;
            }
            else
            {
                if ((removeMask & bit) != 0) ThrowModifierSyntax(); // duplicate in removeFlags
                removeMask |= bit;
            }
        }
        if ((addMask & removeMask) != 0) ThrowModifierSyntax();     // flag in both sets
        if (sawDash && addMask == 0 && removeMask == 0) ThrowModifierSyntax(); // (?-:)
    }

    private static void ThrowModifierSyntax() =>
        throw new Exceptions.ThrowException(new SharpTSSyntaxError(
            "Invalid regular expression: invalid modifier group"));

    /// <summary>
    /// ECMA-262 Annex B Unicode-mode (`u`/`v`) early errors — the safe,
    /// false-positive-free subset: (1) a lookaround assertion immediately
    /// followed by a quantifier is a SyntaxError; (2) <c>\c</c> not followed by
    /// an ASCII letter (a ControlLetter) is a SyntaxError. Other u-mode
    /// restrictions (identity-escape allowlist, octal/backreference rules,
    /// character-class ranges, incomplete quantifiers) need lookahead that risks
    /// rejecting valid patterns, so they're deferred. Kept in sync with the
    /// emitted <c>$RegExp.ValidateUnicodePattern</c>.
    /// </summary>
    private static void ValidateUnicodePattern(string pattern)
    {
        int n = pattern.Length;
        for (int i = 0; i < n; i++)
        {
            char c = pattern[i];
            if (c == '\\')
            {
                // \c (anywhere) must be followed by an ASCII letter.
                if (i + 1 < n && pattern[i + 1] == 'c')
                {
                    char after = i + 2 < n ? pattern[i + 2] : '\0';
                    if (!IsAsciiLetter(after)) ThrowUnicodeSyntax();
                }
                i++;                    // skip the escaped char
                continue;
            }
            // Quantifying a lookaround assertion → SyntaxError.
            if (c == '(' && i + 2 < n && pattern[i + 1] == '?')
            {
                char d = pattern[i + 2];
                bool isAssertion = d == '=' || d == '!'
                    || (d == '<' && i + 3 < n && (pattern[i + 3] == '=' || pattern[i + 3] == '!'));
                if (isAssertion)
                {
                    int close = FindGroupClose(pattern, i);
                    if (close >= 0 && close + 1 < n && IsRegexQuantifierStart(pattern[close + 1]))
                        ThrowUnicodeSyntax();
                }
            }
        }
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsRegexQuantifierStart(char c) =>
        c == '*' || c == '+' || c == '?' || c == '{';

    /// <summary>Index of the <c>)</c> matching the group opening at
    /// <paramref name="openIdx"/> (honoring escapes and char classes), or -1.</summary>
    private static int FindGroupClose(string p, int openIdx)
    {
        int n = p.Length, depth = 0;
        bool inClass = false;
        for (int i = openIdx; i < n; i++)
        {
            char c = p[i];
            if (c == '\\') { i++; continue; }
            if (inClass) { if (c == ']') inClass = false; continue; }
            if (c == '[') inClass = true;
            else if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static void ThrowUnicodeSyntax() =>
        throw new Exceptions.ThrowException(new SharpTSSyntaxError(
            "Invalid regular expression: invalid Unicode-mode pattern"));

    /// <summary>
    /// Detects whether a regex pattern contains named capture groups (?&lt;name&gt;...).
    /// Excludes lookbehind assertions (?&lt;= and (?&lt;!.
    /// </summary>
    internal static bool HasNamedGroups(string pattern)
    {
        int i = 0;
        while (i < pattern.Length - 2)
        {
            if (pattern[i] == '\\')
            {
                i += 2; // skip escaped char
                continue;
            }
            if (pattern[i] == '(' && i + 2 < pattern.Length && pattern[i + 1] == '?' && pattern[i + 2] == '<')
            {
                // Check it's not lookbehind: (?<= or (?<!
                if (i + 3 < pattern.Length && (pattern[i + 3] == '=' || pattern[i + 3] == '!'))
                {
                    i += 4;
                    continue;
                }
                return true;
            }
            i++;
        }
        return false;
    }

    /// <summary>
    /// Builds a groups object from named capture groups in a match result.
    /// Returns null if there are no named groups.
    /// </summary>
    private SharpTSObject? BuildGroupsObject(Match match)
    {
        Dictionary<string, object?>? groups = null;
        foreach (Group group in match.Groups)
        {
            // Skip numeric group names (unnamed groups)
            if (int.TryParse(group.Name, out _))
                continue;

            groups ??= new Dictionary<string, object?>();
            groups[group.Name] = group.Success ? group.Value : null;
        }
        return groups != null ? new SharpTSObject(groups) : null;
    }

    /// <summary>
    /// Tests if the pattern matches the string.
    /// For global regexes, starts from lastIndex and updates it.
    /// </summary>
    /// <param name="input">The string to test against.</param>
    /// <returns>True if the pattern matches, false otherwise.</returns>
    public bool Test(string input)
    {
        bool sticky = Sticky;
        // Sticky AND global both honor lastIndex; sticky additionally requires
        // the match to start exactly at lastIndex (no forward scan).
        if (_global || sticky)
        {
            if (LastIndex > input.Length)
            {
                LastIndex = 0;
                return false;
            }

            var match = _regex.Match(input, Math.Min(LastIndex, input.Length));
            // Sticky requires match.Index == LastIndex; otherwise treat as no-match.
            if (sticky && match.Success && match.Index != LastIndex)
            {
                LastIndex = 0;
                return false;
            }
            if (match.Success)
            {
                LastIndex = match.Index + match.Length;
                return true;
            }
            else
            {
                // ECMA-262 §22.2.5.2.2 step 15.c.i: sticky-OR-global failed match
                // resets lastIndex to 0.
                LastIndex = 0;
                return false;
            }
        }

        return _regex.IsMatch(input);
    }

    /// <summary>
    /// Executes the regex on the string and returns the match result.
    /// For global regexes, maintains state via lastIndex.
    /// </summary>
    /// <param name="input">The string to match against.</param>
    /// <returns>
    /// An Array exotic (per ECMA-262 §22.2.5.6.6 RegExpBuiltinExec) carrying
    /// the matched substrings as elements 0..N and the metadata properties
    /// <c>index</c>, <c>input</c>, <c>groups</c> as named properties; or
    /// <c>null</c> when no match was found.
    /// </returns>
    public SharpTSArray? Exec(string input)
    {
        Match match;
        bool sticky = Sticky;
        bool useLastIndex = _global || sticky;

        if (useLastIndex)
        {
            if (LastIndex > input.Length)
            {
                LastIndex = 0;
                return null;
            }
            match = _regex.Match(input, Math.Min(LastIndex, input.Length));
            // Sticky requires the match to start exactly at LastIndex (no
            // forward scanning). If the engine found a match further along,
            // treat it as no-match and reset.
            if (sticky && match.Success && match.Index != LastIndex)
            {
                LastIndex = 0;
                return null;
            }
        }
        else
        {
            match = _regex.Match(input);
        }

        if (!match.Success)
        {
            if (useLastIndex) LastIndex = 0;
            return null;
        }

        if (useLastIndex)
        {
            LastIndex = match.Index + match.Length;
        }

        // ECMA-262 §22.2.5.6.6 step 27: build an Array exotic with element 0 =
        // matched substring, elements 1..N = capture groups, plus index/input/
        // groups as own named properties (CreateDataProperty steps 24-26, 33).
        var elements = new List<object?>(match.Groups.Count) { match.Value };
        for (int i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            elements.Add(group.Success ? group.Value : null);
        }

        var result = new SharpTSArray(elements);
        result.SetNamedProperty("index", (double)match.Index);
        result.SetNamedProperty("input", input);
        result.SetNamedProperty("groups", BuildGroupsObject(match));
        return result;
    }

    /// <summary>
    /// Returns the string representation of the regex: /pattern/flags
    /// </summary>
    public override string ToString() => $"/{_source}/{_flags}";

    /// <summary>
    /// Internal regex accessor for string methods.
    /// </summary>
    internal Regex InternalRegex => _regex;

    /// <summary>
    /// Match all occurrences in the string (used by String.match with global flag).
    /// </summary>
    internal List<object?> MatchAll(string input)
    {
        // String.prototype.match(/g) only needs the full-match substrings, not
        // capture groups — so use EnumerateMatches (ValueMatch structs, span-
        // based) instead of Matches(). This skips the per-match Match/Group
        // object allocation that dominates this path (~2.3x faster here), and
        // matches Matches(input)'s whole-string scan semantics exactly.
        //
        // Returns List<object?> (not List<string>) so callers hand it straight
        // to the SharpTSArray ctor with no element-by-element re-box into an
        // object list. The substrings are reference types, so storing them as
        // object? is free (no boxing). Mirrored by the emitted MatchAll
        // (RuntimeEmitter.TSRegExp.cs) — keep both in lockstep.
        List<object?> result = [];
        foreach (ValueMatch m in _regex.EnumerateMatches(input))
        {
            result.Add(input.Substring(m.Index, m.Length));
        }
        return result;
    }

    /// <summary>
    /// Match all occurrences returning detailed match objects (used by String.matchAll).
    /// Each object has "0" (full match), "1".."n" (groups), "index", "input", "groups".
    /// </summary>
    internal List<SharpTSObject> MatchAllObjects(string input)
    {
        var matches = _regex.Matches(input);
        List<SharpTSObject> result = [];
        foreach (Match m in matches)
        {
            var fields = new Dictionary<string, object?>
            {
                ["0"] = m.Value,
                ["index"] = (double)m.Index,
                ["input"] = input,
                ["groups"] = BuildGroupsObject(m)
            };
            for (int i = 1; i < m.Groups.Count; i++)
            {
                var group = m.Groups[i];
                fields[i.ToString()] = group.Success ? group.Value : null;
            }
            result.Add(new SharpTSObject(fields));
        }
        return result;
    }

    /// <summary>
    /// Replace occurrences in the string.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="replacement">The replacement string.</param>
    /// <returns>The string with replacements made.</returns>
    internal string Replace(string input, string replacement)
    {
        if (_global)
        {
            return _regex.Replace(input, replacement);
        }
        else
        {
            // Non-global: replace first match only
            return _regex.Replace(input, replacement, 1);
        }
    }

    /// <summary>
    /// Search for the first match in the string.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The index of the first match, or -1 if not found.</returns>
    internal int Search(string input)
    {
        var match = _regex.Match(input);
        return match.Success ? match.Index : -1;
    }

    /// <summary>
    /// Split the string by the regex pattern.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>An array of substrings.</returns>
    internal string[] Split(string input)
    {
        return _regex.Split(input);
    }
}
