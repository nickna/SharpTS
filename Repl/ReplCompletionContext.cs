using SharpTS.Parsing;

namespace SharpTS.Repl;

/// <summary>What the caret is positioned to complete.</summary>
internal enum ReplCompletionContextKind
{
    /// <summary>Completion is suppressed here (inside a literal or comment, or unlexable input).</summary>
    None,

    /// <summary>A bare identifier prefix — offer bindings, globals, and keywords.</summary>
    Identifier,

    /// <summary><c>receiver.partial</c> — offer the receiver type's members.</summary>
    Member,

    /// <summary>A dot-command name at the start of the buffer, e.g. <c>.he</c>.</summary>
    DotCommand,

    /// <summary>The path argument of <c>.load</c> / <c>.save</c>.</summary>
    DotCommandArgument,
}

/// <summary>
/// Where the caret is and what should be offered there. Pure and console-free so the
/// tokenizer edge cases can be unit-tested without an interpreter.
/// </summary>
/// <param name="Kind">The kind of completion to offer.</param>
/// <param name="Partial">The word already typed; empty immediately after a dot.</param>
/// <param name="ReplaceStart">Absolute offset where <paramref name="Partial"/> begins.</param>
/// <param name="Receiver">
/// For <see cref="ReplCompletionContextKind.Member"/>, the receiver's source text
/// (may be an arbitrary expression such as <c>getUser()</c> or <c>arr[0]</c>). Null otherwise.
/// </param>
internal sealed record ReplCompletionContext(
    ReplCompletionContextKind Kind,
    string Partial,
    int ReplaceStart,
    string? Receiver)
{
    public static readonly ReplCompletionContext None =
        new(ReplCompletionContextKind.None, "", 0, null);

    /// <summary>
    /// Classifies the caret position in <paramref name="text"/>.
    /// </summary>
    /// <remarks>
    /// Everything after the caret is irrelevant, so this works on <c>text[..caret]</c>.
    ///
    /// Suppression relies on two lexer behaviours rather than a hand-rolled string/comment scanner:
    /// an unterminated string, template, or comment produces <em>no token at all</em> (comments are
    /// skipped as trivia), and <see cref="Token.Lexeme"/> is the raw source slice, so
    /// <c>Start + Lexeme.Length</c> is an exact end offset. Together they mean a "coverage gap" —
    /// non-whitespace source after the last token — is a reliable signal that the caret sits inside
    /// something the lexer refused to tokenize. That single rule covers unterminated <c>"</c>,
    /// <c>'</c>, <c>`</c>, <c>//</c>, and <c>/* */</c>, while still allowing completion after a
    /// *closed* comment and inside a template interpolation.
    /// </remarks>
    public static ReplCompletionContext Classify(string text, int caret)
    {
        // This runs on every keystroke — PrettyPrompt's completion pane calls
        // GetSpanToReplaceByCompletionAsync for each key press while the menu is open, and
        // ShouldOpenCompletionWindowAsync on key-up. An exception escaping here would propagate out
        // of the pane's key handler and wedge the prompt, so classification failure must degrade to
        // "offer nothing" rather than throw.
        try
        {
            return ClassifyCore(text, caret);
        }
        catch
        {
            return None;
        }
    }

    private static ReplCompletionContext ClassifyCore(string text, int caret)
    {
        if (caret < 0 || caret > text.Length) return None;
        var prefix = text[..caret];

        // Dot-commands are not TypeScript, so handle them before lexing: `.he` would otherwise look
        // like a member access on an empty receiver. Mirrors DotCommands.IsDotCommand.
        if (!prefix.Contains('\n') && prefix.TrimStart().StartsWith('.'))
            return ClassifyDotCommand(prefix);

        List<Token> tokens;
        try
        {
            tokens = new Lexer(prefix).ScanTokens();
        }
        catch
        {
            // Malformed numerics, stray `#`, etc. Correct failure mode is to offer nothing.
            return None;
        }

        // Drop EOF and any synthetic tokens without a real source offset.
        var real = tokens.Where(t => t.Type != TokenType.EOF && t.Start >= 0).ToList();

        // Coverage-gap guard: anything non-whitespace the lexer did not tokenize means the caret is
        // inside a literal or comment.
        var covered = real.Count == 0 ? 0 : real[^1].Start + real[^1].Lexeme.Length;
        for (int i = covered; i < prefix.Length; i++)
            if (!char.IsWhiteSpace(prefix[i]))
                return None;

        if (real.Count == 0)
            return new ReplCompletionContext(ReplCompletionContextKind.Identifier, "", caret, null);

        var last = real[^1];
        var lastEndsAtCaret = last.Start + last.Lexeme.Length == caret;

        // Completing directly after a literal is meaningless. Note `1.` arrives here as a single
        // NUMBER token whose lexeme is "1." (the lexer absorbs the trailing dot), so this is also
        // what stops `1.` from offering number members — there is no DOT token to react to.
        if (lastEndsAtCaret && last.Type is TokenType.NUMBER or TokenType.BIGINT_LITERAL
            or TokenType.STRING or TokenType.REGEX or TokenType.TEMPLATE_HEAD
            or TokenType.TEMPLATE_MIDDLE or TokenType.TEMPLATE_TAIL or TokenType.TEMPLATE_FULL)
            return None;

        // `receiver.` — empty partial.
        if (lastEndsAtCaret && IsDotToken(last.Type))
            return BuildMember(text, real, dotIndex: real.Count - 1, partial: "", replaceStart: caret);

        // `receiver.par` — partial after a dot.
        if (lastEndsAtCaret && IsNameLike(last) && real.Count >= 2 && IsDotToken(real[^2].Type))
            return BuildMember(text, real, dotIndex: real.Count - 2, last.Lexeme, last.Start);

        // A bare identifier prefix.
        if (lastEndsAtCaret && IsNameLike(last))
            return new ReplCompletionContext(
                ReplCompletionContextKind.Identifier, last.Lexeme, last.Start, null);

        // Caret sits after whitespace or punctuation — offer the unfiltered identifier list.
        return new ReplCompletionContext(ReplCompletionContextKind.Identifier, "", caret, null);
    }

    private static ReplCompletionContext ClassifyDotCommand(string prefix)
    {
        var dotIndex = prefix.Length - prefix.TrimStart().Length;
        var rest = prefix[dotIndex..];

        var spaceIndex = rest.IndexOf(' ');
        if (spaceIndex < 0)
            return new ReplCompletionContext(
                ReplCompletionContextKind.DotCommand, rest, dotIndex, null);

        // `.load ./src/fo` — the argument is a filesystem path for the file commands only.
        var command = rest[..spaceIndex].ToLowerInvariant();
        if (command is not (".load" or ".save"))
            return None;

        var argStart = dotIndex + spaceIndex + 1;
        while (argStart < prefix.Length && prefix[argStart] == ' ') argStart++;
        return new ReplCompletionContext(
            ReplCompletionContextKind.DotCommandArgument, prefix[argStart..], argStart, null);
    }

    private static bool IsDotToken(TokenType type)
        => type is TokenType.DOT or TokenType.QUESTION_DOT;

    /// <summary>
    /// True for tokens usable as a property name. Keywords count: the lexer claims <c>type</c>,
    /// <c>get</c>, <c>default</c>, <c>of</c> and ~74 others, but they are all legal after a dot,
    /// so <c>obj.type</c> must still complete.
    /// </summary>
    private static bool IsNameLike(Token token)
        => token.Type == TokenType.IDENTIFIER || Lexer.KeywordTokenType(token.Lexeme) is not null;

    /// <summary>
    /// Builds a Member context by walking left from the dot to find the start of the receiver
    /// expression, balancing bracket pairs so calls and indexers are included.
    /// </summary>
    private static ReplCompletionContext BuildMember(
        string text, List<Token> tokens, int dotIndex, string partial, int replaceStart)
    {
        var receiverEnd = tokens[dotIndex].Start;
        int start = dotIndex;
        int depth = 0;
        int i = dotIndex - 1;

        while (i >= 0)
        {
            var type = tokens[i].Type;

            if (type is TokenType.RIGHT_PAREN or TokenType.RIGHT_BRACKET or TokenType.RIGHT_BRACE)
            {
                depth++;
                start = i--;
                continue;
            }

            if (type is TokenType.LEFT_PAREN or TokenType.LEFT_BRACKET or TokenType.LEFT_BRACE)
            {
                // An unmatched opener belongs to an enclosing construct, so the receiver starts here.
                if (depth == 0) break;
                depth--;
                start = i--;
                continue;
            }

            // Inside a balanced group everything is part of the receiver.
            if (depth > 0)
            {
                start = i--;
                continue;
            }

            if (IsDotToken(type))
            {
                start = i--;
                continue;
            }

            if (IsNameLike(tokens[i]) || type is TokenType.THIS or TokenType.SUPER)
            {
                start = i--;

                // Keep walking only while the chain continues through a dot.
                if (i >= 0 && IsDotToken(tokens[i].Type)) continue;

                // `new C().foo` — the constructor call is part of the receiver.
                if (i >= 0 && tokens[i].Type == TokenType.NEW) start = i;
                break;
            }

            break;
        }

        var receiverStart = tokens[start].Start;
        if (receiverStart >= receiverEnd) return None;

        var receiver = text[receiverStart..receiverEnd].Trim();
        return receiver.Length == 0
            ? None
            : new ReplCompletionContext(
                ReplCompletionContextKind.Member, partial, replaceStart, receiver);
    }
}
