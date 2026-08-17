namespace SharpTS.Parsing;

/// <summary>
/// Lifts generator function EXPRESSIONS (represented as <see cref="Expr.ArrowFunction"/> with
/// <c>IsGenerator = true</c>) into <see cref="Stmt.Function"/> declarations.
///
/// The IL compiler has a mature generator-declaration pipeline (<c>GeneratorStateMachineBuilder</c>
/// + <c>GeneratorMoveNextEmitter</c>) but no arrow-expression counterpart. Rather than duplicate
/// that infrastructure for the relatively rare generator-expression case, this pass rewrites
/// each occurrence so the existing declaration path handles it. Example:
/// <code>
/// let limit = 3;
/// Yallist.prototype[Symbol.iterator] = function* () {
///     for (let w = this.head; w; w = w.next) yield w.value;
/// }
/// </code>
/// becomes:
/// <code>
/// let limit = 3;
/// Yallist.prototype[Symbol.iterator] = __genArrow_0;
/// function __genArrow_0() {
///     for (let w = this.head; w; w = w.next) yield w.value;
/// }
/// </code>
///
/// <para><b>Lift target.</b> A generator expression that does NOT close over an enclosing function's
/// locals is lifted to the END of the module body. Function declarations hoist, so the trailing
/// position is runtime-equivalent in both the interpreter and the compiler, and it lets the
/// source-order type checker see any module-level <c>let</c>/<c>const</c> the body closes over before
/// that body is checked (#522). A generator expression that DOES close over an enclosing function's
/// local is instead lifted to the end of that enclosing function's body, so the lifted declaration
/// keeps the local in lexical scope (#534). The interpreter runs such nested generator declarations
/// natively. On the compile path the <see cref="SharpTS.Compilation.NestedFunctionLifter"/> lambda-lifts
/// it (the captured local becomes a leading parameter forwarded by an arrow); because this lift is
/// appended at the body END, the forwarding binding is hoisted to the body top so the earlier reference
/// resolves — so a generator expression closing over a PLAIN enclosing function's local now runs in both
/// modes (#534). Cases the lambda-lift cannot forward (a body using <c>this</c>/<c>arguments</c>,
/// rest/default params, self-recursion, or an enclosing function that is itself a state machine) stay
/// nested and fail cleanly with a "Yield not supported in this context" compile error, never a
/// miscompile.</para>
///
/// <para><b>Traversal.</b> The rewriter descends through every expression- and statement-bearing AST
/// position so a generator expression is found wherever it is legal to write one — call/IIFE position
/// (#488), <c>for</c>/<c>for-of</c>/<c>for-in</c>/<c>do-while</c>/<c>try</c>/<c>switch</c>/labeled
/// statement bodies (#634), ternaries, array/object literals, etc.</para>
///
/// The rewriter returns the original AST node whenever no change was required, which keeps
/// downstream passes (e.g. the type checker) from seeing fresh reference identities for untouched
/// subtrees.
/// </summary>
internal sealed class GeneratorArrowLifter
{
    /// <summary>Generator expressions that close over no enclosing-function local — appended to the module body.</summary>
    private readonly List<Stmt.Function> _moduleLifted = new();

    // Source positions of the AST being rewritten, when the caller is tracking them.
    private SpanTable? _spans;
    private int _counter;

    /// <summary>
    /// Stack of enclosing function/arrow scopes (innermost last). Each frame records the local
    /// binding names visible in that scope and collects generator declarations that must be lifted
    /// into it (because they close over one of those locals).
    /// </summary>
    private readonly List<FunctionFrame> _frames = new();

    private sealed class FunctionFrame
    {
        public required HashSet<string> Locals { get; init; }
        public List<Stmt.Function> Lifted { get; } = new();
    }

    /// <summary>
    /// Stack of block-scoped binding-name sets currently in scope along the traversal path: loop
    /// variables, catch parameters, and <c>let</c>/<c>const</c>/<c>class</c>/block-level <c>function</c>
    /// declarations inside a nested block, switch body, or try/catch/finally block. A generator function
    /// expression that closes over one of these CANNOT be lifted out — the module body and every
    /// enclosing function body sit outside the block where the binding lives, so the lift would unbind
    /// the reference. Such a generator is left in place as an expression instead (#678): the interpreter
    /// runs generator expressions natively (<c>SharpTSArrowGeneratorFunction</c>) and the type checker
    /// establishes the generator context directly (<c>CheckArrowFunction</c>). The compiler has no
    /// generator-expression IL path and cannot lift this out of its block, so it reports a clear
    /// "Yield not supported in this context" error — unlike a capture of an enclosing FUNCTION local,
    /// which is lambda-lifted and runs in both modes (#534).
    /// </summary>
    private readonly List<HashSet<string>> _blockScopes = new();

    /// <summary>Shared read-only sentinel for blocks that introduce no block-scoped bindings (avoids a
    /// per-block allocation; never mutated after creation).</summary>
    private static readonly HashSet<string> EmptyScope = new(StringComparer.Ordinal);

    private void PushBlockScope(HashSet<string> names) => _blockScopes.Add(names);
    private void PopBlockScope() => _blockScopes.RemoveAt(_blockScopes.Count - 1);

    private static HashSet<string> SingletonScope(string name) => new(StringComparer.Ordinal) { name };

    /// <summary>
    /// Collects the block-scoped binding names declared directly in a statement list (a block body):
    /// <c>let</c>/<c>const</c>/<c>class</c> and block-level <c>function</c> declarations. <c>var</c> is
    /// excluded (function-scoped, and already hoisted to the function top by the parse-time
    /// <c>VarHoister</c>), as are bindings nested in deeper blocks. Returns the shared empty sentinel
    /// when the block declares nothing block-scoped.
    /// </summary>
    private static HashSet<string> CollectBlockBindings(List<Stmt> statements)
    {
        HashSet<string>? names = null;
        foreach (var stmt in statements)
        {
            string? n = stmt switch
            {
                Stmt.Var v when !v.IsVar => v.Name.Lexeme,   // `let` (var is function-scoped + pre-hoisted)
                Stmt.Const c => c.Name.Lexeme,
                Stmt.Function f => f.Name.Lexeme,
                Stmt.Class cl => cl.Name.Lexeme,
                _ => null,
            };
            if (n is not null) (names ??= new HashSet<string>(StringComparer.Ordinal)).Add(n);
        }
        return names ?? EmptyScope;
    }

