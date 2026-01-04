namespace SharpTS.Parsing;

/// <summary>
/// Recursive descent parser that builds an AST from tokens.
/// </summary>
/// <remarks>
/// Second stage of the compiler pipeline. Consumes the token stream from <see cref="Lexer"/>
/// and produces an Abstract Syntax Tree of <see cref="Stmt"/> and <see cref="Expr"/> nodes
/// (defined in AST.cs). Performs syntax-directed desugaring (e.g., converting for loops to
/// while loops, expanding destructuring patterns). The resulting AST is validated by
/// <see cref="TypeChecker"/> and then executed by <see cref="Interpreter"/> or compiled
/// by <see cref="ILCompiler"/>.
/// </remarks>
/// <seealso cref="Lexer"/>
/// <seealso cref="Stmt"/>
/// <seealso cref="Expr"/>
public class Parser(List<Token> tokens)
{
    private readonly List<Token> _tokens = tokens;
    private int _current = 0;
    private int _tempVarCounter = 0;

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

    public List<Stmt> Parse()
    {
        List<Stmt> statements = [];
        while (!IsAtEnd())
        {
            statements.Add(Declaration());
        }
        return statements;
    }

    private Stmt Declaration()
    {
        if (Match(TokenType.ABSTRACT))
        {
            Consume(TokenType.CLASS, "Expect 'class' after 'abstract'.");
            return ClassDeclaration(isAbstract: true);
        }
        if (Match(TokenType.CLASS)) return ClassDeclaration(isAbstract: false);
        if (Match(TokenType.CONST))
        {
            // Check for const enum
            if (Match(TokenType.ENUM)) return EnumDeclaration(isConst: true);
            // Otherwise it's a const variable declaration
            return VarDeclaration();
        }
        if (Match(TokenType.ENUM)) return EnumDeclaration(isConst: false);
        if (Match(TokenType.INTERFACE)) return InterfaceDeclaration();
        if (Match(TokenType.TYPE)) return TypeAliasDeclaration();
        if (Match(TokenType.FUNCTION)) return FunctionDeclaration("function");
        if (Match(TokenType.LET)) return VarDeclaration();
        return Statement();
    }

    private Stmt TypeAliasDeclaration()
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect type alias name.");
        Consume(TokenType.EQUAL, "Expect '=' after type alias name.");

        // ParseTypeAnnotation handles all cases including:
        // - Function types: (params) => returnType
        // - Grouped types: (A & B) | C
        // - Union types: A | B
        // - Intersection types: A & B
        // The disambiguation is done in ParsePrimaryType
        string typeDef = ParseTypeAnnotation();

        Consume(TokenType.SEMICOLON, "Expect ';' after type alias.");
        return new Stmt.TypeAlias(name, typeDef);
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
        return new Stmt.Interface(name, typeParams, members, indexSignatures.Count > 0 ? indexSignatures : null);
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
    /// </summary>
    private string ParseMethodSignature()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' for method parameters.");
        List<string> paramTypes = [];

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

