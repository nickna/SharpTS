using SharpTS.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpTS.Parsing;

// =============================================================================
// JSX element parsing (TSX dialect only; the '<' commit happens in Unary()).
//
// The upfront Lexer applies TypeScript rules inside JSX text — an apostrophe
// starts a string literal, `//` starts a comment, `#` is an error — so JSX text
// runs and attribute string values are scanned directly from the source string
// (JsxText) using Token.Start offsets. Whenever that source-driven scan proves
// the upfront token stream wrong, the stream is repaired in place: re-lex the
// suffix with fresh state (Lexer.Relex) and splice it into _tokens up to a
// convergence point where the original stream agrees with the fresh lex again.
// Splices only ever touch indices >= _current, so speculative-parse backtrack
// positions (always <= _current) stay valid.
// =============================================================================

public partial class Parser
{
    private readonly List<(string Name, int Line)> _jsxElementStack = [];
    private bool _jsxRecoveredExpressionFailure;

    /// <summary>
    /// Records a parse error at an explicit line without throwing — for JSX text errors
    /// tsc recovers from (bare '&gt;'/'}' in text).
    /// </summary>
    private void RecordErrorAt(int line, string message, string? tsCode = null) =>
        _diagnostics.AddError(DiagnosticCode.ParseError, message, new SourceLocation(_filePath, line), tsCode);