    /// <summary>Collects the block-scoped names declared by a <c>for(;;)</c> initializer (the loop
    /// variables of <c>for (let i = 0, j = 0; …)</c>). A <c>var</c> initializer is function-scoped, so
    /// it contributes nothing.</summary>
    private static HashSet<string> CollectForInitBindings(Stmt? init) => init switch
    {
        Stmt.Sequence seq => CollectBlockBindings(seq.Statements),
        Stmt.Var v when !v.IsVar => SingletonScope(v.Name.Lexeme),
        Stmt.Const c => SingletonScope(c.Name.Lexeme),
        _ => EmptyScope,
    };

    /// <summary>The body of a <c>switch</c> is a single lexical scope shared by every case, so its
    /// block-scoped bindings are the union of all case and default body declarations.</summary>
    private static HashSet<string> CollectSwitchBindings(Stmt.Switch sw)
    {
        HashSet<string>? names = null;
        void Merge(HashSet<string> from)
        {
            if (from.Count == 0) return;
            (names ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(from);
        }
        foreach (var c in sw.Cases) Merge(CollectBlockBindings(c.Body));
        if (sw.DefaultBody is not null) Merge(CollectBlockBindings(sw.DefaultBody));
        return names ?? EmptyScope;
    }

    /// <summary>True when the generator arrow references a free variable bound in an enclosing BLOCK
    /// scope (loop variable, catch parameter, or nested-block let/const/class/function). Lifting it out
    /// would move the reference outside that block, so the generator must stay an expression (#678).</summary>
    private bool CapturesBlockScopedBinding(HashSet<string> freeVars)
    {
        if (freeVars.Count == 0) return false;
        foreach (var scope in _blockScopes)
            foreach (var name in freeVars)
                if (scope.Contains(name)) return true;
        return false;
    }

    /// <param name="spans">
    /// When supplied, statements rebuilt around a lifted generator inherit the position of the
    /// statements they replace, and the synthesized top-level function declarations are marked
    /// compiler-generated.
    /// </param>
    public static List<Stmt> Lift(List<Stmt> body, SpanTable? spans = null)
    {
        // Cheap pre-scan: if there are no generator function expressions anywhere, return the
        // input unchanged. This avoids walking the whole AST for every module — almost no
        // real-world CJS module has one. Uses object identity so we can return the input list
        // unchanged when no lifting is needed.
        if (!ContainsGeneratorArrow(body)) return body;

        var lifter = new GeneratorArrowLifter { _spans = spans };
        var rewritten = new List<Stmt>(body.Count);
        foreach (var stmt in body)
        {
            rewritten.Add(lifter.RewriteStmt(stmt));
        }
        // `rewritten` already carries any frame-local lifts (injected into the rewritten function
        // bodies); only the module-scope lifts still need to be appended here.
        if (lifter._moduleLifted.Count == 0) return rewritten;

        // Append (not prepend) the module-scope lifts: function declarations hoist, so the trailing
        // position is runtime-equivalent, and it lets the source-order type checker see any
        // module-level let/const the generator body closes over before that body is checked (#522).
        var result = new List<Stmt>(rewritten.Count + lifter._moduleLifted.Count);
        result.AddRange(rewritten);
        result.AddRange(lifter._moduleLifted);
        return result;
    }

    private static bool ContainsGeneratorArrow(List<Stmt> body)
    {
        var scanner = new GeneratorArrowScanner();
        foreach (var stmt in body)
        {
            scanner.Visit(stmt);
            if (scanner.Found) return true;
        }
        return false;
    }

    private sealed class GeneratorArrowScanner : Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            if (expr.IsGenerator)
            {
                Found = true;
                ShouldContinue = false;
                return;
            }
            base.VisitArrowFunction(expr);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Statement rewriting
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Rewrites a statement, carrying its source position onto whatever replaces it.
    /// </summary>
    /// <remarks>
    /// Lifting a generator arrow rebuilds every statement enclosing it. Those rebuilt statements are
    /// still the user's code, so provenance is copied here — the one place all of them pass through
    /// — rather than at each of the two dozen productions below.
    /// </remarks>
    private Stmt RewriteStmt(Stmt stmt)
    {
        Stmt rewritten = RewriteStmtCore(stmt);
        _spans?.CopySpan(stmt, rewritten);
        return rewritten;
    }

    private Stmt RewriteStmtCore(Stmt stmt)
    {
        return stmt switch
        {
            Stmt.Expression e => ReplaceIfChanged(e, e.Expr, RewriteExpr(e.Expr), (_, x) => new Stmt.Expression(x)),
            Stmt.Return r => r.Value is null ? r : ReplaceIfChanged(r, r.Value, RewriteExpr(r.Value), (s, x) => new Stmt.Return(s.Keyword, x)),
            Stmt.Var v => v.Initializer is null ? v : ReplaceIfChanged(v, v.Initializer, RewriteExpr(v.Initializer), (s, x) => s with { Initializer = x }),
            Stmt.Const c => ReplaceIfChanged(c, c.Initializer, RewriteExpr(c.Initializer), (s, x) => s with { Initializer = x }),
            Stmt.Throw t => ReplaceIfChanged(t, t.Value, RewriteExpr(t.Value), (s, x) => new Stmt.Throw(s.Keyword, x)),
            Stmt.Block b => RewriteBlock(b),
            Stmt.Sequence sq => RewriteSequence(sq),
            Stmt.If i => RewriteIf(i),
            Stmt.While w => RewriteWhile(w),
            Stmt.DoWhile d => RewriteDoWhile(d),
            Stmt.For f => RewriteFor(f),
            Stmt.ForOf fo => RewriteForOf(fo),
            Stmt.ForIn fi => RewriteForIn(fi),
            Stmt.LabeledStatement ls => RewriteLabeled(ls),
            Stmt.Switch sw => RewriteSwitch(sw),
            Stmt.TryCatch tc => RewriteTryCatch(tc),
            Stmt.Function fn => RewriteFunction(fn),
            Stmt.Class cls => RewriteClass(cls),
            Stmt.Namespace ns => RewriteNamespace(ns),
            Stmt.Export ex => RewriteExport(ex),
            _ => stmt,
        };
    }

    private static Stmt ReplaceIfChanged<T>(T original, Expr originalChild, Expr newChild, Func<T, Expr, Stmt> factory)
        where T : Stmt
        => ReferenceEquals(originalChild, newChild) ? original : factory(original, newChild);

    private Stmt RewriteBlock(Stmt.Block b)
    {
        PushBlockScope(CollectBlockBindings(b.Statements));
        try
        {
            var rewritten = RewriteListIfChanged(b.Statements, RewriteStmt);
            return ReferenceEquals(rewritten, b.Statements) ? b : new Stmt.Block(rewritten);
        }
        finally { PopBlockScope(); }
    }

    private Stmt RewriteSequence(Stmt.Sequence sq)
    {
        var rewritten = RewriteListIfChanged(sq.Statements, RewriteStmt);
        return ReferenceEquals(rewritten, sq.Statements) ? sq : new Stmt.Sequence(rewritten);
    }

    private Stmt RewriteIf(Stmt.If i)
    {
        var newCond = RewriteExpr(i.Condition);
        var newThen = RewriteStmt(i.ThenBranch);
        var newElse = i.ElseBranch is null ? null : RewriteStmt(i.ElseBranch);
        if (ReferenceEquals(newCond, i.Condition)
            && ReferenceEquals(newThen, i.ThenBranch)
            && (i.ElseBranch is null || ReferenceEquals(newElse, i.ElseBranch)))
            return i;
        return new Stmt.If(newCond, newThen, newElse);
    }

    private Stmt RewriteWhile(Stmt.While w)
    {
        var newCond = RewriteExpr(w.Condition);
        var newBody = RewriteStmt(w.Body);
        if (ReferenceEquals(newCond, w.Condition) && ReferenceEquals(newBody, w.Body)) return w;
        return new Stmt.While(newCond, newBody);
    }

    private Stmt RewriteDoWhile(Stmt.DoWhile d)
    {
        var newBody = RewriteStmt(d.Body);
        var newCond = RewriteExpr(d.Condition);
        if (ReferenceEquals(newBody, d.Body) && ReferenceEquals(newCond, d.Condition)) return d;
        return new Stmt.DoWhile(newBody, newCond);
    }

    private Stmt RewriteFor(Stmt.For f)
    {
        // The initializer is rewritten BEFORE the loop variables enter scope (a `for (let i = …)`
        // initializer is in i's TDZ); the condition, increment, and body see them as block-scoped.
        var newInit = f.Initializer is null ? null : RewriteStmt(f.Initializer);
        Expr? newCond;
        Expr? newIncr;
        Stmt newBody;
        PushBlockScope(CollectForInitBindings(f.Initializer));
        try
        {
            newCond = f.Condition is null ? null : RewriteExpr(f.Condition);
            newIncr = f.Increment is null ? null : RewriteExpr(f.Increment);
            newBody = RewriteStmt(f.Body);
        }
        finally { PopBlockScope(); }
        if ((f.Initializer is null || ReferenceEquals(newInit, f.Initializer))
            && (f.Condition is null || ReferenceEquals(newCond, f.Condition))
            && (f.Increment is null || ReferenceEquals(newIncr, f.Increment))
            && ReferenceEquals(newBody, f.Body))
            return f;
        return new Stmt.For(newInit, newCond, newIncr, newBody);
    }

    private Stmt RewriteForOf(Stmt.ForOf fo)
    {
        // The iterable is evaluated before the loop variable is bound, so it is rewritten outside the
        // loop-variable scope; the body sees the loop variable as block-scoped (#678).
        var newIter = RewriteExpr(fo.Iterable);
        Stmt newBody;
        PushBlockScope(SingletonScope(fo.Variable.Lexeme));
        try { newBody = RewriteStmt(fo.Body); }
        finally { PopBlockScope(); }
        if (ReferenceEquals(newIter, fo.Iterable) && ReferenceEquals(newBody, fo.Body)) return fo;
        return fo with { Iterable = newIter, Body = newBody };
    }

    private Stmt RewriteForIn(Stmt.ForIn fi)
    {
        var newObj = RewriteExpr(fi.Object);
        Stmt newBody;
        PushBlockScope(SingletonScope(fi.Variable.Lexeme));
        try { newBody = RewriteStmt(fi.Body); }
        finally { PopBlockScope(); }
        if (ReferenceEquals(newObj, fi.Object) && ReferenceEquals(newBody, fi.Body)) return fi;
        return fi with { Object = newObj, Body = newBody };
    }

    private Stmt RewriteLabeled(Stmt.LabeledStatement ls)
    {
        var newInner = RewriteStmt(ls.Statement);
        return ReferenceEquals(newInner, ls.Statement) ? ls : new Stmt.LabeledStatement(ls.Label, newInner);
    }

    private Stmt RewriteSwitch(Stmt.Switch sw)
    {
        // The subject is evaluated outside the switch block; every case shares one lexical scope (#678).
        var newSubject = RewriteExpr(sw.Subject);
        List<Stmt.SwitchCase>? newCases = null;
        List<Stmt>? newDefault;
        PushBlockScope(CollectSwitchBindings(sw));
        try
        {
            for (int i = 0; i < sw.Cases.Count; i++)
            {
                var c = sw.Cases[i];
                var newValue = RewriteExpr(c.Value);
                var newBody = RewriteListIfChanged(c.Body, RewriteStmt);
                if (!ReferenceEquals(newValue, c.Value) || !ReferenceEquals(newBody, c.Body))
                {
                    newCases ??= new List<Stmt.SwitchCase>(sw.Cases);
                    newCases[i] = new Stmt.SwitchCase(newValue, newBody);
                }
            }
            newDefault = sw.DefaultBody is null ? null : RewriteListIfChanged(sw.DefaultBody, RewriteStmt);
        }
        finally { PopBlockScope(); }
        if (ReferenceEquals(newSubject, sw.Subject) && newCases is null
            && (sw.DefaultBody is null || ReferenceEquals(newDefault, sw.DefaultBody)))
            return sw;
        return new Stmt.Switch(newSubject, newCases ?? sw.Cases, newDefault);
    }

    private Stmt RewriteTryCatch(Stmt.TryCatch tc)
    {
        // The try/catch/finally blocks are each their own lexical scope; the catch block additionally
        // binds the catch parameter (#678).
        var newTry = RewriteBlockScoped(tc.TryBlock);

        List<Stmt>? newCatch;
        if (tc.CatchBlock is null)
        {
            newCatch = null;
        }
        else
        {
            var catchBindings = CollectBlockBindings(tc.CatchBlock);
            HashSet<string> catchScope =
                tc.CatchParam is null ? catchBindings
                : catchBindings.Count == 0 ? SingletonScope(tc.CatchParam.Lexeme)
                : new HashSet<string>(catchBindings, StringComparer.Ordinal) { tc.CatchParam.Lexeme };
            PushBlockScope(catchScope);
            try { newCatch = RewriteListIfChanged(tc.CatchBlock, RewriteStmt); }
            finally { PopBlockScope(); }
        }

        var newFinally = tc.FinallyBlock is null ? null : RewriteBlockScoped(tc.FinallyBlock);
        if (ReferenceEquals(newTry, tc.TryBlock)
            && (tc.CatchBlock is null || ReferenceEquals(newCatch, tc.CatchBlock))
            && (tc.FinallyBlock is null || ReferenceEquals(newFinally, tc.FinallyBlock)))
            return tc;
        return tc with { TryBlock = newTry, CatchBlock = newCatch, FinallyBlock = newFinally };
    }

    /// <summary>Rewrites a statement list that forms its own block scope (a try/catch/finally block),
    /// pushing the block's block-scoped bindings for the #678 capture analysis.</summary>
    private List<Stmt> RewriteBlockScoped(List<Stmt> statements)
    {
        PushBlockScope(CollectBlockBindings(statements));
        try { return RewriteListIfChanged(statements, RewriteStmt); }
        finally { PopBlockScope(); }
    }

    private Stmt RewriteFunction(Stmt.Function f)
    {
        if (f.Body is null) return f;
        var newParams = RewriteParameters(f.Parameters);
        var rewrittenBody = RewriteFunctionBody(f.Parameters, f.Body);
        if (ReferenceEquals(newParams, f.Parameters) && ReferenceEquals(rewrittenBody, f.Body)) return f;
        return f with { Parameters = newParams, Body = rewrittenBody };
    }

    private Stmt RewriteClass(Stmt.Class cls)
    {
        var newSuperclass = cls.SuperclassExpr is null ? null : RewriteExpr(cls.SuperclassExpr);
        var newMethods = RewriteListIfChanged(cls.Methods, RewriteClassMethod);
        var newFields = RewriteListIfChanged(cls.Fields, RewriteClassField);
        var newAccessors = cls.Accessors is null
            ? null
            : RewriteListIfChanged(cls.Accessors, RewriteClassAccessor);
        var newAutoAccessors = cls.AutoAccessors is null
            ? null
            : RewriteListIfChanged(cls.AutoAccessors, RewriteAutoAccessor);

        // StaticInitializers repeats the static Field nodes held in Fields. Reuse the
        // corresponding rewritten node so a generator in a static field initializer is
        // lifted exactly once, while still descending into static blocks.
        List<Stmt>? newStaticInitializers = cls.StaticInitializers;
        if (cls.StaticInitializers is not null)
        {
            var rewrittenFields = new Dictionary<Stmt.Field, Stmt.Field>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < cls.Fields.Count; i++)
                rewrittenFields[cls.Fields[i]] = newFields[i];

            newStaticInitializers = RewriteListIfChanged(cls.StaticInitializers, initializer =>
                initializer is Stmt.Field field && rewrittenFields.TryGetValue(field, out var rewrittenField)
                    ? rewrittenField
                    : initializer is Stmt.StaticBlock block
                        ? RewriteStaticBlock(block)
                        : RewriteStmt(initializer));
        }

        if ((cls.SuperclassExpr is null || ReferenceEquals(newSuperclass, cls.SuperclassExpr))
            && ReferenceEquals(newMethods, cls.Methods)
            && ReferenceEquals(newFields, cls.Fields)
            && (cls.Accessors is null || ReferenceEquals(newAccessors, cls.Accessors))
            && (cls.AutoAccessors is null || ReferenceEquals(newAutoAccessors, cls.AutoAccessors))
            && (cls.StaticInitializers is null || ReferenceEquals(newStaticInitializers, cls.StaticInitializers)))
            return cls;

        return cls with
        {
            SuperclassExpr = newSuperclass,
            Methods = newMethods,
            Fields = newFields,
            Accessors = newAccessors,
            AutoAccessors = newAutoAccessors,
            StaticInitializers = newStaticInitializers,
        };
    }

