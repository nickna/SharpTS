using SharpTS.Diagnostics;

namespace SharpTS.Parsing;

/// <summary>
/// Decorator specification mode - determines how decorators are parsed and validated.
/// </summary>
public enum DecoratorMode
{
    /// <summary>No decorator support. Use --noDecorators flag to disable.</summary>
    None,
    /// <summary>Legacy/Experimental TypeScript decorators (Stage 2). Use --experimentalDecorators flag.</summary>
    Legacy,
    /// <summary>TC39 Stage 3 decorators (ES2023+). This is the default.</summary>
    Stage3
}

/// <summary>
/// Recursive descent parser that builds an AST from tokens.
/// </summary>
/// <remarks>
/// Second stage of the compiler pipeline. Consumes the token stream from <see cref="Lexer"/>
/// and produces an Abstract Syntax Tree of <see cref="Stmt"/> and <see cref="Expr"/> nodes
/// (defined in AST.cs). Performs syntax-directed desugaring (e.g., expanding destructuring
/// patterns). The resulting AST is validated by
/// <see cref="TypeChecker"/> and then executed by <see cref="Interpreter"/> or compiled
/// by <see cref="ILCompiler"/>.
/// </remarks>
/// <seealso cref="Lexer"/>
/// <seealso cref="Stmt"/>
/// <seealso cref="Expr"/>
public partial class Parser(List<Token> tokens, DecoratorMode decoratorMode = DecoratorMode.None)
{
    private readonly List<Token> _tokens = tokens;
    private readonly DecoratorMode _decoratorMode = decoratorMode;
    private int _current = 0;
    private int _tempVarCounter = 0;
    private int _ambientNamespaceDepth = 0;
    private int _ambientClassDepth = 0;
    private bool _isDeclarationFile = false;

    // Strict mode tracking - tracks whether we're in a strict mode context
    private bool _isStrictMode = false;

    // Error recovery support
    private readonly DiagnosticCollector _diagnostics = new();
    private string? _filePath = null;

    /// <summary>
    /// Sets the file path for source location reporting.
    /// </summary>
    public Parser WithFilePath(string? filePath)
    {
        _filePath = filePath;
        return this;
    }

    public Parser AsDeclarationFile(bool isDeclarationFile = true)
    {
        _isDeclarationFile = isDeclarationFile;
        return this;
    }

    // JSX/TSX dialect state. Non-null _jsx switches the parser into the TSX dialect:
    // '<' at expression start commits to JSX and angle-bracket assertions are rejected.
    // The source text is required so JSX text runs and attribute strings can be scanned
    // faithfully (the upfront lexer applies TS string/comment rules that corrupt them).
    private JsxParseOptions? _jsx = null;
    private string? _source = null;

    /// <summary>
    /// Enables the TSX dialect for this parse. Call only for .tsx/.jsx sources.
    /// </summary>
    /// <param name="source">The full source text the token stream was lexed from.</param>
    /// <param name="options">Resolved jsx settings (mode, factories, import source).</param>
    public Parser WithJsx(string source, JsxParseOptions options)
    {
        _source = source;
        _jsx = options;
        return this;
    }

    // Internal pattern representation for destructuring (not AST nodes)
    private abstract record DestructurePattern;
    private record ArrayPattern(List<ArrayPatternElement> Elements, int Line) : DestructurePattern;
    private record ObjectPattern(List<ObjectPatternProperty> Properties, int Line) : DestructurePattern;
    private record IdentifierPattern(Token Name, Expr? DefaultValue) : DestructurePattern;
    private record RestPattern(Token Name) : DestructurePattern;

    private record ArrayPatternElement(DestructurePattern? Pattern, bool IsHole);
    private record ObjectPatternProperty(Token Key, DestructurePattern Value, Expr? DefaultValue);

    private Token GenerateTempVar(int line) =>
        new Token(TokenType.IDENTIFIER, $"_dest{_tempVarCounter++}", null, line);

