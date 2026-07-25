using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Consoles;
using PrettyPrompt.Documents;
using PrettyPrompt.Highlighting;
using SharpTS.Parsing;

namespace SharpTS.Repl;

/// <summary>
/// PrettyPrompt callbacks for the SharpTS REPL.
/// Handles multi-line detection via TransformKeyPressAsync, syntax highlighting, and autocomplete.
/// </summary>
internal sealed class ReplCallbacks(ReplCompletionProvider completions) : PromptCallbacks
{
    // Token type to color mapping for syntax highlighting
    private static readonly Dictionary<TokenType, AnsiColor> TokenColors = new()
    {
        // Keywords (blue)
        { TokenType.CONST, AnsiColor.Blue },
        { TokenType.LET, AnsiColor.Blue },
        { TokenType.VAR, AnsiColor.Blue },
        { TokenType.IF, AnsiColor.Blue },
        { TokenType.ELSE, AnsiColor.Blue },
        { TokenType.FOR, AnsiColor.Blue },
        { TokenType.WHILE, AnsiColor.Blue },
        { TokenType.DO, AnsiColor.Blue },
        { TokenType.FUNCTION, AnsiColor.Blue },
        { TokenType.CLASS, AnsiColor.Blue },
        { TokenType.RETURN, AnsiColor.Blue },
        { TokenType.ASYNC, AnsiColor.Blue },
        { TokenType.AWAIT, AnsiColor.Blue },
        { TokenType.NEW, AnsiColor.Blue },
        { TokenType.TYPEOF, AnsiColor.Blue },
        { TokenType.INSTANCEOF, AnsiColor.Blue },
        { TokenType.IMPORT, AnsiColor.Blue },
        { TokenType.EXPORT, AnsiColor.Blue },
        { TokenType.FROM, AnsiColor.Blue },
        { TokenType.SWITCH, AnsiColor.Blue },
        { TokenType.CASE, AnsiColor.Blue },
        { TokenType.DEFAULT, AnsiColor.Blue },
        { TokenType.BREAK, AnsiColor.Blue },
        { TokenType.CONTINUE, AnsiColor.Blue },
        { TokenType.TRY, AnsiColor.Blue },
        { TokenType.CATCH, AnsiColor.Blue },
        { TokenType.FINALLY, AnsiColor.Blue },
        { TokenType.THROW, AnsiColor.Blue },
        { TokenType.YIELD, AnsiColor.Blue },
        { TokenType.EXTENDS, AnsiColor.Blue },
        { TokenType.SUPER, AnsiColor.Blue },
        { TokenType.THIS, AnsiColor.Blue },
        { TokenType.STATIC, AnsiColor.Blue },
        { TokenType.GET, AnsiColor.Blue },
        { TokenType.SET, AnsiColor.Blue },
        { TokenType.OF, AnsiColor.Blue },
        { TokenType.IN, AnsiColor.Blue },
        { TokenType.VOID, AnsiColor.Blue },
        { TokenType.DELETE, AnsiColor.Blue },
        { TokenType.USING, AnsiColor.Blue },
        { TokenType.DECLARE, AnsiColor.Blue },
        { TokenType.ABSTRACT, AnsiColor.Blue },
        { TokenType.OVERRIDE, AnsiColor.Blue },
        { TokenType.PRIVATE, AnsiColor.Blue },
        { TokenType.PROTECTED, AnsiColor.Blue },
        { TokenType.PUBLIC, AnsiColor.Blue },
        { TokenType.READONLY, AnsiColor.Blue },
        { TokenType.IMPLEMENTS, AnsiColor.Blue },
        { TokenType.NAMESPACE, AnsiColor.Blue },
        { TokenType.MODULE, AnsiColor.Blue },
        { TokenType.ACCESSOR, AnsiColor.Blue },
        { TokenType.SATISFIES, AnsiColor.Blue },

        // Type keywords (cyan)
        { TokenType.TYPE_STRING, AnsiColor.Cyan },
        { TokenType.TYPE_NUMBER, AnsiColor.Cyan },
        { TokenType.TYPE_BOOLEAN, AnsiColor.Cyan },
        { TokenType.TYPE_SYMBOL, AnsiColor.Cyan },
        { TokenType.TYPE_BIGINT, AnsiColor.Cyan },
        { TokenType.INTERFACE, AnsiColor.Cyan },
        { TokenType.TYPE, AnsiColor.Cyan },
        { TokenType.ENUM, AnsiColor.Cyan },
        { TokenType.AS, AnsiColor.Cyan },
        { TokenType.IS, AnsiColor.Cyan },
        { TokenType.KEYOF, AnsiColor.Cyan },
        { TokenType.INFER, AnsiColor.Cyan },
        { TokenType.NEVER, AnsiColor.Cyan },
        { TokenType.UNKNOWN, AnsiColor.Cyan },
        { TokenType.UNIQUE, AnsiColor.Cyan },

        // String literals (green)
        { TokenType.STRING, AnsiColor.Green },
        { TokenType.TEMPLATE_HEAD, AnsiColor.Green },
        { TokenType.TEMPLATE_MIDDLE, AnsiColor.Green },
        { TokenType.TEMPLATE_TAIL, AnsiColor.Green },
        { TokenType.TEMPLATE_FULL, AnsiColor.Green },

        // Numeric literals (yellow)
        { TokenType.NUMBER, AnsiColor.Yellow },
        { TokenType.BIGINT_LITERAL, AnsiColor.Yellow },

        // Boolean/null (magenta)
        { TokenType.TRUE, AnsiColor.Magenta },
        { TokenType.FALSE, AnsiColor.Magenta },
        { TokenType.NULL, AnsiColor.Magenta },
        { TokenType.UNDEFINED, AnsiColor.Magenta },

        // Regex (red)
        { TokenType.REGEX, AnsiColor.Red },
    };