    private Stmt.Function RewriteClassMethod(Stmt.Function method)
    {
        // A computed method key is evaluated in the surrounding class-definition
        // scope, while defaults and the body use the method's function scope.
        var newKey = method.ComputedKey is null ? null : RewriteExpr(method.ComputedKey);
        var rewritten = (Stmt.Function)RewriteFunction(method);
        return method.ComputedKey is null || ReferenceEquals(newKey, method.ComputedKey)
            ? rewritten
            : rewritten with { ComputedKey = newKey };
    }

    private Stmt.Field RewriteClassField(Stmt.Field field)
    {
        var newKey = field.ComputedKey is null ? null : RewriteExpr(field.ComputedKey);
        var newInitializer = field.Initializer is null ? null : RewriteExpr(field.Initializer);
        return (field.ComputedKey is null || ReferenceEquals(newKey, field.ComputedKey))
            && (field.Initializer is null || ReferenceEquals(newInitializer, field.Initializer))
                ? field
                : field with { ComputedKey = newKey, Initializer = newInitializer };
    }

    private Stmt.Accessor RewriteClassAccessor(Stmt.Accessor accessor)
    {
        var newKey = accessor.ComputedKey is null ? null : RewriteExpr(accessor.ComputedKey);
        List<Stmt.Parameter> parameters = accessor.SetterParam is null ? [] : [accessor.SetterParam];
        var newParameters = RewriteParameters(parameters);
        var newBody = RewriteFunctionBody(parameters, accessor.Body);
        var newSetterParam = newParameters.Count == 0 ? null : newParameters[0];
        return (accessor.ComputedKey is null || ReferenceEquals(newKey, accessor.ComputedKey))
            && ReferenceEquals(newBody, accessor.Body)
            && ReferenceEquals(newSetterParam, accessor.SetterParam)
                ? accessor
                : accessor with { ComputedKey = newKey, SetterParam = newSetterParam, Body = newBody };
    }