    /// <summary>
    /// Parses the token stream with error recovery, collecting multiple errors.
    /// </summary>
    /// <returns>A ParseDiagnosticResult containing parsed statements and any errors encountered.</returns>
    public ParseDiagnosticResult Parse()
    {
        List<Stmt> statements = [];

        // Parse directive prologue at the start of the file
        var directives = ParseDirectivePrologue();
        statements.AddRange(directives);

        // Check if "use strict" directive is present at file level
        foreach (var directive in directives)
        {
            if (directive.Value == "use strict")
            {
                _isStrictMode = true;
                break;
            }
        }

        while (!IsAtEnd())
        {
            try
            {
                var decl = Declaration();
                if (decl != null) statements.Add(decl);
            }
            catch (Exception ex)
            {
                RecordError(ex.Message, (ex as ParseError)?.TsCode);
                Synchronize();
                if (_diagnostics.HitErrorLimit)
                    return new ParseDiagnosticResult(statements, _diagnostics.Diagnostics, HitErrorLimit: true);
            }
        }

        // Apply var hoisting to the top-level (module/script) statement list. Function bodies
        // and arrow function bodies are hoisted at their respective parse sites.
        statements = VarHoister.Hoist(statements);

        // Lift generator function expressions to top-level function declarations so the
        // existing generator-declaration IL pipeline handles them. No-op when the module
        // contains no `function*() {...}` expressions.
        statements = GeneratorArrowLifter.Lift(statements);

        return new ParseDiagnosticResult(statements, _diagnostics.Diagnostics);
    }

    /// <summary>
    /// Parses directive prologue statements at the beginning of a script or function body.
    /// Directives are string literal expression statements (e.g., "use strict";).
    /// </summary>
    /// <returns>List of parsed directive statements.</returns>
    private List<Stmt.Directive> ParseDirectivePrologue()
    {
        var directives = new List<Stmt.Directive>();

        // Directives must be string literals followed by semicolons at the beginning
        while (Check(TokenType.STRING))
        {
            int saved = _current;
            Token stringToken = Advance();

            // Must be followed by semicolon (or end of input for single-statement case)
            if (!Match(TokenType.SEMICOLON))
            {
                // Not a directive - restore position and stop
                _current = saved;
                break;
            }

            // It's a valid directive
            string directiveValue = (string)stringToken.Literal!;
            directives.Add(new Stmt.Directive(directiveValue, stringToken));
        }

        return directives;
    }

    /// <summary>
    /// Parses the token stream, throwing on the first error (legacy behavior).
    /// </summary>
    /// <returns>The list of parsed statements.</returns>
    /// <exception cref="Exception">Thrown when a parse error is encountered.</exception>
    public List<Stmt> ParseOrThrow()
    {
        var result = Parse();
        if (!result.IsSuccess) throw new Exception(result.Diagnostics.First().ToString());
        return result.Statements;
    }

    /// <summary>
    /// Records a parse error at the current token position, carrying the canonical
    /// TypeScript code when the failure has one (see <see cref="ParseError"/>).
    /// </summary>
    private void RecordError(string message, string? tsCode = null)
    {
        Token current = IsAtEnd() ? Previous() : Peek();
        var location = new SourceLocation(_filePath, current.Line);
        _diagnostics.AddError(DiagnosticCode.ParseError, message, location, tsCode);
    }

    /// <summary>
    /// Synchronizes the parser state after an error by advancing to a safe recovery point.
    /// Recovery points are: after a semicolon, or at a keyword that starts a new declaration/statement.
    /// </summary>
    private void Synchronize()
    {
        while (!IsAtEnd())
        {
            // Check if we're already at a token that starts a new statement/declaration
            // This handles the case where the error was detected AT a keyword
            switch (Peek().Type)
            {
                case TokenType.CLASS:
                case TokenType.FUNCTION:
                case TokenType.INTERFACE:
                case TokenType.LET:
                case TokenType.CONST:
                case TokenType.VAR:
                case TokenType.IMPORT:
                case TokenType.EXPORT:
                case TokenType.TYPE:
                case TokenType.ENUM:
                case TokenType.NAMESPACE:
                case TokenType.IF:
                case TokenType.FOR:
                case TokenType.WHILE:
                case TokenType.DO:
                case TokenType.SWITCH:
                case TokenType.TRY:
                case TokenType.RETURN:
                case TokenType.THROW:
                    return;
                case TokenType.RIGHT_BRACE:
                    // At a closing brace - let the caller handle block termination
                    return;
            }

            Advance();

            // Check if we just passed a semicolon (statement boundary)
            if (Previous().Type == TokenType.SEMICOLON) return;
        }
    }

