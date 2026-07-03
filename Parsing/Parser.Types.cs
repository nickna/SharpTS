using System.Text;
using SharpTS.TypeSystem;

namespace SharpTS.Parsing;

public partial class Parser
{
    /// <summary>
    /// Side channel for the type-AST migration (docs/plans/type-ast-design.md): type-parsing
    /// functions that support node construction publish the node for the string they just
    /// returned here. Consumers read-and-clear via <see cref="TakeTypeNode"/> immediately after
    /// the call. Functions without node support leave it null (cleared at each producer's entry),
    /// so an unsupported construct anywhere inside a composite yields no node and the consumer
    /// falls back to the authoritative string path.
    /// </summary>
    private TypeNode? _lastTypeNode;

    /// <summary>
    /// True while parsing a position where conditional types are not allowed: the extends clause
    /// of a conditional type and the constraint of an <c>infer</c> declaration (tsc's
    /// DisallowConditionalTypes context). Read by the <c>infer ... extends</c> disambiguation:
    /// in an allow-conditionals context, `infer U extends T ?` re-parses with `extends` starting a
    /// conditional type instead of a constraint. Bracketed sub-positions (grouped types, tuple
    /// elements, object members, mapped-type clauses, type arguments) re-allow conditionals by
    /// re-entering <see cref="ParseConditionalType"/>, which saves/clears/restores the flag.
    /// </summary>
    private bool _disallowConditionalTypes;

    /// <summary>Reads and clears the node for the most recent type-parse call.</summary>
    private TypeNode? TakeTypeNode()
    {
        var node = _lastTypeNode;
        _lastTypeNode = null;
        return node;
    }

    /// <summary>
    /// Parses a standalone type-annotation fragment (<c>{ a: number }</c>, <c>A | B[]</c>,
    /// <c>`a${string}`</c>) to its <see cref="TypeNode"/>. Null when the text is not a complete
    /// type: lex/parse error, no node produced, or trailing tokens after the type. This is the
    /// string entry for the checker's <c>ToTypeInfo(string)</c> (fallback + embedding surface) —
    /// the same real lexer + type grammar as source annotations, replacing the retired
    /// char-scanning re-parser. The try/catch covers lex/parse ONLY; resolution errors
    /// (TS2456/TS2314/TS1331, …) happen in the checker and propagate there.
    /// </summary>
    internal static TypeNode? TryParseTypeFragment(string annotation)
    {
        try
        {
            var parser = new Parser(new Lexer(annotation).ScanTokens());
            parser.ParseTypeAnnotation();          // rendered string result discarded
            var node = parser.TakeTypeNode();
            return parser.IsAtEnd() ? node : null; // reject partial parses ("number garbage")
        }
        catch
        {
            return null;
        }
    }

    private string ParseTypeAnnotation()
    {
        _lastTypeNode = null;
        // Handle type predicate return types: "asserts x is T", "asserts x", "x is T"
        // These only appear as function return types but are parsed as type annotations

        // Check for "asserts" keyword
        if (Match(TokenType.ASSERTS))
        {
            if (Check(TokenType.IDENTIFIER))
            {
                string paramName = Advance().Lexeme;
                int assertsLine = Previous().Line;
                if (Match(TokenType.IS))
                {
                    // asserts x is T
                    string predicateType = ParseConditionalType();
                    _lastTypeNode = TakeTypeNode() is { } predNode
                        ? new TypePredicateNode(paramName, predNode, IsAssertion: true, assertsLine)
                        : null;
                    return $"asserts {paramName} is {predicateType}";
                }
                else
                {
                    // asserts x (shorthand for asserting non-null/truthy)
                    _lastTypeNode = new AssertsNonNullTypeNode(paramName, assertsLine);
                    return $"asserts {paramName}";
                }
            }
            else
            {
                throw new Exception($"Parse Error at line {Previous().Line}: Expected identifier after 'asserts'.");
            }
        }

        // Check for "x is T" / "this is T" type predicate: an identifier (or `this`) followed by `is`.
        if ((Check(TokenType.IDENTIFIER) || Check(TokenType.THIS)) && PeekNext().Type == TokenType.IS)
        {
            int predLine = Peek().Line;
            string paramName = Advance().Lexeme;
            Consume(TokenType.IS, "Expected 'is' after parameter name.");
            string predicateType = ParseConditionalType();
            _lastTypeNode = TakeTypeNode() is { } predNode
                ? new TypePredicateNode(paramName, predNode, IsAssertion: false, predLine)
                : null;
            return $"{paramName} is {predicateType}";
        }

        return ParseConditionalType();
    }

