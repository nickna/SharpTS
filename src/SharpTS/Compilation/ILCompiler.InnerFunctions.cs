using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Inner function declaration collection, definition, and emission for the IL compiler.
/// Treats inner function declarations like arrow functions: non-capturing ones become
/// static methods, capturing ones get display classes. Supports hoisting (function
/// declarations are available before their textual position in the source).
/// </summary>
public partial class ILCompiler
{
    // Inner function tracking (keyed by Stmt.Function reference identity)
    private readonly List<(Stmt.Function Func, HashSet<string> Captures, string EnclosingFunctionName)> _collectedInnerFunctions = [];
    private readonly Dictionary<Stmt.Function, MethodBuilder> _innerFunctionMethods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, TypeBuilder> _innerFunctionDisplayClasses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, Dictionary<string, FieldBuilder>> _innerFunctionDCFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, ConstructorBuilder> _innerFunctionDCCtors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, FieldBuilder> _innerFunctionEntryPointDCFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, FieldBuilder> _innerFunctionFunctionDCFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, Type[]> _innerFunctionParamTypes = new(ReferenceEqualityComparer.Instance);

    // #307: inner function declarations that capture variables living on an
    // ancestor arrow's scope display class get a live $arrowScopeDC reference
    // (populated at hoist time) instead of hoist-time value snapshots —
    // hoisting runs BEFORE the body statements that assign those variables,
    // so snapshots read null/stale. The reference is threaded through
    // intermediate callables when the source arrow isn't the immediate parent.
    private readonly Dictionary<Stmt.Function, object> _innerFunctionParentCallable = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, FieldBuilder> _innerFunctionArrowScopeDCFields = new(ReferenceEqualityComparer.Instance);
    // Source values (and the set/dict elements below) are the scope-OWNING callable's
    // AST node — Expr.ArrowFunction or Stmt.Function (#313).
    private readonly Dictionary<Stmt.Function, object> _innerFunctionArrowScopeDCSource = new(ReferenceEqualityComparer.Instance);

    // EXTRA (non-primary) ancestor scope DC sources. A closure capturing from
    // more than one ancestor scope gets one reference field per source —
    // the single primary slot can't represent multi-source captures (lodash's
    // shortOut closure reads nativeNow from runInContext AND HOT_SPAN from the
    // outer IIFE). Sets are accumulated during threading; fields are defined
    // alongside the primary in the respective define passes.
    private readonly Dictionary<Stmt.Function, HashSet<object>> _innerFunctionExtraScopeSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, HashSet<object>> _arrowExtraScopeSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.Function, Dictionary<object, FieldBuilder>> _innerFunctionArrowScopeDCExtraFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, Dictionary<object, FieldBuilder>> _arrowScopeDCExtraFields = new(ReferenceEqualityComparer.Instance);

