using System.Text;
using SharpTS.TypeSystem;

namespace SharpTS.Parsing;

public partial class Parser
{
    private string ParseTypeAnnotation()
    {
        // Handle type predicate return types: "asserts x is T", "asserts x", "x is T"
        // These only appear as function return types but are parsed as type annotations

        // Check for "asserts" keyword
        if (Match(TokenType.ASSERTS))
        {
            if (Check(TokenType.IDENTIFIER))
            {
                string paramName = Advance().Lexeme;
                if (Match(TokenType.IS))
                {
                    // asserts x is T
                    string predicateType = ParseConditionalType();
                    return $"asserts {paramName} is {predicateType}";
                }
                else
                {
                    // asserts x (shorthand for asserting non-null/truthy)
                    return $"asserts {paramName}";
                }
            }
            else
            {
                throw new Exception($"Parse Error at line {Previous().Line}: Expected identifier after 'asserts'.");
            }
        }

        // Check for "x is T" pattern (regular type predicate)
        // Must be: identifier followed by 'is' keyword
        if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.IS)
        {
            string paramName = Advance().Lexeme;
            Consume(TokenType.IS, "Expected 'is' after parameter name.");
            string predicateType = ParseConditionalType();
            return $"{paramName} is {predicateType}";
        }

        return ParseConditionalType();
    }

    /// <summary>
    /// Parses conditional types: T extends U ? X : Y
    /// Conditional types have the lowest precedence among type operators.
    /// </summary>
    private string ParseConditionalType()
    {
        string checkType = ParseUnionType();

        // Check for "extends" keyword indicating a conditional type
        if (!Check(TokenType.EXTENDS))
            return checkType;

        // This might be a constraint in generics - we need to look ahead
        // for the ternary operator to confirm this is a conditional type
        int saved = _current;
        Advance(); // consume 'extends'

        // Parse the extends type (which may contain 'infer' keywords)
        string extendsType = ParseUnionType();

        // Must have '?' for this to be a conditional type
        if (!Check(TokenType.QUESTION))
        {
            // Not a conditional type - backtrack
            _current = saved;
            return checkType;
        }

        Advance(); // consume '?'

        // Parse true branch (recursive - can contain nested conditionals)
        string trueType = ParseConditionalType();

        Consume(TokenType.COLON, "Expect ':' in conditional type.");

        // Parse false branch (recursive - can contain nested conditionals)
        string falseType = ParseConditionalType();

        return $"{checkType} extends {extendsType} ? {trueType} : {falseType}";
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

        // Handle infer keyword for conditional types: infer U
        if (Match(TokenType.INFER))
        {
            Token paramName = Consume(TokenType.IDENTIFIER, "Expect type parameter name after 'infer'.");
            return $"infer {paramName.Lexeme}";
        }

        // Handle keyof prefix operator: keyof T
        if (Match(TokenType.KEYOF))
        {
            string innerType = ParsePrimaryType();
            return $"keyof {innerType}";
        }

        // Handle "unique symbol" type annotation
        if (Match(TokenType.UNIQUE))
        {
            if (Match(TokenType.TYPE_SYMBOL))
            {
                return "unique symbol";
            }
            // If "unique" is not followed by "symbol", it's an error in type context
            throw new Exception($"Parse Error at line {Previous().Line}: 'unique' must be followed by 'symbol' in type annotation.");
        }

        // Handle typeof in type position: typeof someVariable, typeof obj.prop, typeof arr[0]
        if (Match(TokenType.TYPEOF))
        {
            StringBuilder sb = new();
            sb.Append("typeof ");

            Token first = Consume(TokenType.IDENTIFIER, "Expect identifier after 'typeof' in type position.");
            sb.Append(first.Lexeme);

            // Handle property paths and index access: typeof obj.prop, typeof arr[0], typeof obj["key"]
            while (true)
            {
                if (Match(TokenType.DOT))
                {
                    Token next = Consume(TokenType.IDENTIFIER, "Expect property name after '.'");
                    sb.Append('.');
                    sb.Append(next.Lexeme);
                }
                else if (Match(TokenType.LEFT_BRACKET))
                {
                    sb.Append('[');
                    // Handle numeric index: arr[0]
                    if (Check(TokenType.NUMBER))
                    {
                        Token num = Advance();
                        sb.Append(num.Lexeme);
                    }
                    // Handle string key: obj["key"]
                    else if (Check(TokenType.STRING))
                    {
                        Token str = Advance();
                        // Literal contains the parsed string value without quotes
                        sb.Append('"');
                        sb.Append((string)str.Literal!);
                        sb.Append('"');
                    }
                    // Handle identifier key: obj[key] (where key is a const)
                    else if (Check(TokenType.IDENTIFIER))
                    {
                        Token id = Advance();
                        sb.Append(id.Lexeme);
                    }
                    else
                    {
                        throw new Exception("Expect number, string, or identifier in typeof index access.");
                    }
                    Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index.");
                    sb.Append(']');
                }
                else
                {
                    break;
                }
            }

            return sb.ToString();
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
        // Handle template literal types: `literal` or `prefix${Type}suffix`
        else if (Match(TokenType.TEMPLATE_FULL))
        {
            typeName = "`" + (string)Previous().Literal! + "`";
        }
        else if (Match(TokenType.TEMPLATE_HEAD))
        {
            typeName = ParseTemplateLiteralType();
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
                 Check(TokenType.NULL) || Check(TokenType.UNDEFINED) || Check(TokenType.UNKNOWN) || Check(TokenType.NEVER))
        {
            typeName = Advance().Lexeme;
        }
        else
        {
            throw new Exception("Expect type.");
        }

        // Handle generic type arguments: Container<number>, Map<string, number>
        // Uses MatchGreaterInTypeContext() to handle nested generics like Partial<Readonly<T>>
        // where the lexer produces >> as a single token that we need to split.
        if (Check(TokenType.LESS))
        {
            int saved = _current;
            Advance(); // consume <
            if (IsTypeStart())
            {
                List<string> typeArgs = [ParseTypeAnnotation()];
                while (Match(TokenType.COMMA))
                    typeArgs.Add(ParseTypeAnnotation());
                if (MatchGreaterInTypeContext())
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
            // Check for spread or rest element: ...T or ...Type[]
            if (Match(TokenType.DOT_DOT_DOT))
            {
                string spreadType = ParsePrimaryType();

                if (spreadType.EndsWith("[]"))
                {
                    // Trailing rest element (...T[]) - must be last
                    if (!Check(TokenType.RIGHT_BRACKET) && !Check(TokenType.COMMA))
                    {
                        // More content after - this is a variadic spread, allow it
                        elements.Add("..." + spreadType);
                    }
                    else if (!Check(TokenType.RIGHT_BRACKET))
                    {
                        // Followed by comma - check what comes next
                        elements.Add("..." + spreadType);
                    }
                    else
                    {
                        // At end - trailing rest element
                        elements.Add("..." + spreadType);
                        break;
                    }
                }
                else
                {
                    // Variadic spread (...T) - can appear anywhere
                    elements.Add("..." + spreadType);
                }

                if (!Check(TokenType.RIGHT_BRACKET))
                    Consume(TokenType.COMMA, "Expect ',' between tuple elements.");
                continue;
            }

            string elementType;

            // Check for named tuple element: name: type or name?: type
            // Pattern: identifier followed by colon, OR identifier followed by ? then colon
            bool isNamedElement = Check(TokenType.IDENTIFIER) &&
                (PeekNext().Type == TokenType.COLON ||
                 (PeekNext().Type == TokenType.QUESTION && _current + 2 < _tokens.Count && _tokens[_current + 2].Type == TokenType.COLON));

            if (isNamedElement)
            {
                Token name = Advance(); // consume identifier
                bool isOptional = Match(TokenType.QUESTION); // consume ? if present (for name?: type)
                Consume(TokenType.COLON, ""); // consume colon
                string innerType = ParseUnionType();
                elementType = isOptional ? $"{name.Lexeme}?: {innerType}" : $"{name.Lexeme}: {innerType}";
            }
            else
            {
                elementType = ParseUnionType(); // Support union elements like [string | number, boolean]

                // Check for optional marker on unnamed element
                if (Match(TokenType.QUESTION))
                    elementType += "?";
            }

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
    /// Parses type parameters like &lt;T, U extends Base&gt;, &lt;T = string, U extends Base = number&gt;,
    /// &lt;const T&gt; (TypeScript 5.0+ const type parameters), or &lt;out T&gt;, &lt;in T&gt;, &lt;in out T&gt;
    /// (TypeScript 4.7+ variance annotations).
    /// Returns null if no type parameters are present.
    /// Supports variance modifiers (in, out, in out), const modifier, constraints (extends), and default types (=).
    /// </summary>
    private List<TypeParam>? ParseTypeParameters()
    {
        if (!Match(TokenType.LESS)) return null;

        List<TypeParam> typeParams = [];
        bool sawDefault = false;

        do
        {
            // Check for variance modifiers: in, out, in out
            var variance = TypeParameterVariance.Invariant;
            if (Match(TokenType.IN))
            {
                if (Match(TokenType.OUT))
                {
                    variance = TypeParameterVariance.InOut;
                }
                else
                {
                    variance = TypeParameterVariance.In;
                }
            }
            else if (Match(TokenType.OUT))
            {
                variance = TypeParameterVariance.Out;
            }

            // Check for 'const' modifier (TypeScript 5.0+ feature)
            bool isConst = Match(TokenType.CONST);

            Token name = Consume(TokenType.IDENTIFIER, "Expect type parameter name.");
            string? constraint = null;
            string? defaultType = null;

            // Parse optional constraint: extends SomeType
            if (Match(TokenType.EXTENDS))
            {
                constraint = ParseTypeAnnotation();
            }

            // Parse optional default: = SomeType
            if (Match(TokenType.EQUAL))
            {
                defaultType = ParseTypeAnnotation();
                sawDefault = true;
            }
            else if (sawDefault)
            {
                // TypeScript requires: required type parameters cannot follow optional ones
                throw new Exception($"Parse Error: Required type parameter '{name.Lexeme}' cannot follow optional type parameter with default.");
            }

            typeParams.Add(new TypeParam(name, constraint, defaultType, isConst, variance));
        } while (Match(TokenType.COMMA));

        ConsumeGreaterInTypeContext("Expect '>' after type parameters.");
        return typeParams;
    }

    /// <summary>
    /// Tries to parse type arguments like &lt;number, string&gt;.
    /// Returns null if not valid type arguments (backtracking safe).
    /// Uses CheckGreaterInTypeContext/MatchGreaterInTypeContext to handle nested generics.
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

            if (!CheckGreaterInTypeContext()) { _current = saved; return null; }
            MatchGreaterInTypeContext(); // consume >
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
    /// Uses CheckGreaterInTypeContext/MatchGreaterInTypeContext to handle nested generics.
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

            if (!CheckGreaterInTypeContext()) { _current = saved; return null; }
            MatchGreaterInTypeContext(); // consume >

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

    // ============== TEMPLATE LITERAL TYPE PARSING ==============

    /// <summary>
    /// Parses a template literal type after consuming TEMPLATE_HEAD.
    /// Returns the string representation: `prefix${Type}middle${Type}suffix`
    /// </summary>
    private string ParseTemplateLiteralType()
    {
        var sb = new StringBuilder("`");
        sb.Append((string)Previous().Literal!); // head string

        // Parse first interpolated type
        sb.Append("${");
        sb.Append(ParseUnionType()); // Allow unions inside interpolation
        sb.Append('}');

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            sb.Append((string)Previous().Literal!);
            sb.Append("${");
            sb.Append(ParseUnionType());
            sb.Append('}');
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal type.");
        sb.Append((string)Previous().Literal!);
        sb.Append('`');

        return sb.ToString();
    }
}
