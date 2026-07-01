using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Shared base for the state-machine MoveNext emitters: the async-function pair (via
/// <see cref="AsyncFunctionMoveNextEmitter"/>), <see cref="GeneratorMoveNextEmitter"/> and
/// <see cref="AsyncGeneratorMoveNextEmitter"/>. Holds the exit-scope stack and the non-local-exit
/// (return / break / continue) finally-routing scaffolding every state machine needs.
/// </summary>
/// <remarks>
/// A non-local exit that crosses a flag-based try/finally must run the enclosing finally(s) before
/// transferring control; because a finally can itself suspend (await or yield), the routing state has
/// to survive a MoveNext re-entry, so it lives in state-machine fields rather than IL locals.
///
/// Only the machinery common to all four emitters lives here. Each keeps its own suspension-specific
/// try/catch body, its completion, and (generators only) throw routing. The one routing seam that
/// differs is the Leave-vs-Br choice for a routed break/continue: the async functions key it off
/// <see cref="CompilationContext.ExceptionBlockDepth"/> (the default <see cref="ProtectedRegionDepth"/>),
/// the generators track their own protected-region counter and override it. <c>ExitCodeThrow</c> is
/// reserved here for all emitters; the async functions never use it (they have no throw routing),
/// which is harmless.
/// </remarks>
public abstract partial class StateMachineExitRoutingEmitter : StatementEmitterBase
{
    protected StateMachineExitRoutingEmitter(StateMachineEmitHelpers helpers)
        : base(helpers)
    {
    }

    /// <summary>
    /// Defines a private field on the concrete state-machine type. The routing needs fields that
    /// survive a MoveNext re-entry, not IL locals.
    /// </summary>
    protected abstract FieldBuilder DefineStateMachineField(string name, System.Type type);

    /// <summary>
    /// The count of real IL exception blocks open around the current emission point, used to choose
    /// <c>Leave</c> vs <c>Br</c> for a routed break/continue. Defaults to
    /// <see cref="CompilationContext.ExceptionBlockDepth"/>; the generators override it with their own
    /// protected-region counter (which is independent of ExceptionBlockDepth).
    /// </summary>
    protected virtual int ProtectedRegionDepth => Ctx.ExceptionBlockDepth;

    protected interface IExitScope;

    /// <summary>A loop (or switch / labeled statement) that <c>break</c>/<c>continue</c> can target.</summary>
    protected sealed class LoopScope : IExitScope
    {
        public required Label BreakLabel;
        public required Label ContinueLabel;
        public IReadOnlyList<string> LabelNames = CompilationContext.NoLabels;

        // Lazily assigned the first time a break/continue to this loop must run an intervening
        // finally; identifies that exit in the `<>pendingExit` field. Unset while the loop's
        // break/continue need no finally routing (the common case → a direct branch).
        public int? BreakCode;
        public int? ContinueCode;
    }

    /// <summary>A flag-based <c>try</c> that has a <c>finally</c>; its finally runs on any exit that crosses it.</summary>
    protected sealed class FinallyScope : IExitScope
    {
        public required Label CleanupLabel;

        // code → where control goes after this finally for that pending exit: a chain to the next
        // outer finally's cleanup label, or null meaning "this finally is terminal for the exit".
        public readonly Dictionary<int, Label?> Dispatch = [];
    }

    // Innermost-last stack of loop and finally scopes currently open at the emission point. Replaces
    // the base class's `_loopLabels` (the loop-scope methods below are overridden to use this) so loops
    // and finallys share one ordering — needed to know which finallys lie between a break and its loop.
    protected readonly List<IExitScope> _exitScopes = [];

    protected const int ExitCodeReturn = 1;
    protected const int ExitCodeThrow = 2;
    protected int _nextExitCode = 3; // break/continue (and generator throw-into-enclosing) codes allocated from here

    // code → emits the terminal action for that code (invoked when a finally is terminal for it).
    protected readonly Dictionary<int, System.Action> _exitTerminals = [];

    // `<>pendingExit` (int): the code of an in-flight non-local exit, or 0 when none. A finally that
    // suspends (awaits/yields) suspends MoveNext mid-routing, so this must be a field (a local would
    // reset on re-entry). Set once per exit and cleared by the terminal dispatch; defaults to 0.
    private FieldBuilder? _pendingExitField;

    protected FieldBuilder GetPendingExitField() =>
        _pendingExitField ??= DefineStateMachineField("<>pendingExit", typeof(int));

    // `<>pendingReturnValue` (object): the completion value of a `return` being routed through
    // finally(s). The value is stashed here at the `return` and restored by the return terminal, after
    // the finally has run — held across any suspension in those finallys (an IL local would not survive
    // the MoveNext re-entry).
    private FieldBuilder? _pendingReturnValueField;

    protected FieldBuilder GetPendingReturnValueField() =>
        _pendingReturnValueField ??= DefineStateMachineField("<>pendingReturnValue", typeof(object));

    // ---- Loop-scope methods (override the base stack to use `_exitScopes`) -----------------------

    protected override void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
        // Adopt any pending labels (`outer: for (...)`, or a chain `a: b: for`) just like the base
        // EnterLoop does, so a labeled `break`/`continue` can resolve this loop as its target.
        => _exitScopes.Add(new LoopScope { BreakLabel = breakLabel, ContinueLabel = continueLabel, LabelNames = labelName != null ? new[] { labelName } : Ctx.TakePendingLoopLabels() });

    protected override void ExitLoop()
    {
        // Well-nested: any try opened inside the loop body has already been closed, so the top of the
        // stack is this loop's LoopScope.
        _exitScopes.RemoveAt(_exitScopes.Count - 1);
    }

    protected override (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? CurrentLoop
    {
        get
        {
            var loop = CurrentLoopScope();
            return loop == null ? null : (loop.BreakLabel, loop.ContinueLabel, loop.LabelNames);
        }
    }

    protected override (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? FindLabeledLoop(string labelName)
    {
        var loop = FindLabeledLoopScope(labelName);
        return loop == null ? null : (loop.BreakLabel, loop.ContinueLabel, loop.LabelNames);
    }

    protected LoopScope? CurrentLoopScope()
    {
        for (int i = _exitScopes.Count - 1; i >= 0; i--)
            if (_exitScopes[i] is LoopScope ls)
                return ls;
        return null;
    }

    protected LoopScope? FindLabeledLoopScope(string labelName)
    {
        for (int i = _exitScopes.Count - 1; i >= 0; i--)
            if (_exitScopes[i] is LoopScope ls && ls.LabelNames.Contains(labelName))
                return ls;
        return null;
    }

    // ---- Non-local exit overrides ---------------------------------------------------------------

    /// <summary>
    /// A <c>break</c> must run any finally between it and the target loop before jumping out.
    /// </summary>
    protected override void EmitBreak(Stmt.Break b)
    {
        var loop = b.Label != null ? FindLabeledLoopScope(b.Label.Lexeme) : CurrentLoopScope();
        if (loop == null) return; // matches the base: a break with no resolvable target is a no-op

        var chain = FinallyFramesAbove(loop);
        if (chain.Count > 0)
        {
            // Flag-based finally(s) lie between the break and its loop; run them before jumping out.
            int code = loop.BreakCode ??= _nextExitCode++;
            _exitTerminals.TryAdd(code, () => IL.Emit(OpCodes.Br, loop.BreakLabel));
            RouteThroughFinallys(chain, code, ProtectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
            return;
        }

        EmitBranchToLabel(loop.BreakLabel);
    }

    /// <summary>
    /// A <c>continue</c> must run any finally between it and the target loop before jumping to the
    /// loop's continue point.
    /// </summary>
    protected override void EmitContinue(Stmt.Continue c)
    {
        var loop = c.Label != null ? FindLabeledLoopScope(c.Label.Lexeme) : CurrentLoopScope();
        if (loop == null) return;

        var chain = FinallyFramesAbove(loop);
        if (chain.Count > 0)
        {
            int code = loop.ContinueCode ??= _nextExitCode++;
            _exitTerminals.TryAdd(code, () => IL.Emit(OpCodes.Br, loop.ContinueLabel));
            RouteThroughFinallys(chain, code, ProtectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
            return;
        }

        EmitBranchToLabel(loop.ContinueLabel);
    }

    /// <summary>
    /// Branch out to <paramref name="target"/>. Inside a real IL exception block a <c>br</c> out is
    /// illegal, so use <c>Leave</c> — which exits the block legally and runs its (no-suspension)
    /// finally. <see cref="CompilationContext.ExceptionBlockDepth"/> counts only real blocks
    /// (EmitSimpleTryCatch), not the flag-based path's sync segments, so a branch internal to a segment
    /// stays a legal <c>Br</c> and does not illegally leave the mini try/catch.
    /// </summary>
    protected override void EmitBranchToLabel(Label target)
    {
        if (Ctx.ExceptionBlockDepth > 0)
            IL.Emit(OpCodes.Leave, target);
        else
            IL.Emit(OpCodes.Br, target);
    }

    // ---- Routing helpers ------------------------------------------------------------------------

    /// <summary>All open finally scopes, innermost first.</summary>
    protected List<FinallyScope> ActiveFinallyFrames()
    {
        var result = new List<FinallyScope>();
        for (int i = _exitScopes.Count - 1; i >= 0; i--)
            if (_exitScopes[i] is FinallyScope fs)
                result.Add(fs);
        return result;
    }

    /// <summary>The finally scopes between the current point and <paramref name="target"/>, innermost first.</summary>
    protected List<FinallyScope> FinallyFramesAbove(LoopScope target)
    {
        var result = new List<FinallyScope>();
        for (int i = _exitScopes.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_exitScopes[i], target)) break;
            if (_exitScopes[i] is FinallyScope fs)
                result.Add(fs);
        }
        return result;
    }

    /// <summary>
    /// The finally scopes strictly inside the flag-based try whose body began at <paramref
    /// name="scopeDepth"/> (= <c>_exitScopes.Count</c> at that point), innermost first. Excludes the
    /// try's own finally (which lives just below scopeDepth) and everything outside it — the finallys a
    /// throw escaping a nested handler must run before reaching that try's catch (#632). Shared by every
    /// state-machine family (each threw a byte-identical copy before #1123).
    /// </summary>
    protected List<FinallyScope> FinallyFramesInside(int scopeDepth)
    {
        var result = new List<FinallyScope>();
        for (int i = _exitScopes.Count - 1; i >= scopeDepth; i--)
            if (_exitScopes[i] is FinallyScope fs)
                result.Add(fs);
        return result;
    }

    /// <summary>
    /// Records <paramref name="code"/>'s per-frame routing across <paramref name="chain"/> (each frame
    /// chains to the next, the outermost is terminal), then sets the pending-exit field and branches to
    /// the innermost finally. The caller must have prepared any value the terminal needs (e.g. the
    /// return value) beforehand.
    /// </summary>
    /// <param name="branch">
    /// <c>Br</c> when the exit is at the top level, or <c>Leave</c> when it is emitted inside a real IL
    /// exception block (EmitSimpleTryCatch) nested in the flag-based finally(s): the <c>Leave</c> exits
    /// that block — running its no-suspension finally — straight to the innermost flag cleanup label,
    /// then the flag machinery runs the remaining (outer) finally(s). A real try never encloses a flag
    /// try, so every real finally is innermore than every flag one and this ordering is correct.
    /// </param>
    protected void RouteThroughFinallys(List<FinallyScope> chain, int code, OpCode branch)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            // The last frame in the chain is terminal for this code (null); the rest chain outward.
            Label? next = i < chain.Count - 1 ? chain[i + 1].CleanupLabel : null;
            chain[i].Dispatch[code] = next; // idempotent: the same code always routes a frame the same way
        }

        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4, code);
        IL.Emit(OpCodes.Stfld, GetPendingExitField());
        IL.Emit(branch, chain[0].CleanupLabel);
    }

    /// <summary>
    /// Emitted after a finally body: for each pending-exit code that routes through this frame, either
    /// chain to the next outer finally or, if terminal here, clear the pending field and run the
    /// terminal action. A pending value of 0 (none) and codes not handled here fall through unchanged.
    /// </summary>
    protected void EmitFinallyDispatch(FinallyScope frame)
    {
        foreach (var (code, next) in frame.Dispatch)
        {
            var skip = IL.DefineLabel();
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, GetPendingExitField());
            IL.Emit(OpCodes.Ldc_I4, code);
            IL.Emit(OpCodes.Bne_Un, skip);

            if (next.HasValue)
            {
                // Not terminal here — keep the pending code and run the next outer finally.
                IL.Emit(OpCodes.Br, next.Value);
            }
            else
            {
                // Terminal: the exit has run every finally it needed to. Clear the flag first so a
                // break/continue resumes with clean state.
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Stfld, GetPendingExitField());
                _exitTerminals[code]();
            }

            IL.MarkLabel(skip);
        }
    }
}