    private Stmt.AutoAccessor RewriteAutoAccessor(Stmt.AutoAccessor accessor)
    {
        if (accessor.Initializer is null) return accessor;
        var newInitializer = RewriteExpr(accessor.Initializer);
        return ReferenceEquals(newInitializer, accessor.Initializer)
            ? accessor
            : accessor with { Initializer = newInitializer };
    }

    private Stmt.StaticBlock RewriteStaticBlock(Stmt.StaticBlock block)
    {
        var newBody = RewriteBlockScoped(block.Body);
        return ReferenceEquals(newBody, block.Body) ? block : block with { Body = newBody };
    }

    private Stmt RewriteNamespace(Stmt.Namespace ns)
    {
        var rewritten = RewriteListIfChanged(ns.Members, RewriteStmt);
        return ReferenceEquals(rewritten, ns.Members) ? ns : ns with { Members = rewritten };
    }

    private Stmt RewriteExport(Stmt.Export ex)
    {
        var newDecl = ex.Declaration is null ? null : RewriteStmt(ex.Declaration);
        var newDefault = ex.DefaultExpr is null ? null : RewriteExpr(ex.DefaultExpr);
        var newAssign = ex.ExportAssignment is null ? null : RewriteExpr(ex.ExportAssignment);
        if ((ex.Declaration is null || ReferenceEquals(newDecl, ex.Declaration))
            && (ex.DefaultExpr is null || ReferenceEquals(newDefault, ex.DefaultExpr))
            && (ex.ExportAssignment is null || ReferenceEquals(newAssign, ex.ExportAssignment)))
            return ex;
        return ex with { Declaration = newDecl, DefaultExpr = newDefault, ExportAssignment = newAssign };
    }