    /// <summary>
    /// Transforms Enter into Shift+Enter (newline) when the input is incomplete.
    /// This gives multi-line editing behavior: Enter on incomplete code continues editing,
    /// Enter on complete code submits.
    /// </summary>
    protected override Task<KeyPress> TransformKeyPressAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        // Only intercept bare Enter (no modifiers)
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Enter
            && keyPress.ConsoleKeyInfo.Modifiers == 0
            && !IsInputComplete(text))
        {
            // Transform into Shift+Enter (newline instead of submit)
            return Task.FromResult(new KeyPress(
                new ConsoleKeyInfo('\n', ConsoleKey.Enter, shift: true, alt: false, control: false)));
        }

        return Task.FromResult(keyPress);
    }

    /// <summary>
    /// Supplies autocomplete candidates for the caret position.
    /// </summary>
    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        IReadOnlyList<CompletionItem> items;
        try
        {
            items = completions
                .GetCandidates(text, caret)
                .Select(candidate => (CompletionItem)new ReplCompletionItem(candidate))
                .ToList();
        }
        catch
        {
            // A completion failure must never take down the prompt.
            items = [];
        }

        return Task.FromResult(items);
    }

    /// <summary>
    /// Opens the completion window as the user types, and after a dot.
    /// </summary>
    /// <remarks>
    /// PrettyPrompt's default only opens after <c>.</c> or <c>(</c>, at the first character of the
    /// prompt, or after a space — and it opens even inside string literals. This override gives
    /// IntelliSense-style behaviour instead: suggest while a word is being typed, but stay silent
    /// wherever <see cref="ReplCompletionContext.Classify"/> says completion does not apply (inside
    /// strings, templates, comments, and after a numeric literal such as <c>1.</c>).
    /// </remarks>
    protected override Task<bool> ShouldOpenCompletionWindowAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        => Task.FromResult(ShouldOpenCompletionWindow(text, caret));

    /// <summary>The trigger policy, separated from the callback so it can be tested directly.</summary>
    internal static bool ShouldOpenCompletionWindow(string text, int caret)
    {
        var context = ReplCompletionContext.Classify(text, caret);

        return context.Kind switch
        {
            // Only once a word is actually started, so the menu does not appear on a bare prompt or
            // after every space.
            ReplCompletionContextKind.Identifier => context.Partial.Length > 0,
            ReplCompletionContextKind.None => false,
            _ => true,
        };
    }

    /// <summary>
    /// Determines the span a committed completion replaces.
    /// </summary>
    /// <remarks>
    /// The default word detection uses <c>IsLetterOrDigit(c) || c == '_'</c>, which excludes <c>$</c>
    /// even though it is a legal identifier character — committing <c>$foo</c> over a caret in
    /// <c>$fo</c> would produce <c>$$foo</c>. It also stops at the <c>.</c> of a dot-command and at
    /// the separators in a path argument. Deriving the span from the same classification that
    /// produced the candidates keeps the two consistent.
    /// </remarks>
    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text, int caret, CancellationToken cancellationToken)
    {
        var context = ReplCompletionContext.Classify(text, caret);

        return context.Kind == ReplCompletionContextKind.None
            ? base.GetSpanToReplaceByCompletionAsync(text, caret, cancellationToken)
            : Task.FromResult(TextSpan.FromBounds(context.ReplaceStart, caret));
    }

    /// <summary>
    /// Lets Enter submit a finished line instead of committing the highlighted suggestion.
    /// </summary>
    /// <remarks>
    /// Both Enter and Tab commit completions by default, and the menu is open most of the time under
    /// IntelliSense-style triggering — so Enter would rarely reach the prompt. Returning false here
    /// leaves the key unhandled, and it falls through to the code pane, which submits.
    ///
    /// The <c>IsInputComplete</c> guard is load-bearing, not cosmetic. PrettyPrompt skips
    /// <see cref="TransformKeyPressAsync"/> whenever a key would commit a completion, so rejecting
    /// Enter unconditionally would also bypass the Enter-to-newline transform below and break
    /// multi-line editing. Since that transform only fires for *incomplete* input, restricting the
    /// rejection to complete input means the bypass can only ever happen where the transform would
    /// have done nothing anyway.
    /// </remarks>
    protected override Task<bool> ConfirmCompletionCommit(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
        => Task.FromResult(ShouldCommitCompletion(keyPress.ConsoleKeyInfo.Key, text));

    /// <summary>The commit policy, separated from the callback so it can be tested directly.</summary>
    internal static bool ShouldCommitCompletion(ConsoleKey key, string text)
        => key != ConsoleKey.Enter || !IsInputComplete(text);

    /// <summary>
    /// Provides real-time syntax highlighting by tokenizing the current input.
    /// </summary>
    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(
        string text, CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();

        // Pre-scan for comments (the Lexer skips them, so we detect them first)
        AddCommentHighlights(text, spans);

        // Tokenize and highlight
        try
        {
            var lexer = new Lexer(text);
            var tokens = lexer.ScanTokens();

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.EOF) break;
                if (token.Start < 0) continue;

                if (TokenColors.TryGetValue(token.Type, out var color))
                {
                    spans.Add(new FormatSpan(token.Start, token.Lexeme.Length, color));
                }
            }
        }
        catch
        {
            // Lexer threw on malformed input — return whatever highlights we have
        }

        return Task.FromResult<IReadOnlyCollection<FormatSpan>>(spans);
    }

    /// <summary>
    /// Determines if the input appears complete using the Lexer for accurate tokenization.
    /// The Lexer correctly handles regex literals, template expressions, strings, and comments
    /// that would fool a hand-rolled character scanner.
    /// </summary>
    private static bool IsInputComplete(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        var trimmed = text.TrimEnd();

        // Trailing operators suggest continuation
        if (trimmed.EndsWith('+') || trimmed.EndsWith('-') ||
            trimmed.EndsWith('*') || trimmed.EndsWith('/') ||
            trimmed.EndsWith('=') || trimmed.EndsWith(',') ||
            trimmed.EndsWith("&&") || trimmed.EndsWith("||") ||
            trimmed.EndsWith("=>") || trimmed.EndsWith('?') ||
            trimmed.EndsWith("??") || trimmed.EndsWith(':'))
        {
            return false;
        }

        // Use the Lexer to tokenize — it correctly handles strings, regex, templates, comments
        try
        {
            var lexer = new Lexer(text);
            var tokens = lexer.ScanTokens();

            int braces = 0, parens = 0, brackets = 0;
            bool hasTemplateHead = false;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.EOF) break;

                switch (token.Type)
                {
                    case TokenType.LEFT_BRACE: braces++; break;
                    case TokenType.RIGHT_BRACE: braces--; break;
                    case TokenType.LEFT_PAREN: parens++; break;
                    case TokenType.RIGHT_PAREN: parens--; break;
                    case TokenType.LEFT_BRACKET: brackets++; break;
                    case TokenType.RIGHT_BRACKET: brackets--; break;
                    // Template head without a matching tail means we're mid-template
                    case TokenType.TEMPLATE_HEAD: hasTemplateHead = true; break;
                    case TokenType.TEMPLATE_TAIL: hasTemplateHead = false; break;
                }
            }

            return braces <= 0 && parens <= 0 && brackets <= 0 && !hasTemplateHead;
        }
        catch
        {
            // Lexer threw on malformed input — treat as incomplete so user can continue editing
            return false;
        }
    }

    /// <summary>
    /// A completion item that ranks by category and colours itself to match the live highlighting.
    /// </summary>
    /// <remarks>
    /// Nested so it can reuse <see cref="TokenColors"/>: a <c>const</c> suggestion is then the exact
    /// same blue it turns once committed.
    /// </remarks>
    private sealed class ReplCompletionItem : CompletionItem
    {
        private readonly int _categoryRank;

        public ReplCompletionItem(ReplCompletionCandidate candidate)
            : base(
                replacementText: candidate.Name,
                displayText: Display(candidate),
                filterText: candidate.Name,
                getExtendedDescription: candidate.Detail is null
                    ? null
                    : _ => Task.FromResult(new FormattedString(candidate.Detail)))
            => _categoryRank = RankOf(candidate.Kind);

        /// <summary>
        /// Ranks by how well the item matches what has been typed first, then by category, so that
        /// a session binding beats a global which beats a keyword among equally good matches.
        /// </summary>
        public override int GetCompletionItemPriority(string text, int caret, TextSpan spanToBeReplaced)
        {
            var basePriority = base.GetCompletionItemPriority(text, caret, spanToBeReplaced);

            // The base uses `int.MinValue + priority` to mark non-matching items; scaling that would
            // overflow, so leave negatives alone.
            return basePriority < 0 ? basePriority : basePriority * 10 + _categoryRank;
        }

        private static int RankOf(ReplCompletionKind kind) => kind switch
        {
            ReplCompletionKind.Member => 5,
            ReplCompletionKind.Binding => 4,
            ReplCompletionKind.DotCommand => 3,
            ReplCompletionKind.FilePath => 3,
            ReplCompletionKind.Global => 2,
            _ => 1, // Keyword — real bindings should always win.
        };

        private static FormattedString Display(ReplCompletionCandidate candidate)
        {
            var color = ColorOf(candidate);
            return color is null
                ? new FormattedString(candidate.Name)
                : new FormattedString(
                    candidate.Name, new FormatSpan(0, candidate.Name.Length, color.Value));
        }

        private static AnsiColor? ColorOf(ReplCompletionCandidate candidate) => candidate.Kind switch
        {
            // Reuse the highlighter's own mapping so keyword colouring cannot drift from it.
            ReplCompletionKind.Keyword =>
                Lexer.KeywordTokenType(candidate.Name) is { } tokenType
                && TokenColors.TryGetValue(tokenType, out var keywordColor)
                    ? keywordColor
                    : AnsiColor.Blue,
            ReplCompletionKind.Global => AnsiColor.Cyan,
            ReplCompletionKind.DotCommand => AnsiColor.Magenta,
            ReplCompletionKind.FilePath => AnsiColor.Green,
            _ => null, // Members and bindings stay default, as identifiers are in the buffer.
        };
    }

    /// <summary>
    /// Pre-scans for comments and adds gray highlighting spans.
    /// The Lexer skips comments, so we detect them separately.
    /// </summary>
    private static void AddCommentHighlights(string text, List<FormatSpan> spans)
    {
        var commentColor = AnsiColor.BrightBlack;
        bool inSingleQuote = false, inDoubleQuote = false, inTemplate = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Handle escape sequences in strings
            if ((inSingleQuote || inDoubleQuote || inTemplate) && c == '\\' && i + 1 < text.Length)
            {
                i++;
                continue;
            }

            if (c == '\'' && !inDoubleQuote && !inTemplate) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote && !inTemplate) { inDoubleQuote = !inDoubleQuote; continue; }
            if (c == '`' && !inSingleQuote && !inDoubleQuote) { inTemplate = !inTemplate; continue; }

            if (inSingleQuote || inDoubleQuote || inTemplate) continue;

            // Line comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int start = i;
                while (i < text.Length && text[i] != '\n') i++;
                spans.Add(new FormatSpan(start, i - start, commentColor));
                continue;
            }

            // Block comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 < text.Length) i += 2; // skip */
                else i = text.Length;
                spans.Add(new FormatSpan(start, i - start, commentColor));
                continue;
            }
        }
    }
}
