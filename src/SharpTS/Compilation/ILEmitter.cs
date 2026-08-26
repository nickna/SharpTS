using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits IL instructions for AST statements and expressions.
/// </summary>
/// <remarks>
/// Core code generation component used by <see cref="ILCompiler"/>. Traverses AST nodes
/// and emits corresponding IL opcodes via <see cref="ILGenerator"/>. Handles all expression
/// types (literals, binary ops, calls, property access) and statement types (if, while,
/// try/catch, return). Uses <see cref="CompilationContext"/> to track locals, parameters,
/// and the current <see cref="ILGenerator"/>. Supports closures via display class field access.
///
/// This class is split across multiple partial files:
/// - ILEmitter.cs: Core infrastructure and dispatchers
/// - ILEmitter.StackTracking.cs: Stack type tracking and IL helper methods
/// - ILEmitter.Helpers.cs: Boxing, return handling, and utility methods
/// - ILEmitter.ValueTypes.cs: Value type handling (unboxing, address loading, result boxing)
/// - ILEmitter.Modules.cs: Import/export and module support
/// - ILEmitter.Statements.cs: Statement emission
/// - ILEmitter.Expressions.cs: Expression emission
/// - ILEmitter.Operators.cs: Operator emission
/// - ILEmitter.Properties.cs: Property/member access emission
/// - ILEmitter.Calls.cs: Call emission (+ sub-files)
/// - ILEmitter.Namespaces.cs: Namespace handling
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="CompilationContext"/>
public partial class ILEmitter : StatementEmitterBase, IEmitterContext
{
    private readonly CompilationContext _ctx;
    private readonly LocalVariableResolver _resolver;
    private readonly Stack<AbruptCompletionScope> _abruptCompletionScopes = [];
    private readonly Stack<IteratorLoopCompletionScope> _iteratorLoopCompletionScopes = [];
    private readonly Stack<LocalThrowScope> _localThrowScopes = [];

    private sealed class AbruptCompletionScope
    {
        private int _nextBranchCode = 3;

        public required Label RunFinally { get; init; }
        public required LocalBuilder Kind { get; init; }
        public required LocalBuilder Exception { get; init; }
        public required HashSet<Label> EnclosingTargets { get; init; }
        public Dictionary<Label, int> BranchCodes { get; } = [];

        public int GetBranchCode(Label target)
        {
            if (BranchCodes.TryGetValue(target, out int code)) return code;
            code = _nextBranchCode++;
            BranchCodes[target] = code;
            return code;
        }
    }

    private sealed record IteratorLoopCompletionScope(
        LocalBuilder CloseNeeded,
        Label ContinueTarget,
        HashSet<Label> EscapingTargets);

    private sealed record LocalThrowScope(
        LocalBuilder Value,
        Label CatchBody,
        int AbruptCompletionDepth,
        int IteratorCompletionDepth);

    // Abstract property implementations for ExpressionEmitterBase
    protected override ILGenerator IL => _ctx.IL;
    protected override CompilationContext Ctx => _ctx;
    protected override TypeProvider Types => _ctx.Types;
    protected override IVariableResolver Resolver => _resolver;

    /// <summary>
    /// Provides access to the compilation context for type emitter strategies.
    /// </summary>
    public CompilationContext Context => _ctx;

    /// <summary>
    /// Provides public access to the IL generator for call handlers.
    /// </summary>
    public ILGenerator ILGen => _ctx.IL;

    /// <summary>
    /// Provides access to the IL generator via IEmitterContext.
    /// </summary>
    ILGenerator IEmitterContext.IL => _ctx.IL;

    /// <summary>
    /// Marks the stack as containing an unknown/object type.
    /// Part of IEmitterContext interface for type emitter strategies.
    /// </summary>
    void IEmitterContext.SetStackUnknown() => _helpers.SetStackUnknown();

    /// <summary>
    /// Marks the stack as containing a specific type.
    /// Part of IEmitterContext interface for type emitter strategies.
    /// </summary>
    void IEmitterContext.SetStackType(StackType type) => _helpers.SetStackType(type);

    /// <summary>
    /// Provides access to the emit helpers for call handlers.
    /// </summary>
    public StateMachineEmitHelpers Helpers => _helpers;

    /// <summary>
    /// Current type on top of the IL evaluation stack.
    /// Used for unboxed numeric optimization.
    /// Delegates to the shared helpers instance for consistency.
    /// </summary>
    private StackType _stackType
    {
        get => _helpers.StackType;
        set => _helpers.StackType = value;
    }

    public ILEmitter(CompilationContext ctx)
        : base(new StateMachineEmitHelpers(ctx.IL, ctx.Types, ctx.ILBuilder, ctx.Runtime))
    {
        _ctx = ctx;
        _resolver = new LocalVariableResolver(ctx.IL, ctx, ctx.Types);
    }

    #region StatementEmitterBase Abstract Implementations - Loop Labels

    protected override void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
        => _ctx.EnterLoop(breakLabel, continueLabel, labelName);