    // ---------------------------------------------------------------------------------------------
    // Expression rewriting
    // ---------------------------------------------------------------------------------------------

    private Expr RewriteExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af when af.IsGenerator && af.BlockBody is not null:
                return LiftGeneratorArrow(af);
            case Expr.ArrowFunction af:
                return RewriteNonGeneratorArrow(af);

            case Expr.Comma c:
            {
                var l = RewriteExpr(c.Left); var r = RewriteExpr(c.Right);
                return ReferenceEquals(l, c.Left) && ReferenceEquals(r, c.Right) ? c : new Expr.Comma(l, r);
            }
            case Expr.Binary b:
            {
                var l = RewriteExpr(b.Left); var r = RewriteExpr(b.Right);
                return ReferenceEquals(l, b.Left) && ReferenceEquals(r, b.Right) ? b : new Expr.Binary(l, b.Operator, r);
            }
            case Expr.Logical lg:
            {
                var l = RewriteExpr(lg.Left); var r = RewriteExpr(lg.Right);
                return ReferenceEquals(l, lg.Left) && ReferenceEquals(r, lg.Right) ? lg : new Expr.Logical(l, lg.Operator, r);
            }
            case Expr.NullishCoalescing nc:
            {
                var l = RewriteExpr(nc.Left); var r = RewriteExpr(nc.Right);
                return ReferenceEquals(l, nc.Left) && ReferenceEquals(r, nc.Right) ? nc : new Expr.NullishCoalescing(l, r);
            }
            case Expr.Ternary t:
            {
                var c = RewriteExpr(t.Condition); var th = RewriteExpr(t.ThenBranch); var el = RewriteExpr(t.ElseBranch);
                return ReferenceEquals(c, t.Condition) && ReferenceEquals(th, t.ThenBranch) && ReferenceEquals(el, t.ElseBranch)
                    ? t : new Expr.Ternary(c, th, el);
            }
            case Expr.Grouping g:
            {
                var inner = RewriteExpr(g.Expression);
                return ReferenceEquals(inner, g.Expression) ? g : new Expr.Grouping(inner);
            }
            case Expr.Unary u:
            {
                var r = RewriteExpr(u.Right);
                return ReferenceEquals(r, u.Right) ? u : new Expr.Unary(u.Operator, r);
            }
            case Expr.Delete d:
            {
                var o = RewriteExpr(d.Operand);
                return ReferenceEquals(o, d.Operand) ? d : new Expr.Delete(d.Keyword, o);
            }
            case Expr.Assign a:
            {
                var v = RewriteExpr(a.Value);
                return ReferenceEquals(v, a.Value) ? a : a with { Value = v };
            }
            case Expr.Set s:
            {
                var o = RewriteExpr(s.Object); var v = RewriteExpr(s.Value);
                return ReferenceEquals(o, s.Object) && ReferenceEquals(v, s.Value) ? s : new Expr.Set(o, s.Name, v);
            }
            case Expr.SetIndex si:
            {
                var o = RewriteExpr(si.Object); var idx = RewriteExpr(si.Index); var v = RewriteExpr(si.Value);
                return ReferenceEquals(o, si.Object) && ReferenceEquals(idx, si.Index) && ReferenceEquals(v, si.Value)
                    ? si : new Expr.SetIndex(o, idx, v);
            }
            case Expr.SetPrivate sp:
            {
                var o = RewriteExpr(sp.Object); var v = RewriteExpr(sp.Value);
                return ReferenceEquals(o, sp.Object) && ReferenceEquals(v, sp.Value) ? sp : new Expr.SetPrivate(o, sp.Name, v);
            }
            case Expr.CompoundAssign ca:
            {
                var v = RewriteExpr(ca.Value);
                return ReferenceEquals(v, ca.Value) ? ca : new Expr.CompoundAssign(ca.Name, ca.Operator, v);
            }
            case Expr.CompoundSet cs:
            {
                var o = RewriteExpr(cs.Object); var v = RewriteExpr(cs.Value);
                return ReferenceEquals(o, cs.Object) && ReferenceEquals(v, cs.Value) ? cs : new Expr.CompoundSet(o, cs.Name, cs.Operator, v);
            }
            case Expr.CompoundSetIndex csi:
            {
                var o = RewriteExpr(csi.Object); var idx = RewriteExpr(csi.Index); var v = RewriteExpr(csi.Value);
                return ReferenceEquals(o, csi.Object) && ReferenceEquals(idx, csi.Index) && ReferenceEquals(v, csi.Value)
                    ? csi : new Expr.CompoundSetIndex(o, idx, csi.Operator, v);
            }
            case Expr.LogicalAssign la:
            {
                var v = RewriteExpr(la.Value);
                return ReferenceEquals(v, la.Value) ? la : new Expr.LogicalAssign(la.Name, la.Operator, v);
            }
            case Expr.LogicalSet lset:
            {
                var o = RewriteExpr(lset.Object); var v = RewriteExpr(lset.Value);
                return ReferenceEquals(o, lset.Object) && ReferenceEquals(v, lset.Value) ? lset : new Expr.LogicalSet(o, lset.Name, lset.Operator, v);
            }
            case Expr.LogicalSetIndex lsi:
            {
                var o = RewriteExpr(lsi.Object); var idx = RewriteExpr(lsi.Index); var v = RewriteExpr(lsi.Value);
                return ReferenceEquals(o, lsi.Object) && ReferenceEquals(idx, lsi.Index) && ReferenceEquals(v, lsi.Value)
                    ? lsi : new Expr.LogicalSetIndex(o, idx, lsi.Operator, v);
            }
            case Expr.PrefixIncrement pi:
            {
                var o = RewriteExpr(pi.Operand);
                return ReferenceEquals(o, pi.Operand) ? pi : new Expr.PrefixIncrement(pi.Operator, o);
            }
            case Expr.PostfixIncrement po:
            {
                var o = RewriteExpr(po.Operand);
                return ReferenceEquals(o, po.Operand) ? po : new Expr.PostfixIncrement(o, po.Operator);
            }
            case Expr.Call c:
            {
                var callee = RewriteExpr(c.Callee);
                var args = RewriteListIfChanged(c.Arguments, RewriteExpr);
                return ReferenceEquals(callee, c.Callee) && ReferenceEquals(args, c.Arguments)
                    ? c : new Expr.Call(callee, c.Paren, c.TypeArgs, args, c.Optional, c.TypeArgNodes);
            }
            case Expr.CallPrivate cp:
            {
                var o = RewriteExpr(cp.Object);
                var args = RewriteListIfChanged(cp.Arguments, RewriteExpr);
                return ReferenceEquals(o, cp.Object) && ReferenceEquals(args, cp.Arguments)
                    ? cp : new Expr.CallPrivate(o, cp.Name, args);
            }
            case Expr.New n:
            {
                var callee = RewriteExpr(n.Callee);
                var args = RewriteListIfChanged(n.Arguments, RewriteExpr);
                return ReferenceEquals(callee, n.Callee) && ReferenceEquals(args, n.Arguments)
                    ? n : new Expr.New(callee, n.TypeArgs, args, n.TypeArgNodes);
            }
            case Expr.Get g:
            {
                var o = RewriteExpr(g.Object);
                return ReferenceEquals(o, g.Object) ? g : new Expr.Get(o, g.Name, g.Optional);
            }
            case Expr.GetPrivate gp:
            {
                var o = RewriteExpr(gp.Object);
                return ReferenceEquals(o, gp.Object) ? gp : new Expr.GetPrivate(o, gp.Name);
            }
            case Expr.GetIndex gi:
            {
                var o = RewriteExpr(gi.Object); var idx = RewriteExpr(gi.Index);
                return ReferenceEquals(o, gi.Object) && ReferenceEquals(idx, gi.Index)
                    ? gi : new Expr.GetIndex(o, idx, gi.Optional);
            }
            case Expr.ArrayLiteral arr:
            {
                var elems = RewriteListIfChanged(arr.Elements, RewriteExpr);
                return ReferenceEquals(elems, arr.Elements) ? arr : new Expr.ArrayLiteral(elems, arr.HoleIndices);
            }
            case Expr.ObjectLiteral obj:
                return RewriteObjectLiteral(obj);
            case Expr.TemplateLiteral tl:
            {
                var exprs = RewriteListIfChanged(tl.Expressions, RewriteExpr);
                return ReferenceEquals(exprs, tl.Expressions) ? tl : new Expr.TemplateLiteral(tl.Strings, exprs);
            }
            case Expr.TaggedTemplateLiteral ttl:
            {
                var tag = RewriteExpr(ttl.Tag);
                var exprs = RewriteListIfChanged(ttl.Expressions, RewriteExpr);
                return ReferenceEquals(tag, ttl.Tag) && ReferenceEquals(exprs, ttl.Expressions)
                    ? ttl : new Expr.TaggedTemplateLiteral(tag, ttl.CookedStrings, ttl.RawStrings, exprs);
            }
            case Expr.Spread sp2:
            {
                var inner = RewriteExpr(sp2.Expression);
                return ReferenceEquals(inner, sp2.Expression) ? sp2 : new Expr.Spread(inner);
            }
            case Expr.TypeAssertion ta:
            {
                var inner = RewriteExpr(ta.Expression);
                return ReferenceEquals(inner, ta.Expression) ? ta : ta with { Expression = inner };
            }
            case Expr.Satisfies sat:
            {
                var inner = RewriteExpr(sat.Expression);
                return ReferenceEquals(inner, sat.Expression) ? sat : sat with { Expression = inner };
            }
            case Expr.NonNullAssertion nn:
            {
                var inner = RewriteExpr(nn.Expression);
                return ReferenceEquals(inner, nn.Expression) ? nn : new Expr.NonNullAssertion(inner);
            }
            case Expr.Await aw:
            {
                var inner = RewriteExpr(aw.Expression);
                return ReferenceEquals(inner, aw.Expression) ? aw : new Expr.Await(aw.Keyword, inner);
            }
            case Expr.DynamicImport di:
            {
                var inner = RewriteExpr(di.PathExpression);
                return ReferenceEquals(inner, di.PathExpression) ? di : new Expr.DynamicImport(di.Keyword, inner);
            }
            case Expr.Yield y:
            {
                if (y.Value is null) return y;
                var inner = RewriteExpr(y.Value);
                return ReferenceEquals(inner, y.Value) ? y : new Expr.Yield(y.Keyword, inner, y.IsDelegating);
            }