    private int LineAtOffset(int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < _source!.Length; i++)
            if (_source[i] == '\n') line++;
        return line;
    }

    /// <summary>
    /// Positions <c>_current</c> at the token starting exactly at <paramref name="offset"/>
    /// with the expected type, repairing the token stream via <see cref="SpliceRelexedTokens"/>
    /// when the upfront lex was corrupted there. Only used for JSX terminators ('&lt;', '{'),
    /// which always begin a token when the stream is valid.
    /// </summary>
    private void ResyncAtTerminator(int offset, TokenType expected)
    {
        for (int i = _current; i < _tokens.Count; i++)
        {
            Token t = _tokens[i];
            if (t.Start < 0) continue;              // parser-synthesized (e.g. '>>' splits)
            if (t.Start > offset) break;            // stream disagrees — corrupted
            if (t.Start == offset && t.Type == expected)
            {
                _current = i;
                return;
            }
        }
        SpliceRelexedTokens(offset);
    }

    /// <summary>
    /// Re-lexes the source from <paramref name="offset"/> with fresh state and splices the
    /// result into <c>_tokens</c> in place of the corrupted range. The original tail is kept
    /// from the first token where it exactly agrees with the fresh lex (same start/type/lexeme)
    /// while the fresh lexer is state-neutral — beyond that point both lexes are identical, and
    /// keeping the tail preserves correct tokens for enclosing constructs (e.g. the
    /// TEMPLATE_MIDDLE/TAIL of a template interpolation the JSX sits inside). Without
    /// convergence the rest of the stream is replaced wholesale; pathological cases degrade to
    /// ordinary parse errors, never a silent misparse.
    /// </summary>
    /// <summary>
    /// How many template interpolations syntactically enclose the current parse position.
    /// Threaded into <see cref="Lexer.Relex"/> so a JSX repair inside <c>`${&lt;jsx/&gt;}`</c>
    /// re-lexes the template tail as template, not as ordinary code.
    /// </summary>
    private int _templateInterpolationDepth = 0;

    private Expr ParseTemplateInterpolation()
    {
        _templateInterpolationDepth++;
        try
        {
            return Expression();
        }
        finally
        {
            _templateInterpolationDepth--;
        }
    }

    private void SpliceRelexedTokens(int offset)
    {
        var relexed = Lexer.Relex(_source!, offset, LineAtOffset(offset), _templateInterpolationDepth);

        int spliceEnd = _tokens.Count;   // exclusive end of the replaced range
        int insertCount = relexed.Count; // fresh tokens to insert (default: all, incl. EOF)
        for (int n = 0; n < relexed.Count; n++)
        {
            var (token, neutralAfter) = relexed[n];
            if (!neutralAfter || token.Type == TokenType.EOF) continue;
            int match = FindExactToken(token);
            if (match >= 0)
            {
                spliceEnd = match;
                insertCount = n;
                break;
            }
        }

        _tokens.RemoveRange(_current, spliceEnd - _current);
        _tokens.InsertRange(_current, relexed.Take(insertCount).Select(r => r.Token));
    }

    private int FindExactToken(Token fresh)
    {
        for (int i = _current; i < _tokens.Count; i++)
        {
            Token t = _tokens[i];
            if (t.Start < 0) continue;
            if (t.Start > fresh.Start) return -1;
            if (t.Start == fresh.Start && t.Type == fresh.Type &&
                string.Equals(t.Lexeme, fresh.Lexeme, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    /// <summary>True when the next token is '&gt;' or a compound operator fused onto it ('&gt;=', '&gt;&gt;', …).</summary>
    private bool CheckJsxTagEnd()
    {
        Token t = Peek();
        return t.Type == TokenType.GREATER ||
            (t.Start >= 0 && t.Lexeme.Length > 0 && t.Lexeme[0] == '>');
    }

    /// <summary>
    /// Consumes the '&gt;' closing a JSX tag, tolerating the upfront lexer having fused it
    /// into a compound operator (<c>&lt;div&gt;=5&lt;/div&gt;</c> lexes '&gt;=' at the tag
    /// close). Returns the source offset just after the '&gt;'; children scanning restarts
    /// from the source there, and the next resync repositions the cursor.
    /// </summary>
    private int ConsumeJsxTagClose(string context)
    {
        Token t = Peek();
        if (!CheckJsxTagEnd() || t.Start < 0)
            throw new ParseError($"'>' expected {context}.", "TS1005");
        Advance();
        return t.Start + 1;
    }

    /// <summary>
    /// Parses a JSX element/fragment. Currently lowers to a runtime-neutral object expression
    /// asserted as <c>any</c>; intrinsic attributes are checked against
    /// <c>JSX.IntrinsicElements[tag]</c> when a declaration provides it. (Interim lowering:
    /// replaced by factory-call desugaring in the JSX transform work.)
    /// </summary>
    private Expr ParseJsxElement()
    {
        bool isRoot = _jsxElementStack.Count == 0;
        if (isRoot)
            _jsxRecoveredExpressionFailure = false;
        try
        {
            return ParseJsxElementCore().Expr;
        }
        finally
        {
            if (isRoot)
                _jsxElementStack.Clear();
        }
    }

    /// <summary>
    /// Core JSX element parser. Also returns the source offset just past the element's final
    /// '&gt;' so an enclosing children loop can resume source-driven text scanning.
    /// </summary>
    private (Expr Expr, int EndOffset) ParseJsxElementCore()
    {
        Token open = Consume(TokenType.LESS, "Expect '<' before JSX element.");
        if (open.Start >= 0)
        {
            // The ordinary lexer can carry stale line state after JSX text that
            // resembles a string, comment, or regular expression. Source offsets
            // remain authoritative for JSX diagnostics and lowered JSX metadata.
            open = new Token(
                open.Type, open.Lexeme, open.Literal,
                LineAtOffset(open.Start), open.Start);
        }
        RecordJsxUnicodeEscapeDiagnostics(open.Start, open.Line);
        bool isFragment = Check(TokenType.GREATER);

        string tagName;
        Expr tagExpression;
        int typeArgumentCount = 0;
        List<Expr.Property> attributes = [];
        bool selfClosing = false;
        int childStart;

        if (isFragment)
        {
            tagName = "";
            tagExpression = new Expr.Literal("Fragment");
            childStart = ConsumeJsxTagClose("after '<' in JSX fragment");
        }
        else
        {
            // In `<div>< <Child/></div>` the second '<' is consumed by
            // TypeScript's angle-bracket/type-argument recovery, not as a nested
            // JSX element. Preserve the outer element's cursor progress while
            // recording the same recovery cascade on the following tag.
            if (Check(TokenType.LESS))
            {
                int recoveryLine = Peek().Line;
                RecordErrorAt(recoveryLine, "Identifier expected.", "TS1003");
                if (Peek().Start >= 0 &&
                    Peek().Start + 1 < _source!.Length &&
                    _source[Peek().Start + 1] == '/')
                {
                    // `<` immediately followed by the parent's closing tag is
                    // just a missing JSX name. Do not consume that closing tag.
                    return (new Expr.Literal(null), Peek().Start);
                }
                RecordErrorAt(recoveryLine,
                    "Value is being used as a type here.", "TS2749");
                RecordErrorAt(recoveryLine, "'>' expected.", "TS1005");
                return (new Expr.Literal(null), SkipMalformedJsxOpeningTag());
            }

            if (!TryParseEscapedJsxTagName(open, out tagName, out tagExpression))
                (tagName, tagExpression) = ParseJsxTagName();
            childStart = -1;

            // A namespace name cannot continue as a member expression (`<a:b.c>`).
            // tsc leaves the trailing member in expression recovery and reports the
            // resulting four-code cascade on that line.
            if (tagName.Contains(':') && Check(TokenType.DOT))
                return RecoverInvalidNamespacedMemberTag(open);

            // JavaScript/JSX files do not have TSX type-argument syntax. The `<Prop>`
            // following a component name starts another JSX root and drives tsc's
            // JSX recovery diagnostics instead.
            if (_filePath?.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) == true &&
                Check(TokenType.LESS))
            {
                return RecoverJsxTypeArgumentsInJavaScript(open);
            }

            if (Check(TokenType.LESS) &&
                TryParseTypeArguments(out _) is { } jsxTypeArguments)
            {
                typeArgumentCount = jsxTypeArguments.Count;

                // Explicit type arguments make even a lowercase JSX tag a value
                // reference. TypeScript resolves `<diddy<boolean>>` against the
                // declared `diddy` function (and can therefore report TS2558), rather
                // than looking up the intrinsic name "diddy".
                if (tagExpression is Expr.Literal &&
                    !tagName.Contains('-') &&
                    !tagName.Contains(':'))
                {
                    tagExpression = new Expr.Variable(new Token(
                        TokenType.IDENTIFIER, tagName, null, open.Line));
                }
            }

            while (!CheckJsxTagEnd() && !IsAtEnd())
            {
                if (Check(TokenType.LESS))
                {
                    int recoveryLine = Peek().Line;
                    bool isClosingTag = Peek().Start >= 0 &&
                        Peek().Start + 1 < _source!.Length &&
                        _source[Peek().Start + 1] == '/';
                    if (isClosingTag)
                    {
                        // The closing tag belongs to the parent. Retain this partial
                        // element so checker diagnostics such as TS2558/TS2304 are not
                        // lost, and let the parent's child loop consume `</...>`.
                        RecordErrorAt(recoveryLine, "Identifier expected.", "TS1003");
                        selfClosing = true;
                        childStart = Peek().Start;
                        break;
                    }

                    // With no attributes or type arguments, tsc treats the next
                    // `<Name/>` as an attempted type argument during recovery.
                    // Once an attribute/type-argument list has started it instead
                    // reports the ordinary missing-identifier diagnostic.
                    if (typeArgumentCount == 0 && attributes.Count == 0)
                    {
                        RecordErrorAt(recoveryLine,
                            "Expected 0 type arguments, but got 1.", "TS2558");
                        RecordErrorAt(recoveryLine,
                            "Value is being used as a type here.", "TS2749");
                        RecordErrorAt(recoveryLine, "'>' expected.", "TS1005");
                    }
                    else
                    {
                        RecordErrorAt(recoveryLine, "Identifier expected.", "TS1003");
                    }
                    selfClosing = true;
                    childStart = SkipMalformedJsxOpeningTag();
                    break;
                }

                if (Check(TokenType.NUMBER))
                    return RecoverInvalidJsxAttribute(open, numericPrefix: true);
                if (Check(TokenType.MINUS))
                    return RecoverInvalidJsxAttribute(open, numericPrefix: false);

                if (Match(TokenType.SLASH))
                {
                    if (!CheckJsxTagEnd())
                    {
                        RecordErrorAt(Peek().Line, "'>' expected.", "TS1005");
                        selfClosing = true;
                        childStart = Peek().Start;
                        break;
                    }
                    int end = ConsumeJsxTagClose("after '/' in JSX self-closing element");
                    selfClosing = true;
                    childStart = end;
                    break;
                }

                if (Match(TokenType.LEFT_BRACE))
                {
                    Consume(TokenType.DOT_DOT_DOT, "Expect '...' in JSX spread attribute.");
                    Expr spread = Expression();
                    Consume(TokenType.RIGHT_BRACE, "Expect '}' after JSX spread attribute.");
                    attributes.Add(new Expr.Property(null, spread, IsSpread: true));
                    continue;
                }

                int attributeProbe = Peek().Start;
                if (attributeProbe > 0 && _source![attributeProbe - 1] == '\\')
                    attributeProbe--;

                Token nameStart;
                string attributeName;
                if (TryReadEscapedJsxName(attributeProbe, out string escapedName, out int escapedEnd))
                {
                    while (!IsAtEnd() && Peek().Start >= 0 && Peek().Start < escapedEnd)
                        Advance();
                    attributeName = escapedName;
                    nameStart = new Token(TokenType.IDENTIFIER, attributeName, null, Peek().Line);
                }
                else
                {
                    nameStart = ConsumeJsxIdentifierName("Expect JSX attribute name.");
                    attributeName = nameStart.Lexeme;
                    while (Check(TokenType.MINUS) || Check(TokenType.COLON))
                    {
                        char separator = Advance().Type == TokenType.MINUS ? '-' : ':';
                        attributeName += separator + ConsumeJsxNamePart("Expect JSX attribute name part.");
                    }
                }
                var attributeToken = new Token(
                    TokenType.IDENTIFIER, attributeName, null, nameStart.Line);

                Expr value = new Expr.Literal(true);
                if (Match(TokenType.EQUAL))
                {
                    value = ParseJsxAttributeValue(out int? recoveryEndOffset);
                    if (recoveryEndOffset is not null)
                    {
                        attributes.Add(new Expr.Property(
                            new Expr.IdentifierKey(attributeToken), value));
                        // The nested JSX consumed what would have closed this
                        // attribute/opening tag. Retain the partial element and
                        // let its enclosing JSX text scan resume at the next token.
                        selfClosing = true;
                        childStart = recoveryEndOffset.Value;
                        break;
                    }
                }
                attributes.Add(new Expr.Property(new Expr.IdentifierKey(attributeToken), value));
            }

            if (!selfClosing)
            {
                if (IsAtEnd())
                    throw new ParseError(
                        $"JSX element '{tagName}' has no corresponding closing tag.", "TS17008");
                childStart = ConsumeJsxTagClose("after JSX opening tag");
            }
        }

        List<Expr> children = [];
        List<int> childLines = [];
        int endOffset = childStart;
        bool trackedElement = !selfClosing && !isFragment;
        if (trackedElement)
            _jsxElementStack.Add((tagName, open.Line));
        if (!selfClosing)
        {
            bool recoveredWithoutClosingTag = false;
            while (true)
            {
                // A recovered child can end at EOF after trivia that produced no token;
                // derive the line from the source offset rather than Previous(), whose
                // token may still be on the opening line.
                var scan = JsxText.ScanText(_source!, childStart, LineAtOffset(childStart));
                foreach (var error in scan.Errors ?? [])
                {
                    RecordErrorAt(error.Line, error.Character == '>'
                        ? "Unexpected token. Did you mean `{'>'}` or `&gt;`?"
                        : "Unexpected token. Did you mean `{'}'}` or `&rbrace;`?",
                        error.Character == '>' ? "TS1382" : "TS1381");
                }
                string? text = JsxText.CookChildText(scan.Raw);
                if (text is not null)
                {
                    children.Add(new Expr.Literal(text));
                    childLines.Add(LineAtOffset(childStart));
                }

                if (scan.Terminator == '\0')
                {
                    RecordErrorAt(open.Line,
                        isFragment
                            ? "JSX fragment has no corresponding closing tag."
                            : $"JSX element '{tagName}' has no corresponding closing tag.",
                        isFragment ? "TS17014" : "TS17008");
                    // When a nested element consumes the rest of the file, tsc
                    // reports the missing closing tag for that innermost element
                    // and abandons the ancestor without a second EOF diagnostic.
                    if (_jsxElementStack.Count <= 1)
                        RecordErrorAt(scan.EndLine, "'</' expected.", "TS1005");
                    endOffset = scan.EndOffset;
                    recoveredWithoutClosingTag = true;
                    break;
                }

                if (scan.Terminator == '{')
                {
                    ResyncAtTerminator(scan.EndOffset, TokenType.LEFT_BRACE);
                    Advance();
                    int childContentOffset = scan.EndOffset + 1;
                    while (childContentOffset < _source!.Length &&
                           char.IsWhiteSpace(_source[childContentOffset]))
                    {
                        childContentOffset++;
                    }
                    bool childStartedWithSlash = childContentOffset < _source.Length &&
                        _source[childContentOffset] == '/';
                    // A closing tag where a JSX child expression should begin (`{ </div>`)
                    // is TS1109, but the closing tag still belongs to this element. Leave it
                    // for the next text scan so recovery retains the element AST and checker
                    // diagnostics instead of discarding the whole statement.
                    if (Check(TokenType.LESS) &&
                        Peek().Start >= 0 &&
                        Peek().Start + 1 < _source!.Length &&
                        _source[Peek().Start + 1] == '/')
                    {
                        RecordErrorAt(Peek().Line, "Expression expected.", "TS1109");
                        childStart = Peek().Start;
                        continue;
                    }
                    // Empty JSX expressions ({} or {/* comment */} once lexed) contribute no child.
                    if (!Check(TokenType.RIGHT_BRACE))
                    {
                        int expressionLine = Peek().Line;
                        bool isSpreadChild = Match(TokenType.DOT_DOT_DOT);
                        try
                        {
                            Expr child = Expression();
                            children.Add(isSpreadChild ? new Expr.Spread(child) : child);
                            childLines.Add(expressionLine);
                        }
                        catch (ParseError ex)
                        {
                            int offset = Peek().Start;
                            RecordErrorAt(offset >= 0 ? LineAtOffset(offset) : scan.EndLine,
                                ex.Message, ex.TsCode);
                            childStart = offset >= 0 ? offset : _source!.Length;
                            continue;
                        }
                        catch
                        {
                            // Expression recovery inside JSX can fail after nested
                            // tags have already consumed a ternary delimiter or brace.
                            // The JSX text scanner owns those token diagnostics; keep
                            // the partial root and resume from the current source token.
                            int offset = Peek().Start;
                            _jsxRecoveredExpressionFailure = true;
                            childStart = offset >= 0 ? offset : _source!.Length;
                            continue;
                        }
                    }
                    if (!Check(TokenType.RIGHT_BRACE))
                    {
                        // Keep the expression already parsed and resume JSX text
                        // scanning at the unexpected token. This mirrors tsc's
                        // recovery for `{ test: <span/> }`: `test` remains a child,
                        // the nested tag is still discovered, and the later bare
                        // brace is diagnosed by the JSX text scanner.
                        int recoveryOffset = Peek().Start;
                        // A regex-led JSX expression can consume a ternary delimiter
                        // during nested-tag recovery. tsc lets the JSX text recovery
                        // own that cascade and does not add a local missing-brace error.
                        if (!childStartedWithSlash && !_jsxRecoveredExpressionFailure)
                        {
                            RecordErrorAt(
                                recoveryOffset >= 0 ? LineAtOffset(recoveryOffset) : Previous().Line,
                                "'}' expected.", "TS1005");
                        }
                        childStart = recoveryOffset >= 0 ? recoveryOffset : _source!.Length;
                        continue;
                    }
                    Token rightBrace = Advance();
                    childStart = rightBrace.Start + 1;
                    continue;
                }

                // Terminator '<': either this element's closing tag (`</` must be adjacent,
                // as in tsc) or a nested child element.
                if (scan.EndOffset + 1 < _source!.Length && _source[scan.EndOffset + 1] == '/')
                {
                    ResyncAtTerminator(scan.EndOffset, TokenType.LESS);
                    break;
                }

                ResyncAtTerminator(scan.EndOffset, TokenType.LESS);
                int nestedChildLine = Peek().Line;
                var (childExpr, childEnd) = ParseJsxElementCore();
                children.Add(childExpr);
                childLines.Add(nestedChildLine);
                childStart = childEnd;
            }

            if (!recoveredWithoutClosingTag)
            {
                int closingPosition = _current;
                int closingOffset = Peek().Start;
                Consume(TokenType.LESS, "Expect JSX closing tag.");
                Consume(TokenType.SLASH, "Expect '/' in JSX closing tag.");
                if (!isFragment)
                {
                    string closingName = ConsumeJsxIdentifierName("Expect JSX closing tag name.").Lexeme;
                    while (Check(TokenType.MINUS) || Check(TokenType.COLON) || Check(TokenType.DOT))
                    {
                        char separator = Advance().Type switch
                        {
                            TokenType.MINUS => '-',
                            TokenType.COLON => ':',
                            _ => '.',
                        };
                        closingName += separator + ConsumeJsxNamePart("Expect JSX closing name part.");
                    }
                    if (!string.Equals(tagName, closingName, StringComparison.Ordinal))
                    {
                        bool closesAncestor = _jsxElementStack
                            .Take(Math.Max(0, _jsxElementStack.Count - 1))
                            .Any(element => string.Equals(
                                element.Name, closingName, StringComparison.Ordinal));
                        if (closesAncestor)
                        {
                            RecordErrorAt(open.Line,
                                $"JSX element '{tagName}' has no corresponding closing tag.",
                                "TS17008");
                            _current = closingPosition;
                            endOffset = closingOffset;
                            recoveredWithoutClosingTag = true;
                        }
                        else
                        {
                            RecordErrorAt(Peek().Line,
                                $"Expected corresponding JSX closing tag for '{tagName}'.",
                                "TS17002");
                        }
                    }
                }
                if (!recoveredWithoutClosingTag)
                    endOffset = ConsumeJsxTagClose("after JSX closing tag");
            }
        }

        if (trackedElement && _jsxElementStack.Count > 0)
            _jsxElementStack.RemoveAt(_jsxElementStack.Count - 1);

        return (LowerJsxElement(
            open, isFragment, tagName, tagExpression, attributes, children, childLines,
            typeArgumentCount), endOffset);
    }

    /// <summary>
    /// Skips one JSX-looking opening tag during malformed-opening recovery and returns
    /// the source offset after its closing bracket. The caller has already emitted the
    /// tsc-shaped diagnostic cascade; this helper only guarantees forward progress.
    /// </summary>
    private int SkipMalformedJsxOpeningTag()
    {
        int endOffset = Peek().Start;
        while (!IsAtEnd())
        {
            Token token = Advance();
            endOffset = token.End;
            if (token.Type == TokenType.GREATER)
                break;
        }
        return endOffset;
    }

    /// <summary>
    /// Parses a JSX opening-tag name: an identifier, a dashed/namespaced intrinsic
    /// (<c>foo-bar</c>, <c>svg:rect</c>), or a member expression (<c>A.B.C</c>).
    /// </summary>
    private (string TagName, Expr TagExpression) ParseJsxTagName()
    {
        Token first = ConsumeJsxIdentifierName("Expect JSX tag name.");
        string tagName = first.Lexeme;
        int nameEnd = first.End;

        while ((Check(TokenType.MINUS) && Peek().Start == nameEnd) || Check(TokenType.COLON))
        {
            char separator = Advance().Type == TokenType.MINUS ? '-' : ':';
            tagName += separator + ConsumeJsxNamePart("Expect JSX tag name part.");
            nameEnd = Previous().End;
        }

        // Dashed/namespaced names are intrinsic elements. Member expressions take
        // precedence over the lowercase rule (`<x.video />` is a component access).
        if (tagName.Contains('-') || tagName.Contains(':'))
            return (tagName, new Expr.Literal(tagName));

        int firstLine = first.Start >= 0 ? LineAtOffset(first.Start) : first.Line;
        Expr tagExpression = new Expr.Variable(new Token(
            TokenType.IDENTIFIER, first.Lexeme, null, firstLine, first.Start));
        bool hasMemberAccess = false;
        while (Match(TokenType.DOT))
        {
            hasMemberAccess = true;
            Token part = ConsumeJsxIdentifierName("Expect JSX member name.");
            tagName += "." + part.Lexeme;
            tagExpression = new Expr.Get(tagExpression, part);
        }
        if (!hasMemberAccess && char.IsLower(tagName[0]))
            return (tagName, new Expr.Literal(tagName));
        return (tagName, tagExpression);
    }

    private bool TryParseEscapedJsxTagName(
        Token open,
        out string tagName,
        out Expr tagExpression)
    {
        int start = open.Start + open.Lexeme.Length;
        if (!TryReadEscapedJsxName(start, out tagName, out int end))
        {
            tagExpression = null!;
            return false;
        }

        while (!IsAtEnd() && Peek().Start >= 0 && Peek().Start < end)
            Advance();

        tagExpression = BuildJsxTagExpression(tagName, open.Line);
        return true;
    }

    private bool TryReadEscapedJsxName(int start, out string decoded, out int end)
    {
        end = start;
        while (end < _source!.Length &&
               !char.IsWhiteSpace(_source[end]) &&
               _source[end] is not '>' and not '/' and not '=')
        {
            end++;
        }

        string raw = start >= 0 && end > start ? _source[start..end] : "";
        if (!raw.Contains("\\u", StringComparison.Ordinal))
        {
            decoded = raw;
            return false;
        }

        decoded = Regex.Replace(raw, @"\\u\{([0-9a-fA-F]+)\}|\\u([0-9a-fA-F]{4})", match =>
        {
            string digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            int codePoint = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return char.ConvertFromUtf32(codePoint);
        });
        return true;
    }

    private void RecordJsxUnicodeEscapeDiagnostics(int openOffset, int line)
    {
        if (openOffset < 0) return;
        char quote = '\0';
        int braceDepth = 0;
        for (int i = openOffset + 1; i < _source!.Length; i++)
        {
            char c = _source[i];
            if (c == '\n') line++;
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }
            if (c == '{') { braceDepth++; continue; }
            if (c == '}' && braceDepth > 0) { braceDepth--; continue; }
            if (c == '>' && braceDepth == 0) break;
            if (braceDepth == 0 && c == '\\' && i + 1 < _source.Length && _source[i + 1] == 'u')
                RecordErrorAt(line, "Unicode escape sequence cannot appear here.", "TS17021");
        }
    }

    private static Expr BuildJsxTagExpression(string tagName, int line)
    {
        if (tagName.Contains('-') || tagName.Contains(':') ||
            (!tagName.Contains('.') && char.IsLower(tagName[0])))
        {
            return new Expr.Literal(tagName);
        }

        string[] parts = tagName.Split('.');
        Expr expression = new Expr.Variable(
            new Token(TokenType.IDENTIFIER, parts[0], null, line));
        foreach (string part in parts.Skip(1))
            expression = new Expr.Get(expression,
                new Token(TokenType.IDENTIFIER, part, null, line));
        return expression;
    }

    private (Expr Expr, int EndOffset) RecoverInvalidNamespacedMemberTag(Token open)
    {
        int line = Peek().Line;
        RecordErrorAt(line, "Identifier expected.", "TS1003");
        RecordErrorAt(line, "'>' expected.", "TS1005");
        RecordErrorAt(line, "Cannot find name in invalid JSX namespace member.", "TS2304");
        RecordErrorAt(line, "Expression expected.", "TS1109");
        int end = SkipMalformedJsxToStatementBoundary();
        return (new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance), end);
    }

    private (Expr Expr, int EndOffset) RecoverInvalidJsxAttribute(
        Token open,
        bool numericPrefix)
    {
        int line = Peek().Line;
        RecordErrorAt(line, "Identifier expected.", "TS1003");
        RecordErrorAt(line, "';' expected.", "TS1005");
        RecordErrorAt(line, "Cannot find malformed JSX attribute name.", "TS2304");
        RecordErrorAt(line, "Invalid arithmetic operand in JSX recovery.", "TS2362");
        if (numericPrefix)
        {
            RecordErrorAt(line,
                "An identifier or keyword cannot immediately follow a numeric literal.",
                "TS1351");
            RecordErrorAt(line, "Expression expected.", "TS1109");
        }
        else
        {
            RecordErrorAt(line, "Unterminated regular expression literal.", "TS1161");
        }

        int end = SkipMalformedJsxToStatementBoundary();
        return (new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance), end);
    }

    private (Expr Expr, int EndOffset) RecoverJsxTypeArgumentsInJavaScript(Token open)
    {
        int line = open.Line;
        RecordErrorAt(line, "Identifier expected.", "TS1003");
        RecordErrorAt(line, "Unexpected token in JSX text.", "TS1382");
        RecordErrorAt(line, "JSX element has no corresponding closing tag.", "TS17008");
        RecordErrorAt(line, "JSX expressions must have one parent element.", "TS2657");
        RecordErrorAt(line, "Type-only name used as a value.", "TS2693");
        while (!IsAtEnd()) Advance();
        RecordErrorAt(LineAtOffset(_source!.Length), "'</' expected.", "TS1005");
        return (new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance), _source.Length);
    }

    private int SkipMalformedJsxToStatementBoundary()
    {
        while (!IsAtEnd() && !Check(TokenType.SEMICOLON))
            Advance();
        return Check(TokenType.SEMICOLON) && Peek().Start >= 0
            ? Peek().Start
            : _source!.Length;
    }

    /// <summary>Name continuation after '-' or ':' — identifier-ish or numeric (<c>data-1</c>).</summary>
    private string ConsumeJsxNamePart(string message)
    {
        if (Check(TokenType.NUMBER))
            return Advance().Lexeme;
        return ConsumeJsxIdentifierName(message).Lexeme;
    }

    private Token ConsumeJsxIdentifierName(string message)
    {
        Token token = Peek();
        if (token.Type == TokenType.IDENTIFIER ||
            IsKeyword(token.Type) ||
            IsContextualKeyword(token.Type))
        {
            Advance();
            return token.Type == TokenType.IDENTIFIER
                ? token
                : new Token(
                    TokenType.IDENTIFIER,
                    token.Lexeme,
                    null,
                    token.Start >= 0 ? LineAtOffset(token.Start) : token.Line,
                    token.Start);
        }
        throw new ParseError(message, "TS1003");
    }

    /// <summary>
    /// Parses a JSX attribute value after '='. String values are scanned from the source —
    /// JSX strings end at the same quote with no backslash escapes and may span newlines, so
    /// the upfront STRING token (TS escape rules) cannot be trusted.
    /// </summary>
    private Expr ParseJsxAttributeValue(out int? recoveryEndOffset)
    {
        recoveryEndOffset = null;
        // Probe the source after '=' rather than trusting the token stream: a JSX string the
        // upfront lexer mis-lexed under TS escape rules may have swallowed the rest of the
        // file and produced no token at all at the value position.
        Token equals = Previous();
        int probe = equals.Start >= 0 ? equals.Start + equals.Lexeme.Length : -1;
        while (probe >= 0 && probe < _source!.Length && char.IsWhiteSpace(_source[probe]))
            probe++;
        if (probe >= 0 && probe < _source!.Length && _source[probe] is '"' or '\'')
        {
            var scan = JsxText.CookAttributeValue(_source, probe, equals.Line);
            // When the upfront lex agrees on the string's extent, just consume its token;
            // otherwise the stream is corrupted from here — repair it from after the quote.
            Token valueToken = Peek();
            if (valueToken.Type == TokenType.STRING && valueToken.Start == probe &&
                probe + valueToken.Lexeme.Length == scan.EndOffset + 1)
            {
                Advance();
            }
            else
            {
                SpliceRelexedTokens(scan.EndOffset + 1);
            }
            return new Expr.Literal(scan.Value);
        }

        if (Match(TokenType.LEFT_BRACE))
        {
            int expressionLine = Previous().Line;
            if (Match(TokenType.DOT_DOT_DOT))
            {
                // Spread syntax is legal for a JSX child but not as the immediate value of
                // an attribute expression. tsc reports both the expression and identifier
                // recovery diagnostics, then continues with the operand.
                RecordErrorAt(expressionLine, "Expression expected.", "TS1109");
                Expr operand = Expression();
                RecordErrorAt(expressionLine, "Identifier expected.", "TS1003");
                Consume(TokenType.RIGHT_BRACE, "Expect '}' after JSX attribute expression.");
                return operand;
            }

            Expr value = CommaExpression();
            if (value is Expr.Comma)
            {
                RecordErrorAt(expressionLine,
                    "Left side of comma operator is unused and has no side effects.", "TS2695");
                RecordErrorAt(expressionLine,
                    "JSX expressions may not use the comma operator. Did you mean to write an array?",
                    "TS18007");
            }
            if (!Check(TokenType.RIGHT_BRACE))
            {
                int offset = Peek().Start;
                RecordErrorAt(offset >= 0 ? LineAtOffset(offset) : expressionLine,
                    "'}' expected.", "TS1005");
                recoveryEndOffset = offset >= 0 ? offset : _source!.Length;
                return value;
            }
            Advance();
            return value;
        }

        if (Check(TokenType.LESS))
            return ParseJsxElement();

        // tsc recovers `<div attr= />` as a boolean-style attribute without a diagnostic.
        // Keep the tag terminator unconsumed so the opening-tag loop can close normally.
        if (Check(TokenType.SLASH) || CheckJsxTagEnd())
            return new Expr.Literal(true);

        // A bare value after '=' is not a JSX attribute initializer. TypeScript
        // recovers it as an expression but selects TS1145 (not the older TS17000).
        int invalidLine = Peek().Line;
        RecordErrorAt(invalidLine, "'{' or JSX element expected.", "TS1145");
        if (Check(TokenType.RIGHT_BRACE))
        {
            RecordErrorAt(LineAtOffset(_source!.Length), "'</' expected.", "TS1005");
            return new Expr.Literal(true);
        }
        return Assignment();
    }

    // Automatic-runtime usage flags: which names the synthesized
    // `import { … } from "<jsxImportSource>/jsx-runtime"` must bind.
    private bool _jsxUsedJsx;
    private bool _jsxUsedJsxs;
    private bool _jsxUsedJsxDev;
    private bool _jsxUsedFragment;
    private bool JsxUsedAutomaticRuntime => _jsxUsedJsx || _jsxUsedJsxs || _jsxUsedJsxDev || _jsxUsedFragment;

    /// <summary>Reserved local-name prefix for the synthesized runtime bindings (documented).</summary>
    private const string JsxLocalPrefix = "__sharpts_";

    /// <summary>
    /// Lowers a parsed JSX element to its factory call per the configured jsx mode. Every
    /// lowered call carries <see cref="JsxCallInfo"/> so the checker runs JSX semantics
    /// (its Expr references are aliases of nodes reachable from the call's arguments).
    /// </summary>
    private Expr LowerJsxElement(
        Token open,
        bool isFragment,
        string tagName,
        Expr tagExpression,
        List<Expr.Property> attributes,
        List<Expr> children,
        List<int> childLines,
        int typeArgumentCount)
    {
        return _jsx!.Mode is JsxMode.React or JsxMode.Preserve
            ? LowerJsxClassic(open, isFragment, tagName, tagExpression, attributes, children, childLines, typeArgumentCount)
            : LowerJsxAutomatic(open, isFragment, tagName, tagExpression, attributes, children, childLines, typeArgumentCount);
    }

    /// <summary>
    /// Classic transform: <c>jsxFactory(type, propsOrNull, ...children)</c>. Fragments use
    /// jsxFragmentFactory as the type. <c>key</c> stays in props (createElement extracts it
    /// at runtime). An intrinsic with no attributes passes an empty object literal rather
    /// than tsc's <c>null</c> so required-prop checking still fires (runtime-equivalent).
    /// </summary>
    private Expr LowerJsxClassic(
        Token open,
        bool isFragment,
        string tagName,
        Expr tagExpression,
        List<Expr.Property> attributes,
        List<Expr> children,
        List<int> childLines,
        int typeArgumentCount)
    {
        bool isIntrinsic = !isFragment && tagExpression is Expr.Literal;
        bool inlineFactoryWithoutFragment =
            isFragment && _jsx!.FactoryFromPragma && !_jsx.FragmentFactoryFromPragma;
        if (inlineFactoryWithoutFragment)
        {
            // TS17017 is recoverable: tsc still performs its semantic fallback lookup using
            // React.createElement/React.Fragment, which can additionally produce TS2874/TS2879.
            RecordErrorAt(
                open.Line,
                "JSX fragment is not supported when using an inline JSX factory pragma.",
                "TS17017");
        }

        string factory = inlineFactoryWithoutFragment ? "React.createElement" : _jsx!.Factory;
        Expr tag = isFragment
            ? inlineFactoryWithoutFragment
                ? BuildDottedExpr("React.Fragment", open.Line)
                : _jsx!.Mode == JsxMode.Preserve
                    ? new Expr.Literal("Fragment")
                    : BuildFragmentFactoryExpr(open.Line)
            : tagExpression;

        Expr.ObjectLiteral? propsLiteral = null;
        Expr propsArgument;
        if (attributes.Count > 0 || isIntrinsic)
        {
            propsLiteral = new Expr.ObjectLiteral(attributes);
            propsArgument = propsLiteral;
        }
        else
        {
            propsArgument = new Expr.Literal(null);
        }

        var arguments = new List<Expr> { tag, propsArgument };
        arguments.AddRange(children);

        return new Expr.Call(
            BuildDottedExpr(factory, open.Line),
            SynthesizedToken(TokenType.LEFT_PAREN, "(", open.Line),
            null,
            arguments)
        {
            JsxOrigin = new JsxCallInfo(
                isFragment ? JsxElementKind.Fragment
                    : isIntrinsic ? JsxElementKind.Intrinsic : JsxElementKind.Component,
                isFragment ? null : tagName,
                propsLiteral,
                children,
                KeyExpr: null,
                _jsx!.Mode,
                open.Line,
                typeArgumentCount,
                childLines),
        };
    }

    /// <summary>
    /// Automatic transform: <c>__sharpts_jsx(type, propsWithChildren[, key])</c> —
    /// <c>jsxs</c> for 2+ static children or a spread child; <c>jsxDEV</c> in dev mode with
    /// the full dev signature. Children fold into the props object (1 child = the
    /// expression, otherwise an array); <c>key</c> is extracted from the attributes to the
    /// third argument.
    /// </summary>
    private Expr LowerJsxAutomatic(
        Token open,
        bool isFragment,
        string tagName,
        Expr tagExpression,
        List<Expr.Property> attributes,
        List<Expr> children,
        List<int> childLines,
        int typeArgumentCount)
    {
        bool isIntrinsic = !isFragment && tagExpression is Expr.Literal;
        bool dev = _jsx!.Mode == JsxMode.ReactJsxDev;

        Expr? keyExpr = null;
        var props = new List<Expr.Property>(attributes.Count + 1);
        foreach (var attribute in attributes)
        {
            if (!attribute.IsSpread && attribute.Key is Expr.IdentifierKey { Name.Lexeme: "key" })
            {
                keyExpr = attribute.Value;
                continue;
            }
            props.Add(attribute);
        }

        bool useJsxs = children.Count > 1 || children.Any(c => c is Expr.Spread);
        if (children.Count > 0)
        {
            Expr childrenValue = useJsxs || children.Count > 1
                ? new Expr.ArrayLiteral([.. children])
                : children[0];
            props.Add(new Expr.Property(
                new Expr.IdentifierKey(SynthesizedToken(TokenType.IDENTIFIER, "children", open.Line)),
                childrenValue));
        }
        var propsLiteral = new Expr.ObjectLiteral(props);

        string calleeName;
        if (dev)
        {
            calleeName = JsxLocalPrefix + "jsxDEV";
            _jsxUsedJsxDev = true;
        }
        else if (useJsxs)
        {
            calleeName = JsxLocalPrefix + "jsxs";
            _jsxUsedJsxs = true;
        }
        else
        {
            calleeName = JsxLocalPrefix + "jsx";
            _jsxUsedJsx = true;
        }

        Expr tag;
        if (isFragment)
        {
            _jsxUsedFragment = true;
            tag = new Expr.Variable(SynthesizedToken(TokenType.IDENTIFIER, JsxLocalPrefix + "Fragment", open.Line));
        }
        else
        {
            tag = tagExpression;
        }

        var arguments = new List<Expr> { tag, propsLiteral };
        if (keyExpr is not null || dev)
            arguments.Add(keyExpr ?? new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance));
        if (dev)
        {
            // jsxDEV(type, props, key, isStaticChildren, {fileName, lineNumber, columnNumber}, this)
            // Tokens carry no column info, so columnNumber is pinned to 1; `this` is null.
            arguments.Add(new Expr.Literal(useJsxs));
            arguments.Add(new Expr.ObjectLiteral(
            [
                new(new Expr.IdentifierKey(SynthesizedToken(TokenType.IDENTIFIER, "fileName", open.Line)),
                    new Expr.Literal(_filePath ?? "")),
                new(new Expr.IdentifierKey(SynthesizedToken(TokenType.IDENTIFIER, "lineNumber", open.Line)),
                    new Expr.Literal((double)open.Line)),
                new(new Expr.IdentifierKey(SynthesizedToken(TokenType.IDENTIFIER, "columnNumber", open.Line)),
                    new Expr.Literal(1d)),
            ]));
            arguments.Add(new Expr.Literal(null));
        }

        return new Expr.Call(
            new Expr.Variable(SynthesizedToken(TokenType.IDENTIFIER, calleeName, open.Line)),
            SynthesizedToken(TokenType.LEFT_PAREN, "(", open.Line),
            null,
            arguments)
        {
            JsxOrigin = new JsxCallInfo(
                isFragment ? JsxElementKind.Fragment
                    : isIntrinsic ? JsxElementKind.Intrinsic : JsxElementKind.Component,
                isFragment ? null : tagName,
                propsLiteral,
                children,
                keyExpr,
                _jsx.Mode,
                open.Line,
                typeArgumentCount,
                childLines),
        };
    }

    /// <summary>
    /// The classic-mode fragment tag. <c>@jsxFrag null</c> (or jsxFragmentFactory "null")
    /// means a literal null tag — tsc emits <c>factory(null, null, …)</c> — not an
    /// identifier named "null".
    /// </summary>
    private Expr BuildFragmentFactoryExpr(int line) =>
        string.Equals(_jsx!.FragmentFactory, "null", StringComparison.Ordinal)
            ? new Expr.Literal(null)
            : BuildDottedExpr(_jsx.FragmentFactory, line);

    /// <summary>Builds the value expression for a dotted factory name ("React.createElement" → React.createElement).</summary>
    private static Expr BuildDottedExpr(string dottedName, int line)
    {
        string[] parts = dottedName.Split('.');
        Expr expr = new Expr.Variable(SynthesizedToken(TokenType.IDENTIFIER, parts[0], line));
        for (int i = 1; i < parts.Length; i++)
            expr = new Expr.Get(expr, SynthesizedToken(TokenType.IDENTIFIER, parts[i], line));
        return expr;
    }

    private static Token SynthesizedToken(TokenType type, string lexeme, int line) =>
        new(type, lexeme, null, line);

    /// <summary>
    /// The synthesized automatic-runtime import: only the names actually used, aliased under
    /// the reserved <c>__sharpts_</c> prefix, from "&lt;jsxImportSource&gt;/jsx-runtime" (or
    /// the dev runtime). Inserted by <c>Parse()</c> after the directive prologue, before var
    /// hoisting, so the module resolver chases it like any user import.
    /// </summary>
    private Stmt.Import BuildJsxRuntimeImport()
    {
        bool dev = _jsx!.Mode == JsxMode.ReactJsxDev;
        var named = new List<Stmt.ImportSpecifier>();
        void Add(string imported) => named.Add(new Stmt.ImportSpecifier(
            SynthesizedToken(TokenType.IDENTIFIER, imported, 1),
            SynthesizedToken(TokenType.IDENTIFIER, JsxLocalPrefix + imported, 1)));

        if (_jsxUsedJsx) Add("jsx");
        if (_jsxUsedJsxs) Add("jsxs");
        if (_jsxUsedJsxDev) Add("jsxDEV");
        if (_jsxUsedFragment) Add("Fragment");

        string modulePath = _jsx.ImportSource + (dev ? "/jsx-dev-runtime" : "/jsx-runtime");
        return new Stmt.Import(
            SynthesizedToken(TokenType.IMPORT, "import", 1),
            named,
            DefaultImport: null,
            NamespaceImport: null,
            modulePath)
        {
            IsSynthesizedJsxRuntime = true,
        };
    }
}
