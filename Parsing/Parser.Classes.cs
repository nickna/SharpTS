namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== CLASS DECLARATION ==============

    private Stmt ClassDeclaration(bool isAbstract, List<Decorator>? classDecorators = null, bool isDeclare = false)
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
            try
            {
            // Parse member decorators before modifiers
            List<Decorator>? memberDecorators = ParseDecorators();

            // Parse modifiers
            AccessModifier access = AccessModifier.Public;
            bool isStatic = false;
            bool isReadonly = false;
            bool isMemberAbstract = false;
            bool isOverride = false;
            bool isMemberAsync = false;

            while (Match(TokenType.PUBLIC, TokenType.PRIVATE, TokenType.PROTECTED, TokenType.STATIC, TokenType.READONLY, TokenType.ABSTRACT, TokenType.OVERRIDE, TokenType.ASYNC))
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
                    case TokenType.OVERRIDE: isOverride = true; break;
                    case TokenType.ASYNC: isMemberAsync = true; break;
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

            // Validate: override and static are mutually exclusive
            if (isOverride && isStatic)
            {
                throw new Exception($"Parse Error: Static methods cannot use the 'override' modifier.");
            }

            // Validate: override requires the class to have a superclass
            if (isOverride && superclass == null)
            {
                throw new Exception($"Parse Error: Cannot use 'override' modifier in a class that does not extend another class.");
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

                accessors.Add(new Stmt.Accessor(accessorName, kind, setterParam, body, returnType, access, isMemberAbstract, isOverride, memberDecorators));
            }
            // Check for private field: #name...
            else if (Peek().Type == TokenType.PRIVATE_IDENTIFIER)
            {
                // Validate: ES2022 private fields cannot have access modifiers
                if (access != AccessModifier.Public || memberDecorators != null)
                {
                    throw new Exception($"Parse Error at line {Peek().Line}: ES2022 private fields (#name) cannot have access modifiers or decorators.");
                }

                Token fieldName = Consume(TokenType.PRIVATE_IDENTIFIER, "Expect private field name.");

                // Check if it's a method: #name(
                if (Check(TokenType.LEFT_PAREN))
                {
                    // Private method: #name() { }
                    List<TypeParam>? typeParams2 = ParseTypeParameters();
                    Consume(TokenType.LEFT_PAREN, "Expect '(' after private method name.");
                    List<Stmt.Parameter> parameters = ParseMethodParameters();
                    Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

                    string? returnType = null;
                    if (Match(TokenType.COLON))
                    {
                        returnType = ParseTypeAnnotation();
                    }

                    Consume(TokenType.LEFT_BRACE, "Expect '{' before private method body.");
                    List<Stmt> body = Block();

                    var func = new Stmt.Function(fieldName, typeParams2, null, parameters, body, returnType, isStatic, AccessModifier.Public, IsAbstract: false, IsOverride: false, IsAsync: isMemberAsync, IsGenerator: false, Decorators: null, IsPrivate: true);
                    methods.Add(func);
                }
                else
                {
                    // Private field: #name: type or #name = value
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

                    Consume(TokenType.SEMICOLON, "Expect ';' after private field declaration.");
                    fields.Add(new Stmt.Field(fieldName, typeAnnotation, initializer, isStatic, AccessModifier.Public, isReadonly, IsOptional: false, HasDefiniteAssignmentAssertion: false, Decorators: null, IsPrivate: true));
                }
            }
            else if (Peek().Type == TokenType.IDENTIFIER && (PeekNext().Type == TokenType.COLON || PeekNext().Type == TokenType.QUESTION || PeekNext().Type == TokenType.BANG))
            {
                // Field declaration
                Token fieldName = Consume(TokenType.IDENTIFIER, "Expect field name.");
                bool isOptional = Match(TokenType.QUESTION);
                bool hasDefiniteAssignment = Match(TokenType.BANG);

                // Validate: ! and ? are mutually exclusive
                if (isOptional && hasDefiniteAssignment)
                {
                    throw new Exception($"Parse Error at line {fieldName.Line}: A property cannot be both optional and have a definite assignment assertion.");
                }

                Consume(TokenType.COLON, "Expect ':' after field name.");
                string typeAnnotation = ParseTypeAnnotation();
                Expr? initializer = null;
                if (Match(TokenType.EQUAL))
                {
                    initializer = Expression();
                }

                // Validate: ! cannot coexist with initializer
                if (hasDefiniteAssignment && initializer != null)
                {
                    throw new Exception($"Parse Error at line {fieldName.Line}: Definite assignment assertion '!' cannot be used with an initializer.");
                }

                Consume(TokenType.SEMICOLON, "Expect ';' after field declaration.");
                fields.Add(new Stmt.Field(fieldName, typeAnnotation, initializer, isStatic, access, isReadonly, isOptional, hasDefiniteAssignment, memberDecorators));
            }
            else
            {
                // Abstract methods cannot be constructors
                if (isMemberAbstract && Check(TokenType.CONSTRUCTOR))
                {
                    throw new Exception("Parse Error: A constructor cannot be abstract.");
                }

                // Override cannot be used on constructors
                if (isOverride && Check(TokenType.CONSTRUCTOR))
                {
                    throw new Exception("Parse Error: A constructor cannot use the 'override' modifier.");
                }

                if (isMemberAbstract)
                {
                    // Parse abstract method: signature only, no body
                    Token methodName = Consume(TokenType.IDENTIFIER, "Expect method name.");
                    List<TypeParam>? typeParams2 = ParseTypeParameters();
                    Consume(TokenType.LEFT_PAREN, "Expect '(' after method name.");

                    // Check for 'this' parameter in abstract method
                    string? thisType = null;
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

                    List<Stmt.Parameter> parameters = ParseMethodParameters();
                    Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

                    string? returnType = null;
                    if (Match(TokenType.COLON))
                    {
                        returnType = ParseTypeAnnotation();
                    }

                    Consume(TokenType.SEMICOLON, "Expect ';' after abstract method declaration.");

                    var func = new Stmt.Function(methodName, typeParams2, thisType, parameters, null, returnType, isStatic, access, IsAbstract: true, IsOverride: isOverride, IsAsync: isMemberAsync, IsGenerator: false, Decorators: memberDecorators);
                    methods.Add(func);
                }
                else
                {
                    string kind = "method";
                    if (Check(TokenType.CONSTRUCTOR)) kind = "constructor";
                    var func = (Stmt.Function)FunctionDeclaration(kind, isMemberAsync);
                    func = func with { IsStatic = isStatic, Access = access, IsOverride = isOverride, Decorators = memberDecorators };
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
            catch (Exception ex)
            {
                RecordError(ex.Message);
                SynchronizeInClassBody();
                if (_errors.Count >= MaxErrors) break;
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after class body.");
        return new Stmt.Class(name, typeParams, superclass, superclassTypeArgs, methods, fields, accessors.Count > 0 ? accessors : null, interfaces, interfaceTypeArgs, isAbstract, classDecorators, isDeclare);
    }

    /// <summary>
    /// Synchronizes within a class body, stopping at member boundaries or the closing brace.
    /// </summary>
    private void SynchronizeInClassBody()
    {
        while (!IsAtEnd())
        {
            // Stop at closing brace (end of class body)
            if (Check(TokenType.RIGHT_BRACE)) return;

            // Check if we're at a token that starts a new member
            switch (Peek().Type)
            {
                case TokenType.PUBLIC:
                case TokenType.PRIVATE:
                case TokenType.PROTECTED:
                case TokenType.STATIC:
                case TokenType.READONLY:
                case TokenType.ABSTRACT:
                case TokenType.OVERRIDE:
                case TokenType.ASYNC:
                case TokenType.GET:
                case TokenType.SET:
                case TokenType.CONSTRUCTOR:
                    return;
                case TokenType.IDENTIFIER:
                case TokenType.PRIVATE_IDENTIFIER:
                    // Could be the start of a field or method
                    return;
            }

            Advance();

            // Check if we just passed a semicolon (field boundary)
            if (Previous().Type == TokenType.SEMICOLON) return;
        }
    }

    // ============== CLASS EXPRESSION ==============

    /// <summary>
    /// Parses a class expression: class [Name] [extends Base] [implements Interfaces] { members }
    /// Unlike class declarations, the name is optional (anonymous class).
    /// </summary>
    private Expr ClassExpression()
    {
        // Optional class name (visible inside class body for self-reference)
        Token? name = null;
        if (Check(TokenType.IDENTIFIER))
        {
            name = Advance();
        }

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
            try
            {
            // Parse modifiers (no decorators on class expression members per TypeScript spec)
            AccessModifier access = AccessModifier.Public;
            bool isStatic = false;
            bool isReadonly = false;
            bool isMemberAsync = false;

            while (Match(TokenType.PUBLIC, TokenType.PRIVATE, TokenType.PROTECTED, TokenType.STATIC, TokenType.READONLY, TokenType.ASYNC))
            {
                var modifier = Previous().Type;
                switch (modifier)
                {
                    case TokenType.PUBLIC: access = AccessModifier.Public; break;
                    case TokenType.PRIVATE: access = AccessModifier.Private; break;
                    case TokenType.PROTECTED: access = AccessModifier.Protected; break;
                    case TokenType.STATIC: isStatic = true; break;
                    case TokenType.READONLY: isReadonly = true; break;
                    case TokenType.ASYNC: isMemberAsync = true; break;
                }
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

                Consume(TokenType.LEFT_BRACE, "Expect '{' before accessor body.");
                List<Stmt> body = Block();

                accessors.Add(new Stmt.Accessor(accessorName, kind, setterParam, body, returnType, access));
            }
            // Check for private field/method: #name...
            else if (Peek().Type == TokenType.PRIVATE_IDENTIFIER)
            {
                // Validate: ES2022 private fields cannot have access modifiers
                if (access != AccessModifier.Public)
                {
                    throw new Exception($"Parse Error at line {Peek().Line}: ES2022 private fields (#name) cannot have access modifiers.");
                }

                Token fieldName = Consume(TokenType.PRIVATE_IDENTIFIER, "Expect private field name.");

                // Check if it's a method: #name(
                if (Check(TokenType.LEFT_PAREN))
                {
                    // Private method: #name() { }
                    List<TypeParam>? typeParams2 = ParseTypeParameters();
                    Consume(TokenType.LEFT_PAREN, "Expect '(' after private method name.");
                    List<Stmt.Parameter> parameters = ParseMethodParameters();
                    Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

                    string? returnType = null;
                    if (Match(TokenType.COLON))
                    {
                        returnType = ParseTypeAnnotation();
                    }

                    Consume(TokenType.LEFT_BRACE, "Expect '{' before private method body.");
                    List<Stmt> body = Block();

                    var func = new Stmt.Function(fieldName, typeParams2, null, parameters, body, returnType, isStatic, AccessModifier.Public, IsAbstract: false, IsOverride: false, IsAsync: isMemberAsync, IsGenerator: false, Decorators: null, IsPrivate: true);
                    methods.Add(func);
                }
                else
                {
                    // Private field: #name: type or #name = value
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

                    Consume(TokenType.SEMICOLON, "Expect ';' after private field declaration.");
                    fields.Add(new Stmt.Field(fieldName, typeAnnotation, initializer, isStatic, AccessModifier.Public, isReadonly, IsOptional: false, HasDefiniteAssignmentAssertion: false, Decorators: null, IsPrivate: true));
                }
            }
            else if (Peek().Type == TokenType.IDENTIFIER && (PeekNext().Type == TokenType.COLON || PeekNext().Type == TokenType.QUESTION || PeekNext().Type == TokenType.BANG))
            {
                // Field declaration
                Token fieldName = Consume(TokenType.IDENTIFIER, "Expect field name.");
                bool isOptional = Match(TokenType.QUESTION);
                bool hasDefiniteAssignment = Match(TokenType.BANG);

                // Validate: ! and ? are mutually exclusive
                if (isOptional && hasDefiniteAssignment)
                {
                    throw new Exception($"Parse Error at line {fieldName.Line}: A property cannot be both optional and have a definite assignment assertion.");
                }

                Consume(TokenType.COLON, "Expect ':' after field name.");
                string typeAnnotation = ParseTypeAnnotation();
                Expr? initializer = null;
                if (Match(TokenType.EQUAL))
                {
                    initializer = Expression();
                }

                // Validate: ! cannot coexist with initializer
                if (hasDefiniteAssignment && initializer != null)
                {
                    throw new Exception($"Parse Error at line {fieldName.Line}: Definite assignment assertion '!' cannot be used with an initializer.");
                }

                Consume(TokenType.SEMICOLON, "Expect ';' after field declaration.");
                fields.Add(new Stmt.Field(fieldName, typeAnnotation, initializer, isStatic, access, isReadonly, isOptional, hasDefiniteAssignment));
            }
            else
            {
                string kind = "method";
                if (Check(TokenType.CONSTRUCTOR)) kind = "constructor";
                var func = (Stmt.Function)FunctionDeclaration(kind, isMemberAsync);
                func = func with { IsStatic = isStatic, Access = access };
                methods.Add(func);

                // Synthesize fields from constructor parameter properties
                if (kind == "constructor")
                {
                    foreach (var param in func.Parameters)
                    {
                        if (param.IsParameterProperty)
                        {
                            if (fields.Any(f => f.Name.Lexeme == param.Name.Lexeme))
                            {
                                throw new Exception($"Parse Error: Parameter property '{param.Name.Lexeme}' conflicts with existing field declaration.");
                            }
                            fields.Add(new Stmt.Field(
                                param.Name,
                                param.Type,
                                null,
                                false,
                                param.Access ?? AccessModifier.Public,
                                param.IsReadonly,
                                false
                            ));
                        }
                    }
                }
            }
            }
            catch (Exception ex)
            {
                RecordError(ex.Message);
                SynchronizeInClassBody();
                if (_errors.Count >= MaxErrors) break;
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after class body.");

        return new Expr.ClassExpr(
            name,
            typeParams,
            superclass,
            superclassTypeArgs,
            methods,
            fields,
            accessors.Count > 0 ? accessors : null,
            interfaces,
            interfaceTypeArgs,
            IsAbstract: false  // Class expressions cannot be abstract
        );
    }
}
