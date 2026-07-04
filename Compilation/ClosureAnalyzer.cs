using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes the AST to determine which variables are captured by closures.
/// </summary>
/// <remarks>
/// Traverses the AST before IL emission to identify variables defined in outer scopes
/// that are referenced inside nested functions or arrow functions. These "captured"
/// variables require special handling via display classes during IL compilation.
/// Used by <see cref="ILCompiler"/> to decide whether arrow functions need closure
/// support or can be compiled as simple static methods.
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="CompilationContext"/>
public class ClosureAnalyzer : AstVisitorBase
{
    // Maps each function AST node to the set of variable names it captures
    private readonly Dictionary<object, HashSet<string>> _captures = [];

    // Maps each function AST node to the set of variable names defined within it (including params)
    private readonly Dictionary<object, HashSet<string>> _localVars = [];

    // Stack of scopes - each scope tracks variables declared at that level
    private readonly Stack<HashSet<string>> _scopeStack = new();

    // Current function being analyzed (for tracking captures)
    private object? _currentFunction;

    // Current function's name (for self-reference detection in named function expressions)
    private string? _currentFunctionName;

    // Set of variables defined in outer scopes relative to current function
    private readonly HashSet<string> _outerVariables = [];

    // ============================================
    // New: Function-level capture tracking
    // ============================================

    // Stack of function scopes - tracks the function node at each nesting level
    private readonly Stack<object?> _functionStack = new();

    // Maps function node → set of its local variables that are captured by inner closures
    private readonly Dictionary<object, HashSet<string>> _functionCapturedLocals = [];

    // Maps capturing function node → (captured name → defining function node).
    // Records WHICH enclosing scope provides each captured variable, so the
    // compiler can route the access to the correct display class without
    // name-matching across unrelated scopes (which aliases shadowed names).
    private readonly Dictionary<object, Dictionary<string, object>> _captureSources = [];

    // Maps closure node → captured names whose binding is a top-level BLOCK-scoped
    // declaration (the nearest declaring scope outside every function is a nested
    // scope, not the module root), captured by a closure created DIRECTLY in
    // top-level code (function stack depth 1). Such a capture must snapshot the
    // block's local like any ordinary local capture: a same-named entry-point
    // display class field belongs to a DIFFERENT (module-level) binding that the
    // block one shadows (#1222). Recorded only at depth 1 because a nested
    // closure's creation site can't see the entry method's locals — those shapes
    // keep the historical $entryPointDC chain routing.
    private readonly Dictionary<object, HashSet<string>> _directTopLevelBlockCaptures = [];

    // Maps function/arrow node → names it declares as `for (let/const …)` loop
    // bindings. Each iteration of such a loop gets its OWN binding (ECMA-262
    // 13.7.4 CreatePerIterationEnvironment), so a closure created in one iteration
    // must capture a value distinct from other iterations. The shared function
    // display class is a SINGLE instance per call, so routing a per-iteration loop
    // binding through it makes every closure read the loop's final value (#649).
    // Excluding these from the function DC keeps them as locals / state-machine
    // fields that closures snapshot per iteration — matching the top-level case,
    // which already stays correct for the same reason.
    private readonly Dictionary<object, HashSet<string>> _functionLoopBindings = [];

    // Names declared by a NON-loop binding in a function/arrow (params, plain
    // `let`/`const`/`var`). Used to keep the per-iteration exclusion shadow-safe:
    // a name that is also an ordinary captured-and-mutated local must keep its
    // shared function-DC slot, so it is excluded from the loop-binding set.
    private readonly Dictionary<object, HashSet<string>> _functionNonLoopDecls = [];

    // ============================================
    // Performance optimization: Inverse index
    // ============================================

    // O(1) lookup for whether any variable is captured (inverse of _captures)
    private readonly HashSet<string> _allCapturedVariables = [];

    // Per-iteration reference-cell analysis (#650), run alongside capture analysis
    // and reachable wherever this analyzer is threaded (via _ctx.ClosureAnalyzer).
    private readonly PerIterationCellAnalyzer _cells = new();

