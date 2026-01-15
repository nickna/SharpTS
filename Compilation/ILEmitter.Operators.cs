using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Operator emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitBinary(Expr.Binary b)
    {
        // Check for bigint operations
        if (IsBigIntOperation(b))
        {
            EmitBigIntBinary(b);
            return;
        }

        // Addition: use runtime Add() which handles both string concat and numeric add
        if (b.Operator.Type == TokenType.PLUS)
        {
            EmitExpression(b.Left);
            EmitBoxIfNeeded(b.Left);
            EmitExpression(b.Right);
            EmitBoxIfNeeded(b.Right);
            EmitCallUnknown(_ctx.Runtime!.Add);
            return;
        }

        // Comparison operators (== and ===, != and !==)
        if (b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL)
        {
            EmitExpression(b.Left);
            EmitBoxIfNeeded(b.Left);
            EmitExpression(b.Right);
            EmitBoxIfNeeded(b.Right);

            // Loose equality (== and !=) uses runtime.Equals which treats null==undefined
            // Strict equality (=== and !==) uses Object.Equals which keeps them distinct
            bool isLooseEquality = b.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL;
            bool isNegated = b.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;

            if (isLooseEquality)
            {
                // Use our runtime Equals which treats null == undefined
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Equals);
            }
            else
            {
                // Use Object.Equals for strict equality (null !== undefined)
                EmitObjectEqualsBoxed_NoBox();
            }

            if (isNegated)
            {
                // Negate the result
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
            }

            IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
            SetStackUnknown();
            return;
        }

        // Bitwise operators - require int32 conversion
        // Note: GREATER_GREATER_GREATER (>>>) has a known issue with unsigned conversion
        if (b.Operator.Type is TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET
            or TokenType.LESS_LESS or TokenType.GREATER_GREATER or TokenType.GREATER_GREATER_GREATER)
        {
            EmitBitwiseBinary(b);
            return;
        }

        // instanceof operator
        if (b.Operator.Type == TokenType.INSTANCEOF)
        {
            EmitExpression(b.Left);
            EmitBoxIfNeeded(b.Left);
            EmitExpression(b.Right);
            EmitBoxIfNeeded(b.Right);
            EmitCallAndBoxBool(_ctx.Runtime!.InstanceOf);
            return;
        }

        // Numeric operations - defer boxing until needed
        EmitExpressionAsDouble(b.Left);
        EmitExpressionAsDouble(b.Right);

        switch (b.Operator.Type)
        {
            case TokenType.MINUS:
                EmitSub_Double();
                break;
            case TokenType.STAR:
                EmitMul_Double();
                break;
            case TokenType.SLASH:
                EmitDiv_Double();
                break;
            case TokenType.PERCENT:
                EmitRem_Double();
                break;
            case TokenType.LESS:
                EmitClt_Boolean();
                break;
            case TokenType.GREATER:
                EmitCgt_Boolean();
                break;
            case TokenType.LESS_EQUAL:
                EmitLessOrEqual_Boolean();
                break;
            case TokenType.GREATER_EQUAL:
                EmitGreaterOrEqual_Boolean();
                break;
        }
    }

    private void EmitBitwiseBinary(Expr.Binary b)
    {
        // Convert to int32 for bitwise operations
        EmitExpression(b.Left);
        EmitBoxIfNeeded(b.Left);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToInt32", _ctx.Types.Object));

        EmitExpression(b.Right);
        EmitBoxIfNeeded(b.Right);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToInt32", _ctx.Types.Object));

        switch (b.Operator.Type)
        {
            case TokenType.AMPERSAND:
                IL.Emit(OpCodes.And);
                break;
            case TokenType.PIPE:
                IL.Emit(OpCodes.Or);
                break;
            case TokenType.CARET:
                IL.Emit(OpCodes.Xor);
                break;
            case TokenType.LESS_LESS:
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);  // Mask shift amount to 5 bits
                IL.Emit(OpCodes.Shl);
                break;
            case TokenType.GREATER_GREATER:
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);
                IL.Emit(OpCodes.Shr);
                break;
            case TokenType.GREATER_GREATER_GREATER:
                // Unsigned right shift - requires special handling for signed-to-unsigned conversion
                IL.Emit(OpCodes.Ldc_I4, 0x1F);
                IL.Emit(OpCodes.And);
                IL.Emit(OpCodes.Shr_Un);
                // Convert result as unsigned to double:
                // Extend to unsigned int64 (zero-extend), then convert to double
                IL.Emit(OpCodes.Conv_U8);
                EmitConvR8AndBox();
                return;
        }

        // Convert back to double (for signed operations)
        EmitConvR8AndBox();
    }

    private bool IsComparisonExpr(Expr expr)
    {
        return expr is Expr.Binary b && IsComparisonOp(b.Operator.Type);
    }

    protected override void EmitLogical(Expr.Logical l)
    {
        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("logical_end");

        EmitExpression(l.Left);
        EmitBoxIfNeeded(l.Left);
        IL.Emit(OpCodes.Dup);
        EmitTruthyCheck();

        if (l.Operator.Type == TokenType.AND_AND)
        {
            // Short-circuit: if left is falsy, return left
            builder.Emit_Brfalse(endLabel);
        }
        else // OR
        {
            // Short-circuit: if left is truthy, return left
            builder.Emit_Brtrue(endLabel);
        }

        IL.Emit(OpCodes.Pop); // Pop the duplicate left value
        EmitExpression(l.Right);
        EmitBoxIfNeeded(l.Right);

        builder.MarkLabel(endLabel);
        SetStackUnknown(); // Logical operators return boxed object
    }

    protected override void EmitUnary(Expr.Unary u)
    {
        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                if (IsBigIntExpr(u.Right))
                {
                    // BigInt negation
                    EmitExpression(u.Right);
                    EmitBoxIfNeeded(u.Right);
                    EmitCallUnknown(_ctx.Runtime!.BigIntNegate);
                }
                else
                {
                    EmitExpression(u.Right);
                    // If it's a literal number, it's already unboxed on the stack
                    if (u.Right is Expr.Literal { Value: double })
                    {
                        // Already have unboxed double on stack
                    }
                    else
                    {
                        EmitBoxIfNeeded(u.Right);
                        EmitUnboxToDouble();
                    }
                    IL.Emit(OpCodes.Neg);
                    EmitBoxDouble();
                }
                break;

            case TokenType.BANG:
                EmitExpression(u.Right);
                EmitBoxIfNeeded(u.Right);
                EmitTruthyCheck();
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                EmitBoxBool();
                break;

            case TokenType.TYPEOF:
                EmitExpression(u.Right);
                EmitBoxIfNeeded(u.Right);
                EmitCallString(_ctx.Runtime!.TypeOf);
                break;

            case TokenType.TILDE:
                if (IsBigIntExpr(u.Right))
                {
                    // BigInt bitwise not
                    EmitExpression(u.Right);
                    EmitBoxIfNeeded(u.Right);
                    EmitCallUnknown(_ctx.Runtime!.BigIntBitwiseNot);
                }
                else
                {
                    EmitExpression(u.Right);
                    EmitBoxIfNeeded(u.Right);
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToInt32", _ctx.Types.Object));
                    IL.Emit(OpCodes.Not);
                    EmitConvR8AndBox();
                }
                break;
        }
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        var local = _ctx.Locals.GetLocal(ca.Name.Lexeme);

        // Special case: string concatenation with +=
        if (ca.Operator.Type == TokenType.PLUS_EQUAL && IsStringExpression(ca.Value))
        {
            // Load current value as object
            EmitVariable(new Expr.Variable(ca.Name));

            // Load right side as object
            EmitExpression(ca.Value);
            EmitBoxIfNeeded(ca.Value);

            // String concatenation
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.Object, _ctx.Types.Object));
            IL.Emit(OpCodes.Dup);

            // Store result
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }
            SetStackType(StackType.String);
            return;
        }

        // Numeric compound assignment
        bool isBitwise = CompoundOperatorHelper.IsBitwise(ca.Operator.Type);

        // Get current value
        EmitVariable(new Expr.Variable(ca.Name));
        EmitUnboxToDouble();

        if (isBitwise)
        {
            // Convert to int for bitwise operations
            IL.Emit(OpCodes.Conv_I4);
            EmitExpressionAsDouble(ca.Value);
            IL.Emit(OpCodes.Conv_I4);
        }
        else
        {
            // Emit right side as double
            EmitExpressionAsDouble(ca.Value);
        }

        // Emit the operator using centralized helper
        var opcode = CompoundOperatorHelper.GetOpcode(ca.Operator.Type);
        if (opcode.HasValue)
        {
            IL.Emit(opcode.Value);
        }

        if (isBitwise)
        {
            // Convert back to double
            IL.Emit(OpCodes.Conv_R8);
        }

        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Dup);

        // Store result
        if (local != null)
        {
            IL.Emit(OpCodes.Stloc, local);
        }
        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            EmitVariable(v);
            EmitUnboxToDouble();

            if (pi.Operator.Type == TokenType.PLUS_PLUS)
            {
                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(OpCodes.Add);
            }
            else
            {
                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(OpCodes.Sub);
            }

            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            IL.Emit(OpCodes.Dup);

            var local = _ctx.Locals.GetLocal(v.Name.Lexeme);
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }
            SetStackUnknown();
            return;
        }

        if (pi.Operand is Expr.Get get)
        {
            // Prefix increment on property: ++obj.prop
            // Get current value
            EmitExpression(get.Object);
            EmitBoxIfNeeded(get.Object);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            EmitUnboxToDouble();

            // Increment or decrement
            IL.Emit(OpCodes.Ldc_R8, 1.0);
            if (pi.Operator.Type == TokenType.PLUS_PLUS)
                IL.Emit(OpCodes.Add);
            else
                IL.Emit(OpCodes.Sub);

            // Box new value and store in temp
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var newValue = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, newValue);

            // SetProperty(obj, name, newValue)
            EmitExpression(get.Object);
            EmitBoxIfNeeded(get.Object);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

            // Return new value (prefix behavior)
            IL.Emit(OpCodes.Ldloc, newValue);
            SetStackUnknown();
            return;
        }

        if (pi.Operand is Expr.GetIndex gi)
        {
            // Prefix increment on array index: ++arr[i]
            // Get current value
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
            EmitUnboxToDouble();

            // Increment or decrement
            IL.Emit(OpCodes.Ldc_R8, 1.0);
            if (pi.Operator.Type == TokenType.PLUS_PLUS)
                IL.Emit(OpCodes.Add);
            else
                IL.Emit(OpCodes.Sub);

            // Box new value and store in temp
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var newValue = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, newValue);

            // SetIndex(obj, index, newValue)
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);

            // Return new value (prefix behavior)
            IL.Emit(OpCodes.Ldloc, newValue);
            SetStackUnknown();
            return;
        }
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            EmitVariable(v);
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Dup); // Keep original value

            if (pi.Operator.Type == TokenType.PLUS_PLUS)
            {
                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(OpCodes.Add);
            }
            else
            {
                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(OpCodes.Sub);
            }

            IL.Emit(OpCodes.Box, _ctx.Types.Double);

            var local = _ctx.Locals.GetLocal(v.Name.Lexeme);
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }

            // Original value is still on stack, box it
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        if (pi.Operand is Expr.Get get)
        {
            // Postfix increment on property: obj.prop++
            // Get current value
            EmitExpression(get.Object);
            EmitBoxIfNeeded(get.Object);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            EmitUnboxToDouble();

            // Save old value for postfix return
            var oldValue = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, oldValue);

            // Increment or decrement
            IL.Emit(OpCodes.Ldc_R8, 1.0);
            if (pi.Operator.Type == TokenType.PLUS_PLUS)
                IL.Emit(OpCodes.Add);
            else
                IL.Emit(OpCodes.Sub);

            // Box new value and store in temp
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var newValue = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, newValue);

            // SetProperty(obj, name, newValue)
            EmitExpression(get.Object);
            EmitBoxIfNeeded(get.Object);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

            // Return old value (postfix behavior)
            IL.Emit(OpCodes.Ldloc, oldValue);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        if (pi.Operand is Expr.GetIndex gi)
        {
            // Postfix increment on array index: arr[i]++
            // Get current value
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
            EmitUnboxToDouble();

            // Save old value
            var oldValue = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, oldValue);

            // Increment or decrement
            IL.Emit(OpCodes.Ldc_R8, 1.0);
            if (pi.Operator.Type == TokenType.PLUS_PLUS)
                IL.Emit(OpCodes.Add);
            else
                IL.Emit(OpCodes.Sub);

            // Box new value and store in temp
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var newValue = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, newValue);

            // SetIndex(obj, index, newValue)
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);

            // Return old value
            IL.Emit(OpCodes.Ldloc, oldValue);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Compound assignment on object property: obj.prop += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // Get current value: GetProperty(obj, name)
        EmitExpression(cs.Object);
        EmitBoxIfNeeded(cs.Object);
        IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

        // Apply operation
        EmitCompoundOperation(cs.Operator.Type, cs.Value);

        // Store result: SetProperty(obj, name, value)
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, resultLocal);

        EmitExpression(cs.Object);
        EmitBoxIfNeeded(cs.Object);
        IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

        // Leave result on stack
        IL.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    protected override void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        // Compound assignment on array element: arr[i] += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // Get current value: GetIndex(obj, index)
        EmitExpression(csi.Object);
        EmitBoxIfNeeded(csi.Object);
        EmitExpression(csi.Index);
        EmitBoxIfNeeded(csi.Index);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);

        // Apply operation
        EmitCompoundOperation(csi.Operator.Type, csi.Value);

        // Store result: SetIndex(obj, index, value)
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, resultLocal);

        EmitExpression(csi.Object);
        EmitBoxIfNeeded(csi.Object);
        EmitExpression(csi.Index);
        EmitBoxIfNeeded(csi.Index);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);

        // Leave result on stack
        IL.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    private void EmitCompoundOperation(TokenType opType, Expr value)
    {
        // Stack has current value (object). Apply the operation.
        bool isBitwise = opType is TokenType.AMPERSAND_EQUAL or TokenType.PIPE_EQUAL
            or TokenType.CARET_EQUAL or TokenType.LESS_LESS_EQUAL or TokenType.GREATER_GREATER_EQUAL;

        if (opType == TokenType.PLUS_EQUAL && IsStringExpression(value))
        {
            // String concatenation
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.Object, _ctx.Types.Object));
            return;
        }

        // Convert current value to number
        EmitUnboxToDouble();

        if (isBitwise)
        {
            IL.Emit(OpCodes.Conv_I4);
            EmitExpressionAsDouble(value);
            IL.Emit(OpCodes.Conv_I4);
        }
        else
        {
            EmitExpressionAsDouble(value);
        }

        switch (opType)
        {
            case TokenType.PLUS_EQUAL:
                IL.Emit(OpCodes.Add);
                break;
            case TokenType.MINUS_EQUAL:
                IL.Emit(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                IL.Emit(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                IL.Emit(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                IL.Emit(OpCodes.Rem);
                break;
            case TokenType.AMPERSAND_EQUAL:
                IL.Emit(OpCodes.And);
                break;
            case TokenType.PIPE_EQUAL:
                IL.Emit(OpCodes.Or);
                break;
            case TokenType.CARET_EQUAL:
                IL.Emit(OpCodes.Xor);
                break;
            case TokenType.LESS_LESS_EQUAL:
                IL.Emit(OpCodes.Shl);
                break;
            case TokenType.GREATER_GREATER_EQUAL:
                IL.Emit(OpCodes.Shr);
                break;
        }

        if (isBitwise)
        {
            IL.Emit(OpCodes.Conv_R8);
        }

        IL.Emit(OpCodes.Box, _ctx.Types.Double);
    }

    private bool IsBigIntOperation(Expr.Binary b)
    {
        // Check if either operand has bigint type from the type map
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);

        return leftType is TypeInfo.BigInt || rightType is TypeInfo.BigInt;
    }

    private bool IsBigIntExpr(Expr expr)
    {
        if (_ctx.TypeMap == null) return false;
        var type = _ctx.TypeMap.Get(expr);
        return type is TypeInfo.BigInt;
    }

    private void EmitBigIntBinary(Expr.Binary b)
    {
        // Emit both operands as boxed objects
        EmitExpression(b.Left);
        EmitBoxIfNeeded(b.Left);
        EmitExpression(b.Right);
        EmitBoxIfNeeded(b.Right);

        switch (b.Operator.Type)
        {
            // Arithmetic
            case TokenType.PLUS:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntAdd);
                break;
            case TokenType.MINUS:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntSubtract);
                break;
            case TokenType.STAR:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntMultiply);
                break;
            case TokenType.SLASH:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntDivide);
                break;
            case TokenType.PERCENT:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntRemainder);
                break;
            case TokenType.STAR_STAR:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntPow);
                break;

            // Comparison
            case TokenType.LESS:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntLessThan);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case TokenType.LESS_EQUAL:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntLessThanOrEqual);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case TokenType.GREATER:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntGreaterThan);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case TokenType.GREATER_EQUAL:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntGreaterThanOrEqual);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntEquals);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntEquals);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;

            // Bitwise
            case TokenType.AMPERSAND:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntBitwiseAnd);
                break;
            case TokenType.PIPE:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntBitwiseOr);
                break;
            case TokenType.CARET:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntBitwiseXor);
                break;
            case TokenType.LESS_LESS:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntLeftShift);
                break;
            case TokenType.GREATER_GREATER:
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntRightShift);
                break;
            case TokenType.GREATER_GREATER_GREATER:
                throw new Exception("Runtime Error: Unsigned right shift (>>>) is not supported for bigint.");

            default:
                throw new Exception($"Unsupported bigint operator: {b.Operator.Type}");
        }

        SetStackUnknown();
    }
}
