using SharpTS.TypeSystem.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Function declaration type checking - handles function statements including overloads and generics.
/// </summary>
public partial class TypeChecker
{
    // Script/module collection can visit the same body-less declaration again in the authoritative
    // pass. Key by both environment and declaration identity so one declaration contributes one
    // overload signature, while a distinct declaration with the same name still forms a real
    // overload set.
    private readonly Dictionary<TypeEnvironment, HashSet<Stmt.Function>> _completedBodylessFunctions =
        new(ReferenceEqualityComparer.Instance);

    private void ResetFunctionDeclarationTracking() => _completedBodylessFunctions.Clear();

    private bool MarkBodylessFunctionCompleted(Stmt.Function declaration)
    {
        if (!_completedBodylessFunctions.TryGetValue(_environment, out var declarations))
        {
            declarations = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
            _completedBodylessFunctions[_environment] = declarations;
        }
        return declarations.Add(declaration);
    }

    /// <summary>
    /// Builds generic type parameters into <paramref name="env"/>, resolving constraints that may
    /// reference other parameters. Parameters are first defined without constraints so they can
    /// reference one another; the constraint-resolution pass then runs repeatedly so multi-level
    /// chains (<c>T extends U extends V</c>) link fully — each pass deepens the captured chain by one
    /// level, so <c>decls.Count</c> passes suffice for any depth.
    /// </summary>
    private List<TypeInfo.TypeParameter> BuildGenericTypeParameters(List<TypeParam> decls, TypeEnvironment env)
    {
        foreach (var tp in decls)
        {
            DefineSourceTypeParameter(
                env,
                tp,
                new TypeInfo.TypeParameter(
                    tp.Name.Lexeme,
                    null,
                    null,
                    tp.IsConst,
                    tp.Variance));
        }

        List<TypeInfo.TypeParameter> result = [];
        for (int pass = 0; pass < decls.Count; pass++)
        {
            result = [];
            foreach (var tp in decls)
            {
                TypeInfo? constraint = ResolveAnnotation(tp.Constraint, tp.ConstraintNode);
                TypeInfo? defaultType = ResolveAnnotation(tp.Default, tp.DefaultNode);
                var typeParam = new TypeInfo.TypeParameter(tp.Name.Lexeme, constraint, defaultType, tp.IsConst, tp.Variance);
                result.Add(typeParam);
                DefineSourceTypeParameter(env, tp, typeParam);
            }
        }
        return result;
    }

    /// <summary>
    /// Hoists function declarations by registering their types before checking bodies.
    /// This enables functions to reference each other regardless of declaration order.
    /// </summary>
    private void HoistFunctionDeclarations(IEnumerable<Stmt> statements)
    {
        // Materialized once: the two passes below each iterate the statements.
        var stmtList = statements as IReadOnlyList<Stmt> ?? statements.ToList();

        // Pass 1: register each declaration's signature (un-annotated return types as `any`).
        foreach (var stmt in stmtList)
        {
            // Handle top-level functions. Body-less declarations (ambient `declare function`,
            // overload signatures) hoist too — tsc resolves calls that precede the declaration.
            // For an overload group only the first signature lands here; the visit pass installs
            // the full OverloadedFunction when it reaches the group.
            if (stmt is Stmt.Function funcStmt)
            {
                HoistSingleFunction(funcStmt);
            }
            // Handle exported functions
            else if (stmt is Stmt.Export export && export.Declaration is Stmt.Function exportedFunc)
            {
                HoistSingleFunction(exportedFunc);
            }
        }

        // Hoist const/let/var declarations with function expressions
        // This enables mutual recursion like: const isEven = function(n) { return isOdd(n-1); };
        HoistConstFunctionExpressions(stmtList);

        // Pass 2: now that every sibling signature is registered, speculatively infer the return
        // type of un-annotated functions so calls appearing earlier in the same scope type
        // precisely instead of as `any` (#383). Done as a separate pass so sibling references
        // resolve regardless of declaration order.
        foreach (var stmt in stmtList)
        {
            if (stmt is Stmt.Function f2)
                RefineHoistedFunctionReturnType(f2);
            else if (stmt is Stmt.Export { Declaration: Stmt.Function ef2 })
                RefineHoistedFunctionReturnType(ef2);
        }
    }

