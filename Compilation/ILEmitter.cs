using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
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
        : base(new StateMachineEmitHelpers(ctx.IL, ctx.Types))
    {
        _ctx = ctx;
        _resolver = new LocalVariableResolver(ctx.IL, ctx, ctx.Types);
    }

    #region StatementEmitterBase Abstract Implementations - Loop Labels

    protected override void EnterLoop(Label breakLabel, Label continueLabel, string? labelName = null)
        => _ctx.EnterLoop(breakLabel, continueLabel, labelName);

    protected override void ExitLoop()
        => _ctx.ExitLoop();

    protected override (Label BreakLabel, Label ContinueLabel, string? LabelName)? CurrentLoop
        => _ctx.CurrentLoop;

    protected override (Label BreakLabel, Label ContinueLabel, string? LabelName)? FindLabeledLoop(string labelName)
        => _ctx.FindLabeledLoop(labelName);

    #endregion

    #region StatementEmitterBase Overrides

    protected override bool IsDead(Stmt stmt)
        => _ctx.DeadCode?.IsDead(stmt) == true;

    protected override void EmitBranchToLabel(Label target)
    {
        // Use Leave instead of Br when inside exception blocks
        if (_ctx.ExceptionBlockDepth > 0)
            IL.Emit(OpCodes.Leave, target);
        else
            IL.Emit(OpCodes.Br, target);
    }

    protected override LocalBuilder? DeclareLoopVariable(string name)
    {
        return _ctx.Locals.DeclareLocal(name, _ctx.Types.Object);
    }

    #endregion

    public override void EmitStatement(Stmt stmt)
    {
        // Skip dead statements (unreachable code)
        if (_ctx.DeadCode?.IsDead(stmt) == true)
            return;

        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // All expressions leave a value on the stack, so pop when used as a statement
                IL.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
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

            case Stmt.ForOf f:
                EmitForOf(f);
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

            case Stmt.Print p:
                EmitPrint(p);
                break;

            case Stmt.Function:
            case Stmt.Class:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
                // Handled at top level / compile-time only
                break;

            case Stmt.Namespace ns:
                EmitNamespace(ns);
                break;

            case Stmt.Import import:
                EmitImport(import);
                break;

            case Stmt.Export export:
                EmitExport(export);
                break;
        }
    }

    // EmitExpression dispatch is inherited from ExpressionEmitterBase
}