    // ============== TOKEN NAVIGATION ==============

    private bool Match(params TokenType[] types)
    {
        foreach (TokenType type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new Exception(message);
    }

    /// <summary>
    /// TypeScript-only contextual keywords that are valid identifiers in JavaScript.
    /// These tokens should be accepted anywhere a variable/parameter name is expected.
    /// </summary>
    private static bool IsContextualKeyword(TokenType type) => type switch
    {
        // TypeScript-only keywords that are valid identifiers in JavaScript
        TokenType.TYPE or TokenType.MODULE or TokenType.NAMESPACE or TokenType.ASYNC or
        TokenType.DECLARE or TokenType.ABSTRACT or TokenType.READONLY or
        TokenType.OVERRIDE or TokenType.GLOBAL or TokenType.OF or
        TokenType.FROM or TokenType.SATISFIES or TokenType.ACCESSOR or
        TokenType.OUT or TokenType.UNIQUE or TokenType.UNKNOWN or
        TokenType.NEVER or TokenType.INFER or TokenType.KEYOF or
        TokenType.ASSERTS or TokenType.IS or
        // TypeScript primitive-type keywords are contextual — valid
        // identifiers outside of type-position (e.g. `function f(string) {}`,
        // `var number = 1`, `{ namespace: name } = obj`).
        TokenType.TYPE_STRING or TokenType.TYPE_NUMBER or
        TokenType.TYPE_BOOLEAN or TokenType.TYPE_SYMBOL or
        TokenType.TYPE_BIGINT or
        // `get` and `set` are contextual keywords (only meaningful at
        // class/object member positions). Freely usable as identifiers
        // elsewhere — e.g. `function setToArray(set) {}` in lodash.
        TokenType.GET or TokenType.SET or
        // JavaScript globals that are not reserved words (can be shadowed)
        TokenType.UNDEFINED or TokenType.CONSTRUCTOR or
        TokenType.SYMBOL or TokenType.BIGINT => true,
        _ => false,
    };

    /// <summary>
    /// Returns true if the token type opens a class method signature
    /// (<c>(</c>, <c>&lt;</c>) or a class field declaration
    /// (<c>:</c>, <c>=</c>, <c>;</c>, <c>?</c>, <c>!</c>). Used to
    /// disambiguate <c>get</c> / <c>set</c> as regular method/field names
    /// versus accessor keywords in class bodies.
    /// </summary>
    private static bool IsMethodOrFieldOpener(TokenType type) => type switch
    {
        TokenType.LEFT_PAREN => true,   // get() { ... }  — method
        TokenType.LESS => true,          // get<T>() { ... } — generic method
        TokenType.COLON => true,         // get: T — field
        TokenType.EQUAL => true,         // get = 1 — field
        TokenType.SEMICOLON => true,     // get; — field
        TokenType.QUESTION => true,      // get?: T — optional field
        TokenType.BANG => true,          // get!: T — definite-assign field
        _ => false,
    };

    /// <summary>
    /// Returns true if the token type can begin an object/class property name:
    /// IDENTIFIER, STRING, NUMBER, LEFT_BRACKET (computed), or any keyword usable
    /// as a property name. Used to disambiguate accessor shorthand
    /// (<c>get foo() {}</c>, <c>get "x"() {}</c>, <c>get 3() {}</c>,
    /// <c>get [sym]() {}</c>) from a property literally named <c>get</c>/<c>set</c>.
    /// </summary>
    private static bool IsPropertyNameStart(TokenType type) => type switch
    {
        TokenType.IDENTIFIER => true,
        TokenType.STRING => true,
        TokenType.NUMBER => true,
        TokenType.LEFT_BRACKET => true,
        _ => IsKeyword(type) || IsContextualKeyword(type),
    };

    /// <summary>
    /// Token types that, following an identifier in a class body, mark
    /// the declaration as a field (not a method). Includes `;` (bare ES
    /// class field), `=` (initialized field), `:` (typed field), `?`/`!`
    /// (optional / definite-assign) and `}` (last field in class body
    /// with ASI terminating `name`).
    /// </summary>
    private static bool IsFieldDeclarationOpener(TokenType type) => type switch
    {
        TokenType.COLON or TokenType.QUESTION or TokenType.BANG or
        TokenType.EQUAL or TokenType.SEMICOLON or
        TokenType.RIGHT_BRACE => true,
        _ => false,
    };

    /// <summary>
    /// Consumes a token that can be used as a variable or parameter name.
    /// Accepts identifiers and TypeScript contextual keywords (which are valid JS identifiers).
    /// </summary>
    private Token ConsumeIdentifierName(string message)
    {
        if (Check(TokenType.IDENTIFIER)) return Advance();

        if (IsContextualKeyword(Peek().Type))
        {
            var token = Advance();
            return new Token(TokenType.IDENTIFIER, token.Lexeme, null, token.Line);
        }

        throw new Exception(message);
    }

    /// <summary>
    /// Consumes a token that can be used as a property name after '.'.
    /// This includes identifiers and reserved keywords (JavaScript allows keywords as property names).
    /// </summary>
    /// <summary>
    /// Consumes a property name that may be an identifier, a keyword (e.g. <c>type</c>, <c>set</c>),
    /// or a string/numeric literal (e.g. <c>"1"</c>, <c>1</c>), returning it as an identifier-like token.
    /// </summary>
    private Token ConsumePropertyNameOrLiteral(string message)
    {
        if (Check(TokenType.STRING))
        {
            var lit = Advance();
            return new Token(TokenType.IDENTIFIER, (string)lit.Literal!, null, lit.Line);
        }
        if (Check(TokenType.NUMBER))
        {
            var lit = Advance();
            return new Token(TokenType.IDENTIFIER, lit.Literal!.ToString()!, null, lit.Line);
        }
        return ConsumePropertyName(message);
    }

    /// <summary>
    /// Parses an accessor name in a class body: an identifier/keyword/string/number
    /// property name, or a computed name <c>[expr]</c> (e.g. <c>get [Symbol.species]()</c>).
    /// For computed names the returned token is synthetic and the key expression is
    /// carried separately (mirrors Stmt.Field.ComputedKey).
    /// </summary>
    private (Token Name, Expr? ComputedKey) ParseAccessorName()
    {
        if (Match(TokenType.LEFT_BRACKET))
        {
            Expr computedKey = Expression();
            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed accessor name.");

            // Literal string/number keys are static names — fold them so every
            // phase (checker, interpreter, compiler) treats `get ["foo"]()`
            // exactly like `get foo()`.
            if (computedKey is Expr.Literal { Value: string s })
            {
                return (new Token(TokenType.IDENTIFIER, s, null, Previous().Line), null);
            }
            if (computedKey is Expr.Literal { Value: double d })
            {
                return (new Token(TokenType.IDENTIFIER, d.ToString(System.Globalization.CultureInfo.InvariantCulture), null, Previous().Line), null);
            }

            return (new Token(TokenType.IDENTIFIER, "<computed>", null, Previous().Line), computedKey);
        }
        return (ConsumePropertyNameOrLiteral("Expect property name after 'get'/'set'."), null);
    }

    private Token ConsumePropertyName(string message)
    {
        Token current = Peek();

        // Accept any identifier
        if (current.Type == TokenType.IDENTIFIER)
            return Advance();

        // Accept keywords that can be used as property names
        // In JavaScript/TypeScript, all keywords are valid property names
        if (IsKeyword(current.Type) || IsContextualKeyword(current.Type))
        {
            Advance();
            // Convert keyword token to identifier token for AST consistency
            return new Token(TokenType.IDENTIFIER, current.Lexeme, null, current.Line);
        }

        throw new Exception(message);
    }

    /// <summary>
    /// Checks if a token type is a keyword that can be used as a property name.
    /// </summary>
    private static bool IsKeyword(TokenType type)
    {
        return type switch
        {
            TokenType.ABSTRACT or TokenType.AS or TokenType.ASYNC or TokenType.AWAIT or
            TokenType.BREAK or TokenType.CASE or TokenType.CATCH or TokenType.CLASS or
            TokenType.CONST or TokenType.CONSTRUCTOR or TokenType.CONTINUE or
            TokenType.DEFAULT or TokenType.DELETE or TokenType.DO or TokenType.ELSE or
            TokenType.ENUM or TokenType.EXPORT or TokenType.EXTENDS or TokenType.FALSE or
            TokenType.FINALLY or TokenType.FOR or TokenType.FROM or TokenType.FUNCTION or
            TokenType.GET or TokenType.IF or TokenType.IMPLEMENTS or TokenType.IMPORT or
            TokenType.IN or TokenType.INSTANCEOF or TokenType.INTERFACE or TokenType.IS or TokenType.LET or
            TokenType.NEVER or TokenType.NEW or TokenType.NULL or TokenType.OF or TokenType.OVERRIDE or
            TokenType.PRIVATE or TokenType.PROTECTED or TokenType.PUBLIC or TokenType.READONLY or
            TokenType.RETURN or TokenType.SET or TokenType.STATIC or TokenType.SUPER or
            TokenType.SWITCH or TokenType.THIS or TokenType.THROW or TokenType.TRUE or
            TokenType.SATISFIES or
            TokenType.TRY or TokenType.TYPE or TokenType.TYPEOF or TokenType.UNDEFINED or
            TokenType.UNKNOWN or TokenType.USING or TokenType.VAR or TokenType.VOID or
            TokenType.WHILE or TokenType.YIELD => true,
            _ => false
        };
    }

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool CheckNext(TokenType type) => PeekNext().Type == type;

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd() => Peek().Type == TokenType.EOF;

    private Token Peek() => _tokens[_current];

    private Token PeekNext()
    {
        if (_current + 1 >= _tokens.Count) return _tokens.Last();
        return _tokens[_current + 1];
    }

    private Token PeekAt(int offset)
    {
        int i = _current + offset;
        return i >= _tokens.Count ? _tokens[^1] : _tokens[i];
    }

    private Token Previous() => _tokens[_current - 1];

    // ============== AUTOMATIC SEMICOLON INSERTION (ASI) ==============

    /// <summary>
    /// Consumes a semicolon with Automatic Semicolon Insertion (ASI) support.
    /// Succeeds if:
    ///   1. The current token IS a semicolon (consumed and advanced), OR
    ///   2. There is a line terminator between the previous and current token, OR
    ///   3. The current token is '}', OR
    ///   4. The current token is EOF.
    /// In cases 2-4, no token is consumed — the semicolon is "virtually inserted."
    /// </summary>
    private void ConsumeSemicolon(string message)
    {
        if (Match(TokenType.SEMICOLON)) return;
        if (Previous().Line < Peek().Line) return;
        if (Check(TokenType.RIGHT_BRACE)) return;
        if (IsAtEnd()) return;
        throw new Exception(message);
    }

    /// <summary>
    /// Returns true if there is a line terminator between the previous token
    /// and the current token. Used to enforce "no LineTerminator here" restrictions.
    /// </summary>
    private bool HasLineTerminatorBeforeCurrent()
    {
        return Previous().Line < Peek().Line;
    }

    /// <summary>
    /// Consumes an interface member separator: semicolon, comma, or ASI (newline/}/EOF).
    /// TypeScript interface members accept both ';' and ',' as explicit separators.
    /// </summary>
    private void ConsumeInterfaceMemberSeparator()
    {
        if (Match(TokenType.SEMICOLON)) return;
        if (Match(TokenType.COMMA)) return;
        if (Previous().Line < Peek().Line) return;
        if (Check(TokenType.RIGHT_BRACE)) return;
        if (IsAtEnd()) return;
        throw new Exception("Expect ';' or ',' after interface member.");
    }

    private Expr? TryParseAngleBracketAssertion()
    {
        int saved = _current;
        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            string typeName = ParseTypeAnnotation();
            if (!Check(TokenType.GREATER)) { _current = saved; return null; }
            Advance(); // consume >

            Expr expression = Unary();
            return new Expr.TypeAssertion(expression, typeName);
        }
        catch { _current = saved; return null; }
    }

