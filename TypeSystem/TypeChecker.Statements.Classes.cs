using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Class declaration type checking - handles class statements including methods, fields, accessors, and generics.
/// </summary>
public partial class TypeChecker
{
    private void CheckClassDeclaration(Stmt.Class classStmt)
    {
        // Check class decorators
        CheckDecorators(classStmt.Decorators, DecoratorTarget.Class);

        // For declare classes (ambient declarations), skip body checking
        // These are external type declarations (e.g., @DotNetType)
        if (classStmt.IsDeclare)
        {
            CheckDeclareClass(classStmt);
            return;
        }

        TypeInfo.Class? superclass = null;
        if (classStmt.Superclass != null)
        {
            TypeInfo superType = LookupVariable(classStmt.Superclass);
            if (superType is TypeInfo.Instance si && si.ClassType is TypeInfo.Class sic)
                superclass = sic;
            else if (superType is TypeInfo.Class sc)
                superclass = sc;
            else
                throw new TypeCheckException("Superclass must be a class");
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

        // Create mutable class early so self-references in method return types work.
        // This allows methods like "next(): Node" to correctly resolve the return type.
        // The mutable class is populated during signature collection and frozen at the end.
        var mutableClass = new TypeInfo.MutableClass(classStmt.Name.Lexeme)
        {
            Superclass = superclass,
            IsAbstract = classStmt.IsAbstract
        };
        classTypeEnv.Define(classStmt.Name.Lexeme, mutableClass);

        using (new EnvironmentScope(this, classTypeEnv))
        {

        // Helper to build a TypeInfo.Function from a method declaration
        TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
        {
            var (paramTypes, requiredParams, hasRest) = BuildFunctionSignature(
                method.Parameters,
                validateDefaults: true,
                contextName: $"method '{method.Name.Lexeme}'"
            );

            TypeInfo returnType = method.ReturnType != null
                ? ToTypeInfo(method.ReturnType)
                : new TypeInfo.Void();

            return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest);
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
                    throw new TypeCheckException($" Cannot have multiple abstract declarations for method '{methodName}'.");
                }
                var abstractMethod = abstractDecls[0];
                var funcType = BuildMethodFuncType(abstractMethod);

                if (abstractMethod.IsStatic)
                    mutableClass.StaticMethods[methodName] = funcType;
                else
                    mutableClass.Methods[methodName] = funcType;

                mutableClass.MethodAccess[methodName] = abstractMethod.Access;
                mutableClass.AbstractMethods.Add(methodName);
                continue;
            }

            // Handle overloaded methods
            if (signatures.Count > 0)
            {
                if (implementations.Count == 0)
                {
                    throw new TypeCheckException($" Overloaded method '{methodName}' has no implementation.");
                }
                if (implementations.Count > 1)
                {
                    throw new TypeCheckException($" Overloaded method '{methodName}' has multiple implementations.");
                }

                var implementation = implementations[0];
                var signatureTypes = signatures.Select(BuildMethodFuncType).ToList();
                var implType = BuildMethodFuncType(implementation);

                // Validate implementation is compatible with all signatures
                foreach (var sig in signatureTypes)
                {
                    if (implType.MinArity > sig.MinArity)
                    {
                        throw new TypeCheckException($" Implementation of '{methodName}' requires {implType.MinArity} arguments but overload signature requires only {sig.MinArity}.");
                    }
                }

                var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, implType);

                if (implementation.IsStatic)
                    mutableClass.StaticMethods[methodName] = overloadedFunc;
                else
                    mutableClass.Methods[methodName] = overloadedFunc;

                mutableClass.MethodAccess[methodName] = implementation.Access;
            }
            else if (implementations.Count == 1)
            {
                // Single non-overloaded method
                var method = implementations[0];
                var funcType = BuildMethodFuncType(method);

                if (method.IsStatic)
                    mutableClass.StaticMethods[methodName] = funcType;
                else
                    mutableClass.Methods[methodName] = funcType;

                mutableClass.MethodAccess[methodName] = method.Access;
            }
            else if (implementations.Count > 1)
            {
                throw new TypeCheckException($" Multiple implementations of method '{methodName}' without overload signatures.");
            }
        }

        // Collect static property types, field access modifiers, and non-static field types
        foreach (var field in classStmt.Fields)
        {
            // Check field decorators
            DecoratorTarget fieldTarget = field.IsStatic ? DecoratorTarget.StaticField : DecoratorTarget.Field;
            CheckDecorators(field.Decorators, fieldTarget);

            string fieldName = field.Name.Lexeme;
            TypeInfo fieldType = field.TypeAnnotation != null
                ? ToTypeInfo(field.TypeAnnotation)
                : new TypeInfo.Any();

            if (field.IsStatic)
            {
                mutableClass.StaticProperties[fieldName] = fieldType;
            }
            else
            {
                mutableClass.FieldTypes[fieldName] = fieldType;
            }
            mutableClass.FieldAccess[fieldName] = field.Access;
            if (field.IsReadonly)
            {
                mutableClass.ReadonlyFields.Add(fieldName);
            }
        }

        // Collect accessor types
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                // Check accessor decorators
                DecoratorTarget accessorTarget = accessor.Kind.Type == TokenType.GET
                    ? DecoratorTarget.Getter
                    : DecoratorTarget.Setter;
                CheckDecorators(accessor.Decorators, accessorTarget);

                string propName = accessor.Name.Lexeme;

                if (accessor.Kind.Type == TokenType.GET)
                {
                    TypeInfo getterRetType = accessor.ReturnType != null
                        ? ToTypeInfo(accessor.ReturnType)
                        : new TypeInfo.Any();
                    mutableClass.Getters[propName] = getterRetType;

                    // Track abstract getters
                    if (accessor.IsAbstract)
                    {
                        mutableClass.AbstractGetters.Add(propName);
                    }
                }
                else // SET
                {
                    TypeInfo paramType = accessor.SetterParam?.Type != null
                        ? ToTypeInfo(accessor.SetterParam.Type)
                        : new TypeInfo.Any();
                    mutableClass.Setters[propName] = paramType;

                    // Track abstract setters
                    if (accessor.IsAbstract)
                    {
                        mutableClass.AbstractSetters.Add(propName);
                    }
                }
            }

            // Validate that getter/setter pairs have matching types
            foreach (var propName in mutableClass.Getters.Keys.Intersect(mutableClass.Setters.Keys))
            {
                if (!IsCompatible(mutableClass.Getters[propName], mutableClass.Setters[propName]))
                {
                    throw new TypeCheckException($" Getter and setter for '{propName}' have incompatible types.");
                }
            }
        }
        }

        // Freeze the mutable class and create GenericClass or regular Class based on type parameters.
        // Any TypeInfo.Instance created during signature collection (wrapping the MutableClass)
        // will now resolve via ResolvedClassType to the frozen class.
        TypeInfo.Class classTypeForBody;
        if (classTypeParams != null && classTypeParams.Count > 0)
        {
            var genericClassType = new TypeInfo.GenericClass(
                classStmt.Name.Lexeme,
                classTypeParams,
                superclass,
                mutableClass.Methods.ToFrozenDictionary(),
                mutableClass.StaticMethods.ToFrozenDictionary(),
                mutableClass.StaticProperties.ToFrozenDictionary(),
                mutableClass.MethodAccess.ToFrozenDictionary(),
                mutableClass.FieldAccess.ToFrozenDictionary(),
                mutableClass.ReadonlyFields.ToFrozenSet(),
                mutableClass.Getters.ToFrozenDictionary(),
                mutableClass.Setters.ToFrozenDictionary(),
                mutableClass.FieldTypes.ToFrozenDictionary(),
                classStmt.IsAbstract,
                mutableClass.AbstractMethods.Count > 0 ? mutableClass.AbstractMethods.ToFrozenSet() : null,
                mutableClass.AbstractGetters.Count > 0 ? mutableClass.AbstractGetters.ToFrozenSet() : null,
                mutableClass.AbstractSetters.Count > 0 ? mutableClass.AbstractSetters.ToFrozenSet() : null
            );
            _environment.Define(classStmt.Name.Lexeme, genericClassType);
            // For body check, freeze the mutable class (methods/fields have TypeParameter types)
            classTypeForBody = mutableClass.Freeze();
            _typeMap.SetClassType(classStmt.Name.Lexeme, classTypeForBody);
        }
        else
        {
            // Freeze the mutable class into an immutable class type
            TypeInfo.Class classType = mutableClass.Freeze();
            _environment.Define(classStmt.Name.Lexeme, classType);
            _typeMap.SetClassType(classStmt.Name.Lexeme, classType);
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
                    throw new TypeCheckException($" '{interfaceToken.Lexeme}' is not an interface.");
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
                TypeInfo staticFieldDeclaredType = classTypeForBody.StaticProperties[field.Name.Lexeme];
                if (!IsCompatible(staticFieldDeclaredType, initType))
                {
                    throw new TypeCheckException($" Cannot assign type '{initType}' to static property '{field.Name.Lexeme}' of type '{staticFieldDeclaredType}'.");
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
                // Check method decorators
                DecoratorTarget methodTarget = method.IsStatic ? DecoratorTarget.StaticMethod : DecoratorTarget.Method;
                CheckDecorators(method.Decorators, methodTarget);

                // Check parameter decorators
                foreach (var param in method.Parameters)
                {
                    CheckDecorators(param.Decorators, DecoratorTarget.Parameter);
                }

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
                    ? classTypeForBody.StaticMethods[method.Name.Lexeme]
                    : classTypeForBody.Methods[method.Name.Lexeme];

                // Get the actual function type (implementation for overloads)
                TypeInfo.Function methodType = declaredMethodType switch
                {
                    TypeInfo.OverloadedFunction of => of.Implementation,
                    TypeInfo.Function f => f,
                    _ => throw new TypeCheckException($" Unexpected method type for '{method.Name.Lexeme}'.")
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
                bool previousInGeneratorFunc = _inGeneratorFunction;
                int previousLoopDepthFunc = _loopDepth;
                int previousSwitchDepthFunc = _switchDepth;
                var previousActiveLabelsFunc = new Dictionary<string, bool>(_activeLabels);

                _environment = methodEnv;
                _currentFunctionReturnType = methodType.ReturnType;
                _inStaticMethod = method.IsStatic;
                _inAsyncFunction = method.IsAsync;
                _inGeneratorFunction = method.IsGenerator;
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
                    _inGeneratorFunction = previousInGeneratorFunc;
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
                        accessorReturnType = classTypeForBody.Getters[accessor.Name.Lexeme];
                    }
                    else
                    {
                        // Setter has void return type
                        accessorReturnType = new TypeInfo.Void();
                        // Add setter parameter to environment
                        if (accessor.SetterParam != null)
                        {
                            TypeInfo setterParamType = classTypeForBody.Setters[accessor.Name.Lexeme];
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
    }

    /// <summary>
    /// Type checks a declare class (ambient declaration).
    /// For declare classes, we only validate signatures without requiring implementations.
    /// Used for @DotNetType external type declarations.
    /// </summary>
    private void CheckDeclareClass(Stmt.Class classStmt)
    {
        // Save reference to current environment for later registration
        TypeEnvironment parentEnv = _environment;

        // Handle generic type parameters
        TypeEnvironment classTypeEnv = new(_environment);
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            foreach (var tp in classStmt.TypeParams)
            {
                TypeInfo? constraint = tp.Constraint != null ? ToTypeInfo(tp.Constraint) : null;
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint);
                classTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Create mutable class early so self-references in method return types work.
        // This allows methods like "fromSeconds(): TimeSpan" to correctly resolve the return type.
        // The mutable class is populated during signature collection and frozen at the end.
        var mutableClass = new TypeInfo.MutableClass(classStmt.Name.Lexeme)
        {
            IsAbstract = classStmt.IsAbstract
        };
        classTypeEnv.Define(classStmt.Name.Lexeme, mutableClass);

        using (new EnvironmentScope(this, classTypeEnv))
        {

        // Helper to build a TypeInfo.Function from a method declaration
        TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
        {
            var (paramTypes, requiredParams, hasRest) = BuildFunctionSignature(
                method.Parameters,
                validateDefaults: true,
                contextName: $"method '{method.Name.Lexeme}'"
            );

            TypeInfo returnType = method.ReturnType != null
                ? ToTypeInfo(method.ReturnType)
                : new TypeInfo.Void();

            return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest);
        }

        // Collect method signatures (all methods in declare class are treated as signatures)
        // Constructor is included as a method named "constructor"
        var methodGroups = classStmt.Methods.GroupBy(m => m.Name.Lexeme).ToList();

        foreach (var group in methodGroups)
        {
            string methodName = group.Key;
            var methods = group.ToList();

            if (methods.Count == 1)
            {
                // Single method declaration
                var method = methods[0];
                var funcType = BuildMethodFuncType(method);

                if (method.IsStatic)
                    mutableClass.StaticMethods[methodName] = funcType;
                else
                    mutableClass.Methods[methodName] = funcType;

                mutableClass.MethodAccess[methodName] = method.Access;
            }
            else
            {
                // Multiple overloaded signatures - create OverloadedFunction
                var signatureTypes = methods.Select(BuildMethodFuncType).ToList();
                var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, signatureTypes[0]);

                if (methods[0].IsStatic)
                    mutableClass.StaticMethods[methodName] = overloadedFunc;
                else
                    mutableClass.Methods[methodName] = overloadedFunc;

                mutableClass.MethodAccess[methodName] = methods[0].Access;
            }
        }

        // Collect field types
        foreach (var field in classStmt.Fields)
        {
            TypeInfo fieldType = field.TypeAnnotation != null
                ? ToTypeInfo(field.TypeAnnotation)
                : new TypeInfo.Any();

            if (field.IsStatic)
            {
                mutableClass.StaticProperties[field.Name.Lexeme] = fieldType;
            }
            else
            {
                mutableClass.FieldTypes[field.Name.Lexeme] = fieldType;
            }

            mutableClass.FieldAccess[field.Name.Lexeme] = field.Access;
            if (field.IsReadonly)
            {
                mutableClass.ReadonlyFields.Add(field.Name.Lexeme);
            }
        }

        // Collect getter/setter types from accessors (if any)
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                TypeInfo accessorType = accessor.ReturnType != null
                    ? ToTypeInfo(accessor.ReturnType)
                    : new TypeInfo.Any();

                if (accessor.Kind.Type == TokenType.GET)
                {
                    mutableClass.Getters[accessor.Name.Lexeme] = accessorType;
                }
                else
                {
                    mutableClass.Setters[accessor.Name.Lexeme] = accessorType;
                }
            }
        }

        // Freeze the mutable class into an immutable class type.
        // Any TypeInfo.Instance that was created during signature collection
        // (wrapping the MutableClass) will now resolve via ResolvedClassType.
        TypeInfo.Class classType = mutableClass.Freeze();

        // Register class in parent environment (not the classTypeEnv)
        // This ensures the class is visible after the using block ends
        parentEnv.Define(classStmt.Name.Lexeme, classType);
        _typeMap.SetClassType(classStmt.Name.Lexeme, classType);

        } // End EnvironmentScope
    }
}