    /// <summary>
    /// Second hoisting pass for a single un-annotated <c>function</c> declaration: speculatively
    /// infers its return type and registers the refined signature, so a call that textually
    /// precedes the declaration in the same scope sees a concrete return type rather than
    /// <c>any</c> (issue #383). The inferred type is memoized in <see cref="_hoistInferredReturnTypes"/>
    /// and re-registered into each fresh scope on later passes without re-checking the body.
    /// </summary>
    private void RefineHoistedFunctionReturnType(Stmt.Function funcStmt)
    {
        // Only plain, implemented, non-overloaded functions without a return annotation are
        // eligible. Overload groups drive call sites from their explicit signatures, not the
        // implementation's inferred return. Generic functions ARE handled here (#388): their
        // inferred return is expressed in terms of the type parameters and re-instantiated at
        // each call site, so a call preceding the declaration types precisely instead of `any`.
        if (funcStmt.ReturnType != null || funcStmt.Body == null) return;
        string funcName = funcStmt.Name.Lexeme;
        var overloadKey = (_environment, funcName);
        if (_pendingOverloadSignatures.ContainsKey(overloadKey)) return;
        if (!_environment.IsDefinedLocally(funcName)) return;

        // Recognize our hoisted `any`-return placeholder: a non-generic function hoists as a plain
        // Function, a generic one as a GenericFunction (carrying its type parameters). Anything else
        // (annotated, redefined, or already refined) is left untouched.
        List<TypeInfo> paramTypes;
        int requiredParams;
        bool hasRest;
        List<string>? paramNames;
        TypeInfo? thisType;
        List<TypeInfo.TypeParameter>? typeParams;
        switch (_environment.Get(funcName))
        {
            case TypeInfo.GenericFunction { ReturnType: TypeInfo.Any } gh:
                paramTypes = gh.ParamTypes; requiredParams = gh.RequiredParams; hasRest = gh.HasRestParam;
                paramNames = gh.ParamNames; thisType = gh.ThisType; typeParams = gh.TypeParams;
                break;
            case TypeInfo.Function { ReturnType: TypeInfo.Any } fh:
                paramTypes = fh.ParamTypes; requiredParams = fh.RequiredParams; hasRest = fh.HasRestParam;
                paramNames = fh.ParamNames; thisType = fh.ThisType; typeParams = null;
                break;
            default:
                return;
        }

        if (_hoistInferredReturnTypes.TryGetValue(funcStmt, out var cached))
        {
            // Re-register a previously inferred type into this (fresh) scope. A null value marks an
            // in-progress (mutual recursion) or failed inference — leave the placeholder in place.
            if (cached != null)
            {
                _environment.Define(funcName, typeParams is { Count: > 0 }
                    ? new TypeInfo.GenericFunction(typeParams, paramTypes, cached, requiredParams, hasRest, thisType, paramNames)
                    : new TypeInfo.Function(paramTypes, cached, requiredParams, hasRest, thisType, paramNames));
            }
            return;
        }

        // Mark in-progress before inferring so mutual recursion terminates against the placeholder.
        _hoistInferredReturnTypes[funcStmt] = null;
        var funcEnv = new TypeEnvironment(_environment);
        // A generic function's parameter and return types reference its type parameters. Define them
        // in the body environment (the same TypeParameter instances the hoisted signature carries) so
        // references inside the body resolve and the inferred return is expressed in those terms.
        if (typeParams is { Count: > 0 })
        {
            for (int i = 0; i < typeParams.Count; i++)
            {
                if (funcStmt.TypeParams is { } declarations &&
                    i < declarations.Count)
                {
                    DefineSourceTypeParameter(
                        funcEnv,
                        declarations[i],
                        typeParams[i]);
                }
                else
                {
                    funcEnv.DefineTypeParameter(
                        typeParams[i].Name,
                        typeParams[i]);
                }
            }
        }
        CheckFunctionBodyAndInferReturn(
            funcStmt, funcEnv, paramTypes, requiredParams, hasRest,
            paramNames ?? new List<string>(), thisType, typeParams,
            returnType: TypeInfo.Inferred.Shared, inferringReturnType: true, funcName, suppress: true);
    }