    private bool IsTypeStart() =>
        Check(TokenType.IDENTIFIER) ||
        Check(TokenType.TYPE_STRING) ||
        Check(TokenType.TYPE_NUMBER) ||
        Check(TokenType.TYPE_BOOLEAN) ||
        Check(TokenType.UNKNOWN) ||
        Check(TokenType.NEVER) ||
        Check(TokenType.NULL) ||
        Check(TokenType.UNDEFINED) ||
        Check(TokenType.VOID) ||  // void type
        Check(TokenType.LEFT_PAREN) ||
        Check(TokenType.LEFT_BRACE) ||  // for inline object types: { x: number }
        Check(TokenType.LEFT_BRACKET) ||  // for tuple types: [string, number]
        Check(TokenType.INFER) ||  // for conditional type infer patterns
        Check(TokenType.STRING) ||  // for string literal types: "hello" | "world"
        Check(TokenType.NUMBER) ||  // for number literal types: 1 | 2 | 3
        Check(TokenType.TRUE) ||  // for boolean literal type true
        Check(TokenType.FALSE) ||  // for boolean literal type false
        Check(TokenType.TEMPLATE_FULL) ||  // for template literal types: `literal`
        Check(TokenType.TEMPLATE_HEAD) ||  // for template literal types: `prefix${
        Check(TokenType.TYPEOF) ||  // for typeof in type position: typeof someVar
        Check(TokenType.KEYOF) ||  // for keyof operator: keyof T
        Check(TokenType.READONLY) ||  // readonly array/tuple modifier: readonly T[], readonly [A, B]
        Check(TokenType.TYPE_SYMBOL) || Check(TokenType.TYPE_BIGINT) ||  // symbol / bigint primitive types
        Check(TokenType.SYMBOL) || Check(TokenType.BIGINT) ||  // `Symbol` / `BigInt` as type names
        Check(TokenType.BIGINT_LITERAL) ||  // bigint literal types: 1n | 2n
        Check(TokenType.NEW) ||  // constructor types: new (x) => T (e.g. inside InstanceType<...>)
        Check(TokenType.ABSTRACT) ||  // abstract constructor types: abstract new (x) => T
        Check(TokenType.LESS) ||  // generic function types: <T>(x: T) => T as a type argument
        Check(TokenType.THIS);  // polymorphic `this` type

