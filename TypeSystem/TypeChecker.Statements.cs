using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Statement type checking - CheckStmt and related statement handlers.
/// </summary>
/// <remarks>
/// Contains the main statement dispatch (CheckStmt) and handlers for:
/// block, switch, try/catch, function declarations, enum declarations,
/// and their helper methods.
/// </remarks>
public partial class TypeChecker
{
    private void CheckStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                CheckBlock(block.Statements, new TypeEnvironment(_environment));
                break;
            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    CheckStmt(s);
                break;

            case Stmt.LabeledStatement labeledStmt:
                {
                    string labelName = labeledStmt.Label.Lexeme;

                    // Check for label shadowing
                    if (_activeLabels.ContainsKey(labelName))
                    {
                        throw new Exception($"Type Error: Label '{labelName}' already declared in this scope.");
                    }

                    // Determine if this label is on a loop (for continue validation)
                    bool isOnLoop = labeledStmt.Statement is Stmt.While
                                 or Stmt.DoWhile
                                 or Stmt.ForOf
                                 or Stmt.ForIn
                                 or Stmt.LabeledStatement; // Allow chained labels

                    // If chained label, inherit loop status from inner
                    if (labeledStmt.Statement is Stmt.LabeledStatement innerLabeled)
                    {
                        // We need to peek ahead - for now, mark as potentially a loop
                        // The inner labeled statement will be checked recursively
                        isOnLoop = true;
                    }

                    _activeLabels[labelName] = isOnLoop;
                    try
                    {
                        CheckStmt(labeledStmt.Statement);
                    }
                    finally
                    {
                        _activeLabels.Remove(labelName);
                    }
                }
                break;

            case Stmt.Interface interfaceStmt:
                // Handle generic type parameters
                List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
                TypeEnvironment interfaceTypeEnv = new(_environment);
                if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
                {
                    interfaceTypeParams = [];
                    foreach (var tp in interfaceStmt.TypeParams)
                    {
                        TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                        var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                        interfaceTypeParams.Add(typeParam);
                        interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                    }
                }

                // Use interfaceTypeEnv for member type resolution so T resolves correctly
                TypeEnvironment savedEnvForInterface = _environment;
                _environment = interfaceTypeEnv;

                Dictionary<string, TypeInfo> members = [];
                HashSet<string> optionalMembers = [];
                foreach (var member in interfaceStmt.Members)
                {
                    members[member.Name.Lexeme] = ToTypeInfo(member.Type);
                    if (member.IsOptional)
                    {
                        optionalMembers.Add(member.Name.Lexeme);
                    }
                }

