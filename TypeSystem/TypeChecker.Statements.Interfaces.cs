using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Interface declaration type checking - handles interface statements including members and index signatures.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Pre-registers an interface declaration before function hoisting.
    /// This creates a basic interface type without full validation so that
    /// function signatures can reference the interface name.
    /// Full validation happens later in CheckInterfaceDeclaration.
    /// </summary>
    private void PreRegisterInterface(Stmt.Interface interfaceStmt)
    {
        // Skip if already registered
        if (_environment.IsDefinedLocally(interfaceStmt.Name.Lexeme))
            return;

        // Handle generic type parameters with two-pass approach to support recursive constraints
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];

            // First pass: define all type parameters without constraints
            foreach (var tp in interfaceStmt.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null, tp.IsConst, tp.Variance);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }

            // Second pass: parse constraints (which may reference other type parameters)
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var tp in interfaceStmt.TypeParams)
                {
                    // During pre-registration, we use a simple constraint parsing
                    // that may fail on forward references - that's OK, we catch the error
                    TypeInfo? constraint = null;
                    TypeInfo? defaultType = null;
                    try
                    {
                        constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
                        defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
                    }
                    catch
                    {
                        // Ignore constraint/default parsing errors during pre-registration
                    }
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                    interfaceTypeParams.Add(typeParam);
                    // Redefine with the actual constraint
                    interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }
        }

        // Parse member types (may have forward references that resolve to Any, which is OK)
        Dictionary<string, TypeInfo> members = [];
        Dictionary<string, List<TypeInfo.Function>> pendingOverloads = [];
        HashSet<string> optionalMembers = [];
        HashSet<string> readonlyMembers = [];
        HashSet<string> methodMembers = [];

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
            foreach (var member in interfaceStmt.Members)
            {
                try
                {
                    var memberType = ResolveAnnotation(member.Type, member.TypeAnnotationNode)!;

                    // Check if this is a duplicate member name (overload)
                    if (members.TryGetValue(member.Name.Lexeme, out var existingType))
                    {
                        // This is an overloaded method - collect signatures
                        if (!pendingOverloads.TryGetValue(member.Name.Lexeme, out var overloadList))
                        {
                            overloadList = [];
                            pendingOverloads[member.Name.Lexeme] = overloadList;
                            if (existingType is TypeInfo.Function existingFunc)
                                overloadList.Add(existingFunc);
                        }
                        if (memberType is TypeInfo.Function newFunc)
                            overloadList.Add(newFunc);
                    }
                    else
                    {
                        members[member.Name.Lexeme] = memberType;
                    }
                }
                catch
                {
                    // If type parsing fails, use Any as placeholder
                    members[member.Name.Lexeme] = TypeInfo.Any.Shared;
                }
                if (member.IsOptional)
                {
                    optionalMembers.Add(member.Name.Lexeme);
                }
                if (member.IsReadonly)
                {
                    readonlyMembers.Add(member.Name.Lexeme);
                }
                if (member.IsMethod)
                {
                    methodMembers.Add(member.Name.Lexeme);
                }
            }

            // Convert collected overloads to OverloadedFunction types
            foreach (var (name, signatures) in pendingOverloads)
            {
                members[name] = new TypeInfo.OverloadedFunction(signatures, signatures[0]);
            }
        }

        // Resolve extended interfaces
        FrozenSet<TypeInfo.Interface>? extends = null;
        if (interfaceStmt.Extends != null && interfaceStmt.Extends.Count > 0)
        {
            var extendsList = new HashSet<TypeInfo.Interface>();
            for (int i = 0; i < interfaceStmt.Extends.Count; i++)
            {
                try
                {
                    var extendType = ResolveAnnotation(
                        interfaceStmt.Extends[i],
                        interfaceStmt.ExtendsNodes != null && i < interfaceStmt.ExtendsNodes.Count ? interfaceStmt.ExtendsNodes[i] : null)!;
                    if (extendType is TypeInfo.Interface extendInterface)
                    {
                        extendsList.Add(extendInterface);
                    }
                }
                catch
                {
                    // Ignore resolution errors during pre-registration
                }
            }
            if (extendsList.Count > 0)
            {
                extends = extendsList.ToFrozenSet();
            }
        }

        // Parse call signatures (skip during pre-registration - just add empty lists for now)
        List<TypeInfo.CallSignature>? callSignatures = null;
        List<TypeInfo.ConstructorSignature>? constructorSignatures = null;

        // Register the interface (skip index signatures during pre-registration - they'll be added during full check)
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                CallSignatures: callSignatures,
                ConstructorSignatures: constructorSignatures,
                ReadonlyMembers: readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null,
                MethodMembers: methodMembers.Count > 0 ? methodMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
        }
        else
        {
            TypeInfo.Interface itfType = new(
                interfaceStmt.Name.Lexeme,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                Extends: extends,
                CallSignatures: callSignatures,
                ConstructorSignatures: constructorSignatures,
                ReadonlyMembers: readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null,
                MethodMembers: methodMembers.Count > 0 ? methodMembers.ToFrozenSet() : null
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }
    }

    private void CheckInterfaceDeclaration(Stmt.Interface interfaceStmt)
    {
        // An interface may not be named after a primitive type keyword (TS2427). `symbol` is a
        // contextual keyword the parser accepts as an identifier, so it reaches here.
        if (interfaceStmt.Name.Lexeme == "symbol")
        {
            throw new TypeCheckException("Interface name cannot be 'symbol'.", line: interfaceStmt.Name.Line, tsCode: "TS2427");
        }

        // Handle generic type parameters with two-pass approach to support recursive constraints (e.g., T extends TreeNode<T>)
        List<TypeInfo.TypeParameter>? interfaceTypeParams = null;
        TypeEnvironment interfaceTypeEnv = new(_environment);
        if (interfaceStmt.TypeParams != null && interfaceStmt.TypeParams.Count > 0)
        {
            interfaceTypeParams = [];

            // First pass: define all type parameters without constraints so they can reference each other
            foreach (var tp in interfaceStmt.TypeParams)
            {
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, null, null, tp.IsConst, tp.Variance);
                interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
            }

            // Second pass: parse constraints (which may reference other type parameters, including themselves)
            using (new EnvironmentScope(this, interfaceTypeEnv))
            {
                foreach (var tp in interfaceStmt.TypeParams)
                {
                    TypeInfo? constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
                    TypeInfo? defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
                    var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                    interfaceTypeParams.Add(typeParam);
                    // Redefine with the actual constraint
                    interfaceTypeEnv.DefineTypeParameter(tp.Name.Lexeme, typeParam);
                }
            }
        }

        // Use interfaceTypeEnv for member type resolution so T resolves correctly
        Dictionary<string, TypeInfo> members = [];
        // Source line of each member's declaration, so index-signature conformance
        // diagnostics (TS2411) can be reported at the offending property rather than
        // aggregated onto the interface declaration line (matches tsc).
        Dictionary<string, int> memberLines = [];
        Dictionary<string, List<TypeInfo.Function>> pendingOverloads = []; // Track overloaded methods
        HashSet<string> optionalMembers = [];
        HashSet<string> readonlyMembers = [];
        HashSet<string> methodMembers = [];
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;
        bool readonlyNumberIndex = false;

        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
        foreach (var member in interfaceStmt.Members)
        {
            var memberType = ResolveAnnotation(member.Type, member.TypeAnnotationNode)!;

            // Check if this is a duplicate member name (overload)
            if (members.TryGetValue(member.Name.Lexeme, out var existingType))
            {
                // This is an overloaded method - collect signatures
                if (!pendingOverloads.TryGetValue(member.Name.Lexeme, out var overloadList))
                {
                    overloadList = [];
                    pendingOverloads[member.Name.Lexeme] = overloadList;

                    // Add the first signature to the overload list
                    if (existingType is TypeInfo.Function existingFunc)
                        overloadList.Add(existingFunc);
                }

                // Add the new signature
                if (memberType is TypeInfo.Function newFunc)
                    overloadList.Add(newFunc);
            }
            else
            {
                members[member.Name.Lexeme] = memberType;
            }

            // Remember the first declaration line for each member name (used by the
            // string-index conformance check below to locate TS2411 diagnostics).
            if (!memberLines.ContainsKey(member.Name.Lexeme))
                memberLines[member.Name.Lexeme] = member.Name.Line;

            if (member.IsOptional)
            {
                optionalMembers.Add(member.Name.Lexeme);
            }

            if (member.IsReadonly)
            {
                readonlyMembers.Add(member.Name.Lexeme);
            }
            if (member.IsMethod)
            {
                methodMembers.Add(member.Name.Lexeme);
            }
        }

        // Convert collected overloads to OverloadedFunction types
        foreach (var (name, signatures) in pendingOverloads)
        {
            // Use the first signature as the "implementation" for the overloaded function
            // In interfaces, there's no true implementation, so we just need the signatures
            members[name] = new TypeInfo.OverloadedFunction(signatures, signatures[0]);
        }

        // Process index signatures
        if (interfaceStmt.IndexSignatures != null)
        {
            foreach (var indexSig in interfaceStmt.IndexSignatures)
            {
                TypeInfo valueType = ResolveAnnotation(indexSig.ValueType, indexSig.ValueTypeNode)!;
                switch (indexSig.KeyType)
                {
                    case TokenType.TYPE_STRING:
                        if (stringIndexType != null)
                            throw new TypeCheckException($" Duplicate string index signature in interface '{interfaceStmt.Name.Lexeme}'.", tsCode: "TS2374");
                        stringIndexType = valueType;
                        break;
                    case TokenType.TYPE_NUMBER:
                        if (numberIndexType != null)
                            throw new TypeCheckException($" Duplicate number index signature in interface '{interfaceStmt.Name.Lexeme}'.", tsCode: "TS2374");
                        numberIndexType = valueType;
                        break;
                    case TokenType.TYPE_SYMBOL:
                        if (symbolIndexType != null)
                            throw new TypeCheckException($" Duplicate symbol index signature in interface '{interfaceStmt.Name.Lexeme}'.", tsCode: "TS2374");
                        symbolIndexType = valueType;
                        break;
                }
            }

            // TypeScript rule: number index type must be assignable to string index type
            if (stringIndexType != null && numberIndexType != null)
            {
                if (!IsCompatible(stringIndexType, numberIndexType))
                {
                    throw new TypeCheckException($" Number index type '{numberIndexType}' is not assignable to string index type '{stringIndexType}' in interface '{interfaceStmt.Name.Lexeme}'.", tsCode: "TS2413");
                }
            }

            // Validate explicit properties are compatible with the string index signature.
            // tsc reports one TS2411 per offending property, located at that property's
            // own declaration line (not aggregated onto the interface). Record (don't
            // throw) so every offending property is reported, not just the first.
            if (stringIndexType != null)
            {
                foreach (var (name, type) in members)
                {
                    // Computed well-known-symbol members (canonical "@@name") are exempt — a symbol
                    // key can never collide with a string index signature at runtime.
                    if (name.StartsWith("@@", StringComparison.Ordinal)) continue;
                    if (!IsCompatible(stringIndexType, type))
                    {
                        memberLines.TryGetValue(name, out var line);
                        RecordTypeError(new TypeCheckException(
                            $" Property '{name}' of type '{type}' is not assignable to 'string' index type '{stringIndexType}'.",
                            line: line == 0 ? interfaceStmt.Name.Line : line,
                            tsCode: "TS2411"));
                    }
                }
            }

            // Mirror image of the string-index check above: a symbol index signature only
            // constrains computed well-known-symbol members (canonical "@@name"), never
            // plain string/number-keyed ones.
            if (symbolIndexType != null)
            {
                foreach (var (name, type) in members)
                {
                    if (!name.StartsWith("@@", StringComparison.Ordinal)) continue;
                    if (!IsCompatible(symbolIndexType, type))
                    {
                        memberLines.TryGetValue(name, out var line);
                        string displayName = $"[Symbol.{name["@@".Length..]}]";
                        RecordTypeError(new TypeCheckException(
                            $" Property '{displayName}' of type '{type}' is not assignable to 'symbol' index type '{symbolIndexType}'.",
                            line: line == 0 ? interfaceStmt.Name.Line : line,
                            tsCode: "TS2411"));
                    }
                }
            }
        }
        }

        // Resolve extended interfaces. The interface's own type parameters must be in scope here:
        // a base reference like `extends ReadonlyArray<DeepReadonly<T>>` mentions T, and resolving
        // it outside the scope collapses T to `any`, so the inherited numeric index element type
        // is lost (it becomes `any` instead of the substitutable `DeepReadonly<T>`) (#365).
        FrozenSet<TypeInfo.Interface>? extends = null;
        if (interfaceStmt.Extends != null && interfaceStmt.Extends.Count > 0)
        using (new EnvironmentScope(this, interfaceTypeEnv))
        {
            var extendsList = new HashSet<TypeInfo.Interface>();
            // Resolving `extends A<Base>` instantiates the generic base; a type-argument constraint
            // violation (TS2344) is recorded at the interface name's line and resolution continues
            // with the offending argument, so sibling declarations in the enclosing module/block and
            // this interface's own index-signature TS2430 check still run (#895).
            int? savedExtendsLine = _extendsClauseConstraintLine;
            _extendsClauseConstraintLine = interfaceStmt.Name.Line;
            try
            {
            for (int i = 0; i < interfaceStmt.Extends.Count; i++)
            {
                var extendTypeName = interfaceStmt.Extends[i];
                var extendType = ResolveAnnotation(
                    extendTypeName,
                    interfaceStmt.ExtendsNodes != null && i < interfaceStmt.ExtendsNodes.Count ? interfaceStmt.ExtendsNodes[i] : null)!;
                if (extendType is TypeInfo.Interface extendInterface)
                {
                    extendsList.Add(extendInterface);
                }
                else if (extendType is TypeInfo.InstantiatedGeneric extendIG &&
                         FlattenInstantiatedInterface(extendIG) is { } flattened)
                {
                    // `extends A<Base>` — substitute the type arguments into the generic
                    // interface's members so the base behaves like a concrete interface.
                    extendsList.Add(flattened);
                }
                else if ((extendType is TypeInfo.Instance inst ? inst.ResolvedClassType : extendType)
                         is TypeInfo.Class extendClass)
                {
                    // TypeScript allows an interface to extend a CLASS: it inherits the class's
                    // member types as if they were interface members. (A class name in type
                    // position resolves to its Instance type, so unwrap that first.)
                    extendsList.Add(ClassAsInterfaceBase(extendClass));
                }
                else if (extendType is TypeInfo.Array extendArray)
                {
                    // `interface I<T> extends ReadonlyArray<E>` (or Array<E>): model the array base
                    // as a numeric index signature of its element type, read-only for ReadonlyArray.
                    // This is what lets DeepReadonlyArray index-access resolve and reject writes
                    // (#337 item 2). The element type carries the interface's own type parameters
                    // and is substituted at instantiation by FlattenInstantiatedInterface.
                    numberIndexType = extendArray.ElementType;
                    if (extendArray.IsReadonly) readonlyNumberIndex = true;
                }
                else
                {
                    throw new TypeCheckException($" Interface '{interfaceStmt.Name.Lexeme}' can only extend other interfaces, but '{extendTypeName}' is not an interface.", tsCode: "TS2312");
                }
            }
            }
            finally { _extendsClauseConstraintLine = savedExtendsLine; }
            extends = extendsList.ToFrozenSet();

            // TS2320: an interface may not simultaneously extend two bases that declare the same
            // member with non-identical types. Two types are treated as "not identical" when they
            // aren't mutually assignable — conservative, so a genuinely-shared (diamond) member,
            // being mutually assignable, never trips it.
            var seenBaseMembers = new Dictionary<string, (string BaseName, TypeInfo Type)>();
            bool reportedExtendConflict = false;
            foreach (var baseItf in extendsList)
            {
                if (reportedExtendConflict) break;
                foreach (var (mName, mType) in baseItf.GetAllMembers())
                {
                    if (seenBaseMembers.TryGetValue(mName, out var prev)
                        && prev.BaseName != baseItf.Name
                        && !(IsCompatible(prev.Type, mType) && IsCompatible(mType, prev.Type)))
                    {
                        string display = mName.StartsWith("@@") ? $"[Symbol.{mName[2..]}]" : mName;
                        RecordTypeError(new TypeCheckException(
                            $" Interface '{interfaceStmt.Name.Lexeme}' cannot simultaneously extend types '{prev.BaseName}' and '{baseItf.Name}'. Named property '{display}' of types '{prev.BaseName}' and '{baseItf.Name}' are not identical.",
                            line: interfaceStmt.Name.Line, tsCode: "TS2320"));
                        reportedExtendConflict = true;
                        break;
                    }
                    seenBaseMembers.TryAdd(mName, (baseItf.Name, mType));
                }
            }
        }

        // Process call signatures
        List<TypeInfo.CallSignature>? callSignatures = null;
        if (interfaceStmt.CallSignatures != null && interfaceStmt.CallSignatures.Count > 0)
        {
            callSignatures = [];
            foreach (var sig in interfaceStmt.CallSignatures)
            {
                // The signature's own type parameters must be in scope while its parameter and
                // return types resolve — otherwise `<T>(x: T): T[]` silently collapses T to any
                // and the signature relates vacuously.
                var sigEnv = ScopedSignatureTypeParamEnv(interfaceTypeEnv, sig.TypeParams, out var sigTypeParams);
                using (new EnvironmentScope(this, sigEnv))
                {
                    var paramTypes = sig.Parameters.Select(p => ResolveAnnotation(p.Type, p.TypeAnnotationNode) ?? TypeInfo.Any.Shared).ToList();
                    var returnType = ResolveAnnotation(sig.ReturnType, sig.ReturnTypeNode)!;
                    int requiredParams = sig.Parameters.TakeWhile(p => !p.IsOptional && p.DefaultValue == null).Count();
                    bool hasRestParam = sig.Parameters.Any(p => p.IsRest);
                    var paramNames = sig.Parameters.Select(p => p.Name.Lexeme).ToList();
                    callSignatures.Add(new TypeInfo.CallSignature(sigTypeParams, paramTypes, returnType, requiredParams, hasRestParam, paramNames));
                }
            }
        }

        // Process constructor signatures
        List<TypeInfo.ConstructorSignature>? constructorSignatures = null;
        if (interfaceStmt.ConstructorSignatures != null && interfaceStmt.ConstructorSignatures.Count > 0)
        {
            constructorSignatures = [];
            foreach (var sig in interfaceStmt.ConstructorSignatures)
            {
                // Same scoping rule as call signatures above.
                var sigEnv = ScopedSignatureTypeParamEnv(interfaceTypeEnv, sig.TypeParams, out var sigTypeParams);
                using (new EnvironmentScope(this, sigEnv))
                {
                    var paramTypes = sig.Parameters.Select(p => ResolveAnnotation(p.Type, p.TypeAnnotationNode) ?? TypeInfo.Any.Shared).ToList();
                    var returnType = ResolveAnnotation(sig.ReturnType, sig.ReturnTypeNode)!;
                    int requiredParams = sig.Parameters.TakeWhile(p => !p.IsOptional && p.DefaultValue == null).Count();
                    bool hasRestParam = sig.Parameters.Any(p => p.IsRest);
                    var paramNames = sig.Parameters.Select(p => p.Name.Lexeme).ToList();
                    constructorSignatures.Add(new TypeInfo.ConstructorSignature(sigTypeParams, paramTypes, returnType, requiredParams, hasRestParam, paramNames));
                }
            }
        }

        // Create GenericInterface or regular Interface
        if (interfaceTypeParams != null && interfaceTypeParams.Count > 0)
        {
            var genericItfType = new TypeInfo.GenericInterface(
                interfaceStmt.Name.Lexeme,
                interfaceTypeParams,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                stringIndexType,
                numberIndexType,
                symbolIndexType,
                extends,
                callSignatures,
                constructorSignatures,
                readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null,
                methodMembers.Count > 0 ? methodMembers.ToFrozenSet() : null,
                readonlyNumberIndex
            );
            _environment.Define(interfaceStmt.Name.Lexeme, genericItfType);
        }
        else
        {
            TypeInfo.Interface itfType = new(
                interfaceStmt.Name.Lexeme,
                members.ToFrozenDictionary(),
                optionalMembers.ToFrozenSet(),
                stringIndexType,
                numberIndexType,
                symbolIndexType,
                extends,
                callSignatures,
                constructorSignatures,
                readonlyMembers.Count > 0 ? readonlyMembers.ToFrozenSet() : null,
                methodMembers.Count > 0 ? methodMembers.ToFrozenSet() : null,
                ReadonlyNumberIndex: readonlyNumberIndex
            );
            _environment.Define(interfaceStmt.Name.Lexeme, itfType);
        }

        ValidateInterfaceExtends(interfaceStmt, members, optionalMembers, extends);
        ValidateInterfaceIndexSignatureExtends(interfaceStmt, stringIndexType, numberIndexType, extends);
    }

    /// <summary>
    /// TS2430: every member this interface redeclares must be assignable to the corresponding
    /// member of each extended interface. Runs AFTER the interface is defined in the environment,
    /// so an incorrect extension still leaves the type resolvable (no cascading unknown-type
    /// errors); the thrown error carries the interface name's line, and in recovery mode the
    /// enclosing statement/namespace loop records it and keeps checking sibling declarations.
    /// </summary>
    private void ValidateInterfaceExtends(
        Stmt.Interface interfaceStmt,
        Dictionary<string, TypeInfo> members,
        HashSet<string> optionalMembers,
        FrozenSet<TypeInfo.Interface>? extends)
    {
        if (extends is null) return;
        foreach (var baseItf in extends)
        {
            var baseOptional = baseItf.GetAllOptionalMembers().ToHashSet();
            foreach (var (memberName, baseMemberType) in baseItf.GetAllMembers())
            {
                if (!members.TryGetValue(memberName, out var derivedMemberType)) continue;

                // Optionality: a derived interface may not make a base-required member optional
                // (tsc: "Property 'X' is optional in type 'S' but required in type 'T'").
                if (optionalMembers.Contains(memberName) && !baseOptional.Contains(memberName))
                {
                    var optError = new TypeCheckException(
                        $" Interface '{interfaceStmt.Name.Lexeme}' incorrectly extends interface '{baseItf.Name}'. Property '{memberName}' is optional in type '{interfaceStmt.Name.Lexeme}' but required in type '{baseItf.Name}'.",
                        line: interfaceStmt.Name.Line,
                        tsCode: "TS2430");
                    if (_recoveryMode) { RecordTypeError(optError); break; }
                    throw optError;
                }

                if (!IsCompatible(baseMemberType, derivedMemberType))
                {
                    var error = new TypeCheckException(
                        $" Interface '{interfaceStmt.Name.Lexeme}' incorrectly extends interface '{baseItf.Name}'. Property '{memberName}' of type '{derivedMemberType}' is not assignable to '{baseMemberType}'.",
                        line: interfaceStmt.Name.Line,
                        tsCode: "TS2430");
                    // Interfaces inside a namespace are checked in its (non-recovering) collection
                    // pass — throwing there would abort the namespace's remaining declarations. In
                    // recovery mode record the diagnostic directly and keep going; one error per
                    // offending base matches tsc.
                    if (_recoveryMode) { RecordTypeError(error); break; }
                    throw error;
                }
            }
        }
    }

    /// <summary>
    /// TS2430 index-signature variant: a derived interface's own index signature must be assignable to
    /// the corresponding index signature it inherits from each extended interface. Mirrors
    /// <see cref="ValidateClassIndexSignatureExtends"/> (classes/TS2415) for interfaces, including the
    /// generic case: <c>interface B3&lt;T extends Base&gt; extends A&lt;T&gt; { [x: number]: Derived }</c>
    /// is an error because the inherited index resolves to the open <c>T</c> (the base interface is
    /// flattened with its type arguments substituted in <see cref="FlattenInstantiatedInterface"/>),
    /// which a concrete <c>Derived</c> cannot satisfy. Like <see cref="ValidateInterfaceExtends"/> it
    /// records-and-continues in recovery mode so sibling declarations in the same module/block keep
    /// being checked (#895).
    /// </summary>
    private void ValidateInterfaceIndexSignatureExtends(
        Stmt.Interface interfaceStmt,
        TypeInfo? stringIndexType,
        TypeInfo? numberIndexType,
        FrozenSet<TypeInfo.Interface>? extends)
    {
        if (extends is null) return;
        foreach (var baseItf in extends)
        {
            // tsc reports a single TS2430 per offending base even when both index kinds mismatch.
            foreach (var (derived, baseSub, kind) in new[]
            {
                (stringIndexType, baseItf.StringIndexType, "string"),
                (numberIndexType, baseItf.NumberIndexType, "number"),
            })
            {
                // Only an *overriding* index signature is checked — if the base has none, or the
                // derived doesn't redeclare one, there's nothing to relate.
                if (derived is null || baseSub is null) continue;
                if (!IsCompatible(baseSub, derived))
                {
                    var error = new TypeCheckException(
                        $" Interface '{interfaceStmt.Name.Lexeme}' incorrectly extends interface '{baseItf.Name}'. The '{kind}' index signatures are incompatible.",
                        line: interfaceStmt.Name.Line,
                        tsCode: "TS2430");
                    if (_recoveryMode) { RecordTypeError(error); break; }
                    throw error;
                }
            }
        }
    }

    /// <summary>
    /// Parses type parameters from a signature into TypeInfo.TypeParameter list.
    /// </summary>
    private List<TypeInfo.TypeParameter>? ParseSignatureTypeParams(List<TypeParam>? typeParams)
    {
        if (typeParams == null || typeParams.Count == 0)
            return null;

        List<TypeInfo.TypeParameter> result = [];
        foreach (var tp in typeParams)
        {
            TypeInfo? constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
            TypeInfo? defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
            result.Add(new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance));
        }
        return result;
    }

    /// <summary>
    /// Views a class's instance shape (fields, methods, getters — own and inherited) as an
    /// interface, for `interface I extends SomeClass`.
    /// </summary>
    private TypeInfo.Interface ClassAsInterfaceBase(TypeInfo.Class cls)
    {
        Dictionary<string, TypeInfo> members = [];
        // Non-public members (TypeScript `private`/`protected`) keep their nominal origin so the
        // resulting interface relates them like the class did — a private member is only assignable
        // to/from the identical declaration. The declaring class id matches the source class.
        Dictionary<string, MemberAccessBrand> brands = [];
        TypeInfo? current = cls;
        while (current is TypeInfo.Class c)
        {
            var core = c.Core;
            foreach (var (n, t) in c.FieldTypes)
                if (members.TryAdd(n, t)) RecordMemberBrand(brands, n, core.FieldAccess, core.DeclarationId);
            foreach (var (n, t) in c.Methods)
                if (n != "constructor" && members.TryAdd(n, t)) RecordMemberBrand(brands, n, core.MethodAccess, core.DeclarationId);
            foreach (var (n, t) in c.Getters) members.TryAdd(n, t);
            current = GetSuperclass(current);
        }
        return new TypeInfo.Interface(
            cls.Name,
            members.ToFrozenDictionary(),
            FrozenSet<string>.Empty,
            cls.StringIndexType,
            cls.NumberIndexType,
            MemberBrands: brands.Count == 0 ? null : brands.ToFrozenDictionary());
    }

    /// <summary>Records a member's brand when it is non-public (private/protected). Public members
    /// carry no brand — their absence from the map means "public, no nominal origin".</summary>
    private static void RecordMemberBrand(
        Dictionary<string, MemberAccessBrand> brands,
        string name,
        FrozenDictionary<string, AccessModifier> access,
        int declaringClassId)
    {
        if (access.TryGetValue(name, out var mod) && mod != AccessModifier.Public)
            brands[name] = new MemberAccessBrand(mod, declaringClassId);
    }

    /// <summary>
    /// Converts an instantiation of a generic interface (e.g. <c>A&lt;Base&gt;</c>) into a concrete
    /// <see cref="TypeInfo.Interface"/> by substituting the type arguments into its members and
    /// index signatures — the shape `extends A&lt;Base&gt;` needs as a base. Returns null when the
    /// instantiated definition isn't a generic interface.
    /// </summary>
    private TypeInfo.Interface? FlattenInstantiatedInterface(TypeInfo.InstantiatedGeneric ig)
    {
        if (ig.GenericDefinition is not TypeInfo.GenericInterface gi) return null;
        Dictionary<string, TypeInfo> subs = [];
        for (int i = 0; i < gi.TypeParams.Count && i < ig.TypeArguments.Count; i++)
            subs[gi.TypeParams[i].Name] = ig.TypeArguments[i];
        // SubstitutePreservingSignatures (not plain Substitute) so a member or index value that is a
        // construct/call signature — `a: new () => T` resolves to a Record carrying a
        // ConstructorSignature — keeps it through substitution. Plain Substitute rebuilds Records
        // fields-only, collapsing such a member to `{}`, which any derived member then vacuously
        // satisfies, so the interface-extends check (TS2430) never fires under generics (#896). For a
        // non-Record value the helper is identical to Substitute.
        var members = gi.Members.ToDictionary(kv => kv.Key, kv => SubstitutePreservingSignatures(kv.Value, subs));
        return new TypeInfo.Interface(
            $"{gi.Name}<{string.Join(", ", ig.TypeArguments)}>",
            members.ToFrozenDictionary(),
            gi.OptionalMembers,
            gi.StringIndexType is null ? null : SubstitutePreservingSignatures(gi.StringIndexType, subs),
            gi.NumberIndexType is null ? null : SubstitutePreservingSignatures(gi.NumberIndexType, subs),
            gi.SymbolIndexType is null ? null : SubstitutePreservingSignatures(gi.SymbolIndexType, subs),
            gi.Extends,
            gi.CallSignatures,
            gi.ConstructorSignatures,
            gi.ReadonlyMembers,
            gi.MethodMembers,
            ReadonlyNumberIndex: gi.ReadonlyNumberIndex);
    }

    /// <summary>
    /// Builds a child type environment with a signature's own type parameters defined in it, so
    /// the signature's parameter/return types resolve them as <see cref="TypeInfo.TypeParameter"/>
    /// instead of collapsing to <c>any</c>. Two passes: names are defined unconstrained first so a
    /// constraint can reference a sibling parameter, then redefined with constraints resolved.
    /// </summary>
    private TypeEnvironment ScopedSignatureTypeParamEnv(
        TypeEnvironment parent, List<TypeParam>? typeParams, out List<TypeInfo.TypeParameter>? sigTypeParams)
    {
        var env = new TypeEnvironment(parent);
        if (typeParams is { Count: > 0 })
        {
            foreach (var tp in typeParams)
                env.DefineTypeParameter(tp.Name.Lexeme, new TypeInfo.TypeParameter(tp.Name.Lexeme));
            using (new EnvironmentScope(this, env))
                sigTypeParams = ParseSignatureTypeParams(typeParams);
            foreach (var tp in sigTypeParams!)
                env.DefineTypeParameter(tp.Name, tp);
        }
        else
        {
            sigTypeParams = null;
        }
        return env;
    }
}
