using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

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
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="CompilationContext"/>
public partial class ILEmitter
{
    private readonly CompilationContext _ctx;
    private ILGenerator IL => _ctx.IL;

    public ILEmitter(CompilationContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Finalize return handling for methods that had returns inside exception blocks.
    /// Must be called after emitting the method body but before the final Ret.
    /// </summary>
    public void FinalizeReturns()
    {
        if (_ctx.ReturnValueLocal != null)
        {
            // Mark the return label and emit the actual return
            IL.MarkLabel(_ctx.ReturnLabel);
            IL.Emit(OpCodes.Ldloc, _ctx.ReturnValueLocal);
            IL.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Check if the method had returns inside exception blocks that need finalization.
    /// </summary>
    public bool HasDeferredReturns => _ctx.ReturnValueLocal != null;

    /// <summary>
    /// Emit default parameter value checks at function entry.
    /// For each parameter with a default value, checks if arg is null and assigns default.
    /// </summary>
    public void EmitDefaultParameters(List<Stmt.Parameter> parameters, bool isInstanceMethod)
    {
        int argOffset = isInstanceMethod ? 1 : 0;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null) continue;

            int argIndex = i + argOffset;

            // if (arg == null) { arg = <default>; }
            var skipDefault = IL.DefineLabel();

            // Load argument and check if null
            IL.Emit(OpCodes.Ldarg, argIndex);
            IL.Emit(OpCodes.Brtrue, skipDefault);

            // Argument is null, emit default value and store
            EmitExpression(param.DefaultValue);
            EmitBoxIfNeeded(param.DefaultValue);
            IL.Emit(OpCodes.Starg, argIndex);

            IL.MarkLabel(skipDefault);
        }
    }

    public void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // Pop result if expression leaves a value on stack
                if (!IsVoidExpression(e.Expr))
                {
                    IL.Emit(OpCodes.Pop);
                }
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

            case Stmt.Break:
                EmitBreak();
                break;

            case Stmt.Continue:
                EmitContinue();
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
                // Handled at top level / compile-time only
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

            default:
                // Fallback: push null
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    public void EmitBoxIfNeeded(Expr expr)
    {
        // Box value types
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double)
            {
                IL.Emit(OpCodes.Box, typeof(double));
            }
            else if (lit.Value is bool)
            {
                IL.Emit(OpCodes.Box, typeof(bool));
            }
        }
        // Note: Binary expressions already box their result in EmitBinary
        // Note: Comparison expressions already box in EmitBinary
    }

    private void EmitExpressionAsDouble(Expr expr)
    {
        // Emit expression and ensure result is a double on the stack
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            // Literal double - push directly
            IL.Emit(OpCodes.Ldc_R8, d);
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            IL.Emit(OpCodes.Ldc_R8, (double)i);
        }
        else
        {
            // Other expressions - emit and convert
            EmitExpression(expr);
            EmitUnboxToDouble();
        }
    }

    private void EmitUnboxToDouble()
    {
        // Convert object to double using Convert.ToDouble
        IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
    }

    private bool IsStringExpression(Expr expr)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value is string,
            Expr.TemplateLiteral => true,
            Expr.Binary bin when bin.Operator.Type == TokenType.PLUS =>
                IsStringExpression(bin.Left) || IsStringExpression(bin.Right),
            _ => false
        };
    }

    private void EmitTruthyCheck()
    {
        // Truthy check for boxed value:
        // - null => false
        // - boxed false => false
        // - everything else => true
        var checkBoolLabel = IL.DefineLabel();
        var falseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Check for null
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brfalse, falseLabel);

        // Check if it's a boolean
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, typeof(bool));
        IL.Emit(OpCodes.Brfalse, checkBoolLabel);

        // It's a boxed bool - unbox and use the value
        IL.Emit(OpCodes.Unbox_Any, typeof(bool));
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(checkBoolLabel);
        // Not null and not bool - always truthy
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(falseLabel);
        // Null - false
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_0);

        IL.MarkLabel(endLabel);
    }

    private static bool IsNumericOp(TokenType op) =>
        op is TokenType.MINUS or TokenType.STAR or TokenType.SLASH or TokenType.PERCENT;

    private static bool IsComparisonOp(TokenType op) =>
        op is TokenType.LESS or TokenType.GREATER or TokenType.LESS_EQUAL or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL;

    private static bool IsVoidExpression(Expr expr)
    {
        // These expressions leave a value on the stack and should be popped
        // when used as statements. So they're NOT void.
        // Only Call expressions that don't return a value are void.
        // For now, return false for most expressions to be safe.
        return false;
    }
}