    private static void AddExtraScopeSource<TKey>(Dictionary<TKey, HashSet<object>> map, TKey key, object source)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var set))
        {
            set = new HashSet<object>(ReferenceEqualityComparer.Instance);
            map[key] = set;
        }
        set.Add(source);
    }

    // Nesting depth tracker: 0 = top level, 1+ = inside a function body
    private int _functionNestingDepth;

    // Tracks which enclosing function name each inner function belongs to
    private string? _currentEnclosingFunctionName;

    /// <summary>
    /// Collects an inner function declaration found during arrow function collection.
    /// Called from CollectArrowsFromStmt when nesting depth > 0.
    /// </summary>
    private void CollectInnerFunction(Stmt.Function funcStmt)
    {
        var captures = new HashSet<string>(_closures.Analyzer.GetCaptures(funcStmt));
        // Remove self-reference: the function's own name is not a true capture.
        // Self-calls are handled via InnerFunctionMethodsByName direct dispatch.
        captures.Remove(funcStmt.Name.Lexeme);
        // _currentEnclosingFunctionName is null when the enclosing callable is an arrow
        // function (Expr.ArrowFunction) rather than a named Stmt.Function — there's no
        // qualified name to key a function-level display class on. Empty string is used
        // as a sentinel so downstream dict lookups return "no match" instead of throwing
        // ArgumentNullException.
        _collectedInnerFunctions.Add((funcStmt, captures, _currentEnclosingFunctionName ?? ""));

        // Record the callable whose body this declaration is hoisted into.
        if (_currentEnclosingCallable != null)
            _innerFunctionParentCallable[funcStmt] = _currentEnclosingCallable;
    }

    /// <summary>
    /// Resolves, for every collected inner function declaration, the ancestor arrow
    /// whose scope display class provides its captured variables (#307), and marks
    /// every intermediate callable between the function and that arrow so the live
    /// DC reference can be threaded through at hoist/creation time. Must run after
    /// arrow scope display classes are defined and arrows' own $arrowDC sources are
    /// assigned (PropagateArrowDCRequirements), but before arrow methods/display
    /// classes are defined.
    /// </summary>
    private void ResolveInnerFunctionArrowScopeSources()
    {
        var collectedSet = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        foreach (var (f, _, _) in _collectedInnerFunctions)
            collectedSet.Add(f);

        // Collection order is outer-to-inner, so a parent's own capture needs are
        // resolved before any child threads a pass-through requirement onto it.
        foreach (var (func, captures, _) in _collectedInnerFunctions)
        {
            // Candidate source callables: the analyzer-recorded defining scope of
            // each captured variable, when that scope owns a scope DC with a
            // matching field (arrow or inner function declaration, #313).
            Dictionary<object, int>? candidates = null;
            foreach (var c in captures)
            {
                if (_closures.Analyzer.GetCaptureSource(func, c) is not { } sa)
                    continue;
                if (!_closures.ArrowScopeDisplayClassFields.TryGetValue(sa, out var fields) ||
                    !fields.ContainsKey(c))
                    continue;
                candidates ??= new(ReferenceEqualityComparer.Instance);
                candidates[sa] = candidates.TryGetValue(sa, out var n) ? n + 1 : 1;
            }
            if (candidates == null)
                continue;

            // Nearest threadable candidate becomes the PRIMARY $arrowScopeDC source
            // (unless pass-through threading already claimed the slot); every other
            // threadable candidate gets an EXTRA reference field.
            _innerFunctionParentCallable.TryGetValue(func, out var start);
            foreach (var sa in OrderByAncestorProximity(start, candidates))
            {
                if (!TryThreadArrowScopeSource(start, sa, collectedSet))
                    continue; // unreachable chain — this source keeps snapshot semantics
                if (!_innerFunctionArrowScopeDCSource.TryGetValue(func, out var existing))
                    _innerFunctionArrowScopeDCSource[func] = sa;
                else if (!ReferenceEquals(existing, sa))
                    AddExtraScopeSource(_innerFunctionExtraScopeSources, func, sa);
            }
        }
    }

    /// <summary>
    /// Orders candidate source callables (arrows or inner function declarations)
    /// by proximity along the enclosing-callable chain starting at
    /// <paramref name="start"/> — nearest ancestor first. The single
    /// $arrowScopeDC/$arrowDC slot can only bind one source; the nearest
    /// scope is the one whose variables are assigned in the same bodies that
    /// hoist these closures, so it's the scope that NEEDS live reads (lodash's
    /// runInContext helpers). Farther scopes' variables are typically assigned
    /// before the closure is created, so their copy-field snapshots hold the
    /// right values when reachable.
    /// </summary>
    private IEnumerable<object> OrderByAncestorProximity(
        object? start, Dictionary<object, int> candidates)
    {
        var node = start;
        int guard = 0;
        while (node != null && ++guard <= 256)
        {
            if (candidates.ContainsKey(node))
                yield return node;

            if (node is Expr.ArrowFunction ia)
            {
                _arrowEnclosingCallable.TryGetValue(ia, out node);
            }
            else if (node is Stmt.Function pf)
            {
                _innerFunctionParentCallable.TryGetValue(pf, out node);
            }
            else
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Walks the enclosing-callable chain from <paramref name="start"/> up to
    /// <paramref name="source"/>. On success, marks every intermediate so it
    /// carries a live reference to the source's scope DC: the primary slot
    /// ($arrowDC / $arrowScopeDC source) when free or already bound to this
    /// source, otherwise an EXTRA reference field. Fails (committing nothing)
    /// when the chain is broken — async arrow or uncollected function boundary.
    /// </summary>
    private bool TryThreadArrowScopeSource(object? start, object source, HashSet<Stmt.Function> collectedSet)
    {
        var arrowsToMark = new List<Expr.ArrowFunction>();
        var passThroughFuncs = new List<Stmt.Function>();

        var node = start;
        int guard = 0;
        while (node != null && !ReferenceEquals(node, source))
        {
            if (++guard > 256)
                return false;

            if (node is Expr.ArrowFunction ia)
            {
                if (ia.IsAsync)
                    return false;
                arrowsToMark.Add(ia);
                _arrowEnclosingCallable.TryGetValue(ia, out node);
            }
            else if (node is Stmt.Function pf && collectedSet.Contains(pf))
            {
                passThroughFuncs.Add(pf);
                _innerFunctionParentCallable.TryGetValue(pf, out node);
            }
            else
            {
                return false;
            }
        }
        if (node == null)
            return false;

        foreach (var ia in arrowsToMark)
        {
            _arrowsNeedingArrowDC.Add(ia);
            if (!_closures.ArrowScopeDCSource.TryGetValue(ia, out var iaSrc))
                _closures.ArrowScopeDCSource[ia] = source;
            else if (!ReferenceEquals(iaSrc, source))
                AddExtraScopeSource(_arrowExtraScopeSources, ia, source);
        }
        foreach (var pf in passThroughFuncs)
        {
            if (!_innerFunctionArrowScopeDCSource.TryGetValue(pf, out var pfSrc))
                _innerFunctionArrowScopeDCSource[pf] = source;
            else if (!ReferenceEquals(pfSrc, source))
                AddExtraScopeSource(_innerFunctionExtraScopeSources, pf, source);
        }
        return true;
    }

    /// <summary>
    /// Defines methods and display classes for all collected inner functions.
    /// Non-capturing inner functions become static methods on $Program.
    /// Capturing ones get display classes with Invoke methods, mirroring the arrow pattern.
    /// </summary>
    private void DefineInnerFunctions()
    {
        foreach (var (func, captures, enclosingFuncName) in _collectedInnerFunctions)
        {
            // Resolve parameter types (use annotations only, no TypeMap function type)
            var resolvedParamTypes = ParameterTypeResolver.ResolveParameters(
                func.Parameters, _typeMapper, null, _typeMap);

            Type[] paramTypes = new Type[func.Parameters.Count];
            for (int i = 0; i < func.Parameters.Count; i++)
                paramTypes[i] = func.Parameters[i].IsRest ? _types.ListOfObject : resolvedParamTypes[i];

            // Store resolved param types for use during body emission
            _innerFunctionParamTypes[func] = resolvedParamTypes;

            // Return type is always object for inner functions (no TypeMap lookup)
            Type returnType = _types.Object;

            // Check if this inner function captures function-level variables
            bool needsFunctionDC = false;
            string? sourceFunctionForDC = null;
            if (_closures.FunctionDisplayClassFields.TryGetValue(enclosingFuncName, out var enclosingFuncDCFields))
            {
                if (captures.Any(c => enclosingFuncDCFields.ContainsKey(c)))
                {
                    needsFunctionDC = true;
                    sourceFunctionForDC = enclosingFuncName;
                }
            }

            // Check if any captured vars are top-level captured vars
            bool needsEntryPointDC = _closures.EntryPointDisplayClass != null &&
                captures.Any(c => _closures.CapturedTopLevelVars.Contains(c));

            // #307: captured vars that live on an ancestor arrow's scope display
            // class are accessed LIVE via a $arrowScopeDC reference, not copied at
            // hoist time (the copy would run before the arrow body assigns them).
            // The source was resolved by ResolveInnerFunctionArrowScopeSources —
            // it may also be a pass-through requirement from a nested function.
            object? arrowScopeSource = null;
            Dictionary<string, FieldBuilder>? arrowScopeSourceFields = null;
            if (_innerFunctionArrowScopeDCSource.TryGetValue(func, out var resolvedSource) &&
                _closures.ArrowScopeDisplayClassFields.TryGetValue(resolvedSource, out arrowScopeSourceFields))
            {
                arrowScopeSource = resolvedSource;
            }
            _innerFunctionExtraScopeSources.TryGetValue(func, out var extraScopeSources);

            if (captures.Count == 0 && !needsFunctionDC && arrowScopeSource == null && extraScopeSources == null)
            {
                // Non-capturing: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>InnerFunc_{_closures.ArrowMethodCounter++}_{func.Name.Lexeme}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    returnType,
                    paramTypes
                );

                for (int i = 0; i < func.Parameters.Count; i++)
                    methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, func.Parameters[i].Name.Lexeme);

                _innerFunctionMethods[func] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = EmitTypeDefinitions.DefineType(_moduleBuilder,
                    $"<>c__InnerFuncDC{_closures.DisplayClassCounter++}_{func.Name.Lexeme}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    _types.Object
                );
                MarkCompilerGenerated(displayClass);

                // Add fields for captured variables
                Dictionary<string, FieldBuilder> fieldMap = [];
                FieldBuilder? entryPointDCField = null;
                FieldBuilder? functionDCField = null;

                if (needsEntryPointDC)
                {
                    entryPointDCField = displayClass.DefineField("$entryPointDC", _closures.EntryPointDisplayClass!, FieldAttributes.Public);
                }

                if (needsFunctionDC && sourceFunctionForDC != null &&
                    _closures.FunctionDisplayClasses.TryGetValue(sourceFunctionForDC, out var funcDC))
                {
                    functionDCField = displayClass.DefineField("$functionDC", funcDC, FieldAttributes.Public);
                }

                FieldBuilder? arrowScopeDCField = null;
                if (arrowScopeSource != null &&
                    _closures.ArrowScopeDisplayClasses.TryGetValue(arrowScopeSource, out var arrowScopeDC))
                {
                    arrowScopeDCField = displayClass.DefineField("$arrowScopeDC", arrowScopeDC, FieldAttributes.Public);
                }

                Dictionary<object, FieldBuilder>? extraScopeDCFields = null;
                if (extraScopeSources != null)
                {
                    int extraIdx = 0;
                    foreach (var extraSource in extraScopeSources)
                    {
                        if (ReferenceEquals(extraSource, arrowScopeSource))
                            continue;
                        if (!_closures.ArrowScopeDisplayClasses.TryGetValue(extraSource, out var extraDC))
                            continue;
                        var refField = displayClass.DefineField($"$arrowScopeDC{++extraIdx}", extraDC, FieldAttributes.Public);
                        (extraScopeDCFields ??= new(ReferenceEqualityComparer.Instance))[extraSource] = refField;
                    }
                }

                foreach (var capturedVar in captures)
                {
                    // Skip top-level captured vars - accessed through $entryPointDC
                    if (_closures.CapturedTopLevelVars.Contains(capturedVar))
                        continue;

                    // Skip function-level captured vars - accessed through $functionDC
                    if (needsFunctionDC && sourceFunctionForDC != null &&
                        _closures.FunctionDisplayClassFields.TryGetValue(sourceFunctionForDC, out var funcFields) &&
                        funcFields.ContainsKey(capturedVar))
                        continue;

                    // Skip enclosing-arrow scope vars - accessed live through $arrowScopeDC
                    if (arrowScopeDCField != null &&
                        arrowScopeSourceFields!.ContainsKey(capturedVar) &&
                        ReferenceEquals(_closures.Analyzer.GetCaptureSource(func, capturedVar), arrowScopeSource))
                        continue;

                    // Skip vars covered by an EXTRA ancestor scope DC reference
                    if (extraScopeDCFields != null &&
                        _closures.Analyzer.GetCaptureSource(func, capturedVar) is { } extraSrc &&
                        extraScopeDCFields.ContainsKey(extraSrc) &&
                        _closures.ArrowScopeDisplayClassFields.TryGetValue(extraSrc, out var extraSrcFields) &&
                        extraSrcFields.ContainsKey(capturedVar))
                        continue;

                    var field = displayClass.DefineField(capturedVar, _types.Object, FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }

                _innerFunctionDCFields[func] = fieldMap;

                if (entryPointDCField != null)
                    _innerFunctionEntryPointDCFields[func] = entryPointDCField;

                if (functionDCField != null)
                    _innerFunctionFunctionDCFields[func] = functionDCField;

                if (arrowScopeDCField != null)
                    _innerFunctionArrowScopeDCFields[func] = arrowScopeDCField;

                if (extraScopeDCFields != null)
                    _innerFunctionArrowScopeDCExtraFields[func] = extraScopeDCFields;

                // Default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
                ctorIL.Emit(OpCodes.Ret);
                _innerFunctionDCCtors[func] = ctorBuilder;

                // Invoke method on display class
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    returnType,
                    paramTypes
                );

                for (int i = 0; i < func.Parameters.Count; i++)
                    invokeMethod.DefineParameter(i + 1, ParameterAttributes.None, func.Parameters[i].Name.Lexeme);

                _innerFunctionDisplayClasses[func] = displayClass;
                _innerFunctionMethods[func] = invokeMethod;
            }

            // User inner-function body: when invoked as a value, omitted trailing args must
            // pad with the `undefined` sentinel (JS semantics), not CLR null. (#640)
            MarkPadsUndefined(_innerFunctionMethods[func]);
        }
    }

    /// <summary>
    /// Emits method bodies for all collected inner functions.
    /// Follows the same pattern as EmitArrowBody.
    /// </summary>
    private void EmitInnerFunctionBodies()
    {
        var savedPath = _modules.CurrentPath;
        foreach (var (func, captures, enclosingFuncName) in _collectedInnerFunctions)
        {
            if (_functionDefinitionModule.TryGetValue(enclosingFuncName, out var fnModule))
            {
                _modules.CurrentPath = NormalizeToEmissionPath(fnModule);
            }
            var method = _innerFunctionMethods[func];
            var hasDisplayClass = _innerFunctionDisplayClasses.TryGetValue(func, out var displayClass);

            var il = ((MethodBuilder)method).GetILGenerator();

            // Get resolved parameter types for this inner function
            _innerFunctionParamTypes.TryGetValue(func, out var innerParamTypes);

            var ctx = CreateModuleMemberContext(il, ((MethodBuilder)method));
            ApplyCapturedTopLevelVariableAccess(ctx);
            ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
            ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
            ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
            ApplyInnerFunctionSupport(ctx);
            ctx.CurrentMethodReturnType = _types.Object;
            // Enable self-reference: the function can call itself by name
            ctx.InnerFunctionMethodsByName = new Dictionary<string, MethodBuilder>
            {
                [func.Name.Lexeme] = method
            };
            ctx.InnerFunctionDisplayClassesByName = hasDisplayClass
                ? new Dictionary<string, TypeBuilder> { [func.Name.Lexeme] = displayClass! }
                : [];

            if (hasDisplayClass)
            {
                // Instance method on display class - this is arg 0 for captures access.
                ctx.IsInstanceMethod = true;

                // BUT arg0 on a display class is the display-class self, NOT the
                // function's JS `this`. Flag this so LoadThis consults the thread-local
                // _currentFunctionThis (set by $Runtime.NewOnFunction etc.) instead of
                // Ldarg_0.
                ctx.IsInnerFunctionOnDisplayClass = true;

                if (_innerFunctionDCFields.TryGetValue(func, out var fieldMap))
                    ctx.CapturedFields = fieldMap;
                else
                    ctx.CapturedFields = [];

                // Set $entryPointDC field
                if (_innerFunctionEntryPointDCFields.TryGetValue(func, out var epDCField))
                    ctx.CurrentArrowEntryPointDCField = epDCField;

                // Set $functionDC field
                if (_innerFunctionFunctionDCFields.TryGetValue(func, out var funcDCField))
                {
                    ctx.CurrentArrowFunctionDCField = funcDCField;

                    // Set up captured function locals info
                    bool needsFunctionDC = false;
                    string? sourceFuncName = null;
                    if (_closures.FunctionDisplayClassFields.TryGetValue(enclosingFuncName, out var enclosingDCFields))
                    {
                        if (captures.Any(c => enclosingDCFields.ContainsKey(c)))
                        {
                            needsFunctionDC = true;
                            sourceFuncName = enclosingFuncName;
                        }
                    }

                    if (needsFunctionDC && sourceFuncName != null &&
                        _closures.FunctionDisplayClassFields.TryGetValue(sourceFuncName, out var funcDCFields))
                    {
                        ctx.FunctionDisplayClassFields = funcDCFields;
                        ctx.CapturedFunctionLocals = [.. funcDCFields.Keys];
                    }
                }

                // Set $arrowScopeDC field (#307): route captured enclosing-arrow
                // locals through the live reference so reads/writes hit the arrow's
                // scope DC (LocalVariableResolver parent-DC paths). Restrict the
                // name set to this function's own analyzer-confirmed captures so
                // names shadowed by locals/params keep their normal resolution.
                if (_innerFunctionArrowScopeDCFields.TryGetValue(func, out var arrowScopeRefField) &&
                    _innerFunctionArrowScopeDCSource.TryGetValue(func, out var arrowScopeSrc) &&
                    _closures.ArrowScopeDisplayClassFields.TryGetValue(arrowScopeSrc, out var srcArrowFields))
                {
                    ctx.CurrentArrowScopeDCField = arrowScopeRefField;
                    ctx.ParentArrowScopeDisplayClassFields = srcArrowFields;
                    ctx.ParentArrowCapturedLocals = [.. captures.Where(c =>
                        srcArrowFields.ContainsKey(c) &&
                        ReferenceEquals(_closures.Analyzer.GetCaptureSource(func, c), arrowScopeSrc))];
                }

                // EXTRA ancestor scope references: per-name live bindings plus the
                // raw ref fields for chaining into closures created in this body.
                if (_innerFunctionArrowScopeDCExtraFields.TryGetValue(func, out var ownExtraRefs))
                {
                    ctx.CurrentArrowScopeDCExtraFields = ownExtraRefs;
                    ctx.ExtraArrowScopeBindings = BuildExtraScopeBindings(func, captures, ownExtraRefs);
                }

                // Parameters start at index 1 (display class is arg 0)
                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    Type? paramType = innerParamTypes != null && i < innerParamTypes.Length ? innerParamTypes[i] : null;
                    ctx.DefineParameter(func.Parameters[i].Name.Lexeme, i + 1, paramType);
                }
            }
            else
            {
                // Static method - parameters start at index 0
                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    Type? paramType = innerParamTypes != null && i < innerParamTypes.Length ? innerParamTypes[i] : null;
                    ctx.DefineParameter(func.Parameters[i].Name.Lexeme, i, paramType);
                }
            }

            // Create this function's own scope display class instance when its locals
            // are captured by nested closures (#313) — the same per-invocation shared
            // storage arrows get in EmitArrowBody. Reads/writes of those locals route
            // through the DC; closures created in this body receive a live reference
            // via the type-matched $arrowDC population at their creation sites.
            if (_closures.ArrowScopeDisplayClasses.TryGetValue(func, out var ownScopeDCType) &&
                _closures.ArrowScopeDisplayClassCtors.TryGetValue(func, out var ownScopeDCCtor))
            {
                var ownScopeDCLocal = il.DeclareLocal(ownScopeDCType);
                il.Emit(OpCodes.Newobj, ownScopeDCCtor);
                il.Emit(OpCodes.Stloc, ownScopeDCLocal);
                ctx.ArrowScopeDisplayClassLocal = ownScopeDCLocal;
                ctx.ArrowScopeDisplayClassFields = _closures.ArrowScopeDisplayClassFields[func];
                // Derive from the DC's actual field map so per-iteration loop bindings
                // excluded by DefineScopeDisplayClass (#649) are also excluded here.
                ctx.CapturedArrowLocals = [.. _closures.ArrowScopeDisplayClassFields[func].Keys];
            }

            var emitter = new ILEmitter(ctx);

            var environmentParamTypes = new Type[func.Parameters.Count];
            for (int i = 0; i < environmentParamTypes.Length; i++)
                environmentParamTypes[i] = innerParamTypes != null && i < innerParamTypes.Length
                    ? innerParamTypes[i] ?? _types.Object
                    : _types.Object;
            EmitFunctionEnvironmentPrologue(
                il,
                ctx,
                emitter,
                func.Parameters,
                func.Body,
                environmentParamTypes,
                argumentOffset: hasDisplayClass ? 1 : 0);

            // Initialize captured parameters into the scope display class (mirrors
            // EmitArrowBody). Runs AFTER EmitDefaultParameters (which writes defaults
            // via Starg) so the DC fields see defaulted values. Later reassignments
            // dual-write DC + arg slot via LocalVariableResolver store path 1b.
            if (ctx.ArrowScopeDisplayClassLocal != null)
            {
                int paramArgBase = hasDisplayClass ? 1 : 0;
                for (int i = 0; i < func.Parameters.Count; i++)
                {
                    var paramName = func.Parameters[i].Name.Lexeme;
                    if (ctx.CapturedArrowLocals!.Contains(paramName) &&
                        ctx.ArrowScopeDisplayClassFields!.TryGetValue(paramName, out var scopeDCField))
                    {
                        il.Emit(OpCodes.Ldloc, ctx.ArrowScopeDisplayClassLocal);
                        il.Emit(OpCodes.Ldarg, i + paramArgBase);
                        var actualParamType = !func.Parameters[i].IsRest &&
                            innerParamTypes != null && i < innerParamTypes.Length ? innerParamTypes[i] : null;
                        if (actualParamType != null && actualParamType.IsValueType)
                            il.Emit(OpCodes.Box, actualParamType);
                        il.Emit(OpCodes.Stfld, scopeDCField);
                    }
                }
            }

            // Emit function body
            if (func.Body != null)
            {
                // Hoist inner functions within this inner function's body
                EmitInnerFunctionHoisting(il, ctx, func.Body);

                emitter.EmitStatements(func.Body);

                if (emitter.HasDeferredReturns)
                {
                    emitter.FinalizeReturns();
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }

        }
        _modules.CurrentPath = savedPath;
    }

    /// <summary>
    /// Emits hoisting code at the start of a function body for inner function declarations.
    /// Scans the body's TOP-LEVEL statements for Stmt.Function nodes and creates TSFunction locals
    /// for each. Also populates name-based lookup maps on the context for direct call dispatch.
    /// This implements JavaScript function declaration hoisting semantics.
    ///
    /// <para>Declarations nested inside a block/loop/if are NOT hoisted here — they are materialized
    /// in place at their textual position by <see cref="EmitBlockScopedInnerFunctionDeclaration"/>
    /// (wired onto the context below), so per-iteration captures snapshot the right values (#1230).</para>
    /// </summary>
    private void EmitInnerFunctionHoisting(ILGenerator il, CompilationContext ctx, List<Stmt> body)
    {
        // Wire the in-place materializer so the statement emitter can create block-scoped inner
        // function declarations this pre-pass deliberately skips. Also record which declarations we
        // hoist here, so the in-place path recognizes them as already-materialized and does nothing.
        ctx.EmitBlockScopedInnerFunction ??= EmitBlockScopedInnerFunctionDeclaration;
        var hoisted = ctx.HoistedInnerFunctions ??= new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);

        // Two-pass hoisting to match JS spec semantics. JavaScript hoists all
        // `function` declarations to the top of the containing scope before
        // any statement executes, so every hoisted function must see every
        // other hoisted function's final value regardless of source order.
        //
        // Pass 1: for each hoisted function, allocate the slot, Newobj its
        //   display-class instance (capturing $entryPointDC / $functionDC —
        //   both of which reference the enclosing context, not peer hoists),
        //   build the TSFunction, store it to the slot. Save each DC instance
        //   to a temp local so Pass 2 can revisit it.
        //
        // Pass 2: for each hoisted function with captures, reload its DC
        //   temp and populate captured-variable fields. By this point every
        //   peer hoist's slot holds its final TSFunction, so forward
        //   references (including cycles) resolve correctly.
        var dcTemps = new Dictionary<Stmt.Function, LocalBuilder>(ReferenceEqualityComparer.Instance);

        // ─── Pass 1: slot allocation + DC instantiation + TSFunction store ───
        foreach (var stmt in body)
        {
            if (stmt is not Stmt.Function funcStmt) continue;
            hoisted.Add(funcStmt);
            EmitInnerFunctionMaterialize(il, ctx, funcStmt, dcTemps);
        }

        // ─── Pass 2: populate captured-variable fields on each DC instance ───
        // Every peer hoisted function's slot now holds its final TSFunction, so loads
        // through CapturedFunctionLocals / CapturedArrowLocals / locals resolve correctly.
        foreach (var stmt in body)
        {
            if (stmt is not Stmt.Function funcStmt) continue;
            EmitInnerFunctionPopulateCaptures(il, ctx, funcStmt, dcTemps);
        }
    }

    /// <summary>
    /// Wires the in-place inner-function materializer (<see cref="EmitBlockScopedInnerFunctionDeclaration"/>)
    /// onto <paramref name="ctx"/> WITHOUT running the top-of-body two-pass hoist that
    /// <see cref="EmitInnerFunctionHoisting"/> performs. Class-method bodies (#1237) use this: unlike a
    /// plain function or arrow, a method has no function-level display class, so hoisting a capturing
    /// inner function to the top of the body would snapshot its captured method-locals BEFORE the body
    /// assigns them — leaving the closure's capture fields null. Leaving
    /// <see cref="CompilationContext.HoistedInnerFunctions"/> empty means every inner function
    /// declaration — top-level-of-body or block-nested — is materialized in place at its textual
    /// position by the statement emitter's <c>Stmt.Function</c> arm, so its capture snapshot sees the
    /// values already assigned. This matches how arrows inside methods capture (snapshot at creation),
    /// and admits every program the type checker allows: it rejects forward references to a method's
    /// inner functions (unlike function bodies, which it hoists), so no valid program needs the two-pass
    /// pre-materialization here.
    /// </summary>
    private void WireInPlaceInnerFunctions(CompilationContext ctx)
    {
        ctx.EmitBlockScopedInnerFunction ??= EmitBlockScopedInnerFunctionDeclaration;
        ctx.HoistedInnerFunctions ??= new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Materializes a single inner <c>function</c> declaration nested inside a block/loop/if at its
    /// textual position: creates its TSFunction and binds a block-scoped local, then snapshots its
    /// captures. Unlike the callable-body pre-pass (<see cref="EmitInnerFunctionHoisting"/>), this
    /// runs where the declaration actually appears — after the preceding block statements have
    /// executed — so a closure over a per-iteration loop binding captures that iteration's value
    /// rather than a hoist-time null/stale snapshot (#1230).
    ///
    /// <para>No-op when the declaration was already hoisted at the callable-body top level, or when
    /// it was never collected as an inner function (e.g. an overload signature).</para>
    /// </summary>
    private void EmitBlockScopedInnerFunctionDeclaration(ILGenerator il, CompilationContext ctx, Stmt.Function funcStmt)
    {
        if (funcStmt.Body == null) return; // overload signature — nothing to emit
        if (ctx.HoistedInnerFunctions?.Contains(funcStmt) == true) return; // already materialized by the pre-pass
        if (!_innerFunctionMethods.ContainsKey(funcStmt)) return; // not collected as an inner function

        // The type checker requires block-scoped function declarations to be declared before any
        // reference (forward references inside a block are a "not defined" error), so a single
        // create-then-populate pass at the declaration site is sufficient — no peer forward refs.
        var dcTemps = new Dictionary<Stmt.Function, LocalBuilder>(ReferenceEqualityComparer.Instance);
        EmitInnerFunctionMaterialize(il, ctx, funcStmt, dcTemps);
        EmitInnerFunctionPopulateCaptures(il, ctx, funcStmt, dcTemps);
    }

    /// <summary>
    /// Pass 1 for one inner function declaration: allocate its binding slot, instantiate its display
    /// class (populating the $entryPointDC / $functionDC / $arrowScopeDC references), build the
    /// TSFunction, and store it. When the declaration captures, stashes the display-class instance in
    /// <paramref name="dcTemps"/> so its captured-variable fields can be populated afterwards.
    /// </summary>
    private void EmitInnerFunctionMaterialize(ILGenerator il, CompilationContext ctx, Stmt.Function funcStmt, Dictionary<Stmt.Function, LocalBuilder> dcTemps)
    {
        if (funcStmt.Body == null) return; // Skip overload signatures
        if (!_innerFunctionMethods.TryGetValue(funcStmt, out var method)) return;

        var funcName = funcStmt.Name.Lexeme;

        // Check if this inner function's name is stored in the enclosing function's display class.
        // This happens when the inner function references itself (self-reference is seen as a
        // captured outer variable by ClosureAnalyzer because the function name is declared
        // in the enclosing scope). We need to store the TSFunction in the DC field so that
        // LocalVariableResolver (which checks DC fields before locals) finds it correctly.
        FieldBuilder? funcDCStoreField = null;
        bool storeInFunctionDC = ctx.CapturedFunctionLocals?.Contains(funcName) == true &&
            ctx.FunctionDisplayClassFields?.TryGetValue(funcName, out funcDCStoreField) == true &&
            ctx.FunctionDisplayClassLocal != null;

        // Parallel check for the enclosing *arrow's* scope display class. Named function
        // expressions compile as arrows, so when an inner `function X()` is declared inside
        // one and X is captured by yet-another inner closure, X lives in the arrow's
        // ArrowScopeDC. LocalVariableResolver reads via that DC field, so we must write to
        // it here. Without this, reads return the zeroed field (null) and `typeof X` yields
        // "object" — the lodash `var _ = runInContext()` failure mode.
        FieldBuilder? arrowScopeDCStoreField = null;
        bool storeInArrowScopeDC = !storeInFunctionDC &&
            ctx.CapturedArrowLocals?.Contains(funcName) == true &&
            ctx.ArrowScopeDisplayClassFields?.TryGetValue(funcName, out arrowScopeDCStoreField) == true &&
            ctx.ArrowScopeDisplayClassLocal != null;

        // Also declare a regular local (used when not stored in DC, or as fallback)
        LocalBuilder? local = null;
        if (!storeInFunctionDC && !storeInArrowScopeDC)
            local = ctx.Locals.DeclareLocal(funcName, _types.Object);

        if (_innerFunctionDisplayClasses.TryGetValue(funcStmt, out var displayClass))
        {
            // Capturing: create display class instance, populate context fields,
            // stash the instance for capture population, then build TSFunction.
            var ctor = _innerFunctionDCCtors[funcStmt];
            il.Emit(OpCodes.Newobj, ctor);

            // Populate $entryPointDC field (references the enclosing entry-point DC;
            // safe to populate here — it isn't a peer hoisted function).
            if (_innerFunctionEntryPointDCFields.TryGetValue(funcStmt, out var epDCField))
            {
                if (ctx.EntryPointDisplayClassLocal != null)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldloc, ctx.EntryPointDisplayClassLocal);
                    il.Emit(OpCodes.Stfld, epDCField);
                }
                else if (ctx.EntryPointDisplayClassStaticField != null)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldsfld, ctx.EntryPointDisplayClassStaticField);
                    il.Emit(OpCodes.Stfld, epDCField);
                }
            }

            // Populate $functionDC field (references the enclosing function DC; same logic).
            if (_innerFunctionFunctionDCFields.TryGetValue(funcStmt, out var funcDCField))
            {
                if (ctx.FunctionDisplayClassLocal != null)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldloc, ctx.FunctionDisplayClassLocal);
                    il.Emit(OpCodes.Stfld, funcDCField);
                }
            }

            // Populate $arrowScopeDC (and any extra ancestor refs) with live
            // REFERENCES to the source scope DCs (#307). The reference is valid
            // at hoist time even though the DC's variable fields are assigned
            // later by body statements — that's the point: reads go through the
            // reference at call time.
            if (_innerFunctionArrowScopeDCFields.TryGetValue(funcStmt, out var arrowScopeDCRefField))
                EmitScopeDCRefStoreOnTop(il, ctx, arrowScopeDCRefField);
            if (_innerFunctionArrowScopeDCExtraFields.TryGetValue(funcStmt, out var extraRefFields))
                foreach (var refField in extraRefFields.Values)
                    EmitScopeDCRefStoreOnTop(il, ctx, refField);

            // Stash the DC instance in a temp local so its captured variable fields can be
            // populated after every peer hoist has stored its final TSFunction.
            var dcTemp = il.DeclareLocal(displayClass);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc, dcTemp);
            dcTemps[funcStmt] = dcTemp;

            // Create TSFunction: new TSFunction(displayInstance, invokeMethod)
            // Stack has: displayInstance
            il.Emit(OpCodes.Ldtoken, method);
            il.Emit(OpCodes.Ldtoken, displayClass);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
        }
        else
        {
            // Non-capturing: new TSFunction(null, staticMethod)
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, method);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
        }

        // Store TSFunction in the appropriate location
        if (storeInFunctionDC)
        {
            // Store in the enclosing function's display class field
            var temp = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, temp);
            il.Emit(OpCodes.Ldloc, ctx.FunctionDisplayClassLocal!);
            il.Emit(OpCodes.Ldloc, temp);
            il.Emit(OpCodes.Stfld, funcDCStoreField!);
        }
        else if (storeInArrowScopeDC)
        {
            // Store in the enclosing arrow's scope display class field
            var temp = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, temp);
            il.Emit(OpCodes.Ldloc, ctx.ArrowScopeDisplayClassLocal!);
            il.Emit(OpCodes.Ldloc, temp);
            il.Emit(OpCodes.Stfld, arrowScopeDCStoreField!);
        }
        else
        {
            // Store in local variable
            il.Emit(OpCodes.Stloc, local!);
        }
    }

    /// <summary>
    /// Pass 2 for one inner function declaration: reload its stashed display-class instance and
    /// populate each captured-variable field from wherever that variable currently lives (parameter,
    /// enclosing DC, or local). No-op for a non-capturing declaration (no stashed instance).
    /// </summary>
    private void EmitInnerFunctionPopulateCaptures(ILGenerator il, CompilationContext ctx, Stmt.Function funcStmt, Dictionary<Stmt.Function, LocalBuilder> dcTemps)
    {
        if (!dcTemps.TryGetValue(funcStmt, out var dcTemp)) return;
        if (!_innerFunctionDCFields.TryGetValue(funcStmt, out var fieldMap)) return;

        foreach (var (capturedVar, field) in fieldMap)
        {
            il.Emit(OpCodes.Ldloc, dcTemp);

            if (ctx.TryGetParameter(capturedVar, out var argIndex))
            {
                il.Emit(OpCodes.Ldarg, argIndex);
                if (ctx.TryGetParameterType(capturedVar, out var paramType) && paramType != null && paramType.IsValueType)
                    il.Emit(OpCodes.Box, paramType);
            }
            else if (ctx.CapturedFields != null && ctx.CapturedFields.TryGetValue(capturedVar, out var capturedField))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, capturedField);
            }
            else if (ctx.CapturedTopLevelVars?.Contains(capturedVar) == true &&
                     ctx.EntryPointDisplayClassFields?.TryGetValue(capturedVar, out var epField) == true)
            {
                if (ctx.EntryPointDisplayClassLocal != null)
                    il.Emit(OpCodes.Ldloc, ctx.EntryPointDisplayClassLocal);
                else if (ctx.EntryPointDisplayClassStaticField != null)
                    il.Emit(OpCodes.Ldsfld, ctx.EntryPointDisplayClassStaticField);
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldfld, epField);
            }
            else if (ctx.CapturedFunctionLocals?.Contains(capturedVar) == true &&
                     ctx.FunctionDisplayClassFields?.TryGetValue(capturedVar, out var funcField) == true)
            {
                if (ctx.FunctionDisplayClassLocal != null)
                {
                    il.Emit(OpCodes.Ldloc, ctx.FunctionDisplayClassLocal);
                    il.Emit(OpCodes.Ldfld, funcField);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
            }
            else if (ctx.CapturedArrowLocals?.Contains(capturedVar) == true &&
                     ctx.ArrowScopeDisplayClassFields?.TryGetValue(capturedVar, out var arrowField) == true &&
                     ctx.ArrowScopeDisplayClassLocal != null)
            {
                il.Emit(OpCodes.Ldloc, ctx.ArrowScopeDisplayClassLocal);
                il.Emit(OpCodes.Ldfld, arrowField);
            }
            else if (ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(capturedVar, out var parentArrowField) == true &&
                     ctx.CurrentArrowScopeDCField != null)
            {
                // Captured from an ancestor arrow's scope DC reachable through
                // the enclosing closure's $arrowDC/$arrowScopeDC reference.
                // Happens when the inner function's single $arrowScopeDC slot
                // is bound to a DIFFERENT (closer) source arrow, so this var
                // falls back to a copy-field — populate it from the chained
                // ancestor DC instead of emitting null (lodash reIsHostCtor).
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ctx.CurrentArrowScopeDCField);
                il.Emit(OpCodes.Ldfld, parentArrowField);
            }
            else if (ctx.TopLevelStaticVars != null && ctx.TopLevelStaticVars.TryGetValue(capturedVar, out var topField))
            {
                il.Emit(OpCodes.Ldsfld, topField);
            }
            else if (ctx.Functions.TryGetValue(ctx.ResolveFunctionName(capturedVar), out var capturedFuncMethod))
            {
                // Inner function declarations capture function objects too.
                // Populate the field with the canonical MethodInfo-keyed wrapper
                // so expandos assigned to the declaration remain observable in
                // the nested body. A null fallback here made test262 helpers such
                // as assert.sameValue and assert.throwsAsync disappear only when
                // referenced from a nested declaration.
                il.Emit(OpCodes.Ldtoken, capturedFuncMethod);
                if (ctx.ProgramType != null)
                {
                    il.Emit(OpCodes.Ldtoken, ctx.ProgramType);
                    il.Emit(OpCodes.Call, ctx.Types.MethodBaseGetMethodFromHandleWithType);
                }
                else
                {
                    il.Emit(OpCodes.Call, ctx.Types.MethodBaseGetMethodFromHandle);
                }
                il.Emit(OpCodes.Castclass, ctx.Types.MethodInfo);
                int arity = 0;
                foreach (var param in capturedFuncMethod.GetParameters())
                {
                    if (param.IsOptional) continue;
                    if (param.ParameterType == typeof(List<object>)) continue;
                    if (param.Name?.StartsWith("__") == true) continue;
                    arity++;
                }
                il.Emit(OpCodes.Ldstr, capturedVar);
                il.Emit(OpCodes.Ldc_I4, arity);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSFunctionGetOrCreate);
            }
            else
            {
                var existingLocal = ctx.Locals.GetLocal(capturedVar);
                if (existingLocal != null)
                {
                    il.Emit(OpCodes.Ldloc, existingLocal);
                    // The capture field is object-typed, so a value-type local (e.g. a
                    // per-iteration loop variable stored in an unboxed float64 slot) must
                    // be boxed before Stfld — otherwise a raw float64 lands where a
                    // reference is expected and corrupts the captured value (#1230). Matches
                    // the arrow creation path (EmitDisplayInstanceFieldPopulation).
                    if (existingLocal.LocalType.IsValueType)
                        il.Emit(OpCodes.Box, existingLocal.LocalType);
                }
                else
                    il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Stfld, field);
        }
    }

    /// <summary>
    /// Builds the per-name live bindings for a closure's captures that route
    /// through EXTRA ancestor scope DC references. Only analyzer-confirmed
    /// (closure, name) → source pairs are included, so shadowed names keep
    /// their normal resolution.
    /// </summary>
    private Dictionary<string, CompilationContext.ExtraScopeBinding>? BuildExtraScopeBindings(
        object closureNode, IEnumerable<string> captures, Dictionary<object, FieldBuilder> extraRefs)
    {
        Dictionary<string, CompilationContext.ExtraScopeBinding>? bindings = null;
        foreach (var c in captures)
        {
            if (_closures.Analyzer.GetCaptureSource(closureNode, c) is not { } src)
                continue;
            if (!extraRefs.TryGetValue(src, out var refField))
                continue;
            if (!_closures.ArrowScopeDisplayClassFields.TryGetValue(src, out var srcFields) ||
                !srcFields.TryGetValue(c, out var varField))
                continue;
            (bindings ??= [])[c] = new CompilationContext.ExtraScopeBinding(refField, varField);
        }
        return bindings;
    }

    /// <summary>
    /// With a display-class instance on top of the stack, stores a reference to
    /// the ancestor scope DC matching <paramref name="refField"/>'s type into
    /// that field, sourcing it from whatever the current emission context can
    /// reach: the body's own scope-DC local, the primary $arrowDC/$arrowScopeDC
    /// reference, or an extra ancestor reference. Leaves the instance on the
    /// stack. No-op (field stays null) when the context can't reach the DC —
    /// threading should have prevented that.
    /// </summary>
    private static void EmitScopeDCRefStoreOnTop(ILGenerator il, CompilationContext ctx, FieldBuilder refField)
    {
        if (ctx.ArrowScopeDisplayClassLocal != null &&
            ctx.ArrowScopeDisplayClassLocal.LocalType == refField.FieldType)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, ctx.ArrowScopeDisplayClassLocal);
            il.Emit(OpCodes.Stfld, refField);
            return;
        }
        if (ctx.CurrentArrowScopeDCField != null &&
            ctx.CurrentArrowScopeDCField.FieldType == refField.FieldType)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctx.CurrentArrowScopeDCField);
            il.Emit(OpCodes.Stfld, refField);
            return;
        }
        if (ctx.CurrentArrowScopeDCExtraFields != null)
        {
            foreach (var ownRef in ctx.CurrentArrowScopeDCExtraFields.Values)
            {
                if (ownRef.FieldType != refField.FieldType)
                    continue;
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ownRef);
                il.Emit(OpCodes.Stfld, refField);
                return;
            }
        }
    }

    /// <summary>
    /// Finalizes inner function display class types.
    /// </summary>
    private void FinalizeInnerFunctionDisplayClasses()
    {
        foreach (var tb in _innerFunctionDisplayClasses.Values)
        {
            tb.CreateType();
        }
    }
}
