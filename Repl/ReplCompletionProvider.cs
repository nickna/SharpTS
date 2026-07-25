using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Repl;

/// <summary>What kind of thing a completion candidate is. Drives ordering and colour.</summary>
internal enum ReplCompletionKind
{
    /// <summary>A member of the receiver's type.</summary>
    Member,

    /// <summary>A variable/function/class declared in this session.</summary>
    Binding,

    /// <summary>A built-in global (console, Math, …).</summary>
    Global,

    /// <summary>A TypeScript reserved word.</summary>
    Keyword,

    /// <summary>A REPL dot-command.</summary>
    DotCommand,

    /// <summary>A filesystem entry.</summary>
    FilePath,
}

/// <param name="Name">Text inserted when the candidate is committed.</param>
/// <param name="Kind">Category, used for ranking and colour.</param>
/// <param name="Detail">Optional tooltip text (a rendered type, or a command's help line).</param>
internal sealed record ReplCompletionCandidate(
    string Name,
    ReplCompletionKind Kind,
    string? Detail = null);

/// <summary>
/// The live REPL state that completion reads. Mutable, and handed to the callbacks by reference:
/// <c>ReplEngine</c> builds its <c>PromptCallbacks</c> once before the read loop but replaces the
/// interpreter and type checker on <c>.reset</c>, so the callbacks must never capture those
/// instances directly.
/// </summary>
internal sealed class ReplCompletionSession(
    Interpreter interpreter,
    TypeChecker typeChecker,
    DecoratorMode decoratorMode,
    List<Stmt> accumulatedStatements)
{
    public Interpreter Interpreter { get; private set; } = interpreter;
    public TypeChecker TypeChecker { get; private set; } = typeChecker;
    public DecoratorMode DecoratorMode { get; } = decoratorMode;
    public List<Stmt> AccumulatedStatements { get; } = accumulatedStatements;

    /// <summary>
    /// Bumped whenever the session's declarations change. Used as a cache key, so stale member
    /// lists are never served after a new line is submitted or the session is reset.
    /// </summary>
    public int Generation { get; private set; }

    public void OnStatementsAppended() => Generation++;

    public void Replace(Interpreter interpreter, TypeChecker typeChecker)
    {
        Interpreter = interpreter;
        TypeChecker = typeChecker;
        Generation++;
    }
}

/// <summary>
/// Computes REPL autocomplete candidates. Deliberately separate from the PrettyPrompt callbacks so
/// it can be unit-tested without a console: <c>ReplEngine.RunAsync</c> blocks on a real prompt and
/// <c>PromptCallbacks</c> members are protected.
/// </summary>
/// <remarks>
/// Member completion is resolved statically, through the <see cref="TypeChecker"/>, generalizing
/// what the <c>.type</c> dot-command already does. That is what lets an arbitrary receiver
/// expression — <c>getUser().</c>, <c>arr[0].</c> — complete, and it supplies each member's type for
/// the tooltip. The one place static resolution has no answer is the built-in singletons, which the
/// checker models as <c>any</c>; see <see cref="SingletonMembers"/>.
/// </remarks>
internal sealed class ReplCompletionProvider(ReplCompletionSession session)
{
    /// <summary>
    /// Member names for the global singletons the type checker types as <c>any</c>
    /// (<c>console</c>, <c>Math</c>, <c>JSON</c>, …). Without this, the most frequently typed
    /// expressions in a REPL would offer nothing at all. Read straight from the built-in method
    /// tables so the lists cannot drift from what actually dispatches at runtime.
    /// </summary>
    private static readonly Dictionary<string, Func<IEnumerable<string>>> SingletonMembers =
        new(StringComparer.Ordinal)
        {
            ["console"] = () => ConsoleBuiltIns.MemberNames,
            ["Math"] = () => MathBuiltIns.MemberNames,
            ["JSON"] = () => JSONBuiltIns.MemberNames,
            ["Object"] = () => ObjectBuiltIns.StaticMemberNames,
            ["Number"] = () => NumberBuiltIns.StaticMemberNames,
            ["String"] = () => StringBuiltIns.StaticMemberNames,
        };