    protected override void ExitLoop()
        => _ctx.ExitLoop();

    protected override (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? CurrentLoop
        => _ctx.CurrentLoop;

    protected override (Label BreakLabel, Label ContinueLabel, IReadOnlyList<string> LabelNames)? FindLabeledLoop(string labelName)
        => _ctx.FindLabeledLoop(labelName);

    #endregion

    #region StatementEmitterBase Overrides

    protected override bool IsDead(Stmt stmt)
        => _ctx.DeadCode?.IsDead(stmt) == true;

    protected override void EmitBranchToLabel(Label target)
    {
        // Use builder for branch validation - it enforces Leave vs Br rules
        var builder = _ctx.ILBuilder;
        if (_abruptCompletionScopes.TryPeek(out var completion))
        {
            // A loop/switch introduced inside this try remains inside the protected
            // region. Only a target that existed on entry escapes and therefore
            // needs to run the JavaScript finally block.
            if (completion.EnclosingTargets.Contains(target))
            {
                IL.Emit(OpCodes.Ldc_I4, completion.GetBranchCode(target));
                IL.Emit(OpCodes.Stloc, completion.Kind);
                builder.Emit_Leave(completion.RunFinally);
                return;
            }
        }
        if (_iteratorLoopCompletionScopes.TryPeek(out var iteratorCompletion))
        {
            if (iteratorCompletion.EscapingTargets.Contains(target))
            {
                if (target.Equals(iteratorCompletion.ContinueTarget))
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Stloc, iteratorCompletion.CloseNeeded);
                }
                builder.Emit_Leave(target);
                return;
            }

            // The target belongs to a loop/switch nested inside this iteration.
            builder.Emit_Br(target);
            return;
        }
        if (_ctx.ExceptionBlockDepth > 0)
            builder.Emit_Leave(target);
        else
            builder.Emit_Br(target);
    }

    // DeclareLoopVariable uses the base StatementEmitterBase implementation
    // (IL.DeclareLocal + Ctx.Locals.RegisterLocal), which is equivalent.

    #endregion

    protected override void EmitStatementCore(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                if (!TryEmitDiscardedExpression(e.Expr))
                {
                    EmitExpression(e.Expr);
                    // Ordinary expressions leave a value on the stack. A
                    // discarded-expression intrinsic reports success only
                    // after emitting a stack-neutral replacement.
                    IL.Emit(OpCodes.Pop);
                }
                break;

            case Stmt.Var v:
                EmitVariableDeclarationWithLexicalTdz(v);
                break;

            case Stmt.Const c:
                // Const declarations are emitted the same way as var declarations
                EmitVariableDeclarationWithLexicalTdz(
                    new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer));
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
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.Break breakStmt:
                EmitBreak(breakStmt);
                break;

            case Stmt.Continue continueStmt:
                EmitContinue(continueStmt);
                break;

            case Stmt.LabeledStatement labeledStmt:
                EmitLabeledStatement(labeledStmt);
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

            case Stmt.Function fn:
                // A `function` declaration nested in a block/loop/if is materialized in place at its
                // textual position so a closure over a per-iteration binding captures that iteration's
                // value (#1230). Top-level declarations are already hoisted (no-op here); classes and
                // type-only declarations are handled at compile-time.
                _ctx.EmitBlockScopedInnerFunction?.Invoke(IL, _ctx, fn);
                break;

            case Stmt.Class classStmt:
                EmitBlockScopedClassDeclaration(classStmt);
                break;
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
                // Handled at top level / compile-time only
                break;

            case Stmt.Namespace ns:
                EmitNamespace(ns);
                break;

            case Stmt.ImportAlias importAlias:
                EmitImportAlias(importAlias);
                break;

            case Stmt.Import import:
                EmitImport(import);
                break;

            case Stmt.ImportRequire importReq:
                EmitImportRequire(importReq);
                break;

            case Stmt.Export export:
                EmitExport(export);
                break;

            case Stmt.StaticBlock:
                // Static blocks are handled specially in EmitStaticConstructor.
                // If encountered here, it's a no-op (block body already emitted inline).
                break;

            case Stmt.Using u:
                EmitUsingDeclaration(u);
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
                // Directives are handled at parse time; class member declarations
                // are handled within class processing, not emitted directly
                break;

            default:
                throw new CompileException($"Unhandled statement type in ILEmitter: {stmt.GetType().Name}");
        }
    }

    private void EmitVariableDeclarationWithLexicalTdz(Stmt.Var declaration)
    {
        if (declaration.IsVar)
        {
            EmitVarDeclaration(declaration);
            return;
        }

        var saved = _ctx.LexicalInitializerTdzName;
        _ctx.LexicalInitializerTdzName = declaration.Name.Lexeme;
        try
        {
            EmitVarDeclaration(declaration);
        }
        finally
        {
            _ctx.LexicalInitializerTdzName = saved;
        }
    }

    // EmitExpression dispatch is inherited from ExpressionEmitterBase
}
