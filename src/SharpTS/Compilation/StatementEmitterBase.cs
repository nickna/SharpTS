using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Abstract base class for statement emission across different emitter types.
/// Provides unified dispatch logic and shared control flow implementations.
/// </summary>
/// <remarks>
/// This base class centralizes the statement dispatch switch and control flow
/// logic that was duplicated across ILEmitter, AsyncMoveNextEmitter,
/// GeneratorMoveNextEmitter, and AsyncGeneratorMoveNextEmitter.
///
/// Abstract methods must be implemented by subclasses for emitter-specific behavior:
/// - EmitVarDeclaration: Variable storage differs (locals vs fields)
/// - EmitReturn: Return semantics differ (void, bool, ValueTask&lt;bool&gt;)
/// - EmitTryCatch: Async emitters need await-aware exception handling
/// - Loop label management: ILEmitter uses CompilationContext, state machines use stack
///
/// Virtual methods provide default implementations that subclasses may override
/// for optimization (e.g., ILEmitter's dead code elimination and stack type tracking).
/// </remarks>
public abstract class StatementEmitterBase : ExpressionEmitterBase
{
    protected StatementEmitterBase(StateMachineEmitHelpers helpers)
        : base(helpers)
    {
    }

    #region Abstract Methods - Emitter-specific behavior

    /// <summary>
    /// Emits return statement. Different semantics per emitter type:
    /// - ILEmitter: void with Ret, or store + Leave inside exception blocks
    /// - Async: store result + leave to SetResult label
    /// - Generator: set state -2, return false (MoveNext contract)
    /// - AsyncGenerator: set state -2, return ValueTask&lt;bool&gt;(false)
    /// </summary>
    protected abstract void EmitReturn(Stmt.Return r);

    /// <summary>
    /// Emits try/catch/finally block.
    /// Async emitters override with await-aware exception handling.
    /// </summary>
    protected abstract void EmitTryCatch(Stmt.TryCatch t);

    #endregion

    #region Virtual Methods - Variable Declaration

    // GetHoistedVariableField moved to ExpressionEmitterBase for EmitStoreVariable access.