            default:
                // Leaf / type-only / declaration-only nodes carry no nested generator expression:
                // Literal, Variable, This, Super, RegexLiteral, ImportMeta, ClassExpr (handled
                // structurally elsewhere). Returned unchanged.
                return expr;
        }
    }

    private Expr RewriteObjectLiteral(Expr.ObjectLiteral obj)
    {
        List<Expr.Property>? rewritten = null;
        for (int i = 0; i < obj.Properties.Count; i++)
        {
            var prop = obj.Properties[i];
            var newKey = prop.Key is Expr.ComputedKey ck ? RewriteComputedKey(ck) : prop.Key;
            var newValue = RewriteExpr(prop.Value);
            if (!ReferenceEquals(newKey, prop.Key) || !ReferenceEquals(newValue, prop.Value))
            {
                rewritten ??= new List<Expr.Property>(obj.Properties);
                rewritten[i] = prop with { Key = newKey, Value = newValue };
            }
        }
        return rewritten is null ? obj : new Expr.ObjectLiteral(rewritten) { IsFresh = obj.IsFresh };
    }

    private Expr.PropertyKey RewriteComputedKey(Expr.ComputedKey ck)
    {
        var inner = RewriteExpr(ck.Expression);
        return ReferenceEquals(inner, ck.Expression) ? ck : new Expr.ComputedKey(inner);
    }

    /// <summary>Rewrites a non-generator arrow / function expression, descending into its body and
    /// parameter defaults. Function expressions and arrows establish a new lexical scope, so they
    /// push a frame for the #534 enclosing-local capture analysis.</summary>
    private Expr RewriteNonGeneratorArrow(Expr.ArrowFunction af)
    {
        var newParams = RewriteParameters(af.Parameters);
        List<Stmt>? newBody;
        if (af.BlockBody is not null)
        {
            newBody = RewriteFunctionBody(af.Parameters, af.BlockBody, selfName: af.Name?.Lexeme);
        }
        else
        {
            newBody = null;
        }
        var newExpr = af.ExpressionBody is null ? null : RewriteExpr(af.ExpressionBody);
        bool changed = !ReferenceEquals(newParams, af.Parameters)
            || !ReferenceEquals(newBody, af.BlockBody)
            || !ReferenceEquals(newExpr, af.ExpressionBody);
        return changed ? af with { Parameters = newParams, BlockBody = newBody, ExpressionBody = newExpr } : af;
    }

    private List<Stmt.Parameter> RewriteParameters(List<Stmt.Parameter> parameters)
    {
        List<Stmt.Parameter>? rewritten = null;
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.DefaultValue is null) continue;
            var newDefault = RewriteExpr(p.DefaultValue);
            if (!ReferenceEquals(newDefault, p.DefaultValue))
            {
                rewritten ??= new List<Stmt.Parameter>(parameters);
                rewritten[i] = p with { DefaultValue = newDefault };
            }
        }
        return rewritten ?? parameters;
    }

    /// <summary>
    /// Rewrites a function/arrow body within a fresh <see cref="FunctionFrame"/> so that any
    /// generator expression closing over one of this body's locals is lifted into this body
    /// (#534) rather than the module. Lifts collected for this frame are appended to the body.
    /// </summary>
    private List<Stmt> RewriteFunctionBody(List<Stmt.Parameter> parameters, List<Stmt> body, string? selfName = null)
    {
        var locals = LocalBindingCollector.Collect(parameters, body, selfName);
        var frame = new FunctionFrame { Locals = locals };
        _frames.Add(frame);
        try
        {
            var rewritten = RewriteListIfChanged(body, RewriteStmt);
            if (frame.Lifted.Count == 0) return rewritten;

            // Append the frame's lifts (declarations hoist; trailing keeps the source-order type
            // checker happy with locals declared earlier in the body — mirrors the module case).
            var result = new List<Stmt>(rewritten.Count + frame.Lifted.Count);
            result.AddRange(rewritten);
            result.AddRange(frame.Lifted);
            return result;
        }
        finally
        {
            _frames.RemoveAt(_frames.Count - 1);
        }
    }

    private static List<T> RewriteListIfChanged<T>(List<T> source, Func<T, T> rewrite)
    {
        List<T>? rewritten = null;
        for (int i = 0; i < source.Count; i++)
        {
            var original = source[i];
            var next = rewrite(original);
            if (!ReferenceEquals(original, next))
            {
                rewritten ??= new List<T>(source);
                rewritten[i] = next;
            }
        }
        return rewritten ?? source;
    }

    private Expr LiftGeneratorArrow(Expr.ArrowFunction af)
    {
        var freeVars = FreeVariableCollector.Collect(af);

        // #678/#734: a generator expression (sync or async) that closes over a block-scoped binding
        // (loop variable, catch parameter, or a let/const/class declared in a nested block) cannot be
        // lifted — the module body and every enclosing function body sit outside that block, so the lift
        // would unbind the reference. Leave it in place as a generator EXPRESSION: the interpreter runs
        // both sync (SharpTSArrowGeneratorFunction) and async (SharpTSAsyncArrowGeneratorFunction)
        // generator expressions natively, and the type checker establishes the generator context
        // directly (CheckArrowFunction handles arrow.IsAsync). The body is still rewritten so any nested
        // generator expressions inside it are handled, and its own name (if any) stays bound natively —
        // no #679 self-binding rewrite is needed in place. The compiler has no generator-expression IL
        // path and reports a clear "Yield not supported in this context" error for the capturing case.
        if (CapturesBlockScopedBinding(freeVars))
        {
            var inPlaceBody = RewriteFunctionBody(af.Parameters, af.BlockBody!, selfName: af.Name?.Lexeme);
            var inPlaceParams = RewriteParameters(af.Parameters);
            return ReferenceEquals(inPlaceBody, af.BlockBody) && ReferenceEquals(inPlaceParams, af.Parameters)
                ? af
                : af with { BlockBody = inPlaceBody, Parameters = inPlaceParams };
        }

        var name = $"__genArrow_{_counter++}";
        var nameToken = new Token(TokenType.IDENTIFIER, name, null, af.Parameters.FirstOrDefault()?.Name.Line ?? 1);

        // Recurse into the body first so nested generator arrows are also lifted. The body is its
        // own function scope, so run it through the frame machinery too.
        var rewrittenBody = RewriteFunctionBody(af.Parameters, af.BlockBody!, selfName: af.Name?.Lexeme);
        var rewrittenParams = RewriteParameters(af.Parameters);

        // #679: a NAMED generator function expression can reference itself by name for recursion
        // (`const g = function* gen(n) { ... yield* gen(n - 1); }`). The lift renames the declaration to
        // the synthetic __genArrow_N and discards af.Name, so without help the self-reference is unbound.
        // Inject `const <name> = __genArrow_N;` at the top of the lifted body so the name resolves to the
        // generator inside its own body (the lifted declaration hoists, so it is in scope there). Skipped
        // when the name is shadowed by a parameter or a body-level declaration — there the inner binding
        // wins (named-function-expression scoping), and injecting a const would be a duplicate declaration.
        if (af.Name is not null && !IsSelfNameShadowed(af))
        {
            var selfBinding = new Stmt.Const(af.Name, TypeAnnotation: null, Initializer: new Expr.Variable(nameToken));
            var withSelfBinding = new List<Stmt>(rewrittenBody.Count + 1) { selfBinding };
            withSelfBinding.AddRange(rewrittenBody);
            rewrittenBody = withSelfBinding;
        }

        var funcStmt = new Stmt.Function(
            Name: nameToken,
            TypeParams: af.TypeParams,
            ThisType: af.ThisType,
            Parameters: rewrittenParams,
            Body: rewrittenBody,
            ReturnType: af.ReturnType,
            IsAsync: af.IsAsync,
            IsGenerator: true,
            // #775: a HasOwnThis generator expression / object generator method binds its own
            // dynamic receiver. Carry that need onto the lifted declaration so both back ends can
            // thread the call receiver into the generator body's `this`.
            HasDynamicThis: af.HasOwnThis,
            // Carry the annotation node twins so the lifted declaration resolves node-first
            // exactly like the original expression would have.
            ThisTypeNode: af.ThisTypeNode,
            ReturnTypeNode: af.ReturnTypeNode);

        // The declaration itself is scaffolding — the user wrote an expression, not a declaration —
        // but its body statements keep the spans they were parsed with.
        _spans?.MarkHidden(funcStmt);

        // Lift into the nearest enclosing function if the body closes over one of the enclosing
        // functions' locals (#534); otherwise to the module body (#522). Lifting into the enclosing
        // function keeps the captured local in lexical scope. Determining capture conservatively
        // over-approximates the BOUND set, so a free-variable name is only attributed to an
        // enclosing local when it truly escapes the generator body — never a false positive that
        // would relocate a module-closing generator into a function (which the compiler can't lower).
        if (_frames.Count > 0 && ClosesOverEnclosingLocal(freeVars))
        {
            _frames[^1].Lifted.Add(funcStmt);
        }
        else
        {
            _moduleLifted.Add(funcStmt);
        }

        return new Expr.Variable(nameToken);
    }

    /// <summary>
    /// True when a named function expression's own name is shadowed by a parameter or a body-level
    /// var/let/const/function/class declaration. In that case the inner binding — not the function
    /// itself — is what the name refers to inside the body (named-function-expression scoping), so the
    /// #679 self-binding must NOT be injected (it would be a duplicate declaration). Nested-block
    /// bindings do not shadow at the body level, so they are intentionally excluded.
    /// </summary>
    private static bool IsSelfNameShadowed(Expr.ArrowFunction af)
    {
        if (af.Name is null) return false;
        var bodyBindings = LocalBindingCollector.Collect(af.Parameters, af.BlockBody!, selfName: null);
        return bodyBindings.Contains(af.Name.Lexeme);
    }

    /// <summary>
    /// True when the generator arrow references a free variable that is bound as a local by one of
    /// the enclosing function scopes (so the lift must stay inside that scope, #534).
    /// </summary>
    private bool ClosesOverEnclosingLocal(HashSet<string> freeVars)
    {
        if (freeVars.Count == 0) return false;
        foreach (var frame in _frames)
        {
            foreach (var name in freeVars)
            {
                if (frame.Locals.Contains(name)) return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Collects the names declared at the top level of a function/arrow body (its directly-visible
/// locals): parameters, the optional self-reference name, and body-level <c>var</c>/<c>let</c>/
/// <c>const</c>/<c>function</c>/<c>class</c> declarations. Block-, loop-, and catch-scoped bindings
/// are intentionally excluded: a generator lifted to the end of this body would sit outside those
/// inner scopes, so a generator closing over one of them must NOT be attributed to this frame (it
/// falls through to a module-scope lift instead — the same behavior as before #534).
/// </summary>
internal static class LocalBindingCollector
{
    public static HashSet<string> Collect(List<Stmt.Parameter> parameters, List<Stmt> body, string? selfName)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters)
            locals.Add(p.Name.Lexeme);
        if (selfName is not null)
            locals.Add(selfName);
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                case Stmt.Var v: locals.Add(v.Name.Lexeme); break;       // covers `let` and `var`
                case Stmt.Const c: locals.Add(c.Name.Lexeme); break;
                case Stmt.Function f: locals.Add(f.Name.Lexeme); break;
                case Stmt.Class cl: locals.Add(cl.Name.Lexeme); break;
            }
        }
        return locals;
    }
}

