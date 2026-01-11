using System.Reflection.Emit;
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
public partial class ILEmitter
{
    private readonly CompilationContext _ctx;
    private ILGenerator IL => _ctx.IL;

    /// <summary>
    /// Current type on top of the IL evaluation stack.
    /// Used for unboxed numeric optimization.
    /// </summary>
    private StackType _stackType = StackType.Unknown;

    public ILEmitter(CompilationContext ctx)
    {
        _ctx = ctx;
    }

    public void EmitStatement(Stmt stmt)
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
                EmitBreak(breakStmt.Label?.Lexeme);
                break;

            case Stmt.Continue continueStmt:
                EmitContinue(continueStmt.Label?.Lexeme);
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

    public void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;

            case Expr.Variable v:
                EmitVariable(v);
                break;

            case Expr.Assign a:
                EmitAssign(a);
                break;

            case Expr.Binary b:
                EmitBinary(b);
                break;

            case Expr.Logical l:
                EmitLogical(l);
                break;

            case Expr.Unary u:
                EmitUnary(u);
                break;

            case Expr.Grouping g:
                EmitExpression(g.Expression);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.New n:
                EmitNew(n);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Set s:
                EmitSet(s);
                break;

            case Expr.GetIndex gi:
                EmitGetIndex(gi);
                break;

            case Expr.SetIndex si:
                EmitSetIndex(si);
                break;

            case Expr.This:
                EmitThis();
                break;

            case Expr.ArrayLiteral a:
                EmitArrayLiteral(a);
                break;

            case Expr.ObjectLiteral o:
                EmitObjectLiteral(o);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
                break;

            case Expr.NullishCoalescing nc:
                EmitNullishCoalescing(nc);
                break;

            case Expr.TemplateLiteral tl:
                EmitTemplateLiteral(tl);
                break;

            case Expr.CompoundAssign ca:
                EmitCompoundAssign(ca);
                break;

            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;

            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;

            case Expr.PrefixIncrement pi:
                EmitPrefixIncrement(pi);
                break;

            case Expr.PostfixIncrement pi:
                EmitPostfixIncrement(pi);
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.Super s:
                EmitSuper(s);
                break;

            case Expr.Spread sp:
                // Spread expressions are handled in context (arrays, objects, calls)
                // If we get here directly, just emit the inner expression
                EmitExpression(sp.Expression);
                break;

            case Expr.TypeAssertion ta:
                // Type assertions are compile-time only, just emit the inner expression
                EmitExpression(ta.Expression);
                break;

            case Expr.RegexLiteral re:
                EmitRegexLiteral(re);
                break;

            case Expr.DynamicImport di:
                EmitDynamicImport(di);
                break;

            default:
                // Fallback: push null
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }
}
