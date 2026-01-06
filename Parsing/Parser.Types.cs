namespace SharpTS.Parsing;

public partial class Parser
{
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
            else if ((Check(TokenType.IDENTIFIER) || Check(TokenType.THIS)) && PeekNext().Type == TokenType.COLON)
            {
                // (identifier: or (this: - this is a function parameter
                isFunctionType = true;
            }

            if (isFunctionType)
            {
                // Parse as function type: (params) => returnType or (this: Type, params) => returnType
                string? thisType = null;
                List<string> paramTypes = [];

                // Check for 'this' parameter
                if (Check(TokenType.THIS) && PeekNext().Type == TokenType.COLON)
                {
                    Advance(); // consume 'this'
                    Consume(TokenType.COLON, "");
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
                if (thisType != null)
                {
                    typeName = $"(this: {thisType}, {string.Join(", ", paramTypes)}) => {returnType}";
                }
                else
                {
                    typeName = $"({string.Join(", ", paramTypes)}) => {returnType}";
                }
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
                 Check(TokenType.TYPE_BIGINT) ||
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
}