    /// <summary>
    /// The <c>for (let/const …)</c> binding names in <paramref name="loop"/> that need
    /// a per-iteration reference cell (#650); empty when none do.
    /// </summary>
    public HashSet<string> GetForLoopCells(Stmt.For loop) => _cells.GetForLoopCells(loop);

    /// <summary>
    /// The captured names in <paramref name="closure"/> that hold a per-iteration cell
    /// (the closure body must dereference <c>StrongBox.Value</c>); empty when none do.
    /// </summary>
    public HashSet<string> GetClosureCellFields(object closure) => _cells.GetClosureCellFields(closure);

    /// <summary>
    /// Gets the captured variables for a given function/arrow AST node.
    /// </summary>
    public HashSet<string> GetCaptures(object functionNode)
    {
        return _captures.TryGetValue(functionNode, out var captures)
            ? captures
            : [];
    }

    /// <summary>
    /// Checks if a variable is captured by any inner function in the current scope.
    /// O(1) lookup using inverse index.
    /// </summary>
    public bool IsVariableCaptured(string name)
    {
        return _allCapturedVariables.Contains(name);
    }

    /// <summary>
    /// Gets the set of local variables for a function that are captured by inner closures.
    /// These variables need to be hoisted to a display class for proper mutation propagation.
    /// </summary>
    public HashSet<string> GetCapturedLocals(object functionNode)
    {
        return _functionCapturedLocals.TryGetValue(functionNode, out var locals)
            ? locals
            : [];
    }

    /// <summary>
    /// Checks if a function has any local variables that are captured by inner closures.
    /// </summary>
    public bool HasCapturedLocals(object functionNode)
    {
        return _functionCapturedLocals.TryGetValue(functionNode, out var locals) && locals.Count > 0;
    }

    /// <summary>
    /// Returns the names that <paramref name="functionNode"/> declares EXCLUSIVELY as
    /// <c>for (let/const …)</c> loop bindings (never also as an ordinary local/param).
    /// These must be kept out of the shared function display class so closures created
    /// in different iterations capture distinct per-iteration values (ECMA-262 13.7.4;
    /// #649). Callers intersect this with the captured-locals set.
    /// </summary>
    public HashSet<string> GetPerIterationLoopBindings(object functionNode)
    {
        if (!_functionLoopBindings.TryGetValue(functionNode, out var bindings) || bindings.Count == 0)
            return [];
        var result = new HashSet<string>(bindings);
        // Shadow-safety: a name that is ALSO an ordinary declaration in this function
        // keeps its shared slot (an ordinary captured-and-mutated local must stay in
        // the function DC). Only purely-loop bindings are eligible for exclusion.
        if (_functionNonLoopDecls.TryGetValue(functionNode, out var nonLoop))
            result.ExceptWith(nonLoop);
        return result;
    }

    /// <summary>
    /// Returns the function/arrow AST node whose scope defines the variable
    /// <paramref name="name"/> captured by <paramref name="functionNode"/>,
    /// or null when the variable is top-level (or not a recorded capture).
    /// </summary>
    public object? GetCaptureSource(object functionNode, string name)
    {
        return _captureSources.TryGetValue(functionNode, out var sources) &&
               sources.TryGetValue(name, out var src)
            ? src
            : null;
    }

    /// <summary>
    /// True when <paramref name="functionNode"/> — a closure created directly in
    /// top-level code — captures <paramref name="name"/> from a top-level BLOCK
    /// scope rather than the module root scope (#1222). The capture then denotes
    /// the block's binding, which may lexically shadow a same-named module-level
    /// binding; name-keyed routing to the entry-point display class would read
    /// the outer binding instead. Callers must still exclude #1201-lifted
    /// bindings (whose home IS the display-class field) before rerouting.
    /// </summary>
    public bool IsDirectTopLevelBlockScopedCapture(object functionNode, string name)
    {
        return _directTopLevelBlockCaptures.TryGetValue(functionNode, out var names) &&
               names.Contains(name);
    }


    /// <summary>
    /// Analyze the entire program to detect captures.
    /// </summary>
    public void Analyze(List<Stmt> statements)
    {
        _scopeStack.Push([]);
        HoistFunctionDeclarations(statements);
        foreach (var stmt in statements)
            Visit(stmt);
        _scopeStack.Pop();

        // Per-iteration cell analysis (#650) is independent of capture-source state,
        // so run it as its own pass over the same statements.
        _cells.Analyze(statements);
    }