    private Stmt VarDeclaration()
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

        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");
        return new Stmt.Var(name, typeAnnotation, initializer);
    }

    private string ParseTypeAnnotation()
    {
        return ParseUnionType();
    }

    private string ParseUnionType()
    {
        // Union has lower precedence than intersection, so parse intersection first
        List<string> types = [ParseIntersectionType()];

        while (Match(TokenType.PIPE))
        {
            types.Add(ParseIntersectionType());
        }

        return types.Count == 1 ? types[0] : string.Join(" | ", types);
    }

    /// <summary>
    /// Parses intersection types (A &amp; B). Intersection binds tighter than union,
    /// so A | B &amp; C is parsed as A | (B &amp; C).
    /// </summary>
    private string ParseIntersectionType()
    {
        List<string> types = [ParsePrimaryType()];

        while (Match(TokenType.AMPERSAND))
        {
            types.Add(ParsePrimaryType());
        }

        return types.Count == 1 ? types[0] : string.Join(" & ", types);
    }

    private string ParsePrimaryType()
    {
        string typeName;

        // Handle tuple type syntax: [string, number, boolean?]
        if (Match(TokenType.LEFT_BRACKET))
        {
            return ParseTupleType();
        }

        // Handle inline object type syntax: { name: string; age?: number }
        if (Match(TokenType.LEFT_BRACE))
        {
            return ParseInlineObjectType();
        }

        // Handle parenthesized types: (string | number) or function types: (x: number) => number
        if (Match(TokenType.LEFT_PAREN))
        {
            // Check if this is a function type by looking for:
            // 1. Empty params: () =>
            // 2. Named params: (identifier :
            bool isFunctionType = false;
            if (Check(TokenType.RIGHT_PAREN))
            {
                // () - check if followed by =>
                int saved = _current;
                Advance(); // consume )
                if (Check(TokenType.ARROW))
                {
                    isFunctionType = true;
                }
                _current = saved; // backtrack
            }
            else if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.COLON)
            {
                // (identifier: - this is a function parameter
                isFunctionType = true;
            }

            if (isFunctionType)
            {
                // Parse as function type: (params) => returnType
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
                typeName = $"({string.Join(", ", paramTypes)}) => {returnType}";
            }
            else
            {
                // Parse as grouped type: (type1 | type2)
                typeName = "(" + ParseUnionType() + ")";
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after grouped type.");
            }
        }
        // Handle string literal types: "success" | "error"
        else if (Match(TokenType.STRING))
        {
            typeName = "\"" + (string)Previous().Literal! + "\"";
        }
        // Handle number literal types: 0 | 1 | 2
        else if (Match(TokenType.NUMBER))
        {
            typeName = Previous().Literal!.ToString()!;
        }
        // Handle boolean literal types: true | false
        else if (Match(TokenType.TRUE))
        {
            typeName = "true";
        }
        else if (Match(TokenType.FALSE))
        {
            typeName = "false";
        }
        else if (Check(TokenType.TYPE_STRING) || Check(TokenType.TYPE_NUMBER) ||
                 Check(TokenType.TYPE_BOOLEAN) || Check(TokenType.TYPE_SYMBOL) ||
                 Check(TokenType.IDENTIFIER) ||
                 Check(TokenType.NULL) || Check(TokenType.UNKNOWN) || Check(TokenType.NEVER))
        {
            typeName = Advance().Lexeme;
        }
        else
        {
            throw new Exception("Expect type.");
        }

        // Handle generic type arguments: Container<number>, Map<string, number>
        if (Check(TokenType.LESS))
        {
            int saved = _current;
            Advance(); // consume <
            if (IsTypeStart())
            {
                List<string> typeArgs = [ParseTypeAnnotation()];
                while (Match(TokenType.COMMA))
                    typeArgs.Add(ParseTypeAnnotation());
                if (Match(TokenType.GREATER))
                    typeName = $"{typeName}<{string.Join(", ", typeArgs)}>";
                else
                    _current = saved; // Backtrack if not a valid generic type
            }
            else
            {
                _current = saved; // Backtrack if not a type
            }
        }

        // Handle array suffix
        while (Match(TokenType.LEFT_BRACKET))
        {
            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after '[' in array type.");
            typeName += "[]";
        }

        return typeName;
    }

    private string ParseTupleType()
    {
        // Already consumed LEFT_BRACKET
        List<string> elements = [];

        while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
        {
            // Check for rest element: ...Type[]
            if (Match(TokenType.DOT_DOT_DOT))
            {
                string restType = ParsePrimaryType();
                if (!restType.EndsWith("[]"))
                    throw new Exception("Parse Error: Rest element in tuple must be an array type.");
                elements.Add("..." + restType);
                break; // Rest must be last
            }

            string elementType = ParseUnionType(); // Support union elements like [string | number, boolean]

            // Check for optional marker
            if (Match(TokenType.QUESTION))
                elementType += "?";

            elements.Add(elementType);

            if (!Check(TokenType.RIGHT_BRACKET))
                Consume(TokenType.COMMA, "Expect ',' between tuple elements.");
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after tuple type.");
        return "[" + string.Join(", ", elements) + "]";
    }

    private string ParseInlineObjectType()
    {
        // Already consumed LEFT_BRACE
        // Parses: { name: string; age?: number; greet(x: number): string; [key: string]: number }
        List<string> members = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Check for index signature: [key: string]: type
            if (Check(TokenType.LEFT_BRACKET))
            {
                Advance(); // consume [
                Consume(TokenType.IDENTIFIER, "Expect index signature key name.");
                Consume(TokenType.COLON, "Expect ':' after index signature key name.");

                // Get the key type (string, number, or symbol)
                string keyType;
                if (Check(TokenType.TYPE_STRING))
                {
                    keyType = "string";
                    Advance();
                }
                else if (Check(TokenType.TYPE_NUMBER))
                {
                    keyType = "number";
                    Advance();
                }
                else if (Check(TokenType.TYPE_SYMBOL))
                {
                    keyType = "symbol";
                    Advance();
                }
                else
                {
                    throw new Exception("Expect 'string', 'number', or 'symbol' as index signature key type.");
                }

                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index signature key type.");
                Consume(TokenType.COLON, "Expect ':' after index signature.");
                string valueType = ParseUnionType();

                members.Add($"[{keyType}]: {valueType}");
            }
            else
            {
                // Parse property/method name
                Token propertyName = Consume(TokenType.IDENTIFIER, "Expect property name in object type.");

                // Check for optional marker
                bool isOptional = Match(TokenType.QUESTION);

                string propertyType;
                if (Check(TokenType.LEFT_PAREN))
                {
                    // Method signature: methodName(params): returnType
                    propertyType = ParseMethodSignature();
                }
                else
                {
                    // Property: name: type
                    Consume(TokenType.COLON, "Expect ':' after property name in object type.");
                    propertyType = ParseUnionType();
                }

                // Build member string
                string member = isOptional ? $"{propertyName.Lexeme}?: {propertyType}" : $"{propertyName.Lexeme}: {propertyType}";
                members.Add(member);
            }

            // Handle separator - can be semicolon or comma, or nothing before closing brace
            if (!Check(TokenType.RIGHT_BRACE))
            {
                if (!Match(TokenType.SEMICOLON) && !Match(TokenType.COMMA))
                {
                    throw new Exception("Expect ';' or ',' between object type members.");
                }
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after object type.");
        return "{ " + string.Join("; ", members) + " }";
    }

    // ============== GENERIC TYPE PARAMETER PARSING ==============

    /// <summary>
    /// Parses type parameters like &lt;T, U extends Base&gt;.
    /// Returns null if no type parameters are present.
    /// </summary>
    private List<TypeParam>? ParseTypeParameters()
    {
        if (!Match(TokenType.LESS)) return null;

        List<TypeParam> typeParams = [];
        do
        {
            Token name = Consume(TokenType.IDENTIFIER, "Expect type parameter name.");
            string? constraint = null;
            if (Match(TokenType.EXTENDS))
            {
                constraint = ParseTypeAnnotation();
            }
            typeParams.Add(new TypeParam(name, constraint));
        } while (Match(TokenType.COMMA));

        Consume(TokenType.GREATER, "Expect '>' after type parameters.");
        return typeParams;
    }

    /// <summary>
    /// Tries to parse type arguments like &lt;number, string&gt;.
    /// Returns null if not valid type arguments (backtracking safe).
    /// </summary>
    private List<string>? TryParseTypeArguments()
    {
        if (!Check(TokenType.LESS)) return null;
        int saved = _current;

        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            List<string> args = [ParseTypeAnnotation()];
            while (Match(TokenType.COMMA))
            {
                args.Add(ParseTypeAnnotation());
            }

            if (!Check(TokenType.GREATER)) { _current = saved; return null; }
            Advance(); // consume >
            return args;
        }
        catch
        {
            _current = saved;
            return null;
        }
    }

    /// <summary>
    /// Tries to parse type arguments for a function call (must be followed by '(').
    /// Returns null if not valid type arguments for a call (backtracking safe).
    /// </summary>
    private List<string>? TryParseTypeArgumentsForCall()
    {
        if (!Check(TokenType.LESS)) return null;
        int saved = _current;

        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            List<string> args = [ParseTypeAnnotation()];
            while (Match(TokenType.COMMA))
            {
                args.Add(ParseTypeAnnotation());
            }

            if (!Check(TokenType.GREATER)) { _current = saved; return null; }
            Advance(); // consume >

            // Must be followed by '(' for a call
            if (!Check(TokenType.LEFT_PAREN)) { _current = saved; return null; }
            Advance(); // consume (

            return args;
        }
        catch
        {
            _current = saved;
            return null;
        }
    }

    // ============== DESTRUCTURING PATTERN PARSING ==============

    private DestructurePattern ParseDestructurePattern()
    {
        if (Match(TokenType.LEFT_BRACKET))
            return ParseArrayPattern();
        if (Match(TokenType.LEFT_BRACE))
            return ParseObjectPattern();
        if (Match(TokenType.DOT_DOT_DOT))
        {
            Token restName = Consume(TokenType.IDENTIFIER, "Expect identifier after '...'.");
            return new RestPattern(restName);
        }

        Token patternName = Consume(TokenType.IDENTIFIER, "Expect identifier in pattern.");
        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
        return new IdentifierPattern(patternName, defaultValue);
    }

    private ArrayPattern ParseArrayPattern()
    {
        int line = Previous().Line;
        List<ArrayPatternElement> elements = [];

        while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
        {
            if (Check(TokenType.COMMA))
            {
                // Hole in array: [a, , c]
                elements.Add(new ArrayPatternElement(null, IsHole: true));
            }
            else
            {
                elements.Add(new ArrayPatternElement(ParseDestructurePattern(), IsHole: false));
            }

            if (!Check(TokenType.RIGHT_BRACKET))
            {
                Consume(TokenType.COMMA, "Expect ',' between array pattern elements.");
            }
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array pattern.");
        return new ArrayPattern(elements, line);
    }

    private ObjectPattern ParseObjectPattern()
    {
        int line = Previous().Line;
        List<ObjectPatternProperty> properties = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Handle rest pattern: { ...rest }
            if (Match(TokenType.DOT_DOT_DOT))
            {
                Token restName = Consume(TokenType.IDENTIFIER, "Expect identifier after '...'.");
                properties.Add(new ObjectPatternProperty(restName, new RestPattern(restName), null));
                // Rest must be last, so break out of loop
                break;
            }

            Token key = Consume(TokenType.IDENTIFIER, "Expect property name.");
            DestructurePattern value;
            Expr? defaultValue = null;

            if (Match(TokenType.COLON))
            {
                // Rename or nested: { x: newName } or { x: { nested } }
                if (Check(TokenType.LEFT_BRACE) || Check(TokenType.LEFT_BRACKET))
                {
                    value = ParseDestructurePattern();
                }
                else
                {
                    Token rename = Consume(TokenType.IDENTIFIER, "Expect identifier after ':'.");
                    if (Match(TokenType.EQUAL))
                        defaultValue = Expression();
                    value = new IdentifierPattern(rename, defaultValue);
                }
            }
            else
            {
                // Shorthand: { x } or { x = default }
                if (Match(TokenType.EQUAL))
                    defaultValue = Expression();
                value = new IdentifierPattern(key, defaultValue);
            }

            properties.Add(new ObjectPatternProperty(key, value, defaultValue));

            if (!Check(TokenType.RIGHT_BRACE))
            {
                Consume(TokenType.COMMA, "Expect ',' between object pattern properties.");
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after object pattern.");
        return new ObjectPattern(properties, line);
    }

    // ============== DESTRUCTURING DECLARATIONS ==============

    private Stmt DestructureArrayDeclaration()
    {
        Consume(TokenType.LEFT_BRACKET, "Expect '[' for array destructuring.");
        ArrayPattern pattern = ParseArrayPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

        return DesugarArrayPattern(pattern, initializer);
    }

    private Stmt DestructureObjectDeclaration()
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' for object destructuring.");
        ObjectPattern pattern = ParseObjectPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

        return DesugarObjectPattern(pattern, initializer);
    }

    // ============== DESUGARING METHODS ==============

    private Stmt DesugarArrayPattern(ArrayPattern pattern, Expr initializer)
    {
        List<Stmt> statements = [];

        // const _dest0 = initializer;
        Token temp = GenerateTempVar(pattern.Line);
        statements.Add(new Stmt.Var(temp, null, initializer));

        int index = 0;
        foreach (var element in pattern.Elements)
        {
            if (element.IsHole)
            {
                index++;
                continue;
            }

            if (element.Pattern is RestPattern rest)
            {
                // const rest = _dest0.slice(index);
                Expr sliceCall = new Expr.Call(
                    new Expr.Get(new Expr.Variable(temp),
                        new Token(TokenType.IDENTIFIER, "slice", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Literal((double)index)]
                );
                statements.Add(new Stmt.Var(rest.Name, null, sliceCall));
                break; // Rest must be last
            }

            // Access expression: _dest0[index]
            Expr accessExpr = new Expr.GetIndex(
                new Expr.Variable(temp),
                new Expr.Literal((double)index)
            );

            if (element.Pattern is IdentifierPattern id)
            {
                // Apply default value if present
                if (id.DefaultValue != null)
                {
                    accessExpr = new Expr.NullishCoalescing(accessExpr, id.DefaultValue);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (element.Pattern is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr));
            }
            else if (element.Pattern is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr));
            }

            index++;
        }

        return new Stmt.Sequence(statements);
    }

    private Stmt DesugarObjectPattern(ObjectPattern pattern, Expr initializer)
    {
        List<Stmt> statements = [];
        List<string> usedKeys = [];

        // const _dest0 = initializer;
        Token temp = GenerateTempVar(pattern.Line);
        statements.Add(new Stmt.Var(temp, null, initializer));

        foreach (var prop in pattern.Properties)
        {
            // Handle rest pattern: const { x, ...rest } = obj
            if (prop.Value is RestPattern rest)
            {
                // Generate: const rest = __objectRest(_dest0, ["x", "y", ...usedKeys]);
                var excludeKeysExpr = new Expr.ArrayLiteral(
                    usedKeys.Select(k => new Expr.Literal(k) as Expr).ToList()
                );
                var restCall = new Expr.Call(
                    new Expr.Variable(new Token(TokenType.IDENTIFIER, "__objectRest", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Variable(temp), excludeKeysExpr]
                );
                statements.Add(new Stmt.Var(rest.Name, null, restCall));
                break; // Rest must be last
            }

            usedKeys.Add(prop.Key.Lexeme);

            // Access expression: _dest0.key
            Expr accessExpr = new Expr.Get(new Expr.Variable(temp), prop.Key);

            if (prop.Value is IdentifierPattern id)
            {
                // Apply default value if present
                Expr? defaultVal = prop.DefaultValue ?? id.DefaultValue;
                if (defaultVal != null)
                {
                    accessExpr = new Expr.NullishCoalescing(accessExpr, defaultVal);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (prop.Value is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr));
            }
            else if (prop.Value is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr));
            }
        }

        return new Stmt.Sequence(statements);
    }

    // ============== CLASS DECLARATION ==============

    private Stmt ClassDeclaration(bool isAbstract)
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect class name.");
        List<TypeParam>? typeParams = ParseTypeParameters();

        Token? superclass = null;
        List<string>? superclassTypeArgs = null;
        if (Match(TokenType.EXTENDS))
        {
            superclass = Consume(TokenType.IDENTIFIER, "Expect superclass name.");
            superclassTypeArgs = TryParseTypeArguments();
        }

        // Parse implements clause
        List<Token>? interfaces = null;
        List<List<string>>? interfaceTypeArgs = null;
        if (Match(TokenType.IMPLEMENTS))
        {
            interfaces = [];
            interfaceTypeArgs = [];
            do
            {
                interfaces.Add(Consume(TokenType.IDENTIFIER, "Expect interface name."));
                interfaceTypeArgs.Add(TryParseTypeArguments() ?? []);
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before class body.");

        List<Stmt.Function> methods = [];
        List<Stmt.Field> fields = [];
        List<Stmt.Accessor> accessors = [];
        while (!Check(TokenType.RIGHT_BRACKET) && !Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Parse modifiers
            AccessModifier access = AccessModifier.Public;
            bool isStatic = false;
            bool isReadonly = false;
            bool isMemberAbstract = false;

            while (Match(TokenType.PUBLIC, TokenType.PRIVATE, TokenType.PROTECTED, TokenType.STATIC, TokenType.READONLY, TokenType.ABSTRACT))
            {
                var modifier = Previous().Type;
                switch (modifier)
                {
                    case TokenType.PUBLIC: access = AccessModifier.Public; break;
                    case TokenType.PRIVATE: access = AccessModifier.Private; break;
                    case TokenType.PROTECTED: access = AccessModifier.Protected; break;
                    case TokenType.STATIC: isStatic = true; break;
                    case TokenType.READONLY: isReadonly = true; break;
                    case TokenType.ABSTRACT: isMemberAbstract = true; break;
                }
            }

            // Validate: abstract members can only be in abstract classes
            if (isMemberAbstract && !isAbstract)
            {
                throw new Exception($"Parse Error: Abstract methods can only appear within an abstract class.");
            }

            // Validate: abstract and static are mutually exclusive
            if (isMemberAbstract && isStatic)
            {
                throw new Exception($"Parse Error: A method cannot be both abstract and static.");
            }

            // Check for getter/setter
            if (Check(TokenType.GET) || Check(TokenType.SET))
            {
                Token kind = Advance(); // consume 'get' or 'set'
                Token accessorName = Consume(TokenType.IDENTIFIER, "Expect property name after 'get'/'set'.");
                Consume(TokenType.LEFT_PAREN, "Expect '(' after accessor name.");

                Stmt.Parameter? setterParam = null;
                if (kind.Type == TokenType.SET)
                {
                    // Setter must have exactly one parameter
                    Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name in setter.");
                    string? paramType = null;
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                    }
                    setterParam = new Stmt.Parameter(paramName, paramType, null);
                }

                Consume(TokenType.RIGHT_PAREN, "Expect ')' after accessor parameters.");

                string? returnType = null;
                if (Match(TokenType.COLON))
                {
                    returnType = ParseTypeAnnotation();
                }

                List<Stmt> body;
                if (isMemberAbstract)
                {
                    // Abstract accessor: no body, just semicolon
                    Consume(TokenType.SEMICOLON, "Expect ';' after abstract accessor declaration.");
                    body = [];
                }
                else
                {
                    Consume(TokenType.LEFT_BRACE, "Expect '{' before accessor body.");
                    body = Block();
                }

                accessors.Add(new Stmt.Accessor(accessorName, kind, setterParam, body, returnType, access, isMemberAbstract));
            }
            else if (Peek().Type == TokenType.IDENTIFIER && (PeekNext().Type == TokenType.COLON || PeekNext().Type == TokenType.QUESTION))
            {
                // Field declaration
                Token fieldName = Consume(TokenType.IDENTIFIER, "Expect field name.");
                bool isOptional = Match(TokenType.QUESTION);
                Consume(TokenType.COLON, "Expect ':' after field name.");
                string typeAnnotation = ParseTypeAnnotation();
                Expr? initializer = null;
                if (Match(TokenType.EQUAL))
                {
                    initializer = Expression();
                }
                Consume(TokenType.SEMICOLON, "Expect ';' after field declaration.");
                fields.Add(new Stmt.Field(fieldName, typeAnnotation, initializer, isStatic, access, isReadonly, isOptional));
            }
            else
            {
                // Abstract methods cannot be constructors
                if (isMemberAbstract && Check(TokenType.CONSTRUCTOR))
                {
                    throw new Exception("Parse Error: A constructor cannot be abstract.");
                }

                if (isMemberAbstract)
                {
                    // Parse abstract method: signature only, no body
                    Token methodName = Consume(TokenType.IDENTIFIER, "Expect method name.");
                    List<TypeParam>? typeParams2 = ParseTypeParameters();
                    Consume(TokenType.LEFT_PAREN, "Expect '(' after method name.");
                    List<Stmt.Parameter> parameters = ParseMethodParameters();
                    Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

                    string? returnType = null;
                    if (Match(TokenType.COLON))
                    {
                        returnType = ParseTypeAnnotation();
                    }

                    Consume(TokenType.SEMICOLON, "Expect ';' after abstract method declaration.");

                    var func = new Stmt.Function(methodName, typeParams2, parameters, null, returnType, isStatic, access, IsAbstract: true);
                    methods.Add(func);
                }
                else
                {
                    string kind = "method";
                    if (Check(TokenType.CONSTRUCTOR)) kind = "constructor";
                    var func = (Stmt.Function)FunctionDeclaration(kind);
                    func = func with { IsStatic = isStatic, Access = access };
                    methods.Add(func);

                    // Synthesize fields from constructor parameter properties
                    if (kind == "constructor")
                    {
                        foreach (var param in func.Parameters)
                        {
                            if (param.IsParameterProperty)
                            {
                                // Check for conflicts with explicitly declared fields
                                if (fields.Any(f => f.Name.Lexeme == param.Name.Lexeme))
                                {
                                    throw new Exception($"Parse Error: Parameter property '{param.Name.Lexeme}' conflicts with existing field declaration.");
                                }

                                // Synthesize field declaration (no initializer - set in constructor)
                                fields.Add(new Stmt.Field(
                                    param.Name,
                                    param.Type,
                                    null,  // No initializer
                                    false, // Not static
                                    param.Access ?? AccessModifier.Public,
                                    param.IsReadonly,
                                    false  // Not optional
                                ));
                            }
                        }
                    }
                }
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after class body.");
        return new Stmt.Class(name, typeParams, superclass, superclassTypeArgs, methods, fields, accessors.Count > 0 ? accessors : null, interfaces, interfaceTypeArgs, isAbstract);
    }

    private Stmt FunctionDeclaration(string kind)
    {
        Token name;
        if (kind == "constructor" && Match(TokenType.CONSTRUCTOR))
        {
            name = Previous();
        }
        else
        {
            name = Consume(TokenType.IDENTIFIER, $"Expect {kind} name.");
        }

        // Parse type parameters (e.g., <T, U extends Base>)
        List<TypeParam>? typeParams = ParseTypeParameters();

        Consume(TokenType.LEFT_PAREN, $"Expect '(' after {kind} name.");
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Check for destructuring pattern parameter
                if (Check(TokenType.LEFT_BRACKET))
                {
                    // Array destructure: function f([a, b]) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACKET, "");
                    var pattern = ParseArrayPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else if (Check(TokenType.LEFT_BRACE))
                {
                    // Object destructure: function f({ x, y }) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACE, "");
                    var pattern = ParseObjectPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    // Check for parameter property modifiers (only valid in constructors)
                    AccessModifier? access = null;
                    bool isReadonly = false;
                    bool isParameterProperty = false;

                    // Parse modifiers (order doesn't matter: readonly public or public readonly)
                    while (Check(TokenType.PUBLIC) || Check(TokenType.PRIVATE) ||
                           Check(TokenType.PROTECTED) || Check(TokenType.READONLY))
                    {
                        if (Match(TokenType.PUBLIC))
                        {
                            access = AccessModifier.Public;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.PRIVATE))
                        {
                            access = AccessModifier.Private;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.PROTECTED))
                        {
                            access = AccessModifier.Protected;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.READONLY))
                        {
                            isReadonly = true;
                            isParameterProperty = true;
                        }
                    }

                    // If only readonly was specified, default access is public
                    if (isParameterProperty && access == null)
                    {
                        access = AccessModifier.Public;
                    }

                    Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");

                    // Check for optional parameter marker (?)
                    bool isOptional = Match(TokenType.QUESTION);

                    string? paramType = null;
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                    }
                    Expr? defaultValue = null;
                    if (Match(TokenType.EQUAL))
                    {
                        defaultValue = Expression();
                    }
                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest, isParameterProperty, access, isReadonly, isOptional));

                    // Rest parameter must be last
                    if (isRest && Check(TokenType.COMMA))
                    {
                        throw new Exception("Parse Error: Rest parameter must be last.");
                    }
                }
            } while (Match(TokenType.COMMA));
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

        string? returnType = null;
        if (Match(TokenType.COLON))
        {
            returnType = ParseTypeAnnotation();
        }

        // Check for overload signature (semicolon instead of body)
        if (Match(TokenType.SEMICOLON))
        {
            // Overload signature - no body, just declaration
            return new Stmt.Function(name, typeParams, parameters, null, returnType);
        }

        Consume(TokenType.LEFT_BRACE, $"Expect '{{' before {kind} body.");
        List<Stmt> body = Block();

        // Prepend destructuring statements for patterned parameters
        if (destructuredParams.Count > 0)
        {
            List<Stmt> prologue = [];
            foreach (var (synthName, pattern) in destructuredParams)
            {
                var paramVar = new Expr.Variable(synthName);
                Stmt desugar = pattern switch
                {
                    ArrayPattern ap => DesugarArrayPattern(ap, paramVar),
                    ObjectPattern op => DesugarObjectPattern(op, paramVar),
                    _ => throw new Exception("Unknown pattern type")
                };
                prologue.Add(desugar);
            }
            body = prologue.Concat(body).ToList();
        }

        // Prepend parameter property assignments for constructor: this.x = x
        if (kind == "constructor")
        {
            List<Stmt> propAssignments = [];
            foreach (var param in parameters)
            {
                if (param.IsParameterProperty)
                {
                    // Generate: this.<name> = <name>
                    var thisExpr = new Expr.This(new Token(TokenType.THIS, "this", null, param.Name.Line));
                    var paramVar = new Expr.Variable(param.Name);
                    var setExpr = new Expr.Set(thisExpr, param.Name, paramVar);
                    propAssignments.Add(new Stmt.Expression(setExpr));
                }
            }
            if (propAssignments.Count > 0)
            {
                body = propAssignments.Concat(body).ToList();
            }
        }

        return new Stmt.Function(name, typeParams, parameters, body, returnType);
    }

    /// <summary>
    /// Parse method parameters for abstract methods (no destructuring, no parameter properties).
    /// </summary>
    private List<Stmt.Parameter> ParseMethodParameters()
    {
        List<Stmt.Parameter> parameters = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Check for rest parameter
                bool isRest = Match(TokenType.DOT_DOT_DOT);

                Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                string? paramType = null;
                if (Match(TokenType.COLON))
                {
                    paramType = ParseTypeAnnotation();
                }
                // Abstract methods don't have a body, so no default values make sense
                // But TypeScript does allow them in the signature, so let's parse them
                Expr? defaultValue = null;
                if (Match(TokenType.EQUAL))
                {
                    defaultValue = Expression();
                }
                parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest));

                // Rest parameter must be last
                if (isRest && Check(TokenType.COMMA))
                {
                    throw new Exception("Parse Error: Rest parameter must be last.");
                }
            } while (Match(TokenType.COMMA));
        }

        return parameters;
    }

    private Stmt Statement()
    {
        if (Match(TokenType.BREAK)) return BreakStatement();
        if (Match(TokenType.CONTINUE)) return ContinueStatement();
        if (Match(TokenType.FOR)) return ForStatement();
        if (Match(TokenType.IF)) return IfStatement();
        if (Match(TokenType.SWITCH)) return SwitchStatement();
        if (Match(TokenType.TRY)) return TryStatement();
        if (Match(TokenType.THROW)) return ThrowStatement();
        if (Match(TokenType.DO)) return DoWhileStatement();
        if (Match(TokenType.WHILE)) return WhileStatement();
        if (Match(TokenType.RETURN)) return ReturnStatement();
        if (Match(TokenType.LEFT_BRACE)) return new Stmt.Block(Block());

        return ExpressionStatement();
    }

    private Stmt BreakStatement()
    {
        Token keyword = Previous();
        Consume(TokenType.SEMICOLON, "Expect ';' after 'break'.");
        return new Stmt.Break(keyword);
    }

    private Stmt ContinueStatement()
    {
        Token keyword = Previous();
        Consume(TokenType.SEMICOLON, "Expect ';' after 'continue'.");
        return new Stmt.Continue(keyword);
    }

    private Stmt ForStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'for'.");

        // Check for for...of pattern: for (let/const varName of iterable)
        if (Match(TokenType.LET, TokenType.CONST))
        {
            Token varName = Consume(TokenType.IDENTIFIER, "Expect variable name.");

            // Check for optional type annotation
            string? typeAnnotation = null;
            if (Match(TokenType.COLON))
            {
                typeAnnotation = ParseTypeAnnotation();
            }

            // If we see 'of', this is a for...of loop
            if (Match(TokenType.OF))
            {
                Expr iterable = Expression();
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after for...of expression.");
                Stmt body = Statement();
                return new Stmt.ForOf(varName, typeAnnotation, iterable, body);
            }

            // If we see 'in', this is a for...in loop
            if (Match(TokenType.IN))
            {
                Expr obj = Expression();
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after for...in expression.");
                Stmt body = Statement();
                return new Stmt.ForIn(varName, typeAnnotation, obj, body);
            }

            // Otherwise it's a traditional for loop - we need to handle the initializer
            // We've already consumed let/const and the variable name, so reconstruct
            Expr? initValue = null;
            if (Match(TokenType.EQUAL))
            {
                initValue = Expression();
            }
            Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

            Stmt initializer = new Stmt.Var(varName, typeAnnotation, initValue);
            return FinishTraditionalFor(initializer);
        }

        // Traditional for loop without let/const
        Stmt? init;
        if (Match(TokenType.SEMICOLON))
        {
            init = null;
        }
        else
        {
            init = ExpressionStatement();
        }

        return FinishTraditionalFor(init);
    }

    private Stmt FinishTraditionalFor(Stmt? initializer)
    {
        Expr? condition = null;
        if (!Check(TokenType.SEMICOLON))
        {
            condition = Expression();
        }
        Consume(TokenType.SEMICOLON, "Expect ';' after loop condition.");

        Expr? increment = null;
        if (!Check(TokenType.RIGHT_PAREN))
        {
            increment = Expression();
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after for clauses.");

        Stmt body = Statement();

        if (increment != null)
        {
            body = new Stmt.Block([
                body,
                new Stmt.Expression(increment)
            ]);
        }

        condition ??= new Expr.Literal(true);
        body = new Stmt.While(condition, body);

        if (initializer != null)
        {
            body = new Stmt.Block([initializer, body]);
        }

        return body;
    }

    private Stmt WhileStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after condition.");
        Stmt body = Statement();

        return new Stmt.While(condition, body);
    }

    private Stmt DoWhileStatement()
    {
        Stmt body = Statement();
        Consume(TokenType.WHILE, "Expect 'while' after do body.");
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after condition.");
        Consume(TokenType.SEMICOLON, "Expect ';' after do-while condition.");

        return new Stmt.DoWhile(body, condition);
    }

    private Stmt ReturnStatement()
    {
        Token keyword = Previous();
        Expr? value = null;
        if (!Check(TokenType.SEMICOLON))
        {
            value = Expression();
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after return value.");
        return new Stmt.Return(keyword, value);
    }

    private Stmt IfStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'if'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after if condition.");

        Stmt thenBranch = Statement();
        Stmt? elseBranch = null;
        if (Match(TokenType.ELSE))
        {
            elseBranch = Statement();
        }

        return new Stmt.If(condition, thenBranch, elseBranch);
    }

    private Stmt SwitchStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'switch'.");
        Expr subject = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after switch expression.");
        Consume(TokenType.LEFT_BRACE, "Expect '{' before switch body.");

        List<Stmt.SwitchCase> cases = [];
        List<Stmt>? defaultBody = null;

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            if (Match(TokenType.CASE))
            {
                Expr caseValue = Expression();
                Consume(TokenType.COLON, "Expect ':' after case value.");

                List<Stmt> caseBody = [];
                while (!Check(TokenType.CASE) && !Check(TokenType.DEFAULT) &&
                       !Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
                {
                    caseBody.Add(Declaration());
                }
                cases.Add(new Stmt.SwitchCase(caseValue, caseBody));
            }
            else if (Match(TokenType.DEFAULT))
            {
                Consume(TokenType.COLON, "Expect ':' after 'default'.");

                defaultBody = [];
                while (!Check(TokenType.CASE) && !Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
                {
                    defaultBody.Add(Declaration());
                }
            }
            else
            {
                throw new Exception("Expect 'case' or 'default' in switch body.");
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after switch body.");
        return new Stmt.Switch(subject, cases, defaultBody);
    }

    private Stmt TryStatement()
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' after 'try'.");
        List<Stmt> tryBlock = Block();

        Token? catchParam = null;
        List<Stmt>? catchBlock = null;
        List<Stmt>? finallyBlock = null;

        if (Match(TokenType.CATCH))
        {
            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'catch'.");
            catchParam = Consume(TokenType.IDENTIFIER, "Expect catch parameter name.");
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after catch parameter.");
            Consume(TokenType.LEFT_BRACE, "Expect '{' before catch block.");
            catchBlock = Block();
        }

        if (Match(TokenType.FINALLY))
        {
            Consume(TokenType.LEFT_BRACE, "Expect '{' after 'finally'.");
            finallyBlock = Block();
        }

        if (catchBlock == null && finallyBlock == null)
        {
            throw new Exception("Try statement must have catch or finally clause.");
        }

        return new Stmt.TryCatch(tryBlock, catchParam, catchBlock, finallyBlock);
    }

    private Stmt ThrowStatement()
    {
        Token keyword = Previous();
        Expr value = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after throw value.");
        return new Stmt.Throw(keyword, value);
    }

    private List<Stmt> Block()
    {
        List<Stmt> statements = [];
        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            statements.Add(Declaration());
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after block.");
        return statements;
    }

    private Stmt ExpressionStatement()
    {
        Expr expr = Expression();
        // Handle console.log specially for MVP
        if (expr is Expr.Call call && call.Callee is Expr.Variable varExpr && varExpr.Name.Lexeme == "console.log")
        {
             // Simplified for MVP
        }
        
        Consume(TokenType.SEMICOLON, "Expect ';' after expression.");
        return new Stmt.Expression(expr);
    }

    private Expr Expression() => Assignment();

    private Expr Assignment()
    {
        Expr expr = Ternary();

        if (Match(TokenType.EQUAL))
        {
            Token equals = Previous();
            Expr value = Assignment();

            if (expr is Expr.Variable variable)
            {
                return new Expr.Assign(variable.Name, value);
            }
            else if (expr is Expr.Get get)
            {
                return new Expr.Set(get.Object, get.Name, value);
            }
            else if (expr is Expr.GetIndex getIndex)
            {
                return new Expr.SetIndex(getIndex.Object, getIndex.Index, value);
            }

            throw new Exception("Invalid assignment target.");
        }

        // Compound assignment operators
        if (Match(TokenType.PLUS_EQUAL, TokenType.MINUS_EQUAL, TokenType.STAR_EQUAL,
                  TokenType.SLASH_EQUAL, TokenType.PERCENT_EQUAL,
                  TokenType.AMPERSAND_EQUAL, TokenType.PIPE_EQUAL, TokenType.CARET_EQUAL,
                  TokenType.LESS_LESS_EQUAL, TokenType.GREATER_GREATER_EQUAL, TokenType.GREATER_GREATER_GREATER_EQUAL))
        {
            Token op = Previous();
            Expr value = Assignment();

            if (expr is Expr.Variable variable)
            {
                return new Expr.CompoundAssign(variable.Name, op, value);
            }
            else if (expr is Expr.Get get)
            {
                return new Expr.CompoundSet(get.Object, get.Name, op, value);
            }
            else if (expr is Expr.GetIndex getIndex)
            {
                return new Expr.CompoundSetIndex(getIndex.Object, getIndex.Index, op, value);
            }

            throw new Exception("Invalid compound assignment target.");
        }

        return expr;
    }

    private Expr Ternary()
    {
        Expr expr = NullishCoalescing();

        if (Match(TokenType.QUESTION))
        {
            Expr thenBranch = Ternary();
            Consume(TokenType.COLON, "Expect ':' in ternary expression.");
            Expr elseBranch = Ternary();
            expr = new Expr.Ternary(expr, thenBranch, elseBranch);
        }

        return expr;
    }

    private Expr NullishCoalescing()
    {
        Expr expr = Or();

        while (Match(TokenType.QUESTION_QUESTION))
        {
            Expr right = Or();
            expr = new Expr.NullishCoalescing(expr, right);
        }

        return expr;
    }

    private Expr Or()
    {
        Expr expr = And();

        while (Match(TokenType.OR_OR))
        {
            Token op = Previous();
            Expr right = And();
            expr = new Expr.Logical(expr, op, right);
        }

        return expr;
    }

    private Expr And()
    {
        Expr expr = BitwiseOr();

        while (Match(TokenType.AND_AND))
        {
            Token op = Previous();
            Expr right = BitwiseOr();
            expr = new Expr.Logical(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseOr()
    {
        Expr expr = BitwiseXor();

        while (Match(TokenType.PIPE))
        {
            Token op = Previous();
            Expr right = BitwiseXor();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseXor()
    {
        Expr expr = BitwiseAnd();

        while (Match(TokenType.CARET))
        {
            Token op = Previous();
            Expr right = BitwiseAnd();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseAnd()
    {
        Expr expr = Equality();

        while (Match(TokenType.AMPERSAND))
        {
            Token op = Previous();
            Expr right = Equality();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Equality()
    {
        Expr expr = Comparison();

        while (Match(TokenType.BANG_EQUAL, TokenType.EQUAL_EQUAL,
                     TokenType.BANG_EQUAL_EQUAL, TokenType.EQUAL_EQUAL_EQUAL))
        {
            Token op = Previous();
            Expr right = Comparison();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Comparison()
    {
        Expr expr = Shift();

        while (Match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL, TokenType.IN, TokenType.INSTANCEOF))
        {
            Token op = Previous();
            Expr right = Shift();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Shift()
    {
        Expr expr = Term();

        while (Match(TokenType.LESS_LESS, TokenType.GREATER_GREATER, TokenType.GREATER_GREATER_GREATER))
        {
            Token op = Previous();
            Expr right = Term();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Term()
    {
        Expr expr = Factor();

        while (Match(TokenType.MINUS, TokenType.PLUS))
        {
            Token op = Previous();
            Expr right = Factor();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Factor()
    {
        Expr expr = Exponentiation();

        while (Match(TokenType.SLASH, TokenType.STAR, TokenType.PERCENT))
        {
            Token op = Previous();
            Expr right = Exponentiation();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Exponentiation()
    {
        Expr expr = Unary();

        // ** is right-associative, so we use recursion instead of a loop
        if (Match(TokenType.STAR_STAR))
        {
            Token op = Previous();
            Expr right = Exponentiation(); // Right-associative
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Unary()
    {
        // Check for angle-bracket type assertion: <Type>expr
        if (Check(TokenType.LESS))
        {
            var assertion = TryParseAngleBracketAssertion();
            if (assertion != null) return assertion;
        }

        // Prefix increment/decrement
        if (Match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS))
        {
            Token op = Previous();
            Expr operand = Unary();
            if (operand is not (Expr.Variable or Expr.Get or Expr.GetIndex))
            {
                throw new Exception("Invalid operand for prefix increment/decrement.");
            }
            return new Expr.PrefixIncrement(op, operand);
        }

        if (Match(TokenType.BANG, TokenType.MINUS, TokenType.TYPEOF, TokenType.TILDE))
        {
            Token op = Previous();
            Expr right = Unary();
            return new Expr.Unary(op, right);
        }

        if (Match(TokenType.NEW))
        {
            Token className = Consume(TokenType.IDENTIFIER, "Expect class name after 'new'.");
            List<string>? typeArgs = TryParseTypeArguments();
            Consume(TokenType.LEFT_PAREN, "Expect '(' after class name.");
            List<Expr> arguments = [];
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
            return new Expr.New(className, typeArgs, arguments);
        }

        return Call();
    }

    private Expr Call()
    {
        Expr expr = Primary();

        while (true)
        {
            // Check for type arguments before call: func<T>(args)
            List<string>? typeArgs = null;
            if (Check(TokenType.LESS))
            {
                typeArgs = TryParseTypeArgumentsForCall();
            }

            if (typeArgs != null || Match(TokenType.LEFT_PAREN))
            {
                expr = FinishCall(expr, typeArgs);
            }
            else if (Match(TokenType.DOT))
            {
                Token name = Consume(TokenType.IDENTIFIER, "Expect property name after '.'.");
                if (expr is Expr.Variable v && v.Name.Lexeme == "console" && name.Lexeme == "log")
                {
                    expr = new Expr.Variable(new Token(TokenType.IDENTIFIER, "console.log", null, name.Line));
                }
                else
                {
                    expr = new Expr.Get(expr, name);
                }
            }
            else if (Match(TokenType.QUESTION_DOT))
            {
                Token name = Consume(TokenType.IDENTIFIER, "Expect property name after '?.'.");
                expr = new Expr.Get(expr, name, Optional: true);
            }
            else if (Match(TokenType.LEFT_BRACKET))
            {
                Expr index = Expression();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index.");
                expr = new Expr.GetIndex(expr, index);
            }
            else if (Match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS))
            {
                // Postfix increment/decrement
                Token op = Previous();
                if (expr is not (Expr.Variable or Expr.Get or Expr.GetIndex))
                {
                    throw new Exception("Invalid operand for postfix increment/decrement.");
                }
                expr = new Expr.PostfixIncrement(expr, op);
            }
            else if (Match(TokenType.AS))
            {
                // Type assertion: expr as Type
                string targetType = ParseTypeAnnotation();
                expr = new Expr.TypeAssertion(expr, targetType);
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private Expr FinishCall(Expr callee, List<string>? typeArgs = null)
    {
        List<Expr> arguments = [];
        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                if (Match(TokenType.DOT_DOT_DOT))
                {
                    arguments.Add(new Expr.Spread(Expression()));
                }
                else
                {
                    arguments.Add(Expression());
                }
            } while (Match(TokenType.COMMA));
        }

        Token paren = Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
        return new Expr.Call(callee, paren, typeArgs, arguments);
    }

    private Expr Primary()
    {
        if (Match(TokenType.FALSE)) return new Expr.Literal(false);
        if (Match(TokenType.TRUE)) return new Expr.Literal(true);
        if (Match(TokenType.NULL)) return new Expr.Literal(null);
        if (Match(TokenType.NUMBER, TokenType.STRING)) return new Expr.Literal(Previous().Literal);
        if (Match(TokenType.THIS)) return new Expr.This(Previous());
        if (Match(TokenType.SUPER))
        {
            Token keyword = Previous();
            // super() for constructor calls, super.method() for method calls
            if (Check(TokenType.LEFT_PAREN))
            {
                // super() - constructor call, Method is null
                return new Expr.Super(keyword, null);
            }
            Consume(TokenType.DOT, "Expect '.' or '(' after 'super'.");
            Token method;
            if (Match(TokenType.IDENTIFIER, TokenType.CONSTRUCTOR))
            {
                method = Previous();
            }
            else
            {
                throw new Exception("Expect superclass method name.");
            }
            return new Expr.Super(keyword, method);
        }
        if (Match(TokenType.IDENTIFIER)) return new Expr.Variable(Previous());

        // Symbol is a special callable constructor
        if (Match(TokenType.SYMBOL)) return new Expr.Variable(Previous());

        if (Match(TokenType.LEFT_BRACKET))
        {
            List<Expr> elements = [];
            if (!Check(TokenType.RIGHT_BRACKET))
            {
                do
                {
                    if (Match(TokenType.DOT_DOT_DOT))
                    {
                        elements.Add(new Expr.Spread(Expression()));
                    }
                    else
                    {
                        elements.Add(Expression());
                    }
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array elements.");
            return new Expr.ArrayLiteral(elements);
        }

        if (Match(TokenType.LEFT_BRACE))
        {
            List<Expr.Property> properties = [];
            if (!Check(TokenType.RIGHT_BRACE))
            {
                do
                {
                    // Check for spread: { ...obj }
                    if (Match(TokenType.DOT_DOT_DOT))
                    {
                        Expr spreadExpr = Expression();
                        properties.Add(new Expr.Property(null, spreadExpr, IsSpread: true));
                        continue;
                    }

                    Token name = Consume(TokenType.IDENTIFIER, "Expect property name.");
                    Expr value;

                    if (Match(TokenType.LEFT_PAREN))
                    {
                        // Method shorthand: { fn() {} }
                        List<Stmt.Parameter> parameters = [];
                        if (!Check(TokenType.RIGHT_PAREN))
                        {
                            do
                            {
                                Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                                string? paramType = null;
                                if (Match(TokenType.COLON))
                                {
                                    paramType = ParseTypeAnnotation();
                                }
                                Expr? defaultValue = null;
                                if (Match(TokenType.EQUAL))
                                {
                                    defaultValue = Expression();
                                }
                                parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue));
                            } while (Match(TokenType.COMMA));
                        }
                        Consume(TokenType.RIGHT_PAREN, "Expect ')' after method parameters.");

                        string? returnType = null;
                        if (Match(TokenType.COLON))
                        {
                            returnType = ParseTypeAnnotation();
                        }

                        Consume(TokenType.LEFT_BRACE, "Expect '{' before method body.");
                        List<Stmt> body = Block();
                        value = new Expr.ArrowFunction(null, parameters, null, body, returnType);
                    }
                    else if (Match(TokenType.COLON))
                    {
                        // Explicit property: { x: value }
                        value = Expression();
                    }
                    else
                    {
                        // Shorthand property: { x } -> { x: x }
                        value = new Expr.Variable(name);
                    }

                    properties.Add(new Expr.Property(name, value));
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_BRACE, "Expect '}' after object literal.");
            return new Expr.ObjectLiteral(properties);
        }
        
        if (Match(TokenType.LEFT_PAREN))
        {
            // Try to parse as arrow function first
            Expr? arrowFunc = TryParseArrowFunction();
            if (arrowFunc != null) return arrowFunc;

            // Otherwise, parse as grouping
            Expr expr = Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.");
            return new Expr.Grouping(expr);
        }

        // Template literals
        if (Match(TokenType.TEMPLATE_FULL))
        {
            return new Expr.TemplateLiteral([(string)Previous().Literal!], []);
        }
        if (Match(TokenType.TEMPLATE_HEAD))
        {
            return ParseTemplateLiteral();
        }

        throw new Exception("Expect expression.");
    }

    private Expr ParseTemplateLiteral()
    {
        List<string> strings = [(string)Previous().Literal!];
        List<Expr> expressions = [];

        // Parse first expression
        expressions.Add(Expression());

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            strings.Add((string)Previous().Literal!);
            expressions.Add(Expression());
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal.");
        strings.Add((string)Previous().Literal!);

        return new Expr.TemplateLiteral(strings, expressions);
    }

    // Try to parse arrow function after seeing '('
    // Returns null if not an arrow function (caller should parse as grouping)
    private Expr? TryParseArrowFunction()
    {
        int savedPosition = _current;

        // Try to parse parameter list
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            // Must start with identifier, [, {, or ... for it to be arrow function params
            if (!Check(TokenType.IDENTIFIER) && !Check(TokenType.LEFT_BRACKET) && !Check(TokenType.LEFT_BRACE) && !Check(TokenType.DOT_DOT_DOT))
            {
                _current = savedPosition;
                return null;
            }

            do
            {
                if (Check(TokenType.LEFT_BRACKET))
                {
                    // Array destructure parameter: ([a, b]) => ...
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACKET, "");
                    var pattern = ParseArrayPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else if (Check(TokenType.LEFT_BRACE))
                {
                    // Object destructure parameter: ({ x, y }) => ...
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACE, "");
                    var pattern = ParseObjectPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    if (!Check(TokenType.IDENTIFIER))
                    {
                        _current = savedPosition;
                        return null;
                    }

                    Token paramName = Advance();
                    string? paramType = null;
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                    }
                    Expr? defaultValue = null;
                    if (Match(TokenType.EQUAL))
                    {
                        defaultValue = Expression();
                    }
                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest));

                    // Rest parameter must be last
                    if (isRest && Check(TokenType.COMMA))
                    {
                        _current = savedPosition;
                        return null; // Invalid: rest must be last
                    }
                }
            } while (Match(TokenType.COMMA));
        }

        if (!Match(TokenType.RIGHT_PAREN))
        {
            _current = savedPosition;
            return null;
        }

        // Check for optional return type
        string? returnType = null;
        if (Match(TokenType.COLON))
        {
            // This could be return type OR ternary colon - need to check for arrow after
            int beforeType = _current;
            try
            {
                returnType = ParseFunctionTypeAnnotation();
            }
            catch
            {
                _current = savedPosition;
                return null;
            }
        }

        // Must see '=>' for it to be an arrow function
        if (!Match(TokenType.ARROW))
        {
            _current = savedPosition;
            return null;
        }

        // Parse body - either block or expression
        List<Stmt>? body = null;
        Expr? exprBody = null;

        if (Match(TokenType.LEFT_BRACE))
        {
            body = Block();
        }
        else
        {
            exprBody = Expression();
        }

        // If we have destructured parameters, prepend desugaring to body
        if (destructuredParams.Count > 0)
        {
            List<Stmt> prologue = [];
            foreach (var (synthName, pattern) in destructuredParams)
            {
                var paramVar = new Expr.Variable(synthName);
                Stmt desugar = pattern switch
                {
                    ArrayPattern ap => DesugarArrayPattern(ap, paramVar),
                    ObjectPattern op => DesugarObjectPattern(op, paramVar),
                    _ => throw new Exception("Unknown pattern type")
                };
                prologue.Add(desugar);
            }

            if (body != null)
            {
                body = prologue.Concat(body).ToList();
            }
            else if (exprBody != null)
            {
                // For expression body, wrap in a block with prologue + return
                body = prologue.Concat([new Stmt.Return(new Token(TokenType.RETURN, "return", null, 0), exprBody)]).ToList();
                exprBody = null;
            }
        }

        return new Expr.ArrowFunction(null, parameters, exprBody, body, returnType);  // TODO: Parse type params
    }

    // Parse function type annotation like "(number) => number" for return types
    private string ParseFunctionTypeAnnotation()
    {
        // Check if it's a function type: (params) => returnType
        if (Check(TokenType.LEFT_PAREN))
        {
            Advance(); // consume '('
            List<string> paramTypes = [];
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    paramTypes.Add(ParseTypeAnnotation());
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_PAREN, "Expect ')' in function type.");
            Consume(TokenType.ARROW, "Expect '=>' in function type.");
            string returnType = ParseTypeAnnotation();
            return $"({string.Join(", ", paramTypes)}) => {returnType}";
        }

        // Otherwise regular type
        return ParseTypeAnnotation();
    }

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

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

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

    private Token Previous() => _tokens[_current - 1];

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
        Check(TokenType.LEFT_PAREN);
}