    /// <summary>
    /// Pre-registers top-level var declarations by defining them as 'any' in the type environment.
    /// This enables forward references to var-declared variables from within function bodies,
    /// matching JavaScript's var hoisting semantics. <c>let</c>/<c>const</c> are handled by the
    /// sibling <see cref="HoistLexicalDeclarations"/>.
    /// </summary>
    private void HoistVarDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Var v when v.IsVar:
                    RegisterValueDeclaration(v.Name, mergeWithLocal: true);
                    if (!_environment.IsDefinedLocally(v.Name.Lexeme))
                        _environment.Define(v.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
                case Stmt.Export { Declaration: Stmt.Var v } when v.IsVar:
                    RegisterValueDeclaration(v.Name, mergeWithLocal: true);
                    if (!_environment.IsDefinedLocally(v.Name.Lexeme))
                        _environment.Define(v.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
            }
        }
    }

    /// <summary>
    /// Pre-registers top-level <c>let</c>/<c>const</c> declarations by defining their names as
    /// <c>any</c> in the type environment, so a function/closure body that textually PRECEDES the
    /// declaration can still forward-reference it (#533). TypeScript collects every lexical binding
    /// in a scope before checking any function body, so such forward references are legal there; the
    /// body only executes once the binding is initialized. The real type is installed when
    /// VisitVar/VisitConst runs during the sequential pass, overwriting the <c>any</c> placeholder,
    /// so a reference appearing AFTER the declaration keeps its precise type.
    ///
    /// Mirrors <see cref="HoistVarDeclarations"/> (var) and <see cref="HoistClassDeclarations"/>
    /// (class — also block-scoped/TDZ). Like those, this trades precise TDZ diagnostics for
    /// forward-reference support: a *direct* use-before-declaration (e.g. <c>console.log(x); let x = 1;</c>)
    /// no longer reports a static error, but still fails at runtime because the interpreter/compiler
    /// only bind the name when the declaration statement runs. The <c>IsDefinedLocally</c> guard
    /// preserves the precise function type that <c>HoistConstFunctionExpressions</c> may already have
    /// registered for a <c>const f = () =&gt; …</c> arrow.
    /// </summary>
    private void HoistLexicalDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                // `let` is Stmt.Var with IsVar == false; `var` is handled by HoistVarDeclarations.
                case Stmt.Var v when !v.IsVar:
                    RegisterValueDeclaration(v.Name);
                    if (!_environment.IsDefinedLocally(v.Name.Lexeme))
                        _environment.Define(v.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
                case Stmt.Const c:
                    RegisterValueDeclaration(c.Name);
                    if (!_environment.IsDefinedLocally(c.Name.Lexeme))
                        _environment.Define(c.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
                case Stmt.Export { Declaration: Stmt.Var v } when !v.IsVar:
                    RegisterValueDeclaration(v.Name);
                    if (!_environment.IsDefinedLocally(v.Name.Lexeme))
                        _environment.Define(v.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
                case Stmt.Export { Declaration: Stmt.Const c }:
                    RegisterValueDeclaration(c.Name);
                    if (!_environment.IsDefinedLocally(c.Name.Lexeme))
                        _environment.Define(c.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
            }
        }
    }

    /// <summary>
    /// Pre-registers top-level class declarations by defining their names as 'any' in the type
    /// environment. This enables forward references to classes from within function bodies that
    /// appear before the class declaration — a common pattern in CJS libraries where functions
    /// reference classes defined later in the same file. The real class type is defined when
    /// CheckClassDeclaration runs during the sequential pass, overwriting the Any placeholder.
    /// </summary>
    /// <summary>
    /// Source line of each class's textual declaration, keyed by name. Populated during hoisting so
    /// a later <c>extends</c> clause can detect a forward reference (TS2449 — using a class before
    /// its declaration; class values, unlike types, are not hoisted for that position).
    /// </summary>
    private readonly Dictionary<string, int> _classDeclarationLines = [];

    private void HoistClassDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Class cls:
                    RegisterValueDeclaration(cls.Name);
                    _classDeclarationLines[cls.Name.Lexeme] = cls.Name.Line;
                    if (!_environment.IsDefinedLocally(cls.Name.Lexeme))
                        _environment.Define(cls.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
                case Stmt.Export { Declaration: Stmt.Class exportCls }:
                    RegisterValueDeclaration(exportCls.Name);
                    _classDeclarationLines[exportCls.Name.Lexeme] = exportCls.Name.Line;
                    if (!_environment.IsDefinedLocally(exportCls.Name.Lexeme))
                        _environment.Define(exportCls.Name.Lexeme, TypeInfo.Any.Shared);
                    break;
            }
        }
    }

    /// <summary>
    /// Hoists const/var declarations with function expression initializers.
    /// This enables mutual recursion between function expressions declared with const/var.
    /// Only registers the function type, does not check the function body.
    /// </summary>
    private void HoistConstFunctionExpressions(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Const constStmt when constStmt.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(constStmt.Name, constStmt.TypeAnnotation, arrow, constStmt.TypeAnnotationNode);
                    break;

                case Stmt.Var varStmt when varStmt.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(varStmt.Name, varStmt.TypeAnnotation, arrow, varStmt.TypeAnnotationNode);
                    break;

                case Stmt.Export export when export.Declaration is Stmt.Const exportedConst
                    && exportedConst.Initializer is Expr.ArrowFunction arrow:
                    HoistConstFunctionExpression(exportedConst.Name, exportedConst.TypeAnnotation, arrow, exportedConst.TypeAnnotationNode);
                    break;
            }
        }
    }

    /// <summary>
    /// Hoists a single const function expression by registering its type without checking the body.
    /// </summary>
    private void HoistConstFunctionExpression(Token name, string? typeAnnotation, Expr.ArrowFunction arrow, TypeNode? typeAnnotationNode = null)
    {
        RegisterValueDeclaration(name);

        // Skip if already defined
        if (_environment.IsDefinedLocally(name.Lexeme)) return;

        // An explicit annotation is authoritative — hoist with IT, not with the arrow's
        // inferred shape (which collapses unannotated params to any and then trips the TS2403
        // redeclaration check when the real declaration is visited). Resolve node-first so the
        // hoisted type matches VisitVar's resolution exactly (substitution-origin marks and all);
        // a string/node split here re-trips TS2403 on identical renderings.
        if (typeAnnotation is not null)
        {
            try { _environment.Define(name.Lexeme, ResolveAnnotation(typeAnnotation, typeAnnotationNode)!); }
            catch { _environment.Define(name.Lexeme, TypeInfo.Any.Shared); }
            return;
        }

        try
        {
            // Build parameter types from the arrow function's parameter declarations
            var paramTypes = new List<TypeInfo>();
            int requiredParams = 0;
            bool hasRest = false;

            foreach (var param in arrow.Parameters)
            {
                TypeInfo paramType = ResolveAnnotation(param.Type, param.TypeAnnotationNode)
                    ?? TypeInfo.Any.Shared;

                if (param.IsRest)
                {
                    hasRest = true;
                    paramTypes.Add(new TypeInfo.Array(paramType));
                }
                else
                {
                    paramTypes.Add(paramType);
                    if (param.DefaultValue == null && !param.IsOptional)
                        requiredParams++;
                }
            }

            // Determine return type from declaration or annotation
            TypeInfo returnType;
            if (typeAnnotation != null)
            {
                // Use the declared type annotation on the const
                var declaredType = ResolveAnnotation(typeAnnotation, typeAnnotationNode);
                if (declaredType is TypeInfo.Function funcType)
                {
                    returnType = funcType.ReturnType;
                }
                else
                {
                    returnType = TypeInfo.Any.Shared;
                }
            }
            else if (arrow.ReturnType != null)
            {
                // Use the return type from the arrow function
                returnType = ResolveAnnotation(arrow.ReturnType, arrow.ReturnTypeNode)!;
            }
            else
            {
                // Neither the binding nor the arrow annotates a return type. A precise
                // `(params) => any` placeholder here looks like a REAL prior declaration to
                // VisitVar's TS2403 redeclaration check once the real (usually non-`any`)
                // inferred return type is established for this var's OWN first-and-only
                // declaration — falsely tripping "subsequent variable declarations must have
                // the same type". Register a bare `any` instead, matching how
                // HoistVarDeclarations/HoistClassDeclarations placeholder forward references —
                // `any` is already exempt from that check.
                _environment.Define(name.Lexeme, TypeInfo.Any.Shared);
                return;
            }

            // Handle 'this' type
            TypeInfo? thisType = ResolveAnnotation(arrow.ThisType, arrow.ThisTypeNode);
            if (arrow.HasOwnThis && thisType == null)
            {
                thisType = TypeInfo.Any.Shared;
            }

            // Build and register the function type
            var funcType2 = new TypeInfo.Function(paramTypes, returnType, requiredParams, hasRest, thisType);
            _environment.Define(name.Lexeme, funcType2);
        }
        catch
        {
            // If type resolution fails, skip hoisting - will be defined during normal processing
        }
    }

    /// <summary>
    /// Hoists a single function declaration by registering its type without checking the body.
    /// If type resolution fails (e.g., references undefined types), the function will be
    /// defined later during normal statement processing.
    /// </summary>
    private void HoistSingleFunction(Stmt.Function funcStmt)
    {
        string funcName = funcStmt.Name.Lexeme;
        RegisterValueDeclaration(funcStmt.Name, mergeWithLocal: true);

        // Skip if already defined (e.g., from overload processing)
        // But allow overwriting built-in Any types (like process, console) with user definitions
        if (_environment.IsDefinedLocally(funcName))
        {
            var existingType = _environment.Get(funcName);
            if (existingType is not TypeInfo.Any)
                return;
        }

        try
        {
            // Build the function type
            TypeEnvironment funcEnv = new(_environment);

            // Set up environment for parsing type parameters and constraints
            TypeEnvironment previousEnvForParsing = _environment;
            _environment = funcEnv;

            // Handle generic type parameters (constraints may reference other parameters, incl.
            // multi-level chains like `T extends U extends V`).
            List<TypeInfo.TypeParameter>? typeParams =
                funcStmt.TypeParams is { Count: > 0 } tps ? BuildGenericTypeParameters(tps, funcEnv) : null;

            // Parse parameter types and return type (environment already set above)

            try
            {
                var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
                    funcStmt.Parameters,
                    validateDefaults: false, // Don't validate defaults during hoisting
                    contextName: $"function '{funcStmt.Name.Lexeme}'"
                );

                TypeInfo returnType = ResolveAnnotation(funcStmt.ReturnType, funcStmt.ReturnTypeNode)
                    ?? TypeInfo.Any.Shared; // Any during hoisting — real type inferred when body is checked

                TypeInfo? thisType = ResolveAnnotation(funcStmt.ThisType, funcStmt.ThisTypeNode);

                // Restore environment before defining function type
                _environment = previousEnvForParsing;

                // Handle generator return types
                TypeInfo funcReturnType = returnType;
                if (funcStmt.IsGenerator)
                {
                    if (funcStmt.IsAsync && returnType is not TypeInfo.AsyncGenerator)
                    {
                        funcReturnType = new TypeInfo.AsyncGenerator(returnType);
                    }
                    else if (!funcStmt.IsAsync && returnType is not TypeInfo.Generator)
                    {
                        funcReturnType = new TypeInfo.Generator(returnType);
                    }
                }

                // Build the appropriate function type
                TypeInfo funcType;
                if (typeParams != null && typeParams.Count > 0)
                {
                    funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
                }
                else
                {
                    funcType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType, paramNames);
                }

                // Register the function type (hoisting)
                _environment.Define(funcName, funcType);
            }
            catch
            {
                // Restore environment on failure
                _environment = previousEnvForParsing;
                throw;
            }
        }
        catch
        {
            // If type resolution fails during hoisting (e.g., references undefined interface),
            // skip hoisting - the function will be defined normally during statement processing
        }
    }

    /// <summary>
    /// Handle function declarations including overloaded functions.
    /// </summary>
    private void CheckFunctionDeclaration(Stmt.Function funcStmt)
    {
        RegisterValueDeclaration(funcStmt.Name, mergeWithLocal: true);
        ValidateCircularTypeParameterConstraints(funcStmt.TypeParams);

        string funcName = funcStmt.Name.Lexeme;

        // Build the function type for this declaration
        TypeEnvironment funcEnv = new(_environment);

        // Set up environment for parsing type parameters and constraints
        TypeEnvironment previousEnvForParsing = _environment;
        _environment = funcEnv;

        // Handle generic type parameters (constraints may reference other parameters, incl.
        // multi-level chains like `T extends U extends V`).
        List<TypeInfo.TypeParameter>? typeParams =
            funcStmt.TypeParams is { Count: > 0 } tps2 ? BuildGenericTypeParameters(tps2, funcEnv) : null;

        // Parse parameter types and return type (environment already set above)

        var (paramTypes, requiredParams, hasRest, paramNames) = BuildFunctionSignature(
            funcStmt.Parameters,
            validateDefaults: true,
            contextName: $"function '{funcStmt.Name.Lexeme}'"
        );

        // A return/this type may reference a parameter via `typeof param`
        // (e.g. `declare function f(cb: T): typeof cb`). Make the parameters visible in the
        // signature-parsing scope so the query resolves. funcEnv is discarded after the signature
        // is built (restored below), so these bindings don't leak into the body or outer scope.
        for (int i = 0; i < paramNames.Count; i++)
            funcEnv.Define(paramNames[i], paramTypes[i]);

        bool inferringReturnType = funcStmt.ReturnType == null;
        TypeInfo returnType = ResolveAnnotation(funcStmt.ReturnType, funcStmt.ReturnTypeNode)
            ?? TypeInfo.Inferred.Shared;

        // Validate type predicate return types
        if (!inferringReturnType)
            ValidateTypePredicateReturnType(returnType, funcStmt.Parameters, funcStmt.Name.Lexeme);

        // Parse explicit 'this' type if present
        TypeInfo? thisType = ResolveAnnotation(funcStmt.ThisType, funcStmt.ThisTypeNode);

        _environment = previousEnvForParsing;

        // For generator functions, wrap the return type in Generator<> or AsyncGenerator<> if not already
        // Skip wrapping when inferring — wrapping is done after inference resolves
        TypeInfo funcReturnType = returnType;
        if (!inferringReturnType && funcStmt.IsGenerator)
        {
            if (funcStmt.IsAsync && returnType is not TypeInfo.AsyncGenerator)
            {
                funcReturnType = new TypeInfo.AsyncGenerator(returnType);
            }
            else if (!funcStmt.IsAsync && returnType is not TypeInfo.Generator)
            {
                funcReturnType = new TypeInfo.Generator(returnType);
            }
        }

        var thisFuncType = new TypeInfo.Function(paramTypes, funcReturnType, requiredParams, hasRest, thisType, paramNames);
        var overloadKey = (_environment, funcName);

        // Check if this is an overload signature (no body)
        if (funcStmt.Body == null)
        {
            // A preparatory pass may already have installed this exact declaration's signature in
            // this environment. Do not turn a single ambient function into a two-entry overload
            // merely because the authoritative pass visits it again.
            if (!MarkBodylessFunctionCompleted(funcStmt))
                return;

            // This is an overload signature - save for later
            if (!_pendingOverloadSignatures.TryGetValue(overloadKey, out var signatures))
            {
                signatures = [];
                _pendingOverloadSignatures[overloadKey] = signatures;
            }
            signatures.Add(thisFuncType);

            // Also save type parameters if this is a generic overload
            if (typeParams != null && typeParams.Count > 0)
            {
                _pendingOverloadTypeParams[overloadKey] = typeParams;
            }

            // Ambient (`declare function`): there is no implementation to come — the declaration
            // itself defines the function. Define with the signatures accumulated so far; a
            // following ambient overload of the same name re-defines with the grown set.
            if (funcStmt.IsDeclare)
            {
                TypeInfo ambientType = signatures.Count > 1
                    ? new TypeInfo.OverloadedFunction(new List<TypeInfo.Function>(signatures), signatures[^1])
                    : typeParams is { Count: > 0 }
                        ? new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames)
                        : thisFuncType;
                if (typeParams is { Count: > 0 } && signatures.Count > 1)
                    ambientType = new TypeInfo.GenericOverloadedFunction(typeParams, new List<TypeInfo.Function>(signatures), signatures[^1]);
                _environment.Define(funcName, ambientType);
            }
            return;
        }

        // This is an implementation (has a body)
        ReportImplicitAnyParameters(funcStmt.Parameters, isAmbient: funcStmt.IsDeclare);

        TypeInfo funcType;

        // Check if there are pending overload signatures for this function
        if (_pendingOverloadSignatures.TryGetValue(overloadKey, out var pendingSignatures))
        {
            // Validate implementation is compatible with all signatures
            foreach (var sig in pendingSignatures)
            {
                if (thisFuncType.MinArity > sig.MinArity)
                {
                    throw new TypeCheckException($" Implementation of '{funcName}' requires {thisFuncType.MinArity} arguments but overload signature requires only {sig.MinArity}.", tsCode: "TS2394");
                }
            }

            // Check if we have type parameters (generic overloaded function)
            if (_pendingOverloadTypeParams.TryGetValue(overloadKey, out var overloadTypeParams))
            {
                // Create generic overloaded function type
                funcType = new TypeInfo.GenericOverloadedFunction(overloadTypeParams, pendingSignatures, thisFuncType);
                _pendingOverloadTypeParams.Remove(overloadKey);
            }
            else
            {
                // Create non-generic overloaded function type
                funcType = new TypeInfo.OverloadedFunction(pendingSignatures, thisFuncType);
            }

            // Clear pending signatures
            _pendingOverloadSignatures.Remove(overloadKey);
        }
        else if (typeParams != null && typeParams.Count > 0)
        {
            // Generic function (no overloads)
            funcType = new TypeInfo.GenericFunction(typeParams, paramTypes, returnType, requiredParams, hasRest, thisType, paramNames);
        }
        else
        {
            // Regular function (no overloads)
            funcType = thisFuncType;
        }

        // Define or update the function type (may have been hoisted earlier)
        // For overloaded functions, we need to update with the complete type
        if (!_environment.IsDefinedLocally(funcName) || funcType is TypeInfo.OverloadedFunction or TypeInfo.GenericOverloadedFunction)
        {
            _environment.Define(funcName, funcType);
        }

        // Register function type for typed compilation
        // For overloaded functions, use the implementation type
        if (funcType is TypeInfo.Function ft)
        {
            _typeMap.SetFunctionType(funcName, ft);
        }
        else if (funcType is TypeInfo.OverloadedFunction of)
        {
            _typeMap.SetFunctionType(funcName, of.Implementation);
        }
        else if (funcType is TypeInfo.GenericOverloadedFunction gof)
        {
            // For generic overloaded functions, use the implementation type
            _typeMap.SetFunctionType(funcName, gof.Implementation);
        }
        else if (funcType is TypeInfo.GenericFunction gf)
        {
            // For generic functions, store a function type with the unsubstituted types
            _typeMap.SetFunctionType(funcName, new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType));
        }

        // Check the body and (when un-annotated) resolve & register the inferred return type.
        CheckFunctionBodyAndInferReturn(
            funcStmt, funcEnv, paramTypes, requiredParams, hasRest, paramNames,
            thisType, typeParams, returnType, inferringReturnType, funcName, suppress: false);
    }

    /// <summary>
    /// Binds parameters and checks a function body, resolving and registering the inferred return
    /// type when the declaration carries no explicit return annotation. Shared by the normal
    /// declaration pass (<paramref name="suppress"/> = false) and by hoisting's speculative
    /// pre-resolution (<paramref name="suppress"/> = true, issue #383).
    ///
    /// In suppressed mode diagnostics are swallowed and the body is checked in recovery mode so
    /// every <c>return</c> is seen; any failure degrades to leaving the hoisted <c>any</c>
    /// placeholder. The real declaration pass re-checks the same body and reports diagnostics at
    /// their proper locations, so suppressed checking never produces a duplicate or final error.
    /// </summary>
    private void CheckFunctionBodyAndInferReturn(
        Stmt.Function funcStmt, TypeEnvironment funcEnv,
        List<TypeInfo> paramTypes, int requiredParams, bool hasRest, List<string> paramNames,
        TypeInfo? thisType, List<TypeInfo.TypeParameter>? typeParams,
        TypeInfo returnType, bool inferringReturnType, string funcName, bool suppress)
    {
        // Both callers guarantee a body: CheckFunctionDeclaration returns early for body-less
        // overload/ambient signatures, and RefineHoistedFunctionReturnType filters them out.
        List<Stmt> body = funcStmt.Body!;

        // The enclosing environment, where the (possibly refined) function type is registered.
        TypeEnvironment previousEnv = _environment;

        // Add parameters to function environment and check body. Body-scope binding widens a bare
        // `?`-optional parameter with `| undefined` (the caller may omit it) — the function's own
        // callable signature (paramTypes, used for thisFuncType et al.) keeps the declared type.
        var bodyParamTypes = WidenOptionalParamsForBody(paramTypes, funcStmt.Parameters);
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            DeclareValue(
                funcEnv,
                funcStmt.Parameters[i].Name,
                bodyParamTypes[i]);
        }

        // Save and set context - function bodies are isolated from outer loop/switch/label context
        TypeInfo? previousReturn = _currentFunctionReturnType;
        TypeInfo? previousThisType = _currentFunctionThisType;
        bool previousInAsync = _inAsyncFunction;
        bool previousInGenerator = _inGeneratorFunction;
        int previousLoopDepth = _loopDepth;
        int previousSwitchDepth = _switchDepth;
        var previousActiveLabels = new Dictionary<string, ActiveLabel>(_activeLabels);
        bool previousRecoveryMode = _recoveryMode;

        var previousInferredReturnTypes = _inferredReturnTypes;
        var previousInferredYieldTypes = _inferredYieldTypes;

        _environment = funcEnv;
        if (inferringReturnType)
        {
            _inferredReturnTypes = new List<TypeInfo>();
            _currentFunctionReturnType = TypeInfo.Inferred.Shared;
        }
        else
        {
            _currentFunctionReturnType = returnType;
        }
        // Collect yield operand types only while inferring a generator's type (#548). Set to null otherwise
        // so a nested explicitly-typed generator's yields cannot leak into an enclosing inferred one.
        _inferredYieldTypes = inferringReturnType && funcStmt.IsGenerator ? new List<TypeInfo>() : null;
        _currentFunctionThisType = thisType;
        _inAsyncFunction = funcStmt.IsAsync;
        _inGeneratorFunction = funcStmt.IsGenerator;
        _loopDepth = 0;
        _switchDepth = 0;
        _activeLabels.Clear();
        // Conformance/program checking collects diagnostics with recovery. Continue across
        // independent statements in every function body so one property or assignment error does
        // not hide later diagnostics in that body; standalone fail-fast checks retain their
        // statement-atomic behavior.
        if (suppress || _currentModule != null)
            _recoveryMode = true;
        if (suppress)
        {
            // Collect-and-discard: continue past errors to observe every `return`, record nothing.
            _suppressDiagnostics++;
        }

        // Push a new scope for declared variable types and record parameter types (widened, matching
        // the environment binding above — so a later reassignment narrows/widens against the TRUE
        // declared type, which for a bare-optional parameter includes undefined).
        PushDeclaredVariableScope();
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            RecordDeclaredType(funcStmt.Parameters[i].Name.Lexeme, bodyParamTypes[i]);
        }

        // Enter escape analysis scope and register parameters as local variables
        _escapeAnalyzer.EnterScope();
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            _escapeAnalyzer.DefineVariable(funcStmt.Parameters[i].Name.Lexeme);
        }

        // Isolate the narrowing context stack for this function body. Without this,
        // narrowings added by `if (x) return;` via AddNarrowing persist on the stack
        // and leak into sibling functions with parameters of the same name.
        PushEmptyNarrowingScope();

        try
        {
            bool bodyFailed = false;
            try
            {
                // Hoist inner function declarations so forward references resolve.
                // JS spec hoists all `function` decls to the top of the containing
                // scope before any statement executes; the TypeChecker needs to
                // mirror that to accept well-formed mutually-recursive inner fns.
                HoistFunctionDeclarations(body);

                // Hoist the body's own let/const (as `any`) so an inner function declared before a
                // later block-scoped binding in the SAME body can forward-reference it (#533). `var`
                // is already lifted to the body top by the parse-time VarHoister, so it is not repeated
                // here; let/const are not physically moved (TDZ), so they need this registration.
                HoistLexicalDeclarations(body);

                CheckStmtList(body);

                // #367/#372: object-slot any number/boolean-typed local, parameter, or return that an
                // `any`/`undefined` assignment may have left holding the undefined sentinel. Runs after
                // the body so every sub-expression type is known. Skipped while speculating — it only
                // matters for the emitted output of the real pass.
                if (!suppress)
                    MarkUndefinedReachableNumericSlots(body, funcStmt.Parameters);
            }
            catch (Exception) when (suppress)
            {
                // Speculative inference is best-effort: degrade to the `any` placeholder on any failure.
                bodyFailed = true;
            }

            // Resolve inferred return type from collected return expressions
            if (inferringReturnType && !bodyFailed)
            {
                var collected = _inferredReturnTypes!;

                TypeInfo inferredReturn;
                if (collected.Count == 0)
                {
                    inferredReturn = TypeInfo.Void.Shared;
                }
                else
                {
                    var distinct = collected.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                    inferredReturn = CollapseOrCreateUnion(distinct);
                }

                // Apply async/generator wrapping. A generator's type argument is its YIELD type (collected
                // above), not the `return`-derived inferredReturn (#548); a non-generator async function
                // wraps its return type in a Promise.
                if (funcStmt.IsGenerator)
                    inferredReturn = BuildInferredGeneratorType(_inferredYieldTypes!, funcStmt.IsAsync);
                else if (funcStmt.IsAsync && inferredReturn is not TypeInfo.Void)
                    inferredReturn = new TypeInfo.Promise(inferredReturn);

                returnType = inferredReturn;

                // Update the function type in the enclosing environment
                var updatedFuncType = typeParams != null && typeParams.Count > 0
                    ? (TypeInfo)new TypeInfo.GenericFunction(typeParams, paramTypes, inferredReturn, requiredParams, hasRest, thisType, paramNames)
                    : new TypeInfo.Function(paramTypes, inferredReturn, requiredParams, hasRest, thisType, paramNames);
                previousEnv.Define(funcName, updatedFuncType);
                if (suppress)
                {
                    // Memoize so later passes re-register without re-checking the body.
                    _hoistInferredReturnTypes[funcStmt] = inferredReturn;
                }
                else
                {
                    if (updatedFuncType is TypeInfo.Function uf)
                        _typeMap.SetFunctionType(funcName, uf);
                    else if (updatedFuncType is TypeInfo.GenericFunction gf2)
                        _typeMap.SetFunctionType(funcName, new TypeInfo.Function(gf2.ParamTypes, gf2.ReturnType, gf2.RequiredParams, gf2.HasRestParam, gf2.ThisType));
                }
            }

            // Validate that non-void functions return a value on all code paths
            // Skip for void, generators (which use yield), async functions (which return Promise),
            // assertion predicates, and inferred return types (which may legitimately be void)
            if (_strictNullChecks && !inferringReturnType &&
                returnType is not TypeInfo.Void &&
                returnType is not TypeInfo.Generator &&
                returnType is not TypeInfo.AsyncGenerator &&
                returnType is not TypeInfo.TypePredicate { IsAssertion: true } &&
                returnType is not TypeInfo.AssertsNonNull &&
                !funcStmt.IsGenerator &&
                !funcStmt.IsAsync)
            {
                if (!DoesBlockDefinitelyReturn(body))
                {
                    throw new TypeCheckException($" Function '{funcStmt.Name.Lexeme}' must return a value of type '{returnType}'.", tsCode: "TS2366");
                }
            }
        }
        finally
        {
            PopNarrowingScope();

            // Pop the declared variable types scope
            PopDeclaredVariableScope();

            // Exit escape analysis scope
            _escapeAnalyzer.ExitScope();

            _environment = previousEnv;
            _currentFunctionReturnType = previousReturn;
            _inferredReturnTypes = previousInferredReturnTypes;
            _inferredYieldTypes = previousInferredYieldTypes;
            _currentFunctionThisType = previousThisType;
            _inAsyncFunction = previousInAsync;
            _inGeneratorFunction = previousInGenerator;
            _loopDepth = previousLoopDepth;
            _switchDepth = previousSwitchDepth;
            _activeLabels.Clear();
            foreach (var kvp in previousActiveLabels)
                _activeLabels[kvp.Key] = kvp.Value;
            _recoveryMode = previousRecoveryMode;
            if (suppress)
            {
                _suppressDiagnostics--;
            }
        }
    }

    /// <summary>
    /// Builds the inferred type of a generator function from the operand types of its <c>yield</c> /
    /// <c>yield*</c> expressions (collected in <see cref="_inferredYieldTypes"/> during body checking),
    /// wrapped in <see cref="TypeInfo.Generator"/> or <see cref="TypeInfo.AsyncGenerator"/>. The type
    /// argument is the YIELD type, not the function's <c>return</c> value (#548 — previously the inferred
    /// return type was reused, so a generator with no <c>return</c> always inferred <c>void</c>). A
    /// generator that yields nothing infers <c>never</c>, matching tsc's <c>Generator&lt;never, …&gt;</c>.
    /// </summary>
    private TypeInfo BuildInferredGeneratorType(IReadOnlyList<TypeInfo> collectedYields, bool isAsync)
    {
        TypeInfo yieldType;
        if (collectedYields.Count == 0)
        {
            yieldType = TypeInfo.Never.Shared;
        }
        else
        {
            var distinct = collectedYields.Distinct(TypeInfoEqualityComparer.Instance).ToList();
            yieldType = CollapseOrCreateUnion(distinct);
        }

        return isAsync ? new TypeInfo.AsyncGenerator(yieldType) : new TypeInfo.Generator(yieldType);
    }

    /// <summary>
    /// Validates that type predicate return types reference valid parameter names.
    /// </summary>
    private void ValidateTypePredicateReturnType(TypeInfo returnType, List<Stmt.Parameter> parameters, string funcName)
    {
        string? paramToCheck = null;

        if (returnType is TypeInfo.TypePredicate pred)
        {
            paramToCheck = pred.ParameterName;
        }
        else if (returnType is TypeInfo.AssertsNonNull assertsNonNull)
        {
            paramToCheck = assertsNonNull.ParameterName;
        }

        if (paramToCheck != null)
        {
            // Check if the parameter exists in the function signature
            bool paramExists = parameters.Any(p => p.Name.Lexeme == paramToCheck);

            // Also allow 'this' as a valid target for type predicates
            if (!paramExists && paramToCheck != "this")
            {
                throw new TypeCheckException(
                    $" Type predicate in function '{funcName}' references parameter '{paramToCheck}' which is not in the function signature.", tsCode: "TS2304");
            }
        }
    }
}