                // Process index signatures
                TypeInfo? stringIndexType = null;
                TypeInfo? numberIndexType = null;
                TypeInfo? symbolIndexType = null;
                if (interfaceStmt.IndexSignatures != null)
                {
                    foreach (var indexSig in interfaceStmt.IndexSignatures)
                    {
                        TypeInfo valueType = ToTypeInfo(indexSig.ValueType);
                        switch (indexSig.KeyType)
                        {
                            case TokenType.TYPE_STRING:
                                if (stringIndexType != null)
                                    throw new Exception($"Type Error: Duplicate string index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                                stringIndexType = valueType;
                                break;
                            case TokenType.TYPE_NUMBER:
                                if (numberIndexType != null)
                                    throw new Exception($"Type Error: Duplicate number index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                                numberIndexType = valueType;
                                break;
                            case TokenType.TYPE_SYMBOL:
                                if (symbolIndexType != null)
                                    throw new Exception($"Type Error: Duplicate symbol index signature in interface '{interfaceStmt.Name.Lexeme}'.");
                                symbolIndexType = valueType;
                                break;
                        }
                    }

                    // TypeScript rule: number index type must be assignable to string index type
                    if (stringIndexType != null && numberIndexType != null)
                    {
                        if (!IsCompatible(stringIndexType, numberIndexType))
                        {
                            throw new Exception($"Type Error: Number index type '{numberIndexType}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                        }
                    }

                    // Validate explicit properties are compatible with string index signature
                    if (stringIndexType != null)
                    {
                        foreach (var (name, type) in members)
                        {
                            if (!IsCompatible(stringIndexType, type))
                            {
                                throw new Exception($"Type Error: Property '{name}' of type '{type}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.");
                            }
                        }
                    }
                }

                // Restore environment
                _environment = savedEnvForInterface;

                // Create GenericInterface or regular Interface
                if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
                {
                    var genericItfType = new TypeInfo.GenericInterface(
                        interfaceStmt.Name.Lexeme,
                        interfaceTypeParams,
                        members,
                        optionalMembers
                    );
                    _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
                }
                else
                {
                    TypeInfo.Interface itfType = new(
                        interfaceStmt.Name.Lexeme,
                        members,
                        optionalMembers,
                        stringIndexType,
                        numberIndexType,
                        symbolIndexType
                    );
                    _environment.Define(interfaceStmt.Name.Lexeme, itfType);
                }
                break;
            case Stmt.TypeAlias typeAlias:
                _environment.DefineTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition);
                break;
            case Stmt.Enum enumStmt:
                CheckEnumDeclaration(enumStmt);
                break;
            case Stmt.Class classStmt:
                TypeInfo.Class? superclass = null;
                if (classStmt.Superclass != null)
                {
                    TypeInfo superType = LookupVariable(classStmt.Superclass);
                    if (superType is TypeInfo.Instance si && si.ClassType is TypeInfo.Class sic)
                        superclass = sic;
                    else if (superType is TypeInfo.Class sc)
                        superclass = sc;
                    else
                        throw new Exception("Superclass must be a class.");
                }

                // Handle generic type parameters
                List<TypeInfo.TypeParameter>? classTypeParams = null;
                TypeEnvironment classTypeEnv = new(_environment);
                if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
                {
                    classTypeParams = [];
                    foreach (var tp in classStmt.TypeParams)
                    {
                        TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                        var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                        classTypeParams.Add(typeParam);
                        classTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                    }
                }

                // Use classTypeEnv for type resolution so T resolves correctly
                TypeEnvironment savedEnvForClass = _environment;
                _environment = classTypeEnv;

                Dictionary<string, TypeInfo> declaredMethods = [];
                Dictionary<string, TypeInfo> declaredStaticMethods = [];
                Dictionary<string, TypeInfo> declaredStaticProperties = [];
                Dictionary<string, AccessModifier> methodAccess = [];
                Dictionary<string, AccessModifier> fieldAccess = [];
                HashSet<string> readonlyFields = [];
                HashSet<string> abstractMethods = [];
                HashSet<string> abstractGetters = [];
                HashSet<string> abstractSetters = [];

                // Helper to build a TypeInfo.Function from a method declaration
                TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
                {
                    List<TypeInfo> methodParamTypes = [];
                    TypeInfo methodReturnType = method.ReturnType != null ? ToTypeInfo(method.ReturnType) : new TypeInfo.Void();

                    int methodRequiredParams = 0;
                    bool methodSeenDefault = false;

                    foreach (var param in method.Parameters)
                    {
                        methodParamTypes.Add(param.Type != null ? ToTypeInfo(param.Type) : new TypeInfo.Any());

                        if (param.IsRest) continue;

                        if (param.DefaultValue != null || param.IsOptional)
                        {
                            methodSeenDefault = true;
                        }
                        else
                        {
                            if (methodSeenDefault)
                            {
                                throw new Exception($"Type Error: Required parameter cannot follow optional parameter in method '{method.Name.Lexeme}'.");
                            }
                            methodRequiredParams++;
                        }
                    }

                    bool methodHasRest = method.Parameters.Any(p => p.IsRest);
                    return new TypeInfo.Function(methodParamTypes, methodReturnType, methodRequiredParams, methodHasRest);
                }

                // First pass: collect signatures, grouping overloads
                // Group methods by name to detect overloads
                var methodGroups = classStmt.Methods.GroupBy(m => m.Name.Lexeme).ToList();

                foreach (var group in methodGroups)
                {
                    string methodName = group.Key;
                    var methods = group.ToList();

                    // Separate overload signatures (null body) from implementations
                    var signatures = methods.Where(m => m.Body == null && !m.IsAbstract).ToList();
                    var implementations = methods.Where(m => m.Body != null).ToList();
                    var abstractDecls = methods.Where(m => m.IsAbstract).ToList();

                    // Handle abstract methods (no body, but marked abstract)
                    if (abstractDecls.Count > 0)
                    {
                        if (abstractDecls.Count > 1)
                        {
                            throw new Exception($"Type Error: Cannot have multiple abstract declarations for method '{methodName}'.");
                        }
                        var abstractMethod = abstractDecls[0];
                        var funcType = BuildMethodFuncType(abstractMethod);

                        if (abstractMethod.IsStatic)
                            declaredStaticMethods[methodName] = funcType;
                        else
                            declaredMethods[methodName] = funcType;

                        methodAccess[methodName] = abstractMethod.Access;
                        abstractMethods.Add(methodName);
                        continue;
                    }

                    // Handle overloaded methods
                    if (signatures.Count > 0)
                    {
                        if (implementations.Count == 0)
                        {
                            throw new Exception($"Type Error: Overloaded method '{methodName}' has no implementation.");
                        }
                        if (implementations.Count > 1)
                        {
                            throw new Exception($"Type Error: Overloaded method '{methodName}' has multiple implementations.");
                        }

                        var implementation = implementations[0];
                        var signatureTypes = signatures.Select(BuildMethodFuncType).ToList();
                        var implType = BuildMethodFuncType(implementation);

                        // Validate implementation is compatible with all signatures
                        foreach (var sig in signatureTypes)
                        {
                            if (implType.MinArity > sig.MinArity)
                            {
                                throw new Exception($"Type Error: Implementation of '{methodName}' requires {implType.MinArity} arguments but overload signature requires only {sig.MinArity}.");
                            }
                        }

                        var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, implType);

                        if (implementation.IsStatic)
                            declaredStaticMethods[methodName] = overloadedFunc;
                        else
                            declaredMethods[methodName] = overloadedFunc;

                        methodAccess[methodName] = implementation.Access;
                    }
                    else if (implementations.Count == 1)
                    {
                        // Single non-overloaded method
                        var method = implementations[0];
                        var funcType = BuildMethodFuncType(method);

                        if (method.IsStatic)
                            declaredStaticMethods[methodName] = funcType;
                        else
                            declaredMethods[methodName] = funcType;

                        methodAccess[methodName] = method.Access;
                    }
                    else if (implementations.Count > 1)
                    {
                        throw new Exception($"Type Error: Multiple implementations of method '{methodName}' without overload signatures.");
                    }
                }

                // Collect static property types, field access modifiers, and non-static field types
                Dictionary<string, TypeInfo> declaredFieldTypes = [];
                foreach (var field in classStmt.Fields)
                {
                    string fieldName = field.Name.Lexeme;
                    TypeInfo fieldType = field.TypeAnnotation != null
                        ? ToTypeInfo(field.TypeAnnotation)
                        : new TypeInfo.Any();

                    if (field.IsStatic)
                    {
                        declaredStaticProperties[fieldName] = fieldType;
                    }
                    else
                    {
                        declaredFieldTypes[fieldName] = fieldType;
                    }
                    fieldAccess[fieldName] = field.Access;
                    if (field.IsReadonly)
                    {
                        readonlyFields.Add(fieldName);
                    }
                }

                // Collect accessor types
                Dictionary<string, TypeInfo> getters = [];
                Dictionary<string, TypeInfo> setters = [];

                if (classStmt.Accessors != null)
                {
                    foreach (var accessor in classStmt.Accessors)
                    {
                        string propName = accessor.Name.Lexeme;

                        if (accessor.Kind.Type == TokenType.GET)
                        {
                            TypeInfo getterRetType = accessor.ReturnType != null
                                ? ToTypeInfo(accessor.ReturnType)
                                : new TypeInfo.Any();
                            getters[propName] = getterRetType;

                            // Track abstract getters
                            if (accessor.IsAbstract)
                            {
                                abstractGetters.Add(propName);
                            }
                        }
                        else // SET
                        {
                            TypeInfo paramType = accessor.SetterParam?.Type != null
                                ? ToTypeInfo(accessor.SetterParam.Type)
                                : new TypeInfo.Any();
                            setters[propName] = paramType;

                            // Track abstract setters
                            if (accessor.IsAbstract)
                            {
                                abstractSetters.Add(propName);
                            }
                        }
                    }

                    // Validate that getter/setter pairs have matching types
                    foreach (var propName in getters.Keys.Intersect(setters.Keys))
                    {
                        if (!IsCompatible(getters[propName], setters[propName]))
                        {
                            throw new Exception($"Type Error: Getter and setter for '{propName}' have incompatible types.");
                        }
                    }
                }

                // Restore environment before defining class type (define in outer scope)
                _environment = savedEnvForClass;

                // Create GenericClass or regular Class based on type parameters
                TypeInfo.Class classTypeForBody;
                if (classTypeParams != null && classTypeParams.Count > 0)
                {
                    var genericClassType = new TypeInfo.GenericClass(
                        classStmt.Name.Lexeme,
                        classTypeParams,
                        superclass,
                        declaredMethods,
                        declaredStaticMethods,
                        declaredStaticProperties,
                        methodAccess,
                        fieldAccess,
                        readonlyFields,
                        getters,
                        setters,
                        declaredFieldTypes,
                        classStmt.IsAbstract,
                        abstractMethods.Count > 0 ? abstractMethods : null,
                        abstractGetters.Count > 0 ? abstractGetters : null,
                        abstractSetters.Count > 0 ? abstractSetters : null
                    );
                    _environment.Define(classStmt.Name.Lexeme, genericClassType);
                    // For body check, create a Class type (methods/fields have TypeParameter types)
                    classTypeForBody = new TypeInfo.Class(
                        classStmt.Name.Lexeme, superclass, declaredMethods, declaredStaticMethods, declaredStaticProperties,
                        methodAccess, fieldAccess, readonlyFields, getters, setters, declaredFieldTypes,
                        classStmt.IsAbstract, abstractMethods.Count > 0 ? abstractMethods : null,
                        abstractGetters.Count > 0 ? abstractGetters : null, abstractSetters.Count > 0 ? abstractSetters : null);
                }
                else
                {
                    var classType = new TypeInfo.Class(
                        classStmt.Name.Lexeme, superclass, declaredMethods, declaredStaticMethods, declaredStaticProperties,
                        methodAccess, fieldAccess, readonlyFields, getters, setters, declaredFieldTypes,
                        classStmt.IsAbstract, abstractMethods.Count > 0 ? abstractMethods : null,
                        abstractGetters.Count > 0 ? abstractGetters : null, abstractSetters.Count > 0 ? abstractSetters : null);
                    _environment.Define(classStmt.Name.Lexeme, classType);
                    classTypeForBody = classType;
                }

                // Validate implemented interfaces (skip for generic classes - validated at instantiation)
                if (classStmt.Interfaces != null && classTypeParams == null)
                {
                    foreach (var interfaceToken in classStmt.Interfaces)
                    {
                        TypeInfo? itfTypeInfo = _environment.Get(interfaceToken.Lexeme);
                        if (itfTypeInfo is not TypeInfo.Interface interfaceType)
                        {
                            throw new Exception($"Type Error: '{interfaceToken.Lexeme}' is not an interface.");
                        }
                        ValidateInterfaceImplementation(classTypeForBody, interfaceType, classStmt.Name.Lexeme);
                    }
                }

                // Validate abstract member implementation (skip for generic classes - validated at instantiation)
                if (!classStmt.IsAbstract && classTypeParams == null)
                {
                    ValidateAbstractMemberImplementation(classTypeForBody, classStmt.Name.Lexeme);
                }

                // Validate override members (skip for generic classes - validated at instantiation)
                if (classTypeParams == null)
                {
                    ValidateOverrideMembers(classStmt, classTypeForBody);
                }

                // Second pass: check static property initializers at class scope
                foreach (var field in classStmt.Fields)
                {
                    if (field.IsStatic && field.Initializer != null)
                    {
                        TypeInfo initType = CheckExpr(field.Initializer);
                        TypeInfo staticFieldDeclaredType = declaredStaticProperties[field.Name.Lexeme];
                        if (!IsCompatible(staticFieldDeclaredType, initType))
                        {
                            throw new Exception($"Type Error: Cannot assign type '{initType}' to static property '{field.Name.Lexeme}' of type '{staticFieldDeclaredType}'.");
                        }
                    }
                }

                // Third pass: body check
                TypeEnvironment classEnv = new(_environment);
                // For generic classes, add type parameters to class scope
                if (classTypeParams != null)
                {
                    foreach (var tp in classTypeParams)
                        classEnv.DefineTypeParameter(tp.Name, tp);
                }
                classEnv.Define("this", new TypeInfo.Instance(classTypeForBody));
                if (superclass != null)
                {
                    classEnv.Define("super", superclass);
                }

                TypeEnvironment prevEnv = _environment;
                TypeInfo.Class? prevClass = _currentClass;

                _environment = classEnv;
                _currentClass = classTypeForBody;

                try
                {
                    // Only check methods that have bodies (skip overload signatures)
                    foreach (var method in classStmt.Methods.Where(m => m.Body != null))
                    {
                        // For static methods, use a different environment without this/super
                        TypeEnvironment methodEnv;
                        if (method.IsStatic)
                        {
                            methodEnv = new TypeEnvironment(prevEnv); // No this/super
                        }
                        else
                        {
                            methodEnv = new TypeEnvironment(_environment);
                        }

                        // Get the method type (could be Function or OverloadedFunction)
                        var declaredMethodType = method.IsStatic
                            ? declaredStaticMethods[method.Name.Lexeme]
                            : declaredMethods[method.Name.Lexeme];

                        // Get the actual function type (implementation for overloads)
                        TypeInfo.Function methodType = declaredMethodType switch
                        {
                            TypeInfo.OverloadedFunction of => of.Implementation,
                            TypeInfo.Function f => f,
                            _ => throw new Exception($"Type Error: Unexpected method type for '{method.Name.Lexeme}'.")
                        };

                        for (int i = 0; i < method.Parameters.Count; i++)
                        {
                            methodEnv.Define(method.Parameters[i].Name.Lexeme, methodType.ParamTypes[i]);
                        }

                        // Save and set context - method bodies are isolated from outer loop/switch/label context
                        TypeEnvironment previousEnvFunc = _environment;
                        TypeInfo? previousReturnFunc = _currentFunctionReturnType;
                        bool previousInStatic = _inStaticMethod;
                        bool previousInAsyncFunc = _inAsyncFunction;
                        int previousLoopDepthFunc = _loopDepth;
                        int previousSwitchDepthFunc = _switchDepth;
                        var previousActiveLabelsFunc = new Dictionary<string, bool>(_activeLabels);

                        _environment = methodEnv;
                        _currentFunctionReturnType = methodType.ReturnType;
                        _inStaticMethod = method.IsStatic;
                        _inAsyncFunction = method.IsAsync;
                        _loopDepth = 0;
                        _switchDepth = 0;
                        _activeLabels.Clear();

                        try
                        {
                            // Abstract methods have no body to check
                            if (method.Body != null)
                            {
                                foreach (var bodyStmt in method.Body)
                                {
                                    CheckStmt(bodyStmt);
                                }
                            }
                        }
                        finally
                        {
                            _environment = previousEnvFunc;
                            _currentFunctionReturnType = previousReturnFunc;
                            _inStaticMethod = previousInStatic;
                            _inAsyncFunction = previousInAsyncFunc;
                            _loopDepth = previousLoopDepthFunc;
                            _switchDepth = previousSwitchDepthFunc;
                            _activeLabels.Clear();
                            foreach (var kvp in previousActiveLabelsFunc)
                                _activeLabels[kvp.Key] = kvp.Value;
                        }
                    }

                    // Check accessor bodies
                    if (classStmt.Accessors != null)
                    {
                        foreach (var accessor in classStmt.Accessors)
                        {
                            TypeEnvironment accessorEnv = new TypeEnvironment(_environment);

                            TypeInfo accessorReturnType;
                            if (accessor.Kind.Type == TokenType.GET)
                            {
                                accessorReturnType = getters[accessor.Name.Lexeme];
                            }
                            else
                            {
                                // Setter has void return type
                                accessorReturnType = new TypeInfo.Void();
                                // Add setter parameter to environment
                                if (accessor.SetterParam != null)
                                {
                                    TypeInfo setterParamType = setters[accessor.Name.Lexeme];
                                    accessorEnv.Define(accessor.SetterParam.Name.Lexeme, setterParamType);
                                }
                            }

                            // Save and set context - accessor bodies are isolated from outer loop/switch/label context
                            TypeEnvironment previousEnvAcc = _environment;
                            TypeInfo? previousReturnAcc = _currentFunctionReturnType;
                            int previousLoopDepthAcc = _loopDepth;
                            int previousSwitchDepthAcc = _switchDepth;
                            var previousActiveLabelsAcc = new Dictionary<string, bool>(_activeLabels);

                            _environment = accessorEnv;
                            _currentFunctionReturnType = accessorReturnType;
                            _loopDepth = 0;
                            _switchDepth = 0;
                            _activeLabels.Clear();

                            try
                            {
                                foreach (var bodyStmt in accessor.Body)
                                {
                                    CheckStmt(bodyStmt);
                                }
                            }
                            finally
                            {
                                _environment = previousEnvAcc;
                                _currentFunctionReturnType = previousReturnAcc;
                                _loopDepth = previousLoopDepthAcc;
                                _switchDepth = previousSwitchDepthAcc;
                                _activeLabels.Clear();
                                foreach (var kvp in previousActiveLabelsAcc)
                                    _activeLabels[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                finally
                {
                    _environment = prevEnv;
                    _currentClass = prevClass;
                }
                break;

            case Stmt.Var varStmt:
                TypeInfo? declaredType = null;
                if (varStmt.TypeAnnotation != null)
                {
                    declaredType = ToTypeInfo(varStmt.TypeAnnotation);
                }

                if (varStmt.Initializer != null)
                {
                    // Special case: array literal assigned to tuple type (contextual typing)
                    if (declaredType is TypeInfo.Tuple tupleType && varStmt.Initializer is Expr.ArrayLiteral arrayLit)
                    {
                        CheckArrayLiteralAgainstTuple(arrayLit, tupleType, varStmt.Name.Lexeme);
                    }
                    else
                    {
                        TypeInfo initializerType = CheckExpr(varStmt.Initializer);
                        if (declaredType != null && !IsCompatible(declaredType, initializerType))
                        {
                            throw new Exception($"Type Error: Cannot assign type '{initializerType}' to variable '{varStmt.Name.Lexeme}' of type '{declaredType}'.");
                        }
                        declaredType ??= initializerType;
                    }
                }

                declaredType ??= new TypeInfo.Any();
                _environment.Define(varStmt.Name.Lexeme, declaredType);
                break;

            case Stmt.Function funcStmt:
                CheckFunctionDeclaration(funcStmt);
                break;

            case Stmt.Return returnStmt:
                if (_currentFunctionReturnType != null)
                {
                    // Special case: array literal returned to tuple return type (contextual typing)
                    if (_currentFunctionReturnType is TypeInfo.Tuple tupleRetType &&
                        returnStmt.Value is Expr.ArrayLiteral arrayLitRet)
                    {
                        CheckArrayLiteralAgainstTuple(arrayLitRet, tupleRetType, "return value");
                    }
                    else
                    {
                        TypeInfo actualReturnType = returnStmt.Value != null
                            ? CheckExpr(returnStmt.Value)
                            : new TypeInfo.Void();

                        // For async functions, the return type is Promise<T> but we can return T directly
                        // (the runtime automatically wraps it in a Promise)
                        TypeInfo expectedReturnType = _currentFunctionReturnType;
                        if (_inAsyncFunction && expectedReturnType is TypeInfo.Promise promiseType)
                        {
                            expectedReturnType = promiseType.ValueType;
                        }

                        if (!IsCompatible(expectedReturnType, actualReturnType))
                        {
                             throw new Exception($"Type Error: Function declared to return '{_currentFunctionReturnType}' but returned '{actualReturnType}'.");
                        }
                    }
                }
                else if (returnStmt.Value != null)
                {
                    CheckExpr(returnStmt.Value);
                }
                break;

            case Stmt.Expression exprStmt:
                CheckExpr(exprStmt.Expr);
                break;

            case Stmt.If ifStmt:
                CheckExpr(ifStmt.Condition);

                var guard = AnalyzeTypeGuard(ifStmt.Condition);
                if (guard.VarName != null)
                {
                    // Then branch with narrowed type
                    var thenEnv = new TypeEnvironment(_environment);
                    thenEnv.Define(guard.VarName, guard.NarrowedType!);
                    var savedEnv = _environment;
                    _environment = thenEnv;
                    CheckStmt(ifStmt.ThenBranch);
                    _environment = savedEnv;

                    // Else branch with excluded type
                    if (ifStmt.ElseBranch != null && guard.ExcludedType != null)
                    {
                        var elseEnv = new TypeEnvironment(_environment);
                        elseEnv.Define(guard.VarName, guard.ExcludedType);
                        _environment = elseEnv;
                        CheckStmt(ifStmt.ElseBranch);
                        _environment = savedEnv;
                    }
                    else if (ifStmt.ElseBranch != null)
                    {
                        CheckStmt(ifStmt.ElseBranch);
                    }
                }
                else
                {
                    CheckStmt(ifStmt.ThenBranch);
                    if (ifStmt.ElseBranch != null) CheckStmt(ifStmt.ElseBranch);
                }
                break;

            case Stmt.While whileStmt:
                CheckExpr(whileStmt.Condition);
                _loopDepth++;
                try
                {
                    CheckStmt(whileStmt.Body);
                }
                finally
                {
                    _loopDepth--;
                }
                break;

            case Stmt.DoWhile doWhileStmt:
                _loopDepth++;
                try
                {
                    CheckStmt(doWhileStmt.Body);
                }
                finally
                {
                    _loopDepth--;
                }
                CheckExpr(doWhileStmt.Condition);
                break;

            case Stmt.ForOf forOf:
                TypeInfo iterableType = CheckExpr(forOf.Iterable);
                TypeInfo elementType = new TypeInfo.Any();

                // Get element type from array
                if (iterableType is TypeInfo.Array arr)
                {
                    elementType = arr.ElementType;
                }

                // Create new scope and define the loop variable
                TypeEnvironment forOfEnv = new(_environment);
                forOfEnv.Define(forOf.Variable.Lexeme, elementType);

                TypeEnvironment prevForOfEnv = _environment;
                _environment = forOfEnv;
                _loopDepth++;
                try
                {
                    CheckStmt(forOf.Body);
                }
                finally
                {
                    _loopDepth--;
                    _environment = prevForOfEnv;
                }
                break;

            case Stmt.ForIn forIn:
                TypeInfo objType = CheckExpr(forIn.Object);

                // for...in iterates over object keys, so element type is always string
                TypeInfo keyType = new TypeInfo.Primitive(TokenType.TYPE_STRING);

                // Validate that the iterable is an object-like type
                if (objType is not (TypeInfo.Record or TypeInfo.Instance or TypeInfo.Array or TypeInfo.Any or TypeInfo.Class))
                {
                    throw new Exception($"Type Error: 'for...in' requires an object, got {objType}.");
                }

                // Create new scope and define the loop variable
                TypeEnvironment forInEnv = new(_environment);
                forInEnv.Define(forIn.Variable.Lexeme, keyType);

                TypeEnvironment prevForInEnv = _environment;
                _environment = forInEnv;
                _loopDepth++;
                try
                {
                    CheckStmt(forIn.Body);
                }
                finally
                {
                    _loopDepth--;
                    _environment = prevForInEnv;
                }
                break;

            case Stmt.Break breakStmt:
                if (breakStmt.Label != null)
                {
                    // Labeled break: must target a valid label
                    string labelName = breakStmt.Label.Lexeme;
                    if (!_activeLabels.ContainsKey(labelName))
                    {
                        throw new Exception($"Type Error: Label '{labelName}' not found.");
                    }
                }
                else
                {
                    // Unlabeled break: must be inside a loop or switch
                    if (_loopDepth == 0 && _switchDepth == 0)
                    {
                        throw new Exception("Type Error: 'break' can only be used inside a loop or switch.");
                    }
                }
                break;

            case Stmt.Switch switchStmt:
                CheckSwitch(switchStmt);
                break;

            case Stmt.TryCatch tryCatch:
                CheckTryCatch(tryCatch);
                break;

            case Stmt.Throw throwStmt:
                CheckExpr(throwStmt.Value);
                break;

            case Stmt.Continue continueStmt:
                if (continueStmt.Label != null)
                {
                    // Labeled continue: must target a valid label on a loop
                    string labelName = continueStmt.Label.Lexeme;
                    if (!_activeLabels.TryGetValue(labelName, out bool isOnLoop))
                    {
                        throw new Exception($"Type Error: Label '{labelName}' not found.");
                    }
                    if (!isOnLoop)
                    {
                        throw new Exception($"Type Error: Cannot continue to non-loop label '{labelName}'.");
                    }
                }
                else
                {
                    // Unlabeled continue: must be inside a loop
                    if (_loopDepth == 0)
                    {
                        throw new Exception("Type Error: 'continue' can only be used inside a loop.");
                    }
                }
                break;

            case Stmt.Print printStmt:
                CheckExpr(printStmt.Expr);
                break;

            case Stmt.Import importStmt:
                // Imports are handled in CheckModules() during multi-module type checking.
                // In single-file mode, imports are an error since there's no module to import from.
                if (_currentModule == null)
                {
                    throw new Exception($"Type Error at line {importStmt.Keyword.Line}: Import statements require module mode. " +
                                       "Use 'dotnet run -- --compile' with multi-file support.");
                }
                // When in module mode, imports are resolved and bound in BindModuleImports()
                break;

            case Stmt.Export exportStmt:
                // Check the declaration or expression being exported
                CheckExportStatement(exportStmt);
                break;
        }
    }

    /// <summary>
    /// Type-checks an export statement and registers exports in the current module.
    /// </summary>
    private void CheckExportStatement(Stmt.Export exportStmt)
    {
        if (exportStmt.IsDefaultExport)
        {
            // export default expression or export default class/function
            if (exportStmt.Declaration != null)
            {
                CheckStmt(exportStmt.Declaration);
                if (_currentModule != null)
                {
                    _currentModule.DefaultExportType = GetDeclaredType(exportStmt.Declaration);
                }
            }
            else if (exportStmt.DefaultExpr != null)
            {
                var type = CheckExpr(exportStmt.DefaultExpr);
                if (_currentModule != null)
                {
                    _currentModule.DefaultExportType = type;
                }
            }
        }
        else if (exportStmt.Declaration != null)
        {
            // export class/function/const/let
            CheckStmt(exportStmt.Declaration);
            if (_currentModule != null)
            {
                string name = GetDeclarationName(exportStmt.Declaration);
                var type = GetDeclaredType(exportStmt.Declaration);
                _currentModule.ExportedTypes[name] = type;
            }
        }
        else if (exportStmt.NamedExports != null && exportStmt.FromModulePath == null)
        {
            // export { x, y } - verify each exported name exists
            foreach (var spec in exportStmt.NamedExports)
            {
                var type = LookupVariable(spec.LocalName);
                if (_currentModule != null)
                {
                    string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                    _currentModule.ExportedTypes[exportedName] = type;
                }
            }
        }
        else if (exportStmt.FromModulePath != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            // The actual binding happens during module resolution
            // Here we just need to validate the syntax is correct
        }
    }

    /// <summary>
    /// Handle function declarations including overloaded functions.
    /// </summary>
    private void CheckFunctionDeclaration(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;

        // Build the function type for this declaration
        TypeEnvironment funcEnv = new(_environment);

        // Handle generic type parameters
        List<TypeInfo.TypeParameter>? typeParams = null;
        if (funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0)
        {
            typeParams = [];
            foreach (var tp in funcStmt.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                typeParams.Add(typeParam);
                funcEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Parse parameter types and return type
        TypeEnvironment previousEnvForParsing = _environment;
        _environment = funcEnv;

        List<TypeInfo> paramTypes = [];
        int requiredParams = 0;
        bool seenDefault = false;
        TypeInfo returnType = funcStmt.ReturnType != null
            ? ToTypeInfo(funcStmt.ReturnType)
            : new TypeInfo.Void();

        // Parse explicit 'this' type if present
        TypeInfo? thisType = funcStmt.ThisType != null ? ToTypeInfo(funcStmt.ThisType) : null;

        foreach (var param in funcStmt.Parameters)
        {
            TypeInfo paramType = param.Type != null ? ToTypeInfo(param.Type) : new TypeInfo.Any();
            paramTypes.Add(paramType);

            if (param.IsRest) continue;

            // A parameter is optional if it has a default value OR is marked with ?
            bool isOptional = param.DefaultValue != null || param.IsOptional;

            if (param.DefaultValue != null)
            {
                seenDefault = true;
                TypeInfo defaultType = CheckExpr(param.DefaultValue);
                if (!IsCompatible(paramType, defaultType))
                {
                    throw new Exception($"Type Error: Default value type '{defaultType}' is not assignable to parameter type '{paramType}'.");
                }
            }
            else if (param.IsOptional)
            {
                seenDefault = true; // Optional parameters are like having a default
            }
            else
            {
                if (seenDefault)
                {
                    throw new Exception($"Type Error: Required parameter cannot follow optional parameter.");
                }
                requiredParams++;
            }
        }

        _environment = previousEnvForParsing;

        bool hasRest = funcStmt.Parameters.Any(p => p.IsRest);
        var thisFuncType = new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType);

        // Check if this is an overload signature (no body)
        if (funcStmt.Body == null)
        {
            // This is an overload signature - save for later
            if (!_pendingOverloadSignatures.TryGetValue(funcName, out var signatures))
            {
                signatures = [];
                _pendingOverloadSignatures[funcName] = signatures;
            }
            signatures.Add(thisFuncType);
            return;
        }

        // This is an implementation (has a body)
        TypeInfo funcType;

        // Check if there are pending overload signatures for this function
        if (_pendingOverloadSignatures.TryGetValue(funcName, out var pendingSignatures))
        {
            // Validate implementation is compatible with all signatures
            foreach (var sig in pendingSignatures)
            {
                if (thisFuncType.MinArity > sig.MinArity)
                {
                    throw new Exception($"Type Error: Implementation of '{funcName}' requires {thisFuncType.MinArity} arguments but overload signature requires only {sig.MinArity}.");
                }
            }

            // Create overloaded function type
            funcType = new TypeInfo.OverloadedFunction(pendingSignatures, thisFuncType);

            // Clear pending signatures
            _pendingOverloadSignatures.Remove(funcName);
        }
        else if (typeParams != null && typeParams.Count > 0)
        {
            // Generic function (no overloads)
            funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType);
        }
        else
        {
            // Regular function (no overloads)
            funcType = thisFuncType;
        }

        _environment.Define(funcName, funcType);

        // Add parameters to function environment and check body
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            funcEnv.Define(funcStmt.Parameters[i].Name.Lexeme, paramTypes[i]);
        }

        // Save and set context - function bodies are isolated from outer loop/switch/label context
        TypeEnvironment previousEnv = _environment;
        TypeInfo? previousReturn = _currentFunctionReturnType;
        TypeInfo? previousThisType = _currentFunctionThisType;
        bool previousInAsync = _inAsyncFunction;
        int previousLoopDepth = _loopDepth;
        int previousSwitchDepth = _switchDepth;
        var previousActiveLabels = new Dictionary<string, bool>(_activeLabels);

        _environment = funcEnv;
        _currentFunctionReturnType = returnType;
        _currentFunctionThisType = thisType;
        _inAsyncFunction = funcStmt.IsAsync;
        _loopDepth = 0;
        _switchDepth = 0;
        _activeLabels.Clear();

        try
        {
            foreach (var bodyStmt in funcStmt.Body)
            {
                CheckStmt(bodyStmt);
            }
        }
        finally
        {
            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
            _currentFunctionThisType = previousThisType;
            _inAsyncFunction = previousInAsync;
            _loopDepth = previousLoopDepth;
            _switchDepth = previousSwitchDepth;
            _activeLabels.Clear();
            foreach (var kvp in previousActiveLabels)
                _activeLabels[kvp.Key] = kvp.Value;
        }
    }

    private void CheckBlock(List<Stmt> statements, TypeEnvironment environment)
    {
        TypeEnvironment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                CheckStmt(statement);
            }
        }
        finally
        {
            _environment = previous;
        }
    }

    private void CheckSwitch(Stmt.Switch switchStmt)
    {
        CheckExpr(switchStmt.Subject);

        _switchDepth++;
        try
        {
            foreach (var caseItem in switchStmt.Cases)
            {
                CheckExpr(caseItem.Value);
                foreach (var stmt in caseItem.Body)
                {
                    CheckStmt(stmt);
                }
            }

            if (switchStmt.DefaultBody != null)
            {
                foreach (var stmt in switchStmt.DefaultBody)
                {
                    CheckStmt(stmt);
                }
            }
        }
        finally
        {
            _switchDepth--;
        }
    }

    private void CheckTryCatch(Stmt.TryCatch tryCatch)
    {
        // Check try block
        foreach (var stmt in tryCatch.TryBlock)
        {
            CheckStmt(stmt);
        }

        // Check catch block with its parameter in scope
        if (tryCatch.CatchBlock != null && tryCatch.CatchParam != null)
        {
            TypeEnvironment catchEnv = new(_environment);
            catchEnv.Define(tryCatch.CatchParam.Lexeme, new TypeInfo.Any());

            TypeEnvironment prevEnv = _environment;
            _environment = catchEnv;
            try
            {
                foreach (var stmt in tryCatch.CatchBlock)
                {
                    CheckStmt(stmt);
                }
            }
            finally
            {
                _environment = prevEnv;
            }
        }

        // Check finally block
        if (tryCatch.FinallyBlock != null)
        {
            foreach (var stmt in tryCatch.FinallyBlock)
            {
                CheckStmt(stmt);
            }
        }
    }

    private void CheckEnumDeclaration(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;
        bool autoIncrementActive = true;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value != null)
            {
                // For literals, do normal type checking
                // For const enum computed expressions, skip CheckExpr (enum not yet defined)
                if (member.Value is Expr.Literal lit)
                {
                    if (lit.Value is double d)
                    {
                        // Numeric literal - enable auto-increment from this value
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (lit.Value is string s)
                    {
                        // String literal - disable auto-increment
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must be a string or number literal.");
                    }
                }
                else if (enumStmt.IsConst)
                {
                    // Const enums support computed values (e.g., B = A * 2)
                    var computedValue = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                    if (computedValue is double d)
                    {
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (computedValue is string s)
                    {
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new Exception($"Type Error: Const enum member '{member.Name.Lexeme}' must evaluate to a string or number.");
                    }
                }
                else
                {
                    throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must be a literal value.");
                }
            }
            else
            {
                // No initializer - use auto-increment if active
                if (!autoIncrementActive)
                {
                    throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must have an initializer " +
                                        "(string enum members cannot use auto-increment).");
                }

                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        // Determine enum kind
        EnumKind kind = (hasNumeric, hasString) switch
        {
            (true, false) => EnumKind.Numeric,
            (false, true) => EnumKind.String,
            (true, true) => EnumKind.Heterogeneous,
            _ => EnumKind.Numeric  // Empty enum defaults to numeric
        };

        _environment.Define(enumStmt.Name.Lexeme, new TypeInfo.Enum(enumStmt.Name.Lexeme, members, kind, enumStmt.IsConst));
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members.
    /// Supports literals, references to other enum members, and arithmetic operations.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new Exception($"Type Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Type Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Type Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
        };
    }

    private object EvaluateConstEnumUnary(Expr.Unary unary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var operand = EvaluateConstEnumExpression(unary.Right, resolvedMembers, enumName);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw new Exception($"Type Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
        };
    }

    private object EvaluateConstEnumBinary(Expr.Binary binary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var left = EvaluateConstEnumExpression(binary.Left, resolvedMembers, enumName);
        var right = EvaluateConstEnumExpression(binary.Right, resolvedMembers, enumName);

        if (left is double l && right is double r)
        {
            return binary.Operator.Type switch
            {
                TokenType.PLUS => l + r,
                TokenType.MINUS => l - r,
                TokenType.STAR => l * r,
                TokenType.SLASH => l / r,
                TokenType.PERCENT => l % r,
                TokenType.STAR_STAR => Math.Pow(l, r),
                TokenType.AMPERSAND => (double)((int)l & (int)r),
                TokenType.PIPE => (double)((int)l | (int)r),
                TokenType.CARET => (double)((int)l ^ (int)r),
                TokenType.LESS_LESS => (double)((int)l << (int)r),
                TokenType.GREATER_GREATER => (double)((int)l >> (int)r),
                _ => throw new Exception($"Type Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Type Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }
}
