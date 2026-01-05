using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.TypeSystem;

/// <summary>
/// Static type analyzer that validates the AST before execution.
/// </summary>
/// <remarks>
/// Third stage of the compiler pipeline. Traverses the AST from <see cref="Parser"/> and
/// validates type compatibility, function signatures, class inheritance, and interface
/// implementations. Uses <see cref="TypeEnvironment"/> for scope tracking and <see cref="TypeInfo"/>
/// records for type representations. Supports both structural typing (interfaces) and nominal
/// typing (classes). Type checking runs at compile-time, completely separate from runtime
/// execution. Errors throw exceptions with "Type Error:" prefix.
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
/// <seealso cref="TypeInfo"/>
/// <seealso cref="Interpreter"/>
public class TypeChecker
{
    private TypeEnvironment _environment = new();
    private TypeMap _typeMap = new();

    // We need to track the current function's expected return type to validate 'return' statements
    private TypeInfo? _currentFunctionReturnType = null;
    private TypeInfo.Class? _currentClass = null;
    private bool _inStaticMethod = false;
    private int _loopDepth = 0;
    private int _switchDepth = 0;

    // Track active labels for labeled statements (label name -> isOnLoop)
    private readonly Dictionary<string, bool> _activeLabels = [];

    // Track pending overload signatures for top-level functions
    private readonly Dictionary<string, List<TypeInfo.Function>> _pendingOverloadSignatures = [];

    /// <summary>
    /// Type-checks the given statements and returns a TypeMap with resolved types for all expressions.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeMap containing the resolved type for each expression.</returns>
    public TypeMap Check(List<Stmt> statements)
    {
        // Pre-define built-ins
        _environment.Define("console", new TypeInfo.Any());

        foreach (Stmt statement in statements)
        {
            CheckStmt(statement);
        }

        return _typeMap;
    }

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

                        TypeEnvironment previousEnvFunc = _environment;
                        TypeInfo? previousReturnFunc = _currentFunctionReturnType;
                        bool previousInStatic = _inStaticMethod;

                        _environment = methodEnv;
                        _currentFunctionReturnType = methodType.ReturnType;
                        _inStaticMethod = method.IsStatic;