    // ============== GENERIC TYPE CLOSING BRACKET HANDLING ==============
    //
    // The lexer tokenizes >> as GREATER_GREATER (right-shift operator) and >>> as
    // GREATER_GREATER_GREATER (unsigned right-shift). This causes issues with nested
    // generic types like Partial<Readonly<T>> where the >> should be two separate > tokens.
    //
    // Solution: When parsing type contexts, we handle compound greater-than tokens specially.
    // If we need a single > but have >> or >>>, we "split" the token by replacing it with
    // the remainder (>> becomes >, >>> becomes >>). This follows the approach used by
    // C#, Java, and TypeScript compilers.

    /// <summary>
    /// Checks if the current token can provide a closing '>' for generic type syntax.
    /// Returns true for GREATER, GREATER_GREATER, or GREATER_GREATER_GREATER.
    /// </summary>
    private bool CheckGreaterInTypeContext()
    {
        if (IsAtEnd()) return false;
        return Peek().Type is TokenType.GREATER
            or TokenType.GREATER_GREATER
            or TokenType.GREATER_GREATER_GREATER;
    }

    /// <summary>
    /// Consumes a single '>' from the current token in a type context.
    /// For GREATER, advances normally. For GREATER_GREATER or GREATER_GREATER_GREATER,
    /// splits the token by replacing it with the remainder.
    /// </summary>
    /// <returns>True if a '>' was consumed, false otherwise.</returns>
    private bool MatchGreaterInTypeContext()
    {
        if (IsAtEnd()) return false;

        Token current = Peek();
        switch (current.Type)
        {
            case TokenType.GREATER:
                Advance();
                return true;

            case TokenType.GREATER_GREATER:
                // Split >> into > (consumed) and > (remaining)
                _tokens[_current] = new Token(TokenType.GREATER, ">", null, current.Line);
                return true;

            case TokenType.GREATER_GREATER_GREATER:
                // Split >>> into > (consumed) and >> (remaining)
                _tokens[_current] = new Token(TokenType.GREATER_GREATER, ">>", null, current.Line);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Consumes a '>' in type context or throws an exception with the given message.
    /// Handles compound tokens (>> and >>>) by splitting them.
    /// </summary>
    private void ConsumeGreaterInTypeContext(string message)
    {
        if (!MatchGreaterInTypeContext())
            throw new Exception(message);
    }
}