    // Member lists for the current session generation only. Resolving a member means type-checking
    // the whole accumulated session, which is far too expensive to redo for every keystroke, but the
    // result is only valid until the session's declarations change — so the whole cache is dropped
    // when the generation moves rather than accumulating one entry per generation forever.
    private readonly Dictionary<string, List<ReplCompletionCandidate>> _memberCache = [];
    private int _cachedGeneration = -1;

    /// <summary>
    /// The candidates for the caret position. Never throws; returns an empty list when nothing
    /// applies, so a completion failure can never take down the prompt.
    /// </summary>
    public IReadOnlyList<ReplCompletionCandidate> GetCandidates(string text, int caret)
    {
        try
        {
            var context = ReplCompletionContext.Classify(text, caret);
            return context.Kind switch
            {
                ReplCompletionContextKind.Member => GetMemberCandidates(context.Receiver!),
                ReplCompletionContextKind.Identifier => GetIdentifierCandidates(),
                ReplCompletionContextKind.DotCommand => GetDotCommandCandidates(),
                ReplCompletionContextKind.DotCommandArgument => GetPathCandidates(context.Partial),
                _ => [],
            };
        }
        catch
        {
            return [];
        }
    }

    // ===================== Members =====================

    private List<ReplCompletionCandidate> GetMemberCandidates(string receiver)
    {
        if (_cachedGeneration != session.Generation)
        {
            _memberCache.Clear();
            _cachedGeneration = session.Generation;
        }
        else if (_memberCache.TryGetValue(receiver, out var cached))
        {
            return cached;
        }

        var candidates = ResolveMembers(receiver);
        _memberCache[receiver] = candidates;
        return candidates;
    }

    private List<ReplCompletionCandidate> ResolveMembers(string receiver)
    {
        List<ReplCompletionCandidate> candidates = [];

        var receiverType = ResolveReceiverType(receiver);

        // The checker models the built-in singletons as `any`, so an `any` (or unresolvable)
        // receiver falls back to the runtime member tables when it names one of them.
        if (receiverType is null or TypeInfo.Any)
            return SingletonFallback(receiver);

        foreach (var (name, memberType) in session.TypeChecker.GetCompletionMembers(receiverType))
        {
            if (!IsCompletableName(name)) continue;
            candidates.Add(new ReplCompletionCandidate(
                name,
                ReplCompletionKind.Member,
                memberType is null ? null : Tooltip($"{name}: {memberType}")));
        }

        // A resolvable type with no members is still worth a singleton check: `Object` resolves to a
        // real type in some positions but carries its statics only in the runtime table.
        return candidates.Count == 0 ? SingletonFallback(receiver) : candidates;
    }

    private static List<ReplCompletionCandidate> SingletonFallback(string receiver)
    {
        if (!SingletonMembers.TryGetValue(receiver, out var names)) return [];
        return names()
            .Where(IsCompletableName)
            .Select(n => new ReplCompletionCandidate(n, ReplCompletionKind.Member))
            .ToList();
    }

