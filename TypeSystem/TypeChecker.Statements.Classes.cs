using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Class declaration type checking - handles class statements including methods, fields, accessors, and generics.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Maps a class computed method/accessor key expression to the canonical well-known-symbol member
    /// name the type system uses (<c>Symbol.iterator</c> → <c>@@iterator</c>,
    /// <c>Symbol.asyncIterator</c> → <c>@@asyncIterator</c>, …) — matching the <c>@@</c> convention the
    /// parser already applies to symbol-keyed members in type/interface positions
    /// (see <c>Parser.Types.cs</c>). Returns null for keys that aren't a static <c>Symbol.&lt;name&gt;</c>
    /// access (an arbitrary computed key, e.g. <c>[myVar]()</c>, isn't modeled as a named member —
    /// it parses and runs but carries no static member type, mirroring computed accessors).
    /// </summary>
    private static string? TryGetWellKnownSymbolMemberName(Expr? key) => key switch
    {
        Expr.Get { Object: Expr.Variable { Name.Lexeme: "Symbol" }, Name.Lexeme: var n } => "@@" + n,
        _ => null
    };

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

        // Handle generic type parameters first — the extends clause may reference them
        // (`class D<T> extends B<T>`). Resolving the superclass before they are in scope
        // collapses those references to `any`, silently dropping the base's parameterization
        // (its inherited members and index signature then lose the type parameter).
        List<TypeInfo.TypeParameter>? classTypeParams = null;
        TypeEnvironment classTypeEnv = new(_environment);
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            // Multi-pass so constraints that reference a later parameter resolve
            // (`class D<T extends U, U>` — U is declared after T). Mirrors generic functions/arrows.
            // BuildGenericTypeParameters resolves constraints against the active environment, so
            // make classTypeEnv current (where the sibling parameters are defined) for the call.
            using (new EnvironmentScope(this, classTypeEnv))
                classTypeParams = BuildGenericTypeParameters(classStmt.TypeParams, classTypeEnv);
        }

        // Resolve the superclass with the class's own type parameters in scope so a base
        // reference like `extends A<T>` keeps T as the open parameter.
        TypeInfo? superclass;
        using (new EnvironmentScope(this, classTypeEnv))
            superclass = ResolveDeclaredSuperclass(classStmt);

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

        // Index signatures: [key: string|number|symbol]: ValueType. Resolved inside the class
        // type-environment scope so value types can reference the class's own type parameters.
        if (classStmt.IndexSignatures != null)
        {
            foreach (var indexSig in classStmt.IndexSignatures)
            {
                TypeInfo valueType = ResolveAnnotation(indexSig.ValueType, indexSig.ValueTypeNode)!;
                switch (indexSig.KeyType)
                {
                    case TokenType.TYPE_STRING: mutableClass.StringIndexType = valueType; break;
                    case TokenType.TYPE_NUMBER: mutableClass.NumberIndexType = valueType; break;
                    case TokenType.TYPE_SYMBOL: mutableClass.SymbolIndexType = valueType; break;
                }
            }
        }

        // Helper to build a TypeInfo.Function from a method declaration
        TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
        {
            var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
                method.Parameters,
                validateDefaults: true,
                contextName: $"method '{method.Name.Lexeme}'"
            );

            TypeInfo returnType = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode)
                ?? new TypeInfo.Inferred();

            // Wrap return type for generator/async generator methods (skip when inferring)
            if (method.ReturnType != null && method.IsGenerator)
            {
                if (method.IsAsync && returnType is not TypeInfo.AsyncGenerator)
                {
                    returnType = new TypeInfo.AsyncGenerator(returnType);
                }
                else if (!method.IsAsync && returnType is not TypeInfo.Generator)
                {
                    returnType = new TypeInfo.Generator(returnType);
                }
            }

            return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, null, paramNames);
        }

        // Computed symbol-keyed methods (`[Symbol.iterator]() {...}`) are modeled under their canonical
        // well-known-symbol member name (@@iterator, @@asyncIterator, …) so structural iterability and
        // member lookup see them (#592/#485). They carry the synthetic `<computed>` name, so they are
        // pulled out of the name-keyed overload grouping below. Arbitrary computed keys (e.g. `[v]()`)
        // aren't statically named members and are skipped (still parsed/checked, just untyped — like
        // computed accessors).
        foreach (var method in classStmt.Methods.Where(m => m.ComputedKey != null && m.Body != null))
        {
            if (TryGetWellKnownSymbolMemberName(method.ComputedKey) is not { } memberName)
                continue;
            // Build the factory signature WITHOUT the generator-return wrapping BuildMethodFuncType
            // applies: a [Symbol.iterator]()/[Symbol.asyncIterator]() factory must return the iterator
            // type itself (e.g. `Iterator<number>` — what for...of consumes and reads its element from),
            // not Generator<Iterator<number>>. An un-annotated generator factory stays <inferred> here
            // and is filled in by the inferred-return post-pass below as Generator<yieldType>.
            var (cParamTypes, cRequired, cHasRest, cParamNames) = BuildFunctionSignature(
                method.Parameters, validateDefaults: true, contextName: $"method '{memberName}'");
            TypeInfo factoryReturn = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode) ?? new TypeInfo.Inferred();
            var funcType = new TypeInfo.Function(cParamTypes, factoryReturn, cRequired, cHasRest, null, cParamNames);
            if (method.IsStatic)
                mutableClass.StaticMethods[memberName] = funcType;
            else
                mutableClass.Methods[memberName] = funcType;
            (method.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[memberName] = method.Access;
        }

        // First pass: collect signatures, grouping overloads
        // Group methods by name to detect overloads (computed-key methods handled above)
        var methodGroups = classStmt.Methods.Where(m => m.ComputedKey == null).GroupBy(m => (m.IsStatic, Name: m.Name.Lexeme)).ToList();

        foreach (var group in methodGroups)
        {
            string methodName = group.Key.Name;
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
                    throw new TypeCheckException($" Cannot have multiple abstract declarations for method '{methodName}'.", tsCode: "TS2516");
                }
                var abstractMethod = abstractDecls[0];
                var funcType = BuildMethodFuncType(abstractMethod);

                if (abstractMethod.IsStatic)
                    mutableClass.StaticMethods[methodName] = funcType;
                else
                    mutableClass.Methods[methodName] = funcType;

                (abstractMethod.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = abstractMethod.Access;
                mutableClass.AbstractMethods.Add(methodName);
                continue;
            }

            // Handle overloaded methods
            if (signatures.Count > 0)
            {
                if (implementations.Count == 0)
                {
                    throw new TypeCheckException($" Overloaded method '{methodName}' has no implementation.", tsCode: "TS2391");
                }
                if (implementations.Count > 1)
                {
                    throw new TypeCheckException($" Overloaded method '{methodName}' has multiple implementations.", tsCode: "TS2393");
                }

                var implementation = implementations[0];
                var signatureTypes = signatures.Select(BuildMethodFuncType).ToList();
                var implType = BuildMethodFuncType(implementation);

                // Validate implementation is compatible with all signatures
                foreach (var sig in signatureTypes)
                {
                    if (implType.MinArity > sig.MinArity)
                    {
                        throw new TypeCheckException($" Implementation of '{methodName}' requires {implType.MinArity} arguments but overload signature requires only {sig.MinArity}.", tsCode: "TS2394");
                    }
                }

                var overloadedFunc = new TypeInfo.OverloadedFunction(signatureTypes, implType);

                if (implementation.IsStatic)
                    mutableClass.StaticMethods[methodName] = overloadedFunc;
                else
                    mutableClass.Methods[methodName] = overloadedFunc;

                (implementation.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = implementation.Access;
            }
            else if (implementations.Count == 1)
            {
                // Single non-overloaded method
                var method = implementations[0];
                var funcType = BuildMethodFuncType(method);

                // Handle ES2022 private methods (#method)
                if (method.IsPrivate)
                {
                    if (method.IsStatic)
                        mutableClass.StaticPrivateMethods[methodName] = funcType;
                    else
                        mutableClass.PrivateMethods[methodName] = funcType;
                }
                else
                {
                    if (method.IsStatic)
                        mutableClass.StaticMethods[methodName] = funcType;
                    else
                        mutableClass.Methods[methodName] = funcType;

                    (method.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = method.Access;
                }
            }
            else if (implementations.Count > 1)
            {
                throw new TypeCheckException($" Multiple implementations of method '{methodName}' without overload signatures.", tsCode: "TS2393");
            }
        }

        // Collect static property types, field access modifiers, and non-static field types
        foreach (var field in classStmt.Fields)
        {
            // Check field decorators
            DecoratorTarget fieldTarget = field.IsStatic ? DecoratorTarget.StaticField : DecoratorTarget.Field;
            CheckDecorators(field.Decorators, fieldTarget);

            string fieldName = field.Name.Lexeme;
            TypeInfo fieldType = ResolveAnnotation(field.TypeAnnotation, field.TypeAnnotationNode)
                ?? new TypeInfo.Any();

            // Handle ES2022 private fields (#field)
            if (field.IsPrivate)
            {
                if (field.IsStatic)
                {
                    mutableClass.StaticPrivateFields[fieldName] = fieldType;
                }
                else
                {
                    mutableClass.PrivateFields[fieldName] = fieldType;
                }
            }
            else if (field.IsStatic)
            {
                mutableClass.StaticProperties[fieldName] = fieldType;
            }
            else
            {
                mutableClass.FieldTypes[fieldName] = fieldType;
            }
            if (!field.IsPrivate)
            {
                (field.IsStatic ? mutableClass.StaticFieldAccess : mutableClass.FieldAccess)[fieldName] = field.Access;
            }
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

                // Computed accessor names (get [Symbol.x]()) have no static member
                // name to register; their bodies are still checked later.
                if (accessor.ComputedKey != null)
                {
                    continue;
                }

                string propName = accessor.Name.Lexeme;

                if (accessor.Kind.Type == TokenType.GET)
                {
                    TypeInfo getterRetType = accessor.ReturnType != null
                        ? ResolveAnnotation(accessor.ReturnType, accessor.ReturnTypeNode)!
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
                        ? ResolveAnnotation(accessor.SetterParam.Type, accessor.SetterParam.TypeAnnotationNode)!
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
                    throw new TypeCheckException($" Getter and setter for '{propName}' have incompatible types.", tsCode: "TS2380");
                }
            }
        }

        // Collect auto-accessor types (TypeScript 4.9+)
        if (classStmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in classStmt.AutoAccessors)
            {
                // Check auto-accessor decorators (Stage 3: kind = "accessor")
                DecoratorTarget accessorTarget = DecoratorTarget.Getter; // Use Getter target for auto-accessors
                CheckDecorators(autoAccessor.Decorators, accessorTarget);

                string propName = autoAccessor.Name.Lexeme;

                // Determine the type from annotation or initializer
                TypeInfo accessorType;
                if (autoAccessor.TypeAnnotation != null)
                {
                    accessorType = ResolveAnnotation(autoAccessor.TypeAnnotation, autoAccessor.TypeAnnotationNode)!;
                }
                else if (autoAccessor.Initializer != null)
                {
                    accessorType = CheckExpr(autoAccessor.Initializer);
                }
                else
                {
                    accessorType = new TypeInfo.Any();
                }

                // Register as getter (always available)
                mutableClass.Getters[propName] = accessorType;

                // Register as setter (unless readonly)
                if (!autoAccessor.IsReadonly)
                {
                    mutableClass.Setters[propName] = accessorType;
                }

                // Track as auto-accessor for decorator context
                mutableClass.AutoAccessors.Add(propName);

                // Validate override if specified
                if (autoAccessor.IsOverride)
                {
                    if (superclass == null)
                    {
                        throw new TypeCheckException($" Cannot use 'override' for auto-accessor '{propName}' in a class that does not extend another class.", tsCode: "TS4112");
                    }

                    // Check if parent has a matching getter
                    bool parentHasGetter = false;
                    TypeInfo? currentSuperclass = superclass;
                    while (currentSuperclass != null)
                    {
                        if (currentSuperclass is TypeInfo.Class sc && sc.Getters.ContainsKey(propName))
                        {
                            parentHasGetter = true;
                            break;
                        }
                        currentSuperclass = currentSuperclass switch
                        {
                            TypeInfo.Class c => c.Superclass,
                            TypeInfo.InstantiatedGeneric ig when ig.GenericDefinition is TypeInfo.GenericClass gc => gc.Superclass,
                            _ => null
                        };
                    }

                    if (!parentHasGetter)
                    {
                        throw new TypeCheckException($" Auto-accessor '{propName}' uses 'override' but parent class has no accessor with this name.", tsCode: "TS4113");
                    }
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
            var genericClassType = mutableClass.FreezeGeneric(classTypeParams);
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
            for (int i = 0; i < classStmt.Interfaces.Count; i++)
            {
                var interfaceToken = classStmt.Interfaces[i];
                TypeInfo? itfTypeInfo = _environment.Get(interfaceToken.Lexeme);

                // Get type arguments for this interface if provided
                List<string>? typeArgs = classStmt.InterfaceTypeArgs != null && i < classStmt.InterfaceTypeArgs.Count
                    ? classStmt.InterfaceTypeArgs[i]
                    : null;
                List<TypeNode?>? typeArgNodes = classStmt.InterfaceTypeArgNodes != null && i < classStmt.InterfaceTypeArgNodes.Count
                    ? classStmt.InterfaceTypeArgNodes[i]
                    : null;

                TypeInfo.Interface? interfaceType = null;

                if (itfTypeInfo is TypeInfo.Interface plainInterface && (typeArgs == null || typeArgs.Count == 0))
                {
                    // Non-generic interface
                    interfaceType = plainInterface;
                }
                else if (itfTypeInfo is TypeInfo.GenericInterface genericInterface)
                {
                    // Generic interface - need to instantiate it
                    if (typeArgs == null || typeArgs.Count == 0)
                    {
                        throw new TypeCheckException($" Generic interface '{interfaceToken.Lexeme}' requires type arguments.", tsCode: "TS2314");
                    }

                    // Resolve type arguments
                    var resolvedTypeArgs = typeArgs.Select((_, j) => ResolveTypeArg(typeArgs, typeArgNodes, j)).ToList();

                    // Instantiate the generic interface
                    var instantiated = InstantiateGenericInterface(genericInterface, resolvedTypeArgs);

                    // For validation, we need the concrete interface members
                    // Create a substitution map and substitute members
                    Dictionary<string, TypeInfo> substitutions = [];
                    for (int j = 0; j < genericInterface.TypeParams.Count && j < resolvedTypeArgs.Count; j++)
                    {
                        substitutions[genericInterface.TypeParams[j].Name] = resolvedTypeArgs[j];
                    }

                    var substitutedMembers = genericInterface.Members.ToDictionary(
                        m => m.Key,
                        m => Substitute(m.Value, substitutions)).ToFrozenDictionary();

                    interfaceType = new TypeInfo.Interface(
                        genericInterface.Name,
                        substitutedMembers,
                        genericInterface.OptionalMembers);
                }
                else if (itfTypeInfo == null && TryResolveIterableProtocolInterface(interfaceToken.Lexeme, typeArgs, out var protocolType, typeArgNodes))
                {
                    // Built-in iterable-protocol interface (Iterable<T>, AsyncIterable<T>, …) — not a
                    // user-declared interface, so validate the class structurally implements it (#756).
                    ValidateProtocolInterfaceImplementation(classTypeForBody, protocolType, classStmt.Name.Lexeme);
                    continue;
                }
                else
                {
                    throw new TypeCheckException($" '{interfaceToken.Lexeme}' is not an interface.", tsCode: "TS2304");
                }

                ValidateInterfaceImplementation(classTypeForBody, interfaceType, classStmt.Name.Lexeme, classStmt.Name.Line);
            }
        }

        // Validate abstract member implementation (skip for generic classes - validated at instantiation)
        if (!classStmt.IsAbstract && classTypeParams == null)
        {
            ValidateAbstractMemberImplementation(classTypeForBody, classStmt.Name.Lexeme);
        }

        // The 'override' keyword check (TS4113) is skipped for generic classes (validated at
        // instantiation); it isn't needed for the generic member-compatibility cases below.
        if (classTypeParams == null)
        {
            ValidateOverrideMembers(classStmt, classTypeForBody);
        }

        // Member-override compatibility (TS2416) runs for generic classes too: a base member typed
        // as an open type parameter can't be satisfied by a concrete (or differently-constrained)
        // override. ValidateClassExtends substitutes the base's type arguments so the comparison is
        // correct under generics.
        ValidateClassExtends(classStmt, classTypeForBody);

        // Index-signature override compatibility (TS2415) runs for generic classes too: the
        // base index can resolve to an open type parameter, which a concrete derived index
        // can't satisfy. Independent of the per-property checks above, so not gated on type params.
        ValidateClassIndexSignatureExtends(classStmt, classTypeForBody);

        // Property-vs-own-index-signature compatibility (TS2411) runs for generic classes too: a
        // declared property must be assignable to the class's own string index type (which may be
        // an open type parameter).
        ValidateClassPropertiesAgainstIndex(classStmt, classTypeForBody);

        // Index-signature compatibility against implemented interfaces (TS2420). Like the TS2415
        // extends check above (and unlike the named-member `implements` validation gated on
        // classTypeParams == null), this runs for generic classes too — the interface index can
        // resolve to an open type parameter that a concrete class index can't satisfy (#897).
        if (classStmt.Interfaces != null &&
            (classTypeForBody.StringIndexType != null || classTypeForBody.NumberIndexType != null))
        {
            // Resolve interface references with the class's own type parameters in scope so
            // `implements A<T>` keeps T as the open parameter (mirrors superclass resolution).
            using (new EnvironmentScope(this, classTypeEnv))
            {
                for (int i = 0; i < classStmt.Interfaces.Count; i++)
                {
                    var interfaceToken = classStmt.Interfaces[i];
                    TypeInfo? itfTypeInfo = _environment.Get(interfaceToken.Lexeme);
                    List<string>? typeArgs = classStmt.InterfaceTypeArgs != null && i < classStmt.InterfaceTypeArgs.Count
                        ? classStmt.InterfaceTypeArgs[i]
                        : null;
                    List<TypeNode?>? typeArgNodes = classStmt.InterfaceTypeArgNodes != null && i < classStmt.InterfaceTypeArgNodes.Count
                        ? classStmt.InterfaceTypeArgNodes[i]
                        : null;

                    // Construct the instantiation directly (rather than InstantiateGenericInterface)
                    // to avoid its constraint-validation throw aborting this check.
                    TypeInfo? resolvedInterface = itfTypeInfo switch
                    {
                        TypeInfo.GenericInterface gi when typeArgs is { Count: > 0 } =>
                            new TypeInfo.InstantiatedGeneric(gi, typeArgs.Select((_, j) => ResolveTypeArg(typeArgs, typeArgNodes, j)).ToList()),
                        TypeInfo.Interface plain => plain,
                        _ => null,
                    };
                    if (resolvedInterface != null)
                        ValidateInterfaceIndexSignatureImplementation(classStmt, classTypeForBody, resolvedInterface);
                }
            }
        }

        // Second pass: check static property initializers at class scope
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic && field.Initializer != null)
            {
                TypeInfo initType = CheckExpr(field.Initializer);
                // For ES2022 private static fields, look in StaticPrivateFieldTypes
                TypeInfo staticFieldDeclaredType = field.IsPrivate
                    ? classTypeForBody.StaticPrivateFieldTypes[field.Name.Lexeme]
                    : classTypeForBody.StaticProperties[field.Name.Lexeme];
                if (!IsCompatible(staticFieldDeclaredType, initType))
                {
                    throw new TypeCheckException($" Cannot assign type '{initType}' to static property '{field.Name.Lexeme}' of type '{staticFieldDeclaredType}'.", tsCode: "TS2322");
                }
            }
        }

        // Check auto-accessor initializers
        if (classStmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in classStmt.AutoAccessors)
            {
                if (autoAccessor.Initializer != null)
                {
                    TypeInfo initType = CheckExpr(autoAccessor.Initializer);
                    TypeInfo declaredType = classTypeForBody.Getters[autoAccessor.Name.Lexeme];
                    if (!IsCompatible(declaredType, initType))
                    {
                        throw new TypeCheckException($" Cannot assign type '{initType}' to auto-accessor '{autoAccessor.Name.Lexeme}' of type '{declaredType}'.", tsCode: "TS2322");
                    }
                }
            }
        }

        // Type-check static blocks
        if (classStmt.StaticInitializers != null)
        {
            // Create environment with static class context ('this' refers to the class constructor)
            var staticBlockEnv = new TypeEnvironment(_environment);
            staticBlockEnv.Define("this", classTypeForBody);

            foreach (var initializer in classStmt.StaticInitializers)
            {
                if (initializer is Stmt.StaticBlock block)
                {
                    using var _ = new EnvironmentScope(this, staticBlockEnv);
                    bool previousInStaticBlock = _inStaticBlock;
                    bool previousInStaticMethod = _inStaticMethod;
                    var previousClass = _currentClass;
                    _inStaticBlock = true;
                    _inStaticMethod = true;
                    _currentClass = classTypeForBody;

                    try
                    {
                        foreach (var stmt in block.Body)
                        {
                            CheckStmt(stmt);
                        }
                    }
                    finally
                    {
                        _inStaticBlock = previousInStaticBlock;
                        _inStaticMethod = previousInStaticMethod;
                        _currentClass = previousClass;
                    }
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

        // Set when a method's inferred (un-annotated) return type is resolved during the body
        // pass below. The class was frozen with <inferred> placeholders before this pass, so
        // when this is true the frozen class is rebuilt and re-published afterwards (#658/#661).
        bool anyInferredMethodReturnResolved = false;

        try
        {
            // Check instance field initializers (e.g. `x = 5`, `r = () => { ... }`) within the
            // instance context so `this` and the class's type parameters resolve inside them. Static
            // fields are checked separately at class scope. Inferred/Any field types skip the
            // assignability check but the initializer is still type-checked.
            foreach (var field in classStmt.Fields)
            {
                if (field.IsStatic || field.Initializer == null) continue;
                TypeInfo initType = CheckExpr(field.Initializer);
                var declaredTypes = field.IsPrivate ? classTypeForBody.PrivateFieldTypes : classTypeForBody.FieldTypes;
                if (declaredTypes.TryGetValue(field.Name.Lexeme, out var fieldDeclaredType)
                    && fieldDeclaredType is not (TypeInfo.Inferred or TypeInfo.Any)
                    && !IsCompatible(fieldDeclaredType, initType))
                {
                    throw new TypeCheckException($" Cannot assign type '{initType}' to field '{field.Name.Lexeme}' of type '{fieldDeclaredType}'.", tsCode: "TS2322");
                }
            }

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
                // For ES2022 private methods, look in PrivateMethodTypes/StaticPrivateMethodTypes
                TypeInfo declaredMethodType;
                if (method.ComputedKey != null)
                {
                    // Computed symbol-keyed methods carry the `<computed>` name, so they aren't keyed
                    // in the method dictionaries by a static name. Well-known ones are stored under their
                    // @@name; reuse that type. An arbitrary computed key has no modeled member — build a
                    // signature inline just to bind parameter types for the body check.
                    string? memberName = TryGetWellKnownSymbolMemberName(method.ComputedKey);
                    var computedDict = method.IsStatic ? classTypeForBody.StaticMethods : classTypeForBody.Methods;
                    if (memberName != null && computedDict.TryGetValue(memberName, out var computedType))
                    {
                        declaredMethodType = computedType;
                    }
                    else
                    {
                        var (cParamTypes, cRequired, cHasRest, cParamNames) = BuildFunctionSignature(
                            method.Parameters, validateDefaults: true, contextName: "computed method");
                        TypeInfo cReturn = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode) ?? new TypeInfo.Inferred();
                        declaredMethodType = new TypeInfo.Function(cParamTypes, cReturn, cRequired, cHasRest, null, cParamNames);
                    }
                }
                else if (method.IsPrivate)
                {
                    declaredMethodType = method.IsStatic
                        ? classTypeForBody.StaticPrivateMethodTypes[method.Name.Lexeme]
                        : classTypeForBody.PrivateMethodTypes[method.Name.Lexeme];
                }
                else
                {
                    declaredMethodType = method.IsStatic
                        ? classTypeForBody.StaticMethods[method.Name.Lexeme]
                        : classTypeForBody.Methods[method.Name.Lexeme];
                }

                // Get the actual function type (implementation for overloads)
                TypeInfo.Function methodType = declaredMethodType switch
                {
                    TypeInfo.OverloadedFunction of => of.Implementation,
                    TypeInfo.Function f => f,
                    // SharpTS-only: internal invariant
                    _ => throw new TypeCheckException($" Unexpected method type for '{method.Name.Lexeme}'.")
                };

                for (int i = 0; i < method.Parameters.Count; i++)
                {
                    methodEnv.Define(method.Parameters[i].Name.Lexeme, methodType.ParamTypes[i]);
                }

                // Save and set context - method bodies are isolated from outer loop/switch/label context
                TypeEnvironment previousEnvFunc = _environment;
                TypeInfo? previousReturnFunc = _currentFunctionReturnType;
                var previousInferredFunc = _inferredReturnTypes;
                var previousInferredYieldFunc = _inferredYieldTypes;
                bool previousInStatic = _inStaticMethod;
                bool previousInAsyncFunc = _inAsyncFunction;
                bool previousInGeneratorFunc = _inGeneratorFunction;
                int previousLoopDepthFunc = _loopDepth;
                int previousSwitchDepthFunc = _switchDepth;
                var previousActiveLabelsFunc = new Dictionary<string, bool>(_activeLabels);

                bool inferringMethodReturn = methodType.ReturnType is TypeInfo.Inferred;
                _environment = methodEnv;
                if (inferringMethodReturn)
                {
                    _inferredReturnTypes = new List<TypeInfo>();
                    _currentFunctionReturnType = new TypeInfo.Inferred();
                }
                else
                {
                    _currentFunctionReturnType = methodType.ReturnType;
                }
                // Collect yield operand types only while inferring a generator method's type (#548).
                _inferredYieldTypes = inferringMethodReturn && method.IsGenerator ? new List<TypeInfo>() : null;
                _inStaticMethod = method.IsStatic;
                _inAsyncFunction = method.IsAsync;
                _inGeneratorFunction = method.IsGenerator;
                _loopDepth = 0;
                _switchDepth = 0;
                _activeLabels.Clear();

                // Isolate the narrowing context for this method body so narrowings
                // from `if (x) return;` don't leak into sibling methods/accessors.
                PushEmptyNarrowingScope();

                try
                {
                    // Abstract methods have no body to check
                    if (method.Body != null)
                    {
                        CheckStmtList(method.Body);

                        // #367/#372: object-slot number/boolean-typed locals, parameters, and returns
                        // that an `any`/`undefined` value may have left holding the sentinel, so they
                        // are not coerced to NaN/false.
                        MarkUndefinedReachableNumericSlots(method.Body, method.Parameters);
                    }

                    // Resolve inferred method return type
                    if (inferringMethodReturn && method.Body != null)
                    {
                        var collected = _inferredReturnTypes!;
                        _inferredReturnTypes = null;

                        TypeInfo inferredReturn;
                        if (collected.Count == 0)
                        {
                            inferredReturn = new TypeInfo.Void();
                        }
                        else
                        {
                            var distinct = collected.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                            inferredReturn = CollapseOrCreateUnion(distinct);
                        }

                        // A generator method's type argument is its YIELD type (#548), not the
                        // `return`-derived inferredReturn; a non-generator async method wraps in Promise.
                        // The resolved type is re-published below (anyInferredMethodReturnResolved), so
                        // `new C().m()` reads the real Generator<…>/Promise<…> at the call site rather than
                        // the `<inferred>` placeholder (#658/#661; the generator-method face was #687).
                        if (method.IsGenerator)
                            inferredReturn = BuildInferredGeneratorType(_inferredYieldTypes!, method.IsAsync);
                        else if (method.IsAsync && inferredReturn is not TypeInfo.Void)
                            inferredReturn = new TypeInfo.Promise(inferredReturn);

                        // Update the method type in the class. Computed symbol-keyed methods are keyed
                        // by their @@name (e.g. @@iterator), not the synthetic `<computed>` lexeme; an
                        // arbitrary computed key (no well-known @@name) carries no static member to update.
                        var updatedMethodType = new TypeInfo.Function(methodType.ParamTypes, inferredReturn, methodType.RequiredParams, methodType.HasRestParam, methodType.ThisType, methodType.ParamNames);
                        string? mName = method.ComputedKey != null
                            ? TryGetWellKnownSymbolMemberName(method.ComputedKey)
                            : method.Name.Lexeme;
                        if (mName != null)
                        {
                            if (method.IsPrivate)
                            {
                                if (method.IsStatic) mutableClass.StaticPrivateMethods[mName] = updatedMethodType;
                                else mutableClass.PrivateMethods[mName] = updatedMethodType;
                            }
                            else
                            {
                                if (method.IsStatic) mutableClass.StaticMethods[mName] = updatedMethodType;
                                else mutableClass.Methods[mName] = updatedMethodType;
                            }
                            // The frozen class call sites read still holds the <inferred> placeholder for
                            // this method; flag a rebuild after the body pass (#658/#661).
                            anyInferredMethodReturnResolved = true;
                        }
                    }
                }
                finally
                {
                    PopNarrowingScope();
                    _environment = previousEnvFunc;
                    _currentFunctionReturnType = previousReturnFunc;
                    _inferredReturnTypes = previousInferredFunc;
                    _inferredYieldTypes = previousInferredYieldFunc;
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
                        // Computed-name accessors aren't registered in Getters/Setters
                        // (no static member name); derive types from the declaration.
                        accessorReturnType = accessor.ComputedKey != null
                            ? (accessor.ReturnType != null ? ResolveAnnotation(accessor.ReturnType, accessor.ReturnTypeNode)! : new TypeInfo.Any())
                            : classTypeForBody.Getters[accessor.Name.Lexeme];
                    }
                    else
                    {
                        // Setter has void return type
                        accessorReturnType = new TypeInfo.Void();
                        // Add setter parameter to environment
                        if (accessor.SetterParam != null)
                        {
                            TypeInfo setterParamType = accessor.ComputedKey != null
                                ? (accessor.SetterParam.Type != null ? ResolveAnnotation(accessor.SetterParam.Type, accessor.SetterParam.TypeAnnotationNode)! : new TypeInfo.Any())
                                : classTypeForBody.Setters[accessor.Name.Lexeme];
                            accessorEnv.Define(accessor.SetterParam.Name.Lexeme, setterParamType);
                        }
                    }

                    // Save and set context - accessor bodies are isolated from outer loop/switch/label context
                    TypeEnvironment previousEnvAcc = _environment;
                    TypeInfo? previousReturnAcc = _currentFunctionReturnType;
                    int previousLoopDepthAcc = _loopDepth;
                    int previousSwitchDepthAcc = _switchDepth;
                    var previousActiveLabelsAcc = new Dictionary<string, bool>(_activeLabels);
                    bool previousInStaticAcc = _inStaticMethod;

                    _environment = accessorEnv;
                    _currentFunctionReturnType = accessorReturnType;
                    _loopDepth = 0;
                    _switchDepth = 0;
                    _activeLabels.Clear();
                    // Per JS spec, `this` inside a static accessor is the class constructor,
                    // enabling patterns like `static get ANY(): Range { return new this("any"); }`.
                    _inStaticMethod = accessor.IsStatic;

                    // Isolate narrowing context so that narrowings don't leak between
                    // accessors or into sibling methods.
                    PushEmptyNarrowingScope();

                    try
                    {
                        CheckStmtList(accessor.Body);

                        // #367/#372: object-slot number/boolean-typed locals and returns that may hold
                        // the undefined sentinel. The setter parameter always uses an object slot, so
                        // it never corrupts and is not passed.
                        MarkUndefinedReachableNumericSlots(accessor.Body);
                    }
                    finally
                    {
                        PopNarrowingScope();
                        _environment = previousEnvAcc;
                        _currentFunctionReturnType = previousReturnAcc;
                        _loopDepth = previousLoopDepthAcc;
                        _switchDepth = previousSwitchDepthAcc;
                        _activeLabels.Clear();
                        foreach (var kvp in previousActiveLabelsAcc)
                            _activeLabels[kvp.Key] = kvp.Value;
                        _inStaticMethod = previousInStaticAcc;
                    }
                }
            }
        }
        finally
        {
            _environment = prevEnv;
            _currentClass = prevClass;
        }

        // Publish method return types inferred during the body pass. The class was frozen with
        // <inferred> placeholders before the body could be checked, so the frozen Class/GenericClass
        // that call sites read (and the TypeMap the compiler reads) still hold the placeholder for
        // every un-annotated method. Rebuild the frozen forms from the now-resolved mutable state and
        // re-register them. Without this, `new C().m()` on an inferred method reads <inferred> (~any):
        // ordinary methods silently skip assignability checks and a generator method's result is
        // rejected as non-iterable by spread/for...of/yield* (#658/#661). `_environment` here is the
        // outer scope the class was originally defined in (the body pass restored it above).
        if (anyInferredMethodReturnResolved)
        {
            mutableClass.ResetFrozenCache();
            if (classTypeParams != null && classTypeParams.Count > 0)
            {
                _environment.Define(classStmt.Name.Lexeme, mutableClass.FreezeGeneric(classTypeParams));
                _typeMap.SetClassType(classStmt.Name.Lexeme, mutableClass.Freeze());
            }
            else
            {
                var refrozen = mutableClass.Freeze();
                _environment.Define(classStmt.Name.Lexeme, refrozen);
                _typeMap.SetClassType(classStmt.Name.Lexeme, refrozen);
            }
            // Structural compatibility results cache on CacheKey() (carries the stable DeclarationId),
            // so any comparison made against the placeholder during the body pass must not be reused.
            _compatibilityCache = null;
            _identityCompatibilityCache = null;
        }
    }

    /// <summary>
    /// Resolves a class declaration's <c>extends</c> clause to its superclass <see cref="TypeInfo"/>:
    /// generic instantiation (<c>extends Box&lt;number&gt;</c>), a plain class/instance base, or an
    /// Any-typed global base (e.g. <c>Error</c>) via a constructor placeholder. Returns <c>null</c>
    /// when there is no <c>extends</c> clause. Shared by <see cref="CheckClassDeclaration"/> and
    /// <see cref="CheckDeclareClass"/> so the two paths cannot drift and ambient
    /// (<c>declare class</c>/<c>@DotNetType</c>) declarations inherit superclass members too (#505).
    /// Resolved against the enclosing environment (the class's own type parameters are not in scope,
    /// matching the regular path).
    /// </summary>
    private TypeInfo? ResolveDeclaredSuperclass(Stmt.Class classStmt)
    {
        if (classStmt.SuperclassExpr == null) return null;

        TypeInfo superType = CheckExpr(classStmt.SuperclassExpr);
        TypeInfo? superclass = null;

        // Handle generic class with type arguments: extends Box<number>
        if (classStmt.SuperclassTypeArgs != null && classStmt.SuperclassTypeArgs.Count > 0)
        {
            if (superType is TypeInfo.GenericClass gc)
            {
                // Resolve type arguments (node-first with string fallback)
                var typeArgs = classStmt.SuperclassTypeArgs
                    .Select((_, i) => ResolveTypeArg(classStmt.SuperclassTypeArgs, classStmt.SuperclassTypeArgNodes, i)).ToList();
                // Instantiate the generic class with the type arguments. A type-argument constraint
                // violation here (TS2344) is recorded at the class name's line and instantiation
                // continues with the offending argument, so the rest of this class (its index-sig
                // TS2415 check) and the sibling declarations still get checked (#895).
                int? savedExtendsLine = _extendsClauseConstraintLine;
                _extendsClauseConstraintLine = classStmt.Name.Line;
                try { superclass = InstantiateGenericClass(gc, typeArgs); }
                finally { _extendsClauseConstraintLine = savedExtendsLine; }
            }
            else if (superType is TypeInfo.Any)
            {
                // Built-in generic globals (Promise<T>, Array<T>) resolve to
                // Any in value position; `extends Promise<T>` must not be
                // rejected as "non-generic" (#233). Validate the type args
                // resolve, then fall through to the Any placeholder below.
                for (int i = 0; i < classStmt.SuperclassTypeArgs.Count; i++)
                    ResolveTypeArg(classStmt.SuperclassTypeArgs, classStmt.SuperclassTypeArgNodes, i);
            }
            else
            {
                throw new TypeCheckException($"Cannot use type arguments with non-generic class '{Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)}'", tsCode: "TS2315");
            }
        }
        if (superclass == null)
        {
            if (superType is TypeInfo.Instance si && si.ClassType is TypeInfo.Class sic)
                superclass = sic;
            else if (superType is TypeInfo.Class sc)
                superclass = sc;
            else if (superType is TypeInfo.GenericClass gc2)
            {
                // Generic class without type arguments - error
                throw new TypeCheckException($"Generic class '{gc2.Name}' requires type arguments", tsCode: "TS2314");
            }
            else if (superType is TypeInfo.Any)
            {
                // Allow extending Any-typed globals (e.g. Error, TypeError,
                // Promise<T>, Array). Create a placeholder class so super()
                // calls and constructor validation type-check correctly
                // (accept any number of args).
                var leafName = Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!;
                var placeholder = new TypeInfo.MutableClass(leafName);
                placeholder.Methods["constructor"] = new TypeInfo.Function(
                    [new TypeInfo.Any()], new TypeInfo.Void(), RequiredParams: 0, HasRestParam: true);
                // When the base is a built-in iterable (`class C extends Array<number>`), record its
                // element type as an @@iterator member. The global resolves to Any in value position so
                // the type argument is otherwise dropped; recovering it lets an instance's for...of /
                // yield* / spread bind the real element type, and lets a genuinely non-iterable instance
                // be told apart from an iterable one (#593).
                if (TryGetBuiltInIterableElement(leafName, classStmt.SuperclassTypeArgs, classStmt.SuperclassTypeArgNodes, out var iterableElement))
                {
                    placeholder.Methods["@@iterator"] = new TypeInfo.Function(
                        [], new TypeInfo.Iterator(iterableElement), RequiredParams: 0, HasRestParam: false);
                }
                superclass = placeholder;
            }
            else
                throw new TypeCheckException("Superclass must be a class", tsCode: "TS2507");
        }
        return superclass;
    }

    /// <summary>
    /// Element type of a built-in iterable used as a superclass (<c>class C extends Array&lt;number&gt;</c>),
    /// recovered from the <c>extends</c> type-argument names that <see cref="ResolveDeclaredSuperclass"/>
    /// otherwise drops (the global resolves to <c>Any</c> in value position). Array/Set/typed-array element
    /// is the sole argument; Map yields the <c>[K, V]</c> tuple; String yields <c>string</c>; the bigint
    /// typed arrays yield <c>bigint</c> and the rest <c>number</c>. Returns <c>false</c> for a non-iterable
    /// base (Error, Date, Promise, WeakMap, …), so such a subclass instance stays correctly non-iterable
    /// (#593). A missing/omitted type argument degrades to <c>any</c>, matching <c>class C extends Array</c>.
    /// </summary>
    private bool TryGetBuiltInIterableElement(string baseName, List<string>? typeArgs, List<TypeNode?>? typeArgNodes, out TypeInfo element)
    {
        TypeInfo Arg(int i) => typeArgs != null && i < typeArgs.Count ? ResolveTypeArg(typeArgs, typeArgNodes, i) : new TypeInfo.Any();
        switch (baseName)
        {
            case "Array" or "ReadonlyArray" or "Set" or "ReadonlySet":
                element = Arg(0);
                return true;
            case "Map" or "ReadonlyMap":
                element = TypeInfo.Tuple.FromTypes([Arg(0), Arg(1)], 2);
                return true;
            case "String":
                element = new TypeInfo.String();
                return true;
            case var n when BuiltInNames.IsTypedArrayName(n):
                element = n is BuiltInNames.BigInt64Array or BuiltInNames.BigUint64Array
                    ? new TypeInfo.BigInt()
                    : new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
                return true;
            default:
                element = null!;
                return false;
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

        // Resolve the `extends` clause in the enclosing environment (before the class's own type
        // parameters enter scope), so inherited members are visible to consumers of this ambient
        // type (member access, structural assignability, conditional `infer`) — #505.
        TypeInfo? superclass = ResolveDeclaredSuperclass(classStmt);

        // Handle generic type parameters
        TypeEnvironment classTypeEnv = new(_environment);
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            foreach (var tp in classStmt.TypeParams)
            {
                TypeInfo? constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
                TypeInfo? defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                classTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }
        }

        // Create mutable class early so self-references in method return types work.
        // This allows methods like "fromSeconds(): TimeSpan" to correctly resolve the return type.
        // The mutable class is populated during signature collection and frozen at the end.
        var mutableClass = new TypeInfo.MutableClass(classStmt.Name.Lexeme)
        {
            Superclass = superclass,
            IsAbstract = classStmt.IsAbstract
        };
        classTypeEnv.Define(classStmt.Name.Lexeme, mutableClass);

        using (new EnvironmentScope(this, classTypeEnv))
        {

        // Index signatures: [key: string|number|symbol]: ValueType. Resolved inside the class
        // type-environment scope so value types can reference the class's own type parameters.
        if (classStmt.IndexSignatures != null)
        {
            foreach (var indexSig in classStmt.IndexSignatures)
            {
                TypeInfo valueType = ResolveAnnotation(indexSig.ValueType, indexSig.ValueTypeNode)!;
                switch (indexSig.KeyType)
                {
                    case TokenType.TYPE_STRING: mutableClass.StringIndexType = valueType; break;
                    case TokenType.TYPE_NUMBER: mutableClass.NumberIndexType = valueType; break;
                    case TokenType.TYPE_SYMBOL: mutableClass.SymbolIndexType = valueType; break;
                }
            }
        }

        // Helper to build a TypeInfo.Function from a method declaration
        TypeInfo.Function BuildMethodFuncType(Stmt.Function method)
        {
            var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
                method.Parameters,
                validateDefaults: true,
                contextName: $"method '{method.Name.Lexeme}'"
            );

            TypeInfo returnType = ResolveAnnotation(method.ReturnType, method.ReturnTypeNode)
                ?? new TypeInfo.Inferred();

            // Wrap return type for generator/async generator methods (skip when inferring)
            if (method.ReturnType != null && method.IsGenerator)
            {
                if (method.IsAsync && returnType is not TypeInfo.AsyncGenerator)
                {
                    returnType = new TypeInfo.AsyncGenerator(returnType);
                }
                else if (!method.IsAsync && returnType is not TypeInfo.Generator)
                {
                    returnType = new TypeInfo.Generator(returnType);
                }
            }

            return new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, null, paramNames);
        }

        // Collect method signatures (all methods in declare class are treated as signatures)
        // Constructor is included as a method named "constructor"
        var methodGroups = classStmt.Methods.GroupBy(m => (m.IsStatic, Name: m.Name.Lexeme)).ToList();

        foreach (var group in methodGroups)
        {
            string methodName = group.Key.Name;
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

                (method.IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = method.Access;
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

                (methods[0].IsStatic ? mutableClass.StaticMethodAccess : mutableClass.MethodAccess)[methodName] = methods[0].Access;
            }
        }

        // Collect field types
        foreach (var field in classStmt.Fields)
        {
            TypeInfo fieldType = ResolveAnnotation(field.TypeAnnotation, field.TypeAnnotationNode)
                ?? new TypeInfo.Any();

            if (field.IsStatic)
            {
                mutableClass.StaticProperties[field.Name.Lexeme] = fieldType;
            }
            else
            {
                mutableClass.FieldTypes[field.Name.Lexeme] = fieldType;
            }

            (field.IsStatic ? mutableClass.StaticFieldAccess : mutableClass.FieldAccess)[field.Name.Lexeme] = field.Access;
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
                // Computed accessor names have no static member name to register.
                if (accessor.ComputedKey != null)
                {
                    continue;
                }

                TypeInfo accessorType = accessor.ReturnType != null
                    ? ResolveAnnotation(accessor.ReturnType, accessor.ReturnTypeNode)!
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
