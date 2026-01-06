namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== NAMESPACE PARSING ==============

    /// <summary>
    /// Parses a namespace declaration: namespace Name { ... }
    /// Supports dotted names (A.B.C) which are desugared to nested namespaces.
    /// </summary>
    /// <param name="isExported">Whether this is an exported namespace</param>
    private Stmt NamespaceDeclaration(bool isExported = false)
    {
        // Parse namespace name (may be dotted: A.B.C)
        Token firstName = Consume(TokenType.IDENTIFIER, "Expect namespace name.");
        List<Token> nameParts = [firstName];

        // Collect dotted parts: A.B.C
        while (Match(TokenType.DOT))
        {
            Token part = Consume(TokenType.IDENTIFIER, "Expect identifier after '.' in namespace name.");
            nameParts.Add(part);
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before namespace body.");

        // Parse namespace members
        List<Stmt> members = [];
        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            members.Add(NamespaceMember());
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

        // Parse decorators for class declarations
        List<Decorator>? decorators = ParseDecorators();

        if (Match(TokenType.NAMESPACE))
        {
            return WrapIfExported(NamespaceDeclaration(), isExported);
        }
        if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            return WrapIfExported(ClassDeclaration(isAbstract: true, classDecorators: decorators), isExported);
        }
        if (Match(TokenType.CLASS))
        {
            return WrapIfExported(ClassDeclaration(isAbstract: false, classDecorators: decorators), isExported);
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
            return WrapIfExported(VarDeclaration(), isExported);
        }
        if (Match(TokenType.ENUM))
        {
            return WrapIfExported(EnumDeclaration(isConst: false), isExported);
        }
        if (Match(TokenType.ASYNC))
        {
            Consume(TokenType.FUNCTION, "Expect 'function' after 'async'.");
            return WrapIfExported(FunctionDeclaration("function", isAsync: true), isExported);
        }
        if (Match(TokenType.FUNCTION))
        {
            bool isGenerator = Match(TokenType.STAR);
            return WrapIfExported(FunctionDeclaration("function", isAsync: false, isGenerator: isGenerator), isExported);
        }
        if (Match(TokenType.LET))
        {
            return WrapIfExported(VarDeclaration(), isExported);
        }

        throw new Exception($"Parse Error at line {Peek().Line}: Unexpected token in namespace body: {Peek().Lexeme}");
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