    /// <summary>
    /// Type-checks the receiver expression in the context of everything already submitted this
    /// session, and returns its type. Diagnostics are discarded — accumulated statements may
    /// legitimately contain type errors, and completion must stay silent either way.
    /// </summary>
    private TypeInfo? ResolveReceiverType(string receiver)
    {
        var parsed = TryParseExpression(receiver);
        if (parsed is null) return null;

        // The receiver is an expression statement, so it declares nothing and cannot pollute the
        // checker's persistent environment.
        var combined = new List<Stmt>(session.AccumulatedStatements);
        combined.AddRange(parsed);

        var lastExpression = parsed.OfType<Stmt.Expression>().LastOrDefault();
        if (lastExpression is null) return null;

        try
        {
            var result = session.TypeChecker.CheckWithRecovery(combined);
            return result.TypeMap.TryGet(lastExpression.Expr, out var type) ? type : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a receiver expression, retrying with a trailing semicolon — the same convenience the
    /// REPL applies to submitted input.
    /// </summary>
    private List<Stmt>? TryParseExpression(string source)
    {
        foreach (var candidate in new[] { source, source + ";" })
        {
            try
            {
                var tokens = new Lexer(candidate).ScanTokens();
                var result = new Parser(tokens, session.DecoratorMode).Parse();
                if (result.IsSuccess) return result.Statements;
            }
            catch
            {
                // Try the next form.
            }
        }

        return null;
    }

    // ===================== Identifiers, globals, keywords =====================

    /// <summary>
    /// Bindings, globals, and keywords. Deliberately does no type checking: the completion window
    /// opens as the user types, so this path has to stay cheap.
    /// </summary>
    private List<ReplCompletionCandidate> GetIdentifierCandidates()
    {
        List<ReplCompletionCandidate> candidates = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var interpreter = session.Interpreter;
        if (!interpreter.IsDisposed)
        {
            // Innermost scope first, so an inner binding shadows an outer one of the same name.
            for (var env = interpreter.Environment; env is not null; env = env.Enclosing)
            {
                // Snapshot: a timer callback drained at the next tick could otherwise mutate the
                // dictionary while PrettyPrompt is still rendering.
                foreach (var name in env.Names.ToList())
                    if (IsCompletableName(name) && seen.Add(name))
                        candidates.Add(new ReplCompletionCandidate(name, ReplCompletionKind.Binding));
            }
        }

        // Two sources: the realm's constant table, plus the registered built-in namespaces. The
        // latter is not redundant — `console` has no singleton instance, so it never lands in the
        // realm table even though it is very much a global.
        foreach (var name in Interpreter.GlobalNames.Concat(BuiltInRegistry.Instance.NamespaceNames))
            if (IsCompletableName(name) && seen.Add(name))
                candidates.Add(new ReplCompletionCandidate(name, ReplCompletionKind.Global));

        foreach (var name in Lexer.KeywordNames)
            if (seen.Add(name))
                candidates.Add(new ReplCompletionCandidate(name, ReplCompletionKind.Keyword));

        return candidates;
    }

    // ===================== Dot-commands and paths =====================

    private static List<ReplCompletionCandidate> GetDotCommandCandidates() =>
        DotCommands.Commands
            .Select(c => new ReplCompletionCandidate(c.Name, ReplCompletionKind.DotCommand, c.Help))
            .ToList();

    /// <summary>
    /// Filesystem entries for <c>.load</c> / <c>.save</c>. Enumerated relative to the process
    /// working directory, which is what those commands resolve against.
    /// </summary>
    private static List<ReplCompletionCandidate> GetPathCandidates(string partial)
    {
        const int limit = 200;

        var separator = partial.LastIndexOfAny(['/', '\\']);
        var directoryPart = separator < 0 ? "" : partial[..(separator + 1)];
        var searchDirectory = directoryPart.Length == 0 ? "." : directoryPart;

        List<ReplCompletionCandidate> candidates = [];
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(searchDirectory))
            {
                var name = Path.GetFileName(entry);
                if (name.Length == 0) continue;

                var isDirectory = Directory.Exists(entry);
                candidates.Add(new ReplCompletionCandidate(
                    directoryPart + name + (isDirectory ? "/" : ""),
                    ReplCompletionKind.FilePath));

                if (candidates.Count >= limit) break;
            }
        }
        catch
        {
            // Missing directory, permission denied, long path — offer nothing.
        }

        return candidates;
    }

    // ===================== Helpers =====================

    /// <summary>
    /// Flattens a rendered type onto one capped line. A recursive or deeply generic type can render
    /// to something enormous, and it goes straight into the description pane beside the menu.
    /// </summary>
    private static string Tooltip(string text)
    {
        const int limit = 160;

        var flattened = text.ReplaceLineEndings(" ");
        return flattened.Length <= limit ? flattened : flattened[..(limit - 1)] + "…";
    }

    /// <summary>
    /// True when a name can actually be typed as an identifier or after a dot. Filters synthetic
    /// runtime slots (mangled or symbol-keyed names) that would be useless or invalid to insert.
    /// </summary>
    private static bool IsCompletableName(string name)
    {
        if (name.Length == 0) return false;
        if (!char.IsLetter(name[0]) && name[0] is not ('_' or '$')) return false;

        foreach (var c in name)
            if (!char.IsLetterOrDigit(c) && c is not ('_' or '$'))
                return false;

        return true;
    }
}