    /// <summary>
    /// Parses the body of a function type after the opening '(' has been consumed.
    /// Handles: this parameter, regular/optional/rest parameters, closing ')', '=>', and return type.
    /// Returns the complete function type string like "(number, string) => void".
    /// </summary>
    private string ParseFunctionTypeBody()
    {
        int startLine = Previous().Line; // the '(' the caller consumed
        string? thisType = null;
        TypeNode? thisTypeNode = null;
        List<string> paramTypes = [];
        List<ParameterTypeNode> paramNodes = [];
        bool nodeComplete = true; // false once any component lacks a node → whole type falls back

        // Check for 'this' parameter: (this: Window, ...)
        if (Check(TokenType.THIS) && PeekNext().Type == TokenType.COLON)
        {
            Advance(); // consume 'this'
            Consume(TokenType.COLON, "Expect ':' after 'this' in function type.");
            thisType = ParseTypeAnnotation();
            thisTypeNode = TakeTypeNode();
            if (thisTypeNode is null) nodeComplete = false;
            if (Check(TokenType.COMMA))
            {
                Advance(); // consume ','
            }
        }

        // Parse parameter types
        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Handle rest parameter: ...args: number[]
                bool isRest = Match(TokenType.DOT_DOT_DOT);

                // Handle optional parameter marker: x?: number
                bool isOptional = false;

                // Parameter can be: name: type, name?: type, name (bare, implicit any),
                // name? (bare optional, implicit any), or just a type expression.
                string paramType;
                string? paramName = null;
                TypeNode? paramTypeNode;
                if ((Check(TokenType.IDENTIFIER) || IsContextualKeyword(Peek().Type)) &&
                    (PeekNext().Type == TokenType.COLON || PeekNext().Type == TokenType.QUESTION))
                {
                    paramName = Advance().Lexeme; // name may be a contextual keyword, e.g. `set: Set<T>`
                    if (Match(TokenType.QUESTION))
                    {
                        isOptional = true;
                    }
                    // The type annotation is optional: `(x?) => R` / `(x) => R` give the parameter
                    // an implicit `any` type (the name is a label only).
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                        paramTypeNode = TakeTypeNode();
                    }
                    else
                    {
                        paramType = "any";
                        paramTypeNode = new NamedTypeNode("any", null, Previous().Line);
                    }
                }
                else if (!isRest && Check(TokenType.IDENTIFIER) &&
                         (PeekNext().Type == TokenType.RIGHT_PAREN || PeekNext().Type == TokenType.COMMA))
                {
                    // Bare parameter name with no annotation: implicit `any`.
                    paramName = Advance().Lexeme;
                    paramType = "any";
                    paramTypeNode = new NamedTypeNode("any", null, Previous().Line);
                }
                else
                {
                    paramType = ParseTypeAnnotation();
                    paramTypeNode = TakeTypeNode();
                }

                if (paramTypeNode is null)
                    nodeComplete = false;
                else
                    paramNodes.Add(new ParameterTypeNode(paramName, paramTypeNode, isOptional, isRest, paramTypeNode.Line));