                        try
                        {
                            foreach (var bodyStmt in method.Body)
                            {
                                CheckStmt(bodyStmt);
                            }
                        }
                        finally
                        {
                            _environment = previousEnvFunc;
                            _currentFunctionReturnType = previousReturnFunc;
                            _inStaticMethod = previousInStatic;
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

                            TypeEnvironment previousEnvAcc = _environment;
                            TypeInfo? previousReturnAcc = _currentFunctionReturnType;

                            _environment = accessorEnv;
                            _currentFunctionReturnType = accessorReturnType;

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

                        if (!IsCompatible(_currentFunctionReturnType, actualReturnType))
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
        var thisFuncType = new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest);

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
            funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest);
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

        TypeEnvironment previousEnv = _environment;
        TypeInfo? previousReturn = _currentFunctionReturnType;

        _environment = funcEnv;
        _currentFunctionReturnType = returnType;

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

    private TypeInfo CheckExpr(Expr expr)
    {
        TypeInfo result = expr switch
        {
            Expr.Literal literal => GetLiteralType(literal.Value),
            Expr.Variable variable => LookupVariable(variable.Name),
            Expr.Assign assign => CheckAssign(assign),
            Expr.Binary binary => CheckBinary(binary),
            Expr.Logical logical => CheckLogical(logical),
            Expr.NullishCoalescing nc => CheckNullishCoalescing(nc),
            Expr.Ternary ternary => CheckTernary(ternary),
            Expr.Call call => CheckCall(call),
            Expr.Grouping grouping => CheckExpr(grouping.Expression),
            Expr.Unary unary => CheckUnary(unary),
            Expr.Get get => CheckGet(get),
            Expr.Set set => CheckSet(set),
            Expr.This thisExpr => CheckThis(thisExpr),
            Expr.New newExpr => CheckNew(newExpr),
            Expr.ArrayLiteral array => CheckArray(array),
            Expr.ObjectLiteral obj => CheckObject(obj),
            Expr.GetIndex getIndex => CheckGetIndex(getIndex),
            Expr.SetIndex setIndex => CheckSetIndex(setIndex),
            Expr.Super super => CheckSuper(super),
            Expr.CompoundAssign compound => CheckCompoundAssign(compound),
            Expr.CompoundSet compoundSet => CheckCompoundSet(compoundSet),
            Expr.CompoundSetIndex compoundSetIndex => CheckCompoundSetIndex(compoundSetIndex),
            Expr.PrefixIncrement prefix => CheckPrefixIncrement(prefix),
            Expr.PostfixIncrement postfix => CheckPostfixIncrement(postfix),
            Expr.ArrowFunction arrow => CheckArrowFunction(arrow),
            Expr.TemplateLiteral template => CheckTemplateLiteral(template),
            Expr.Spread spread => CheckSpread(spread),
            Expr.TypeAssertion ta => CheckTypeAssertion(ta),
            _ => new TypeInfo.Any()
        };

        // Store the resolved type in the TypeMap for use by ILCompiler/Interpreter
        _typeMap.Set(expr, result);

        return result;
    }

    private TypeInfo CheckTypeAssertion(Expr.TypeAssertion ta)
    {
        TypeInfo sourceType = CheckExpr(ta.Expression);
        TypeInfo targetType = ToTypeInfo(ta.TargetType);

        // Allow any <-> anything (escape hatch)
        if (sourceType is TypeInfo.Any || targetType is TypeInfo.Any)
            return targetType;

        // Check if types are related (either direction)
        if (IsCompatible(targetType, sourceType) || IsCompatible(sourceType, targetType))
            return targetType;

        throw new Exception($"Type Error: Cannot assert type '{sourceType}' to '{targetType}'.");
    }

    private TypeInfo CheckTemplateLiteral(Expr.TemplateLiteral template)
    {
        // Type check all interpolated expressions (any type is allowed)
        foreach (var expr in template.Expressions)
        {
            CheckExpr(expr);
        }
        // Template literals always result in string
        return new TypeInfo.Primitive(TokenType.TYPE_STRING);
    }

    private TypeInfo CheckSuper(Expr.Super expr)
    {
        if (_currentClass == null)
        {
            throw new Exception("Type Error: Cannot use 'super' outside of a class.");
        }
        if (_currentClass.Superclass == null)
        {
            throw new Exception($"Type Error: Class '{_currentClass.Name}' does not have a superclass.");
        }

        // super() constructor call - Method is null
        if (expr.Method == null)
        {
            if (_currentClass.Superclass.Methods.TryGetValue("constructor", out var ctorType))
            {
                return ctorType;
            }
            // Default constructor with no parameters
            return new TypeInfo.Function([], new TypeInfo.Void());
        }

        if (_currentClass.Superclass.Methods.TryGetValue(expr.Method.Lexeme, out var methodType))
        {
            return methodType;
        }

        throw new Exception($"Type Error: Property '{expr.Method.Lexeme}' does not exist on superclass '{_currentClass.Superclass.Name}'.");
    }

    private TypeInfo CheckObject(Expr.ObjectLiteral obj)
    {
        Dictionary<string, TypeInfo> fields = [];
        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                // Spread property - merge fields from the spread object
                TypeInfo spreadType = CheckExpr(prop.Value);
                if (spreadType is TypeInfo.Record record)
                {
                    foreach (var kv in record.Fields)
                    {
                        fields[kv.Key] = kv.Value;
                    }
                }
                else if (spreadType is TypeInfo.Instance inst)
                {
                    // Instance fields are dynamic, just accept
                }
                else if (spreadType is TypeInfo.Any)
                {
                    // Any is fine
                }
                else
                {
                    throw new Exception($"Type Error: Spread in object literal requires an object, got '{spreadType}'.");
                }
            }
            else
            {
                fields[prop.Name!.Lexeme] = CheckExpr(prop.Value);
            }
        }
        return new TypeInfo.Record(fields);
    }

    private TypeInfo CheckArray(Expr.ArrayLiteral array)
    {
        if (array.Elements.Count == 0) return new TypeInfo.Array(new TypeInfo.Any()); // Empty array is any[]? or generic?

        List<TypeInfo> elementTypes = [];
        foreach (var element in array.Elements)
        {
            TypeInfo elemType;
            if (element is Expr.Spread spread)
            {
                // Spread element - get element type from array or tuple
                TypeInfo spreadType = CheckExpr(spread.Expression);
                if (spreadType is TypeInfo.Array arrType)
                {
                    elemType = arrType.ElementType;
                }
                else if (spreadType is TypeInfo.Tuple tupType)
                {
                    // Spread tuple - add all its element types
                    elementTypes.AddRange(tupType.ElementTypes);
                    if (tupType.RestElementType != null)
                        elementTypes.Add(tupType.RestElementType);
                    continue; // Don't add elemType again since we added multiple
                }
                else if (spreadType is TypeInfo.Any)
                {
                    elemType = new TypeInfo.Any();
                }
                else
                {
                    throw new Exception($"Type Error: Spread expression must be an array or tuple, got '{spreadType}'.");
                }
            }
            else
            {
                elemType = CheckExpr(element);
            }
            elementTypes.Add(elemType);
        }

        // Find common type or create union
        TypeInfo commonType = elementTypes[0];
        bool allCompatible = true;
        for (int i = 1; i < elementTypes.Count; i++)
        {
            if (!IsCompatible(commonType, elementTypes[i]) && !IsCompatible(elementTypes[i], commonType))
            {
                allCompatible = false;
                break;
            }
        }

        if (!allCompatible)
        {
            // Create union of all unique element types
            var uniqueTypes = elementTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            commonType = uniqueTypes.Count == 1 ? uniqueTypes[0] : new TypeInfo.Union(uniqueTypes);
        }

        return new TypeInfo.Array(commonType);
    }

    private TypeInfo CheckSpread(Expr.Spread spread)
    {
        // Spread just passes through to the underlying expression type
        // The actual spread logic is handled by the caller (array literal, call, etc.)
        return CheckExpr(spread.Expression);
    }

    private void CheckArrayLiteralAgainstTuple(Expr.ArrayLiteral arrayLit, TypeInfo.Tuple tupleType, string varName)
    {
        int elemCount = arrayLit.Elements.Count;
        int requiredCount = tupleType.RequiredCount;
        int maxCount = tupleType.MaxLength ?? int.MaxValue;

        // Check element count
        if (elemCount < requiredCount)
        {
            throw new Exception($"Type Error: Tuple requires at least {requiredCount} elements, but got {elemCount} for variable '{varName}'.");
        }
        if (tupleType.RestElementType == null && elemCount > tupleType.ElementTypes.Count)
        {
            throw new Exception($"Type Error: Tuple expects at most {tupleType.ElementTypes.Count} elements, but got {elemCount} for variable '{varName}'.");
        }

        // Check each element type
        for (int i = 0; i < elemCount; i++)
        {
            var element = arrayLit.Elements[i];
            TypeInfo expectedType;

            if (i < tupleType.ElementTypes.Count)
            {
                expectedType = tupleType.ElementTypes[i];
            }
            else if (tupleType.RestElementType != null)
            {
                expectedType = tupleType.RestElementType;
            }
            else
            {
                throw new Exception($"Type Error: Tuple index {i} is out of bounds for variable '{varName}'.");
            }

            // Recursively apply contextual typing for nested array literals with tuple types
            if (expectedType is TypeInfo.Tuple nestedTuple && element is Expr.ArrayLiteral nestedArrayLit)
            {
                CheckArrayLiteralAgainstTuple(nestedArrayLit, nestedTuple, $"{varName}[{i}]");
            }
            else
            {
                TypeInfo elemType = CheckExpr(element);
                if (!IsCompatible(expectedType, elemType))
                {
                    throw new Exception($"Type Error: Element at index {i} has type '{elemType}' but expected '{expectedType}' for variable '{varName}'.");
                }
            }
        }
    }

    private TypeInfo CheckGetIndex(Expr.GetIndex getIndex)
    {
        TypeInfo objType = CheckExpr(getIndex.Object);
        TypeInfo indexType = CheckExpr(getIndex.Index);

        // Allow indexing on 'any' type (returns 'any')
        if (objType is TypeInfo.Any)
        {
            return new TypeInfo.Any();
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // String literal index - look up specific property
            if (getIndex.Index is Expr.Literal { Value: string propName })
            {
                if (objType is TypeInfo.Record rec && rec.Fields.TryGetValue(propName, out var fieldType))
                    return fieldType;
                if (objType is TypeInfo.Interface itf && itf.Members.TryGetValue(propName, out var memberType))
                    return memberType;
            }

            // Dynamic string index - use index signature if available
            if (objType is TypeInfo.Record rec2 && rec2.StringIndexType != null)
                return rec2.StringIndexType;
            if (objType is TypeInfo.Interface itf2 && itf2.StringIndexType != null)
                return itf2.StringIndexType;

            // Allow bracket access on any object/interface (returns any for unknown keys)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple indexing with position-based types
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> exact element type
                if (getIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                        return tupleType.ElementTypes[i];
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                        return tupleType.RestElementType;
                    if (i < 0 || (tupleType.MaxLength != null && i >= tupleType.MaxLength))
                        throw new Exception($"Type Error: Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> union of all possible types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
            }

            if (objType is TypeInfo.Array arrayType)
            {
                return arrayType.ElementType;
            }

            // Enum reverse mapping: Direction[0] returns "Up" (only for numeric enums)
            if (objType is TypeInfo.Enum enumType)
            {
                // Const enums cannot use reverse mapping
                if (enumType.IsConst)
                {
                    throw new Exception($"Type Error: A const enum member can only be accessed using its name, not by index. Cannot use reverse mapping on const enum '{enumType.Name}'.");
                }
                if (enumType.Kind == EnumKind.String)
                {
                    throw new Exception($"Type Error: Reverse mapping is not supported for string enum '{enumType.Name}'.");
                }
                return new TypeInfo.Primitive(TokenType.TYPE_STRING);
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf3 && itf3.NumberIndexType != null)
                return itf3.NumberIndexType;
            if (objType is TypeInfo.Record rec3 && rec3.NumberIndexType != null)
                return rec3.NumberIndexType;
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf4 && itf4.SymbolIndexType != null)
                return itf4.SymbolIndexType;
            if (objType is TypeInfo.Record rec4 && rec4.SymbolIndexType != null)
                return rec4.SymbolIndexType;

            // Allow symbol bracket access on any object (returns any)
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return new TypeInfo.Any();
        }

        throw new Exception($"Type Error: Index type '{indexType}' is not valid for indexing '{objType}'.");
    }

    private TypeInfo CheckSetIndex(Expr.SetIndex setIndex)
    {
        TypeInfo objType = CheckExpr(setIndex.Object);
        TypeInfo indexType = CheckExpr(setIndex.Index);
        TypeInfo valueType = CheckExpr(setIndex.Value);

        // Allow setting on 'any' type
        if (objType is TypeInfo.Any)
        {
            return valueType;
        }

        // Handle string index on objects/interfaces
        if (IsString(indexType) || indexType is TypeInfo.StringLiteral)
        {
            // Check if value is compatible with string index signature
            if (objType is TypeInfo.Interface itf && itf.StringIndexType != null)
            {
                if (!IsCompatible(itf.StringIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to index signature type '{itf.StringIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec && rec.StringIndexType != null)
            {
                if (!IsCompatible(rec.StringIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to index signature type '{rec.StringIndexType}'.");
                return valueType;
            }

            // Allow bracket assignment on any object/interface
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        // Handle number index
        if (IsNumber(indexType) || indexType is TypeInfo.NumberLiteral)
        {
            // Tuple index assignment
            if (objType is TypeInfo.Tuple tupleType)
            {
                // Literal index -> check against specific element type
                if (setIndex.Index is Expr.Literal { Value: double idx })
                {
                    int i = (int)idx;
                    if (i >= 0 && i < tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.ElementTypes[i], valueType))
                            throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple element of type '{tupleType.ElementTypes[i]}'.");
                        return valueType;
                    }
                    if (tupleType.RestElementType != null && i >= tupleType.ElementTypes.Count)
                    {
                        if (!IsCompatible(tupleType.RestElementType, valueType))
                            throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple rest element of type '{tupleType.RestElementType}'.");
                        return valueType;
                    }
                    throw new Exception($"Type Error: Tuple index {i} is out of bounds.");
                }
                // Dynamic index -> value must be compatible with all possible element types
                var allTypes = tupleType.ElementTypes.ToList();
                if (tupleType.RestElementType != null)
                    allTypes.Add(tupleType.RestElementType);
                if (!allTypes.All(t => IsCompatible(t, valueType)))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to tuple with mixed element types.");
                return valueType;
            }

            if (objType is TypeInfo.Array arrayType)
            {
                if (!IsCompatible(arrayType.ElementType, valueType))
                {
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to array of '{arrayType.ElementType}'.");
                }
                return valueType;
            }

            // Number index signature on interface/record
            if (objType is TypeInfo.Interface itf2 && itf2.NumberIndexType != null)
            {
                if (!IsCompatible(itf2.NumberIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to number index signature type '{itf2.NumberIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec2 && rec2.NumberIndexType != null)
            {
                if (!IsCompatible(rec2.NumberIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to number index signature type '{rec2.NumberIndexType}'.");
                return valueType;
            }
        }

        // Handle symbol index
        if (indexType is TypeInfo.Symbol)
        {
            if (objType is TypeInfo.Interface itf3 && itf3.SymbolIndexType != null)
            {
                if (!IsCompatible(itf3.SymbolIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to symbol index signature type '{itf3.SymbolIndexType}'.");
                return valueType;
            }
            if (objType is TypeInfo.Record rec3 && rec3.SymbolIndexType != null)
            {
                if (!IsCompatible(rec3.SymbolIndexType, valueType))
                    throw new Exception($"Type Error: Cannot assign '{valueType}' to symbol index signature type '{rec3.SymbolIndexType}'.");
                return valueType;
            }

            // Allow symbol bracket assignment on any object
            if (objType is TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance)
                return valueType;
        }

        throw new Exception($"Type Error: Index type '{indexType}' is not valid for assigning to '{objType}'.");
    }

    private TypeInfo CheckNew(Expr.New newExpr)
    {
        TypeInfo type = LookupVariable(newExpr.ClassName);

        // Check for abstract class instantiation
        if (type is TypeInfo.GenericClass gc && gc.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{newExpr.ClassName.Lexeme}'.");
        }
        if (type is TypeInfo.Class c && c.IsAbstract)
        {
            throw new Exception($"Type Error: Cannot create an instance of abstract class '{newExpr.ClassName.Lexeme}'.");
        }

        // Handle generic class instantiation
        if (type is TypeInfo.GenericClass genericClass)
        {
            if (newExpr.TypeArgs == null || newExpr.TypeArgs.Count == 0)
            {
                throw new Exception($"Type Error: Generic class '{newExpr.ClassName.Lexeme}' requires type arguments.");
            }

            var typeArgs = newExpr.TypeArgs.Select(ToTypeInfo).ToList();
            var instantiated = InstantiateGenericClass(genericClass, typeArgs);

            // Build substitution map for constructor parameter types
            var subs = new Dictionary<string, TypeInfo>();
            for (int i = 0; i < genericClass.TypeParams.Count; i++)
                subs[genericClass.TypeParams[i].Name] = typeArgs[i];

            // Check constructor with substituted parameter types
            if (genericClass.Methods.TryGetValue("constructor", out var ctorTypeInfo))
            {
                // Handle both Function and OverloadedFunction for constructor
                if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
                {
                    // Resolve overloaded constructor call
                    List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
                    bool matched = false;
                    foreach (var sig in overloadedCtor.Signatures)
                    {
                        var substitutedParamTypes = sig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                        if (TryMatchConstructorArgs(argTypes, substitutedParamTypes, sig.MinArity, sig.HasRestParam))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        throw new Exception($"Type Error: No constructor overload matches the call for '{newExpr.ClassName.Lexeme}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    var substitutedParamTypes = ctorType.ParamTypes.Select(p => Substitute(p, subs)).ToList();

                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(substitutedParamTypes[i], argType))
                        {
                            throw new Exception($"Type Error: Constructor argument {i + 1} expected type '{substitutedParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected 0 arguments but got {newExpr.Arguments.Count}.");
            }

            return new TypeInfo.Instance(instantiated);
        }

        if (type is TypeInfo.Class classType)
        {
            if (classType.Methods.TryGetValue("constructor", out var ctorTypeInfo))
            {
                // Handle both Function and OverloadedFunction for constructor
                if (ctorTypeInfo is TypeInfo.OverloadedFunction overloadedCtor)
                {
                    // Resolve overloaded constructor call
                    List<TypeInfo> argTypes = newExpr.Arguments.Select(CheckExpr).ToList();
                    bool matched = false;
                    foreach (var sig in overloadedCtor.Signatures)
                    {
                        if (TryMatchConstructorArgs(argTypes, sig.ParamTypes, sig.MinArity, sig.HasRestParam))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        throw new Exception($"Type Error: No constructor overload matches the call for '{newExpr.ClassName.Lexeme}'.");
                    }
                }
                else if (ctorTypeInfo is TypeInfo.Function ctorType)
                {
                    // Use MinArity to allow optional parameters
                    if (newExpr.Arguments.Count < ctorType.MinArity)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at least {ctorType.MinArity} arguments but got {newExpr.Arguments.Count}.");
                    }
                    if (newExpr.Arguments.Count > ctorType.ParamTypes.Count)
                    {
                        throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected at most {ctorType.ParamTypes.Count} arguments but got {newExpr.Arguments.Count}.");
                    }

                    for (int i = 0; i < newExpr.Arguments.Count; i++)
                    {
                        TypeInfo argType = CheckExpr(newExpr.Arguments[i]);
                        if (!IsCompatible(ctorType.ParamTypes[i], argType))
                        {
                            throw new Exception($"Type Error: Constructor argument {i + 1} expected type '{ctorType.ParamTypes[i]}' but got '{argType}'.");
                        }
                    }
                }
            }
            else if (newExpr.Arguments.Count > 0)
            {
                throw new Exception($"Type Error: Constructor for '{newExpr.ClassName.Lexeme}' expected 0 arguments but got {newExpr.Arguments.Count}.");
            }

            return new TypeInfo.Instance(classType);
        }
        throw new Exception($"Type Error: '{newExpr.ClassName.Lexeme}' is not a class.");
    }

    private TypeInfo CheckThis(Expr.This expr)
    {
        if (_currentClass == null)
        {
            throw new Exception("Type Error: Cannot use 'this' outside of a class.");
        }
        if (_inStaticMethod)
        {
            throw new Exception("Type Error: Cannot use 'this' in a static method.");
        }
        return new TypeInfo.Instance(_currentClass);
    }

    private TypeInfo CheckGet(Expr.Get get)
    {
        TypeInfo objType = CheckExpr(get.Object);

        // Handle static member access on class type
        if (objType is TypeInfo.Class classType)
        {
            // Check static methods
            TypeInfo.Class? current = classType;
            while (current != null)
            {
                if (current.StaticMethods.TryGetValue(get.Name.Lexeme, out var staticMethodType))
                {
                    return staticMethodType;
                }
                if (current.StaticProperties.TryGetValue(get.Name.Lexeme, out var staticPropType))
                {
                    return staticPropType;
                }
                current = current.Superclass;
            }
            return new TypeInfo.Any();
        }

        // Handle enum member access (e.g., Direction.Up or Status.Success)
        if (objType is TypeInfo.Enum enumTypeInfo)
        {
            if (enumTypeInfo.Members.TryGetValue(get.Name.Lexeme, out var memberValue))
            {
                // Return type based on the actual member value type
                return memberValue switch
                {
                    double => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                    string => new TypeInfo.Primitive(TokenType.TYPE_STRING),
                    _ => throw new Exception($"Type Error: Unexpected enum member type for '{get.Name.Lexeme}'.")
                };
            }
            throw new Exception($"Type Error: '{get.Name.Lexeme}' does not exist on enum '{enumTypeInfo.Name}'.");
        }

        if (objType is TypeInfo.Instance instance)
        {
            string memberName = get.Name.Lexeme;

            // Handle instantiated generic class (e.g., Box<number>)
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                // Build substitution map from type parameters to type arguments
                var subs = new Dictionary<string, TypeInfo>();
                for (int i = 0; i < gc.TypeParams.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                // Check for getter first
                if (gc.Getters?.TryGetValue(memberName, out var getterType) == true)
                {
                    return Substitute(getterType, subs);
                }

                // Check for field
                if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                {
                    return Substitute(fieldType, subs);
                }

                // Check for method
                if (gc.Methods.TryGetValue(memberName, out var methodType))
                {
                    // Substitute type parameters in method signature
                    if (methodType is TypeInfo.Function funcType)
                    {
                        var substitutedParams = funcType.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                        var substitutedReturn = Substitute(funcType.ReturnType, subs);
                        return new TypeInfo.Function(substitutedParams, substitutedReturn, funcType.RequiredParams, funcType.HasRestParam);
                    }
                    else if (methodType is TypeInfo.OverloadedFunction overloadedFunc)
                    {
                        // Substitute type parameters in all overload signatures
                        var substitutedSignatures = overloadedFunc.Signatures.Select(sig =>
                        {
                            var substitutedParams = sig.ParamTypes.Select(p => Substitute(p, subs)).ToList();
                            var substitutedReturn = Substitute(sig.ReturnType, subs);
                            return new TypeInfo.Function(substitutedParams, substitutedReturn, sig.RequiredParams, sig.HasRestParam);
                        }).ToList();
                        var substitutedImpl = new TypeInfo.Function(
                            overloadedFunc.Implementation.ParamTypes.Select(p => Substitute(p, subs)).ToList(),
                            Substitute(overloadedFunc.Implementation.ReturnType, subs),
                            overloadedFunc.Implementation.RequiredParams,
                            overloadedFunc.Implementation.HasRestParam);
                        return new TypeInfo.OverloadedFunction(substitutedSignatures, substitutedImpl);
                    }
                    return methodType; // Fallback - shouldn't happen
                }

                // Check superclass if any
                if (gc.Superclass != null)
                {
                    TypeInfo.Class? current = gc.Superclass;
                    while (current != null)
                    {
                        if (current.Methods.TryGetValue(memberName, out var superMethod))
                            return superMethod;
                        if (current.FieldTypes?.TryGetValue(memberName, out var superField) == true)
                            return superField;
                        current = current.Superclass;
                    }
                }

                return new TypeInfo.Any();
            }

            // Handle regular class instance
            if (instance.ClassType is TypeInfo.Class instanceClassType)
            {
                TypeInfo.Class? current = instanceClassType;
                while (current != null)
                {
                    // Check for getter first
                    if (current.GetterTypes.TryGetValue(memberName, out var getterType))
                    {
                        return getterType;
                    }

                    // Check access modifier
                    AccessModifier access = AccessModifier.Public;
                    if (current.MethodAccessModifiers.TryGetValue(memberName, out var ma))
                        access = ma;
                    else if (current.FieldAccessModifiers.TryGetValue(memberName, out var fa))
                        access = fa;

                    if (access == AccessModifier.Private && _currentClass?.Name != current.Name)
                    {
                        throw new Exception($"Type Error: Property '{memberName}' is private and only accessible within class '{current.Name}'.");
                    }
                    if (access == AccessModifier.Protected && !IsSubclassOf(_currentClass, current))
                    {
                        throw new Exception($"Type Error: Property '{memberName}' is protected and only accessible within class '{current.Name}' and its subclasses.");
                    }

                    if (current.Methods.TryGetValue(memberName, out var methodType))
                    {
                        return methodType;
                    }

                    // Check for field
                    if (current.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                    {
                        return fieldType;
                    }

                    current = current.Superclass;
                }
                return new TypeInfo.Any();
            }

            return new TypeInfo.Any();
        }
        if (objType is TypeInfo.Record record)
        {
            if (record.Fields.TryGetValue(get.Name.Lexeme, out var fieldType))
            {
                return fieldType;
            }
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type '{record}'.");
        }
        // Handle string methods
        if (objType is TypeInfo.Primitive p && p.Type == TokenType.TYPE_STRING)
        {
            var memberType = BuiltInTypes.GetStringMemberType(get.Name.Lexeme);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'string'.");
        }
        // Handle array methods
        if (objType is TypeInfo.Array arrayType)
        {
            var memberType = BuiltInTypes.GetArrayMemberType(get.Name.Lexeme, arrayType.ElementType);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on type 'array'.");
        }
        // Handle tuple methods (tuples support array methods)
        if (objType is TypeInfo.Tuple tupleType)
        {
            // Create union of all element types for method type resolution
            var allTypes = tupleType.ElementTypes.ToList();
            if (tupleType.RestElementType != null)
                allTypes.Add(tupleType.RestElementType);
            var unique = allTypes.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            TypeInfo unionElem = unique.Count == 0
                ? new TypeInfo.Any()
                : (unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique));
            var memberType = BuiltInTypes.GetArrayMemberType(get.Name.Lexeme, unionElem);
            if (memberType != null) return memberType;
            throw new Exception($"Type Error: Property '{get.Name.Lexeme}' does not exist on tuple type.");
        }
        return new TypeInfo.Any();
    }

    private TypeInfo CheckSet(Expr.Set set)
    {
        TypeInfo objType = CheckExpr(set.Object);

        // Handle static property assignment
        if (objType is TypeInfo.Class classType)
        {
            TypeInfo.Class? current = classType;
            while (current != null)
            {
                if (current.StaticProperties.TryGetValue(set.Name.Lexeme, out var staticPropType))
                {
                    TypeInfo valueType = CheckExpr(set.Value);
                    if (!IsCompatible(staticPropType, valueType))
                    {
                        throw new Exception($"Type Error: Cannot assign '{valueType}' to static property '{set.Name.Lexeme}' of type '{staticPropType}'.");
                    }
                    return valueType;
                }
                current = current.Superclass;
            }
            return CheckExpr(set.Value);
        }

        if (objType is TypeInfo.Instance instance)
        {
             string memberName = set.Name.Lexeme;

             // Handle InstantiatedGeneric
             if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                 ig.GenericDefinition is TypeInfo.GenericClass gc)
             {
                 // Build substitution map
                 var subs = new Dictionary<string, TypeInfo>();
                 for (int i = 0; i < gc.TypeParams.Count; i++)
                     subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                 // Check for setter
                 if (gc.Setters?.TryGetValue(memberName, out var setterType) == true)
                 {
                     var substitutedType = Substitute(setterType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{memberName}' expecting '{substitutedType}'.");
                     }
                     return valueType;
                 }

                 // Check for field
                 if (gc.FieldTypes?.TryGetValue(memberName, out var fieldType) == true)
                 {
                     var substitutedType = Substitute(fieldType, subs);
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(substitutedType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to field '{memberName}' of type '{substitutedType}'.");
                     }
                     return valueType;
                 }

                 return CheckExpr(set.Value);
             }

             // Handle regular Class
             if (instance.ClassType is not TypeInfo.Class startClass)
                 return CheckExpr(set.Value);

             TypeInfo.Class? current = startClass;

             // Check for setter first
             while (current != null)
             {
                 if (current.SetterTypes.TryGetValue(memberName, out var setterType))
                 {
                     TypeInfo valueType = CheckExpr(set.Value);
                     if (!IsCompatible(setterType, valueType))
                     {
                         throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{memberName}' expecting '{setterType}'.");
                     }
                     return valueType;
                 }

                 // Check if there's a getter but no setter (read-only property)
                 if (current.GetterTypes.ContainsKey(memberName) && !current.SetterTypes.ContainsKey(memberName))
                 {
                     throw new Exception($"Type Error: Cannot assign to '{memberName}' because it is a read-only property (has getter but no setter).");
                 }

                 current = current.Superclass;
             }

             // Reset to check access and readonly
             current = startClass;

             // Check access and readonly
             while (current != null)
             {
                 // Check access modifier
                 AccessModifier access = AccessModifier.Public;
                 if (current.FieldAccessModifiers.TryGetValue(memberName, out var fa))
                     access = fa;

                 if (access == AccessModifier.Private && _currentClass?.Name != current.Name)
                 {
                     throw new Exception($"Type Error: Property '{memberName}' is private and only accessible within class '{current.Name}'.");
                 }
                 if (access == AccessModifier.Protected && !IsSubclassOf(_currentClass, current))
                 {
                     throw new Exception($"Type Error: Property '{memberName}' is protected and only accessible within class '{current.Name}' and its subclasses.");
                 }

                 // Check readonly - only allow assignment in constructor
                 if (current.ReadonlyFieldSet.Contains(memberName))
                 {
                     // Allow in constructor
                     bool inConstructor = _currentClass?.Name == current.Name &&
                         _environment.IsDefined("this");
                     // Simplified check - just allow if we're in the same class
                     if (_currentClass?.Name != current.Name)
                     {
                         throw new Exception($"Type Error: Cannot assign to '{memberName}' because it is a read-only property.");
                     }
                 }

                 current = current.Superclass;
             }

             return CheckExpr(set.Value);
        }
        else if (objType is TypeInfo.Record record)
        {
             if (record.Fields.TryGetValue(set.Name.Lexeme, out var fieldType))
             {
                 TypeInfo valueType = CheckExpr(set.Value);
                 if (!IsCompatible(fieldType, valueType))
                 {
                     throw new Exception($"Type Error: Cannot assign '{valueType}' to property '{set.Name.Lexeme}' of type '{fieldType}'.");
                 }
                 return valueType;
             }
             // For now, disallow adding new properties to records via assignment to mimic strictness
             throw new Exception($"Type Error: Property '{set.Name.Lexeme}' does not exist on type '{record}'.");
        }
        throw new Exception("Type Error: Only instances and objects have properties.");
    }

    private TypeInfo CheckCall(Expr.Call call)
    {
        if (call.Callee is Expr.Variable v && v.Name.Lexeme == "console.log")
        {
             foreach(var arg in call.Arguments) CheckExpr(arg);
             return new TypeInfo.Void();
        }

        // Handle Symbol() constructor - creates unique symbols
        if (call.Callee is Expr.Variable symVar && symVar.Name.Lexeme == "Symbol")
        {
            if (call.Arguments.Count > 1)
            {
                throw new Exception("Type Error: Symbol() accepts at most one argument (description).");
            }
            if (call.Arguments.Count == 1)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (!IsString(argType) && argType is not TypeInfo.Any)
                {
                    throw new Exception($"Type Error: Symbol() description must be a string, got '{argType}'.");
                }
            }
            return new TypeInfo.Symbol();
        }

        // Handle Object.keys(), Object.values(), Object.entries()
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable objVar &&
            objVar.Name.Lexeme == "Object")
        {
            var methodType = BuiltInTypes.GetObjectStaticMethodType(get.Name.Lexeme);
            if (methodType is TypeInfo.Function objMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                return objMethodType.ReturnType;
            }
        }

        // Handle Array.isArray()
        if (call.Callee is Expr.Get arrGet &&
            arrGet.Object is Expr.Variable arrVar &&
            arrVar.Name.Lexeme == "Array")
        {
            var methodType = BuiltInTypes.GetArrayStaticMethodType(arrGet.Name.Lexeme);
            if (methodType is TypeInfo.Function arrMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                return arrMethodType.ReturnType;
            }
        }

        // Handle JSON.parse(), JSON.stringify()
        if (call.Callee is Expr.Get jsonGet &&
            jsonGet.Object is Expr.Variable jsonVar &&
            jsonVar.Name.Lexeme == "JSON")
        {
            var methodType = BuiltInTypes.GetJSONStaticMethodType(jsonGet.Name.Lexeme);
            if (methodType is TypeInfo.Function jsonMethodType)
            {
                foreach (var arg in call.Arguments) CheckExpr(arg);
                return jsonMethodType.ReturnType;
            }
        }

        // Handle __objectRest (internal helper for object rest patterns)
        if (call.Callee is Expr.Variable restVar && restVar.Name.Lexeme == "__objectRest")
        {
            foreach (var arg in call.Arguments) CheckExpr(arg);
            return new TypeInfo.Any(); // Returns an object with remaining properties
        }

        TypeInfo calleeType = CheckExpr(call.Callee);

        if (calleeType is TypeInfo.Class classType)
        {
             return new TypeInfo.Instance(classType);
        }

        // Handle generic function calls
        if (calleeType is TypeInfo.GenericFunction genericFunc)
        {
            // Check each argument and collect their types
            List<TypeInfo> argTypes = [];
            foreach (var arg in call.Arguments)
            {
                if (arg is Expr.Spread spread)
                {
                    argTypes.Add(CheckExpr(spread.Expression));
                }
                else
                {
                    argTypes.Add(CheckExpr(arg));
                }
            }

            // Determine type arguments (explicit or inferred)
            List<TypeInfo> typeArgs;
            if (call.TypeArgs != null && call.TypeArgs.Count > 0)
            {
                // Explicit type arguments provided
                typeArgs = call.TypeArgs.Select(ToTypeInfo).ToList();
            }
            else
            {
                // Infer type arguments from call arguments
                typeArgs = InferTypeArguments(genericFunc, argTypes);
            }

            // Instantiate the function with the type arguments
            var instantiatedFunc = InstantiateGenericFunction(genericFunc, typeArgs);
            if (instantiatedFunc is TypeInfo.Function instFunc)
            {
                return instFunc.ReturnType;
            }
            return new TypeInfo.Any();
        }

        // Handle overloaded function calls
        if (calleeType is TypeInfo.OverloadedFunction overloadedFunc)
        {
            return ResolveOverloadedCall(call, overloadedFunc);
        }

        if (calleeType is TypeInfo.Function funcType)
        {
            // Count non-spread arguments and check for spreads
            bool hasSpread = call.Arguments.Any(a => a is Expr.Spread);
            int nonSpreadCount = call.Arguments.Count(a => a is not Expr.Spread);

            // Only check min arity if no spreads (spreads can expand to any count)
            if (!hasSpread && nonSpreadCount < funcType.MinArity)
            {
                throw new Exception($"Type Error: Expected at least {funcType.MinArity} arguments but got {nonSpreadCount}.");
            }

            // Get rest param element type if function has rest parameter
            TypeInfo? restElementType = null;
            if (funcType.HasRestParam && funcType.ParamTypes.Count > 0)
            {
                var lastParamType = funcType.ParamTypes[^1];
                if (lastParamType is TypeInfo.Array arrType)
                {
                    restElementType = arrType.ElementType;
                }
            }

            // Check types for provided arguments
            int argIndex = 0;
            int paramIndex = 0;
            int regularParamCount = funcType.HasRestParam ? funcType.ParamTypes.Count - 1 : funcType.ParamTypes.Count;

            foreach (var arg in call.Arguments)
            {
                if (arg is Expr.Spread spread)
                {
                    // Spread argument - check that it's an array
                    TypeInfo spreadType = CheckExpr(spread.Expression);
                    if (spreadType is TypeInfo.Array arrType)
                    {
                        // Check element type compatibility with rest param or remaining regular params
                        if (restElementType != null && !IsCompatible(restElementType, arrType.ElementType))
                        {
                            throw new Exception($"Type Error: Spread element type '{arrType.ElementType}' not compatible with rest parameter type '{restElementType}'.");
                        }
                    }
                    else if (spreadType is not TypeInfo.Any)
                    {
                        throw new Exception($"Type Error: Spread argument must be an array.");
                    }
                    // After spread, we can't reliably match params
                    break;
                }
                else
                {
                    TypeInfo expectedParamType = paramIndex < regularParamCount
                        ? funcType.ParamTypes[paramIndex]
                        : restElementType ?? new TypeInfo.Any();

                    // Apply contextual typing for array literals with tuple parameter types
                    if (expectedParamType is TypeInfo.Tuple tupleParamType && arg is Expr.ArrayLiteral argArrayLit)
                    {
                        CheckArrayLiteralAgainstTuple(argArrayLit, tupleParamType, $"argument {argIndex + 1}");
                    }
                    else
                    {
                        TypeInfo argType = CheckExpr(arg);
                        if (paramIndex < regularParamCount)
                        {
                            // Check against regular parameter
                            if (!IsCompatible(funcType.ParamTypes[paramIndex], argType))
                            {
                                throw new Exception($"Type Error: Argument {argIndex + 1} expected type '{funcType.ParamTypes[paramIndex]}' but got '{argType}'.");
                            }
                        }
                        else if (restElementType != null)
                        {
                            // Check against rest parameter element type
                            if (!IsCompatible(restElementType, argType))
                            {
                                throw new Exception($"Type Error: Argument {argIndex + 1} expected type '{restElementType}' but got '{argType}'.");
                            }
                        }
                    }
                    if (paramIndex < regularParamCount) paramIndex++;
                    argIndex++;
                }
            }
            return funcType.ReturnType;
        }
        else if (calleeType is TypeInfo.Any)
        {
             foreach(var arg in call.Arguments) CheckExpr(arg);
             return new TypeInfo.Any();
        }

        throw new Exception($"Type Error: Can only call functions.");
    }

    /// <summary>
    /// Extracts the callable function type from a TypeInfo that could be Function or OverloadedFunction.
    /// For OverloadedFunction, returns the implementation's type.
    /// </summary>
    private TypeInfo.Function? GetCallableFunction(TypeInfo? methodType)
    {
        return methodType switch
        {
            TypeInfo.Function f => f,
            TypeInfo.OverloadedFunction of => of.Implementation,
            _ => null
        };
    }

    /// <summary>
    /// Checks if constructor arguments match a constructor signature.
    /// </summary>
    private bool TryMatchConstructorArgs(List<TypeInfo> argTypes, List<TypeInfo> paramTypes, int minArity, bool hasRestParam)
    {
        if (argTypes.Count < minArity)
            return false;
        if (!hasRestParam && argTypes.Count > paramTypes.Count)
            return false;

        int regularParamCount = hasRestParam ? paramTypes.Count - 1 : paramTypes.Count;

        for (int i = 0; i < argTypes.Count && i < regularParamCount; i++)
        {
            if (!IsCompatible(paramTypes[i], argTypes[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolve an overloaded function call by finding the best matching signature.
    /// </summary>
    private TypeInfo ResolveOverloadedCall(Expr.Call call, TypeInfo.OverloadedFunction overloadedFunc)
    {
        // Collect argument types
        List<TypeInfo> argTypes = [];
        foreach (var arg in call.Arguments)
        {
            if (arg is Expr.Spread spread)
            {
                argTypes.Add(CheckExpr(spread.Expression));
            }
            else
            {
                argTypes.Add(CheckExpr(arg));
            }
        }

        // Find matching signatures
        List<TypeInfo.Function> matchingSignatures = [];

        foreach (var signature in overloadedFunc.Signatures)
        {
            if (TryMatchSignature(signature, argTypes))
            {
                matchingSignatures.Add(signature);
            }
        }

        if (matchingSignatures.Count == 0)
        {
            string argTypesStr = string.Join(", ", argTypes);
            throw new Exception($"Type Error: No overload matches call with arguments ({argTypesStr}).");
        }

        // If multiple signatures match, select the most specific one
        TypeInfo.Function bestMatch = SelectMostSpecificOverload(matchingSignatures, argTypes);

        return bestMatch.ReturnType;
    }

    /// <summary>
    /// Check if a signature matches the given argument types.
    /// </summary>
    private bool TryMatchSignature(TypeInfo.Function signature, List<TypeInfo> argTypes)
    {
        // Check argument count
        if (argTypes.Count < signature.MinArity)
            return false;

        if (!signature.HasRestParam && argTypes.Count > signature.ParamTypes.Count)
            return false;

        // Check each argument type
        int regularParamCount = signature.HasRestParam ? signature.ParamTypes.Count - 1 : signature.ParamTypes.Count;

        for (int i = 0; i < argTypes.Count; i++)
        {
            TypeInfo expectedType;
            if (i < regularParamCount)
            {
                expectedType = signature.ParamTypes[i];
            }
            else if (signature.HasRestParam && signature.ParamTypes.Count > 0)
            {
                // Rest parameter - check against element type
                var restType = signature.ParamTypes[^1];
                if (restType is TypeInfo.Array arrType)
                {
                    expectedType = arrType.ElementType;
                }
                else
                {
                    expectedType = new TypeInfo.Any();
                }
            }
            else
            {
                break; // No more parameters to check
            }

            if (!IsCompatible(expectedType, argTypes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Select the most specific signature from a list of matching signatures.
    /// Uses "most specific match" rules: prefer more specific types over general ones.
    /// </summary>
    private TypeInfo.Function SelectMostSpecificOverload(List<TypeInfo.Function> candidates, List<TypeInfo> argTypes)
    {
        if (candidates.Count == 1)
            return candidates[0];

        TypeInfo.Function mostSpecific = candidates[0];

        for (int i = 1; i < candidates.Count; i++)
        {
            int comparison = CompareSpecificity(mostSpecific, candidates[i], argTypes);
            if (comparison < 0)
            {
                // candidates[i] is more specific
                mostSpecific = candidates[i];
            }
            // If comparison == 0 (equally specific), keep the first one (declaration order)
        }

        return mostSpecific;
    }

    /// <summary>
    /// Compare two signatures for specificity.
    /// Returns: &gt;0 if sig1 is more specific, &lt;0 if sig2 is more specific, 0 if equally specific.
    /// </summary>
    private int CompareSpecificity(TypeInfo.Function sig1, TypeInfo.Function sig2, List<TypeInfo> argTypes)
    {
        int score = 0;
        int paramCount = Math.Min(Math.Min(sig1.ParamTypes.Count, sig2.ParamTypes.Count), argTypes.Count);

        for (int i = 0; i < paramCount; i++)
        {
            var p1 = sig1.ParamTypes[i];
            var p2 = sig2.ParamTypes[i];

            if (IsMoreSpecific(p1, p2))
                score++;
            else if (IsMoreSpecific(p2, p1))
                score--;
        }

        return score;
    }

    /// <summary>
    /// Returns true if 'specific' is a more specific type than 'general'.
    /// Specificity rules:
    /// - Literal types are more specific than primitives
    /// - Primitives are more specific than unions containing them
    /// - Derived classes are more specific than base classes
    /// - Non-nullable types are more specific than nullable types
    /// </summary>
    private bool IsMoreSpecific(TypeInfo specific, TypeInfo general)
    {
        // Literal type > Primitive type
        if (specific is TypeInfo.StringLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
            return true;
        if (specific is TypeInfo.NumberLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
            return true;
        if (specific is TypeInfo.BooleanLiteral && general is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN })
            return true;

        // Primitive > Union containing it
        if (general is TypeInfo.Union union)
        {
            if (specific is TypeInfo.Primitive || specific is TypeInfo.StringLiteral ||
                specific is TypeInfo.NumberLiteral || specific is TypeInfo.BooleanLiteral)
            {
                // Check if the specific type is one of the union members
                if (union.FlattenedTypes.Any(t => IsCompatible(t, specific)))
                    return true;
            }
        }

        // Non-nullable > Nullable (union with null)
        if (general is TypeInfo.Union nullableUnion && nullableUnion.ContainsNull)
        {
            if (specific is not TypeInfo.Null && specific is not TypeInfo.Union)
                return true;
        }

        // Derived class > Base class
        if (specific is TypeInfo.Instance i1 && general is TypeInfo.Instance i2)
        {
            if (i1.ClassType is TypeInfo.Class specificClass && i2.ClassType is TypeInfo.Class generalClass)
            {
                return IsSubclassOf(specificClass, generalClass);
            }
        }

        return false;
    }

    private TypeInfo CheckBinary(Expr.Binary binary)
    {
        TypeInfo left = CheckExpr(binary.Left);
        TypeInfo right = CheckExpr(binary.Right);

        switch (binary.Operator.Type)
        {
            case TokenType.MINUS:
            case TokenType.SLASH:
            case TokenType.STAR:
            case TokenType.STAR_STAR:
            case TokenType.PERCENT:
            case TokenType.GREATER:
            case TokenType.GREATER_EQUAL:
            case TokenType.LESS:
            case TokenType.LESS_EQUAL:
                if (!IsNumber(left) || !IsNumber(right))
                    throw new Exception("Type Error: Operands must be numbers.");

                return binary.Operator.Type switch
                {
                    TokenType.MINUS or TokenType.SLASH or TokenType.STAR or TokenType.STAR_STAR or TokenType.PERCENT => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                    _ => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN)
                };

            case TokenType.PLUS:
                if (IsNumber(left) && IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
                if (IsString(left) || IsString(right)) return new TypeInfo.Primitive(TokenType.TYPE_STRING);
                throw new Exception("Type Error: Operator '+' cannot be applied to types '" + left + "' and '" + right + "'.");

            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

            case TokenType.IN:
                // 'in' operator: left should be string/number, right should be object/array
                return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

            case TokenType.INSTANCEOF:
                // 'instanceof' operator: returns boolean
                return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);

            case TokenType.AMPERSAND:
            case TokenType.PIPE:
            case TokenType.CARET:
            case TokenType.LESS_LESS:
            case TokenType.GREATER_GREATER:
            case TokenType.GREATER_GREATER_GREATER:
                if (!IsNumber(left) || !IsNumber(right))
                    throw new Exception("Type Error: Bitwise operators require numeric operands.");
                return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckLogical(Expr.Logical logical)
    {
        CheckExpr(logical.Left);
        CheckExpr(logical.Right);
        return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
    }

    private TypeInfo CheckNullishCoalescing(Expr.NullishCoalescing nc)
    {
        TypeInfo leftType = CheckExpr(nc.Left);
        TypeInfo rightType = CheckExpr(nc.Right);

        // Remove null from left type since ?? handles the null case
        TypeInfo nonNullLeft = leftType;
        if (leftType is TypeInfo.Union u && u.ContainsNull)
        {
            var nonNullTypes = u.FlattenedTypes.Where(t => t is not TypeInfo.Null).ToList();
            nonNullLeft = nonNullTypes.Count == 0 ? rightType :
                nonNullTypes.Count == 1 ? nonNullTypes[0] :
                new TypeInfo.Union(nonNullTypes);
        }
        else if (leftType is TypeInfo.Null)
        {
            return rightType;  // null ?? right = right
        }

        // If left (non-null) and right are compatible, return non-null left
        if (IsCompatible(nonNullLeft, rightType) || IsCompatible(rightType, nonNullLeft))
        {
            return nonNullLeft;
        }

        // Otherwise return union of non-null left and right
        return new TypeInfo.Union([nonNullLeft, rightType]);
    }

    private TypeInfo CheckTernary(Expr.Ternary ternary)
    {
        CheckExpr(ternary.Condition);
        TypeInfo thenType = CheckExpr(ternary.ThenBranch);
        TypeInfo elseType = CheckExpr(ternary.ElseBranch);

        // Return the more specific type, or thenType if both are compatible
        if (IsCompatible(thenType, elseType) || IsCompatible(elseType, thenType))
        {
            return thenType;
        }

        // For now, allow different types and return Any
        return new TypeInfo.Any();
    }

    private TypeInfo CheckCompoundAssign(Expr.CompoundAssign compound)
    {
        TypeInfo varType = LookupVariable(compound.Name);
        TypeInfo valueType = CheckExpr(compound.Value);

        // For += with strings, allow string concatenation
        if (compound.Operator.Type == TokenType.PLUS_EQUAL)
        {
            if (IsString(varType)) return varType;
            if (!IsNumber(varType) || !IsNumber(valueType))
                throw new Exception("Type Error: Compound assignment requires numeric operands.");
            return varType;
        }

        // All other compound operators require numbers
        if (!IsNumber(varType) || !IsNumber(valueType))
        {
            throw new Exception("Type Error: Compound assignment requires numeric operands.");
        }

        return varType;
    }

    private TypeInfo CheckCompoundSet(Expr.CompoundSet compound)
    {
        CheckExpr(compound.Object);
        CheckExpr(compound.Value);
        return new TypeInfo.Any();
    }

    private TypeInfo CheckCompoundSetIndex(Expr.CompoundSetIndex compound)
    {
        TypeInfo objType = CheckExpr(compound.Object);
        TypeInfo indexType = CheckExpr(compound.Index);
        TypeInfo valueType = CheckExpr(compound.Value);

        if (!IsNumber(indexType))
            throw new Exception("Type Error: Array index must be a number.");

        if (objType is TypeInfo.Array arrayType)
        {
            if (!IsNumber(arrayType.ElementType) || !IsNumber(valueType))
                throw new Exception("Type Error: Compound assignment requires numeric operands.");
            return arrayType.ElementType;
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckPrefixIncrement(Expr.PrefixIncrement prefix)
    {
        TypeInfo operandType = CheckExpr(prefix.Operand);
        if (!IsNumber(operandType))
        {
            throw new Exception("Type Error: Increment/decrement operand must be a number.");
        }
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckPostfixIncrement(Expr.PostfixIncrement postfix)
    {
        TypeInfo operandType = CheckExpr(postfix.Operand);
        if (!IsNumber(operandType))
        {
            throw new Exception("Type Error: Increment/decrement operand must be a number.");
        }
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckArrowFunction(Expr.ArrowFunction arrow)
    {
        // Build parameter types and check defaults
        List<TypeInfo> paramTypes = [];
        int requiredParams = 0;
        bool seenDefault = false;

        foreach (var param in arrow.Parameters)
        {
            TypeInfo paramType = param.Type != null ? ToTypeInfo(param.Type) : new TypeInfo.Any();
            paramTypes.Add(paramType);

            // Rest parameters are not counted toward required params
            if (param.IsRest)
            {
                continue;
            }

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

        // Determine return type
        TypeInfo returnType = arrow.ReturnType != null
            ? ToTypeInfo(arrow.ReturnType)
            : new TypeInfo.Any();

        // Create new environment with parameters
        TypeEnvironment arrowEnv = new(_environment);
        for (int i = 0; i < arrow.Parameters.Count; i++)
        {
            arrowEnv.Define(arrow.Parameters[i].Name.Lexeme, paramTypes[i]);
        }

        // Save and set context
        TypeEnvironment previousEnv = _environment;
        TypeInfo? previousReturn = _currentFunctionReturnType;

        _environment = arrowEnv;
        _currentFunctionReturnType = returnType;

        try
        {
            if (arrow.ExpressionBody != null)
            {
                // Expression body - infer return type if not specified
                TypeInfo exprType = CheckExpr(arrow.ExpressionBody);
                if (arrow.ReturnType == null)
                {
                    returnType = exprType;
                }
                else if (!IsCompatible(returnType, exprType))
                {
                    throw new Exception($"Type Error: Arrow function declared to return '{returnType}' but expression evaluates to '{exprType}'.");
                }
            }
            else if (arrow.BlockBody != null)
            {
                // Block body - check statements
                foreach (var stmt in arrow.BlockBody)
                {
                    CheckStmt(stmt);
                }
            }
        }
        finally
        {
            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
        }

        bool hasRest = arrow.Parameters.Any(p => p.IsRest);
        return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest);
    }

    private TypeInfo CheckUnary(Expr.Unary unary)
    {
        TypeInfo right = CheckExpr(unary.Right);
        if (unary.Operator.Type == TokenType.TYPEOF)
            return new TypeInfo.Primitive(TokenType.TYPE_STRING);
        if (unary.Operator.Type == TokenType.MINUS && !IsNumber(right))
             throw new Exception("Type Error: Unary '-' expects a number.");
        if (unary.Operator.Type == TokenType.BANG)
             return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (unary.Operator.Type == TokenType.TILDE)
        {
            if (!IsNumber(right))
                throw new Exception("Type Error: Bitwise NOT requires a numeric operand.");
            return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        }

        return right;
    }

    private TypeInfo CheckAssign(Expr.Assign assign)
    {
        TypeInfo varType = LookupVariable(assign.Name);
        TypeInfo valueType = CheckExpr(assign.Value);

        if (!IsCompatible(varType, valueType))
        {
            throw new Exception($"Type Error: Cannot assign type '{valueType}' to variable '{assign.Name.Lexeme}' of type '{varType}'.");
        }
        return valueType;
    }

    private TypeInfo LookupVariable(Token name)
    {
        if (name.Lexeme == "console") return new TypeInfo.Any();
        if (name.Lexeme == "Math") return new TypeInfo.Any(); // Math is a special global object
        if (name.Lexeme == "Object") return new TypeInfo.Any(); // Object is a special global object
        if (name.Lexeme == "Array") return new TypeInfo.Any(); // Array is a special global object
        if (name.Lexeme == "JSON") return new TypeInfo.Any(); // JSON is a special global object

        var type = _environment.Get(name.Lexeme);
        if (type == null)
        {
             throw new Exception($"Type Error: Undefined variable '{name.Lexeme}'.");
        }
        return type;
    }

    private TypeInfo GetLiteralType(object? value)
    {
        if (value is null) return new TypeInfo.Null();
        if (value is int i) return new TypeInfo.NumberLiteral((double)i);
        if (value is double d) return new TypeInfo.NumberLiteral(d);
        if (value is string s) return new TypeInfo.StringLiteral(s);
        if (value is bool b) return new TypeInfo.BooleanLiteral(b);
        return new TypeInfo.Void();
    }

    private bool IsCompatible(TypeInfo expected, TypeInfo actual)
    {
        if (expected is TypeInfo.Any || actual is TypeInfo.Any) return true;

        // Type parameter compatibility: same name = compatible
        if (expected is TypeInfo.TypeParameter expectedTp && actual is TypeInfo.TypeParameter actualTp)
        {
            return expectedTp.Name == actualTp.Name;
        }

        // Type parameter as expected: actual satisfies if it matches the constraint
        if (expected is TypeInfo.TypeParameter tp)
        {
            if (tp.Constraint != null)
                return IsCompatible(tp.Constraint, actual);
            return true; // Unconstrained type parameter accepts anything
        }

        // Type parameter as actual: can be assigned to any or same type parameter
        if (actual is TypeInfo.TypeParameter)
        {
            return expected is TypeInfo.Any;
        }

        // never as actual: assignable to anything (bottom type)
        if (actual is TypeInfo.Never) return true;

        // never as expected: nothing assignable to never except never
        if (expected is TypeInfo.Never) return actual is TypeInfo.Never;

        // unknown as expected: anything can be assigned TO unknown (top type)
        if (expected is TypeInfo.Unknown) return true;

        // unknown as actual: can only be assigned to unknown or any
        if (actual is TypeInfo.Unknown)
            return expected is TypeInfo.Unknown || expected is TypeInfo.Any;

        // Null compatibility
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) return true;
            if (expected is TypeInfo.Null) return true;
            return false;
        }

        // Literal type compatibility - literal to literal (must have same value)
        if (expected is TypeInfo.StringLiteral sl1 && actual is TypeInfo.StringLiteral sl2)
            return sl1.Value == sl2.Value;
        if (expected is TypeInfo.NumberLiteral nl1 && actual is TypeInfo.NumberLiteral nl2)
            return nl1.Value == nl2.Value;
        if (expected is TypeInfo.BooleanLiteral bl1 && actual is TypeInfo.BooleanLiteral bl2)
            return bl1.Value == bl2.Value;

        // Literal to primitive widening
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_STRING } && actual is TypeInfo.StringLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            return true;

        // Union-to-union: each type in actual must be compatible with at least one type in expected
        if (expected is TypeInfo.Union expectedUnion && actual is TypeInfo.Union actualUnion)
        {
            return actualUnion.FlattenedTypes.All(actualType =>
                expectedUnion.FlattenedTypes.Any(expectedType => IsCompatible(expectedType, actualType)));
        }

        // Union as expected: actual must match at least one member
        if (expected is TypeInfo.Union expUnion)
        {
            return expUnion.FlattenedTypes.Any(t => IsCompatible(t, actual));
        }

        // Union as actual: all members must be compatible with expected
        if (actual is TypeInfo.Union actUnion)
        {
            return actUnion.FlattenedTypes.All(t => IsCompatible(expected, t));
        }

        // Intersection as expected: actual must satisfy ALL member types
        if (expected is TypeInfo.Intersection expIntersection)
        {
            return expIntersection.FlattenedTypes.All(t => IsCompatible(t, actual));
        }

        // Intersection as actual: satisfies expected if any member does
        // (because intersection value has all the properties of all its constituents)
        if (actual is TypeInfo.Intersection actIntersection)
        {
            return actIntersection.FlattenedTypes.Any(t => IsCompatible(expected, t));
        }

        // Enum compatibility: primitive values are assignable to their enum type
        // (e.g., Direction.Up which is typed as 'number' can be assigned to Direction)
        if (expected is TypeInfo.Enum expectedEnum)
        {
            // Same enum type is compatible
            if (actual is TypeInfo.Enum actualEnum && expectedEnum.Name == actualEnum.Name)
                return true;

            // Numeric enum accepts number
            if (expectedEnum.Kind == EnumKind.Numeric &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            // String enum accepts string
            if (expectedEnum.Kind == EnumKind.String &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                return true;

            // Heterogeneous enum accepts both
            if (expectedEnum.Kind == EnumKind.Heterogeneous &&
                actual is TypeInfo.Primitive p &&
                (p.Type == TokenType.TYPE_NUMBER || p.Type == TokenType.TYPE_STRING))
                return true;

            return false;
        }

        // Enum as actual: can be assigned to compatible primitive type
        // (e.g., a Direction variable can be used where a number is expected)
        if (actual is TypeInfo.Enum actualEnumType)
        {
            if (actualEnumType.Kind == EnumKind.Numeric &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            if (actualEnumType.Kind == EnumKind.String &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                return true;

            if (actualEnumType.Kind == EnumKind.Heterogeneous &&
                expected is TypeInfo.Primitive ep &&
                (ep.Type == TokenType.TYPE_NUMBER || ep.Type == TokenType.TYPE_STRING))
                return true;
        }

        if (expected is TypeInfo.Primitive p1 && actual is TypeInfo.Primitive p2)
        {
            return p1.Type == p2.Type;
        }

        // Symbol type compatibility
        if (expected is TypeInfo.Symbol && actual is TypeInfo.Symbol)
        {
            return true;
        }

        if (expected is TypeInfo.Instance i1 && actual is TypeInfo.Instance i2)
        {
            // Handle InstantiatedGeneric comparison
            if (i1.ClassType is TypeInfo.InstantiatedGeneric expectedIG &&
                i2.ClassType is TypeInfo.InstantiatedGeneric actualIG)
            {
                // Same generic definition and compatible type arguments
                if (expectedIG.GenericDefinition is TypeInfo.GenericClass gc1 &&
                    actualIG.GenericDefinition is TypeInfo.GenericClass gc2 &&
                    gc1.Name == gc2.Name)
                {
                    if (expectedIG.TypeArguments.Count != actualIG.TypeArguments.Count)
                        return false;
                    for (int i = 0; i < expectedIG.TypeArguments.Count; i++)
                    {
                        if (!IsCompatible(expectedIG.TypeArguments[i], actualIG.TypeArguments[i]))
                            return false;
                    }
                    return true;
                }
                return false;
            }

            // Handle regular Class comparison
            if (i1.ClassType is TypeInfo.Class expectedClass && i2.ClassType is TypeInfo.Class actualClass)
            {
                TypeInfo.Class? current = actualClass;
                while (current != null)
                {
                    if (current.Name == expectedClass.Name) return true;
                    current = current.Superclass;
                }
            }

            // Mixed case: InstantiatedGeneric vs regular Class - not compatible
            return false;
        }

        if (expected is TypeInfo.Interface itf)
        {
            return CheckStructuralCompatibility(itf.Members, actual, itf.OptionalMemberSet);
        }

        // Handle InstantiatedGeneric interface (e.g., Container<number>)
        if (expected is TypeInfo.InstantiatedGeneric expectedInterfaceIG &&
            expectedInterfaceIG.GenericDefinition is TypeInfo.GenericInterface gi)
        {
            // Build substitution map
            var subs = new Dictionary<string, TypeInfo>();
            for (int i = 0; i < gi.TypeParams.Count; i++)
                subs[gi.TypeParams[i].Name] = expectedInterfaceIG.TypeArguments[i];

            // Substitute type parameters in interface members
            var substitutedMembers = new Dictionary<string, TypeInfo>();
            foreach (var kvp in gi.Members)
                substitutedMembers[kvp.Key] = Substitute(kvp.Value, subs);

            return CheckStructuralCompatibility(substitutedMembers, actual, gi.OptionalMembers);
        }

        if (expected is TypeInfo.Array a1 && actual is TypeInfo.Array a2)
        {
            return IsCompatible(a1.ElementType, a2.ElementType);
        }

        // Record-to-Record compatibility (inline object types)
        if (expected is TypeInfo.Record expRecord && actual is TypeInfo.Record actRecord)
        {
            // All explicit fields in expected must exist in actual with compatible types
            foreach (var (name, expectedFieldType) in expRecord.Fields)
            {
                if (!actRecord.Fields.TryGetValue(name, out var actualFieldType))
                    return false;
                if (!IsCompatible(expectedFieldType, actualFieldType))
                    return false;
            }
            // If expected has only index signatures (no explicit fields), empty object is compatible
            // Index signatures allow any number of keys (including zero)
            return true;
        }

        // Tuple-to-tuple compatibility
        if (expected is TypeInfo.Tuple expTuple && actual is TypeInfo.Tuple actTuple)
        {
            return IsTupleCompatible(expTuple, actTuple);
        }

        // Tuple assignable to array (e.g., [string, number] -> (string | number)[])
        if (expected is TypeInfo.Array expArr && actual is TypeInfo.Tuple actTuple2)
        {
            return IsTupleToArrayCompatible(expArr, actTuple2);
        }

        // Array assignable to tuple (limited - only for rest tuples or all-optional)
        if (expected is TypeInfo.Tuple expTuple2 && actual is TypeInfo.Array actArr)
        {
            return IsArrayToTupleCompatible(expTuple2, actArr);
        }

        if (expected is TypeInfo.Void && actual is TypeInfo.Void) return true;

        // Function type compatibility
        if (expected is TypeInfo.Function f1 && actual is TypeInfo.Function f2)
        {
            // For callbacks, actual can have fewer params than expected (unused params)
            if (f2.ParamTypes.Count > f1.ParamTypes.Count) return false;
            for (int i = 0; i < f2.ParamTypes.Count; i++)
            {
                if (!IsCompatible(f1.ParamTypes[i], f2.ParamTypes[i])) return false;
            }
            // Return type: actual must be compatible with expected
            return IsCompatible(f1.ReturnType, f2.ReturnType);
        }

        return false;
    }

    private bool IsTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        // Actual must have at least the required elements
        if (actual.ElementTypes.Count < expected.RequiredCount) return false;

        // If expected has fixed length (no rest), actual cannot be longer
        if (expected.MaxLength != null && actual.ElementTypes.Count > expected.MaxLength) return false;

        // Check element type compatibility for overlapping positions
        int minLen = Math.Min(expected.ElementTypes.Count, actual.ElementTypes.Count);
        for (int i = 0; i < minLen; i++)
        {
            if (!IsCompatible(expected.ElementTypes[i], actual.ElementTypes[i]))
                return false;
        }

        // If expected has rest, check remaining actual elements against rest type
        if (expected.RestElementType != null)
        {
            for (int i = expected.ElementTypes.Count; i < actual.ElementTypes.Count; i++)
            {
                if (!IsCompatible(expected.RestElementType, actual.ElementTypes[i]))
                    return false;
            }
            // If actual also has rest, check rest compatibility
            if (actual.RestElementType != null &&
                !IsCompatible(expected.RestElementType, actual.RestElementType))
                return false;
        }

        return true;
    }

    private bool IsTupleToArrayCompatible(TypeInfo.Array expected, TypeInfo.Tuple actual)
    {
        // All tuple element types must be compatible with array element type
        foreach (var elemType in actual.ElementTypes)
        {
            if (!IsCompatible(expected.ElementType, elemType))
                return false;
        }
        if (actual.RestElementType != null &&
            !IsCompatible(expected.ElementType, actual.RestElementType))
            return false;
        return true;
    }

    private bool IsArrayToTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Array actual)
    {
        // Array can match tuple only if tuple has rest or is all-optional
        if (expected.RequiredCount > 0 && expected.RestElementType == null)
            return false;

        // All expected element types must be compatible with actual's element type
        foreach (var elemType in expected.ElementTypes)
        {
            if (!IsCompatible(elemType, actual.ElementType))
                return false;
        }
        if (expected.RestElementType != null &&
            !IsCompatible(expected.RestElementType, actual.ElementType))
            return false;
        return true;
    }

    private (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeGuard(Expr condition)
    {
        // Pattern: typeof x === "string" or typeof x == "string"
        if (condition is Expr.Binary bin &&
            bin.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL &&
            bin.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v } &&
            bin.Right is Expr.Literal { Value: string typeStr })
        {
            var currentType = _environment.Get(v.Name.Lexeme);

            // Handle unknown type narrowing - typeof checks narrow unknown to specific types
            if (currentType is TypeInfo.Unknown)
            {
                TypeInfo? narrowedType = typeStr switch
                {
                    "string" => new TypeInfo.Primitive(TokenType.TYPE_STRING),
                    "number" => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
                    "boolean" => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
                    _ => null
                };
                // Excluded type remains unknown (we don't know what else it could be)
                return (v.Name.Lexeme, narrowedType, new TypeInfo.Unknown());
            }

            if (currentType is TypeInfo.Union union)
            {
                var narrowed = union.FlattenedTypes.Where(t => TypeMatchesTypeof(t, typeStr)).ToList();
                var excluded = union.FlattenedTypes.Where(t => !TypeMatchesTypeof(t, typeStr)).ToList();

                TypeInfo? narrowedType = narrowed.Count == 0 ? null :
                    narrowed.Count == 1 ? narrowed[0] : new TypeInfo.Union(narrowed);
                TypeInfo? excludedType = excluded.Count == 0 ? null :
                    excluded.Count == 1 ? excluded[0] : new TypeInfo.Union(excluded);

                return (v.Name.Lexeme, narrowedType, excludedType);
            }
        }
        return (null, null, null);
    }

    private bool TypeMatchesTypeof(TypeInfo type, string typeofResult) => typeofResult switch
    {
        "string" => type is TypeInfo.Primitive { Type: TokenType.TYPE_STRING },
        "number" => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER },
        "boolean" => type is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN },
        "object" => type is TypeInfo.Null or TypeInfo.Record or TypeInfo.Array or TypeInfo.Instance,
        "function" => type is TypeInfo.Function,
        _ => false
    };

    private bool CheckStructuralCompatibility(Dictionary<string, TypeInfo> requiredMembers, TypeInfo actual, HashSet<string>? optionalMembers = null)
    {
        optionalMembers ??= [];
        foreach (var member in requiredMembers)
        {
            TypeInfo? actualMemberType = GetMemberType(actual, member.Key);

            // If member is optional and not present, that's OK
            if (actualMemberType == null && optionalMembers.Contains(member.Key))
            {
                continue;
            }

            if (actualMemberType == null || !IsCompatible(member.Value, actualMemberType))
            {
                return false;
            }
        }
        return true;
    }

    private TypeInfo? GetMemberType(TypeInfo type, string name)
    {
        if (type is TypeInfo.Record record)
        {
            return record.Fields.TryGetValue(name, out var t) ? t : null;
        }
        if (type is TypeInfo.Instance instance)
        {
            // Handle InstantiatedGeneric
            if (instance.ClassType is TypeInfo.InstantiatedGeneric ig &&
                ig.GenericDefinition is TypeInfo.GenericClass gc)
            {
                if (gc.Methods.TryGetValue(name, out var methodType)) return methodType;
                var current = gc.Superclass;
                while (current != null)
                {
                    if (current.Methods.TryGetValue(name, out var superMethod)) return superMethod;
                    current = current.Superclass;
                }
            }
            else if (instance.ClassType is TypeInfo.Class classType)
            {
                TypeInfo.Class? current = classType;
                while (current != null)
                {
                    if (current.Methods.TryGetValue(name, out var methodType)) return methodType;
                    current = current.Superclass;
                }
            }
        }
        return null;
    }
    
    private bool IsNumber(TypeInfo t) => t is TypeInfo.Primitive p && p.Type == TokenType.TYPE_NUMBER || t is TypeInfo.NumberLiteral || t is TypeInfo.Any;
    private bool IsString(TypeInfo t) => t is TypeInfo.Primitive p && p.Type == TokenType.TYPE_STRING || t is TypeInfo.StringLiteral || t is TypeInfo.Any;

    private bool IsSubclassOf(TypeInfo.Class? subclass, TypeInfo.Class target)
    {
        if (subclass == null) return false;
        TypeInfo.Class? current = subclass;
        while (current != null)
        {
            if (current.Name == target.Name) return true;
            current = current.Superclass;
        }
        return false;
    }

    private void ValidateInterfaceImplementation(TypeInfo.Class classType, TypeInfo.Interface interfaceType, string className)
    {
        foreach (var member in interfaceType.Members)
        {
            string memberName = member.Key;
            TypeInfo expectedType = member.Value;
            bool isOptional = interfaceType.OptionalMemberSet.Contains(memberName);

            // Check: field, getter, or method (including inheritance chain)
            TypeInfo? actualType = FindMemberInClass(classType, memberName);

            if (actualType == null && !isOptional)
            {
                throw new Exception($"Type Error: Class '{className}' does not implement '{memberName}' from interface '{interfaceType.Name}'.");
            }

            if (actualType != null && !IsCompatible(expectedType, actualType))
            {
                throw new Exception($"Type Error: '{className}.{memberName}' has incompatible type. Expected '{expectedType}', got '{actualType}'.");
            }
        }
    }

    /// <summary>
    /// Validates that a non-abstract class implements all abstract members from its superclass chain.
    /// </summary>
    private void ValidateAbstractMemberImplementation(TypeInfo.Class classType, string className)
    {
        // Collect all unimplemented abstract members from the superclass chain
        List<string> missingMembers = [];

        TypeInfo.Class? current = classType.Superclass;
        while (current != null)
        {
            // Check abstract methods from this superclass
            foreach (var abstractMethod in current.AbstractMethodSet)
            {
                // Check if this class or any class in between implements it
                if (!IsMethodImplemented(classType, abstractMethod, current))
                {
                    missingMembers.Add(abstractMethod + "()");
                }
            }

            // Check abstract getters
            foreach (var abstractGetter in current.AbstractGetterSet)
            {
                if (!IsGetterImplemented(classType, abstractGetter, current))
                {
                    missingMembers.Add("get " + abstractGetter);
                }
            }

            // Check abstract setters
            foreach (var abstractSetter in current.AbstractSetterSet)
            {
                if (!IsSetterImplemented(classType, abstractSetter, current))
                {
                    missingMembers.Add("set " + abstractSetter);
                }
            }

            current = current.Superclass;
        }

        if (missingMembers.Count > 0)
        {
            throw new Exception($"Type Error: Class '{className}' must implement the following abstract members: {string.Join(", ", missingMembers)}");
        }
    }

    /// <summary>
    /// Checks if a method is implemented in the class chain between classType and the abstract superclass.
    /// </summary>
    private bool IsMethodImplemented(TypeInfo.Class classType, string methodName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            // Check if this class has the method and it's NOT abstract
            if (current.Methods.ContainsKey(methodName) && !current.AbstractMethodSet.Contains(methodName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private bool IsGetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            if (current.GetterTypes.ContainsKey(propertyName) && !current.AbstractGetterSet.Contains(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private bool IsSetterImplemented(TypeInfo.Class classType, string propertyName, TypeInfo.Class abstractSuperclass)
    {
        TypeInfo.Class? current = classType;
        while (current != null && current != abstractSuperclass)
        {
            if (current.SetterTypes.ContainsKey(propertyName) && !current.AbstractSetterSet.Contains(propertyName))
            {
                return true;
            }
            current = current.Superclass;
        }
        return false;
    }

    private TypeInfo? FindMemberInClass(TypeInfo.Class classType, string name)
    {
        TypeInfo.Class? current = classType;
        while (current != null)
        {
            if (current.DeclaredFieldTypes.TryGetValue(name, out var ft)) return ft;
            if (current.GetterTypes.TryGetValue(name, out var gt)) return gt;
            if (current.Methods.TryGetValue(name, out var mt)) return mt;
            current = current.Superclass;
        }
        return null;
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

    private TypeInfo ToTypeInfo(string typeName)
    {
        // Check for type parameter in current scope first
        var typeParam = _environment.GetTypeParameter(typeName);
        if (typeParam != null)
        {
            return typeParam;
        }

        // Check for type alias
        var aliasExpansion = _environment.GetTypeAlias(typeName);
        if (aliasExpansion != null)
        {
            return ToTypeInfo(aliasExpansion);
        }

        // Handle generic type syntax: Box<number>, Map<string, number>
        if (typeName.Contains('<') && typeName.Contains('>'))
        {
            return ParseGenericTypeReference(typeName);
        }

        // Handle union types: "string | number"
        // Union has lower precedence than intersection, check it first at top level
        if (typeName.Contains(" | "))
        {
            var parts = SplitUnionParts(typeName);
            if (parts.Count > 1)  // Only create union if we actually split at top level
            {
                var types = parts.Select(ToTypeInfo).ToList();
                return new TypeInfo.Union(types);
            }
        }

        // Handle intersection types: "A & B"
        // Intersection has higher precedence than union
        if (typeName.Contains(" & "))
        {
            var parts = SplitIntersectionParts(typeName);
            if (parts.Count > 1)  // Only create intersection if we actually split at top level
            {
                var types = parts.Select(ToTypeInfo).ToList();
                return SimplifyIntersection(types);
            }
        }

        // Handle inline object types: "{ x: number; y?: string }"
        // Must check BEFORE function types since objects can contain function-typed properties
        if (typeName.StartsWith("{ ") && typeName.EndsWith(" }"))
        {
            return ParseInlineObjectTypeInfo(typeName);
        }

        // Check for function type syntax: "(params) => returnType"
        // Must check BEFORE parenthesized types since both start with "("
        if (typeName.Contains("=>"))
        {
            return ParseFunctionTypeInfo(typeName);
        }

        // Handle parenthesized types: "(string | number)[]"
        if (typeName.StartsWith("("))
        {
            return ParseParenthesizedType(typeName);
        }

        // Handle tuple types: "[string, number, boolean?]"
        if (typeName.StartsWith("[") && typeName.EndsWith("]"))
        {
            return ParseTupleTypeInfo(typeName);
        }

        if (typeName.EndsWith("[]"))
        {
            string elementTypeString = typeName.Substring(0, typeName.Length - 2);
            TypeInfo elementType = ToTypeInfo(elementTypeString);
            return new TypeInfo.Array(elementType);
        }

        // Handle string literal types: "value"
        if (typeName.StartsWith("\"") && typeName.EndsWith("\""))
        {
            return new TypeInfo.StringLiteral(typeName[1..^1]);
        }

        // Handle boolean literal types
        if (typeName == "true") return new TypeInfo.BooleanLiteral(true);
        if (typeName == "false") return new TypeInfo.BooleanLiteral(false);

        // Handle number literal types (check before primitives)
        if (double.TryParse(typeName, out double numValue))
        {
            return new TypeInfo.NumberLiteral(numValue);
        }

        if (typeName == "string") return new TypeInfo.Primitive(TokenType.TYPE_STRING);
        if (typeName == "number") return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (typeName == "boolean") return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (typeName == "symbol") return new TypeInfo.Symbol();
        if (typeName == "void") return new TypeInfo.Void();
        if (typeName == "null") return new TypeInfo.Null();
        if (typeName == "unknown") return new TypeInfo.Unknown();
        if (typeName == "never") return new TypeInfo.Never();

        TypeInfo? type = _environment.Get(typeName);
        if (type is TypeInfo.Class classType)
        {
            return new TypeInfo.Instance(classType);
        }
        if (type is TypeInfo.Interface itfType)
        {
            return itfType;
        }
        if (type is TypeInfo.Enum enumType)
        {
            return enumType;
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// Parses a generic type reference like "Box&lt;number&gt;" or "Map&lt;string, number&gt;".
    /// </summary>
    private TypeInfo ParseGenericTypeReference(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        string baseName = typeName[..openAngle];
        string argsStr = typeName[(openAngle + 1)..^1];

        // Split type arguments respecting nesting
        var typeArgStrings = SplitTypeArguments(argsStr);
        var typeArgs = typeArgStrings.Select(ToTypeInfo).ToList();

        // Look up the generic definition
        TypeInfo? genericDef = _environment.Get(baseName);

        return genericDef switch
        {
            TypeInfo.GenericClass gc => new TypeInfo.Instance(InstantiateGenericClass(gc, typeArgs)),
            TypeInfo.GenericInterface gi => InstantiateGenericInterface(gi, typeArgs),
            TypeInfo.GenericFunction gf => InstantiateGenericFunction(gf, typeArgs),
            _ => new TypeInfo.Any() // Unknown generic type - fallback to any
        };
    }

    /// <summary>
    /// Splits type arguments respecting nested angle brackets.
    /// </summary>
    private List<string> SplitTypeArguments(string argsStr)
    {
        List<string> args = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(argsStr[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < argsStr.Length)
        {
            args.Add(argsStr[start..].Trim());
        }

        return args;
    }

    /// <summary>
    /// Instantiates a generic class with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericClass(TypeInfo.GenericClass generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic class '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, typeArgs);
    }

    /// <summary>
    /// Instantiates a generic interface with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericInterface(TypeInfo.GenericInterface generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic interface '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, typeArgs);
    }

    /// <summary>
    /// Instantiates a generic function with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericFunction(TypeInfo.GenericFunction generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic function requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        // Create substitution map
        var substitutions = new Dictionary<string, TypeInfo>();
        for (int i = 0; i < typeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = typeArgs[i];
        }

        // Substitute type parameters in the function signature
        var substitutedParams = generic.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
        var substitutedReturn = Substitute(generic.ReturnType, substitutions);

        return new TypeInfo.Function(substitutedParams, substitutedReturn, generic.RequiredParams, generic.HasRestParam);
    }

    /// <summary>
    /// Substitutes type parameters with concrete types.
    /// </summary>
    private TypeInfo Substitute(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
    {
        return type switch
        {
            TypeInfo.TypeParameter tp =>
                substitutions.TryGetValue(tp.Name, out var sub) ? sub : type,
            TypeInfo.Array arr =>
                new TypeInfo.Array(Substitute(arr.ElementType, substitutions)),
            TypeInfo.Function func =>
                new TypeInfo.Function(
                    func.ParamTypes.Select(p => Substitute(p, substitutions)).ToList(),
                    Substitute(func.ReturnType, substitutions),
                    func.RequiredParams,
                    func.HasRestParam),
            TypeInfo.Tuple tuple =>
                new TypeInfo.Tuple(
                    tuple.ElementTypes.Select(e => Substitute(e, substitutions)).ToList(),
                    tuple.RequiredCount,
                    tuple.RestElementType != null ? Substitute(tuple.RestElementType, substitutions) : null),
            TypeInfo.Union union =>
                new TypeInfo.Union(union.Types.Select(t => Substitute(t, substitutions)).ToList()),
            TypeInfo.Record rec =>
                new TypeInfo.Record(rec.Fields.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Substitute(kvp.Value, substitutions))),
            TypeInfo.InstantiatedGeneric ig =>
                new TypeInfo.InstantiatedGeneric(
                    ig.GenericDefinition,
                    ig.TypeArguments.Select(a => Substitute(a, substitutions)).ToList()),
            // Primitives, Any, Void, Never, Unknown, Null pass through unchanged
            _ => type
        };
    }

    /// <summary>
    /// Infers type arguments from call arguments for a generic function.
    /// </summary>
    private List<TypeInfo> InferTypeArguments(TypeInfo.GenericFunction gf, List<TypeInfo> argTypes)
    {
        var inferred = new Dictionary<string, TypeInfo>();

        // Try to infer each type parameter from the corresponding argument
        for (int i = 0; i < gf.ParamTypes.Count && i < argTypes.Count; i++)
        {
            InferFromType(gf.ParamTypes[i], argTypes[i], inferred);
        }

        // Build result list in order of type parameters
        var result = new List<TypeInfo>();
        foreach (var tp in gf.TypeParams)
        {
            if (inferred.TryGetValue(tp.Name, out var inferredType))
            {
                // Validate constraint
                if (tp.Constraint != null && !IsCompatible(tp.Constraint, inferredType))
                {
                    throw new Exception($"Type Error: Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
                }
                result.Add(inferredType);
            }
            else
            {
                // Default to constraint or any if not inferred
                result.Add(tp.Constraint ?? new TypeInfo.Any());
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively infers type parameter bindings from a parameter type and an argument type.
    /// </summary>
    private void InferFromType(TypeInfo paramType, TypeInfo argType, Dictionary<string, TypeInfo> inferred)
    {
        if (paramType is TypeInfo.TypeParameter tp)
        {
            // Direct type parameter - infer from argument
            if (!inferred.ContainsKey(tp.Name))
            {
                inferred[tp.Name] = argType;
            }
            // If already inferred, we could unify types here for more sophisticated inference
        }
        else if (paramType is TypeInfo.Array paramArr && argType is TypeInfo.Array argArr)
        {
            // Recurse into array element types
            InferFromType(paramArr.ElementType, argArr.ElementType, inferred);
        }
        else if (paramType is TypeInfo.Function paramFunc && argType is TypeInfo.Function argFunc)
        {
            // Recurse into function types
            for (int i = 0; i < paramFunc.ParamTypes.Count && i < argFunc.ParamTypes.Count; i++)
            {
                InferFromType(paramFunc.ParamTypes[i], argFunc.ParamTypes[i], inferred);
            }
            InferFromType(paramFunc.ReturnType, argFunc.ReturnType, inferred);
        }
        else if (paramType is TypeInfo.InstantiatedGeneric paramGen && argType is TypeInfo.InstantiatedGeneric argGen)
        {
            // Same generic base - infer from type arguments
            for (int i = 0; i < paramGen.TypeArguments.Count && i < argGen.TypeArguments.Count; i++)
            {
                InferFromType(paramGen.TypeArguments[i], argGen.TypeArguments[i], inferred);
            }
        }
    }

    private List<string> SplitUnionParts(string typeName)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == '|' && depth == 0 && i > 0 && typeName[i - 1] == ' ')
            {
                parts.Add(typeName[start..(i - 1)].Trim());
                start = i + 2;
            }
        }
        parts.Add(typeName[start..].Trim());
        return parts;
    }

    /// <summary>
    /// Splits an intersection type string into its component parts, respecting nesting.
    /// E.g., "A &amp; B &amp; C" becomes ["A", "B", "C"]
    /// </summary>
    private List<string> SplitIntersectionParts(string typeName)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '(' || c == '<' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == '>' || c == ']' || c == '}') depth--;
            else if (c == '&' && depth == 0 && i > 0 && typeName[i - 1] == ' ')
            {
                parts.Add(typeName[start..(i - 1)].Trim());
                start = i + 2;
            }
        }
        parts.Add(typeName[start..].Trim());
        return parts;
    }

    /// <summary>
    /// Simplifies an intersection type according to TypeScript semantics:
    /// - Conflicting primitives (string &amp; number) = never
    /// - never &amp; T = never
    /// - any &amp; T = any
    /// - unknown &amp; T = T
    /// - Object types are merged with property combination
    /// </summary>
    private TypeInfo SimplifyIntersection(List<TypeInfo> types)
    {
        // Handle empty or single type
        if (types.Count == 0) return new TypeInfo.Unknown();
        if (types.Count == 1) return types[0];

        // Check for never (absorbs everything)
        if (types.Any(t => t is TypeInfo.Never))
            return new TypeInfo.Never();

        // Check for any (absorbs in intersection)
        if (types.Any(t => t is TypeInfo.Any))
            return new TypeInfo.Any();

        // Remove unknown (identity element)
        types = types.Where(t => t is not TypeInfo.Unknown).ToList();
        if (types.Count == 0) return new TypeInfo.Unknown();
        if (types.Count == 1) return types[0];

        // Check for conflicting primitives (e.g., string & number = never)
        var primitives = types.OfType<TypeInfo.Primitive>().ToList();
        if (primitives.Count > 1)
        {
            var distinctPrimitives = primitives.Select(p => p.Type).Distinct().ToList();
            if (distinctPrimitives.Count > 1)
                return new TypeInfo.Never();  // Conflicting primitives
        }

        // Collect object-like types for merging
        var records = types.OfType<TypeInfo.Record>().ToList();
        var interfaces = types.OfType<TypeInfo.Interface>().ToList();
        var classes = types.OfType<TypeInfo.Class>().ToList();
        var instances = types.OfType<TypeInfo.Instance>().ToList();

        if (records.Count > 0 || interfaces.Count > 0 || classes.Count > 0 || instances.Count > 0)
        {
            // Merge all object-like types
            var mergedFields = new Dictionary<string, TypeInfo>();
            var optionalFields = new HashSet<string>();
            var requiredInAny = new HashSet<string>(); // Track if property is required in any type
            var nonObjectTypes = new List<TypeInfo>();

            foreach (var type in types)
            {
                Dictionary<string, TypeInfo>? fields = type switch
                {
                    TypeInfo.Record r => r.Fields,
                    TypeInfo.Interface i => i.Members,
                    TypeInfo.Class c => c.DeclaredFieldTypes,
                    TypeInfo.Instance inst => inst.ClassType switch
                    {
                        TypeInfo.Class c => c.DeclaredFieldTypes,
                        _ => null
                    },
                    _ => null
                };

                HashSet<string>? optionals = type switch
                {
                    TypeInfo.Interface i => i.OptionalMemberSet,
                    _ => null
                };

                if (fields == null || fields.Count == 0)
                {
                    // For classes/instances without explicit field types, keep as non-object type
                    // so the intersection is preserved
                    if (type is TypeInfo.Class || type is TypeInfo.Instance)
                    {
                        nonObjectTypes.Add(type);
                    }
                    else if (fields == null)
                    {
                        nonObjectTypes.Add(type);
                    }
                    continue;
                }

                foreach (var (name, fieldType) in fields)
                {
                    bool isOptionalInThisType = optionals?.Contains(name) ?? false;

                    if (mergedFields.TryGetValue(name, out var existingType))
                    {
                        // Check for property type conflict
                        if (!IsCompatible(existingType, fieldType) && !IsCompatible(fieldType, existingType))
                        {
                            // Conflicting types - property becomes never
                            mergedFields[name] = new TypeInfo.Never();
                        }
                        // If compatible, keep the more specific type (or the first one)

                        // If required in any type, mark as required
                        if (!isOptionalInThisType)
                        {
                            requiredInAny.Add(name);
                        }
                    }
                    else
                    {
                        mergedFields[name] = fieldType;
                        // Initially mark optional if optional in this type
                        if (isOptionalInThisType)
                        {
                            optionalFields.Add(name);
                        }
                        else
                        {
                            requiredInAny.Add(name);
                        }
                    }
                }
            }

            // A property is optional in the intersection only if it's optional in ALL types that have it
            // (or if it only appears in types where it's optional)
            optionalFields.ExceptWith(requiredInAny);

            // If all types were object-like, return merged interface (to preserve optional info)
            if (nonObjectTypes.Count == 0)
            {
                // Use Interface if we have optional fields, otherwise Record
                if (optionalFields.Count > 0)
                {
                    return new TypeInfo.Interface("", mergedFields, optionalFields);
                }
                return new TypeInfo.Record(mergedFields);
            }

            // Otherwise, return intersection with merged record/interface
            var resultTypes = new List<TypeInfo>(nonObjectTypes) { new TypeInfo.Record(mergedFields) };
            return new TypeInfo.Intersection(resultTypes);
        }

        // Return intersection for other cases (e.g., class instances)
        return new TypeInfo.Intersection(types);
    }

    private TypeInfo ParseParenthesizedType(string typeName)
    {
        int depth = 0;
        int closeIndex = -1;
        for (int i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] == '(') depth++;
            else if (typeName[i] == ')') { depth--; if (depth == 0) { closeIndex = i; break; } }
        }

        string inner = typeName[1..closeIndex];
        string suffix = typeName[(closeIndex + 1)..];

        TypeInfo result = ToTypeInfo(inner);
        while (suffix.StartsWith("[]")) { result = new TypeInfo.Array(result); suffix = suffix[2..]; }
        return result;
    }

    private TypeInfo ParseFunctionTypeInfo(string funcType)
    {
        // Parse "(param1, param2) => returnType"
        var arrowIdx = funcType.IndexOf("=>");
        var paramsSection = funcType.Substring(0, arrowIdx).Trim();
        var returnTypeStr = funcType.Substring(arrowIdx + 2).Trim();

        // Remove surrounding parentheses
        if (paramsSection.StartsWith("(") && paramsSection.EndsWith(")"))
        {
            paramsSection = paramsSection.Substring(1, paramsSection.Length - 2);
        }

        List<TypeInfo> paramTypes = [];
        if (!string.IsNullOrWhiteSpace(paramsSection))
        {
            foreach (var param in paramsSection.Split(','))
            {
                paramTypes.Add(ToTypeInfo(param.Trim()));
            }
        }

        TypeInfo returnType = ToTypeInfo(returnTypeStr);
        return new TypeInfo.Function(paramTypes, returnType);
    }

    private TypeInfo ParseTupleTypeInfo(string tupleStr)
    {
        string inner = tupleStr[1..^1].Trim(); // Remove [ and ]
        if (string.IsNullOrEmpty(inner))
            return new TypeInfo.Tuple([], 0, null);

        var elements = SplitTupleElements(inner);
        List<TypeInfo> elementTypes = [];
        int requiredCount = 0;
        bool seenOptional = false;
        TypeInfo? restType = null;

        for (int i = 0; i < elements.Count; i++)
        {
            string elem = elements[i].Trim();

            // Rest element: ...type[]
            if (elem.StartsWith("..."))
            {
                if (i != elements.Count - 1)
                    throw new Exception("Type Error: Rest element must be last in tuple type.");
                string arrayType = elem[3..];
                if (!arrayType.EndsWith("[]"))
                    throw new Exception("Type Error: Rest element must be an array type.");
                restType = ToTypeInfo(arrayType[..^2]);
                break;
            }

            // Optional element: type?
            bool isOptional = elem.EndsWith("?");
            if (isOptional)
            {
                elem = elem[..^1];
                seenOptional = true;
            }
            else if (seenOptional)
            {
                throw new Exception("Type Error: Required element cannot follow optional element in tuple.");
            }

            elementTypes.Add(ToTypeInfo(elem));
            if (!isOptional) requiredCount++;
        }

        return new TypeInfo.Tuple(elementTypes, requiredCount, restType);
    }

    private List<string> SplitTupleElements(string inner)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(' || c == '[' || c == '<') depth++;
            else if (c == ')' || c == ']' || c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }
        parts.Add(inner[start..]);
        return parts;
    }

    /// <summary>
    /// Parses inline object type strings like "{ x: number; y?: string }".
    /// </summary>
    private TypeInfo ParseInlineObjectTypeInfo(string objStr)
    {
        // Remove "{ " and " }" from the string
        string inner = objStr[2..^2].Trim();
        if (string.IsNullOrEmpty(inner))
            return new TypeInfo.Record(new Dictionary<string, TypeInfo>());

        var fields = new Dictionary<string, TypeInfo>();
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        // Split by semicolon (the separator used in ParseInlineObjectType)
        var members = SplitObjectMembers(inner);

        foreach (var member in members)
        {
            string m = member.Trim();
            if (string.IsNullOrEmpty(m)) continue;

            // Check for index signature: [string]: type, [number]: type, [symbol]: type
            if (m.StartsWith("["))
            {
                int bracketEnd = m.IndexOf(']');
                if (bracketEnd > 0)
                {
                    string keyType = m[1..bracketEnd].Trim();
                    int colonIdx = m.IndexOf(':', bracketEnd);
                    if (colonIdx > 0)
                    {
                        string valueType = m[(colonIdx + 1)..].Trim();
                        TypeInfo valueTypeInfo = ToTypeInfo(valueType);

                        switch (keyType)
                        {
                            case "string":
                                stringIndexType = valueTypeInfo;
                                break;
                            case "number":
                                numberIndexType = valueTypeInfo;
                                break;
                            case "symbol":
                                symbolIndexType = valueTypeInfo;
                                break;
                        }
                        continue;
                    }
                }
            }

            // Find the colon separator (property name: type)
            int regularColonIdx = m.IndexOf(':');
            if (regularColonIdx < 0) continue;

            string propName = m[..regularColonIdx].Trim();
            string propType = m[(regularColonIdx + 1)..].Trim();

            // Check for optional marker (?) and remove it
            if (propName.EndsWith("?"))
            {
                propName = propName[..^1].Trim();
            }

            fields[propName] = ToTypeInfo(propType);
        }

        return new TypeInfo.Record(fields, stringIndexType, numberIndexType, symbolIndexType);
    }

    private List<string> SplitObjectMembers(string inner)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '(' || c == '[' || c == '<' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '>' || c == '}') depth--;
            else if (c == ';' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }
        if (start < inner.Length)
            parts.Add(inner[start..]);
        return parts;
    }
}