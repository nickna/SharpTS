using SharpTS.Diagnostics;

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
    private Expr ParseJsxElement() => ParseJsxElementCore().Expr;

    /// <summary>
    /// Core JSX element parser. Also returns the source offset just past the element's final
    /// '&gt;' so an enclosing children loop can resume source-driven text scanning.
    /// </summary>
    private (Expr Expr, int EndOffset) ParseJsxElementCore()
    {
        Token open = Consume(TokenType.LESS, "Expect '<' before JSX element.");
        bool isFragment = Check(TokenType.GREATER);

        string tagName;
        Expr tagExpression;
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
            (tagName, tagExpression) = ParseJsxTagName();
            childStart = -1;

            while (!CheckJsxTagEnd() && !IsAtEnd())
            {
                if (Match(TokenType.SLASH))
                {
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

                Token nameStart = ConsumeIdentifierName("Expect JSX attribute name.");
                string attributeName = nameStart.Lexeme;
                while (Check(TokenType.MINUS) || Check(TokenType.COLON))
                {
                    char separator = Advance().Type == TokenType.MINUS ? '-' : ':';
                    attributeName += separator + ConsumeJsxNamePart("Expect JSX attribute name part.");
                }
                var attributeToken = new Token(
                    TokenType.IDENTIFIER, attributeName, null, nameStart.Line);

                Expr value = new Expr.Literal(true);
                if (Match(TokenType.EQUAL))
                    value = ParseJsxAttributeValue();
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
        int endOffset = childStart;
        if (!selfClosing)
        {
            while (true)
            {
                var scan = JsxText.ScanText(_source!, childStart, Previous().Line);
                foreach (var error in scan.Errors ?? [])
                {
                    RecordErrorAt(error.Line, error.Character == '>'
                        ? "Unexpected token. Did you mean `{'>'}` or `&gt;`?"
                        : "Unexpected token. Did you mean `{'}'}` or `&rbrace;`?",
                        error.Character == '>' ? "TS1382" : "TS1381");
                }
                string? text = JsxText.CookChildText(scan.Raw);
                if (text is not null)
                    children.Add(new Expr.Literal(text));

                if (scan.Terminator == '\0')
                    throw new ParseError(isFragment
                        ? "JSX fragment has no corresponding closing tag."
                        : $"JSX element '{tagName}' has no corresponding closing tag.",
                        isFragment ? "TS17014" : "TS17008");

                if (scan.Terminator == '{')
                {
                    ResyncAtTerminator(scan.EndOffset, TokenType.LEFT_BRACE);
                    Advance();
                    // Empty JSX expressions ({} or {/* comment */} once lexed) contribute no child.
                    if (!Check(TokenType.RIGHT_BRACE))
                    {
                        bool isSpreadChild = Match(TokenType.DOT_DOT_DOT);
                        Expr child = Expression();
                        children.Add(isSpreadChild ? new Expr.Spread(child) : child);
                    }
                    Token rightBrace = Consume(TokenType.RIGHT_BRACE, "Expect '}' after JSX child expression.");
                    if (rightBrace.Start < 0)
                        throw new ParseError("Expect '}' after JSX child expression.", "TS1005");
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
                var (childExpr, childEnd) = ParseJsxElementCore();
                children.Add(childExpr);
                childStart = childEnd;
            }

            Consume(TokenType.LESS, "Expect JSX closing tag.");
            Consume(TokenType.SLASH, "Expect '/' in JSX closing tag.");
            if (!isFragment)
            {
                string closingName = ConsumeIdentifierName("Expect JSX closing tag name.").Lexeme;
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
                    throw new ParseError(
                        $"Expected corresponding JSX closing tag for '{tagName}'.", "TS17002");
            }
            endOffset = ConsumeJsxTagClose("after JSX closing tag");
        }

        return (LowerJsxElement(open, isFragment, tagName, tagExpression, attributes, children), endOffset);
    }

    /// <summary>
    /// Parses a JSX opening-tag name: an identifier, a dashed/namespaced intrinsic
    /// (<c>foo-bar</c>, <c>svg:rect</c>), or a member expression (<c>A.B.C</c>).
    /// </summary>
    private (string TagName, Expr TagExpression) ParseJsxTagName()
    {
        Token first = ConsumeIdentifierName("Expect JSX tag name.");
        string tagName = first.Lexeme;

        while (Check(TokenType.MINUS) || Check(TokenType.COLON))
        {
            char separator = Advance().Type == TokenType.MINUS ? '-' : ':';
            tagName += separator + ConsumeJsxNamePart("Expect JSX tag name part.");
        }

        // Dashed/namespaced names and lowercase-initial names are intrinsic elements.
        if (char.IsLower(tagName[0]) || tagName.Contains('-') || tagName.Contains(':'))
            return (tagName, new Expr.Literal(tagName));

        Expr tagExpression = new Expr.Variable(first.Type == TokenType.IDENTIFIER
            ? first
            : new Token(TokenType.IDENTIFIER, first.Lexeme, null, first.Line));
        while (Match(TokenType.DOT))
        {
            Token part = ConsumeIdentifierName("Expect JSX member name.");
            tagName += "." + part.Lexeme;
            tagExpression = new Expr.Get(tagExpression, part);
        }
        return (tagName, tagExpression);
    }

    /// <summary>Name continuation after '-' or ':' — identifier-ish or numeric (<c>data-1</c>).</summary>
    private string ConsumeJsxNamePart(string message)
    {
        if (Check(TokenType.NUMBER))
            return Advance().Lexeme;
        return ConsumeIdentifierName(message).Lexeme;
    }

    /// <summary>
    /// Parses a JSX attribute value after '='. String values are scanned from the source —
    /// JSX strings end at the same quote with no backslash escapes and may span newlines, so
    /// the upfront STRING token (TS escape rules) cannot be trusted.
    /// </summary>
    private Expr ParseJsxAttributeValue()
    {
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
            Expr value = Expression();
            Consume(TokenType.RIGHT_BRACE, "Expect '}' after JSX attribute expression.");
            return value;
        }

        if (Check(TokenType.LESS))
            return ParseJsxElement();

        throw new ParseError("JSX attribute value must be a string, expression, or element.", "TS17000");
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
        List<Expr> children)
    {
        return _jsx!.Mode == JsxMode.React
            ? LowerJsxClassic(open, isFragment, tagName, tagExpression, attributes, children)
            : LowerJsxAutomatic(open, isFragment, tagName, tagExpression, attributes, children);
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
        List<Expr> children)
    {
        bool isIntrinsic = !isFragment && tagExpression is Expr.Literal;
        Expr tag = isFragment ? BuildDottedExpr(_jsx!.FragmentFactory, open.Line) : tagExpression;

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
            BuildDottedExpr(_jsx!.Factory, open.Line),
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
                _jsx.Mode,
                open.Line),
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
        List<Expr> children)
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
                open.Line),
        };
    }

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