/// <summary>
/// Collects the free variables of a generator function expression: identifier names referenced in
/// value position but not bound inside the function itself. The BOUND set is deliberately
/// over-approximated — every binding found anywhere in the body (including nested functions and
/// blocks) is treated as bound — which can only shrink the free set. That bias guarantees no false
/// positive: a name is reported free only when it genuinely escapes the generator, so a generator
/// that actually closes over a module-scope binding is never mis-attributed to an enclosing function
/// (whose nested-generator lowering the compiler can't perform, #501). The cost is occasional false
/// negatives (a capture also shadowed in a nested scope is missed), which merely fall back to the
/// pre-#534 module-scope lift — never a regression.
/// </summary>
internal sealed class FreeVariableCollector : Visitors.AstVisitorBase
{
    private readonly HashSet<string> _referenced = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bound = new(StringComparer.Ordinal);

    public static HashSet<string> Collect(Expr.ArrowFunction af)
    {
        var c = new FreeVariableCollector();
        c.SeedFunctionBindings(af.Parameters, af.Name?.Lexeme);
        foreach (var p in af.Parameters)
            if (p.DefaultValue is not null) c.Visit(p.DefaultValue);
        if (af.ExpressionBody is not null) c.Visit(af.ExpressionBody);
        if (af.BlockBody is not null)
            foreach (var s in af.BlockBody) c.Visit(s);
        c._referenced.ExceptWith(c._bound);
        return c._referenced;
    }

