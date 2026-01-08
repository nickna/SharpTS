using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
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

    public void EmitBoxIfNeeded(Expr expr)
    {
        // First, check if we already have an unboxed value type on the stack
        // This handles typed locals and other cases where _stackType is known
        if (_stackType == StackType.Double)
        {
            EmitBoxDouble();
            return;
        }
        if (_stackType == StackType.Boolean)
        {
            EmitBoxBool();
            return;
        }

        // Optimization: Use TypeMap to skip boxing check for known reference types
        // This avoids the pattern match overhead for expressions that definitely don't need boxing
        TypeInfo? type = _ctx.TypeMap?.Get(expr);
        if (type != null)
        {
            // Reference types never need boxing - skip the literal check entirely
            if (type is TypeInfo.Primitive { Type: TokenType.TYPE_STRING }
                or TypeInfo.Array
                or TypeInfo.Instance
                or TypeInfo.Record
                or TypeInfo.Class
                or TypeInfo.Interface
                or TypeInfo.Function
                or TypeInfo.Void
                or TypeInfo.Null)
            {
                return;
            }
            // For primitives (number/boolean) and other types (Any, Union, etc.),
            // fall through to the literal check - only literals produce unboxed values
        }

        // Only Expr.Literal with double/bool produces unboxed value types on the stack.
        // All other expressions (Variable, Call, Binary, etc.) already produce boxed objects.
        // IMPORTANT: Never add boxing for non-literals - their results are already boxed,
        // and Box(double) on an object reference causes garbage output.
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double)
            {
                EmitBoxDouble();
            }
            else if (lit.Value is bool)
            {
                EmitBoxBool();
            }
        }
    }

    private void EmitExpressionAsDouble(Expr expr)
    {
        // Emit expression and ensure result is a double on the stack
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            // Literal double - push directly
            EmitDoubleConstant(d);
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            EmitDoubleConstant((double)i);
        }
        else
        {
            // Other expressions - emit and convert if needed
            EmitExpression(expr);
            EnsureDouble();
        }
    }

    private void EmitUnboxToDouble()
    {
        // Convert object to double using Convert.ToDouble
        EmitConvertToDouble();
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
        IL.Emit(OpCodes.Isinst, _ctx.Types.Boolean);
        IL.Emit(OpCodes.Brfalse, checkBoolLabel);

        // It's a boxed bool - unbox and use the value
        IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
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

    private static bool IsComparisonOp(TokenType op) =>
        op is TokenType.LESS or TokenType.GREATER or TokenType.LESS_EQUAL or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL;
}
