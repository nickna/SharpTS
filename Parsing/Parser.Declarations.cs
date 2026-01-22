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
        List<Stmt.CallSignature> callSignatures = [];
        List<Stmt.ConstructorSignature> constructorSignatures = [];

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

            Token memberName = Consume(TokenType.IDENTIFIER, "Expect member name.");
            bool isOptional = Match(TokenType.QUESTION);

            string type;
            if (Check(TokenType.LEFT_PAREN) || Check(TokenType.LESS))
            {
                // Method signature: methodName(params): returnType or methodName<T>(params): returnType
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
        return new Stmt.Interface(
            name,
            typeParams,
            members,
            indexSignatures.Count > 0 ? indexSignatures : null,
            extends,
            callSignatures.Count > 0 ? callSignatures : null,
            constructorSignatures.Count > 0 ? constructorSignatures : null
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
            Consume(TokenType.SEMICOLON, "Expect ';' after call signature.");

            return new Stmt.CallSignature(sigTypeParams, parameters, returnType);
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
            Consume(TokenType.SEMICOLON, "Expect ';' after constructor signature.");

            return new Stmt.ConstructorSignature(sigTypeParams, parameters, returnType);
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

                Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");

                // Check for optional marker
                bool isOptional = Match(TokenType.QUESTION);

                // Parse type annotation
                string? paramType = null;
                if (Match(TokenType.COLON))
                {
                    paramType = ParseTypeAnnotation();
                }

                parameters.Add(new Stmt.Parameter(paramName, paramType, null, isRest, IsOptional: isOptional));

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
    /// Supports generic type parameters: "&lt;T&gt;(a: T): T".
    /// </summary>
    private string ParseMethodSignature()
    {
        // Parse optional generic type parameters: <T, U extends Base>
        string genericPrefix = "";
        if (Check(TokenType.LESS))
        {
            List<TypeParam>? typeParams = ParseTypeParameters();
            if (typeParams != null && typeParams.Count > 0)
            {
                var parts = typeParams.Select(tp =>
                {
                    string part = tp.Name.Lexeme;
                    if (tp.Constraint != null) part += $" extends {tp.Constraint}";
                    if (tp.Default != null) part += $" = {tp.Default}";
                    return part;
                });
                genericPrefix = $"<{string.Join(", ", parts)}>";
            }
        }

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

    private Stmt VarDeclaration(bool isConst = false)
    {
        // Check for destructuring patterns
        if (Check(TokenType.LEFT_BRACKET))
            return DestructureArrayDeclaration();
        if (Check(TokenType.LEFT_BRACE))
            return DestructureObjectDeclaration();

        // Standard single-variable declaration
        Token name = Consume(TokenType.IDENTIFIER, "Expect variable name.");

        // Check for definite assignment assertion: let x!: number;
        bool hasDefiniteAssignment = Match(TokenType.BANG);

        string? typeAnnotation = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
        }

        // Validate: ! requires type annotation
        if (hasDefiniteAssignment && typeAnnotation == null)
        {
            throw new Exception($"Parse Error at line {name.Line}: Definite assignment assertion '!' requires a type annotation.");
        }

        // Validate: ! cannot be used with const
        if (hasDefiniteAssignment && isConst)
        {
            throw new Exception($"Parse Error at line {name.Line}: 'const' declarations cannot use definite assignment assertion '!' (const must be initialized).");
        }

        Expr? initializer = null;
        if (Match(TokenType.EQUAL))
        {
            initializer = Expression();
        }

        // Validate: ! cannot coexist with initializer
        if (hasDefiniteAssignment && initializer != null)
        {
            throw new Exception($"Parse Error at line {name.Line}: Definite assignment assertion '!' cannot be used with an initializer.");
        }

        // const declarations require an initializer
        if (isConst && initializer == null)
        {
            throw new Exception($"Parse Error at line {name.Line}: 'const' declarations must be initialized.");
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

        // Return Stmt.Const for const declarations, Stmt.Var otherwise
        if (isConst)
            return new Stmt.Const(name, typeAnnotation, initializer!);
        return new Stmt.Var(name, typeAnnotation, initializer, hasDefiniteAssignment);
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

            // export let x: number;
            if (Match(TokenType.LET))
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

        if (Match(TokenType.LET))
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
        Token name = Consume(TokenType.IDENTIFIER, "Expect variable name.");

        string? typeAnnotation = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after ambient variable declaration.");

        // Ambient declarations have no initializer
        if (isConst)
        {
            // For ambient const, we use Var with no initializer (special case)
            return new Stmt.Var(name, typeAnnotation, null);
        }
        return new Stmt.Var(name, typeAnnotation, null);
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

        Consume(TokenType.SEMICOLON, "Expect ';' after 'using' declaration.");
        return new Stmt.Using(usingKeyword, bindings, isAwait);
    }

    /// <summary>
    /// Parses a single binding in a using declaration.
    /// Currently only supports simple identifiers (destructuring may be added later).
    /// </summary>
    private Stmt.UsingBinding ParseUsingBinding()
    {
        Token name = Consume(TokenType.IDENTIFIER, "Expect variable name in 'using' declaration.");

        string? typeAnnotation = null;
        if (Match(TokenType.COLON))
        {
            typeAnnotation = ParseTypeAnnotation();
        }

        Consume(TokenType.EQUAL, "'using' declarations must be initialized.");
        Expr initializer = Expression();

        return new Stmt.UsingBinding(name, null, typeAnnotation, initializer);
    }
}
