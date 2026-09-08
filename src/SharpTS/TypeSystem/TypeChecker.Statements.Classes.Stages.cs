using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // Creates the signature environment. Temporary activation is scoped here and restored
    // before returning; superclass resolution retains the original generic parameter scope.
    private (TypeEnvironment Environment, List<TypeInfo.TypeParameter>? Parameters, TypeInfo? Superclass) CreateClassTypeEnvironment(
        Stmt.Class classStmt)
    {
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
        return (classTypeEnv, classTypeParams, superclass);
    }

    // Records source-order diagnostics without aborting the subsequent class stages.
    private void CheckClassSuperclassDeclarationOrder(Stmt.Class classStmt)
    {
        // `class C1 extends C2 {} class C2 {}` — a class value referenced in its own or an earlier
        // class's extends clause, before that class is textually declared, is TS2449 (class values
        // aren't hoisted for this position). Recorded (not thrown) so the body is still checked.
        if (classStmt.SuperclassExpr is Expr.Variable baseVar
            && _classDeclarationLines.TryGetValue(baseVar.Name.Lexeme, out int baseLine)
            && baseLine > classStmt.Name.Line)
        {
            RecordTypeError(new TypeCheckException(
                $"Class '{baseVar.Name.Lexeme}' used before its declaration.",
                line: classStmt.Name.Line, tsCode: "TS2449"));
        }
    }

    // Called within the declaration-owned class type environment; updates only index metadata.
    private void CollectClassIndexSignatures(Stmt.Class classStmt, TypeInfo.MutableClass mutableClass)
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
    }

    // Publishes the initial frozen class in the active outer environment and TypeMap.
    // Returns the non-generic class shape used for body checking.
    private TypeInfo.Class PublishInitialClassType(
        Stmt.Class classStmt,
        TypeInfo.MutableClass mutableClass,
        List<TypeInfo.TypeParameter>? classTypeParams)
    {
        // Freeze the mutable class and create GenericClass or regular Class based on type parameters.
        // Any TypeInfo.Instance created during signature collection (wrapping the MutableClass)
        // will now resolve via ResolvedClassType to the frozen class.
        TypeInfo.Class classTypeForBody;
        if (classTypeParams != null && classTypeParams.Count > 0)
        {
            var genericClassType = mutableClass.FreezeGeneric(classTypeParams);
            _environment.Define(classStmt.Name.Lexeme, genericClassType);
            _environment.DefineType(classStmt.Name.Lexeme, genericClassType);
            // For body check, freeze the mutable class (methods/fields have TypeParameter types)
            classTypeForBody = mutableClass.Freeze();
            _typeMap.SetClassType(classStmt.Name.Lexeme, classTypeForBody);
        }
        else
        {
            // Freeze the mutable class into an immutable class type
            TypeInfo.Class classType = mutableClass.Freeze();
            _environment.Define(classStmt.Name.Lexeme, classType);
            _environment.DefineType(classStmt.Name.Lexeme, classType);
            _typeMap.SetClassType(classStmt.Name.Lexeme, classType);
            classTypeForBody = classType;
        }
        return classTypeForBody;
    }

    // Runs in the outer declaration environment before other member checks.
    // Keeps interface resolution, source diagnostics, and generic deferral in their original order.
    private void ValidateDeclaredClassInterfaces(
        Stmt.Class classStmt,
        TypeInfo.Class classTypeForBody,
        List<TypeInfo.TypeParameter>? classTypeParams)
    {
        // Validate implemented interfaces (skip for generic classes - validated at instantiation)
        if (classStmt.Interfaces != null && classTypeParams == null)
        {
            for (int i = 0; i < classStmt.Interfaces.Count; i++)
            {
                var interfaceToken = classStmt.Interfaces[i];
                TypeInfo? itfTypeInfo = interfaceToken.Lexeme.Contains('.', StringComparison.Ordinal)
                    ? ResolveTypeName(interfaceToken.Lexeme)
                    : _environment.GetTypeBinding(interfaceToken.Lexeme)
                        ?? ResolveAnnotation(interfaceToken.Lexeme, null);

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
                else if (itfTypeInfo is TypeInfo.Record record &&
                         (typeArgs == null || typeArgs.Count == 0))
                {
                    interfaceType = RecordAsInterfaceBase(interfaceToken.Lexeme, record);
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
                else if (itfTypeInfo is null or TypeInfo.Any &&
                         TryResolveIterableProtocolInterface(interfaceToken.Lexeme, typeArgs, out var protocolType, typeArgNodes))
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
    }

    // Runs after named-interface validation. Generic index-interface lookup temporarily
    // activates classTypeEnv with EnvironmentScope; all other validation uses the outer environment.
    private void ValidateDeclaredClassMembers(
        Stmt.Class classStmt,
        TypeInfo.Class classTypeForBody,
        List<TypeInfo.TypeParameter>? classTypeParams,
        TypeEnvironment classTypeEnv)
    {
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

        ValidateDeclaredClassInterfaceIndexes(classStmt, classTypeForBody, classTypeEnv);
    }

    // Owns temporary generic-interface resolution scope and restores it on every exit.
    private void ValidateDeclaredClassInterfaceIndexes(
        Stmt.Class classStmt,
        TypeInfo.Class classTypeForBody,
        TypeEnvironment classTypeEnv)
    {
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
                    TypeInfo? itfTypeInfo = _environment.GetTypeBinding(interfaceToken.Lexeme);
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
    }

    // Checks static initializers in the outer declaration environment. Returns whether
    // mutableClass acquired inferred fields and therefore needs republishing.
    private bool CheckClassStaticFieldInitializers(
        Stmt.Class classStmt,
        TypeInfo.Class classTypeForBody,
        TypeInfo.MutableClass mutableClass)
    {
        bool anyInferredFieldTypeResolved = false;

        // Second pass: check static property initializers at class scope
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic && field.Initializer != null)
            {
                TypeInfo initType = CheckExpr(field.Initializer);
                // For ES2022 private static fields, look in StaticPrivateFieldTypes
                TypeInfo staticFieldDeclaredType = field.IsPrivate
                    ? classTypeForBody.StaticPrivateFieldTypes[GetFieldMemberName(field)]
                    : classTypeForBody.StaticProperties[GetFieldMemberName(field)];
                if (field.TypeAnnotation is null)
                {
                    TypeInfo inferredFieldType = WidenLiteralType(initType);
                    if (field.IsPrivate)
                        mutableClass.StaticPrivateFields[GetFieldMemberName(field)] = inferredFieldType;
                    else
                        mutableClass.StaticProperties[GetFieldMemberName(field)] = inferredFieldType;
                    anyInferredFieldTypeResolved = true;
                    continue;
                }
                if (!IsCompatible(staticFieldDeclaredType, initType))
                {
                    throw new TypeCheckException($" Cannot assign type '{initType}' to static property '{field.Name.Lexeme}' of type '{staticFieldDeclaredType}'.", tsCode: "TS2322");
                }
            }
        }
        return anyInferredFieldTypeResolved;
    }

    // Checks initializers at declaration scope before static blocks, preserving diagnostic order.
    private void CheckClassAutoAccessorInitializers(Stmt.Class classStmt, TypeInfo.Class classTypeForBody)
    {
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
    }

    // Creates but does not activate the body environment. The declaration owns activation
    // and restoration of both _environment and _currentClass around all body stages.
    private TypeEnvironment CreateClassBodyEnvironment(
        Stmt.Class classStmt,
        TypeInfo.Class classTypeForBody,
        List<TypeInfo.TypeParameter>? classTypeParams,
        TypeInfo? superclass)
    {
        // Third pass: body check
        TypeEnvironment classEnv = new(_environment);
        // For generic classes, add type parameters to class scope
        if (classTypeParams != null)
        {
            for (int i = 0; i < classTypeParams.Count; i++)
            {
                if (classStmt.TypeParams is { } declarations &&
                    i < declarations.Count)
                {
                    DefineSourceTypeParameter(
                        classEnv,
                        declarations[i],
                        classTypeParams[i]);
                }
            }
        }
        classEnv.Define("this", new TypeInfo.Instance(classTypeForBody));
        if (superclass != null)
        {
            classEnv.Define("super", superclass);
        }

        return classEnv;
    }

    // Called only after body scope restoration and strict initialization checks.
    // Owns conditional refreezing, outer-environment publication, and compatibility cache invalidation.
    private void RepublishInferredClassType(
        Stmt.Class classStmt,
        TypeInfo.MutableClass mutableClass,
        List<TypeInfo.TypeParameter>? classTypeParams,
        bool anyInferredMethodReturnResolved,
        bool anyInferredFieldTypeResolved)
    {
        // Publish method return types inferred during the body pass. The class was frozen with
        // <inferred> placeholders before the body could be checked, so the frozen Class/GenericClass
        // that call sites read (and the TypeMap the compiler reads) still hold the placeholder for
        // every un-annotated method. Rebuild the frozen forms from the now-resolved mutable state and
        // re-register them. Without this, `new C().m()` on an inferred method reads <inferred> (~any):
        // ordinary methods silently skip assignability checks and a generator method's result is
        // rejected as non-iterable by spread/for...of/yield* (#658/#661). `_environment` here is the
        // outer scope the class was originally defined in (the body pass restored it above).
        if (anyInferredMethodReturnResolved || anyInferredFieldTypeResolved)
        {
            mutableClass.ResetFrozenCache();
            if (classTypeParams != null && classTypeParams.Count > 0)
            {
                var frozen = mutableClass.FreezeGeneric(classTypeParams);
                _environment.Define(classStmt.Name.Lexeme, frozen);
                _environment.DefineType(classStmt.Name.Lexeme, frozen);
                _typeMap.SetClassType(classStmt.Name.Lexeme, mutableClass.Freeze());
            }
            else
            {
                var refrozen = mutableClass.Freeze();
                _environment.Define(classStmt.Name.Lexeme, refrozen);
                _environment.DefineType(classStmt.Name.Lexeme, refrozen);
                _typeMap.SetClassType(classStmt.Name.Lexeme, refrozen);
            }
            // Structural compatibility results cache on CacheKey() (carries the stable DeclarationId),
            // so any comparison made against the placeholder during the body pass must not be reused.
            _compatibilityCache = null;
            _identityCompatibilityCache = null;

            ValidateInferredClassInterfaceMembers(classStmt, mutableClass);
        }
    }

    // Revalidates computed methods after inferred types have been republished.
    // Uses the restored outer environment and preserves TS2416 source locations.
    private void ValidateInferredClassInterfaceMembers(Stmt.Class classStmt, TypeInfo.MutableClass mutableClass)
    {
        // Revalidate inferred computed methods against the corresponding implemented
        // interface member. The initial implements pass necessarily saw an <inferred>
        // placeholder; tsc reports the resolved mismatch on the member itself as TS2416.
        if (classStmt.Interfaces != null)
        {
            TypeInfo.Class resolvedClass = mutableClass.Freeze();
            foreach (var method in classStmt.Methods.Where(candidate =>
                         candidate.ComputedKey != null && candidate.Body != null))
            {
                string? memberName = TryGetWellKnownSymbolMemberName(method.ComputedKey);
                if (memberName == null
                    || !resolvedClass.Methods.TryGetValue(memberName, out var actualMember))
                    continue;

                foreach (var interfaceToken in classStmt.Interfaces)
                {
                    if (_environment.GetTypeBinding(interfaceToken.Lexeme) is TypeInfo.Interface implemented
                        && implemented.Members.TryGetValue(memberName, out var expectedMember)
                        && !IsCompatible(expectedMember, actualMember))
                    {
                        RecordTypeError(new TypeCheckException(
                            $"Property '[Symbol.{memberName[2..]}]' in type '{classStmt.Name.Lexeme}' is not assignable to the same property in base type '{implemented.Name}'.",
                            line: TryGetExprLine(method.ComputedKey!), tsCode: "TS2416"));
                    }
                }
            }
        }
    }
}
