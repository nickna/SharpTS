namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt Declaration()
    {
        // Module declarations - must be at top level
        // Note: import followed by ( is dynamic import, and import followed by . is import.meta —
        // both expressions, not a static import declaration.
        if (Check(TokenType.IMPORT) && PeekNext().Type != TokenType.LEFT_PAREN && PeekNext().Type != TokenType.DOT)
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

        // `@decorator export class ...` — decorators legally appear *before* `export`.
        // The EXPORT branch at the top of Declaration() only fires when EXPORT is the
        // first token, so a decorated exported class reaches here with EXPORT unconsumed.
        // Thread the decorators into ExportDeclaration() so they reach the class body
        // the same way the bare (non-export) class paths below already do.
        if (Match(TokenType.EXPORT)) return ExportDeclaration(decorators);

        if (Match(TokenType.DECLARE))
        {
            Token declareKeyword = Previous();

            // declare module 'path' { ... } - module augmentation or ambient declaration
            if (Match(TokenType.MODULE))
            {
                return DeclareModuleDeclaration(declareKeyword);
            }

            // declare global { ... } - global augmentation
            if (Match(TokenType.GLOBAL))
            {
                return DeclareGlobalDeclaration(declareKeyword);
            }

            // declare [abstract] class — ambient class declarations (external types)
            if (Match(TokenType.ABSTRACT))
            {
                Consume(TokenType.CLASS, "Expect 'class' after 'declare abstract'.");
                return ClassDeclaration(isAbstract: true, classDecorators: decorators, isDeclare: true);
            }
            if (Match(TokenType.CLASS))
            {
                return ClassDeclaration(isAbstract: false, classDecorators: decorators, isDeclare: true);
            }

            // Decorators are only valid on classes; reject them on other declare forms.
            if (decorators != null && decorators.Count > 0)
            {
                throw new Exception($"Parse Error at line {decorators[0].AtToken.Line}: Decorators are not valid here. Decorators can only be applied to classes and class members.");
            }

            // Other ambient declarations: declare function/const/let/var/enum/interface/type/namespace
            return AmbientDeclaration();
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
        // `namespace` is a contextual keyword. Treat it as a namespace
        // declaration only when followed by an identifier (the namespace
        // name). Otherwise fall through — e.g. `namespace = value` or
        // `typeof namespace` uses it as a plain variable name.
        if (Check(TokenType.NAMESPACE) &&
            (PeekNext().Type == TokenType.IDENTIFIER || IsContextualKeyword(PeekNext().Type)))
        {
            Advance(); // consume NAMESPACE
            return NamespaceDeclaration();
        }
        // `module Foo { }` is the older spelling of `namespace Foo { }`. Only treat it as a
        // namespace when followed by an identifier name; `module "path" { }` (string name) is an
        // ambient module declaration handled under `declare`.
        if (Check(TokenType.MODULE) &&
            (PeekNext().Type == TokenType.IDENTIFIER || IsContextualKeyword(PeekNext().Type)))
        {
            Advance(); // consume MODULE
            return NamespaceDeclaration();
        }
        if (Match(TokenType.INTERFACE)) return InterfaceDeclaration();
        // 'type' is a contextual keyword — only treat as type alias when followed by an identifier
        // (e.g. `type Foo = string`), not when used as a variable name (e.g. `var type = "x"`).
        if (Check(TokenType.TYPE) && PeekNext().Type == TokenType.IDENTIFIER)
        {
            Advance(); // consume TYPE
            return TypeAliasDeclaration();
        }
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
        if (Match(TokenType.VAR)) return VarDeclaration(isConst: false, isVar: true);

        // Handle 'using' declaration (contextual keyword for explicit resource management)
        if (Check(TokenType.USING) && IsUsingDeclarationContext())
        {
            Token usingKeyword = Advance(); // consume USING
            return UsingDeclaration(usingKeyword, isAwait: false);
        }

        // Handle 'await using' declaration
        if (Check(TokenType.AWAIT) && PeekNext().Type == TokenType.USING)
        {
            Advance(); // consume AWAIT
            Token usingKeyword = Advance(); // consume USING
            return UsingDeclaration(usingKeyword, isAwait: true);
        }

        return Statement();
    }

    /// <summary>
    /// Parses a non-class ambient declaration following the `declare` keyword, after the
    /// `declare module`/`declare global`/`declare [abstract] class` forms have been ruled out:
    /// declare function/const/let/var/enum/interface/type/namespace. Bodies and initializers are
    /// absent in ambient context — the underlying parsers already accept the bodyless forms
    /// (FunctionDeclaration via its overload-signature path, AmbientVarDeclaration with no '=').
    /// </summary>
    private Stmt AmbientDeclaration()
    {
        if (Match(TokenType.FUNCTION))
        {
            bool isGenerator = Match(TokenType.STAR);
            // Ambient: the bodyless declaration IS the function — the checker defines it
            // immediately rather than holding it as a pending overload signature.
            return FunctionDeclaration("function", isAsync: false, isGenerator: isGenerator, isDeclare: true);
        }
        if (Match(TokenType.CONST))
        {
            // declare const enum E { ... }
            if (Match(TokenType.ENUM)) return EnumDeclaration(isConst: true);
            return AmbientVarDeclaration(isConst: true);
        }
        if (Match(TokenType.LET) || Match(TokenType.VAR))
        {
            return AmbientVarDeclaration(isConst: false);
        }
        if (Match(TokenType.ENUM))
        {
            return EnumDeclaration(isConst: false);
        }
        if (Match(TokenType.INTERFACE))
        {
            return InterfaceDeclaration();
        }
        // 'type' is a contextual keyword — only a type alias when followed by an identifier.
        if (Check(TokenType.TYPE) && PeekNext().Type == TokenType.IDENTIFIER)
        {
            Advance(); // consume TYPE
            return TypeAliasDeclaration();
        }
        // 'namespace' is a contextual keyword — only a namespace when followed by a name.
        if (Check(TokenType.NAMESPACE) &&
            (PeekNext().Type == TokenType.IDENTIFIER || IsContextualKeyword(PeekNext().Type)))
        {
            Advance(); // consume NAMESPACE
            return NamespaceDeclaration();
        }

        throw new Exception($"Parse Error at line {Peek().Line}: Expected 'class', 'function', 'const', 'let', 'var', 'enum', 'interface', 'type', 'namespace', 'module', or 'global' after 'declare'.");
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
        TypeNode? typeDefNode = TakeTypeNode();

        ConsumeSemicolon("Expect ';' after type alias.");
        return new Stmt.TypeAlias(name, typeDef, typeParams, typeDefNode);
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
        // Allow contextual-keyword names (e.g. lib.d.ts declares `interface Symbol`, `interface BigInt`).
        Token name = ConsumeIdentifierName("Expect interface name.");
        List<TypeParam>? typeParams = ParseTypeParameters();

        // Parse extends clause: interface Foo extends Bar, Baz { ... }
        List<string>? extends = null;
        List<TypeNode?>? extendsNodes = null;
        if (Match(TokenType.EXTENDS))
        {
            extends = [];
            extendsNodes = [];
            do
            {
                extends.Add(ParseTypeAnnotation());
                extendsNodes.Add(TakeTypeNode()); // per-entry; null keeps indices aligned
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before interface body.");

        List<Stmt.InterfaceMember> members = [];
        List<Stmt.IndexSignature> indexSignatures = [];
        List<Stmt.CallSignature> callSignatures = [];
        List<Stmt.ConstructorSignature> constructorSignatures = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Readonly index signature: `readonly [index: number]: T` (lib.d.ts). Consume the
            // `readonly` modifier so the index-signature parser sees the leading '['.
            if (Check(TokenType.READONLY) && PeekNext().Type == TokenType.LEFT_BRACKET)
            {
                Advance(); // consume readonly
                var roIndexSig = TryParseIndexSignature();
                if (roIndexSig != null)
                {
                    indexSignatures.Add(roIndexSig);
                    continue;
                }
            }

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

            // Check for constructor signature: new (params): ReturnType or new <T>(params): ReturnType
            if (Check(TokenType.NEW))
            {
                var ctorSig = TryParseConstructorSignature();
                if (ctorSig != null)
                {
                    constructorSignatures.Add(ctorSig);
                    continue;
                }
            }

            // Check for call signature: (params): ReturnType or <T>(params): ReturnType
            // Starts with '(' or '<' followed eventually by '('
            if (Check(TokenType.LEFT_PAREN) || (Check(TokenType.LESS) && IsCallSignatureStart()))
            {
                var callSig = TryParseCallSignature();
                if (callSig != null)
                {
                    callSignatures.Add(callSig);
                    continue;
                }
            }

            // Computed member name using a well-known symbol: `[Symbol.iterator](): T`,
            // `readonly [Symbol.toStringTag]: "X"` (lib.d.ts). Index signatures were ruled out above,
            // so a leading '[' here is a computed name. Map `Symbol.x` to the canonical `@@x`.
            bool computedReadonly = Check(TokenType.READONLY) && PeekNext().Type == TokenType.LEFT_BRACKET;
            if (computedReadonly) Advance(); // consume readonly
            if (Check(TokenType.LEFT_BRACKET))
            {
                int computedLine = Peek().Line;
                Advance(); // consume '['
                string raw = "";
                while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
                    raw += Advance().Lexeme;
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed member name.");
                string computedName = raw.StartsWith("Symbol.") ? "@@" + raw["Symbol.".Length..] : "@@" + raw;
                var computedTok = new Token(TokenType.IDENTIFIER, computedName, null, computedLine);

                bool computedOptional = Match(TokenType.QUESTION);
                string computedType;
                bool computedIsMethod = Check(TokenType.LEFT_PAREN) || Check(TokenType.LESS);
                if (computedIsMethod)
                {
                    computedType = ParseMethodSignature();
                }
                else if (Match(TokenType.COLON))
                {
                    computedType = ParseTypeAnnotation();
                }
                else
                {
                    // No type annotation — implicit `any`.
                    computedType = "any";
                    _lastTypeNode = new NamedTypeNode("any", null, computedLine);
                }
                TypeNode? computedTypeNode = TakeTypeNode();
                ConsumeInterfaceMemberSeparator();
                members.Add(new Stmt.InterfaceMember(computedTok, computedType, computedOptional, computedReadonly, computedIsMethod, computedTypeNode));
                continue;
            }

            // Check for readonly modifier
            bool isReadonly = Match(TokenType.READONLY);

            // Member name — identifier, keyword, or string/numeric literal (e.g. `1: T`, `"1": T`).
            Token memberName = ConsumePropertyNameOrLiteral("Expect member name.");
            bool isOptional = Match(TokenType.QUESTION);

            string type;
            bool isMethod = Check(TokenType.LEFT_PAREN) || Check(TokenType.LESS);
            if (isMethod)
            {
                // Method signature: methodName(params): returnType or methodName<T>(params): returnType
                type = ParseMethodSignature();
            }
            else if (Match(TokenType.COLON))
            {
                type = ParseTypeAnnotation();
            }
            else
            {
                // No type annotation — implicit `any` (e.g. `interface Foo { prop }`).
                type = "any";
                _lastTypeNode = new NamedTypeNode("any", null, memberName.Line);
            }
            TypeNode? memberTypeNode = TakeTypeNode();

            ConsumeInterfaceMemberSeparator();
            members.Add(new Stmt.InterfaceMember(memberName, type, isOptional, isReadonly, isMethod, memberTypeNode));
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after interface body.");
        return new Stmt.Interface(
            name,
            typeParams,
            members,
            indexSignatures.Count > 0 ? indexSignatures : null,
            extends,
            callSignatures.Count > 0 ? callSignatures : null,
            constructorSignatures.Count > 0 ? constructorSignatures : null,
            extendsNodes
        );
    }

    /// <summary>
    /// Determines if the current position is the start of a call signature (generic type params followed by params).
    /// Used to disambiguate '<' as start of generic type params vs. comparison operator.
    /// </summary>
    private bool IsCallSignatureStart()
    {
        // We're at '<', look ahead to see if this is <T>(params): ReturnType pattern
        int saved = _current;
        try
        {
            Advance(); // consume '<'

            // Skip over type parameters
            int depth = 1;
            while (!IsAtEnd() && depth > 0)
            {
                if (Check(TokenType.LESS)) depth++;
                else if (Check(TokenType.GREATER)) depth--;
                Advance();
            }

            // After closing '>', should see '('
            return Check(TokenType.LEFT_PAREN);
        }
        finally
        {
            _current = saved;
        }
    }

    /// <summary>
    /// Tries to parse a call signature: (params): ReturnType or &lt;T&gt;(params): ReturnType
    /// </summary>
    private Stmt.CallSignature? TryParseCallSignature()
    {
        int saved = _current;

        try
        {
            // Parse optional generic type parameters
            List<TypeParam>? sigTypeParams = ParseTypeParameters();

            // Must have '('
            if (!Match(TokenType.LEFT_PAREN))
            {
                _current = saved;
                return null;
            }

            // Parse parameters
            List<Stmt.Parameter> parameters = ParseSignatureParameters();

            Consume(TokenType.RIGHT_PAREN, "Expect ')' after call signature parameters.");
            Consume(TokenType.COLON, "Expect ':' before return type in call signature.");
            string returnType = ParseTypeAnnotation();
            TypeNode? returnTypeNode = TakeTypeNode();
            ConsumeInterfaceMemberSeparator();

            return new Stmt.CallSignature(sigTypeParams, parameters, returnType, returnTypeNode);
        }
        catch
        {
            _current = saved;
            return null;
        }
    }

    /// <summary>
    /// Tries to parse a constructor signature: new (params): ReturnType or new &lt;T&gt;(params): ReturnType
    /// </summary>
    private Stmt.ConstructorSignature? TryParseConstructorSignature()
    {
        int saved = _current;

        try
        {
            Consume(TokenType.NEW, "Expect 'new' keyword.");

            // Parse optional generic type parameters
            List<TypeParam>? sigTypeParams = ParseTypeParameters();

            // Must have '('
            if (!Match(TokenType.LEFT_PAREN))
            {
                _current = saved;
                return null;
            }

            // Parse parameters
            List<Stmt.Parameter> parameters = ParseSignatureParameters();

            Consume(TokenType.RIGHT_PAREN, "Expect ')' after constructor signature parameters.");
            Consume(TokenType.COLON, "Expect ':' before return type in constructor signature.");
            string returnType = ParseTypeAnnotation();
            TypeNode? returnTypeNode = TakeTypeNode();
            ConsumeInterfaceMemberSeparator();

            return new Stmt.ConstructorSignature(sigTypeParams, parameters, returnType, returnTypeNode);
        }
        catch
        {
            _current = saved;
            return null;
        }
    }

    /// <summary>
    /// Parses parameters for call/constructor signatures (name: type, ...).
    /// </summary>
    private List<Stmt.Parameter> ParseSignatureParameters()
    {
        List<Stmt.Parameter> parameters = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Check for rest parameter
                bool isRest = Match(TokenType.DOT_DOT_DOT);

                Token paramName = ConsumeIdentifierName("Expect parameter name.");

                // Check for optional marker
                bool isOptional = Match(TokenType.QUESTION);

                // Parse type annotation
                string? paramType = null;
                TypeNode? paramTypeNode = null;
                if (Match(TokenType.COLON))
                {
                    paramType = ParseTypeAnnotation();
                    paramTypeNode = TakeTypeNode();
                }

                parameters.Add(new Stmt.Parameter(paramName, paramType, null, isRest, IsOptional: isOptional, TypeAnnotationNode: paramTypeNode));

            } while (Match(TokenType.COMMA));
        }

        return parameters;
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
        else if (Check(TokenType.IDENTIFIER))
        {
            // Non-primitive key type, e.g. a type alias such as `PropertyKey`
            // (= `string | number | symbol`). lib.d.ts uses these. Model as a string
            // index — the broadest key kind — so the declaration parses.
            keyType = TokenType.TYPE_STRING;
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
        TypeNode? valueTypeNode = TakeTypeNode();
        ConsumeInterfaceMemberSeparator();

        return new Stmt.IndexSignature(keyName, keyType, valueType, valueTypeNode);
    }

    /// <summary>
    /// Parses a method signature like "(a: number, b: string): returnType" and returns it as a function type string.
    /// Supports 'this' parameter: "(this: Type, a: number): returnType".
    /// Supports generic type parameters: "&lt;T&gt;(a: T): T".
    /// </summary>
    private string ParseMethodSignature()
    {
        // Parse optional generic type parameters: <T, U extends Base>
        string genericPrefix = "";
        List<TypeParam>? methodTypeParams = null;
        if (Check(TokenType.LESS))
        {
            methodTypeParams = ParseTypeParameters();
            genericPrefix = FormatTypeParams(methodTypeParams);
        }

        Consume(TokenType.LEFT_PAREN, "Expect '(' for method parameters.");
        int startLine = Previous().Line;
        string? thisType = null;
        TypeNode? thisTypeNode = null;
        List<string> paramTypes = [];
        List<ParameterTypeNode> paramNodes = [];
        bool nodeComplete = true;

        // Check for 'this' parameter in interface method
        if (Check(TokenType.THIS))
        {
            Advance(); // consume 'this'
            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
            thisType = ParseTypeAnnotation();
            thisTypeNode = TakeTypeNode();
            if (thisTypeNode is null) nodeComplete = false;
            if (Check(TokenType.COMMA))
            {
                Advance(); // consume ','
            }
        }

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                bool isRest = Match(TokenType.DOT_DOT_DOT);
                string paramName = ConsumeIdentifierName("Expect parameter name.").Lexeme;
                bool isOptional = Match(TokenType.QUESTION);
                Consume(TokenType.COLON, "Expect ':' after parameter name.");
                string paramType = ParseTypeAnnotation();
                if (TakeTypeNode() is { } paramTypeNode)
                    paramNodes.Add(new ParameterTypeNode(paramName, paramTypeNode, isOptional, isRest, paramTypeNode.Line));
                else
                    nodeComplete = false;
                // Encode rest/optional the same way ParseFunctionTypeBody does so the signature
                // resolver models arity correctly.
                if (isRest) paramType = "..." + paramType;
                else if (isOptional) paramType += "?";
                paramTypes.Add(paramType);
            } while (Match(TokenType.COMMA));
        }

        Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");
        string returnType;
        TypeNode? returnTypeNode;
        if (Match(TokenType.COLON))
        {
            returnType = ParseTypeAnnotation();
            returnTypeNode = TakeTypeNode();
        }
        else
        {
            // No return type annotation — implicit `any` (e.g. `foo();` in an interface/type literal).
            returnType = "any";
            returnTypeNode = new NamedTypeNode("any", null, Previous().Line);
        }

        // Publish the structured form (or explicitly clear, so no nested node leaks out). A generic
        // method signature wraps the function in a GenericFunctionTypeNode; a `this` parameter has
        // no slot on a generic signature's resolved form, so those fall back.
        FunctionTypeNode? functionNode = nodeComplete && returnTypeNode is not null
            ? new FunctionTypeNode(thisTypeNode, paramNodes, returnTypeNode, startLine)
            : null;
        _lastTypeNode = functionNode is null
            ? null
            : methodTypeParams is { Count: > 0 }
                ? (thisTypeNode is null ? new GenericFunctionTypeNode(methodTypeParams, functionNode, startLine) : null)
                : functionNode;

        if (thisType != null)
        {
            // Only add comma between this and params if there are params
            if (paramTypes.Count > 0)
            {
                return $"{genericPrefix}(this: {thisType}, {string.Join(", ", paramTypes)}) => {returnType}";
            }
            return $"{genericPrefix}(this: {thisType}) => {returnType}";
        }
        return $"{genericPrefix}({string.Join(", ", paramTypes)}) => {returnType}";
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

    private Stmt VarDeclaration(bool isConst = false, bool isVar = false)
    {
        // Check for destructuring patterns
        if (Check(TokenType.LEFT_BRACKET))
            return DestructureArrayDeclaration();
        if (Check(TokenType.LEFT_BRACE))
            return DestructureObjectDeclaration();

        // Parse the first declarator.
        var first = ParseSingleDeclarator(isConst, isVar);

        // Multi-declarator support: `let a = 1, b = 2, c;`
        // We accumulate into a Stmt.Sequence — same scope, just multiple statements.
        if (Check(TokenType.COMMA))
        {
            var statements = new List<Stmt> { first };
            while (Match(TokenType.COMMA))
            {
                statements.Add(ParseSingleDeclarator(isConst, isVar));
            }
            ConsumeSemicolon("Expect ';' after variable declaration.");
            return new Stmt.Sequence(statements);
        }

        ConsumeSemicolon("Expect ';' after variable declaration.");
        return first;
    }

    /// <summary>
    /// Parses a single declarator (no leading keyword, no trailing semicolon or comma).
    /// Used by <see cref="VarDeclaration"/> to build both single and multi-declarator forms.
    /// </summary>
    private Stmt ParseSingleDeclarator(bool isConst, bool isVar = false)
    {
        Token name = ConsumeIdentifierName("Expect variable name.");

        // Check for definite assignment assertion: let x!: number;
        bool hasDefiniteAssignment = Match(TokenType.BANG);

        string? typeAnnotation = null;
        TypeNode? typeAnnotationNode = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
            typeAnnotationNode = TakeTypeNode();
        }

        if (hasDefiniteAssignment && typeAnnotation == null)
        {
            throw new Exception($"Parse Error at line {name.Line}: Definite assignment assertion '!' requires a type annotation.");
        }

        if (hasDefiniteAssignment && isConst)
        {
            throw new Exception($"Parse Error at line {name.Line}: 'const' declarations cannot use definite assignment assertion '!' (const must be initialized).");
        }

        Expr? initializer = null;
        if (Match(TokenType.EQUAL))
        {
            initializer = Expression();
        }

        if (hasDefiniteAssignment && initializer != null)
        {
            throw new Exception($"Parse Error at line {name.Line}: Definite assignment assertion '!' cannot be used with an initializer.");
        }

        if (isConst && initializer == null)
        {
            throw new Exception($"Parse Error at line {name.Line}: 'const' declarations must be initialized.");
        }

        if (isConst)
            return new Stmt.Const(name, typeAnnotation, initializer!, typeAnnotationNode);
        return new Stmt.Var(name, typeAnnotation, initializer, hasDefiniteAssignment, isVar, typeAnnotationNode);
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

    /// <summary>
    /// Parses a declare module declaration: declare module 'path' { ... }
    /// Used for module augmentation (extending existing modules) or ambient declarations (typing external packages).
    /// </summary>
    /// <param name="declareKeyword">The 'declare' token for error reporting</param>
    private Stmt DeclareModuleDeclaration(Token declareKeyword)
    {
        // Module path must be a string literal
        string modulePath = (string)Consume(TokenType.STRING, "Expect module path string after 'declare module'.").Literal!;

        Consume(TokenType.LEFT_BRACE, "Expect '{' before declare module body.");

        List<Stmt> members = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Parse declaration members (interface, function, var, const, class, type, etc.)
            // These can be exported or not
            members.Add(ParseDeclareModuleMember());
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after declare module body.");

        return new Stmt.DeclareModule(declareKeyword, modulePath, members);
    }

    /// <summary>
    /// Parses a declare global declaration: declare global { ... }
    /// Used for global augmentation - extending global types like Array, String, etc.
    /// </summary>
    /// <param name="declareKeyword">The 'declare' token for error reporting</param>
    private Stmt DeclareGlobalDeclaration(Token declareKeyword)
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' before declare global body.");

        List<Stmt> members = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Parse declaration members (interface, function, var, const, etc.)
            members.Add(ParseDeclareModuleMember());
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after declare global body.");

        return new Stmt.DeclareGlobal(declareKeyword, members);
    }

    /// <summary>
    /// Parses a single member inside a declare module or declare global block.
    /// Supports: export, interface, function, var, const, let, class, type, namespace
    /// </summary>
    private Stmt ParseDeclareModuleMember()
    {
        // Members can be exported
        if (Match(TokenType.EXPORT))
        {
            Token exportKeyword = Previous();

            // export { x, y as z } [from './module'] — e.g. `declare global { export { globalThis as global } }`
            if (Match(TokenType.LEFT_BRACE))
            {
                var namedExports = ParseExportSpecifiers();
                string? fromPath = null;
                if (Match(TokenType.FROM))
                {
                    fromPath = (string)Consume(TokenType.STRING, "Expect module path.").Literal!;
                }
                ConsumeSemicolon("Expect ';' after export.");
                return new Stmt.Export(exportKeyword, null, namedExports, null, fromPath, IsDefaultExport: false);
            }

            // export interface Foo { }
            if (Match(TokenType.INTERFACE))
            {
                var iface = InterfaceDeclaration();
                return new Stmt.Export(exportKeyword, iface, null, null, null, false);
            }

            // export function foo(): void;
            if (Match(TokenType.FUNCTION))
            {
                var func = FunctionDeclaration("function", isAsync: false, isGenerator: false);
                return new Stmt.Export(exportKeyword, func, null, null, null, false);
            }

            // export const x: number;
            if (Match(TokenType.CONST))
            {
                var varDecl = AmbientVarDeclaration(isConst: true);
                return new Stmt.Export(exportKeyword, varDecl, null, null, null, false);
            }

            // export let x: number; / export var x: number;
            if (Match(TokenType.LET) || Match(TokenType.VAR))
            {
                var varDecl = AmbientVarDeclaration(isConst: false);
                return new Stmt.Export(exportKeyword, varDecl, null, null, null, false);
            }

            // export class Foo { }
            if (Match(TokenType.CLASS))
            {
                var cls = ClassDeclaration(isAbstract: false, isDeclare: true);
                return new Stmt.Export(exportKeyword, cls, null, null, null, false);
            }

            // export type Foo = ...;
            if (Match(TokenType.TYPE))
            {
                var typeAlias = TypeAliasDeclaration();
                return new Stmt.Export(exportKeyword, typeAlias, null, null, null, false);
            }

            // export namespace Foo { }
            if (Match(TokenType.NAMESPACE))
            {
                var ns = NamespaceDeclaration(isExported: true);
                return new Stmt.Export(exportKeyword, ns, null, null, null, false);
            }

            throw new Exception($"Parse Error at line {Peek().Line}: Expected declaration after 'export' in declare block.");
        }

        // Non-exported members
        if (Match(TokenType.INTERFACE))
        {
            return InterfaceDeclaration();
        }

        if (Match(TokenType.FUNCTION))
        {
            return FunctionDeclaration("function", isAsync: false, isGenerator: false);
        }

        if (Match(TokenType.CONST))
        {
            return AmbientVarDeclaration(isConst: true);
        }

        if (Match(TokenType.LET) || Match(TokenType.VAR))
        {
            return AmbientVarDeclaration(isConst: false);
        }

        if (Match(TokenType.CLASS))
        {
            return ClassDeclaration(isAbstract: false, isDeclare: true);
        }

        if (Match(TokenType.TYPE))
        {
            return TypeAliasDeclaration();
        }

        if (Match(TokenType.NAMESPACE))
        {
            return NamespaceDeclaration();
        }

        throw new Exception($"Parse Error at line {Peek().Line}: Expected declaration in declare block.");
    }

    /// <summary>
    /// Parses an ambient variable declaration (no initializer allowed).
    /// Used in declare module/global blocks.
    /// </summary>
    private Stmt AmbientVarDeclaration(bool isConst)
    {
        Token name = ConsumeIdentifierName("Expect variable name.");

        string? typeAnnotation = null;
        TypeNode? typeAnnotationNode = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
            typeAnnotationNode = TakeTypeNode();
        }

        ConsumeSemicolon("Expect ';' after ambient variable declaration.");

        // Ambient declarations have no initializer
        if (isConst)
        {
            // For ambient const, we use Var with no initializer (special case)
            return new Stmt.Var(name, typeAnnotation, null, TypeAnnotationNode: typeAnnotationNode, IsDeclare: true);
        }
        return new Stmt.Var(name, typeAnnotation, null, TypeAnnotationNode: typeAnnotationNode, IsDeclare: true);
    }

    /// <summary>
    /// Determines if 'using' should be treated as a declaration keyword (contextual keyword).
    /// Returns true if followed by identifier, '{' (object destructuring), or '[' (array destructuring).
    /// </summary>
    private bool IsUsingDeclarationContext()
    {
        var nextType = PeekNext().Type;
        return nextType == TokenType.IDENTIFIER ||
               nextType == TokenType.LEFT_BRACE ||   // object destructuring: using { x } = expr
               nextType == TokenType.LEFT_BRACKET;   // array destructuring: using [a, b] = expr
    }

    /// <summary>
    /// Parses a 'using' or 'await using' declaration for explicit resource management.
    /// Syntax: using name = expr; or using name = expr, name2 = expr2;
    /// </summary>
    /// <param name="usingKeyword">The 'using' token for error reporting.</param>
    /// <param name="isAwait">True for 'await using', false for 'using'.</param>
    private Stmt UsingDeclaration(Token usingKeyword, bool isAwait)
    {
        var bindings = new List<Stmt.UsingBinding>();

        do
        {
            bindings.Add(ParseUsingBinding());
        } while (Match(TokenType.COMMA));

        ConsumeSemicolon("Expect ';' after 'using' declaration.");
        return new Stmt.Using(usingKeyword, bindings, isAwait);
    }

    /// <summary>
    /// Parses a single binding in a using declaration.
    /// Currently only supports simple identifiers (destructuring may be added later).
    /// </summary>
    private Stmt.UsingBinding ParseUsingBinding()
    {
        Token name = ConsumeIdentifierName("Expect variable name in 'using' declaration.");

        string? typeAnnotation = null;
        TypeNode? typeAnnotationNode = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
            typeAnnotationNode = TakeTypeNode();
        }

        Consume(TokenType.EQUAL, "'using' declarations must be initialized.");
        Expr initializer = Expression();

        return new Stmt.UsingBinding(name, null, typeAnnotation, initializer, typeAnnotationNode);
    }
}
