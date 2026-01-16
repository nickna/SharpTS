namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt Declaration()
    {
        // Module declarations - must be at top level
        // Note: import followed by ( is dynamic import (expression), not static import (statement)
        if (Check(TokenType.IMPORT) && PeekNext().Type != TokenType.LEFT_PAREN)
        {
            Advance(); // consume IMPORT
            // Detect import alias: import X = Namespace.Member
            // Pattern: IDENTIFIER EQUAL (after consuming IMPORT)
            if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.EQUAL)
            {
                return ImportAliasDeclaration(isExported: false);
            }
            return ImportDeclaration();
        }
        if (Match(TokenType.EXPORT)) return ExportDeclaration();

        // Parse decorators before class declarations
        List<Decorator>? decorators = ParseDecorators();

        // Check for file-level @Namespace decorator (must appear before any class)
        if (decorators != null && decorators.Count > 0 && IsNamespaceDecorator(decorators[0]))
        {
            return new Stmt.FileDirective(decorators);
        }

        if (Match(TokenType.DECLARE))
        {
            // declare class is for ambient declarations (external types)
            if (Match(TokenType.ABSTRACT))
            {
                Consume(TokenType.CLASS, "Expect 'class' after 'declare abstract'.");
                return ClassDeclaration(isAbstract: true, classDecorators: decorators, isDeclare: true);
            }
            Consume(TokenType.CLASS, "Expect 'class' after 'declare'.");
            return ClassDeclaration(isAbstract: false, classDecorators: decorators, isDeclare: true);
        }
        if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            return ClassDeclaration(isAbstract: true, classDecorators: decorators);
        }
        if (Match(TokenType.CLASS)) return ClassDeclaration(isAbstract: false, classDecorators: decorators);

        // If decorators were found but next token is not a class, report error
        if (decorators != null && decorators.Count > 0)
        {
            throw new Exception($"Parse Error at line {decorators[0].AtToken.Line}: Decorators are not valid here. Decorators can only be applied to classes and class members.");
        }
        if (Match(TokenType.CONST))
        {
            // Check for const enum
            if (Match(TokenType.ENUM)) return EnumDeclaration(isConst: true);
            // Otherwise it's a const variable declaration
            return VarDeclaration(isConst: true);
        }
        if (Match(TokenType.ENUM)) return EnumDeclaration(isConst: false);
        if (Match(TokenType.NAMESPACE)) return NamespaceDeclaration();
        if (Match(TokenType.INTERFACE)) return InterfaceDeclaration();
        if (Match(TokenType.TYPE)) return TypeAliasDeclaration();
        if (Match(TokenType.ASYNC))
        {
            Consume(TokenType.FUNCTION, "Expect 'function' after 'async'.");
            // Check for async generator: async function* name() {}
            bool isGenerator = Match(TokenType.STAR);
            return FunctionDeclaration("function", isAsync: true, isGenerator: isGenerator);
        }
        if (Match(TokenType.FUNCTION))
        {
            // Check for generator function: function* name() {}
            bool isGenerator = Match(TokenType.STAR);
            return FunctionDeclaration("function", isAsync: false, isGenerator: isGenerator);
        }
        if (Match(TokenType.LET)) return VarDeclaration();
        return Statement();
    }

    private Stmt TypeAliasDeclaration()
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect type alias name.");

        // Parse optional generic type parameters: type Foo<T, U extends Base> = ...
        List<TypeParam>? typeParams = ParseTypeParameters();

        Consume(TokenType.EQUAL, "Expect '=' after type alias name.");

        // ParseTypeAnnotation handles all cases including:
        // - Function types: (params) => returnType
        // - Grouped types: (A & B) | C
        // - Union types: A | B
        // - Intersection types: A & B
        // - Conditional types: T extends U ? X : Y
        // The disambiguation is done in ParsePrimaryType
        string typeDef = ParseTypeAnnotation();

        Consume(TokenType.SEMICOLON, "Expect ';' after type alias.");
        return new Stmt.TypeAlias(name, typeDef, typeParams);
    }

    private string ParseFunctionTypeDefinition()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' for function type.");
        List<string> paramTypes = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Parameter can be: name: type or just type
                if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.COLON)
                {
                    Advance(); // skip name
                    Consume(TokenType.COLON, "");
                }
                paramTypes.Add(ParseTypeAnnotation());
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_PAREN, "Expect ')' after function type parameters.");
        Consume(TokenType.ARROW, "Expect '=>' after function type parameters.");
        string returnType = ParseTypeAnnotation();

        return $"({string.Join(", ", paramTypes)}) => {returnType}";
    }

    private Stmt InterfaceDeclaration()
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect interface name.");
        List<TypeParam>? typeParams = ParseTypeParameters();

        // Parse extends clause: interface Foo extends Bar, Baz { ... }
        List<string>? extends = null;
        if (Match(TokenType.EXTENDS))
        {
            extends = [];
            do
            {
                extends.Add(ParseTypeAnnotation());
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before interface body.");

        List<Stmt.InterfaceMember> members = [];
        List<Stmt.IndexSignature> indexSignatures = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Check for index signature: [key: string]: valueType
            if (Check(TokenType.LEFT_BRACKET))
            {
                var indexSig = TryParseIndexSignature();
                if (indexSig != null)
                {
                    indexSignatures.Add(indexSig);
                    continue;
                }
            }

            Token memberName = Consume(TokenType.IDENTIFIER, "Expect member name.");
            bool isOptional = Match(TokenType.QUESTION);

            string type;
            if (Check(TokenType.LEFT_PAREN))
            {
                // Method signature: methodName(params): returnType
                type = ParseMethodSignature();
            }
            else
            {
                // Property: name: type
                Consume(TokenType.COLON, "Expect ':' after member name.");
                type = ParseTypeAnnotation();
            }

            Consume(TokenType.SEMICOLON, "Expect ';' after member declaration.");
            members.Add(new Stmt.InterfaceMember(memberName, type, isOptional));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after interface body.");
        return new Stmt.Interface(name, typeParams, members, indexSignatures.Count > 0 ? indexSignatures : null, extends);
    }

    /// <summary>
    /// Tries to parse an index signature: [key: string]: valueType; or [key: number]: valueType; or [key: symbol]: valueType;
    /// Returns null if it's not an index signature pattern.
    /// </summary>
    private Stmt.IndexSignature? TryParseIndexSignature()
    {
        if (!Check(TokenType.LEFT_BRACKET)) return null;

        int savedPosition = _current;

        Advance(); // consume [

        if (!Check(TokenType.IDENTIFIER))
        {
            _current = savedPosition;
            return null;
        }
        Token keyName = Advance();

        if (!Match(TokenType.COLON))
        {
            _current = savedPosition;
            return null;
        }

        // Check for string, number, or symbol key type
        TokenType keyType;
        if (Check(TokenType.TYPE_STRING))
        {
            keyType = TokenType.TYPE_STRING;
            Advance();
        }
        else if (Check(TokenType.TYPE_NUMBER))
        {
            keyType = TokenType.TYPE_NUMBER;
            Advance();
        }
        else if (Check(TokenType.TYPE_SYMBOL))
        {
            keyType = TokenType.TYPE_SYMBOL;
            Advance();
        }
        else
        {
            _current = savedPosition;
            return null;
        }

        if (!Match(TokenType.RIGHT_BRACKET))
        {
            _current = savedPosition;
            return null;
        }

        if (!Match(TokenType.COLON))
        {
            _current = savedPosition;
            return null;
        }

        string valueType = ParseTypeAnnotation();
        Consume(TokenType.SEMICOLON, "Expect ';' after index signature.");

        return new Stmt.IndexSignature(keyName, keyType, valueType);
    }

    /// <summary>
    /// Parses a method signature like "(a: number, b: string): returnType" and returns it as a function type string.
    /// Supports 'this' parameter: "(this: Type, a: number): returnType".
    /// </summary>
    private string ParseMethodSignature()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' for method parameters.");
        string? thisType = null;
        List<string> paramTypes = [];

        // Check for 'this' parameter in interface method
        if (Check(TokenType.THIS))
        {
            Advance(); // consume 'this'
            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
            thisType = ParseTypeAnnotation();
            if (Check(TokenType.COMMA))
            {
                Advance(); // consume ','
            }
        }

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                if (Match(TokenType.QUESTION))
                {
                    // Optional parameter marker
                }
                Consume(TokenType.COLON, "Expect ':' after parameter name.");
                string paramType = ParseTypeAnnotation();
                paramTypes.Add(paramType);
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");
        Consume(TokenType.COLON, "Expect ':' before return type.");
        string returnType = ParseTypeAnnotation();

        if (thisType != null)
        {
            return $"(this: {thisType}, {string.Join(", ", paramTypes)}) => {returnType}";
        }
        return $"({string.Join(", ", paramTypes)}) => {returnType}";
    }

    private Stmt EnumDeclaration(bool isConst = false)
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect enum name.");
        Consume(TokenType.LEFT_BRACE, "Expect '{' before enum body.");

        List<Stmt.EnumMember> members = [];
        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            Token memberName = Consume(TokenType.IDENTIFIER, "Expect member name.");
            Expr? value = Match(TokenType.EQUAL) ? Expression() : null;
            members.Add(new Stmt.EnumMember(memberName, value));

            if (!Check(TokenType.RIGHT_BRACE))
                Match(TokenType.COMMA);
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after enum body.");
        return new Stmt.Enum(name, members, isConst);
    }

    private Stmt VarDeclaration(bool isConst = false)
    {
        // Check for destructuring patterns
        if (Check(TokenType.LEFT_BRACKET))
            return DestructureArrayDeclaration();
        if (Check(TokenType.LEFT_BRACE))
            return DestructureObjectDeclaration();

        // Standard single-variable declaration
        Token name = Consume(TokenType.IDENTIFIER, "Expect variable name.");

        string? typeAnnotation = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
        }

        Expr? initializer = null;
        if (Match(TokenType.EQUAL))
        {
            initializer = Expression();
        }

        // const declarations require an initializer
        if (isConst && initializer == null)
        {
            throw new Exception($"Parse Error at line {name.Line}: 'const' declarations must be initialized.");
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");
        return new Stmt.Var(name, typeAnnotation, initializer);
    }

    /// <summary>
    /// Checks if a decorator is the file-level @Namespace decorator.
    /// </summary>
    private bool IsNamespaceDecorator(Decorator decorator)
    {
        return decorator.Expression is Expr.Call call &&
               call.Callee is Expr.Variable v &&
               v.Name.Lexeme == "Namespace";
    }
}
