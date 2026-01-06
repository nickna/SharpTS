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

        // Handle keyof prefix operator: keyof T
        if (Match(TokenType.KEYOF))
        {
            string innerType = ParsePrimaryType();
            return $"keyof {innerType}";
        }

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

        // Handle array suffix T[] and indexed access types T[K]
        while (Check(TokenType.LEFT_BRACKET))
        {
            int saved = _current;
            Advance(); // consume [

            if (Check(TokenType.RIGHT_BRACKET))
            {
                // Array suffix: T[]
                Advance(); // consume ]
                typeName += "[]";
            }
            else
            {
                // Indexed access type: T[K] or T["key"]
                string indexType = ParseTypeAnnotation();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after indexed access type.");
                typeName = $"{typeName}[{indexType}]";
            }
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
        // Also handles mapped types: { [K in keyof T]: T[K] }, { +readonly [K in keyof T]-?: T[K] }
        List<string> members = [];

        // Check for mapped type syntax: { [+/-readonly] [K in ...]: ... }
        // Mapped types have a single member that uses 'in' instead of ':'
        if (IsMappedTypeStart())
        {
            return ParseMappedType();
        }

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

    /// <summary>
    /// Checks if the current position starts a mapped type.
    /// Mapped types look like: { [K in ...]: ... } or { +readonly [K in ...]: ... }
    /// </summary>
    private bool IsMappedTypeStart()
    {
        int saved = _current;
        try
        {
            // Skip optional modifiers: +readonly, -readonly, readonly
            if (Check(TokenType.PLUS) || Check(TokenType.MINUS))
            {
                Advance();
                if (!Check(TokenType.READONLY))
                {
                    _current = saved;
                    return false;
                }
                Advance();
            }
            else if (Check(TokenType.READONLY))
            {
                Advance();
            }

            // Must have [ next
            if (!Check(TokenType.LEFT_BRACKET))
            {
                _current = saved;
                return false;
            }
            Advance(); // consume [

            // Must have identifier
            if (!Check(TokenType.IDENTIFIER))
            {
                _current = saved;
                return false;
            }
            Advance();

            // Must have 'in' keyword (distinguishes from index signature which has ':')
            bool isMapped = Check(TokenType.IN);
            _current = saved;
            return isMapped;
        }
        catch
        {
            _current = saved;
            return false;
        }
    }

    /// <summary>
    /// Parses a mapped type: { [+/-readonly] [K in Constraint [as RemapType]][+/-?]: ValueType }
    /// Already consumed LEFT_BRACE.
    /// </summary>
    private string ParseMappedType()
    {
        // Parse optional leading modifiers: +readonly, -readonly, readonly
        string readonlyMod = "";
        if (Match(TokenType.PLUS))
        {
            if (Check(TokenType.READONLY))
            {
                Advance();
                readonlyMod = "+readonly ";
            }
            else
            {
                throw new Exception("Parse Error: Expected 'readonly' after '+' in mapped type.");
            }
        }
        else if (Match(TokenType.MINUS))
        {
            if (Check(TokenType.READONLY))
            {
                Advance();
                readonlyMod = "-readonly ";
            }
            else
            {
                throw new Exception("Parse Error: Expected 'readonly' after '-' in mapped type.");
            }
        }
        else if (Match(TokenType.READONLY))
        {
            readonlyMod = "readonly ";
        }

        // Parse [K in Constraint]
        Consume(TokenType.LEFT_BRACKET, "Expect '[' in mapped type.");

        // Parse type parameter name
        Token paramName = Consume(TokenType.IDENTIFIER, "Expect type parameter name in mapped type.");

        // Expect 'in'
        Consume(TokenType.IN, "Expect 'in' after type parameter in mapped type.");

        // Parse constraint (e.g., keyof T, or a union of string literals)
        string constraint = ParseTypeAnnotation();

        // Parse optional 'as' clause for key remapping
        string asClause = "";
        if (Match(TokenType.AS))
        {
            string remapType = ParseTypeAnnotation();
            asClause = $" as {remapType}";
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after mapped type parameter.");

        // Parse optional trailing modifiers: +?, -?, ?
        string optionalMod = "";
        if (Match(TokenType.PLUS))
        {
            if (Match(TokenType.QUESTION))
            {
                optionalMod = "+?";
            }
            else
            {
                throw new Exception("Parse Error: Expected '?' after '+' in mapped type.");
            }
        }
        else if (Match(TokenType.MINUS))
        {
            if (Match(TokenType.QUESTION))
            {
                optionalMod = "-?";
            }
            else
            {
                throw new Exception("Parse Error: Expected '?' after '-' in mapped type.");
            }
        }
        else if (Match(TokenType.QUESTION))
        {
            optionalMod = "?";
        }

        // Parse : ValueType
        Consume(TokenType.COLON, "Expect ':' after mapped type parameter.");
        string valueType = ParseTypeAnnotation();

        // Handle optional separator and closing brace
        Match(TokenType.SEMICOLON);
        Consume(TokenType.RIGHT_BRACE, "Expect '}' after mapped type.");

        return $"{{ {readonlyMod}[{paramName.Lexeme} in {constraint}{asClause}]{optionalMod}: {valueType} }}";
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