    /// <summary>
    /// Pre-declares function declaration names in the current scope so that forward
    /// references to sibling functions resolve as outer-scope references (and therefore
    /// as captures) when those siblings' bodies are analyzed before the declarations
    /// are reached in source order. Matches the interpreter's block-level hoisting
    /// (see <c>Interpreter.Statements.cs</c> and <c>Interpreter.HoistFunctionDeclarations</c>).
    /// </summary>
    private void HoistFunctionDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function f && f.Body != null)
                DeclareVariable(f.Name.Lexeme);
            else if (stmt is Stmt.Export export && export.Declaration is Stmt.Function ef && ef.Body != null)
                DeclareVariable(ef.Name.Lexeme);
            // Also hoist `var X = ...` declarations so inner function declarations appearing
            // earlier in source order see them as captured outer variables. JS spec hoists
            // var declarations to the top of the enclosing function. Without this, lodash's
            // pattern `function baseRest() { ... setToString(...) ... } ... var setToString
            // = shortOut(baseSetToString);` makes baseRest treat setToString as undeclared,
            // and the compiled inner function body emits a runtime ReferenceError fallback.
            else if (stmt is Stmt.Var vr && vr.IsVar)
                DeclareVariable(vr.Name.Lexeme);
            // Pre-declare block-scoped let/const for the same reason (#533): an inner function
            // declared BEFORE a let/const in this scope must still capture it, because the function
            // only runs after the binding is initialized. This method is called per-scope (module,
            // block, function body, arrow body), so each binding is declared in its own scope.
            // Unlike var, no source-order initialization is implied — capture analysis only needs to
            // know which scope OWNS the name so the inner function shares the binding's slot.
            else if (stmt is Stmt.Var lt && !lt.IsVar)
                DeclareVariable(lt.Name.Lexeme);
            else if (stmt is Stmt.Const ct)
                DeclareVariable(ct.Name.Lexeme);
        }
    }

    #region Scope management

    private void EnterScope() => _scopeStack.Push([]);
    private void ExitScope() => _scopeStack.Pop();

    private void DeclareVariable(string name)
    {
        if (_scopeStack.Count > 0)
            _scopeStack.Peek().Add(name);

        // Track local variables for the current function
        if (_currentFunction != null && _localVars.TryGetValue(_currentFunction, out var locals))
            locals.Add(name);

        // Record every NON-loop declaration so the per-iteration exclusion stays
        // shadow-safe (see _functionNonLoopDecls). A for-let/const loop binding is
        // declared with its name parked in _pendingLoopBindings, so it is skipped
        // here and counts only as a loop binding.
        if (_currentFunction != null && !_pendingLoopBindings.Contains(name))
        {
            if (!_functionNonLoopDecls.TryGetValue(_currentFunction, out var nonLoop))
                _functionNonLoopDecls[_currentFunction] = nonLoop = [];
            nonLoop.Add(name);
        }
    }

    // Loop-binding names currently being declared by a for-initializer, so
    // DeclareVariable records them as loop bindings rather than ordinary locals.
    private readonly HashSet<string> _pendingLoopBindings = [];

    private void ReferenceVariable(string name)
    {
        if (_currentFunction == null) return;

        // Skip built-ins
        if (name is "console.log" or "Math" or "console" or "undefined" or "NaN" or "Infinity" or "Symbol")
            return;

        // Check for self-reference in named function expressions
        // This needs to happen BEFORE the local variable check
        if (name == _currentFunctionName)
        {
            _captures[_currentFunction].Add(name);
            _allCapturedVariables.Add(name); // O(1) inverse index
            return;
        }

        // Check if this is a local variable in the current function
        if (_localVars.TryGetValue(_currentFunction, out var locals) && locals.Contains(name))
            return;

        // Check if it's an outer variable - this means it's captured
        if (_outerVariables.Contains(name))
        {
            _captures[_currentFunction].Add(name);
            _allCapturedVariables.Add(name); // O(1) inverse index

            // Track that this variable is captured from its defining function
            // This enables function-level display class creation
            // Walk up the function stack to find which function defines this variable
            var definingFunc = FindDefiningFunction(name);
            if (definingFunc != null)
            {
                // The variable is defined in some function, not at top level
                // Mark it as captured in the defining function
                if (!_functionCapturedLocals.TryGetValue(definingFunc, out var capturedLocals))
                {
                    capturedLocals = [];
                    _functionCapturedLocals[definingFunc] = capturedLocals;
                }
                capturedLocals.Add(name);

                // Record the defining scope for this capture. Within a single
                // function's analysis the scope stack is fixed, so repeated
                // references resolve to the same defining function.
                if (!_captureSources.TryGetValue(_currentFunction, out var sources))
                {
                    sources = [];
                    _captureSources[_currentFunction] = sources;
                }
                sources[name] = definingFunc;

                PropagateCaptureUpAsyncArrowChain(name, definingFunc);
            }
            else if (_functionStack.Count == 1 && IsNearestDeclaringScopeNested(name))
            {
                // Top-level capture (no enclosing function defines the name) whose
                // nearest declaring scope is a nested top-level block, referenced by
                // a closure created directly in top-level code (#1222) — see
                // _directTopLevelBlockCaptures.
                if (!_directTopLevelBlockCaptures.TryGetValue(_currentFunction, out var blockCaps))
                {
                    blockCaps = [];
                    _directTopLevelBlockCaptures[_currentFunction] = blockCaps;
                }
                blockCaps.Add(name);
            }
        }
    }

    /// <summary>
    /// True when the innermost scope on the stack declaring <paramref name="name"/>
    /// is NOT the root (module) scope. Only meaningful for names with no defining
    /// function (see <see cref="FindDefiningFunction"/>): for those, every scope
    /// declaring the name lies in the top-level region, so a non-root hit means a
    /// top-level block binding. <c>Stack&lt;T&gt;</c> enumerates LIFO, so the last
    /// scope enumerated is the root pushed by <see cref="Analyze"/>.
    /// </summary>
    private bool IsNearestDeclaringScopeNested(string name)
    {
        int index = 0;
        int count = _scopeStack.Count;
        foreach (var scope in _scopeStack)
        {
            index++;
            if (scope.Contains(name))
                return index < count;
        }
        return false;
    }

    /// <summary>
    /// A STANDALONE async arrow (one not nested in an async <em>function</em>) copies each captured
    /// variable BY VALUE into its own state machine — there is no shared display class to relay it,
    /// unlike sync arrows. So an INTERMEDIATE async arrow that does not itself reference
    /// <paramref name="name"/> must still capture-and-forward it for a deeper nested arrow that does,
    /// or the deeper arrow's capture array reads it as null (#716). This mirrors the up-the-chain
    /// propagation <see cref="VisitThis"/> already does for <c>this</c>, but unions
    /// <paramref name="name"/> onto enclosing ASYNC arrow frames only — sync arrows relay captures
    /// through shared scope display classes and rely on per-arrow capture sets staying innermost-only,
    /// so adding to them would perturb that machinery. Stops at the variable's defining scope (its
    /// owner, which already has it as a local) and at the first non-async-arrow boundary (a sync arrow
    /// or any function declaration), beyond which a different relay mechanism applies. Only invoked for
    /// function-local captures (<paramref name="definingFunc"/> non-null); top-level variables reach an
    /// arrow through the entry-point display class, not capture forwarding, so they are not propagated.
    /// </summary>
    private void PropagateCaptureUpAsyncArrowChain(string name, object definingFunc)
    {
        // _functionStack enumerates innermost-first; the top is _currentFunction (already recorded).
        bool passedCurrent = false;
        foreach (var frame in _functionStack)
        {
            if (!passedCurrent)
            {
                if (ReferenceEquals(frame, _currentFunction)) passedCurrent = true;
                continue;
            }
            if (ReferenceEquals(frame, definingFunc))
                break; // reached the owning scope — it holds `name` as a local
            if (frame is not Expr.ArrowFunction { IsAsync: true } intermediateArrow)
                break; // sync arrow / function boundary — different relay mechanism applies
            if (_captures.TryGetValue(intermediateArrow, out var caps) && caps.Add(name))
                _allCapturedVariables.Add(name);
        }
    }

    /// <summary>
    /// Finds the function that defines a given variable by checking _localVars
    /// for functions in the stack. Returns null if the variable is top-level.
    /// Note: O(depth) where depth is nesting level, typically small (5-10).
    /// </summary>
    /// <remarks>
    /// <c>Stack&lt;T&gt;</c> enumerates LIFO (most-recently-pushed first), so a plain
    /// <c>foreach</c> already visits the innermost function first — the nearest
    /// declaring scope wins. That's what we want. The minimal reproducer that
    /// surfaced this code path had two sibling module-level functions with the
    /// same parameter name and inner closures capturing it; each function is
    /// analyzed with its own fresh stack, so sibling collisions don't occur
    /// through this path.
    /// </remarks>
    private object? FindDefiningFunction(string name)
    {
        foreach (var func in _functionStack)
        {
            if (func != null && _localVars.TryGetValue(func, out var locals) && locals.Contains(name))
            {
                return func;
            }
        }
        return null;
    }

    #endregion

    #region Statement visitors

    protected override void VisitVar(Stmt.Var stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        base.VisitConst(stmt);
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        // Skip overload signatures (no body)
        if (stmt.Body != null)
            AnalyzeFunctionBody(stmt, stmt.Parameters, stmt.Body);
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        DeclareVariable(stmt.Name.Lexeme);
        foreach (var method in stmt.Methods)
        {
            // Skip overload signatures (no body)
            if (method.Body != null)
                AnalyzeFunctionBody(method, method.Parameters, method.Body);
        }
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        // Class expressions don't declare the class name in the outer scope
        // (unlike class declarations), but we still need to analyze all bodies

        // Analyze field initializers for captured variables
        foreach (var field in expr.Fields)
        {
            if (field.Initializer != null)
                Visit(field.Initializer);
        }

        // Analyze methods
        foreach (var method in expr.Methods)
        {
            // Skip overload signatures (no body)
            if (method.Body != null)
                AnalyzeFunctionBody(method, method.Parameters, method.Body);
        }

        // Analyze accessors
        if (expr.Accessors != null)
        {
            foreach (var accessor in expr.Accessors)
            {
                var parameters = accessor.SetterParam != null
                    ? [accessor.SetterParam]
                    : new List<Stmt.Parameter>();
                AnalyzeFunctionBody(accessor, parameters, accessor.Body);
            }
        }
    }

    protected override void VisitBlock(Stmt.Block stmt)
    {
        EnterScope();
        HoistFunctionDeclarations(stmt.Statements);
        base.VisitBlock(stmt);
        ExitScope();
    }

    // Sequence intentionally uses base implementation (no new scope)

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        // Visit iterable BEFORE creating loop scope - matches interpreter/resolver behavior
        Visit(stmt.Iterable);
        EnterScope();
        DeclareVariable(stmt.Variable.Lexeme);
        Visit(stmt.Body);
        ExitScope();
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        // Visit object BEFORE creating loop scope - matches interpreter/resolver behavior
        Visit(stmt.Object);
        EnterScope();
        DeclareVariable(stmt.Variable.Lexeme);
        Visit(stmt.Body);
        ExitScope();
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // For loops create a scope for the loop variable (e.g., let i in "for (let i = 0; ...)")
        EnterScope();
        if (stmt.Initializer != null)
        {
            // Record `for (let/const …)` loop bindings for the enclosing function so the
            // per-iteration exclusion can keep them out of the shared function display
            // class (#649). Parked in _pendingLoopBindings across the initializer visit
            // so DeclareVariable classifies them as loop bindings, not ordinary locals.
            var loopNames = CollectLoopBindingNames(stmt.Initializer);
            if (loopNames != null && _currentFunction != null)
            {
                if (!_functionLoopBindings.TryGetValue(_currentFunction, out var bindings))
                    _functionLoopBindings[_currentFunction] = bindings = [];
                foreach (var n in loopNames) { bindings.Add(n); _pendingLoopBindings.Add(n); }
                Visit(stmt.Initializer);
                foreach (var n in loopNames) _pendingLoopBindings.Remove(n);
            }
            else
            {
                Visit(stmt.Initializer);
            }
        }
        if (stmt.Condition != null)
            Visit(stmt.Condition);
        if (stmt.Increment != null)
            Visit(stmt.Increment);
        Visit(stmt.Body);
        ExitScope();
    }

    /// <summary>
    /// Returns the names a for-initializer binds per iteration (ECMA-262 13.7.4):
    /// <c>let</c>/<c>const</c> declarations. Returns <c>null</c> for <c>var</c> or
    /// expression initializers, which share one binding across the whole loop.
    /// Mirrors <c>Interpreter.CollectPerIterationBindings</c>.
    /// </summary>
    private static List<string>? CollectLoopBindingNames(Stmt? initializer)
    {
        switch (initializer)
        {
            case Stmt.Var v when !v.IsVar:
                return [v.Name.Lexeme];
            case Stmt.Const c:
                return [c.Name.Lexeme];
            case Stmt.Sequence seq:
                List<string>? names = null;
                foreach (var s in seq.Statements)
                {
                    var sub = CollectLoopBindingNames(s);
                    if (sub != null) (names ??= []).AddRange(sub);
                }
                return names;
            default:
                return null;
        }
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        foreach (var s in stmt.TryBlock)
            Visit(s);

        if (stmt.CatchBlock != null)
        {
            EnterScope();
            if (stmt.CatchParam != null)
                DeclareVariable(stmt.CatchParam.Lexeme);
            foreach (var s in stmt.CatchBlock)
                Visit(s);
            ExitScope();
        }

        if (stmt.FinallyBlock != null)
            foreach (var s in stmt.FinallyBlock)
                Visit(s);
    }

    #endregion

    #region Expression visitors

    protected override void VisitVariable(Expr.Variable expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        ReferenceVariable(expr.Name.Lexeme);
        base.VisitLogicalAssign(expr);
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        AnalyzeArrowFunctionBody(expr);
    }

    protected override void VisitNew(Expr.New expr)
    {
        // Base AstVisitorBase.VisitNew visits only Arguments, not Callee —
        // asymmetric with VisitCall which visits both. That gap meant
        // `new X()` where X is an outer variable (e.g. a peer hoisted
        // function) never registered X as a reference, so the enclosing
        // function's display class didn't receive X as a captured field,
        // and the compiled body fell back to a runtime global-variable
        // lookup (which throws ReferenceError or silently returns null).
        // See issue #59.
        Visit(expr.Callee);
        foreach (var arg in expr.Arguments)
            Visit(arg);
    }

    protected override void VisitThis(Expr.This expr)
    {
        // Arrow functions capture 'this' from their lexical scope. Also propagate the
        // capture up the arrow chain — every enclosing arrow (up to the first non-arrow
        // or `function(){}`-expression boundary) needs `this` in its display class so
        // the inner arrow can read it via the outer DC field during construction. Without
        // this, the arrow-construction emitter fell through to bare `Ldarg_0` in arrow-body
        // context (which is the outer DC, not the class's `this`), causing NREs or
        // InvalidCastExceptions when the captured `this` was then used.
        if (_currentFunction == null) return;

        foreach (var frame in _functionStack)
        {
            if (frame is not Expr.ArrowFunction arrow)
                break; // hit a non-arrow scope — `this` is bound there, stop propagating
            if (arrow.HasOwnThis)
                break; // function-expression arrow with explicit __this — stop propagating

            _captures[arrow].Add("this");
            _allCapturedVariables.Add("this"); // O(1) inverse index
        }
    }

    #endregion

    #region Function/Arrow body analysis

    private void AnalyzeFunctionBody(object funcNode, List<Stmt.Parameter> parameters, List<Stmt> body)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
            foreach (var name in scope)
                _outerVariables.Add(name);

        // Set up new function context
        _currentFunction = funcNode;
        _captures[funcNode] = [];
        _localVars[funcNode] = [];

        // Push this function onto the function stack for capture tracking
        _functionStack.Push(funcNode);

        // Enter function scope and declare parameters
        EnterScope();
        foreach (var param in parameters)
        {
            DeclareVariable(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }

        // Pre-declare `arguments` as a synthetic local if the body (including nested
        // arrows, but not nested non-arrow functions) references it. Without this, a
        // nested arrow reading `arguments` would resolve against the (non-existent)
        // outer scope and fail at IL emission; declaring it here routes the reference
        // through the normal captured-local machinery so arrows get it via the display
        // class (#64). Non-arrow nested functions declare their own `arguments` and
        // shadow this one — matching JS spec.
        if (ReferencesArgumentsIdentifierNonArrow(body))
        {
            DeclareVariable("arguments");
        }

        // Hoist sibling function declarations so forward references in one sibling's
        // body resolve to another sibling declared later in source order.
        HoistFunctionDeclarations(body);

        // Analyze body
        foreach (var stmt in body)
            Visit(stmt);

        ExitScope();

        // Pop from function stack
        _functionStack.Pop();

        // Restore context
        _currentFunction = previousFunction;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
            _outerVariables.Add(name);
    }

    private void AnalyzeArrowFunctionBody(Expr.ArrowFunction af)
    {
        // Save current context
        var previousFunction = _currentFunction;
        var previousOuter = new HashSet<string>(_outerVariables);
        var previousFunctionName = _currentFunctionName;

        // Build set of outer variables for this function
        _outerVariables.Clear();
        foreach (var scope in _scopeStack)
            foreach (var name in scope)
                _outerVariables.Add(name);

        // Set up new function context
        _currentFunction = af;
        _currentFunctionName = af.Name?.Lexeme;
        _captures[af] = [];
        _localVars[af] = [];

        // Push this arrow function onto the function stack for capture tracking
        _functionStack.Push(af);

        // Enter function scope
        EnterScope();

        // For named function expressions, declare the name as a local variable
        // so that it doesn't get captured from outer scopes.
        // Self-references will be detected by ReferenceVariable checking _currentFunctionName.
        if (af.Name != null)
        {
            DeclareVariable(af.Name.Lexeme);
        }

        // Declare parameters (may shadow function name if same identifier)
        foreach (var param in af.Parameters)
        {
            DeclareVariable(param.Name.Lexeme);
            if (param.DefaultValue != null)
                Visit(param.DefaultValue);
        }

        // Analyze body
        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        else if (af.BlockBody != null)
        {
            HoistFunctionDeclarations(af.BlockBody);
            foreach (var stmt in af.BlockBody)
                Visit(stmt);
        }

        ExitScope();

        // Pop from function stack
        _functionStack.Pop();

        // Restore context
        _currentFunction = previousFunction;
        _currentFunctionName = previousFunctionName;
        _outerVariables.Clear();
        foreach (var name in previousOuter)
            _outerVariables.Add(name);
    }

    #endregion

    /// <summary>
    /// Returns true if any statement (directly or inside a nested arrow) references
    /// the identifier <c>arguments</c>, stopping at nested non-arrow function boundaries
    /// (those have their own <c>arguments</c> binding per JS spec, so a reference inside
    /// them belongs to the nested function, not this one).
    /// </summary>
    private static bool ReferencesArgumentsIdentifierNonArrow(List<Stmt> stmts)
    {
        var scanner = new ArgumentsRefScanner();
        foreach (var s in stmts)
        {
            scanner.Visit(s);
            if (scanner.Found) return true;
        }
        return false;
    }

    private sealed class ArgumentsRefScanner : AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitVariable(Expr.Variable expr)
        {
            if (expr.Name.Lexeme == "arguments")
            {
                Found = true;
                ShouldContinue = false;
            }
        }

        // Nested non-arrow function declarations/expressions introduce their own
        // `arguments` binding — references inside belong to that inner function,
        // so stop descending.
        protected override void VisitFunction(Stmt.Function stmt) { /* skip */ }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            // Function expressions (HasOwnThis=true) behave like declarations: their
            // own `arguments` shadows ours. True arrow functions (HasOwnThis=false)
            // inherit lexically, so we must recurse.
            if (expr.HasOwnThis) return;
            base.VisitArrowFunction(expr);
        }
    }
}
