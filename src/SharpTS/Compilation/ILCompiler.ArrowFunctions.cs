using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Arrow function collection and emission methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private readonly List<(Expr.ArrowFunction Arrow, HashSet<string> Captures)> _collectedArrows = [];
    private readonly HashSet<Expr.ArrowFunction> _strictArrows = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.ArrowFunction> _arrowsInAnonymousEmptyDerivedClass = new(ReferenceEqualityComparer.Instance);
    // Maps each collected arrow to the module path that owns it (null = script/single-file
    // scope). Set during collection so body emission can restore the correct
    // per-module view of TopLevelStaticVars / captured-top-level-var fields —
    // body emission otherwise runs with a stale _modules.CurrentPath and
    // resolves against the wrong module's storage.
    private readonly Dictionary<Expr.ArrowFunction, string?> _arrowToModule = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, Expr.ArrowFunction?> _arrowParent = new(ReferenceEqualityComparer.Instance);
    // Immediately enclosing callable (Expr.ArrowFunction or Stmt.Function) of each
    // collected arrow — i.e. the body whose emission context creates the arrow's
    // display class. Unlike _arrowParent this does NOT skip function-declaration
    // boundaries; used to thread $arrowScopeDC references for inner functions (#307).
    private readonly Dictionary<Expr.ArrowFunction, object?> _arrowEnclosingCallable = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.ArrowFunction> _arrowsNeedingFunctionDC = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.ArrowFunction> _arrowsNeedingArrowDC = new(ReferenceEqualityComparer.Instance);
    // Maps each collected arrow (or `function` expression) to the AST node of
    // the top-level function that lexically contains it. Needed so
    // PropagateFunctionDCRequirements matches `$functionDC` field lookups
    // against the CORRECT enclosing function — not just any function whose
    // display class happens to have a field with a matching name. Before
    // this was tracked, two sibling module-level functions with a same-named
    // parameter (e.g. both taking `fn`) aliased onto whichever enclosing
    // function's display class was enumerated first, causing the later
    // function's captured param to read back null at runtime.
    //
    // We store the AST node rather than a qualified name because
    // CollectAndDefineArrowFunctions runs after module context is cleared, so
    // name qualification isn't available here. The AST node is stable; we
    // resolve it to the qualified name at propagate time via FunctionAstNodes.
    private readonly Dictionary<Expr.ArrowFunction, Stmt.Function> _arrowEnclosingFunction = new(ReferenceEqualityComparer.Instance);
    // Follow-up to #838: the nearest enclosing ASYNC ARROW of each collected arrow. An async arrow plays
    // the role of a "function" scope for a nested sync arrow that captures-and-writes one of its locals:
    // PropagateFunctionDCRequirements resolves the sync arrow's DC source to the async arrow's display
    // class. Separate from _arrowEnclosingFunction because the enclosing scope is an Expr.ArrowFunction.
    private readonly Dictionary<Expr.ArrowFunction, Expr.ArrowFunction> _arrowEnclosingAsyncArrow = new(ReferenceEqualityComparer.Instance);
    private Stmt.Function? _currentEnclosingFunctionStmt;
    private Expr.ArrowFunction? _currentEnclosingAsyncArrow;
    private Expr.ArrowFunction? _currentParentArrow;
    private string? _currentCollectClassName;
    // The IMMEDIATELY enclosing callable (Expr.ArrowFunction or Stmt.Function) during
    // collection. Unlike _currentParentArrow (nearest ancestor arrow, which skips
    // function-declaration boundaries), this tells CollectInnerFunction whether an
    // inner function declaration is hoisted directly into an arrow's body — the
    // case that needs a live $arrowScopeDC reference instead of value snapshots (#307).
    private object? _currentEnclosingCallable;

    private void CollectAndDefineArrowFunctions(List<Stmt> statements)
    {
        CollectArrowsFromStatementsInCurrentModule(statements);
        FinalizeArrowFunctionCollection();
    }

    /// <summary>
    /// Walks statements under the current module context to populate
    /// <see cref="_collectedArrows"/> and the arrow→module map. Module compilation
    /// calls this once per module (with <c>_modules.CurrentPath</c> set);
    /// single-file compilation calls it once with the path unset.
    /// </summary>
    private void CollectArrowsFromStatementsInCurrentModule(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            CollectArrowsFromStmt(stmt);
        }

        // Top-level only: register `const NAME = (args) => …` (and the
        // `export const` form) so iterator-helper fast paths can resolve
        // `Expr.Variable` callbacks like `arr.map(myFn)` to the literal
        // arrow's AST node and inline through TryEmitArrowAsDelegate.
        // Nested const bindings would require scope-aware shadowing analysis;
        // out of scope for this pass.
        foreach (var stmt in statements)
        {
            RegisterTopLevelConstArrowBinding(stmt);
        }
    }

    private void RegisterTopLevelConstArrowBinding(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Const c when c.Initializer is Expr.ArrowFunction af:
                // Last-write-wins is fine: TS forbids redeclaring a `const`,
                // so the parser/checker would already have rejected dupes.
                _closures.ConstArrowBindings[c.Name.Lexeme] = af;
                break;
            case Stmt.Export exp when exp.Declaration != null:
                RegisterTopLevelConstArrowBinding(exp.Declaration);
                break;
        }
    }

    /// <summary>
    /// Runs the cross-module finalization pass — propagates DC requirements,
    /// defines scope display classes, and defines per-arrow methods/display classes.
    /// Must run AFTER every module has been walked via
    /// <see cref="CollectArrowsFromStatementsInCurrentModule"/>.
    /// </summary>
    private void FinalizeArrowFunctionCollection()
    {
        // #789: register function display classes for class-EXPRESSION generator methods whose arrows
        // write a captured method local. Class declarations do this in Phase 4 (DefineClass); class
        // expressions are only collected during the Phase-5 arrow walk, so the registration is sequenced
        // here — after every module has been walked (all class-expr names assigned) and before the
        // PropagateFunctionDCRequirements pass below, which needs the function DC fields already present.
        RegisterClassExpressionGeneratorMethodFunctionDisplayClasses();

        // Follow-up to #838: register a function display class for each async arrow whose own local is
        // captured-and-written by a nested SYNC arrow, before PropagateFunctionDCRequirements (which
        // resolves those sync arrows to the async arrow's DC).
        RegisterAsyncArrowFunctionDisplayClasses();

        // Propagate function DC requirements through arrow nesting
        // If an inner arrow needs $functionDC, parent arrows also need it
        PropagateFunctionDCRequirements();

        // Define arrow scope display classes (for arrow-local vars captured by nested arrows)
        // Skip async arrows - their bodies are emitted by async state machine emitters,
        // which don't support arrow scope DC instantiation
        foreach (var (arrow, _) in _collectedArrows)
        {
            if (!arrow.IsAsync)
            {
                DefineScopeDisplayClass(arrow, "ArrowScopeDC");
            }
        }

        // Inner function declarations whose locals are captured by nested closures
        // get the same scope-DC treatment (#313) — without it, sibling closures
        // created in the same invocation each snapshot the local instead of
        // sharing storage. Async/generator inner functions are skipped for the
        // same reason async arrows are: their bodies go through state-machine
        // emitters that don't instantiate scope DCs.
        foreach (var (func, _, _) in _collectedInnerFunctions)
        {
            if (!func.IsAsync && !func.IsGenerator)
            {
                DefineScopeDisplayClass(func, $"FuncScopeDC_{func.Name.Lexeme}_");
            }
        }

        // Propagate arrow scope DC requirements through arrow nesting
        PropagateArrowDCRequirements();

        // Resolve which arrow scope DC (if any) each inner function declaration
        // reads its captures from, marking intermediate arrows that must carry
        // the reference (#307). Runs after arrows' own $arrowDC sources are
        // assigned so threading conflicts are detected, and before the define
        // loop below so marked arrows get their $arrowDC field.
        ResolveInnerFunctionArrowScopeSources();

        // Define methods and display classes
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // Skip async arrows - they're handled via DefineTopLevelAsyncArrows() or
            // DefineAsyncArrowStateMachines() (if inside an async function)
            if (arrow.IsAsync)
            {
                continue;
            }

            // Resolve typed parameters from annotations (optimization for numeric parameters).
            // Rest parameters always use List<object> to enable detection at invoke time.
            // ResolveParameters widens defaulted params to an object slot (#646/#705) so the
            // runtime entry prologue can detect the missing/undefined argument and fire the default.
            var resolvedParamTypes = ParameterTypeResolver.ResolveParameters(
                arrow.Parameters, _typeMapper, null, _typeMap); // null funcType - use annotations only

            // Store resolved types for use during arrow body emission
            _closures.ArrowParameterTypes[arrow] = resolvedParamTypes;

            // Resolve typed return type from TypeMap (optimization for typed returns)
            // Arrow functions can use typed returns - reflection automatically boxes at the boundary.
            // However, we must be careful with expression body arrows that produce void (like console.log).
            Type returnType = _types.Object;
            var arrowTypeInfo = _typeMap?.Get(arrow);
            if (arrowTypeInfo is TypeSystem.TypeInfo.Function funcTypeInfo)
            {
                // Widen a number/boolean slot to object if the checker flagged an undefined-reachable
                // return (e.g. `(): number => undefined as any`) — the unboxed slot would coerce it (#344).
                bool returnMayBeUndefined = arrow.ExpressionBody != null
                    ? ReturnSlotAnalysis.ExpressionReturnMayBeUndefined(arrow.ExpressionBody, _typeMap)
                    : ReturnSlotAnalysis.BlockReturnsMayBeUndefined(arrow.BlockBody, _typeMap);
                var logicalReturnType = ParameterTypeResolver.ResolveReturnType(
                    funcTypeInfo.ReturnType, isAsync: false, _typeMapper, returnMayBeUndefined);

                // Use typed return if:
                // 1. Return type is not void (void methods need special handling)
                // 2. For expression body arrows, the expression must produce a value
                if (logicalReturnType != typeof(void))
                {
                    // For expression body arrows, check if the expression produces void
                    // (like console.log) - in that case, keep return type as object
                    bool isVoidExpressionBody = false;
                    if (arrow.ExpressionBody != null)
                    {
                        var exprType = _typeMap?.Get(arrow.ExpressionBody);
                        isVoidExpressionBody = exprType is TypeSystem.TypeInfo.Void;
                    }

                    if (!isVoidExpressionBody)
                    {
                        returnType = logicalReturnType;
                    }
                }
            }
            _closures.ArrowReturnTypes[arrow] = returnType;

            // For object methods, add __this as the first parameter
            Type[] paramTypes;
            if (arrow.HasOwnThis)
            {
                paramTypes = new Type[arrow.Parameters.Count + 1];
                paramTypes[0] = _types.Object;  // __this
                for (int i = 0; i < arrow.Parameters.Count; i++)
                    paramTypes[i + 1] = arrow.Parameters[i].IsRest ? _types.ListOfObject : resolvedParamTypes[i];
            }
            else
            {
                paramTypes = new Type[arrow.Parameters.Count];
                for (int i = 0; i < arrow.Parameters.Count; i++)
                    paramTypes[i] = arrow.Parameters[i].IsRest ? _types.ListOfObject : resolvedParamTypes[i];
            }

            // Check if arrow needs function DC or arrow DC (for itself or to pass to inner arrows)
            bool needsFunctionDCForArrow = _arrowsNeedingFunctionDC.Contains(arrow);
            bool needsArrowDCForArrow = _arrowsNeedingArrowDC.Contains(arrow);

            if (captures.Count == 0 && !needsFunctionDCForArrow && !needsArrowDCForArrow)
            {
                // Non-capturing and doesn't need function DC: static method on $Program
                // Use typed return when available - reflection automatically boxes at $TSFunction boundary
                // Assembly (internal) so iterator-helper fast-path emitters
                // in $Module_X types can `ldftn` directly into these arrow
                // bodies on $Program. Synthetic methods (compiler-generated
                // names like `<>Arrow_N`); access modifier is an internal
                // detail with no observable JS semantics.
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_closures.ArrowMethodCounter++}",
                    MethodAttributes.Assembly | MethodAttributes.Static,
                    returnType,
                    paramTypes
                );

                // Define parameter names (important for InvokeWithThis to detect __this)
                if (arrow.HasOwnThis)
                {
                    methodBuilder.DefineParameter(1, ParameterAttributes.None, "__this");
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        methodBuilder.DefineParameter(i + 2, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                    // Back the name-based __this detection with an attribute that survives a --ref-asm
                    // parameter-name strip, so a value-call doesn't shift its arguments. (#738)
                    MarkExpectsThis(methodBuilder);
                }
                else
                {
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }

                _closures.ArrowMethods[arrow] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = EmitTypeDefinitions.DefineType(_moduleBuilder,
                    $"<>c__DisplayClass{_closures.DisplayClassCounter++}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    _types.Object
                );
                MarkCompilerGenerated(displayClass);

                // Determine if any captured vars are top-level captured vars
                bool needsEntryPointDC = _closures.EntryPointDisplayClass != null &&
                    captures.Any(c => _closures.CapturedTopLevelVars.Contains(c));

                // Check if this arrow needs function DC (either directly or to propagate to inner arrows)
                bool needsFunctionDC = _arrowsNeedingFunctionDC.Contains(arrow);
                string? sourceFunction = needsFunctionDC && _closures.ArrowFunctionDCSource.TryGetValue(arrow, out var src) ? src : null;

                // Check if this arrow needs a scope DC reference (source may be an
                // arrow or an inner function declaration, #313)
                bool needsArrowDC = _arrowsNeedingArrowDC.Contains(arrow);
                object? sourceArrow = needsArrowDC && _closures.ArrowScopeDCSource.TryGetValue(arrow, out var srcArrow) ? srcArrow : null;

                // Add fields for captured variables (except top-level, function-level, and arrow-scope captured vars)
                Dictionary<string, FieldBuilder> fieldMap = [];
                FieldBuilder? entryPointDCField = null;
                FieldBuilder? functionDCField = null;
                FieldBuilder? arrowDCField = null;

                if (needsEntryPointDC)
                {
                    // Add field to hold reference to entry-point display class
                    entryPointDCField = displayClass.DefineField("$entryPointDC", _closures.EntryPointDisplayClass!, FieldAttributes.Public);
                }

                if (needsFunctionDC && sourceFunction != null && _closures.FunctionDisplayClasses.TryGetValue(sourceFunction, out var funcDC))
                {
                    // Add field to hold reference to function display class
                    functionDCField = displayClass.DefineField("$functionDC", funcDC, FieldAttributes.Public);
                }

                if (needsArrowDC && sourceArrow != null && _closures.ArrowScopeDisplayClasses.TryGetValue(sourceArrow, out var arrowScopeDC))
                {
                    // Add field to hold reference to parent arrow scope display class
                    arrowDCField = displayClass.DefineField("$arrowDC", arrowScopeDC, FieldAttributes.Public);
                }

                // EXTRA ancestor scope DC references (multi-source captures) —
                // one field per additional source scope beyond the primary.
                Dictionary<object, FieldBuilder>? extraScopeDCFields = null;
                if (_arrowExtraScopeSources.TryGetValue(arrow, out var extraScopeSources))
                {
                    int extraIdx = 0;
                    foreach (var extraSource in extraScopeSources)
                    {
                        if (ReferenceEquals(extraSource, sourceArrow))
                            continue;
                        if (!_closures.ArrowScopeDisplayClasses.TryGetValue(extraSource, out var extraDC))
                            continue;
                        var refField = displayClass.DefineField($"$arrowDC{++extraIdx}", extraDC, FieldAttributes.Public);
                        (extraScopeDCFields ??= new(ReferenceEqualityComparer.Instance))[extraSource] = refField;
                    }
                    if (extraScopeDCFields != null)
                        _arrowScopeDCExtraFields[arrow] = extraScopeDCFields;
                }

                foreach (var capturedVar in captures)
                {
                    // Skip top-level captured vars - they'll be accessed through $entryPointDC.
                    // #1222 exception: when THIS arrow's capture is an unlifted top-level
                    // BLOCK-scoped binding, the same-named entry-DC field belongs to a
                    // DIFFERENT binding (typically the module-level one the block binding
                    // shadows — the declared-more-than-once name is exactly what the #1201
                    // lift declined). Give the capture its own copy field so it snapshots
                    // the creating scope's local like any ordinary local capture.
                    if (_closures.CapturedTopLevelVars.Contains(capturedVar) &&
                        !IsShadowedTopLevelBlockCapture(arrow, capturedVar))
                        continue;

                    // Skip function-level captured vars - they'll be accessed through $functionDC.
                    // But only skip if the function DC is from a SYNC function (always populated).
                    // For ASYNC functions, the arrow might be nested inside an async arrow where
                    // $functionDC won't be populated, so keep the arrow's own field as fallback.
                    if (needsFunctionDC && sourceFunction != null &&
                        _closures.FunctionDisplayClassFields.TryGetValue(sourceFunction, out var funcFields) &&
                        funcFields.ContainsKey(capturedVar) &&
                        !_async.StateMachines.ContainsKey(sourceFunction))
                        continue;

                    // Skip arrow-scope captured vars - they'll be accessed through $arrowDC
                    if (needsArrowDC && sourceArrow != null &&
                        _closures.ArrowScopeDisplayClassFields.TryGetValue(sourceArrow, out var arrowFields) &&
                        arrowFields.ContainsKey(capturedVar))
                        continue;

                    // Skip vars covered by an EXTRA ancestor scope DC reference
                    if (extraScopeDCFields != null &&
                        _closures.Analyzer.GetCaptureSource(arrow, capturedVar) is { } extraSrc &&
                        extraScopeDCFields.ContainsKey(extraSrc) &&
                        _closures.ArrowScopeDisplayClassFields.TryGetValue(extraSrc, out var extraSrcFields) &&
                        extraSrcFields.ContainsKey(capturedVar))
                        continue;

                    var field = displayClass.DefineField(capturedVar, _types.Object, FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _closures.DisplayClassFields[arrow] = fieldMap;

                // Track $entryPointDC field for this arrow
                if (entryPointDCField != null)
                {
                    _closures.ArrowEntryPointDCFields[arrow] = entryPointDCField;
                }

                // Track $functionDC field for this arrow
                if (functionDCField != null)
                {
                    _closures.ArrowFunctionDCFields[arrow] = functionDCField;
                }

                // Track $arrowDC field for this arrow
                if (arrowDCField != null)
                {
                    _closures.ArrowScopeDCFields[arrow] = arrowDCField;
                }

                // Add default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
                ctorIL.Emit(OpCodes.Ret);
                _closures.DisplayClassConstructors[arrow] = ctorBuilder;

                // Add Invoke method
                // Use typed return when available - reflection automatically boxes at $TSFunction boundary
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    returnType,
                    paramTypes
                );

                // Define parameter names (important for InvokeWithThis to detect __this)
                if (arrow.HasOwnThis)
                {
                    invokeMethod.DefineParameter(1, ParameterAttributes.None, "__this");
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        invokeMethod.DefineParameter(i + 2, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                    // Survive a --ref-asm parameter-name strip (see the sibling site above). (#738)
                    MarkExpectsThis(invokeMethod);
                }
                else
                {
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        invokeMethod.DefineParameter(i + 1, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }

                _closures.DisplayClasses[arrow] = displayClass;
                _closures.ArrowMethods[arrow] = invokeMethod;
            }

            // User arrow / function-expression body: when invoked as a value, omitted trailing
            // args must pad with the `undefined` sentinel (JS semantics), not CLR null. (#640)
            MarkPadsUndefined(_closures.ArrowMethods[arrow]);
        }
    }

    /// <summary>
    /// True when <paramref name="arrow"/>'s capture of <paramref name="name"/> resolves to a
    /// top-level BLOCK-scoped binding that was NOT lifted onto the entry-point display class
    /// (#1201's declared-exactly-once rule declined it, so the name is also declared elsewhere
    /// — typically the module-level binding the block one shadows). Such a capture must not
    /// ride the $entryPointDC chain: the same-named DC field is a DIFFERENT binding (#1222).
    /// The analyzer records the flag only for closures created directly in top-level code, so
    /// nested closures keep the historical chain routing. The lifted-set lookup uses the
    /// arrow's own module key — registration and collection both key scripts under
    /// <see cref="ClosureCompilationState.SingleFileKey"/> via a null path.
    /// </summary>
    private bool IsShadowedTopLevelBlockCapture(Expr.ArrowFunction arrow, string name)
    {
        if (!_closures.Analyzer.IsDirectTopLevelBlockScopedCapture(arrow, name))
            return false;
        _arrowToModule.TryGetValue(arrow, out var modulePath);
        string key = modulePath ?? ClosureCompilationState.SingleFileKey;
        return !(_closures.ModuleLiftedBlockScopedVars.TryGetValue(key, out var lifted) &&
                 lifted.Contains(name));
    }

    private void CollectArrowsFromStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                CollectArrowsFromExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                {
                    // If initializing with a class expression, track variable name → class expr mapping
                    if (v.Initializer is Expr.ClassExpr classExpr)
                    {
                        _classExprs.VarToClassExpr[v.Name.Lexeme] = classExpr;
                    }
                    CollectArrowsFromExpr(v.Initializer);
                }
                break;
            case Stmt.Const c:
                // If initializing with a class expression, track variable name → class expr mapping
                if (c.Initializer is Expr.ClassExpr classExprConst)
                {
                    _classExprs.VarToClassExpr[c.Name.Lexeme] = classExprConst;
                }
                CollectArrowsFromExpr(c.Initializer);
                break;
            case Stmt.Function f:
                // Skip overload signatures (no body)
                if (f.Body != null)
                {
                    // Track inner function declarations (nested inside another function)
                    if (_functionNestingDepth > 0)
                    {
                        CollectInnerFunction(f);
                    }

                    var previousEnclosing = _currentEnclosingFunctionName;
                    var previousEnclosingStmt = _currentEnclosingFunctionStmt;
                    var previousCallable = _currentEnclosingCallable;
                    // A nested function declaration is a lambda-lift boundary: an arrow inside it captures
                    // from the function, not the enclosing async arrow. Clear the async-arrow cursor so a
                    // deeper arrow is not mis-attributed to the async arrow's function DC (#838 follow-up).
                    var previousEnclosingAsyncArrowFn = _currentEnclosingAsyncArrow;
                    _currentEnclosingAsyncArrow = null;
                    if (_functionNestingDepth == 0)
                    {
                        // Top-level function: use its qualified name as enclosing context
                        _currentEnclosingFunctionName = GetDefinitionContext().GetQualifiedFunctionName(f.Name.Lexeme);
                        _currentEnclosingFunctionStmt = f;
                    }
                    _currentEnclosingCallable = f;

                    _functionNestingDepth++;
                    foreach (var s in f.Body)
                        CollectArrowsFromStmt(s);
                    _functionNestingDepth--;

                    _currentEnclosingFunctionName = previousEnclosing;
                    _currentEnclosingFunctionStmt = previousEnclosingStmt;
                    _currentEnclosingCallable = previousCallable;
                    _currentEnclosingAsyncArrow = previousEnclosingAsyncArrowFn;
                }
                foreach (var p in f.Parameters)
                    if (p.DefaultValue != null)
                        CollectArrowsFromExpr(p.DefaultValue);
                break;
            case Stmt.Class c:
                var previousClassName = _currentCollectClassName;
                _currentCollectClassName = c.Name.Lexeme;
                foreach (var method in c.Methods)
                {
                    // Skip overload signatures (no body)
                    if (method.Body != null)
                        CollectArrowsFromStmt(method);
                }
                // Field initializers are evaluated in the class's lexical context, but are
                // not part of a method body. Walk them explicitly so function/arrow values
                // get MethodBuilders instead of EmitArrowFunction's null fallback.
                foreach (var field in c.Fields)
                {
                    if (field.ComputedKey != null)
                        CollectArrowsFromExpr(field.ComputedKey);
                    if (field.Initializer != null)
                        CollectArrowsFromExpr(field.Initializer);
                }
                if (c.Accessors != null)
                {
                    foreach (var accessor in c.Accessors)
                        foreach (var statement in accessor.Body)
                            CollectArrowsFromStmt(statement);
                }
                _currentCollectClassName = previousClassName;
                break;
            case Stmt.If i:
                CollectArrowsFromExpr(i.Condition);
                CollectArrowsFromStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    CollectArrowsFromStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                CollectArrowsFromExpr(w.Condition);
                CollectArrowsFromStmt(w.Body);
                break;
            case Stmt.For f:
                if (f.Initializer != null)
                    CollectArrowsFromStmt(f.Initializer);
                if (f.Condition != null)
                    CollectArrowsFromExpr(f.Condition);
                if (f.Increment != null)
                    CollectArrowsFromExpr(f.Increment);
                CollectArrowsFromStmt(f.Body);
                break;
            case Stmt.ForOf forOf:
                CollectArrowsFromExpr(forOf.Iterable);
                CollectArrowsFromStmt(forOf.Body);
                break;
            case Stmt.ForIn forIn:
                CollectArrowsFromExpr(forIn.Object);
                CollectArrowsFromStmt(forIn.Body);
                break;
            case Stmt.DoWhile dw:
                CollectArrowsFromStmt(dw.Body);
                CollectArrowsFromExpr(dw.Condition);
                break;
            case Stmt.LabeledStatement ls:
                CollectArrowsFromStmt(ls.Statement);
                break;
            case Stmt.StaticBlock sb:
                foreach (var s in sb.Body)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowsFromExpr(r.Value);
                break;
            case Stmt.Switch s:
                CollectArrowsFromExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    CollectArrowsFromExpr(c.Value);
                    foreach (var cs in c.Body)
                        CollectArrowsFromStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowsFromStmt(ds);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowsFromStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        CollectArrowsFromStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowsFromStmt(fs);
                break;
            case Stmt.Throw th:
                CollectArrowsFromExpr(th.Value);
                break;
            case Stmt.Using u:
                foreach (var binding in u.Bindings)
                    CollectArrowsFromExpr(binding.Initializer);
                break;
            case Stmt.Namespace ns:
                // Recurse into namespace members so arrows and nested function declarations
                // inside namespace member functions get collected. Without this the collection
                // never enters namespace bodies, so a `function* `/arrow member's method is never
                // registered (arrow `export const f = () => …` left non-callable, #659) and a
                // nested `function inner(){}` inside a member function is never lifted, throwing
                // "Undefined variable 'inner'" (#660). Setting _currentNamespacePath keeps the
                // enclosing-function-name qualification symmetric with the define/emit phases
                // (DefineNamespaceFields / EmitNamespaceMemberBodies). Members may be wrapped in
                // Stmt.Export, which the Export arm unwraps. Depth is unchanged: a direct member
                // function is a top-level (depth-0) function, exactly like a module-scoped one.
                var savedNsPath = _currentNamespacePath;
                _currentNamespacePath = string.IsNullOrEmpty(_currentNamespacePath)
                    ? ns.Name.Lexeme
                    : $"{_currentNamespacePath}.{ns.Name.Lexeme}";
                foreach (var member in ns.Members)
                    CollectArrowsFromStmt(member);
                _currentNamespacePath = savedNsPath;
                break;
            case Stmt.Export exp:
                // Recurse into the wrapped declaration so arrows inside
                // `export const X = () => …` or `export function …` get
                // collected. Without this the arrow's method never gets
                // registered in _collectedArrows and EmitArrowFunction
                // silently emits `ldnull`, leaving the export field null.
                if (exp.Declaration != null)
                    CollectArrowsFromStmt(exp.Declaration);
                if (exp.DefaultExpr != null)
                    CollectArrowsFromExpr(exp.DefaultExpr);
                if (exp.ExportAssignment != null)
                    CollectArrowsFromExpr(exp.ExportAssignment);
                break;
        }
    }

    private void CollectArrowsFromExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af:
                var captures = _closures.Analyzer.GetCaptures(af);
                _collectedArrows.Add((af, captures));
                if (_currentCollectClassName != null)
                    _strictArrows.Add(af);
                // Only record a module mapping when collection is under a real
                // module context. Single-file compile runs without setting
                // _modules.CurrentPath, and entering a null key would cause
                // body emission to overwrite the path with null, dropping any
                // outer context the caller set up.
                if (_modules.CurrentPath != null)
                {
                    _arrowToModule[af] = _modules.CurrentPath;
                }

                // Track enclosing class name for arrows that need class-scoped dispatch:
                // private method / private field access, `super` calls, etc. Recorded for
                // every arrow (sync + async) so EmitArrowBody can propagate it to the
                // compilation context — without this, a private method call inside an
                // arrow throws "class context not available" at runtime. Stored as the
                // QUALIFIED class name because the ClassRegistry keys private methods
                // by qualified name (simple names like "AST" miss the `$M_index_AST` key).
                if (_currentCollectClassName != null)
                {
                    _async.ArrowEnclosingClassNames[af] = GetDefinitionContext().GetQualifiedClassName(_currentCollectClassName);
                    if (_classes.Declarations.Any(c =>
                        c.Name.Lexeme == _currentCollectClassName
                        && c.SuperclassExpr is Expr.ClassExpr
                        {
                            Methods.Count: 0,
                            Fields.Count: 0,
                            Accessors: null or { Count: 0 },
                            AutoAccessors: null or { Count: 0 },
                            StaticInitializers: null or { Count: 0 }
                        }))
                        _arrowsInAnonymousEmptyDerivedClass.Add(af);
                }

                // Track the immediately enclosing top-level function so we can
                // match `$functionDC` requests to the right display class
                // during PropagateFunctionDCRequirements.
                if (_currentEnclosingFunctionStmt != null)
                {
                    _arrowEnclosingFunction[af] = _currentEnclosingFunctionStmt;
                }

                // Follow-up to #838: record the nearest enclosing async arrow so a nested sync arrow can
                // resolve a write-capture to that async arrow's function DC. Recorded for descendants of
                // an async arrow (the async arrow itself maps to its own outer async arrow, if any).
                if (_currentEnclosingAsyncArrow != null)
                {
                    _arrowEnclosingAsyncArrow[af] = _currentEnclosingAsyncArrow;
                }

                // Track parent arrow for function DC propagation
                _arrowParent[af] = _currentParentArrow;
                _arrowEnclosingCallable[af] = _currentEnclosingCallable;
                var previousParent = _currentParentArrow;
                var previousCallable = _currentEnclosingCallable;
                var previousEnclosingAsyncArrow = _currentEnclosingAsyncArrow;
                _currentParentArrow = af;
                _currentEnclosingCallable = af;
                if (af.IsAsync)
                    _currentEnclosingAsyncArrow = af;

                // Entering an arrow body is the same as entering a function body for the
                // purpose of "inner function declaration" classification: `function X() {}`
                // inside an arrow is a nested declaration that should be hoisted into the
                // arrow's scope. Without incrementing the depth, CollectArrowsFromStmt's
                // `Stmt.Function f` case skips `CollectInnerFunction` (it only fires when
                // depth > 0), and the declaration never gets a MethodBuilder — references
                // to it inside the arrow then fall through to "Undefined variable" at
                // runtime. Real-world case: lodash's IIFE `(function() { ... })()` wraps
                // ~5000 lines of nested `function basePropertyOf(...)` declarations.
                _functionNestingDepth++;
                if (af.ExpressionBody != null)
                    CollectArrowsFromExpr(af.ExpressionBody);
                if (af.BlockBody != null)
                    foreach (var s in af.BlockBody)
                        CollectArrowsFromStmt(s);
                _functionNestingDepth--;

                _currentParentArrow = previousParent;
                _currentEnclosingCallable = previousCallable;
                _currentEnclosingAsyncArrow = previousEnclosingAsyncArrow;
                break;
            case Expr.Binary b:
                CollectArrowsFromExpr(b.Left);
                CollectArrowsFromExpr(b.Right);
                break;
            case Expr.Logical l:
                CollectArrowsFromExpr(l.Left);
                CollectArrowsFromExpr(l.Right);
                break;
            case Expr.Unary u:
                CollectArrowsFromExpr(u.Right);
                break;
            case Expr.Delete d:
                CollectArrowsFromExpr(d.Operand);
                break;
            case Expr.Grouping g:
                CollectArrowsFromExpr(g.Expression);
                break;
            case Expr.Call c:
                CollectArrowsFromExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.Get g:
                CollectArrowsFromExpr(g.Object);
                break;
            case Expr.Set s:
                CollectArrowsFromExpr(s.Object);
                CollectArrowsFromExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                CollectArrowsFromExpr(gi.Object);
                CollectArrowsFromExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                CollectArrowsFromExpr(si.Object);
                CollectArrowsFromExpr(si.Index);
                CollectArrowsFromExpr(si.Value);
                break;
            case Expr.Assign a:
                CollectArrowsFromExpr(a.Value);
                break;
            case Expr.New n:
                CollectArrowsFromExpr(n.Callee);
                foreach (var arg in n.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowsFromExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowsFromExpr(prop.Value);
                break;
            case Expr.Ternary t:
                CollectArrowsFromExpr(t.Condition);
                CollectArrowsFromExpr(t.ThenBranch);
                CollectArrowsFromExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowsFromExpr(nc.Left);
                CollectArrowsFromExpr(nc.Right);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.TaggedTemplateLiteral ttl:
                CollectArrowsFromExpr(ttl.Tag);
                foreach (var e in ttl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.CompoundAssign ca:
                CollectArrowsFromExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                CollectArrowsFromExpr(cs.Object);
                CollectArrowsFromExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowsFromExpr(csi.Object);
                CollectArrowsFromExpr(csi.Index);
                CollectArrowsFromExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowsFromExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowsFromExpr(poi.Operand);
                break;
            case Expr.Await aw:
                CollectArrowsFromExpr(aw.Expression);
                break;
            case Expr.Yield y:
                // Arrows inside a yielded expression (`yield arr.map(x => …)`,
                // `yield* gen(() => …)`) must be collected too. Without this case the
                // collection walk stops at the yield, the arrow's method is never
                // registered in ArrowMethods, and EmitArrowFunction falls back to
                // `ldnull` — surfacing at runtime as "callback is not callable" inside
                // a sync generator body (#435/#669).
                if (y.Value != null)
                    CollectArrowsFromExpr(y.Value);
                break;
            case Expr.DynamicImport di:
                CollectArrowsFromExpr(di.PathExpression);
                break;
            case Expr.TypeAssertion ta:
                CollectArrowsFromExpr(ta.Expression);
                break;
            case Expr.Satisfies sat:
                CollectArrowsFromExpr(sat.Expression);
                break;
            case Expr.NonNullAssertion nna:
                CollectArrowsFromExpr(nna.Expression);
                break;
            case Expr.Spread sp:
                CollectArrowsFromExpr(sp.Expression);
                break;
            case Expr.Comma cm:
                CollectArrowsFromExpr(cm.Left);
                CollectArrowsFromExpr(cm.Right);
                break;
            case Expr.LogicalAssign la:
                CollectArrowsFromExpr(la.Value);
                break;
            case Expr.LogicalSet lsObj:
                CollectArrowsFromExpr(lsObj.Object);
                CollectArrowsFromExpr(lsObj.Value);
                break;
            case Expr.LogicalSetIndex lsi:
                CollectArrowsFromExpr(lsi.Object);
                CollectArrowsFromExpr(lsi.Index);
                CollectArrowsFromExpr(lsi.Value);
                break;
            case Expr.CallPrivate cp:
                CollectArrowsFromExpr(cp.Object);
                foreach (var arg in cp.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.GetPrivate gp:
                CollectArrowsFromExpr(gp.Object);
                break;
            case Expr.SetPrivate sp2:
                CollectArrowsFromExpr(sp2.Object);
                CollectArrowsFromExpr(sp2.Value);
                break;
            case Expr.ClassExpr ce:
                // Collect the class expression for later definition
                CollectClassExpression(ce);
                var previousClassNameCE = _currentCollectClassName;
                if (previousClassNameCE != null)
                {
                    _classExprs.EnclosingClass[ce] = previousClassNameCE.StartsWith("$ClassExpr_", StringComparison.Ordinal)
                        ? previousClassNameCE
                        : GetDefinitionContext().GetQualifiedClassName(previousClassNameCE);
                }
                _currentCollectClassName = _classExprs.Names[ce];
                // Method/accessor bodies are separate callables — an inner function
                // declaration inside them is NOT hoisted into the enclosing arrow's
                // body, so clear the immediate-callable marker while walking them.
                var prevCallableCE = _currentEnclosingCallable;
                _currentEnclosingCallable = null;
                // Also collect arrows inside class expression methods. Walk each method via
                // CollectArrowsFromStmt(method) — not its body statements directly — so the Stmt.Function
                // case records the method as `_currentEnclosingFunctionStmt`, the arrow→method mapping
                // PropagateFunctionDCRequirements needs to route a generator method's write-capture through
                // its function display class (#789). Mirrors the Stmt.Class declaration path above.
                foreach (var method in ce.Methods)
                    if (method.Body != null)
                        CollectArrowsFromStmt(method);
                // Collect arrows in field initializers
                foreach (var field in ce.Fields)
                    if (field.Initializer != null)
                        CollectArrowsFromExpr(field.Initializer);
                // Collect arrows in accessor bodies
                if (ce.Accessors != null)
                    foreach (var accessor in ce.Accessors)
                        foreach (var s in accessor.Body)
                            CollectArrowsFromStmt(s);
                _currentCollectClassName = previousClassNameCE;
                _currentEnclosingCallable = prevCallableCE;
                break;
        }
    }

    /// <summary>
    /// #789: registers function display classes for class-EXPRESSION generator methods that write a
    /// captured method local, mirroring the Phase-4 registration class declarations get via
    /// <see cref="RegisterGeneratorMethodFunctionDisplayClasses(Stmt.Class, string)"/>. Uses the generated
    /// class-expr name (assigned by <see cref="CollectClassExpression"/>) as the qualified name; the key is
    /// only required to be internally consistent because the DC-key dictionaries are AST-identity-keyed.
    /// Must run before <see cref="PropagateFunctionDCRequirements"/>.
    /// </summary>
    private void RegisterClassExpressionGeneratorMethodFunctionDisplayClasses()
    {
        foreach (var classExpr in _classExprs.ToDefine)
            if (_classExprs.Names.TryGetValue(classExpr, out var name))
            {
                RegisterSyncMethodFunctionDisplayClasses(classExpr.Methods, name);
                RegisterGeneratorMethodFunctionDisplayClasses(classExpr.Methods, name);
                RegisterAsyncMethodFunctionDisplayClasses(classExpr.Methods, name);
            }
    }

    private int _asyncArrowDCCounter;

    /// <summary>
    /// Follow-up to #838: registers a function display class for each async arrow whose OWN local is
    /// captured-and-written by a nested SYNC arrow, so that write shares verifiable reference storage on
    /// the async arrow's state machine (the async-arrow analogue of free async functions / async methods).
    /// Runs in Phase 5 before <see cref="PropagateFunctionDCRequirements"/>, which resolves the nested sync
    /// arrow's DC source to this DC via <see cref="ClosureCompilationState.AsyncArrowDCKeys"/>. No-op for
    /// async arrows with no such write-capture (the common case).
    /// </summary>
    private void RegisterAsyncArrowFunctionDisplayClasses()
    {
        foreach (var (arrow, _) in _collectedArrows)
        {
            if (!arrow.IsAsync) continue;

            var members = ComputeAsyncArrowSyncWriteCaptures(arrow);
            if (members.Count == 0) continue;

            string key = $"$asyncArrow${_asyncArrowDCCounter++}";

            // #838 (additive): expands `members` with renamed storage keys for write-captured shadows AND
            // records ArrowFunctionDCFieldRenames[nestedSyncArrow] so the sync arrow's body resolves the
            // renamed field generically (main's CurrentArrowFunctionDCFieldRenames path).
            ApplyWriteCaptureRenames(members, GeneratorBlockScopeRenamer.Compute(arrow));

            RegisterFunctionDisplayClass(key, members);
            if (_closures.FunctionDisplayClasses.ContainsKey(key))
                _closures.AsyncArrowDCKeys[arrow] = key;
        }
    }

    /// <summary>
    /// The async arrow's OWN locals (captured by an inner closure) that a nested SYNC arrow WRITES — the
    /// member set of the async arrow's function display class. Mirrors the generator path: per nested sync
    /// arrow, the names it assigns AND captures; intersected with the async arrow's captured locals so only
    /// its own bindings are lifted (transitive captures stay on their existing path). Parameters and
    /// per-iteration loop bindings (#649) are excluded.
    /// </summary>
    private HashSet<string> ComputeAsyncArrowSyncWriteCaptures(Expr.ArrowFunction asyncArrow)
    {
        var capturedLocals = _closures.Analyzer.GetCapturedLocals(asyncArrow);
        if (capturedLocals.Count == 0) return [];

        var writes = new HashSet<string>();
        foreach (var (arrow, _) in _collectedArrows)
        {
            if (arrow.IsAsync) continue;   // only SYNC arrows share through the async arrow's function DC
            if (!_arrowEnclosingAsyncArrow.TryGetValue(arrow, out var encl) || !ReferenceEquals(encl, asyncArrow))
                continue;
            var w = CapturedWriteAnalysis.CollectImmediateWrites(arrow);
            w.IntersectWith(_closures.Analyzer.GetCaptures(arrow));
            writes.UnionWith(w);
        }
        if (writes.Count == 0) return [];

        var result = new HashSet<string>(capturedLocals);
        result.IntersectWith(writes);
        // Parameters stay on their hoisted fields (the stub seeds only the DC's locals); a
        // captured-written async-arrow PARAMETER stays on the existing path (rare; out of scope here).
        foreach (var p in asyncArrow.Parameters)
            result.Remove(p.Name.Lexeme);
        var perIteration = _closures.Analyzer.GetPerIterationLoopBindings(asyncArrow);
        if (perIteration.Count > 0) result.ExceptWith(perIteration);
        return result;
    }

    /// <summary>
    /// Collects a class expression for later type definition.
    /// </summary>
    private void CollectClassExpression(Expr.ClassExpr classExpr)
    {
        if (_classExprs.Names.ContainsKey(classExpr))
            return; // Already collected

        // Generate unique name
        string className = classExpr.Name?.Lexeme ?? $"$ClassExpr_{++_classExprs.Counter}";
        _classExprs.Names[classExpr] = className;
        _classExprs.ToDefine.Add(classExpr);
    }

    /// <summary>
    /// Propagates function display class requirements through arrow nesting.
    /// If an inner arrow needs $functionDC, its parent arrows also need it to pass it through.
    /// </summary>
    private void PropagateFunctionDCRequirements()
    {
        // First, identify arrows that directly need $functionDC.
        //
        // An arrow needs $functionDC when it captures a variable that lives on
        // its enclosing top-level function's display class. We resolve the
        // enclosing function from `_arrowEnclosingFunction` (populated at
        // collection time) rather than iterating every registered display
        // class by name — the old "match on field name" heuristic aliased
        // same-named parameters across sibling module-level functions, so a
        // closure would read from another function's uninitialized display
        // class field and blow up with NullReferenceException at runtime.
        // Resolve the enclosing AST node back to the qualified function name
        // by scanning FunctionAstNodes (populated during DefineFunction per
        // module). This pair is one-to-one and the map is small.
        string? ResolveQualifiedName(Stmt.Function stmt)
        {
            foreach (var kv in _closures.FunctionAstNodes)
            {
                if (ReferenceEquals(kv.Value, stmt)) return kv.Key;
            }
            return null;
        }

        foreach (var (arrow, captures) in _collectedArrows)
        {
            // (1) Enclosing Stmt.Function's display class (free functions / methods). Membership is by
            // plain ContainsKey: #838 renames are ADDITIVE (ApplyWriteCaptureRenames keeps the source
            // name AND adds the renamed storage key), so a write-captured shadow's source name is present.
            if (_arrowEnclosingFunction.TryGetValue(arrow, out var enclosingStmt) &&
                ResolveQualifiedName(enclosingStmt) is string enclosingFuncName &&
                _closures.FunctionDisplayClassFields.TryGetValue(enclosingFuncName, out var funcDCFields) &&
                captures.Any(funcDCFields.ContainsKey))
            {
                _arrowsNeedingFunctionDC.Add(arrow);
                _closures.ArrowFunctionDCSource[arrow] = enclosingFuncName;
                continue;
            }

            // (2) Follow-up to #838: enclosing ASYNC ARROW's display class — a sync arrow that
            // captures-and-writes one of the async arrow's own locals routes through that DC.
            if (_arrowEnclosingAsyncArrow.TryGetValue(arrow, out var enclAsyncArrow) &&
                _closures.AsyncArrowDCKeys.TryGetValue(enclAsyncArrow, out var asyncKey) &&
                _closures.FunctionDisplayClassFields.TryGetValue(asyncKey, out var asyncDCFields) &&
                captures.Any(asyncDCFields.ContainsKey))
            {
                _arrowsNeedingFunctionDC.Add(arrow);
                _closures.ArrowFunctionDCSource[arrow] = asyncKey;
            }
        }

        // Propagate requirements up the parent chain
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var arrow in _arrowsNeedingFunctionDC.ToList())
            {
                if (_arrowParent.TryGetValue(arrow, out var parent) && parent != null)
                {
                    // Follow-up to #838: stop at the async arrow that OWNS the DC the child sources from —
                    // it reaches its own DC via `this.<>__functionDC`, not a threaded $functionDC field, so
                    // it must not be marked as needing one passed in.
                    if (_closures.AsyncArrowDCKeys.TryGetValue(parent, out var parentOwnKey) &&
                        _closures.ArrowFunctionDCSource.TryGetValue(arrow, out var childSrc) &&
                        parentOwnKey == childSrc)
                        continue;

                    if (!_arrowsNeedingFunctionDC.Contains(parent))
                    {
                        _arrowsNeedingFunctionDC.Add(parent);
                        // Inherit the source function from the child
                        if (_closures.ArrowFunctionDCSource.TryGetValue(arrow, out var source))
                        {
                            _closures.ArrowFunctionDCSource[parent] = source;
                        }
                        changed = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a display class for a callable's captured local variables.
    /// This is needed when local variables are captured by nested closures.
    /// The callable may be an arrow function or an inner function declaration
    /// (#313); the name suffix only aids debugging.
    /// </summary>
    private void DefineScopeDisplayClass(object callable, string nameSuffix)
    {
        if (_closures.ArrowScopeDisplayClasses.ContainsKey(callable))
            return;

        var capturedLocals = _closures.Analyzer.GetCapturedLocals(callable);
        if (capturedLocals.Count == 0)
            return;

        // Per-iteration `for (let/const …)` loop bindings must NOT share this scope
        // display class (a single instance per arrow/inner-function call): each
        // iteration gets its own binding (ECMA-262 13.7.4), so a closure created in
        // one iteration must capture a value distinct from other iterations. Keeping
        // them out of the scope DC leaves them as locals that nested closures snapshot
        // per iteration — mirroring the function-DC exclusion (#649).
        var perIterationBindings = _closures.Analyzer.GetPerIterationLoopBindings(callable);
        if (perIterationBindings.Count > 0)
        {
            capturedLocals = new HashSet<string>(capturedLocals);
            capturedLocals.ExceptWith(perIterationBindings);
            if (capturedLocals.Count == 0)
                return;
        }

        var displayClass = EmitTypeDefinitions.DefineType(_moduleBuilder,
            $"<>c__{nameSuffix}{_closures.DisplayClassCounter++}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        MarkCompilerGenerated(displayClass);

        var fieldMap = new Dictionary<string, FieldBuilder>();
        foreach (var varName in capturedLocals)
        {
            var field = displayClass.DefineField(varName, _types.Object, FieldAttributes.Public);
            fieldMap[varName] = field;
        }

        var ctor = displayClass.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ret);

        _closures.ArrowScopeDisplayClasses[callable] = displayClass;
        _closures.ArrowScopeDisplayClassCtors[callable] = ctor;
        _closures.ArrowScopeDisplayClassFields[callable] = fieldMap;
    }

    /// <summary>
    /// Propagates arrow scope display class requirements through arrow nesting.
    /// If an inner arrow needs $arrowDC, its parent arrows also need it to pass it through.
    /// </summary>
    private void PropagateArrowDCRequirements()
    {
        var collectedInnerFuncs = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        foreach (var (f, _, _) in _collectedInnerFunctions)
            collectedInnerFuncs.Add(f);

        // First, identify arrows that directly capture arrow-scope locals.
        // The source arrow is resolved from the analyzer's capture-source record
        // (the scope that actually DEFINES each captured variable), not by name-
        // matching across every scope DC — name matching aliased same-named
        // variables across unrelated scopes and picked sources in arbitrary
        // dictionary order. When captures span multiple ancestor arrow scopes,
        // the single $arrowDC slot is bound to the candidate covering the most
        // captures whose chain can be threaded; the rest fall back to copy-field
        // population through the chained DC (see EmitDisplayInstanceFieldPopulation).
        // Collection order is outer-to-inner, so an enclosing arrow's own needs
        // are settled before nested closures thread pass-throughs onto it.
        foreach (var (arrow, captures) in _collectedArrows)
        {
            Dictionary<object, int>? candidates = null;
            foreach (var c in captures)
            {
                if (_closures.Analyzer.GetCaptureSource(arrow, c) is not { } sa)
                    continue;
                if (!_closures.ArrowScopeDisplayClassFields.TryGetValue(sa, out var fields) ||
                    !fields.ContainsKey(c))
                    continue;
                candidates ??= new(ReferenceEqualityComparer.Instance);
                candidates[sa] = candidates.TryGetValue(sa, out var n) ? n + 1 : 1;
            }
            if (candidates == null)
                continue;

            // Nearest threadable candidate claims the primary $arrowDC slot
            // (unless pass-through threading already did); the rest become
            // EXTRA reference fields. The $arrowDC is populated at the arrow's
            // creation site (the enclosing callable's body), so chains are
            // walked from there.
            _arrowEnclosingCallable.TryGetValue(arrow, out var start);
            foreach (var sa in OrderByAncestorProximity(start, candidates))
            {
                if (!ReferenceEquals(start, sa) && !TryThreadArrowScopeSource(start, sa, collectedInnerFuncs))
                    continue; // unreachable chain — this source keeps snapshot semantics

                _arrowsNeedingArrowDC.Add(arrow);
                if (!_closures.ArrowScopeDCSource.TryGetValue(arrow, out var existingSrc))
                    _closures.ArrowScopeDCSource[arrow] = sa;
                else if (!ReferenceEquals(existingSrc, sa))
                    AddExtraScopeSource(_arrowExtraScopeSources, arrow, sa);
            }
        }

        // Propagate requirements up the parent chain
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var arrow in _arrowsNeedingArrowDC.ToList())
            {
                if (_arrowParent.TryGetValue(arrow, out var parent) && parent != null)
                {
                    // Don't propagate past the arrow that owns the DC
                    if (_closures.ArrowScopeDisplayClasses.ContainsKey(parent))
                        continue;

                    if (!_arrowsNeedingArrowDC.Contains(parent))
                    {
                        _arrowsNeedingArrowDC.Add(parent);
                        if (_closures.ArrowScopeDCSource.TryGetValue(arrow, out var source))
                        {
                            _closures.ArrowScopeDCSource[parent] = source;
                        }
                        changed = true;
                    }
                }
            }
        }
    }

    private void EmitArrowFunctionBodies()
    {
        var savedPath = _modules.CurrentPath;
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // Skip async arrows - they're handled separately via AsyncArrowMoveNextEmitter
            // in ILCompiler.Async.cs
            if (arrow.IsAsync)
            {
                continue;
            }

            if (_arrowToModule.TryGetValue(arrow, out var arrowModule))
            {
                _modules.CurrentPath = NormalizeToEmissionPath(arrowModule);
            }

            var methodBuilder = _closures.ArrowMethods[arrow];

            // Check if this arrow needs function DC or arrow DC (either directly or to pass to inner arrows)
            // This must match the logic in CollectAndDefineArrowFunctions
            bool needsFunctionDCForArrow = _arrowsNeedingFunctionDC.Contains(arrow);
            bool needsArrowDCForArrow = _arrowsNeedingArrowDC.Contains(arrow);

            if (captures.Count == 0 && !needsFunctionDCForArrow && !needsArrowDCForArrow)
            {
                // Non-capturing and doesn't need function DC: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing or needs function DC: emit body into display class method
                var displayClass = _closures.DisplayClasses[arrow];
                EmitArrowBody(arrow, methodBuilder, displayClass);
            }
        }
        _modules.CurrentPath = savedPath;
    }

    /// <summary>
    /// Detects a function-body "use strict" directive prologue in either parsed
    /// form: a <see cref="Stmt.Directive"/> (declarations parse a prologue) or a
    /// leading string-literal <see cref="Stmt.Expression"/> (function expressions /
    /// arrows parse their body with <c>Block()</c> and no prologue, so "use strict"
    /// lands as an ordinary expression statement). Mirrors a directive prologue:
    /// scan consecutive string-literal statements, stop at the first non-string.
    /// </summary>
    private static bool BodyDeclaresUseStrict(List<Stmt>? body)
    {
        if (body is null) return false;
        foreach (var stmt in body)
        {
            string? directiveValue = stmt switch
            {
                Stmt.Directive d => d.Value,
                Stmt.Expression { Expr: Expr.Literal { Value: string s } } => s,
                _ => null,
            };
            if (directiveValue is null) return false; // prologue ended
            if (directiveValue == "use strict") return true;
            // otherwise another directive (e.g. "use asm") — keep scanning
        }
        return false;
    }

    private void EmitArrowBody(Expr.ArrowFunction arrow, MethodBuilder method, TypeBuilder? displayClass)
    {
        var il = method.GetILGenerator();

        // Get the resolved return type for this arrow (for typed return optimization)
        _closures.ArrowReturnTypes.TryGetValue(arrow, out var arrowReturnType);
        arrowReturnType ??= _types.Object;

        var ctx = CreateModuleMemberContext(il, method);
        ctx.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        // A function expression / arrow with its own "use strict" prologue is
        // strict even when the enclosing code is sloppy. Detect it here (not
        // just inherit the parent) so `this` resolution honors it — otherwise a
        // strict `function(){ "use strict"; … }` callback is misclassified
        // sloppy and LoadThis's undefined→globalThis coercion wrongly fires
        // (Test262 String/prototype/replace/S15.5.4.11_A12: a strict replace
        // callback must see `this === undefined`). Function-expression/arrow
        // bodies are parsed with Block() and no directive prologue, so their
        // "use strict" is an ordinary string expression statement rather than a
        // Stmt.Directive — BodyDeclaresUseStrict handles both forms (plain
        // CheckForUseStrict only matches Directive).
        ctx.IsStrictMode = _strictArrows.Contains(arrow)
            || _isStrictMode
            || BodyDeclaresUseStrict(arrow.BlockBody);
        // Propagate the enclosing class name so private-member dispatch (#field / #method
        // access, `super` lookups) works inside arrow bodies nested in class methods.
        // Without this, `this.#parseGlob()` inside an arrow throws "class context not
        // available" at runtime.
        ctx.CurrentClassName = _async.ArrowEnclosingClassNames.TryGetValue(arrow, out var enclosingClassName)
            ? enclosingClassName
            : null;
        ctx.CurrentSuperclassIsAnonymousEmptyClass = _arrowsInAnonymousEmptyDerivedClass.Contains(arrow);
        // Entry-point display class for accessing captured top-level variables
        ApplyCapturedTopLevelVariableAccess(ctx);
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        // Function-level display class for nested arrow functions
        ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        // Arrow scope display class for nested arrow functions
        ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
        ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
        // Inner function metadata — required for any `function X() {}` declared INSIDE this
        // arrow body to be reachable from sibling statements. Without this, IIFE-style
        // wrappers that declare nested functions (lodash's `(function() { function basePropertyOf() {...} }())`)
        // crash at runtime with "Undefined variable" when earlier statements reference them.
        ApplyInnerFunctionSupport(ctx);
        // CJS-context propagation — arrow bodies nested in a CJS module must see the
        // same `exports` binding and require() resolution as the enclosing module init.
        // Without this, bare `exports` inside an IIFE resolves to null and the lodash-
        // style feature detection `typeof exports == 'object' && exports && ...` fails.
        ApplyCommonJsModuleAccess(ctx);
        // Typed return optimization - set return type so EmitReturn knows whether to box
        ctx.CurrentMethodReturnType = arrowReturnType;

        if (displayClass != null)
        {
            // Instance method on display class - this is arg 0
            ctx.IsInstanceMethod = true;

            // Use the pre-stored field mapping
            if (_closures.DisplayClassFields.TryGetValue(arrow, out var fieldMap))
            {
                ctx.CapturedFields = fieldMap;
            }
            else
            {
                ctx.CapturedFields = [];
            }

            // Per-iteration cell capture (#650): mark which captured fields hold a
            // StrongBox so the body dereferences Value on read/write.
            var arrowCellFields = _closures.Analyzer.GetClosureCellFields(arrow);
            if (arrowCellFields.Count > 0)
                ctx.CellCapturedFieldNames = arrowCellFields;

            // Set the $entryPointDC field if this arrow captures top-level variables
            if (_closures.ArrowEntryPointDCFields.TryGetValue(arrow, out var entryPointDCField))
            {
                ctx.CurrentArrowEntryPointDCField = entryPointDCField;
            }

            // Set the $functionDC field if this arrow captures function-level variables
            if (_closures.ArrowFunctionDCFields.TryGetValue(arrow, out var functionDCField))
            {
                ctx.CurrentArrowFunctionDCField = functionDCField;

                // Also set up the captured function locals info so LocalVariableResolver can use it
                if (_closures.ArrowFunctionDCSource.TryGetValue(arrow, out var sourceFuncName) &&
                    _closures.FunctionDisplayClassFields.TryGetValue(sourceFuncName, out var funcDCFields))
                {
                    ctx.FunctionDisplayClassFields = funcDCFields;
                    ctx.CapturedFunctionLocals = [.. funcDCFields.Keys];
                }

                // #838: redirect this arrow's write-captured block-scope shadows to their renamed DC
                // storage keys so they reach the shadow's field rather than the outer same-named binding.
                ctx.CurrentArrowFunctionDCFieldRenames =
                    _closures.ArrowFunctionDCFieldRenames.TryGetValue(arrow, out var dcFieldRenames)
                        ? dcFieldRenames : null;
            }

            // Set the $arrowDC field if this arrow captures arrow-scope variables
            // from a parent arrow. Populate BOTH the parent-slot pair (for
            // LocalVariableResolver case 2c — arrows that also own a DC) and
            // the legacy own-slot pair (for arrows without their own DC,
            // which read parent fields through the same direct-field map). The
            // own slots may be overwritten later if this arrow turns out to
            // have its own arrow-scope DC; in that case the parent slots
            // remain intact so same-named parent fields are still reachable.
            if (_closures.ArrowScopeDCFields.TryGetValue(arrow, out var arrowScopeDCFieldRef))
            {
                ctx.CurrentArrowScopeDCField = arrowScopeDCFieldRef;

                if (_closures.ArrowScopeDCSource.TryGetValue(arrow, out var sourceArrowRef) &&
                    _closures.ArrowScopeDisplayClassFields.TryGetValue(sourceArrowRef, out var arrowDCFields))
                {
                    ctx.ParentArrowScopeDisplayClassFields = arrowDCFields;
                    ctx.ParentArrowCapturedLocals = [.. arrowDCFields.Keys];
                    // Legacy fallback: arrows without their own arrow-scope DC
                    // also see these as their "own" fields (read through
                    // $arrowDC) — preserved for compatibility with code paths
                    // that haven't been updated to check the parent slots.
                    ctx.ArrowScopeDisplayClassFields = arrowDCFields;
                    ctx.CapturedArrowLocals = [.. arrowDCFields.Keys];
                }
            }

            // EXTRA ancestor scope references (multi-source captures): per-name
            // live bindings, plus the raw ref fields for chaining into closures
            // created in this body.
            if (_arrowScopeDCExtraFields.TryGetValue(arrow, out var ownExtraScopeRefs))
            {
                ctx.CurrentArrowScopeDCExtraFields = ownExtraScopeRefs;
                ctx.ExtraArrowScopeBindings = BuildExtraScopeBindings(
                    arrow, _closures.Analyzer.GetCaptures(arrow), ownExtraScopeRefs);
            }

            // Get resolved parameter types for this arrow (for typed parameter optimization)
            _closures.ArrowParameterTypes.TryGetValue(arrow, out var arrowParamTypes);

            // For object methods, __this is the first parameter after 'this' (display class)
            // Parameters start at index 1 (display class is arg 0)
            if (arrow.HasOwnThis)
            {
                // __this is at index 1, actual parameters start at index 2
                ctx.DefineParameter("__this", 1);
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    Type? paramType = arrowParamTypes != null && i < arrowParamTypes.Length ? arrowParamTypes[i] : null;
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 2, paramType);
                }
            }
            else
            {
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    Type? paramType = arrowParamTypes != null && i < arrowParamTypes.Length ? arrowParamTypes[i] : null;
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1, paramType);
                }
            }
        }
        else
        {
            // Get resolved parameter types for this arrow (for typed parameter optimization)
            _closures.ArrowParameterTypes.TryGetValue(arrow, out var arrowParamTypes);

            // Static method - parameters start at index 0
            if (arrow.HasOwnThis)
            {
                // __this is at index 0, actual parameters start at index 1
                ctx.DefineParameter("__this", 0);
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    Type? paramType = arrowParamTypes != null && i < arrowParamTypes.Length ? arrowParamTypes[i] : null;
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1, paramType);
                }
            }
            else
            {
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    Type? paramType = arrowParamTypes != null && i < arrowParamTypes.Length ? arrowParamTypes[i] : null;
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i, paramType);
                }
            }
        }

        // Create arrow scope display class instance if this arrow has captured locals
        if (_closures.ArrowScopeDisplayClasses.TryGetValue(arrow, out var arrowScopeDCType) &&
            _closures.ArrowScopeDisplayClassCtors.TryGetValue(arrow, out var arrowScopeDCCtor))
        {
            var arrowScopeDCLocal = il.DeclareLocal(arrowScopeDCType);
            il.Emit(OpCodes.Newobj, arrowScopeDCCtor);
            il.Emit(OpCodes.Stloc, arrowScopeDCLocal);
            ctx.ArrowScopeDisplayClassLocal = arrowScopeDCLocal;
            ctx.ArrowScopeDisplayClassFields = _closures.ArrowScopeDisplayClassFields[arrow];
            // Derive from the DC's actual field map (not the raw analyzer set) so
            // per-iteration loop bindings excluded by DefineScopeDisplayClass (#649)
            // are also excluded here — keeping CapturedArrowLocals consistent with
            // the fields the DC actually has.
            ctx.CapturedArrowLocals = [.. _closures.ArrowScopeDisplayClassFields[arrow].Keys];
        }

        var emitter = new ILEmitter(ctx);

        int environmentArgOffset = (displayClass != null ? 1 : 0) + (arrow.HasOwnThis ? 1 : 0);
        _closures.ArrowParameterTypes.TryGetValue(arrow, out var environmentResolvedParamTypes);
        var environmentParamTypes = new Type[arrow.Parameters.Count];
        for (int i = 0; i < environmentParamTypes.Length; i++)
            environmentParamTypes[i] = environmentResolvedParamTypes != null && i < environmentResolvedParamTypes.Length
                ? environmentResolvedParamTypes[i] ?? _types.Object
                : _types.Object;

        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            arrow.Parameters,
            arrow.BlockBody,
            environmentParamTypes,
            environmentArgOffset,
            createsArgumentsBinding: arrow.HasOwnThis,
            publishedArgsLeadingSkip: arrow.HasOwnThis ? 1 : 0);

        // Initialize captured parameters into the arrow scope display class.
        // This mirrors the equivalent code in ILCompiler.Functions.cs.
        // Without this, inner arrows that read parameters via $arrowDC get null.
        // Runs AFTER EmitDefaultParameters (which writes defaults via Starg) so
        // the DC fields see defaulted values, not the raw missing-arg padding.
        if (ctx.ArrowScopeDisplayClassLocal != null)
        {
            _closures.ArrowParameterTypes.TryGetValue(arrow, out var capturedParamTypes);
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                var paramName = arrow.Parameters[i].Name.Lexeme;
                if (ctx.CapturedArrowLocals!.Contains(paramName) &&
                    ctx.ArrowScopeDisplayClassFields!.TryGetValue(paramName, out var arrowDCField))
                {
                    il.Emit(OpCodes.Ldloc, ctx.ArrowScopeDisplayClassLocal);
                    // Arg index must match DefineParameter calls (lines 841-858 / 866-883):
                    //   instance (displayClass != null): params at i+1, or i+2 with HasOwnThis
                    //   static (displayClass == null):   params at i,   or i+1 with HasOwnThis
                    int argOffset = displayClass != null ? 1 : 0;
                    if (arrow.HasOwnThis) argOffset++;
                    il.Emit(OpCodes.Ldarg, i + argOffset);
                    // Typed (value-type) parameter slots must be boxed before
                    // landing in the object-typed DC field — Stfld of a raw
                    // double fails verification (StackUnexpected family, #284).
                    var capturedParamType = !arrow.Parameters[i].IsRest &&
                        capturedParamTypes != null && i < capturedParamTypes.Length ? capturedParamTypes[i] : null;
                    if (capturedParamType != null && capturedParamType.IsValueType)
                        il.Emit(OpCodes.Box, capturedParamType);
                    il.Emit(OpCodes.Stfld, arrowDCField);
                }
            }
        }

        if (arrow.ExpressionBody != null)
        {
            // Expression body: emit expression and return
            emitter.EmitExpression(arrow.ExpressionBody);

            // Handle return based on method return type
            if (arrowReturnType == _types.Object)
            {
                // Return type is object - box value types
                emitter.EmitBoxIfNeeded(arrow.ExpressionBody);
            }
            else if (_types.IsDouble(arrowReturnType))
            {
                // Return type is double - ensure unboxed double on stack
                emitter.EnsureDouble();
            }
            else if (_types.IsBoolean(arrowReturnType))
            {
                // Return type is bool - ensure unboxed bool on stack
                emitter.EnsureBoolean();
            }
            // For reference-typed returns, no conversion needed. Note that `string`
            // return types never reach here as a narrow `string` slot:
            // ParameterTypeResolver.ResolveReturnType maps them to object, because an
            // inferred-string arrow body can produce $Undefined at runtime
            // (e.g. `(n: any) => cond ? "x" : undefined`), which no string-typed slot
            // can carry. See #318.

            il.Emit(OpCodes.Ret);
        }
        else if (arrow.BlockBody != null)
        {
            // Hoist nested `function X() {}` declarations into the arrow's scope. Creates
            // TSFunction locals before any body statement runs, mirroring top-level function
            // compilation at ILCompiler.Functions.cs. Without this, IIFE bodies that reference
            // nested function declarations before those declarations appear lexically (the
            // classic hoisting idiom lodash relies on) get "Undefined variable" at runtime.
            EmitInnerFunctionHoisting(il, ctx, arrow.BlockBody);
            // Block body: emit statements
            foreach (var stmt in arrow.BlockBody)
            {
                emitter.EmitStatement(stmt);
            }
            // Finalize any deferred returns from exception blocks
            if (emitter.HasDeferredReturns)
            {
                emitter.FinalizeReturns();
            }
            else
            {
                // Default return based on return type
                EmitDefaultReturnValue(il, arrowReturnType);
                il.Emit(OpCodes.Ret);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

    }
}