    private void SeedFunctionBindings(List<Stmt.Parameter> parameters, string? selfName)
    {
        foreach (var p in parameters)
            _bound.Add(p.Name.Lexeme);
        if (selfName is not null)
            _bound.Add(selfName);
        _bound.Add("arguments"); // function expressions bind their own `arguments`
    }

    protected override void VisitVariable(Expr.Variable expr) => _referenced.Add(expr.Name.Lexeme);

    protected override void VisitVar(Stmt.Var stmt)
    {
        _bound.Add(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        _bound.Add(stmt.Name.Lexeme);
        base.VisitConst(stmt);
    }

    protected override void VisitFunction(Stmt.Function stmt)
    {
        _bound.Add(stmt.Name.Lexeme);
        SeedFunctionBindings(stmt.Parameters, stmt.Name.Lexeme);
        base.VisitFunction(stmt);
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        SeedFunctionBindings(expr.Parameters, expr.Name?.Lexeme);
        base.VisitArrowFunction(expr);
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        _bound.Add(stmt.Name.Lexeme);
        base.VisitClass(stmt);
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        if (expr.Name is not null) _bound.Add(expr.Name.Lexeme);
        base.VisitClassExpr(expr);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        _bound.Add(stmt.Variable.Lexeme);
        base.VisitForOf(stmt);
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        _bound.Add(stmt.Variable.Lexeme);
        base.VisitForIn(stmt);
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        if (stmt.CatchParam is not null) _bound.Add(stmt.CatchParam.Lexeme);
        base.VisitTryCatch(stmt);
    }
}
