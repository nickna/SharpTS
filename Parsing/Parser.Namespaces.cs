namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== NAMESPACE PARSING ==============

    /// <summary>
    /// Parses a namespace declaration: namespace Name { ... }
    /// Supports dotted names (A.B.C) which are desugared to nested namespaces.
    /// </summary>
    /// <param name="isExported">Whether this is an exported namespace</param>
    private Stmt NamespaceDeclaration(bool isExported = false, bool isAmbient = false)
    {
        isAmbient |= _isDeclarationFile;

        // Parse namespace name (may be dotted: A.B.C). Contextual keywords (e.g. `Symbol`) are
        // valid identifiers here too — `module Symbol { }` shadows the global.
        Token firstName = ConsumeIdentifierName("Expect namespace name.");
        List<Token> nameParts = [firstName];

        // Collect dotted parts: A.B.C
        while (Match(TokenType.DOT))
        {
            Token part = ConsumeIdentifierName("Expect identifier after '.' in namespace name.");
            nameParts.Add(part);
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before namespace body.");

        // Parse namespace members
        List<Stmt> members = [];
        if (isAmbient) _ambientNamespaceDepth++;
        try
        {
            while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
            {
                members.Add(NamespaceMember());
            }
        }
        finally
        {
            if (isAmbient) _ambientNamespaceDepth--;
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after namespace body.");

        // Desugar dotted names: namespace A.B.C { } becomes namespace A { namespace B { namespace C { } } }
        // Start from the innermost and work outward
        Stmt result = new Stmt.Namespace(nameParts[^1], members, isExported && nameParts.Count == 1);

        for (int i = nameParts.Count - 2; i >= 0; i--)
        {
            // Only the outermost namespace should be marked as exported
            result = new Stmt.Namespace(nameParts[i], [result], isExported && i == 0);
        }

        return result;
    }

    /// <summary>
    /// Parses a member inside a namespace body.
    /// Supports: export modifier, classes, interfaces, functions, variables, enums, type aliases, nested namespaces.
    /// </summary>
    private Stmt NamespaceMember()
    {
        bool isExported = Match(TokenType.EXPORT);
        Token? exportKeyword = isExported ? Previous() : null;

        // Ambient namespaces can explicitly export an alias imported with
        // `import X = Namespace.member` (used by @types/node promisify hooks).
        if (isExported && Match(TokenType.LEFT_BRACE))
        {
            var namedExports = ParseExportSpecifiers();
            string? fromPath = null;
            if (Match(TokenType.FROM))
                fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
            ConsumeSemicolon("Expect ';' after namespace export.");
            return new Stmt.Export(
                exportKeyword!, null, namedExports, null, fromPath, IsDefaultExport: false);
        }

        // Handle import alias inside namespace: [export] import X = Namespace.Member
        if (Match(TokenType.IMPORT))
        {
            if ((Check(TokenType.IDENTIFIER) || IsContextualKeyword(Peek().Type))
                && PeekNext().Type == TokenType.EQUAL)
            {
                return ImportAliasDeclaration(isExported);
            }
            throw new Exception($"Parse Error at line {Previous().Line}: ES6 imports not allowed inside namespaces. Use 'import X = Namespace.Member' syntax.");
        }

        // Parse decorators for class declarations
        List<Decorator>? decorators = ParseDecorators();

        if (Match(TokenType.NAMESPACE))
        {
            return WrapIfExported(
                NamespaceDeclaration(isAmbient: _ambientNamespaceDepth > 0),
                isExported);
        }
        // `module Foo { }` — the older spelling of a nested namespace (identifier name).
        if (Check(TokenType.MODULE) &&
            (PeekNext().Type == TokenType.IDENTIFIER || IsContextualKeyword(PeekNext().Type)))
        {
            Advance(); // consume MODULE
            return WrapIfExported(
                NamespaceDeclaration(isAmbient: _ambientNamespaceDepth > 0),
                isExported);
        }
        // Ambient declarations inside a namespace: `declare class/function/var/...`.
        if (Check(TokenType.DECLARE))
        {
            return WrapIfExported(Declaration(), isExported);
        }
        if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            return WrapIfExported(
                ClassDeclaration(
                    isAbstract: true,
                    classDecorators: decorators,
                    isDeclare: _ambientNamespaceDepth > 0),
                isExported);
        }
        if (Match(TokenType.CLASS))
        {
            return WrapIfExported(
                ClassDeclaration(
                    isAbstract: false,
                    classDecorators: decorators,
                    isDeclare: _ambientNamespaceDepth > 0),
                isExported);
        }

        // If decorators were found but next token is not a class, report error
        if (decorators != null && decorators.Count > 0)
        {
            throw new Exception($"Parse Error at line {decorators[0].AtToken.Line}: Decorators can only be applied to classes and class members.");
        }

        if (Match(TokenType.INTERFACE))
        {
            return WrapIfExported(InterfaceDeclaration(), isExported);
        }
        if (Match(TokenType.TYPE))
        {
            return WrapIfExported(TypeAliasDeclaration(), isExported);
        }
        if (Match(TokenType.CONST))
        {
            if (Match(TokenType.ENUM))
            {
                return WrapIfExported(EnumDeclaration(isConst: true), isExported);
            }
            // A namespace-scoped `const` must parse as Stmt.Const (not the default mutable
            // Stmt.Var) so const-ness — literal-type narrowing and reassignment checks — is
            // preserved, mirroring the module-export path fixed in #428 (#467).
            return WrapIfExported(
                _ambientNamespaceDepth > 0
                    ? AmbientVarDeclaration(isConst: true)
                    : VarDeclaration(isConst: true),
                isExported);
        }
        if (Match(TokenType.ENUM))
        {
            return WrapIfExported(EnumDeclaration(isConst: false), isExported);
        }
        if (Match(TokenType.ASYNC))
        {
            Consume(TokenType.FUNCTION, "Expect 'function' after 'async'.");
            bool isGenerator = Match(TokenType.STAR);
            return WrapIfExported(
                FunctionDeclaration(
                    "function",
                    isAsync: true,
                    isGenerator: isGenerator,
                    isDeclare: _ambientNamespaceDepth > 0),
                isExported);
        }
        if (Match(TokenType.FUNCTION))
        {
            bool isGenerator = Match(TokenType.STAR);
            return WrapIfExported(
                FunctionDeclaration(
                    "function",
                    isAsync: false,
                    isGenerator: isGenerator,
                    isDeclare: _ambientNamespaceDepth > 0),
                isExported);
        }
        if (Match(TokenType.LET))
        {
            return WrapIfExported(
                _ambientNamespaceDepth > 0
                    ? AmbientVarDeclaration(isConst: false)
                    : VarDeclaration(),
                isExported);
        }
        if (Match(TokenType.VAR))
        {
            // `var` must carry IsVar so it participates in var hoisting (self-referential
            // annotations/initializers like `var a: { foo: typeof a }` need the name pre-defined).
            return WrapIfExported(
                _ambientNamespaceDepth > 0
                    ? AmbientVarDeclaration(isConst: false)
                    : VarDeclaration(isConst: false, isVar: true),
                isExported);
        }

        // Namespace bodies may also contain ordinary statements (e.g. `s = t;` expression
        // statements). `export` must be followed by a declaration, so it can't reach here.
        if (isExported)
            throw new Exception($"Parse Error at line {Peek().Line}: 'export' must be followed by a declaration in a namespace body.");
        return Statement();
    }

    /// <summary>
    /// Wraps a declaration in an export statement if needed (for namespace members).
    /// </summary>
    private Stmt WrapIfExported(Stmt declaration, bool isExported)
    {
        if (isExported)
        {
            return new Stmt.Export(
                new Token(TokenType.EXPORT, "export", null, Previous().Line),
                declaration,
                null, null, null, false
            );
        }
        return declaration;
    }
}