                // Preserve optional/rest info in the type string representation
                if (isRest)
                    paramTypes.Add("..." + paramType);
                else if (isOptional)
                    paramTypes.Add(paramType + "?");
                else
                    paramTypes.Add(paramType);

            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_PAREN, "Expect ')' after function type parameters.");
        Consume(TokenType.ARROW, "Expect '=>' after function type parameters.");
        string returnType = ParseTypeAnnotation();
        TypeNode? returnTypeNode = TakeTypeNode();

        // Publish the structured form (or explicitly clear, so no nested node leaks out).
        _lastTypeNode = nodeComplete && returnTypeNode is not null
            ? new FunctionTypeNode(thisTypeNode, paramNodes, returnTypeNode, startLine)
            : null;

        // Build the function type string
        if (thisType != null)
        {
            return $"(this: {thisType}, {string.Join(", ", paramTypes)}) => {returnType}";
        }
        return $"({string.Join(", ", paramTypes)}) => {returnType}";
    }

    /// <summary>
    /// Speculative lookahead used in type position: with the opening '(' already consumed, scans to
    /// the matching ')' and reports whether a '=>' immediately follows. This distinguishes a function
    /// type whose first parameter is a bare untyped name — <c>(x) =&gt; R</c>, <c>(x, y) =&gt; R</c> —
    /// from a grouped type such as <c>(Foo)</c> or <c>(A | B)</c>. Restores the position before returning.
    /// </summary>
    private bool ParenGroupFollowedByArrow()
    {
        int saved = _current;
        int depth = 1; // the opening '(' has already been consumed
        while (!IsAtEnd() && depth > 0)
        {
            var type = Advance().Type;
            if (type == TokenType.LEFT_PAREN) depth++;
            else if (type == TokenType.RIGHT_PAREN) depth--;
        }
        bool followedByArrow = depth == 0 && Check(TokenType.ARROW);
        _current = saved;
        return followedByArrow;
    }

    /// <summary>
    /// Parses conditional types: T extends U ? X : Y
    /// Conditional types have the lowest precedence among type operators.
    /// </summary>
    private string ParseConditionalType()
    {
        // Entering a full-type position re-allows conditional types (tsc: parseType resets the
        // DisallowConditionalTypes context). Restored before every return so an enclosing extends
        // clause keeps its disallow state after a bracketed sub-parse.
        bool savedDisallow = _disallowConditionalTypes;
        _disallowConditionalTypes = false;
        try
        {
            return ParseConditionalTypeCore();
        }
        finally
        {
            _disallowConditionalTypes = savedDisallow;
        }
    }

    private string ParseConditionalTypeCore()
    {
        string checkType = ParseUnionType();
        TypeNode? checkNode = TakeTypeNode();

        // Check for "extends" keyword indicating a conditional type
        if (!Check(TokenType.EXTENDS))
        {
            _lastTypeNode = checkNode;
            return checkType;
        }

        // This might be a constraint in generics - we need to look ahead
        // for the ternary operator to confirm this is a conditional type
        int saved = _current;
        Advance(); // consume 'extends'

        int conditionalLine = Previous().Line;

        // Parse the extends type (which may contain 'infer' keywords). Conditional types are
        // not allowed directly in an extends clause, which is what lets `infer U extends C`
        // keep its constraint here (X13-style) while re-allowing inside brackets.
        _disallowConditionalTypes = true;
        string extendsType = ParseUnionType();
        _disallowConditionalTypes = false;
        TypeNode? extendsNode = TakeTypeNode();

        // Must have '?' for this to be a conditional type
        if (!Check(TokenType.QUESTION))
        {
            // Not a conditional type - backtrack
            _current = saved;
            _lastTypeNode = checkNode;
            return checkType;
        }

        Advance(); // consume '?'

        // Parse true branch (recursive - can contain nested conditionals)
        string trueType = ParseConditionalType();
        TypeNode? trueNode = TakeTypeNode();

        Consume(TokenType.COLON, "Expect ':' in conditional type.");

        // Parse false branch (recursive - can contain nested conditionals)
        string falseType = ParseConditionalType();
        TypeNode? falseNode = TakeTypeNode();

        _lastTypeNode = checkNode is not null && extendsNode is not null
                        && trueNode is not null && falseNode is not null
            ? new ConditionalTypeNode(checkNode, extendsNode, trueNode, falseNode, conditionalLine)
            : null;
        return $"{checkType} extends {extendsType} ? {trueType} : {falseType}";
    }

    private string ParseUnionType()
    {
        // Tolerate a leading '|' before the first member (common when each member
        // is written on its own line):  type T = | A | B;
        Match(TokenType.PIPE);

        // Union has lower precedence than intersection, so parse intersection first
        List<string> types = [ParseIntersectionType()];
        List<TypeNode>? memberNodes = TakeTypeNode() is { } firstNode ? [firstNode] : null;

        while (Match(TokenType.PIPE))
        {
            types.Add(ParseIntersectionType());
            var node = TakeTypeNode();
            if (memberNodes is not null && node is not null)
                memberNodes.Add(node);
            else
                memberNodes = null; // any node-less member disables the union node
        }

        if (types.Count == 1)
        {
            _lastTypeNode = memberNodes is { Count: 1 } ? memberNodes[0] : null;
            return types[0];
        }
        _lastTypeNode = memberNodes is { } all && all.Count == types.Count
            ? new UnionTypeNode(all, Peek().Line)
            : null;
        return string.Join(" | ", types);
    }

    /// <summary>
    /// Parses intersection types (A &amp; B). Intersection binds tighter than union,
    /// so A | B &amp; C is parsed as A | (B &amp; C).
    /// </summary>
    private string ParseIntersectionType()
    {
        // Tolerate a leading '&' before the first member (common when each member
        // is written on its own line):  type T = & A & B;
        Match(TokenType.AMPERSAND);

        int startLine = Peek().Line;
        List<string> types = [ParsePrimaryType()];
        List<TypeNode>? memberNodes = TakeTypeNode() is { } firstNode ? [firstNode] : null;

        while (Match(TokenType.AMPERSAND))
        {
            types.Add(ParsePrimaryType());
            var node = TakeTypeNode();
            if (memberNodes is not null && node is not null)
                memberNodes.Add(node);
            else
                memberNodes = null; // any node-less member disables the intersection node
        }

        if (types.Count == 1)
        {
            _lastTypeNode = memberNodes is { Count: 1 } ? memberNodes[0] : null;
            return types[0];
        }
        _lastTypeNode = memberNodes is { } all && all.Count == types.Count
            ? new IntersectionTypeNode(all, startLine)
            : null;
        return string.Join(" & ", types);
    }

    private string ParsePrimaryType()
    {
        string typeName;
        // Node under construction for this primary type (type-AST migration). Branches with node
        // support assign it; the common tail publishes it. EARLY returns clear the side channel
        // explicitly so nodes from nested sub-parses cannot leak onto unrelated strings.
        TypeNode? typeNode = null;
        _lastTypeNode = null;

        // `readonly` array/tuple modifier: `readonly T[]`, `readonly [A, B]` (lib.d.ts). Handled at
        // the primary-type level so it binds correctly inside unions (`readonly T[] | U`) and other
        // nested positions. ToTypeInfo marks the array/tuple readonly.
        if (Check(TokenType.READONLY))
        {
            int readonlyLine = Peek().Line;
            Advance();
            string readonlyInner = ParsePrimaryType();
            _lastTypeNode = TakeTypeNode() is { } innerNode ? new ReadonlyTypeNode(innerNode, readonlyLine) : null;
            return "readonly " + readonlyInner;
        }

        // Handle infer keyword for conditional types: infer U, or constrained infer: infer U extends C
        if (Match(TokenType.INFER))
        {
            Token paramName = Consume(TokenType.IDENTIFIER, "Expect type parameter name after 'infer'.");
            if (Check(TokenType.EXTENDS))
            {
                int savedPos = _current;
                bool outerDisallow = _disallowConditionalTypes;
                Advance(); // consume 'extends'
                // The constraint itself never contains a top-level conditional (tsc parses it in a
                // DisallowConditionalTypes context); union level is the ceiling.
                _disallowConditionalTypes = true;
                string constraint = ParseUnionType();
                TypeNode? constraintNode = TakeTypeNode();
                _disallowConditionalTypes = outerDisallow;
                if (!outerDisallow && Check(TokenType.QUESTION))
                {
                    // `infer U extends T ?` where conditional types are allowed: the `extends`
                    // starts a conditional type whose check type is the bare `infer U`, not a
                    // constraint (tsc's speculative lookahead). Backtrack to before 'extends'.
                    _current = savedPos;
                }
                else
                {
                    // Node requires the constraint node; a constraint without node support drops
                    // the whole infer to the string path rather than losing the constraint.
                    _lastTypeNode = constraintNode is not null
                        ? new InferTypeNode(paramName.Lexeme, paramName.Line, constraintNode)
                        : null;
                    return $"infer {paramName.Lexeme} extends {constraint}";
                }
            }
            _lastTypeNode = new InferTypeNode(paramName.Lexeme, paramName.Line);
            return $"infer {paramName.Lexeme}";
        }

        // `abstract` constructor type: abstract new (params) => ReturnType. The abstract modifier
        // doesn't change the structural shape, so parse it like a regular constructor type.
        if (Check(TokenType.ABSTRACT) && PeekNext().Type == TokenType.NEW)
        {
            Advance(); // consume 'abstract'
        }

        // Handle constructor type: new (params) => ReturnType  or  new <T>(params) => ReturnType.
        // Represented as an object type with a construct signature so it flows through the existing
        // constructable-object-type modelling.
        if (Match(TokenType.NEW))
        {
            int ctorLine = Previous().Line;
            string genericPrefix = "";
            List<TypeParam>? ctorTypeParams = null;
            if (Check(TokenType.LESS))
            {
                ctorTypeParams = ParseTypeParameters();
                genericPrefix = FormatTypeParams(ctorTypeParams);
            }
            Consume(TokenType.LEFT_PAREN, "Expect '(' in constructor type.");
            string ctorBody = ParseFunctionTypeBody(); // returns "(params) => ReturnType"
            // The construct signature (with or without type parameters) carries onto a node; a
            // `this` pseudo-parameter rides along and is dropped at resolution, string-path style.
            _lastTypeNode = TakeTypeNode() is FunctionTypeNode ctorFn
                ? (ctorTypeParams is { Count: > 0 }
                    ? new GenericConstructorTypeNode(ctorTypeParams, ctorFn, ctorLine)
                    : new ConstructorTypeNode(ctorFn.Parameters, ctorFn.ReturnType, ctorFn.Line, ctorFn.ThisType))
                : null;
            return $"{{ new {genericPrefix}{ctorBody} }}";
        }

        // Handle keyof prefix operator: keyof T
        if (Match(TokenType.KEYOF))
        {
            int keyofLine = Previous().Line;
            string innerType = ParsePrimaryType();
            _lastTypeNode = TakeTypeNode() is { } innerNode ? new KeyofTypeNode(innerNode, keyofLine) : null;
            return $"keyof {innerType}";
        }

        // Handle "unique symbol" type annotation
        if (Match(TokenType.UNIQUE))
        {
            if (Match(TokenType.TYPE_SYMBOL))
            {
                _lastTypeNode = new UniqueSymbolTypeNode(Previous().Line);
                return "unique symbol";
            }
            // If "unique" is not followed by "symbol", it's an error in type context
            throw new Exception($"Parse Error at line {Previous().Line}: 'unique' must be followed by 'symbol' in type annotation.");
        }

        // Handle typeof in type position: typeof someVariable, typeof obj.prop, typeof arr[0]
        if (Match(TokenType.TYPEOF))
        {
            int typeofLine = Previous().Line;
            StringBuilder sb = new();
            sb.Append("typeof ");

            // `typeof undefined` / `typeof this` are valid queries alongside identifiers.
            Token first = Check(TokenType.UNDEFINED) || Check(TokenType.THIS)
                ? Advance()
                : Consume(TokenType.IDENTIFIER, "Expect identifier after 'typeof' in type position.");
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

            // The entity path is everything after the "typeof " prefix; the resolver hands it to
            // the same EvaluateTypeOf the string path uses.
            _lastTypeNode = new TypeQueryNode(sb.ToString()["typeof ".Length..], typeofLine);
            return sb.ToString();
        }

        // Handle generic function type: <T>(params) => ReturnType
        // A leading '<' in type position can only begin a generic function type's
        // type-parameter list (e.g. `<U extends boolean>(a: U) => never`).
        if (Check(TokenType.LESS))
        {
            int genericLine = Peek().Line;
            List<TypeParam>? typeParams = ParseTypeParameters(); // consumes <...>
            Consume(TokenType.LEFT_PAREN, "Expect '(' after type parameters in function type.");
            string body = ParseFunctionTypeBody(); // returns "(params) => ReturnType"
            // The checker resolves type-parameter constraints/defaults from their TypeParam strings
            // in a fresh scope, so the node only needs the params plus a node-formed body.
            _lastTypeNode = typeParams is { Count: > 0 } && TakeTypeNode() is FunctionTypeNode bodyNode
                ? new GenericFunctionTypeNode(typeParams, bodyNode, genericLine)
                : null;
            string genericPrefix = FormatTypeParams(typeParams);
            return $"{genericPrefix}{body}";
        }

        // Handle tuple type syntax: [string, number, boolean?]
        // Assigns typeName (rather than returning) so the array-suffix / indexed-access
        // loop below applies, e.g. [string, number][0] or [string, number][].
        if (Match(TokenType.LEFT_BRACKET))
        {
            typeName = ParseTupleType();
            typeNode = TakeTypeNode();
        }
        // Handle inline object type syntax: { name: string; age?: number }
        // Assigns typeName (rather than returning) so postfix indexed access applies to
        // object/mapped type literals, e.g. { [K in keyof T]: T[K] }[keyof T].
        else if (Match(TokenType.LEFT_BRACE))
        {
            typeName = ParseInlineObjectType();
            typeNode = TakeTypeNode();
        }
        // Handle parenthesized types: (string | number) or function types: (x: number) => number
        else if (Match(TokenType.LEFT_PAREN))
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
            else if ((Check(TokenType.IDENTIFIER) || Check(TokenType.THIS)) &&
                     (PeekNext().Type == TokenType.COLON || PeekNext().Type == TokenType.QUESTION))
            {
                // (identifier: / (this: / (identifier? - a (possibly optional) function parameter
                isFunctionType = true;
            }
            else if (Check(TokenType.DOT_DOT_DOT))
            {
                // (...rest: T) => R - rest parameter as the first parameter
                isFunctionType = true;
            }
            else if (Check(TokenType.IDENTIFIER) && ParenGroupFollowedByArrow())
            {
                // (x) => R, (x, y) => R - function type with bare (implicitly-`any`) parameter
                // names. Disambiguated from a grouped type (e.g. `(Foo)` or `(A | B)`) by the `=>`
                // that follows the matching ')'.
                isFunctionType = true;
            }

            if (isFunctionType)
            {
                typeName = ParseFunctionTypeBody();
                typeNode = TakeTypeNode();
            }
            else
            {
                // Parse as grouped type: (type1 | type2), or a parenthesized conditional type
                // (T extends U ? X : Y). Use ParseConditionalType so the grouped body can contain
                // `extends ? :` rather than stopping at `extends`.
                typeName = "(" + ParseConditionalType() + ")";
                typeNode = TakeTypeNode(); // parens are semantically transparent
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after grouped type.");
            }
        }
        // Handle template literal types: `literal` or `prefix${Type}suffix`
        else if (Match(TokenType.TEMPLATE_FULL))
        {
            string literal = ((TemplateStringValue)Previous().Literal!).Cooked ?? "";
            typeName = "`" + literal + "`";
            // No interpolations — a single static segment (resolves to a string-literal type).
            typeNode = new TemplateLiteralTypeNode([literal], [], Previous().Line);
        }
        else if (Match(TokenType.TEMPLATE_HEAD))
        {
            typeName = ParseTemplateLiteralType();
            typeNode = TakeTypeNode();
        }
        // Handle string literal types: "success" | "error"
        else if (Match(TokenType.STRING))
        {
            typeName = "\"" + (string)Previous().Literal! + "\"";
            typeNode = new LiteralTypeNode(Previous().Literal, Previous().Line);
        }
        // Handle number literal types: 0 | 1 | 2
        else if (Match(TokenType.NUMBER))
        {
            typeName = Previous().Literal!.ToString()!;
            typeNode = new LiteralTypeNode(Previous().Literal, Previous().Line);
        }
        // Handle bigint literal types: 1n | 2n
        else if (Match(TokenType.BIGINT_LITERAL))
        {
            typeName = Previous().Literal!.ToString()! + "n";
            typeNode = new LiteralTypeNode(Previous().Literal, Previous().Line); // BigInteger value
        }
        // Handle boolean literal types: true | false
        else if (Match(TokenType.TRUE))
        {
            typeName = "true";
            typeNode = new LiteralTypeNode(true, Previous().Line);
        }
        else if (Match(TokenType.FALSE))
        {
            typeName = "false";
            typeNode = new LiteralTypeNode(false, Previous().Line);
        }
        else if (Check(TokenType.TYPE_STRING) || Check(TokenType.TYPE_NUMBER) ||
                 Check(TokenType.TYPE_BOOLEAN) || Check(TokenType.TYPE_SYMBOL) ||
                 Check(TokenType.TYPE_BIGINT) ||
                 Check(TokenType.IDENTIFIER) ||
                 Check(TokenType.SYMBOL) || Check(TokenType.BIGINT) ||  // `Symbol`/`BigInt` as type names (lib.d.ts: `interface Symbol`)
                 Check(TokenType.THIS) ||  // polymorphic 'this' type (e.g. `): this`, `keyof this`)
                 Check(TokenType.VOID) ||  // void type
                 Check(TokenType.NULL) || Check(TokenType.UNDEFINED) || Check(TokenType.UNKNOWN) || Check(TokenType.NEVER))
        {
            typeName = Advance().Lexeme;
            typeNode = new NamedTypeNode(typeName, null, Previous().Line);

            // Qualified type name (namespace member): `Intl.CollatorOptions`, `NodeJS.Timer`.
            // The dotted name carries on the NamedTypeNode; resolution hands the whole "Foo.Bar"
            // to the same single-name path the string side uses (and ResolveGenericType for
            // `Foo.Bar<T>`), so namespace lookup is identical.
            while (Check(TokenType.DOT) &&
                   (PeekNext().Type == TokenType.IDENTIFIER || IsContextualKeyword(PeekNext().Type)))
            {
                Advance(); // consume '.'
                typeName += "." + Advance().Lexeme;
                typeNode = new NamedTypeNode(typeName, null, Previous().Line);
            }
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
                List<TypeNode>? argNodes = [];
                List<string> typeArgs = [ParseTypeAnnotation()];
                if (TakeTypeNode() is { } firstArgNode) argNodes.Add(firstArgNode);
                else argNodes = null;
                while (Match(TokenType.COMMA))
                {
                    typeArgs.Add(ParseTypeAnnotation());
                    // Always drain the side channel, even once a previous argument had no node.
                    var argNode = TakeTypeNode();
                    if (argNodes is not null && argNode is not null) argNodes.Add(argNode);
                    else argNodes = null;
                }
                if (MatchGreaterInTypeContext())
                {
                    typeName = $"{typeName}<{string.Join(", ", typeArgs)}>";
                    // Attach argument nodes to the bare reference (qualified names stay node-less).
                    typeNode = typeNode is NamedTypeNode { TypeArguments: null } bare && argNodes is not null
                        ? bare with { TypeArguments = argNodes }
                        : null;
                }
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
                typeNode = typeNode is null ? null : new ArrayTypeNode(typeNode, Previous().Line);
            }
            else
            {
                // Indexed access type: T[K] or T["key"]
                int indexLine = Previous().Line;
                string indexType = ParseTypeAnnotation();
                TypeNode? indexNode = TakeTypeNode();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after indexed access type.");
                typeNode = typeNode is { } objNode && indexNode is not null
                    ? new IndexedAccessTypeNode(objNode, indexNode, indexLine)
                    : null;
                typeName = $"{typeName}[{indexType}]";
            }
        }

        _lastTypeNode = typeNode;
        return typeName;
    }

    private string ParseTupleType()
    {
        // Already consumed LEFT_BRACKET
        int startLine = Previous().Line;
        List<string> elements = [];
        List<TupleElementNode> elementNodes = [];
        bool nodeComplete = true;

        while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
        {
            // Check for spread or rest element: ...T or ...Type[]
            if (Match(TokenType.DOT_DOT_DOT))
            {
                string spreadType = ParsePrimaryType();
                // The resolver distinguishes a trailing rest (`...T[]`) from a variadic spread
                // TEXTUALLY (EndsWith "[]"). Only carry a node when the structured view agrees —
                // e.g. a grouped `...(T[])` reads as an array node but not as a "[]" suffix.
                if (TakeTypeNode() is { } spreadNode && spreadType.EndsWith("[]") == spreadNode is ArrayTypeNode)
                    elementNodes.Add(new TupleElementNode(null, spreadNode, false, true, spreadNode.Line));
                else
                    nodeComplete = false;

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
                // The resolver rejects type-keyword names ("string:", "object:", …) and reparses
                // the whole element as a type — don't model those as named elements.
                if (TakeTypeNode() is { } innerNode && IsValidTupleElementNameLexeme(name.Lexeme))
                    elementNodes.Add(new TupleElementNode(name.Lexeme, innerNode, isOptional, false, innerNode.Line));
                else
                    nodeComplete = false;
            }
            else
            {
                elementType = ParseUnionType(); // Support union elements like [string | number, boolean]
                TypeNode? elementNode = TakeTypeNode();

                // Check for optional marker on unnamed element
                bool isOptional = Match(TokenType.QUESTION);
                if (isOptional)
                    elementType += "?";

                if (elementNode is { })
                    elementNodes.Add(new TupleElementNode(null, elementNode, isOptional, false, elementNode.Line));
                else
                    nodeComplete = false;
            }

            elements.Add(elementType);

            if (!Check(TokenType.RIGHT_BRACKET))
                Consume(TokenType.COMMA, "Expect ',' between tuple elements.");
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after tuple type.");
        _lastTypeNode = nodeComplete ? new TupleTypeNode(elementNodes, startLine) : null;
        return "[" + string.Join(", ", elements) + "]";
    }

    /// <summary>
    /// Mirror of the resolver's tuple-element-name validation: the string path only treats
    /// <c>name:</c> as a label when it isn't a type keyword (otherwise the element re-parses as a
    /// type). Token-level parsing already guarantees identifier shape; only the keyword list matters.
    /// </summary>
    private static bool IsValidTupleElementNameLexeme(string s) =>
        s is not ("string" or "number" or "boolean" or "void" or "null" or "undefined"
                or "unknown" or "never" or "any" or "symbol" or "bigint" or "object");

    private string ParseInlineObjectType()
    {
        // Already consumed LEFT_BRACE
        // Parses: { name: string; age?: number; greet(x: number): string; [key: string]: number }
        // Also handles mapped types: { [K in keyof T]: T[K] }, { +readonly [K in keyof T]-?: T[K] }
        int startLine = Previous().Line;
        List<string> members = [];
        List<ObjectTypeMemberNode> memberNodes = [];
        bool nodeComplete = true;

        // Check for mapped type syntax: { [+/-readonly] [K in ...]: ... }
        // Mapped types have a single member that uses 'in' instead of ':'
        if (IsMappedTypeStart())
        {
            return ParseMappedType(); // sets _lastTypeNode (or null) itself
        }

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Optional readonly modifier before an index signature or computed member.
            bool bracketReadonly = Check(TokenType.READONLY) && PeekNext().Type == TokenType.LEFT_BRACKET;
            if (bracketReadonly) Advance();

            // `[` begins either an index signature (`[k: string]: T`) or a computed member name
            // (`[Symbol.iterator](): T`, `[Symbol.match]: T`). Distinguish by `identifier :`.
            if (Check(TokenType.LEFT_BRACKET) && !(PeekNext().Type == TokenType.IDENTIFIER && PeekAt(2).Type == TokenType.COLON))
            {
                Advance(); // consume [
                string raw = "";
                while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
                    raw += Advance().Lexeme;
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed member name.");
                string computedName = raw.StartsWith("Symbol.") ? "@@" + raw["Symbol.".Length..] : "@@" + raw;
                bool computedOptional = Match(TokenType.QUESTION);
                string computedType;
                int computedLine = Previous().Line;
                if (Check(TokenType.LEFT_PAREN) || Check(TokenType.LESS))
                {
                    computedType = ParseMethodSignature();
                }
                else
                {
                    Consume(TokenType.COLON, "Expect ':' after computed member name.");
                    computedType = ParseUnionType();
                }
                // The string path renders computed members as plain fields (no method marker).
                if (TakeTypeNode() is { } computedNode)
                    memberNodes.Add(new PropertyMemberNode(computedName, computedNode, computedOptional, false, computedLine));
                else
                    nodeComplete = false;
                members.Add($"{computedName}{(computedOptional ? "?" : "")}: {computedType}");
            }
            // Check for index signature: [key: string]: type
            else if (Check(TokenType.LEFT_BRACKET))
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
                int indexLine = Previous().Line;
                string valueType = ParseUnionType();

                if (TakeTypeNode() is { } valueNode)
                    memberNodes.Add(new IndexSignatureNode(keyType, valueNode, indexLine));
                else
                    nodeComplete = false;
                members.Add($"[{keyType}]: {valueType}");
            }
            // Construct signature: new (params): ReturnType or new <T>(params): ReturnType
            else if (Check(TokenType.NEW))
            {
                Advance(); // consume 'new'
                int newLine = Previous().Line;
                // ParseMethodSignature consumes optional <generics>, the params, and the return
                // type, producing an arrow string "(params) => ret"; prefix "new " for a
                // construct signature so ParseInlineObjectTypeInfo can tell the two apart.
                members.Add("new " + ParseMethodSignature());
                var ctorSig = TakeTypeNode();
                if (ctorSig is FunctionTypeNode or GenericFunctionTypeNode)
                    memberNodes.Add(new ConstructSignatureMemberNode(ctorSig, newLine));
                else
                    nodeComplete = false;
            }
            // Call signature: (params): ReturnType or <T>(params): ReturnType
            else if (Check(TokenType.LEFT_PAREN) || (Check(TokenType.LESS) && IsCallSignatureStart()))
            {
                int callLine = Peek().Line;
                members.Add(ParseMethodSignature());
                var callSig = TakeTypeNode();
                if (callSig is FunctionTypeNode or GenericFunctionTypeNode)
                    memberNodes.Add(new CallSignatureMemberNode(callSig, callLine));
                else
                    nodeComplete = false;
            }
            else
            {
                // Optional readonly modifier on a property member
                bool isReadonly = Match(TokenType.READONLY);
                _ = isReadonly; // readonly is parsed but not yet modeled on inline object types

                // Parse property/method name — an identifier, a keyword (e.g. `type`, `set`),
                // or a string/numeric literal name (e.g. `"1"`, `1`).
                Token propertyName = ConsumePropertyNameOrLiteral("Expect property name in object type.");

                // Check for optional marker
                bool isOptional = Match(TokenType.QUESTION);

                string propertyType;
                bool isMethodMember = Check(TokenType.LEFT_PAREN);
                if (isMethodMember)
                {
                    // Method signature: methodName(params): returnType
                    propertyType = ParseMethodSignature();
                }
                else
                {
                    // Property: name: type
                    Consume(TokenType.COLON, "Expect ':' after property name in object type.");
                    // Member values may be conditional types: { x: T extends number ? T : string }
                    propertyType = ParseConditionalType();
                }

                if (TakeTypeNode() is { } propertyNode)
                    memberNodes.Add(new PropertyMemberNode(propertyName.Lexeme, propertyNode, isOptional, isMethodMember, propertyName.Line));
                else
                    nodeComplete = false;

                // Build member string. Method members carry a '#m' marker (stripped by
                // ParseInlineObjectTypeInfo) so method-ness survives the string round-trip —
                // under strictFunctionTypes methods keep bivariant parameter relating.
                string member = $"{propertyName.Lexeme}{(isMethodMember ? "#m" : "")}{(isOptional ? "?" : "")}: {propertyType}";
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
        _lastTypeNode = nodeComplete ? new ObjectTypeNode(memberNodes, startLine) : null;
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
        int startLine = Peek().Line;
        // Parse optional leading modifiers: +readonly, -readonly, readonly. Track flags alongside
        // the string spelling so the node carries the same modifier intent the string path derives.
        string readonlyMod = "";
        bool addReadonly = false, removeReadonly = false;
        if (Match(TokenType.PLUS))
        {
            if (Check(TokenType.READONLY))
            {
                Advance();
                readonlyMod = "+readonly ";
                addReadonly = true;
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
                removeReadonly = true;
            }
            else
            {
                throw new Exception("Parse Error: Expected 'readonly' after '-' in mapped type.");
            }
        }
        else if (Match(TokenType.READONLY))
        {
            readonlyMod = "readonly ";
            addReadonly = true; // plain readonly == AddReadonly (mirrors ParseMappedTypeInfo)
        }

        // Parse [K in Constraint]
        Consume(TokenType.LEFT_BRACKET, "Expect '[' in mapped type.");

        // Parse type parameter name
        Token paramName = Consume(TokenType.IDENTIFIER, "Expect type parameter name in mapped type.");

        // Expect 'in'
        Consume(TokenType.IN, "Expect 'in' after type parameter in mapped type.");

        // Parse constraint (e.g., keyof T, or a union of string literals)
        string constraint = ParseTypeAnnotation();
        TypeNode? constraintNode = TakeTypeNode();

        // Parse optional 'as' clause for key remapping
        string asClause = "";
        TypeNode? asNode = null;
        bool hasAs = false;
        if (Match(TokenType.AS))
        {
            hasAs = true;
            string remapType = ParseTypeAnnotation();
            asNode = TakeTypeNode();
            asClause = $" as {remapType}";
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after mapped type parameter.");

        // Parse optional trailing modifiers: +?, -?, ?
        string optionalMod = "";
        bool addOptional = false, removeOptional = false;
        if (Match(TokenType.PLUS))
        {
            if (Match(TokenType.QUESTION))
            {
                optionalMod = "+?";
                addOptional = true;
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
                removeOptional = true;
            }
            else
            {
                throw new Exception("Parse Error: Expected '?' after '-' in mapped type.");
            }
        }
        else if (Match(TokenType.QUESTION))
        {
            optionalMod = "?";
            addOptional = true;
        }

        // Parse : ValueType
        Consume(TokenType.COLON, "Expect ':' after mapped type parameter.");
        string valueType = ParseTypeAnnotation();
        TypeNode? valueNode = TakeTypeNode();

        // Handle optional separator and closing brace
        Match(TokenType.SEMICOLON);
        Consume(TokenType.RIGHT_BRACE, "Expect '}' after mapped type.");

        // The node needs the constraint, the value type, and (when present) the as-clause to all
        // have node forms. Any missing component drops the whole mapped type to the string path.
        _lastTypeNode = constraintNode is not null && valueNode is not null && (!hasAs || asNode is not null)
            ? new MappedTypeNode(paramName.Lexeme, constraintNode, valueNode, asNode,
                addReadonly, removeReadonly, addOptional, removeOptional, startLine)
            : null;
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
            TypeNode? constraintNode = null;
            TypeNode? defaultNode = null;

            // Parse optional constraint: extends SomeType
            if (Match(TokenType.EXTENDS))
            {
                constraint = ParseTypeAnnotation();
                constraintNode = TakeTypeNode();
            }

            // Parse optional default: = SomeType
            if (Match(TokenType.EQUAL))
            {
                defaultType = ParseTypeAnnotation();
                defaultNode = TakeTypeNode();
                sawDefault = true;
            }
            else if (sawDefault)
            {
                // TypeScript requires: required type parameters cannot follow optional ones
                throw new Exception($"Parse Error: Required type parameter '{name.Lexeme}' cannot follow optional type parameter with default.");
            }

            typeParams.Add(new TypeParam(name, constraint, defaultType, isConst, variance, constraintNode, defaultNode));
        } while (Match(TokenType.COMMA));

        ConsumeGreaterInTypeContext("Expect '>' after type parameters.");
        return typeParams;
    }

    /// <summary>
    /// Renders a parsed type-parameter list back to its string form, e.g.
    /// &lt;T, U extends Base = number&gt;. Returns "" for null/empty so callers can
    /// unconditionally prepend it to a function/method type string.
    /// </summary>
    private static string FormatTypeParams(List<TypeParam>? typeParams)
    {
        if (typeParams == null || typeParams.Count == 0) return "";

        var parts = typeParams.Select(tp =>
        {
            string part = tp.Name.Lexeme;
            if (tp.Constraint != null) part += $" extends {tp.Constraint}";
            if (tp.Default != null) part += $" = {tp.Default}";
            return part;
        });
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Tries to parse type arguments like &lt;number, string&gt;.
    /// Returns null if not valid type arguments (backtracking safe).
    /// Uses CheckGreaterInTypeContext/MatchGreaterInTypeContext to handle nested generics.
    /// <paramref name="argNodes"/> receives the per-argument node twins (type-AST migration),
    /// always index-aligned with the returned strings; an argument without node support
    /// contributes a null element. Null when the whole parse failed.
    /// </summary>
    private List<string>? TryParseTypeArguments(out List<TypeNode?>? argNodes)
    {
        argNodes = null;
        if (!Check(TokenType.LESS)) return null;
        int saved = _current;

        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            List<string> args = [ParseTypeAnnotation()];
            List<TypeNode?> nodes = [TakeTypeNode()];
            while (Match(TokenType.COMMA))
            {
                args.Add(ParseTypeAnnotation());
                nodes.Add(TakeTypeNode());
            }

            if (!CheckGreaterInTypeContext()) { _current = saved; return null; }
            MatchGreaterInTypeContext(); // consume >
            argNodes = nodes;
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
    /// <paramref name="argNodes"/> mirrors <see cref="TryParseTypeArguments"/>.
    /// </summary>
    private List<string>? TryParseTypeArgumentsForCall(out List<TypeNode?>? argNodes)
    {
        argNodes = null;
        if (!Check(TokenType.LESS)) return null;
        int saved = _current;

        try
        {
            Advance(); // consume <
            if (!IsTypeStart()) { _current = saved; return null; }

            List<string> args = [ParseTypeAnnotation()];
            List<TypeNode?> nodes = [TakeTypeNode()];
            while (Match(TokenType.COMMA))
            {
                args.Add(ParseTypeAnnotation());
                nodes.Add(TakeTypeNode());
            }

            if (!CheckGreaterInTypeContext()) { _current = saved; return null; }
            MatchGreaterInTypeContext(); // consume >

            // Must be followed by '(' for a call
            if (!Check(TokenType.LEFT_PAREN)) { _current = saved; return null; }
            Advance(); // consume (

            argNodes = nodes;
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
        int startLine = Previous().Line;
        var sb = new StringBuilder("`");
        // Static segments (N+1) around the N interpolations, mirroring NormalizeTemplateLiteralType.
        var strings = new List<string>();
        var interpolated = new List<TypeNode>();
        bool nodeComplete = true;

        // Template tokens carry a TemplateStringValue (cooked + raw); the type uses the cooked text.
        string head = ((TemplateStringValue)Previous().Literal!).Cooked ?? "";
        sb.Append(head);
        strings.Add(head);

        // Parse first interpolated type
        sb.Append("${");
        sb.Append(ParseUnionType()); // Allow unions inside interpolation
        sb.Append('}');
        if (TakeTypeNode() is { } firstNode) interpolated.Add(firstNode); else nodeComplete = false;

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            string mid = ((TemplateStringValue)Previous().Literal!).Cooked ?? "";
            sb.Append(mid);
            strings.Add(mid);
            sb.Append("${");
            sb.Append(ParseUnionType());
            sb.Append('}');
            if (TakeTypeNode() is { } midNode) interpolated.Add(midNode); else nodeComplete = false;
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal type.");
        string tail = ((TemplateStringValue)Previous().Literal!).Cooked ?? "";
        sb.Append(tail);
        sb.Append('`');
        strings.Add(tail);

        _lastTypeNode = nodeComplete ? new TemplateLiteralTypeNode(strings, interpolated, startLine) : null;
        return sb.ToString();
    }
}