    /// <summary>
    /// Declares and initializes a variable.
    /// Default implementation handles hoisted fields (via GetHoistedVariableField) and
    /// non-hoisted locals. ILEmitter and AsyncArrowMoveNextEmitter override with their
    /// own logic.
    /// </summary>
    protected virtual void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        var field = GetHoistedVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store to field
            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                var temp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, temp);
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, field);
            }
            else
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
                IL.Emit(OpCodes.Stfld, field);
            }
        }
        else
        {
            // Not hoisted - use local variable
            var local = IL.DeclareLocal(typeof(object));
            Ctx.Locals.RegisterLocal(name, local);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                IL.Emit(OpCodes.Stloc, local);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
                IL.Emit(OpCodes.Stloc, local);
            }
        }
    }

    /// <summary>
    /// Declares and initializes a const variable.
    /// Similar to var declaration but const always has an initializer.
    /// </summary>
    protected virtual void EmitConstDeclaration(Stmt.Const c)
    {
        // Default: treat as Var with guaranteed initializer
        EmitVarDeclaration(new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer));
    }

    #endregion

    #region Virtual Methods - Loop Label Management (default: stack-based)

    protected readonly Stack<(Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)> _loopLabels = new();

    /// <summary>
    /// Registers a loop context for break/continue resolution.
    /// Default: pushes onto an internal stack. ILEmitter overrides to use CompilationContext.
    /// </summary>
    protected virtual void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
        => _loopLabels.Push((breakLabel, continueLabel, labelName != null ? new[] { labelName } : Ctx.TakePendingLoopLabels()));

    /// <summary>
    /// Exits the current loop context.
    /// </summary>
    protected virtual void ExitLoop()
        => _loopLabels.Pop();

    /// <summary>
    /// Gets the current innermost loop context, or null if not in a loop.
    /// </summary>
    protected virtual (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? CurrentLoop
        => _loopLabels.Count > 0 ? _loopLabels.Peek() : null;

    /// <summary>
    /// Finds a loop context that carries the given label name.
    /// </summary>
    protected virtual (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? FindLabeledLoop(string labelName)
    {
        foreach (var loop in _loopLabels)
            if (loop.LabelNames.Contains(labelName))
                return loop;
        return null;
    }

    #endregion

    #region Virtual Hooks - Override for customization

    /// <summary>
    /// Checks if a statement should be skipped due to dead code analysis.
    /// Default: no dead code analysis. ILEmitter overrides to use DeadCodeAnalyzer.
    /// </summary>
    protected virtual bool IsDead(Stmt stmt) => false;

    /// <summary>
    /// Emits a condition check that leaves a boolean-compatible value on stack.
    /// Default: EnsureBoxed + EmitTruthyCheck. ILEmitter overrides for stack type optimization.
    /// </summary>
    protected virtual void EmitConditionCheck(Expr condition)
    {
        EmitExpression(condition);
        EnsureBoxed();
        EmitTruthyCheck();
    }

    /// <summary>
    /// Emits a branch to a label. Override to use Leave instead of Br in exception blocks.
    /// </summary>
    protected virtual void EmitBranchToLabel(Label target)
    {
        IL.Emit(OpCodes.Br, target);
    }

    /// <summary>
    /// Emits truthy check. Default calls Runtime.IsTruthy via helpers (with stack tracking).
    /// </summary>
    protected virtual void EmitTruthyCheck()
    {
        _helpers.EmitTruthyCheck(Ctx.Runtime!.IsTruthy);
    }

    #endregion

    #region Core Statement Dispatch

    /// <summary>
    /// Emits an assignment-destructuring expression (#754): run the lowered assignment statements (temp
    /// binding + per-target writes, all synthesized <see cref="Stmt.Var"/>/<see cref="Stmt.Expression"/>
    /// with no control flow), then leave the result value (the original rhs) on the stack. Each statement
    /// goes through the normal <see cref="EmitStatement"/> path, so the async/generator subclasses handle
    /// an <c>await</c>/<c>yield</c> in the rhs without any extra plumbing.
    /// </summary>
    protected override void EmitDestructuringAssign(Expr.DestructuringAssign da)
    {
        foreach (var stmt in da.Assignments)
            EmitStatement(stmt);
        EmitExpression(da.ResultValue);
    }

    /// <summary>
    /// Emits one statement, marking where it starts so a debugger can bind a breakpoint to it.
    /// </summary>
    /// <remarks>
    /// This is the single dispatch point every backend shares. <see cref="ILEmitter"/> and the
    /// state-machine emitters differ in how they lower a statement — they override
    /// <see cref="EmitStatementCore"/> — but they all enter through here, which is what makes one
    /// sequence-point hook cover ordinary IL, async, and generator bodies alike. Marking happens
    /// after the dead-code check and before any IL is emitted, so the recorded offset is the first
    /// instruction the statement actually produces.
    /// </remarks>
    public void EmitStatement(Stmt stmt)
    {
        if (IsDead(stmt))
            return;

        MarkStatementStart(stmt);

        EmitStatementCore(stmt);
    }

    /// <summary>
    /// Records that <paramref name="stmt"/>'s code begins at the current IL offset, so a debugger
    /// can bind a breakpoint on that line to this instruction.
    /// </summary>
    /// <remarks>
    /// <see cref="EmitStatement"/> calls this for everything it dispatches. It is public because a
    /// few top-level paths wrap a statement in extra IL — awaiting a top-level async call — and
    /// drive expression emission themselves instead of going through the dispatcher; they mark the
    /// statement here so those lines stay steppable.
    /// </remarks>
    public void MarkStatementStart(Stmt stmt)
    {
        // Both are set together, and only for bodies emitted with symbols; a context that emits no
        // user-steppable code (definition phase, overload forwarding) simply has neither.
        if (Ctx.DebugScope is { } debugScope && Ctx.CurrentMethod is { } currentMethod)
            debugScope.MarkStatement(currentMethod, stmt, IL.ILOffset);
    }

    /// <summary>
    /// Dispatches statement emission to the appropriate handler method.
    /// </summary>
    protected virtual void EmitStatementCore(Stmt stmt)
    {
        // Spill temps never cross a statement boundary, so drop the live set here to keep
        // the per-suspension persist/rehydrate cost proportional to one statement (#400).
        _helpers.ClearLiveSpills();

        switch (stmt)
        {
            case Stmt.Expression e:
                if (!TryEmitDiscardedExpression(e.Expr))
                {
                    EmitExpression(e.Expr);
                    IL.Emit(OpCodes.Pop);
                }
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.Const c:
                EmitConstDeclaration(c);
                break;

            case Stmt.Function fn:
                Ctx.EmitBlockScopedInnerFunction?.Invoke(IL, Ctx, fn);
                break;

            case Stmt.Class classStmt:
                EmitStateMachineClassDeclaration(classStmt);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.DoWhile dw:
                EmitDoWhile(dw);
                break;

            case Stmt.For f:
                EmitFor(f);
                break;

            case Stmt.ForOf forOf:
                EmitForOf(forOf);
                break;

            case Stmt.ForIn fi:
                EmitForIn(fi);
                break;

            case Stmt.Block b:
                EmitBlock(b);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.Break b:
                EmitBreak(b);
                break;

            case Stmt.Continue c:
                EmitContinue(c);
                break;

            case Stmt.LabeledStatement ls:
                EmitLabeledStatement(ls);
                break;

            case Stmt.Switch s:
                EmitSwitch(s);
                break;

            case Stmt.TryCatch t:
                EmitTryCatch(t);
                break;

            case Stmt.Throw t:
                EmitThrow(t);
                break;

            case Stmt.DeclareModule:
            case Stmt.DeclareGlobal:
                // Module/global augmentations are type-only - no IL emission needed
                break;

            case Stmt.Directive:
            case Stmt.FileDirective:
            case Stmt.Field:
            case Stmt.Accessor:
            case Stmt.AutoAccessor:
            case Stmt.StaticBlock:
                // Directive effects (notably strictness) are established when
                // the function body is analyzed. These nodes and class-member
                // declarations do not emit executable state-machine IL.
                break;

            case Stmt.Using u:
                EmitUsingDeclaration(u);
                break;
            default:
                throw new CompileException($"Unhandled statement type in ILEmitter: {stmt.GetType().Name}");
        }
    }

    /// <summary>
    /// Gives an emitter a chance to lower an expression whose JavaScript value
    /// is discarded without first manufacturing a stack result. The default
    /// recognizes the object-backed array push shape shared by async/generator
    /// state machines; specialized synchronous emitters may add narrower typed
    /// paths before delegating here.
    /// </summary>
    protected virtual bool TryEmitDiscardedExpression(Expr expression)
    {
        if (expression is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Get
                {
                    Optional: false,
                    Name.Lexeme: "push"
                } methodGet,
                Arguments: { Count: 1 } arguments
            }
            || Ctx.TypeMap?.Get(methodGet.Object) is not TypeSystem.TypeInfo.Array receiverType
            || ArrayElements.Resolve(receiverType) is not { Kind: ArrayElementsKind.Object }
            || arguments[0] is Expr.Spread
            || ExpressionSuspensionDetector.Contains(arguments[0])
            || Ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != false
            || Ctx.RuntimeFeatures.UsesArrayPrototypeMutation)
        {
            return false;
        }

        EmitExpression(methodGet.Object);
        EnsureBoxed();
        EmitExpression(arguments[0]);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ArrayPushOneDiscarded);
        SetStackUnknown();
        return true;
    }

    private sealed class ExpressionSuspensionDetector : AstVisitorBase
    {
        public bool Found { get; private set; }

        public static bool Contains(Expr expression)
        {
            var detector = new ExpressionSuspensionDetector();
            detector.Visit(expression);
            return detector.Found;
        }

        protected override void VisitAwait(Expr.Await expression)
        {
            Found = true;
            ShouldContinue = false;
        }

        protected override void VisitYield(Expr.Yield expression)
        {
            Found = true;
            ShouldContinue = false;
        }

        // Creating a nested callable does not execute its body while the
        // receiver is live on this state machine's evaluation stack.
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    /// <summary>
    /// Emits a 'using' or 'await using' declaration.
    /// Stores the resource and registers it for disposal at scope exit.
    /// </summary>
    protected virtual void EmitUsingDeclaration(Stmt.Using u)
    {
        foreach (var binding in u.Bindings)
        {
            // Evaluate the initializer
            EmitExpression(binding.Initializer);
            EnsureBoxed();

            // Store in a local variable
            if (binding.Name != null)
            {
                var local = Ctx.Locals.DeclareLocal(binding.Name.Lexeme, Types.Object);
                IL.Emit(OpCodes.Stloc, local);
            }
            else
            {
                // No name binding - just pop the value
                IL.Emit(OpCodes.Pop);
            }

            // Note: Proper disposal at scope exit would require modifying EmitBlock
            // to emit try/finally blocks. For now, disposal is handled by the runtime
            // when using the interpreter. Full IL support can be added later.
        }
    }

    #endregion

    #region Virtual Control Flow Methods - Default Implementations

    /// <summary>
    /// Emits an if/else statement.
    /// </summary>
    protected virtual void EmitIf(Stmt.If i)
    {
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        EmitConditionCheck(i.Condition);
        IL.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        IL.MarkLabel(endLabel);
    }

    /// <summary>
    /// Honors the runner's cooperative cancellation flag at a loop backedge so
    /// compiled IL inside async/generator state machines (which inherit these
    /// base loop emitters) can be unwound. See issue #74 — without this, a
    /// `while(true){}` inside an async function hangs the test thread past the
    /// runner's timeout.
    /// </summary>
    /// <remarks>
    /// Perf (#856): the backedge inlines the field test and, on the cold cancel
    /// path, emits <c>call BuildCancellationException(); throw</c> — NOT
    /// <c>call CheckCancellation()</c>. The distinction is decisive on SysV x64:
    /// <c>CheckCancellation()</c> is, from the JIT's flow-graph view, a
    /// <em>returning</em> call (its <c>throw</c> is internal and conditional), so
    /// the register allocator must assume control returns from it. Because every
    /// XMM register is caller-saved on SysV x64, a returning call inside the loop
    /// forces the loop-carried doubles (and the loop counter) to be stack-resident
    /// across every iteration — a load/store per use. Emitting a real <c>throw</c>
    /// opcode makes the path non-returning, so those values are dead on the cancel
    /// path and stay in registers on the hot path. Measured ~1.8× on tight numeric
    /// loops (objects/factorial reach Node parity); the earlier inline-call form
    /// (#874) only removed the unconditional-call overhead, not this spill.
    /// <c>BuildCancellationException</c> merely <em>constructs</em> the exception
    /// (it does not throw), so the <c>throw</c> happens here at the backedge.
    ///
    /// The <c>volatile.</c> prefix is mandatory: <c>_cancelRequested</c> is
    /// loop-invariant, so a plain <c>ldsfld</c> could be hoisted out of the loop
    /// by RyuJIT's LICM — reading the flag once and never re-checking, silently
    /// reintroducing the #74 hang. The volatile read forbids that hoist and was
    /// measured to cost nothing here.
    /// </remarks>
    protected void EmitCancellationCheck()
    {
        if (Ctx.Runtime?.BuildCancellationExceptionMethod == null || Ctx.Runtime?.CancelRequestedField == null)
            return;

        var notCancelled = IL.DefineLabel();
        IL.Emit(OpCodes.Volatile);
        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.CancelRequestedField);
        IL.Emit(OpCodes.Brfalse, notCancelled);
        IL.Emit(OpCodes.Call, Ctx.Runtime.BuildCancellationExceptionMethod);
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(notCancelled);
    }

    /// <summary>
    /// Emits a while loop.
    /// </summary>
    protected virtual void EmitWhile(Stmt.While w)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);
        EmitCancellationCheck();
        EmitConditionCheck(w.Condition);
        IL.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);

        IL.MarkLabel(continueLabel);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a do-while loop.
    /// </summary>
    protected virtual void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);
        EmitCancellationCheck();
        EmitStatement(dw.Body);

        IL.MarkLabel(continueLabel);
        EmitConditionCheck(dw.Condition);
        IL.Emit(OpCodes.Brtrue, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a for loop with proper continue handling and ES6 block scoping.
    /// Continue jumps to the increment, not past it.
    /// Variables declared with let/const in the initializer are scoped to the loop.
    /// </summary>
    protected virtual void EmitFor(Stmt.For f)
    {
        // Create scope for loop variables (ES6 let/const block scoping)
        Ctx.Locals.EnterScope();

        // Emit initializer (once, defines loop variable in current scope)
        if (f.Initializer != null)
            EmitStatement(f.Initializer);

        // Per-iteration reference cells (#650): see EmitForLoopCellInit. In a state-machine
        // emitter the cell stays an IL local — valid because the analyzer only marks a loop
        // when its body has no direct suspension point, so the whole loop runs within one
        // MoveNext segment (the cell, a heap StrongBox, is captured by reference by closures
        // and outlives the segment).
        var cellNames = Ctx.ClosureAnalyzer?.GetForLoopCells(f);
        List<(string Name, LocalBuilder? Prior)>? activeCells = null;
        if (cellNames != null && cellNames.Count > 0)
            activeCells = EmitForLoopCellInit(cellNames);

        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();  // Points to increment

        EnterLoop(endLabel, continueLabel);

        IL.MarkLabel(startLabel);
        EmitCancellationCheck();

        // Check condition (if present)
        if (f.Condition != null)
        {
            EmitConditionCheck(f.Condition);
            IL.Emit(OpCodes.Brfalse, endLabel);
        }

        // Emit body
        EmitStatement(f.Body);

        // Continue target: increment goes here
        IL.MarkLabel(continueLabel);
        if (activeCells != null)
            EmitForLoopCellCopyForward(activeCells);
        if (f.Increment != null)
        {
            EmitExpression(f.Increment);
            IL.Emit(OpCodes.Pop);  // Discard increment result
        }

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();

        if (activeCells != null)
            foreach (var (name, prior) in activeCells)
            {
                if (prior != null) Ctx.CellBindingLocals[name] = prior;
                else Ctx.CellBindingLocals.Remove(name);
            }

        // Exit loop scope
        Ctx.Locals.ExitScope();
    }

    /// <summary>
    /// For each per-iteration cell binding (#650), wraps the loop variable's initial value
    /// (already stored in its plain local by the initializer) in a fresh
    /// <c>StrongBox&lt;object&gt;</c>, stores the box in a dedicated local, and registers it in
    /// <see cref="CompilationContext.CellBindingLocals"/> so subsequent body/condition/increment
    /// access (and any capturing closure) goes through the cell. Returns the names set up, each
    /// paired with any shadowed outer cell to restore on loop exit (nested same-named loops).
    /// Shared by the sync <see cref="ILEmitter"/> override and the state-machine emitters.
    /// </summary>
    protected List<(string Name, LocalBuilder? Prior)> EmitForLoopCellInit(IEnumerable<string> cellNames)
    {
        var active = new List<(string, LocalBuilder?)>();
        foreach (var name in cellNames)
        {
            // Load the binding's initial value through the resolver so this works whether the
            // loop variable lives as an IL local (sync / async fn / generator) or a hoisted
            // state-machine field (async arrow). The cell is not registered yet, so the
            // resolver reads the underlying storage rather than recursing.
            if (!Resolver.HasVariable(name)) continue; // defensive: binding has no slot
            var st = Resolver.TryLoadVariable(name);
            if (st == null) continue;
            SetStackType(st.Value);
            EnsureBoxed(); // box double/bool to object for StrongBox<object>
            IL.Emit(OpCodes.Newobj, Ctx.Types.StrongBoxOfObjectCtor);

            var cellLocal = IL.DeclareLocal(Ctx.Types.StrongBoxOfObject);
            IL.Emit(OpCodes.Stloc, cellLocal);
            Ctx.CellBindingLocals.TryGetValue(name, out var prior);
            Ctx.CellBindingLocals[name] = cellLocal;
            active.Add((name, prior));
        }
        return active;
    }

    /// <summary>
    /// ECMA-262 13.7.4 CreatePerIterationEnvironment analog: allocates a fresh cell for each
    /// binding, copying the current cell's value forward. Closures created in the just-finished
    /// iteration keep their (old) cell; the loop's increment then operates on the new cell.
    /// </summary>
    protected void EmitForLoopCellCopyForward(List<(string Name, LocalBuilder? Prior)> activeCells)
    {
        foreach (var (name, _) in activeCells)
        {
            var cellLocal = Ctx.CellBindingLocals[name];
            IL.Emit(OpCodes.Ldloc, cellLocal);
            IL.Emit(OpCodes.Ldfld, Ctx.Types.StrongBoxOfObjectValueField);
            IL.Emit(OpCodes.Newobj, Ctx.Types.StrongBoxOfObjectCtor);
            IL.Emit(OpCodes.Stloc, cellLocal);
        }
    }

    /// <summary>
    /// Emits a for...of loop using IEnumerable pattern.
    /// Override in ILEmitter for iterator protocol support.
    /// Override in async emitters for for await...of support.
    /// </summary>
    protected virtual void EmitForOf(Stmt.ForOf f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // Emit iterable and get enumerator
        EmitExpression(f.Iterable);
        EnsureBoxed();

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        IL.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = IL.DeclareLocal(typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        EnterLoop(endLabel, continueLabel);

        // Declare loop variable
        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        IL.MarkLabel(startLabel);
        EmitCancellationCheck();

        // Check MoveNext
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from Current
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, current);
        });

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a <c>for await...of</c> loop over an async iterable. Shared by every async
    /// state-machine emitter (async functions, async arrows, async generators) so the
    /// async-iterator lowering lives in exactly one place instead of being duplicated and
    /// drifting per emitter.
    /// </summary>
    /// <remarks>
    /// First tries the <c>Symbol.asyncIterator</c> protocol (custom async iterators), then
    /// falls back to the <c>$IAsyncGenerator</c> interface. Each <c>next()</c> result is
    /// awaited via a blocking <c>GetAwaiter().GetResult()</c>; the loop does not suspend the
    /// enclosing state machine, so loop-scoped IL locals are safe as long as the loop body
    /// itself does not await across them.
    ///
    /// Subclasses dispatch here from their <see cref="EmitForOf"/> override when
    /// <see cref="Stmt.ForOf.IsAsync"/> is set. <see cref="GetHoistedVariableField"/> supplies
    /// the loop-variable storage so each emitter routes the binding through its own state
    /// machine fields.
    /// </remarks>
    protected virtual void EmitForAwaitOf(Stmt.ForOf f)
    {
        // for await...of iterates over async iterables.
        // First try the Symbol.asyncIterator protocol, then fall back to $IAsyncGenerator.
        // The result from next() is a promise/task with { value, done } properties.
        var il = IL;
        var types = Types;
        var runtime = Ctx.Runtime!;

        string varName = f.Variable.Lexeme;

        // Emit the async iterable expression
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Store the iterable
        var iterableLocal = il.DeclareLocal(types.Object);
        il.Emit(OpCodes.Stloc, iterableLocal);

        // Declare the loop variable AFTER the iterable is evaluated (so the iterable expression
        // can't resolve to the not-yet-bound loop variable). Storage is routed through the
        // DeclareLoopVariable/EmitStoreLoopVariable hooks so each emitter binds it correctly: a
        // hoisted state-machine field for async functions and async generators, or an
        // emitter-local for async arrows (whose resolver reads its own local map, not Ctx.Locals).
        var loopVarLocal = DeclareLoopVariable(varName);

        // Try async iterator protocol: GetIteratorFunction(iterable, Symbol.asyncIterator)
        var asyncIteratorFnLocal = il.DeclareLocal(types.Object);
        var asyncGenLabel = il.DefineLabel();
        var afterLoopLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolAsyncIterator);
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Stloc, asyncIteratorFnLocal);

        // GetMethod treats null and undefined as an absent async method, so
        // either value falls back to the synchronous/async-generator adapter.
        il.Emit(OpCodes.Ldloc, asyncIteratorFnLocal);
        il.Emit(OpCodes.Brfalse, asyncGenLabel);
        il.Emit(OpCodes.Ldloc, asyncIteratorFnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, asyncGenLabel);

        // ===== Custom async iterator protocol path =====
        {
            // Call the async iterator function to get the async iterator object.
            // Use InvokeMethodValue to properly bind 'this' to the iterable object.
            il.Emit(OpCodes.Ldloc, iterableLocal);           // receiver (this)
            il.Emit(OpCodes.Ldloc, asyncIteratorFnLocal);    // method
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, types.Object);           // args
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

            var asyncIteratorLocal = il.DeclareLocal(types.Object);
            il.Emit(OpCodes.Stloc, asyncIteratorLocal);

            var startLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            var cleanupLabel = il.DefineLabel();
            var continueLabel = il.DefineLabel();

            // Break goes to cleanup (calls iterator.return()), not directly to end
            EnterLoop(cleanupLabel, continueLabel);

            il.MarkLabel(startLabel);

            // Call InvokeIteratorNext(asyncIterator) which returns a Promise/Task
            il.Emit(OpCodes.Ldloc, asyncIteratorLocal);
            il.Emit(OpCodes.Call, runtime.InvokeIteratorNext);

            // The result should be a Task/Promise - await it.
            // Store as object first, then check if it's a $TSPromise or Task.
            var nextResultLocal = il.DeclareLocal(types.Object);
            il.Emit(OpCodes.Stloc, nextResultLocal);

            // If it's a $TSPromise, unwrap to its inner Task<object?>
            // (custom async iterators may return $TSPromise via WrapTaskAsPromise)
            var notTSPromiseLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, nextResultLocal);
            il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
            il.Emit(OpCodes.Brfalse, notTSPromiseLabel);
            // Replace nextResultLocal with the inner Task
            il.Emit(OpCodes.Ldloc, nextResultLocal);
            il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
            il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
            il.Emit(OpCodes.Stloc, nextResultLocal);
            il.MarkLabel(notTSPromiseLabel);

            // Check if result is a Task<object> and await it
            var isTaskLabel = il.DefineLabel();
            var afterAwaitLabel = il.DefineLabel();
            var resultLocal = il.DeclareLocal(types.Object);

            il.Emit(OpCodes.Ldloc, nextResultLocal);
            il.Emit(OpCodes.Isinst, types.TaskOfObject);
            il.Emit(OpCodes.Brtrue, isTaskLabel);

            // Not a task - use the result directly (might be a sync iterator result)
            il.Emit(OpCodes.Ldloc, nextResultLocal);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, afterAwaitLabel);

            // Is a Task - await it
            il.MarkLabel(isTaskLabel);
            il.Emit(OpCodes.Ldloc, nextResultLocal);
            il.Emit(OpCodes.Castclass, types.TaskOfObject);
            var taskLocal = il.DeclareLocal(types.TaskOfObject);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloc, taskLocal);
            var getAwaiter = types.GetMethodNoParams(types.TaskOfObject, "GetAwaiter");
            il.Emit(OpCodes.Call, getAwaiter);
            var awaiterLocal = il.DeclareLocal(types.TaskAwaiterOfObject);
            il.Emit(OpCodes.Stloc, awaiterLocal);

            il.Emit(OpCodes.Ldloca, awaiterLocal);
            var getResult = types.GetMethodNoParams(types.TaskAwaiterOfObject, "GetResult");
            il.Emit(OpCodes.Call, getResult);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(afterAwaitLabel);

            // Check if done: use GetIteratorDone
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, runtime.GetIteratorDone);
            il.Emit(OpCodes.Brtrue, endLabel);

            // Assign to loop variable (value via GetIteratorValue)
            EmitStoreLoopVariable(loopVarLocal, varName, () =>
            {
                il.Emit(OpCodes.Ldloc, resultLocal);
                il.Emit(OpCodes.Call, runtime.GetIteratorValue);
            });

            EmitStatement(f.Body);

            il.MarkLabel(continueLabel);
            il.Emit(OpCodes.Br, startLabel);

            // Cleanup on break: call iterator.return() to trigger finally blocks in generators
            il.MarkLabel(cleanupLabel);
            {
                // Get the "return" method from the async iterator
                il.Emit(OpCodes.Ldloc, asyncIteratorLocal);
                il.Emit(OpCodes.Ldstr, "return");
                il.Emit(OpCodes.Call, runtime.GetProperty);

                var returnFnLocal = il.DeclareLocal(types.Object);
                il.Emit(OpCodes.Stloc, returnFnLocal);

                // If no return method, skip cleanup — iterator.return() is
                // optional per the iterator protocol. GetProperty reports a
                // missing member as either null or $Undefined, and
                // InvokeMethodValue now throws TypeError for both (#260), so
                // guard against both here.
                il.Emit(OpCodes.Ldloc, returnFnLocal);
                il.Emit(OpCodes.Brfalse, endLabel);
                il.Emit(OpCodes.Ldloc, returnFnLocal);
                il.Emit(OpCodes.Isinst, runtime.UndefinedType);
                il.Emit(OpCodes.Brtrue, endLabel);

                // Call: InvokeMethodValue(asyncIterator, returnFn, [])
                il.Emit(OpCodes.Ldloc, asyncIteratorLocal);
                il.Emit(OpCodes.Ldloc, returnFnLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, types.Object);
                il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

                // If result is a Task, await it. Unwrap $TSPromise first if present.
                var returnResultLocal = il.DeclareLocal(types.Object);
                il.Emit(OpCodes.Stloc, returnResultLocal);

                // If $TSPromise, replace with inner Task<object?>
                var returnNotTSPromiseLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, returnResultLocal);
                il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
                il.Emit(OpCodes.Brfalse, returnNotTSPromiseLabel);
                il.Emit(OpCodes.Ldloc, returnResultLocal);
                il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
                il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
                il.Emit(OpCodes.Stloc, returnResultLocal);
                il.MarkLabel(returnNotTSPromiseLabel);

                il.Emit(OpCodes.Ldloc, returnResultLocal);
                il.Emit(OpCodes.Isinst, types.TaskOfObject);
                il.Emit(OpCodes.Brfalse, endLabel);

                il.Emit(OpCodes.Ldloc, returnResultLocal);
                il.Emit(OpCodes.Castclass, types.TaskOfObject);
                var cleanupTaskLocal = il.DeclareLocal(types.TaskOfObject);
                il.Emit(OpCodes.Stloc, cleanupTaskLocal);
                il.Emit(OpCodes.Ldloc, cleanupTaskLocal);
                var cleanupGetAwaiter = types.GetMethodNoParams(types.TaskOfObject, "GetAwaiter");
                il.Emit(OpCodes.Call, cleanupGetAwaiter);
                var cleanupAwaiterLocal = il.DeclareLocal(types.TaskAwaiterOfObject);
                il.Emit(OpCodes.Stloc, cleanupAwaiterLocal);
                il.Emit(OpCodes.Ldloca, cleanupAwaiterLocal);
                var cleanupGetResult = types.GetMethodNoParams(types.TaskAwaiterOfObject, "GetResult");
                il.Emit(OpCodes.Call, cleanupGetResult);
                il.Emit(OpCodes.Pop); // Discard return result
            }

            il.MarkLabel(endLabel);
            ExitLoop();
            il.Emit(OpCodes.Br, afterLoopLabel); // Skip the fallback path
        }

        // ===== $IAsyncGenerator / async-from-sync fallback path =====
        il.MarkLabel(asyncGenLabel);
        {
            // Adapt synchronous iterables, then cast to the common interface.
            // Non-iterables still fail here.
            var asyncGenInterface = runtime.AsyncGeneratorInterfaceType;
            il.Emit(OpCodes.Ldloc, iterableLocal);
            il.Emit(OpCodes.Call, runtime.AdaptSyncIterableToAsyncGenerator);
            il.Emit(OpCodes.Castclass, asyncGenInterface);

            // Store the async generator in a local
            var asyncGenLocal = il.DeclareLocal(asyncGenInterface);
            il.Emit(OpCodes.Stloc, asyncGenLocal);

            var genStartLabel = il.DefineLabel();
            var genEndLabel = il.DefineLabel();
            var genCleanupLabel = il.DefineLabel();
            var genContinueLabel = il.DefineLabel();

            // Break goes to cleanup (calls generator.return()), not directly to end
            EnterLoop(genCleanupLabel, genContinueLabel);

            il.MarkLabel(genStartLabel);

            // Call next(undefined) which returns Task<object>; for-await-of passes undefined as the sent
            // value (#473 — next() now takes one argument matching the $IAsyncGenerator interface).
            il.Emit(OpCodes.Ldloc, asyncGenLocal);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, runtime.AsyncGeneratorNextMethod);

            // Await the Task<object>
            var genTaskLocal = il.DeclareLocal(types.TaskOfObject);
            il.Emit(OpCodes.Stloc, genTaskLocal);
            il.Emit(OpCodes.Ldloc, genTaskLocal);
            var genGetAwaiter = types.GetMethodNoParams(types.TaskOfObject, "GetAwaiter");
            il.Emit(OpCodes.Call, genGetAwaiter);
            var genAwaiterLocal = il.DeclareLocal(types.TaskAwaiterOfObject);
            il.Emit(OpCodes.Stloc, genAwaiterLocal);
            il.Emit(OpCodes.Ldloca, genAwaiterLocal);
            var genGetResult = types.GetMethodNoParams(types.TaskAwaiterOfObject, "GetResult");
            il.Emit(OpCodes.Call, genGetResult);

            // Result is a Dictionary<string, object> with { value, done }
            var genResultLocal = il.DeclareLocal(types.Object);
            il.Emit(OpCodes.Stloc, genResultLocal);

            // Check if done: GetProperty(result, "done")
            il.Emit(OpCodes.Ldloc, genResultLocal);
            il.Emit(OpCodes.Ldstr, "done");
            il.Emit(OpCodes.Call, runtime.GetProperty);

            // Convert to bool and check - natural done exits directly (no cleanup needed)
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brtrue, genEndLabel);

            // Assign to loop variable (value via GetProperty(result, "value"))
            EmitStoreLoopVariable(loopVarLocal, varName, () =>
            {
                il.Emit(OpCodes.Ldloc, genResultLocal);
                il.Emit(OpCodes.Ldstr, "value");
                il.Emit(OpCodes.Call, runtime.GetProperty);
            });

            EmitStatement(f.Body);

            il.MarkLabel(genContinueLabel);
            il.Emit(OpCodes.Br, genStartLabel);

            // Cleanup on break: call generator.return(null) to trigger finally blocks
            il.MarkLabel(genCleanupLabel);
            il.Emit(OpCodes.Ldloc, asyncGenLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, runtime.AsyncGeneratorReturnMethod);
            // Await the Task<object> result and discard it
            var cleanupGenTaskLocal = il.DeclareLocal(types.TaskOfObject);
            il.Emit(OpCodes.Stloc, cleanupGenTaskLocal);
            il.Emit(OpCodes.Ldloc, cleanupGenTaskLocal);
            il.Emit(OpCodes.Call, genGetAwaiter);
            var cleanupGenAwaiterLocal = il.DeclareLocal(types.TaskAwaiterOfObject);
            il.Emit(OpCodes.Stloc, cleanupGenAwaiterLocal);
            il.Emit(OpCodes.Ldloca, cleanupGenAwaiterLocal);
            il.Emit(OpCodes.Call, genGetResult);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, genEndLabel);

            il.MarkLabel(genEndLabel);
            ExitLoop();
        }

        // Common exit point for both paths
        il.MarkLabel(afterLoopLabel);
    }

    /// <summary>
    /// Emits a for...in loop iterating over object keys.
    /// </summary>
    protected virtual void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // Get keys from object
        EmitExpression(f.Object);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetKeys);

        var keysLocal = IL.DeclareLocal(Types.ListOfObject);
        IL.Emit(OpCodes.Stloc, keysLocal);

        // Index variable
        var indexLocal = IL.DeclareLocal(Types.Int32);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        EnterLoop(endLabel, continueLabel);

        // Loop variable
        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        IL.MarkLabel(startLabel);
        EmitCancellationCheck();

        // Check index < keys.Count
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from keys[index]
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            IL.Emit(OpCodes.Ldloc, keysLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetElement);
        });

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Emits a block statement.
    /// </summary>
    protected virtual void EmitBlock(Stmt.Block b)
    {
        if (b.Statements == null) return;

        // ES6 block scoping: let/const declared in the block are scoped to the block.
        // Without this, an inner `const x = ...` registers a local that shadows the
        // outer `let x` for the rest of the method — even after the block exits.
        Ctx.Locals.EnterScope();
        try
        {
            foreach (var stmt in b.Statements)
                EmitStatement(stmt);
        }
        finally
        {
            Ctx.Locals.ExitScope();
        }
    }

    /// <summary>
    /// Emits a break statement.
    /// </summary>
    protected virtual void EmitBreak(Stmt.Break b)
    {
        var loop = b.Label != null
            ? FindLabeledLoop(b.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.BreakLabel);
    }

    /// <summary>
    /// Emits a continue statement.
    /// </summary>
    protected virtual void EmitContinue(Stmt.Continue c)
    {
        var loop = c.Label != null
            ? FindLabeledLoop(c.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.ContinueLabel);
    }

    /// <summary>
    /// Emits a labeled statement.
    /// </summary>
    protected virtual void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        // Look through a chain of labels (a: b: …) to whatever they ultimately wrap.
        var inner = UnwrapLabelChain(ls, out var chainLabels);

        if (IsLabelableLoop(inner))
        {
            // Direct (or chained) loop: park EVERY label in the chain so the loop attaches them all
            // to its OWN break/continue targets. A for registers continue at its increment, a while
            // at its condition, etc.; marking a continue label here (ahead of a for's initializer)
            // would re-run it forever — and the outer label of a chain used to fall into exactly
            // that path (#558/#580).
            foreach (var label in chainLabels)
                Ctx.AddPendingLoopLabel(label);
            try
            {
                EmitStatement(inner);
            }
            finally
            {
                // The loop's EnterLoop drains the parked labels; clear any it somehow didn't.
                Ctx.ClearPendingLoopLabels();
            }
            return;
        }

        // Non-loop labeled statement (a block, etc.). Mark the continue target before the statement
        // (harmless for a block) and keep one wrapper scope per label by recursing through the chain;
        // only `break <label>` is meaningful here.
        var breakLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();
        IL.MarkLabel(continueLabel);
        EnterLoop(breakLabel, continueLabel, ls.Label.Lexeme);
        EmitStatement(ls.Statement);
        ExitLoop();
        IL.MarkLabel(breakLabel);
    }

    /// <summary>
    /// Follows a chain of nested labeled statements (<c>a: b: stmt</c>) to the statement they wrap,
    /// collecting every label name along the way.
    /// </summary>
    protected static Stmt UnwrapLabelChain(Stmt.LabeledStatement ls, out List<string> labels)
    {
        labels = [];
        Stmt inner = ls;
        while (inner is Stmt.LabeledStatement l)
        {
            labels.Add(l.Label.Lexeme);
            inner = l.Statement;
        }
        return inner;
    }

    /// <summary>
    /// True when a labeled statement directly wraps an iteration statement, so the label
    /// belongs on the loop's own break/continue targets rather than a wrapper.
    /// </summary>
    protected static bool IsLabelableLoop(Stmt stmt)
        => stmt is Stmt.While or Stmt.DoWhile or Stmt.For or Stmt.ForOf or Stmt.ForIn;

    /// <summary>
    /// Detects return/break/continue that would transfer control out of the surrounding try
    /// region. Over-approximates conservatively (labeled break/continue are always treated as
    /// escaping): a false positive only costs a statement some mini-segment exception coverage,
    /// whereas a false negative would emit a <c>br</c>/<c>ret</c> inside a protected region (illegal IL).
    /// Nested function/arrow bodies are not traversed (their returns are their own).
    /// </summary>
    /// <remarks>
    /// Shared by the suspension-aware emitters (generator, async generator, async function): each
    /// segments a flag-based try body so a suspension point or a non-local exit lands at the top level
    /// (outside the mini IL try/catch), where its <c>ret</c>/<c>br</c>/<c>Leave</c> is legal.
    /// </remarks>
    protected static bool ContainsEscapingExit(Stmt stmt, bool insideLoop, bool insideSwitch)
    {
        switch (stmt)
        {
            case Stmt.Return:
                return true;
            case Stmt.Break b:
                return b.Label != null || !(insideLoop || insideSwitch);
            case Stmt.Continue c:
                return c.Label != null || !insideLoop;
            case Stmt.If i:
                return ContainsEscapingExit(i.ThenBranch, insideLoop, insideSwitch)
                    || (i.ElseBranch != null && ContainsEscapingExit(i.ElseBranch, insideLoop, insideSwitch));
            case Stmt.Block b:
                if (b.Statements == null) return false;
                foreach (var s in b.Statements)
                    if (ContainsEscapingExit(s, insideLoop, insideSwitch)) return true;
                return false;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    if (ContainsEscapingExit(s, insideLoop, insideSwitch)) return true;
                return false;
            case Stmt.While w:
                return ContainsEscapingExit(w.Body, insideLoop: true, insideSwitch);
            case Stmt.DoWhile dw:
                return ContainsEscapingExit(dw.Body, insideLoop: true, insideSwitch);
            case Stmt.For f:
                return ContainsEscapingExit(f.Body, insideLoop: true, insideSwitch);
            case Stmt.ForOf fo:
                return ContainsEscapingExit(fo.Body, insideLoop: true, insideSwitch);
            case Stmt.ForIn fi:
                return ContainsEscapingExit(fi.Body, insideLoop: true, insideSwitch);
            case Stmt.Switch s:
                foreach (var c in s.Cases)
                    foreach (var cs in c.Body)
                        if (ContainsEscapingExit(cs, insideLoop, insideSwitch: true)) return true;
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        if (ContainsEscapingExit(ds, insideLoop, insideSwitch: true)) return true;
                return false;
            case Stmt.LabeledStatement ls:
                return ContainsEscapingExit(ls.Statement, insideLoop, insideSwitch);
            case Stmt.TryCatch t:
                if (ContainsEscapingExit2(t.TryBlock, insideLoop, insideSwitch)) return true;
                if (t.CatchBlock != null && ContainsEscapingExit2(t.CatchBlock, insideLoop, insideSwitch)) return true;
                if (t.FinallyBlock != null && ContainsEscapingExit2(t.FinallyBlock, insideLoop, insideSwitch)) return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>Any statement in <paramref name="statements"/> contains an escaping exit (see <see cref="ContainsEscapingExit"/>).</summary>
    protected static bool ContainsEscapingExit2(List<Stmt> statements, bool insideLoop, bool insideSwitch)
    {
        foreach (var s in statements)
            if (ContainsEscapingExit(s, insideLoop, insideSwitch)) return true;
        return false;
    }

    /// <summary>
    /// Emits a switch statement.
    /// </summary>
    protected virtual void EmitSwitch(Stmt.Switch s)
    {
        var endLabel = IL.DefineLabel();
        var defaultLabel = IL.DefineLabel();
        var caseLabels = s.Cases.Select(_ => IL.DefineLabel()).ToList();

        // Evaluate subject
        EmitExpression(s.Subject);
        EnsureBoxed();
        var subjectLocal = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Call, Ctx.Runtime!.Equals);
            IL.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        // Jump to default or end
        if (s.DefaultBody == null)
            IL.Emit(OpCodes.Br, endLabel);
        else
            IL.Emit(OpCodes.Br, defaultLabel);

        // Register the switch end as the current break target so nested breaks
        // (inside blocks, if/else, try/catch, etc.) exit the switch. Preserve the
        // outer loop's continue target so `continue;` still propagates outward.
        var outerContinue = CurrentLoop?.ContinueLabel ?? endLabel;
        EnterLoop(endLabel, outerContinue);
        try
        {
            // Emit case bodies
            for (int i = 0; i < s.Cases.Count; i++)
            {
                IL.MarkLabel(caseLabels[i]);
                foreach (var stmt in s.Cases[i].Body)
                {
                    if (stmt is Stmt.Break breakStmt && breakStmt.Label == null)
                        IL.Emit(OpCodes.Br, endLabel);
                    else
                        EmitStatement(stmt);
                }
            }

            // Default case
            if (s.DefaultBody != null)
            {
                IL.MarkLabel(defaultLabel);
                foreach (var stmt in s.DefaultBody)
                {
                    if (stmt is Stmt.Break breakStmt && breakStmt.Label == null)
                        IL.Emit(OpCodes.Br, endLabel);
                    else
                        EmitStatement(stmt);
                }
            }
        }
        finally
        {
            ExitLoop();
        }

        IL.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits a throw statement.
    /// </summary>
    protected virtual void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateException);
        IL.Emit(OpCodes.Throw);
    }

    #endregion

    #region Loop Variable Helpers

    /// <summary>
    /// Declares a loop variable. If the variable is hoisted to a state machine field
    /// (detected via <see cref="GetHoistedVariableField"/>), returns null.
    /// Otherwise declares a local and registers it via <see cref="RegisterLoopLocal"/>.
    /// </summary>
    protected virtual LocalBuilder? DeclareLoopVariable(string name)
    {
        if (GetHoistedVariableField(name) != null)
            return null;

        var local = IL.DeclareLocal(Types.Object);
        RegisterLoopLocal(name, local);
        return local;
    }

    /// <summary>
    /// Registers a newly declared loop local variable for later lookup.
    /// Override to use a different local variable store (e.g. a private dictionary).
    /// </summary>
    protected virtual void RegisterLoopLocal(string name, LocalBuilder local)
    {
        Ctx.Locals.RegisterLocal(name, local);
    }

    /// <summary>
    /// Stores a value into a loop variable. If the variable is hoisted to a state machine
    /// field, emits a safe store through a temp local to avoid stack ordering issues.
    /// </summary>
    protected virtual void EmitStoreLoopVariable(LocalBuilder? local, string name, Action emitValue)
    {
        var field = GetHoistedVariableField(name);
        if (field != null)
        {
            // Use temp local to avoid stack corruption:
            // emitValue() may have complex stack effects, so we evaluate first,
            // then load 'this' and store to field.
            var temp = IL.DeclareLocal(Types.Object);
            emitValue();
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, field);
        }
        else if (local != null)
        {
            emitValue();
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    #endregion

    #region Class Expressions

    private void EmitStateMachineClassDeclaration(Stmt.Class classStmt)
    {
        TypeBuilder? builder = null;
        if (Ctx.BlockScopedClassBuilders?.TryGetValue(classStmt, out var scopedBuilder) == true)
            builder = scopedBuilder;
        else
            Ctx.Classes.TryGetValue(Ctx.GetQualifiedClassName(classStmt.Name.Lexeme), out builder);
        if (builder == null)
            return;

        EmitClassHeritageExpression(classStmt.SuperclassExpr, classStmt.Name.Lexeme);
        IL.Emit(OpCodes.Ldtoken, builder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.RunClassDefinitionMethod);

        if (Ctx.DeferredComputedClassKeys?.TryGetValue(classStmt, out var deferred) == true)
            EmitDeferredComputedKeys(deferred.Method, deferred.Keys);

        var field = GetHoistedVariableField(classStmt.Name.Lexeme);
        if (field != null)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldtoken, builder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Stfld, field);
            return;
        }

        var local = Ctx.Locals.GetLocal(classStmt.Name.Lexeme)
            ?? Ctx.Locals.DeclareLocal(classStmt.Name.Lexeme, Types.Object, classStmt);
        IL.Emit(OpCodes.Ldtoken, builder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Stloc, local);
    }

    private void EmitDeferredComputedKeys(MethodBuilder method, IReadOnlyList<Expr> keys)
    {
        var values = new List<LocalBuilder>(keys.Count);
        foreach (var key in keys)
        {
            EmitExpression(key);
            EnsureBoxed();
            values.Add(_helpers.SpillStoreObject());
        }

        IL.Emit(OpCodes.Ldc_I4, values.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < values.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, values[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Call, method);
    }

    /// <summary>
    /// Default implementation for class expressions.
    /// Loads the pre-defined TypeBuilder as a Type object at runtime.
    /// </summary>
    protected override void EmitClassExpression(Expr.ClassExpr ce)
    {
        if (Ctx?.ClassExprBuilders != null && Ctx.ClassExprBuilders.TryGetValue(ce, out var typeBuilder))
        {
            EmitClassHeritageExpression(ce.SuperclassExpr, ce.Name?.Lexeme);
            IL.Emit(OpCodes.Ldtoken, typeBuilder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.RunClassDefinitionMethod);
            if (Ctx.DeferredComputedClassExprKeys?.TryGetValue(ce, out var deferred) == true)
                EmitDeferredComputedKeys(deferred.Method, deferred.Keys);
            IL.Emit(OpCodes.Ldtoken, typeBuilder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            SetStackUnknown();
        }
        else
        {
            // Fallback: push null (should not happen if collection worked)
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
    }

    #endregion

    #region Delete Expression

    /// <summary>
    /// Default implementation for delete expressions.
    /// Handles strict mode: throws SyntaxError for variable deletion, TypeError for frozen/sealed objects.
    /// </summary>
    protected override void EmitDelete(Expr.Delete del)
    {
        // delete operator: returns boolean
        // - delete obj.prop: removes property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete obj[key]: removes computed property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete variable: throws SyntaxError in strict mode, returns false in sloppy mode
        Expr operand = del.Operand;
        while (operand is Expr.Grouping grouping)
            operand = grouping.Expression;
        switch (operand)
        {
            case Expr.Get get:
                // delete obj.prop - use static runtime helper with strict mode
                EmitExpression(get.Object);
                EnsureBoxed();
                IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
                if (Ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.DeletePropertyStrict);
                }
                else
                {
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.DeleteProperty);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.GetIndex getIndex:
                // delete obj[key] - use DeleteIndex with strict mode
                EmitExpression(getIndex.Object);
                EnsureBoxed();
                EmitExpression(getIndex.Index);
                EnsureBoxed();
                if (Ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.DeleteIndexStrict);
                }
                else
                {
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.DeleteIndex);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.Variable v:
                if (Ctx.IsStrictMode)
                {
                    // Strict mode: throw SyntaxError
                    IL.Emit(OpCodes.Ldstr, $"Delete of unqualified identifier '{v.Name.Lexeme}' in strict mode");
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.ThrowStrictSyntaxError);
                    // ThrowStrictSyntaxError throws, but we need a value on stack for IL verification
                    EmitBoolConstant(false);
                }
                else
                {
                    if (!IsKnownVariable(v.Name.Lexeme))
                        EmitBoolConstant(true);
                    else
                    {
                        IL.Emit(OpCodes.Ldstr, v.Name.Lexeme);
                        IL.Emit(OpCodes.Call, Ctx.Runtime!.WarnSloppyDeleteVariable);
                    }
                }
                SetStackType(StackType.Boolean);
                break;

            default:
                // delete on other expressions: returns true but does nothing
                // Still need to evaluate for side effects
                EmitExpression(operand);
                IL.Emit(OpCodes.Pop);
                EmitBoolConstant(true);
                break;
        }
    }

    #endregion
}
