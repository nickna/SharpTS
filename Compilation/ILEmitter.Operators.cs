using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using static SharpTS.TypeSystem.OperatorDescriptor;

namespace SharpTS.Compilation;

/// <summary>
/// Operator emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitBinary(Expr.Binary b)
    {
        // Try constant folding first
        if (TryEmitConstantFolded(b))
            return;

        // Check for bigint operations first
        if (IsBigIntOperation(b))
        {
            EmitBigIntBinary(b);
            return;
        }

        var desc = SemanticOperatorResolver.Resolve(b.Operator.Type);

        switch (desc)
        {
            case Plus:
                // Try string concatenation chain optimization first
                if (TryEmitStringConcatChain(b))
                    return;

                // Use runtime Add() which handles both string concat and numeric add
                EmitExpression(b.Left);
                EmitBoxIfNeeded(b.Left);
                EmitExpression(b.Right);
                EmitBoxIfNeeded(b.Right);
                EmitCallUnknown(_ctx.Runtime!.Add);
                break;

            case Arithmetic arith:
                // Numeric arithmetic with direct IL opcodes
                EmitExpressionAsDouble(b.Left);
                EmitExpressionAsDouble(b.Right);
                IL.Emit(arith.Opcode);
                EmitBoxDouble();
                break;

            case Power:
                EmitPowerBinary(b);
                break;

            case Comparison cmp:
                EmitExpressionAsDouble(b.Left);
                EmitExpressionAsDouble(b.Right);
                IL.Emit(cmp.Opcode);
                if (cmp.Negated)
                {
                    // For <= and >=, we use the inverse opcode and negate
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ceq);
                }
                EmitBoxBool();
                break;

            case Equality eq:
                EmitEqualityBinary(b, eq.IsStrict, eq.IsNegated);
                break;

            case Bitwise or BitwiseShift or UnsignedRightShift:
                EmitBitwiseBinary(b);
                break;

            case In:
                // 'in' operator checks if a property exists in an object
                EmitExpression(b.Left);
                EmitBoxIfNeeded(b.Left);
                EmitExpression(b.Right);
                EmitBoxIfNeeded(b.Right);
                EmitCallAndBoxBool(_ctx.Runtime!.HasIn);
                break;

            case InstanceOf:
                EmitExpression(b.Left);
                EmitBoxIfNeeded(b.Left);
                EmitExpression(b.Right);
                EmitBoxIfNeeded(b.Right);
                EmitCallAndBoxBool(_ctx.Runtime!.InstanceOf);
                break;
        }
    }

    /// <summary>
    /// Emits power operator (**) using Math.Pow.
    /// </summary>
    private void EmitPowerBinary(Expr.Binary b)
    {
        EmitExpressionAsDouble(b.Left);
        EmitExpressionAsDouble(b.Right);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Math, "Pow", _ctx.Types.Double, _ctx.Types.Double));
        EmitBoxDouble();
    }

    /// <summary>
    /// Emits equality operators (==, ===, !=, !==).
    /// </summary>
    private void EmitEqualityBinary(Expr.Binary b, bool isStrict, bool isNegated)
    {
        EmitExpression(b.Left);
        EmitBoxIfNeeded(b.Left);
        EmitExpression(b.Right);
        EmitBoxIfNeeded(b.Right);

        if (!isStrict)
        {
            // Loose equality: use runtime.Equals which treats null == undefined
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Equals);
        }
        else
        {
            // Strict equality: use Object.Equals which keeps null !== undefined
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
        // Try constant folding first
        if (ConstantFolder.TryFoldLogical(l, out var result))
        {
            EmitConstantValue(result);
            return;
        }

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
        // Try constant folding first
        if (ConstantFolder.TryFoldUnary(u, out var result))
        {
            EmitConstantValue(result);
            return;
        }

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

            case TokenType.VOID:
                // void operator: evaluate expression for side effects, return undefined
                EmitExpression(u.Right);
                EmitBoxIfNeeded(u.Right);
                IL.Emit(OpCodes.Pop); // Discard the result
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance); // Load undefined
                SetStackUnknown();
                break;
        }
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        var local = _ctx.Locals.GetLocal(ca.Name.Lexeme);
        FieldBuilder? topLevelField = null;
        _ctx.TopLevelStaticVars?.TryGetValue(ca.Name.Lexeme, out topLevelField);

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
            else if (topLevelField != null)
            {
                IL.Emit(OpCodes.Stsfld, topLevelField);
            }
            SetStackType(StackType.String);
            return;
        }

        // For += with unknown right-hand side types, use runtime Add (handles both string concat and numeric add)
        if (ca.Operator.Type == TokenType.PLUS_EQUAL)
        {
            // Load current value as object
            EmitVariable(new Expr.Variable(ca.Name));
            EmitBoxIfNeeded(new Expr.Variable(ca.Name));

            // Load right side as object
            EmitExpression(ca.Value);
            EmitBoxIfNeeded(ca.Value);

            // Use runtime Add which handles both string concatenation and numeric addition
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Add);
            IL.Emit(OpCodes.Dup);

            // Store result
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }
            else if (topLevelField != null)
            {
                IL.Emit(OpCodes.Stsfld, topLevelField);
            }
            SetStackUnknown();
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
        else if (topLevelField != null)
        {
            IL.Emit(OpCodes.Stsfld, topLevelField);
        }
        SetStackUnknown();
    }

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("logical_assign_end");
        var local = _ctx.Locals.GetLocal(la.Name.Lexeme);
        FieldBuilder? topLevelField = null;
        _ctx.TopLevelStaticVars?.TryGetValue(la.Name.Lexeme, out topLevelField);

        // Load current value
        EmitVariable(new Expr.Variable(la.Name));
        EmitBoxIfNeeded(new Expr.Variable(la.Name));
        IL.Emit(OpCodes.Dup);

        switch (la.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                // x &&= y: Only assign if x is truthy
                EmitTruthyCheck();
                builder.Emit_Brfalse(endLabel); // If falsy, keep current value
                break;
            case TokenType.OR_OR_EQUAL:
                // x ||= y: Only assign if x is falsy
                EmitTruthyCheck();
                builder.Emit_Brtrue(endLabel); // If truthy, keep current value
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                // x ??= y: Only assign if x is nullish
                var assignLabel = builder.DefineLabel("nullish_assign");
                // Check for null
                IL.Emit(OpCodes.Dup);
                builder.Emit_Brfalse(assignLabel);
                // Check for undefined
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
                builder.Emit_Brtrue(assignLabel);
                // Not nullish - pop extra value and keep current value
                IL.Emit(OpCodes.Pop);
                builder.Emit_Br(endLabel);
                builder.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                IL.Emit(OpCodes.Pop);
                break;
        }

        // Pop the duplicate current value
        IL.Emit(OpCodes.Pop);

        // Evaluate and assign the right side
        EmitExpression(la.Value);
        EmitBoxIfNeeded(la.Value);
        IL.Emit(OpCodes.Dup);
        if (local != null)
        {
            IL.Emit(OpCodes.Stloc, local);
        }
        else if (topLevelField != null)
        {
            IL.Emit(OpCodes.Stsfld, topLevelField);
        }

        builder.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSet(Expr.LogicalSet ls)
    {
        var builder = _ctx.ILBuilder;
        var skipAssignLabel = builder.DefineLabel("logical_set_skip");
        var endLabel = builder.DefineLabel("logical_set_end");

        // Store object in a local for later use
        EmitExpression(ls.Object);
        EmitBoxIfNeeded(ls.Object);
        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        // Get current property value
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        IL.Emit(OpCodes.Dup);

        switch (ls.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                EmitTruthyCheck();
                builder.Emit_Brfalse(skipAssignLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                EmitTruthyCheck();
                builder.Emit_Brtrue(skipAssignLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = builder.DefineLabel("nullish_set_assign");
                IL.Emit(OpCodes.Dup);
                builder.Emit_Brfalse(assignLabel);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
                builder.Emit_Brtrue(assignLabel);
                // Not nullish - pop extra value and skip assignment
                IL.Emit(OpCodes.Pop);
                builder.Emit_Br(skipAssignLabel);
                builder.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                IL.Emit(OpCodes.Pop);
                break;
        }

        // Pop current value and assign new value
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        EmitExpression(ls.Value);
        EmitBoxIfNeeded(ls.Value);
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Stloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(skipAssignLabel);
        // Current value is on stack, just use it

        builder.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi)
    {
        var builder = _ctx.ILBuilder;
        var skipAssignLabel = builder.DefineLabel("logical_setindex_skip");
        var endLabel = builder.DefineLabel("logical_setindex_end");

        // Store object and index in locals
        EmitExpression(lsi.Object);
        EmitBoxIfNeeded(lsi.Object);
        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        EmitExpression(lsi.Index);
        EmitBoxIfNeeded(lsi.Index);
        var indexLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Get current value at index
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
        IL.Emit(OpCodes.Dup);

        switch (lsi.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                EmitTruthyCheck();
                builder.Emit_Brfalse(skipAssignLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                EmitTruthyCheck();
                builder.Emit_Brtrue(skipAssignLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = builder.DefineLabel("nullish_setindex_assign");
                IL.Emit(OpCodes.Dup);
                builder.Emit_Brfalse(assignLabel);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
                builder.Emit_Brtrue(assignLabel);
                // Not nullish - pop extra value and skip assignment
                IL.Emit(OpCodes.Pop);
                builder.Emit_Br(skipAssignLabel);
                builder.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                IL.Emit(OpCodes.Pop);
                break;
        }

        // Pop current value and assign new value
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        EmitExpression(lsi.Value);
        EmitBoxIfNeeded(lsi.Value);
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Stloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(skipAssignLabel);
        // Current value is on stack, just use it

        builder.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            // Check if this is a typed double local
            var localType = _ctx.Locals.GetLocalType(v.Name.Lexeme);
            bool isTypedDouble = localType != null && _ctx.Types.IsDouble(localType);

            EmitVariable(v);

            // Only unbox if not already an unboxed double
            if (_stackType != StackType.Double)
            {
                EmitUnboxToDouble();
            }

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

            // Duplicate for expression result, then store
            // For typed double locals, duplicate unboxed, store unboxed, then box the duplicate for result
            if (isTypedDouble)
            {
                IL.Emit(OpCodes.Dup); // Duplicate unboxed value
            }
            else
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                IL.Emit(OpCodes.Dup);
            }

            // Check function display class first (before regular locals)
            if (_ctx.CapturedFunctionLocals?.Contains(v.Name.Lexeme) == true &&
                _ctx.FunctionDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var funcDCField) == true)
            {
                // Store to function display class field (always boxed)
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                if (_ctx.FunctionDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                }
                else if (_ctx.CurrentArrowFunctionDCField != null)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                }
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, funcDCField);
                // Box result if needed
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                SetStackUnknown();
                return;
            }

            // Check entry-point display class (captured top-level vars)
            if (_ctx.CapturedTopLevelVars?.Contains(v.Name.Lexeme) == true &&
                _ctx.EntryPointDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var entryPointField) == true)
            {
                // Store to entry-point display class field (always boxed)
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                if (_ctx.EntryPointDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                }
                else if (_ctx.CurrentArrowEntryPointDCField != null)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
                }
                else if (_ctx.EntryPointDisplayClassStaticField != null)
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                }
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, entryPointField);
                // Box result if needed
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                SetStackUnknown();
                return;
            }

            var local = _ctx.Locals.GetLocal(v.Name.Lexeme);
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }
            else if (_ctx.CapturedFields?.TryGetValue(v.Name.Lexeme, out var capturedField) == true)
            {
                // Store to captured field (always boxed): need to use temp pattern
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                IL.Emit(OpCodes.Ldarg_0); // display class instance
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, capturedField);
            }
            else if (_ctx.TopLevelStaticVars?.TryGetValue(v.Name.Lexeme, out var topLevelField) == true)
            {
                // Top-level static fields are always boxed
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                IL.Emit(OpCodes.Stsfld, topLevelField);
            }

            // Box result if needed
            if (isTypedDouble)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
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
            // Check if this is a typed double local
            var localType = _ctx.Locals.GetLocalType(v.Name.Lexeme);
            bool isTypedDouble = localType != null && _ctx.Types.IsDouble(localType);

            EmitVariable(v);

            // Only unbox if not already an unboxed double
            if (_stackType != StackType.Double)
            {
                EmitUnboxToDouble();
            }
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

            // Box only if storing to a non-typed location
            if (!isTypedDouble)
            {
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }

            // Check function display class first (before regular locals)
            if (_ctx.CapturedFunctionLocals?.Contains(v.Name.Lexeme) == true &&
                _ctx.FunctionDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var funcDCField) == true)
            {
                // Store to function display class field (always boxed)
                if (isTypedDouble)
                {
                    // Need to box for field storage
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                if (_ctx.FunctionDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                }
                else if (_ctx.CurrentArrowFunctionDCField != null)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                }
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, funcDCField);

                // Original value is still on stack, box it
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
                return;
            }

            // Check entry-point display class (captured top-level vars)
            if (_ctx.CapturedTopLevelVars?.Contains(v.Name.Lexeme) == true &&
                _ctx.EntryPointDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var entryPointField) == true)
            {
                // Store to entry-point display class field (always boxed)
                if (isTypedDouble)
                {
                    // Need to box for field storage
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                if (_ctx.EntryPointDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                }
                else if (_ctx.CurrentArrowEntryPointDCField != null)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
                }
                else if (_ctx.EntryPointDisplayClassStaticField != null)
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                }
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, entryPointField);

                // Original value is still on stack, box it
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
                return;
            }

            var local = _ctx.Locals.GetLocal(v.Name.Lexeme);
            if (local != null)
            {
                IL.Emit(OpCodes.Stloc, local);
            }
            else if (_ctx.CapturedFields?.TryGetValue(v.Name.Lexeme, out var capturedField) == true)
            {
                // Store to captured field (always boxed): need to use temp pattern
                if (isTypedDouble)
                {
                    // Need to box for field storage
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                var temp = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, temp);
                IL.Emit(OpCodes.Ldarg_0); // display class instance
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, capturedField);
            }
            else if (_ctx.TopLevelStaticVars?.TryGetValue(v.Name.Lexeme, out var topLevelField) == true)
            {
                // Top-level static fields are always boxed
                if (isTypedDouble)
                {
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                IL.Emit(OpCodes.Stsfld, topLevelField);
            }

            // Original value is still on stack, box it for the expression result
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
            // String concatenation - we know right side is a string at compile time
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.Object, _ctx.Types.Object));
            return;
        }

        // For += with unknown types, use runtime Add which handles both string concat and numeric add
        if (opType == TokenType.PLUS_EQUAL)
        {
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Add);
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

        // Use centralized helper to get the runtime method and result type
        var (method, resultType) = BigIntOperatorHelper.GetRuntimeMethod(b.Operator.Type, _ctx.Runtime!);

        if (method == null || resultType == BigIntResultType.Unsupported)
        {
            if (b.Operator.Type == TokenType.GREATER_GREATER_GREATER)
                throw new CompileException("Unsigned right shift (>>>) is not supported for bigint.");
            throw new CompileException($"Unsupported bigint operator: {b.Operator.Type}");
        }

        // Call the runtime method
        IL.Emit(OpCodes.Call, method);

        // Handle result based on type
        switch (resultType)
        {
            case BigIntResultType.Boolean:
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case BigIntResultType.NegatedBoolean:
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            // BigIntResultType.Value - no additional boxing needed
        }

        SetStackUnknown();
    }

    /// <summary>
    /// Attempts to evaluate a binary expression at compile time and emit the result as a constant.
    /// </summary>
    /// <param name="b">The binary expression to fold.</param>
    /// <returns>True if the expression was constant-folded; false otherwise.</returns>
    private bool TryEmitConstantFolded(Expr.Binary b)
    {
        if (!ConstantFolder.TryFoldBinary(b, out var result))
            return false;

        EmitConstantValue(result);
        return true;
    }

    /// <summary>
    /// Emits a constant value onto the stack.
    /// </summary>
    private void EmitConstantValue(object? value)
    {
        switch (value)
        {
            case null:
                IL.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;

            case SharpTS.Runtime.Types.SharpTSUndefined:
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                SetStackUnknown();
                break;

            case bool boolVal:
                IL.Emit(boolVal ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                break;

            case double doubleVal:
                IL.Emit(OpCodes.Ldc_R8, doubleVal);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
                break;

            case string strVal:
                IL.Emit(OpCodes.Ldstr, strVal);
                SetStackType(StackType.String);
                break;

            default:
                // Shouldn't happen, but fall back to null
                IL.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    /// <summary>
    /// Attempts to optimize a string concatenation chain into a single String.Concat call.
    /// </summary>
    /// <param name="b">The binary expression to optimize.</param>
    /// <returns>True if the chain was optimized; false otherwise.</returns>
    private bool TryEmitStringConcatChain(Expr.Binary b)
    {
        if (!StringConcatOptimizer.TryFlattenConcatChain(b, out var parts))
            return false;

        // Check if we can fold everything to a constant string
        if (StringConcatOptimizer.TryFoldAllToString(parts, out var constantResult))
        {
            IL.Emit(OpCodes.Ldstr, constantResult!);
            SetStackType(StackType.String);
            return true;
        }

        // Emit optimized String.Concat with array
        // For 2-4 parts, use the specific overloads; for more, use params array
        if (parts.Count <= 4)
        {
            // Use String.Concat(object, object, ...) overloads
            EmitStringConcatWithOverload(parts);
        }
        else
        {
            // Use String.Concat(params object[]) for 5+ parts
            EmitStringConcatWithArray(parts);
        }

        SetStackType(StackType.String);
        return true;
    }

    /// <summary>
    /// Emits String.Concat using specific overloads for 2-3 arguments.
    /// Falls back to array version for 4+ arguments.
    /// </summary>
    private void EmitStringConcatWithOverload(List<Expr> parts)
    {
        // Get the appropriate String.Concat overload
        var paramTypes = new Type[parts.Count];
        for (int i = 0; i < parts.Count; i++)
            paramTypes[i] = _ctx.Types.Object;

        var concatMethod = _ctx.Types.String.GetMethod("Concat", paramTypes);

        // If we can't find the specific overload (e.g., for 4 args), use array version
        if (concatMethod == null)
        {
            EmitStringConcatWithArray(parts);
            return;
        }

        // Emit each part as boxed object
        foreach (var part in parts)
        {
            EmitExpression(part);
            EmitBoxIfNeeded(part);
        }

        IL.Emit(OpCodes.Call, concatMethod);
    }

    /// <summary>
    /// Emits String.Concat using the params object[] overload for 5+ arguments.
    /// </summary>
    private void EmitStringConcatWithArray(List<Expr> parts)
    {
        // Create array: new object[parts.Count]
        IL.Emit(OpCodes.Ldc_I4, parts.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

        // Fill array with parts
        for (int i = 0; i < parts.Count; i++)
        {
            IL.Emit(OpCodes.Dup);           // Duplicate array reference
            IL.Emit(OpCodes.Ldc_I4, i);     // Index
            EmitExpression(parts[i]);       // Value
            EmitBoxIfNeeded(parts[i]);
            IL.Emit(OpCodes.Stelem_Ref);    // Store in array
        }

        // Call String.Concat(object[])
        var concatMethod = _ctx.Types.String.GetMethod("Concat", [_ctx.Types.ObjectArray]);
        IL.Emit(OpCodes.Call, concatMethod!);
    }
}
