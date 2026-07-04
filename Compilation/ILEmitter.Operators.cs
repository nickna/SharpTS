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

        // instanceof/in are valid with any operand type — bypass bigint-arithmetic path
        // so a bigint LHS gets boxed and flows through the normal InstanceOf/HasIn runtime call.
        if (IsBigIntOperation(b) && b.Operator.Type is not (TokenType.IN or TokenType.INSTANCEOF))
        {
            if (IsBothBigInt(b))
            {
                EmitBigIntBinary(b);
                return;
            }
            // Mixed bigint/non-bigint: only equality (==/===/!=/!==) and string-concat
            // `+` are well-typed here (the type checker rejects all other mixed bigint
            // operators). Equality is handled below; mixed `+` falls through to the
            // general string-concat path.
            if (TryEmitMixedBigIntEquality(b))
                return;
        }

        var desc = SemanticOperatorResolver.Resolve(b.Operator.Type);

        switch (desc)
        {
            case Plus:
                // Try string concatenation chain optimization first
                if (TryEmitStringConcatChain(b))
                    return;

                // If both operands are known numeric, emit direct IL add (no Runtime.Add overhead)
                if (IsNumericPlusOperation(b))
                {
                    EmitExpressionAsDouble(b.Left);
                    EmitExpressionAsDouble(b.Right);
                    IL.Emit(OpCodes.Add);
                    SetStackType(StackType.Double);
                    break;
                }

                // If both operands are statically string, emit a direct
                // String.Concat(string, string) — skipping the dynamic
                // $Runtime.Add type-dispatch + ToNumber/ToString probing every
                // call. This is the 2-operand string concat the 3+-part chain
                // optimizer (TryEmitStringConcatChain, MinPartsForOptimization=3)
                // does not cover, e.g. `s = s + "ab"`. Both-string only: a
                // non-string operand would need StringifyCoerce, whose
                // string-hint ToPrimitive can diverge from `+`'s default-hint
                // ToPrimitive for objects with differing valueOf/toString —
                // those stay on the sound $Runtime.Add path.
                if (IsStringPlusOperation(b))
                {
                    EmitExpression(b.Left);
                    EnsureString();
                    EmitExpression(b.Right);
                    EnsureString();
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.String, _ctx.Types.String));
                    SetStackType(StackType.String);
                    break;
                }

                // Fallback: Use runtime Add() which handles string concat and mixed types
                EmitExpression(b.Left);
                EmitBoxIfNeeded(b.Left);
                EmitExpression(b.Right);
                EmitBoxIfNeeded(b.Right);
                EmitCallUnknown(_ctx.Runtime!.Add);
                break;

            case Arithmetic arith:
                // #928: `counter % intLiteral` → native int64 rem instead of FP fmod (the dominant
                // per-iteration write-kernel cost). Sound within the int-counter gate (truncated
                // remainder matches JS for |i| ≤ 2^53). Falls through to the double path otherwise.
                if (arith.Opcode == OpCodes.Rem && TryEmitIntegerCounterModulo(b))
                {
                    SetStackType(StackType.Double);
                    break;
                }
                // Numeric arithmetic with direct IL opcodes
                EmitExpressionAsDouble(b.Left);
                EmitExpressionAsDouble(b.Right);
                IL.Emit(arith.Opcode);
                SetStackType(StackType.Double);
                break;

            case Power:
                EmitPowerBinary(b);
                break;

            case Comparison cmp:
                // JS AbstractRelationalComparison: when both operands are
                // statically known strings, use lexicographic comparison via
                // string.CompareOrdinal (fast path). When both are statically
                // numeric, direct double IL (fast path). Otherwise route
                // through $Runtime.JsLessThan which handles the both-strings
                // case at runtime (was a FormatException with Convert.ToDouble
                // for sort callbacks like (x, y) => String(x) < String(y)).
                if (IsStringComparison(b))
                {
                    EmitExpression(b.Left);
                    EnsureString();
                    EmitExpression(b.Right);
                    EnsureString();
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "CompareOrdinal", _ctx.Types.String, _ctx.Types.String));
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(cmp.Opcode);
                    if (cmp.Negated)
                    {
                        IL.Emit(OpCodes.Ldc_I4_0);
                        IL.Emit(OpCodes.Ceq);
                    }
                }
                else if (IsNumericComparison(b))
                {
                    EmitExpressionAsDouble(b.Left);
                    EmitExpressionAsDouble(b.Right);
                    IL.Emit(cmp.Opcode);
                    if (cmp.Negated)
                    {
                        IL.Emit(OpCodes.Ldc_I4_0);
                        IL.Emit(OpCodes.Ceq);
                    }
                }
                else
                {
                    // Generic path: route through JsLessThan with operand
                    // ordering chosen per the operator. Decoder:
                    //   <  : Clt, !negated → JsLessThan(a, b)
                    //   >  : Cgt, !negated → JsLessThan(b, a)
                    //   <= : Cgt,  negated → !JsLessThan(b, a)
                    //   >= : Clt,  negated → !JsLessThan(a, b)
                    bool swapArgs = cmp.Opcode == OpCodes.Cgt;
                    EmitExpression(b.Left);
                    EmitBoxIfNeeded(b.Left);
                    EmitExpression(b.Right);
                    EmitBoxIfNeeded(b.Right);
                    if (swapArgs)
                    {
                        var tmp = IL.DeclareLocal(_ctx.Types.Object);
                        IL.Emit(OpCodes.Stloc, tmp);
                        var leftLocal = IL.DeclareLocal(_ctx.Types.Object);
                        IL.Emit(OpCodes.Stloc, leftLocal);
                        IL.Emit(OpCodes.Ldloc, tmp);
                        IL.Emit(OpCodes.Ldloc, leftLocal);
                    }
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsLessThan);
                    if (cmp.Negated)
                    {
                        IL.Emit(OpCodes.Ldc_I4_0);
                        IL.Emit(OpCodes.Ceq);
                    }
                }
                SetStackType(StackType.Boolean);
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
        SetStackType(StackType.Double);
    }

    /// <summary>
    /// Emits equality operators (==, ===, !=, !==).
    /// </summary>
    private void EmitEqualityBinary(Expr.Binary b, bool isStrict, bool isNegated)
    {
        // Fast path: comparison against the `null` or `undefined` literal. In compiled
        // output JS null is CLR null and JS undefined is the $Undefined singleton, so
        // the test is a direct reference check (ceq) rather than boxing the operand and
        // dispatching Object.Equals/runtime.Equals — null guards are everywhere.
        if (TryEmitNullishEqualityFastPath(b, isStrict, isNegated))
        {
            return;
        }

        // Fast path: both operands statically numeric. JS number equality has no
        // coercion (=== and == agree when both sides are number), and IEEE `ceq`
        // matches the spec exactly — NaN is never equal (ceq → false, correct for
        // `NaN === NaN`) and +0/-0 compare equal (ceq → true, correct for `===`).
        // Mirrors the numeric relational fast path above; avoids boxing both
        // operands and the boolean result plus an Object.Equals dispatch per
        // comparison (the dominant per-iteration cost in comparison-heavy loops).
        if (IsNumericComparison(b))
        {
            EmitExpressionAsDouble(b.Left);
            EmitExpressionAsDouble(b.Right);
            IL.Emit(OpCodes.Ceq);
            if (isNegated)
            {
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
            }
            SetStackType(StackType.Boolean);
            return;
        }

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
        // Convert to int32 for bitwise operations (ECMA-262 ToInt32: wraps, never throws)
        EmitExpression(b.Left);
        EmitBoxIfNeeded(b.Left);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.JsToInt32);

        EmitExpression(b.Right);
        EmitBoxIfNeeded(b.Right);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.JsToInt32);

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

            case TokenType.PLUS:
                // Unary plus: numeric coercion (ToNumber per ECMA-262). This override was
                // previously missing a PLUS case, so the switch fell through silently and
                // `+x` emitted zero bytes — whoever was waiting on the result (e.g. a
                // subsequent `dup`/`stloc`/SetProperty) got a stack underflow and the JIT
                // rejected the method as InvalidProgram. Real impact: semver's SemVer ctor
                // does `this.major = +m[1]` which was unreachable.
                EmitExpression(u.Right);
                if (u.Right is Expr.Literal { Value: double })
                {
                    EmitBoxDouble();
                }
                else
                {
                    EmitBoxIfNeeded(u.Right);
                    // ECMA-262 21.1.1.1 ToNumber: handles hex strings ("0x..."),
                    // "Infinity", boolean coercion. Convert.ToDouble doesn't.
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ConvertToNumber);
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
                // typeof never throws on undeclared variables - returns "undefined"
                if (u.Right is Expr.Variable tv && !IsKnownVariable(tv.Name.Lexeme))
                {
                    IL.Emit(OpCodes.Ldstr, "undefined");
                }
                else if (IsTypeofOnBuiltInMethod(u.Right))
                {
                    // Built-in module methods and static type methods are functions
                    IL.Emit(OpCodes.Ldstr, "function");
                }
                else if (u.Right is Expr.Variable ev
                    && Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(ev.Name.Lexeme))
                {
                    // Error constructors are functions
                    IL.Emit(OpCodes.Ldstr, "function");
                }
                else if (IsTypeofOnProcessStream(u.Right))
                {
                    // process.stdin/stdout/stderr are stream objects
                    IL.Emit(OpCodes.Ldstr, "object");
                }
                else
                {
                    EmitExpression(u.Right);
                    EmitBoxIfNeeded(u.Right);
                    EmitCallString(_ctx.Runtime!.TypeOf);
                }
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
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsToInt32);
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
        // Promoted string-accumulator append (#857): `s += E` where `s` is a StringBuilder slot →
        // `sb.Append(E)`. Promotion guarantees statement position, so the Append-returned builder on the
        // stack is the value Stmt.Expression pops. See EmitAssign and StringAccumulatorPromotionAnalyzer.
        if (ca.Operator.Type == TokenType.PLUS_EQUAL
            && _ctx.TryGetPromotedStringAccumulator(ca.Name.Lexeme) is { } accSb)
        {
            IL.Emit(OpCodes.Ldloc, accSb);
            EmitExpression(ca.Value);
            EnsureString();
            IL.Emit(OpCodes.Callvirt, _ctx.Types.StringBuilderAppendString);
            SetStackUnknown();
            return;
        }

        var local = _ctx.Locals.GetLocal(ca.Name.Lexeme);
        FieldBuilder? topLevelField = null;
        _ctx.TopLevelStaticVars?.TryGetValue(ca.Name.Lexeme, out topLevelField);

        // Special case: string concatenation with +=
        if (ca.Operator.Type == TokenType.PLUS_EQUAL && IsStringExpression(ca.Value))
        {
            // Load current value, StringifyCoerce'd: JS-compatible conversion
            // (null → "null", undefined → "undefined") that throws TypeError
            // for Symbol operands (§7.1.17) — String.Concat(object, object)
            // did neither.
            EmitVariable(new Expr.Variable(ca.Name));
            EmitBoxIfNeeded(new Expr.Variable(ca.Name));
            IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);

            // Load right side, StringifyCoerce'd
            EmitExpression(ca.Value);
            EmitBoxIfNeeded(ca.Value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);

            // String concatenation
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.String, _ctx.Types.String));
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
            else
            {
                _resolver.TryStoreVariable(ca.Name.Lexeme);
            }
            SetStackType(StackType.String);
            return;
        }

        // Check if target is a typed double local (for boxing elimination)
        var localType = local != null ? _ctx.Locals.GetLocalType(ca.Name.Lexeme) : null;
        bool isTypedDouble = localType != null && _ctx.Types.IsDouble(localType);

        // For += with unknown right-hand side types, use runtime Add (handles both string concat and numeric add)
        // When the target is a typed double local, we know it's numeric — skip runtime Add.
        if (!isTypedDouble && ca.Operator.Type == TokenType.PLUS_EQUAL)
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
            else
            {
                _resolver.TryStoreVariable(ca.Name.Lexeme);
            }
            SetStackUnknown();
            return;
        }

        // Numeric compound assignment
        bool isBitwise = CompoundOperatorHelper.IsBitwise(ca.Operator.Type);
        bool isUnsignedShift = ca.Operator.Type == TokenType.GREATER_GREATER_GREATER_EQUAL;

        if (isUnsignedShift)
        {
            // `x >>>= y`: route through JsToInt32 for spec-correct ToInt32, then Shr_Un with
            // zero-extend to uint64 before Conv_R8 so bit 31 doesn't flip the result negative.
            EmitVariable(new Expr.Variable(ca.Name));
            EnsureBoxed();
            IL.Emit(OpCodes.Call, _ctx.Runtime!.JsToInt32);

            EmitExpression(ca.Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Call, _ctx.Runtime!.JsToInt32);

            IL.Emit(OpCodes.Ldc_I4, 0x1F);
            IL.Emit(OpCodes.And);
            IL.Emit(OpCodes.Shr_Un);
            IL.Emit(OpCodes.Conv_U8);
            IL.Emit(OpCodes.Conv_R8);
        }
        else
        {
            // Get current value as double — EnsureDouble() is stack-type-aware
            // and avoids emitting Convert.ToDouble when the value is already unboxed.
            EmitVariable(new Expr.Variable(ca.Name));
            EnsureDouble();

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
        }

        // Store result — keep unboxed for typed double locals
        if (isTypedDouble && local != null)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, local);
            SetStackType(StackType.Double);
        }
        else
        {
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
            else
            {
                _resolver.TryStoreVariable(ca.Name.Lexeme);
            }
            SetStackUnknown();
        }
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
        else
        {
            _resolver.TryStoreVariable(la.Name.Lexeme);
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

        // Logical assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before the short-circuit check (#733).
        if (!IsNullPlaceholderGlobal(ls.Object))
            EmitThrowIfReceiverUndefined(objLocal, ls.Name.Lexeme, isWrite: false);

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

        // Logical assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before the short-circuit check (#733).
        if (!IsNullPlaceholderGlobal(lsi.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocal, indexLocal, isWrite: false);

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

    // ===== Integer loop-counter prototype (#928) — gated by SHARPTS_INT_LOOP_COUNTER =====

    /// <summary>True iff <paramref name="name"/> is a loop counter currently backed by a native Int64 slot.</summary>
    private bool IsIntegerCounterLocal(string name)
        => _ctx.IntegerCounterLocals.Contains(name)
           && _ctx.Locals.GetLocalType(name) == _ctx.Types.Int64;

    /// <summary>
    /// Pushes a typed-array/array index as a native int32. When the index is an integer-counter
    /// expression (the counter, or counter ± integer literal), emits it in native int — skipping the
    /// <c>EmitExpressionAsDouble + Conv_I4</c> round trip and the double induction-variable tax that
    /// dominates typed-array kernels (#928). Produces the identical int32 for the in-range values a
    /// ±1 loop counter takes, so this is a pure representation choice. Falls back otherwise.
    /// </summary>
    private void EmitIndexAsInt32(Expr index)
    {
        if (TryEmitIntegerCounterIndexI4(index)) return;
        EmitExpressionAsDouble(index);
        IL.Emit(OpCodes.Conv_I4);
    }

    /// <summary>
    /// Emits a typed-array receiver onto the stack as its concrete <c>$XArray</c> type: the hoisted
    /// loop-invariant local when one is available (#928 — cast once per loop), otherwise the
    /// per-access <c>ldloc; EnsureBoxed; castclass</c>. Eliminating the per-access cast is what,
    /// together with the native-int counter, closes the typed-array read-stencil gap.
    /// </summary>
    private void EmitTypedArrayReceiver(Expr receiver, Type xArrayType)
    {
        if (receiver is Expr.Variable rv && _ctx.TryGetHoistedTypedArray(rv.Name.Lexeme) is { } h)
        {
            IL.Emit(OpCodes.Ldloc, h.TypedLocal);
            return;
        }
        EmitExpression(receiver);
        EnsureBoxed();
        IL.Emit(OpCodes.Castclass, xArrayType);
    }

    private bool TryEmitIntegerCounterIndexI4(Expr index)
    {
        switch (index)
        {
            // a[i]
            case Expr.Variable v when IsIntegerCounterLocal(v.Name.Lexeme):
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(v.Name.Lexeme)!);
                IL.Emit(OpCodes.Conv_I4);
                return true;

            // a[i + k] / a[i - k]  (counter left, integer literal right)
            case Expr.Binary { Operator.Type: TokenType.PLUS or TokenType.MINUS } b
                when b.Left is Expr.Variable lv && IsIntegerCounterLocal(lv.Name.Lexeme)
                     && TryGetIntLiteralValue(b.Right, out long k):
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(lv.Name.Lexeme)!);
                IL.Emit(OpCodes.Ldc_I8, k);
                IL.Emit(b.Operator.Type == TokenType.PLUS ? OpCodes.Add : OpCodes.Sub);
                IL.Emit(OpCodes.Conv_I4);
                return true;

            // a[k + i]  (commutative addition, counter right)
            case Expr.Binary { Operator.Type: TokenType.PLUS } b2
                when b2.Right is Expr.Variable rv && IsIntegerCounterLocal(rv.Name.Lexeme)
                     && TryGetIntLiteralValue(b2.Left, out long k2):
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(rv.Name.Lexeme)!);
                IL.Emit(OpCodes.Ldc_I8, k2);
                IL.Emit(OpCodes.Add);
                IL.Emit(OpCodes.Conv_I4);
                return true;
        }
        return false;
    }

    private static bool TryGetIntLiteralValue(Expr e, out long value)
    {
        value = 0;
        double d;
        if (e is Expr.Literal { Value: double lit }) d = lit;
        else if (e is Expr.Unary { Operator.Type: TokenType.MINUS, Right: Expr.Literal { Value: double neg } }) d = -neg;
        else return false;
        if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Truncate(d)) return false;
        value = (long)d;
        return true;
    }

    /// <summary>
    /// #928: emits <c>counter % integerLiteral</c> as a native int64 <c>rem</c> followed by
    /// <c>conv.r8</c>, replacing the per-iteration FP <c>fmod</c> (the dominant write-kernel cost).
    /// The left operand must be an integer-counter expression (the counter, or counter ± int literal)
    /// and the right a non-zero integer literal. C# <c>long %</c> and JS <c>%</c> are both truncated
    /// (result takes the dividend's sign), so this is bit-identical to the double computation for every
    /// <c>|dividend| ≤ 2^53</c> — the same range the int-counter representation already accepts. Divisor
    /// 0 is left to the double path (<c>fmod(x,0)=NaN</c>), so no <c>DivideByZeroException</c> is risked.
    /// Returns false (emitting nothing) when the shape does not qualify.
    /// </summary>
    private bool TryEmitIntegerCounterModulo(Expr.Binary b)
    {
        if (!ForLoopAnalyzer.IntegerModuloEnabled) return false;
        if (!TryGetIntLiteralValue(b.Right, out long divisor) || divisor == 0) return false;
        if (!TryEmitIntegerCounterValueI8(b.Left)) return false;
        IL.Emit(OpCodes.Ldc_I8, divisor);
        IL.Emit(OpCodes.Rem);
        IL.Emit(OpCodes.Conv_R8);
        return true;
    }

    /// <summary>
    /// Emits an integer-counter expression as a native <c>Int64</c> (no <c>conv.r8</c>): the counter
    /// <c>i</c>, or <c>i ± intLiteral</c> / <c>intLiteral + i</c>. Companion to
    /// <see cref="TryEmitIntegerCounterIndexI4"/> but leaves the value as Int64 on the stack. Returns
    /// false (emitting nothing) when the shape is not a recognized integer-counter expression.
    /// </summary>
    private bool TryEmitIntegerCounterValueI8(Expr e)
    {
        switch (e)
        {
            case Expr.Variable v when IsIntegerCounterLocal(v.Name.Lexeme):
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(v.Name.Lexeme)!);
                return true;

            case Expr.Binary { Operator.Type: TokenType.PLUS or TokenType.MINUS } bb
                when bb.Left is Expr.Variable lv && IsIntegerCounterLocal(lv.Name.Lexeme)
                     && TryGetIntLiteralValue(bb.Right, out long k):
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(lv.Name.Lexeme)!);
                IL.Emit(OpCodes.Ldc_I8, k);
                IL.Emit(bb.Operator.Type == TokenType.PLUS ? OpCodes.Add : OpCodes.Sub);
                return true;

            case Expr.Binary { Operator.Type: TokenType.PLUS } b2
                when b2.Right is Expr.Variable rv && IsIntegerCounterLocal(rv.Name.Lexeme)
                     && TryGetIntLiteralValue(b2.Left, out long k2):
                IL.Emit(OpCodes.Ldc_I8, k2);
                IL.Emit(OpCodes.Ldloc, _ctx.Locals.GetLocal(rv.Name.Lexeme)!);
                IL.Emit(OpCodes.Add);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Native int64 increment for an integer-counter local: <c>i++</c>/<c>i--</c> (postfix) or
    /// <c>++i</c>/<c>--i</c> (prefix). Mutates the slot in place and leaves the postfix-old /
    /// prefix-new value as a double (the numeric expression result). The counter is provably
    /// non-captured (analyzer), so the cell / display-class write-backs of the general path are
    /// unnecessary here.
    /// </summary>
    private void EmitIntegerCounterIncrement(string name, bool isPlus, bool postfix)
    {
        var local = _ctx.Locals.GetLocal(name)!;
        IL.Emit(OpCodes.Ldloc, local);
        if (postfix)
        {
            IL.Emit(OpCodes.Dup);                            // old, old
            IL.Emit(OpCodes.Ldc_I8, 1L);
            IL.Emit(isPlus ? OpCodes.Add : OpCodes.Sub);     // old, new
            IL.Emit(OpCodes.Stloc, local);                   // old
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I8, 1L);
            IL.Emit(isPlus ? OpCodes.Add : OpCodes.Sub);     // new
            IL.Emit(OpCodes.Dup);                            // new, new
            IL.Emit(OpCodes.Stloc, local);                   // new
        }
        IL.Emit(OpCodes.Conv_R8);
        SetStackType(StackType.Double);
    }

    /// <summary>
    /// Stores an incremented numeric value into the named local/parameter, walking the storage ladder
    /// (per-iteration binding cell #650 → function display class #674/#321 → arrow-scope display class →
    /// entry-point display class → IL local / captured display-class field / top-level static). Shared by
    /// <see cref="EmitPrefixIncrement"/> and <see cref="EmitPostfixIncrement"/>, which walked byte-identical
    /// copies of this ~8-rung ladder that repeatedly had to be kept in sync (#1127).
    /// </summary>
    /// <remarks>
    /// Precondition — the evaluation stack holds <c>[result, valueToStore]</c> (result underneath):
    /// <paramref name="isTypedDouble"/> is true when the target is a double-typed IL local, so
    /// <c>valueToStore</c> arrives as an unboxed double (stored unboxed to the local, boxed for the
    /// object-typed field/static rungs) — otherwise it arrives already boxed;
    /// <paramref name="resultIsUnboxedDouble"/> is true when <c>result</c> is still an unboxed double
    /// (the prefix new value for a typed-double local, or the postfix original value, which is dup'd
    /// before boxing). The early-exit storage rungs box that result and report <c>Unknown</c>; the
    /// fall-through IL-local / static rungs leave it unboxed for lazy boxing and report
    /// <see cref="StackType.Double"/>. Emitted IL is identical to the two former inline copies.
    /// </remarks>
    private void EmitStoreIncrementedVariable(string name, bool isTypedDouble, bool resultIsUnboxedDouble)
    {
        // Per-iteration loop-binding cell (#650): write the new value through the StrongBox so closures
        // that captured this iteration's cell observe it.
        if (_ctx.CellBindingLocals.TryGetValue(name, out var cell))
        {
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var cellTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, cellTemp);
            IL.Emit(OpCodes.Ldloc, cell);
            IL.Emit(OpCodes.Ldloc, cellTemp);
            IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
            if (resultIsUnboxedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Check function display class first (before regular locals).
        // #838: remap a write-captured block-scope shadow to its renamed DC storage key in an arrow body.
        var incDCName = _ctx.ResolveFunctionDCFieldName(name);
        if (_ctx.CapturedFunctionLocals?.Contains(incDCName) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(incDCName, out var funcDCField) == true)
        {
            // Store to function display class field (always boxed).
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
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
            // Captured PARAMETER of this function: also sync the arg slot — reads resolve parameters
            // before the DC, so a DC-only store leaves later same-body reads seeing the stale arg (#321).
            if (_ctx.FunctionDisplayClassLocal != null &&
                _ctx.TryGetParameter(name, out var funcParamSync))
            {
                IL.Emit(OpCodes.Ldloc, temp);
                _ctx.EmitConvertForParamSlot(IL, name);
                IL.Emit(OpCodes.Starg, funcParamSync);
            }
            if (resultIsUnboxedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Check arrow scope display class (captured arrow-local vars).
        if (_ctx.CapturedArrowLocals?.Contains(name) == true &&
            _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowDCField) == true)
        {
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            if (_ctx.ArrowScopeDisplayClassLocal != null)
            {
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowScopeDCField != null)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
            }
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, arrowDCField);
            // Captured PARAMETER of this arrow: also sync the arg slot (#321).
            if (_ctx.ArrowScopeDisplayClassLocal != null &&
                _ctx.TryGetParameter(name, out var arrowParamSync))
            {
                IL.Emit(OpCodes.Ldloc, temp);
                _ctx.EmitConvertForParamSlot(IL, name);
                IL.Emit(OpCodes.Starg, arrowParamSync);
            }
            if (resultIsUnboxedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Check entry-point display class (captured top-level vars).
        if (_ctx.CapturedTopLevelVars?.Contains(name) == true &&
            _ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true)
        {
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
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
            if (resultIsUnboxedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Fall-through storage: an IL local (typed double stored unboxed), a captured display-class
        // field, or a top-level static. The result is left on the stack unboxed for lazy boxing.
        var local = _ctx.Locals.GetLocal(name);
        if (local != null)
        {
            IL.Emit(OpCodes.Stloc, local);
        }
        else if (_ctx.CapturedFields?.TryGetValue(name, out var capturedField) == true)
        {
            // Store to captured field (always boxed): need to use temp pattern.
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            // Per-iteration cell capture (#650): write through the shared StrongBox.
            if (_ctx.CellCapturedFieldNames?.Contains(name) == true)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, capturedField);
                IL.Emit(OpCodes.Castclass, _ctx.Types.StrongBoxOfObject);
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
            }
            else
            {
                IL.Emit(OpCodes.Ldarg_0); // display class instance
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, capturedField);
            }
        }
        else if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            // Top-level static fields are always boxed.
            if (isTypedDouble) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            IL.Emit(OpCodes.Stsfld, topLevelField);
        }

        // Result is on the stack; keep an unboxed double as-is for lazy boxing by consumers.
        if (resultIsUnboxedDouble)
            SetStackType(StackType.Double);
        else
            SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            // Integer loop-counter prototype (#928): native int64 increment, no double/box round trip.
            if (IsIntegerCounterLocal(v.Name.Lexeme))
            {
                EmitIntegerCounterIncrement(v.Name.Lexeme, isPlus: pi.Operator.Type == TokenType.PLUS_PLUS, postfix: false);
                return;
            }

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

            EmitStoreIncrementedVariable(v.Name.Lexeme, isTypedDouble, resultIsUnboxedDouble: isTypedDouble);
            return;
        }

        if (pi.Operand is Expr.Get get)
        {
            // Static field fast path: ++this.staticField inside a static method/block
            if (TryResolveStaticThisField(get, out _, out var staticFieldPre))
            {
                IL.Emit(OpCodes.Ldsfld, staticFieldPre);
                EmitUnboxToDouble();
                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(pi.Operator.Type == TokenType.PLUS_PLUS ? OpCodes.Add : OpCodes.Sub);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stsfld, staticFieldPre);
                SetStackUnknown();
                return;
            }

            // Static data field via `Class.field` (own or inherited) — bind to the field / PDS shadow
            // so the increment lands on the same storage the static-typed read consults (#339).
            if (TryEmitStaticFieldIncrement(get, pi.Operator.Type == TokenType.PLUS_PLUS, isPrefix: true))
                return;

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
            // Integer loop-counter prototype (#928): native int64 increment, no double/box round trip.
            if (IsIntegerCounterLocal(v.Name.Lexeme))
            {
                EmitIntegerCounterIncrement(v.Name.Lexeme, isPlus: pi.Operator.Type == TokenType.PLUS_PLUS, postfix: true);
                return;
            }

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

            EmitStoreIncrementedVariable(v.Name.Lexeme, isTypedDouble, resultIsUnboxedDouble: true);
            return;
        }

        if (pi.Operand is Expr.Get get)
        {
            // Static field fast path: this.staticField++ inside a static method/block
            if (TryResolveStaticThisField(get, out _, out var staticFieldPost))
            {
                IL.Emit(OpCodes.Ldsfld, staticFieldPost);
                EmitUnboxToDouble();

                var oldValueStatic = IL.DeclareLocal(_ctx.Types.Double);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, oldValueStatic);

                IL.Emit(OpCodes.Ldc_R8, 1.0);
                IL.Emit(pi.Operator.Type == TokenType.PLUS_PLUS ? OpCodes.Add : OpCodes.Sub);

                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                IL.Emit(OpCodes.Stsfld, staticFieldPost);

                IL.Emit(OpCodes.Ldloc, oldValueStatic);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
                return;
            }

            // Static data field via `Class.field` (own or inherited) — bind to the field / PDS shadow
            // so the increment lands on the same storage the static-typed read consults (#339).
            if (TryEmitStaticFieldIncrement(get, pi.Operator.Type == TokenType.PLUS_PLUS, isPrefix: false))
                return;

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

    /// <summary>
    /// Emits <c>++</c>/<c>--</c> on a class's static data field accessed as <c>Class.field</c>
    /// (member access). Returns false (emitting nothing) when <paramref name="get"/> is not a known
    /// class's static field, so callers fall through to the generic property path. An own field is
    /// read and written directly (mirroring plain assignment); an inherited field is read shadow-or-base
    /// and the result written as a per-subclass own shadow via SetProperty's Type arm (→ PDS), matching
    /// JS own-shadow semantics (#339). The dynamic GetProperty/SetProperty path used otherwise desyncs
    /// with the Ldsfld-based static read (write lands in PDS, read sees the field), so binding here keeps
    /// read and write on the same storage. <paramref name="isPrefix"/> selects the result (new vs old).
    /// </summary>
    private bool TryEmitStaticFieldIncrement(Expr.Get get, bool isIncrement, bool isPrefix)
    {
        if (get.Object is not Expr.Variable classVar)
            return false;
        string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
        if (!_ctx.Classes.TryGetValue(resolvedClassName, out var classBuilder))
            return false;

        bool own = _ctx.ClassRegistry!.TryGetOwnCallableStaticField(resolvedClassName, get.Name.Lexeme, classBuilder, out var ownField);
        System.Reflection.FieldInfo? inheritedField = null;
        if (!own && !_ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, get.Name.Lexeme, classBuilder, out inheritedField))
            return false;

        // Load current value and coerce to double (ToNumber semantics).
        if (own)
            IL.Emit(OpCodes.Ldsfld, ownField!);
        else
            EmitStaticFieldLoadWithShadow(resolvedClassName, classBuilder, get.Name.Lexeme, inheritedField!);
        EmitUnboxToDouble();

        // Postfix returns the original value — stash it before incrementing.
        System.Reflection.Emit.LocalBuilder? oldValue = null;
        if (!isPrefix)
        {
            oldValue = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, oldValue);
        }

        IL.Emit(OpCodes.Ldc_R8, 1.0);
        IL.Emit(isIncrement ? OpCodes.Add : OpCodes.Sub);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        var newValue = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, newValue);

        // Write back to the same storage the matching read consults.
        if (own)
        {
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Stsfld, ownField!);
        }
        else
        {
            IL.Emit(OpCodes.Ldtoken, classBuilder);
            IL.Emit(OpCodes.Call, _ctx.Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);
        }

        if (isPrefix)
        {
            IL.Emit(OpCodes.Ldloc, newValue);
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, oldValue!);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
        }
        SetStackUnknown();
        return true;
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Static field fast path: this.staticField op= value inside a static method/block
        if (cs.Object is Expr.This &&
            TryResolveStaticThisField(new Expr.Get(cs.Object, cs.Name), out _, out var staticFieldCS))
        {
            IL.Emit(OpCodes.Ldsfld, staticFieldCS);
            EmitCompoundOperation(cs.Operator.Type, cs.Value);
            var csResult = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, csResult);
            IL.Emit(OpCodes.Ldloc, csResult);
            IL.Emit(OpCodes.Stsfld, staticFieldCS);
            IL.Emit(OpCodes.Ldloc, csResult);
            SetStackUnknown();
            return;
        }

        // Compound assignment on a class's static data field: `Class.field op= x`. The generic
        // dynamic path below routes the write through SetProperty (→ PDS) while the static-typed
        // read uses Ldsfld, so they desync. Bind own/inherited static fields explicitly here:
        //   * own field   → read+write the field directly (mirror the plain-assignment path);
        //   * inherited    → read shadow-or-base, then write a per-subclass own shadow via
        //                    SetProperty's Type arm (→ PDS), matching JS own-shadow semantics (#339).
        if (cs.Object is Expr.Variable classVarCS &&
            _ctx.Classes.TryGetValue(_ctx.ResolveClassName(classVarCS.Name.Lexeme), out var classBuilderCS))
        {
            string resolvedClassNameCS = _ctx.ResolveClassName(classVarCS.Name.Lexeme);
            if (_ctx.ClassRegistry!.TryGetOwnCallableStaticField(resolvedClassNameCS, cs.Name.Lexeme, classBuilderCS, out var ownFieldCS))
            {
                IL.Emit(OpCodes.Ldsfld, ownFieldCS!);
                EmitCompoundOperation(cs.Operator.Type, cs.Value);
                var ownResultCS = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, ownResultCS);
                IL.Emit(OpCodes.Ldloc, ownResultCS);
                IL.Emit(OpCodes.Stsfld, ownFieldCS!);
                IL.Emit(OpCodes.Ldloc, ownResultCS);
                SetStackUnknown();
                return;
            }
            if (_ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassNameCS, cs.Name.Lexeme, classBuilderCS, out var inheritedFieldCS))
            {
                EmitStaticFieldLoadWithShadow(resolvedClassNameCS, classBuilderCS, cs.Name.Lexeme, inheritedFieldCS!);
                EmitCompoundOperation(cs.Operator.Type, cs.Value);
                var inheritedResultCS = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, inheritedResultCS);

                IL.Emit(OpCodes.Ldtoken, classBuilderCS);
                IL.Emit(OpCodes.Call, _ctx.Types.TypeGetTypeFromHandle);
                IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
                IL.Emit(OpCodes.Ldloc, inheritedResultCS);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

                IL.Emit(OpCodes.Ldloc, inheritedResultCS);
                SetStackUnknown();
                return;
            }
        }

        // Compound assignment on object property: obj.prop += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // Get current value: GetProperty(obj, name)
        EmitExpression(cs.Object);
        EmitBoxIfNeeded(cs.Object);
        // Compound assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before GetProperty (which would otherwise no-op) (#733).
        if (!IsNullPlaceholderGlobal(cs.Object))
            EmitThrowIfUndefinedReceiverOnStack(cs.Name.Lexeme);
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

        // Numeric typed-array compound-assign fast path (#3): `a[i] OP= v` where `a` is a statically-
        // typed numeric typed array and `v` is statically a number. Reads via GetUnboxed → double,
        // applies the op in native double/int space (the same helper the boxed path uses), writes via
        // SetUnboxed — no GetIndex/Add/SetIndex dispatch and no per-element boxing. Other receivers,
        // operators, RHS types (BigInt/Uint8Clamped have no accessor) fall through to the boxed path.
        if (csi.Object is Expr.Variable
            && _ctx.TypeMap?.Get(csi.Object) is TypeInfo.TypedArray cta
            && _ctx.Runtime!.GetTypedArrayType(cta.ElementType) is { } ctaType
            && _ctx.Runtime!.TypedArrayGetUnboxedByElement.TryGetValue(cta.ElementType, out var ctaGet)
            && _ctx.Runtime!.TypedArraySetUnboxedByElement.TryGetValue(cta.ElementType, out var ctaSet)
            && _ctx.TypeMap?.Get(csi.Value) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral
            && IsArithmeticOrBitwiseCompound(csi.Operator.Type))
        {
            // recv = (TAType)a  — side-effect-free variable, loaded once. Reuse the loop-hoisted
            // typed local directly when available (#928), else cast once into a fresh recvLocal.
            LocalBuilder recvLocal;
            if (csi.Object is Expr.Variable cv && _ctx.TryGetHoistedTypedArray(cv.Name.Lexeme) is { } choist)
            {
                recvLocal = choist.TypedLocal;
            }
            else
            {
                EmitExpression(csi.Object);
                EnsureBoxed();
                IL.Emit(OpCodes.Castclass, ctaType);
                recvLocal = IL.DeclareLocal(ctaType);
                IL.Emit(OpCodes.Stloc, recvLocal);
            }

            // idx = (int)i  (native-int fast path when the index is an integer loop counter, #928)
            EmitIndexAsInt32(csi.Index);
            var idxLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Stloc, idxLocal);

            // cur = recv.GetUnboxed(idx)  → double
            IL.Emit(OpCodes.Ldloc, recvLocal);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Call, ctaGet);

            // result = cur OP v  (double)
            EmitCompoundArithmeticDoubleOnStack(csi.Operator.Type, csi.Value);
            var resLocal = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Stloc, resLocal);

            // recv.SetUnboxed(idx, result)
            IL.Emit(OpCodes.Ldloc, recvLocal);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Ldloc, resLocal);
            IL.Emit(OpCodes.Call, ctaSet);

            // The compound-assignment expression evaluates to the stored numeric result.
            IL.Emit(OpCodes.Ldloc, resLocal);
            SetStackType(StackType.Double);
            return;
        }

        // Spill receiver and index once so the read-guard and the write reuse them
        // (also avoids re-evaluating side-effecting subexpressions twice).
        EmitExpression(csi.Object);
        EmitBoxIfNeeded(csi.Object);
        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        EmitExpression(csi.Index);
        EmitBoxIfNeeded(csi.Index);
        var indexLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Compound assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before GetIndex (which would otherwise no-op) (#733).
        if (!IsNullPlaceholderGlobal(csi.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocal, indexLocal, isWrite: false);

        // Get current value: GetIndex(obj, index)
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);

        // Apply operation
        EmitCompoundOperation(csi.Operator.Type, csi.Value);

        // Store result: SetIndex(obj, index, value)
        var resultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, resultLocal);

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);

        // Leave result on stack
        IL.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    private void EmitCompoundOperation(TokenType opType, Expr value)
    {
        // Stack has current value (object). Apply the operation.
        if (opType == TokenType.PLUS_EQUAL && IsStringExpression(value))
        {
            // String concatenation - we know right side is a string at compile time.
            // StringifyCoerce both operands: JS-compatible conversion (null →
            // "null", undefined → "undefined") that throws TypeError for Symbol
            // operands (§7.1.17) — String.Concat(object, object) did neither.
            IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.String, "Concat", _ctx.Types.String, _ctx.Types.String));
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

        // Convert current value to number, apply the op in double/int space, re-box.
        EmitUnboxToDouble();
        EmitCompoundArithmeticDoubleOnStack(opType, value);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
    }

    // The compound operators handled by the numeric path (arithmetic + bitwise) — i.e. those that
    // the typed-array compound fast path can compute in native double/int space.
    private static bool IsArithmeticOrBitwiseCompound(TokenType op) => op is
        TokenType.PLUS_EQUAL or TokenType.MINUS_EQUAL or TokenType.STAR_EQUAL or
        TokenType.SLASH_EQUAL or TokenType.PERCENT_EQUAL or TokenType.AMPERSAND_EQUAL or
        TokenType.PIPE_EQUAL or TokenType.CARET_EQUAL or TokenType.LESS_LESS_EQUAL or
        TokenType.GREATER_GREATER_EQUAL;

    // Stack in: [cur (double)]. Applies `cur OP value` in native double space (JS bitwise ops route
    // through int32) and leaves the numeric result as a double. Shared by the boxed compound path
    // and the typed-array compound fast path so the two always agree on semantics.
    private void EmitCompoundArithmeticDoubleOnStack(TokenType opType, Expr value)
    {
        bool isBitwise = opType is TokenType.AMPERSAND_EQUAL or TokenType.PIPE_EQUAL
            or TokenType.CARET_EQUAL or TokenType.LESS_LESS_EQUAL or TokenType.GREATER_GREATER_EQUAL;

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
            case TokenType.PLUS_EQUAL: IL.Emit(OpCodes.Add); break;
            case TokenType.MINUS_EQUAL: IL.Emit(OpCodes.Sub); break;
            case TokenType.STAR_EQUAL: IL.Emit(OpCodes.Mul); break;
            case TokenType.SLASH_EQUAL: IL.Emit(OpCodes.Div); break;
            case TokenType.PERCENT_EQUAL: IL.Emit(OpCodes.Rem); break;
            case TokenType.AMPERSAND_EQUAL: IL.Emit(OpCodes.And); break;
            case TokenType.PIPE_EQUAL: IL.Emit(OpCodes.Or); break;
            case TokenType.CARET_EQUAL: IL.Emit(OpCodes.Xor); break;
            case TokenType.LESS_LESS_EQUAL: IL.Emit(OpCodes.Shl); break;
            case TokenType.GREATER_GREATER_EQUAL: IL.Emit(OpCodes.Shr); break;
        }

        if (isBitwise)
        {
            IL.Emit(OpCodes.Conv_R8);
        }
    }

    private bool IsNumericPlusOperation(Expr.Binary b)
    {
        // Check if both operands are known numeric — safe to emit direct IL add
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);

        return IsNumericType(leftType) && IsNumericType(rightType);
    }

    private bool IsStringPlusOperation(Expr.Binary b)
    {
        // Both operands statically string — `+` is pure concatenation with no
        // ToPrimitive ambiguity, so it can lower to String.Concat(string,string).
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);

        return IsStringTypeInfo(leftType) && IsStringTypeInfo(rightType);
    }

    private bool IsStringComparison(Expr.Binary b)
    {
        // Check if both operands are statically string-typed — safe to emit
        // direct string.CompareOrdinal IL for lexicographic JS comparison.
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);

        return IsStringTypeInfo(leftType) && IsStringTypeInfo(rightType);
    }

    private static bool IsStringTypeInfo(TypeSystem.TypeInfo? type) => type switch
    {
        TypeSystem.TypeInfo.String => true,
        TypeSystem.TypeInfo.StringLiteral => true,
        // Union where every branch is string (e.g. a union of string literals
        // produced by widening different literal elements of an array).
        TypeSystem.TypeInfo.Union u => u.FlattenedTypes.All(IsStringTypeInfo),
        _ => false
    };

    /// <summary>
    /// Both operands statically known numeric — safe to emit direct double
    /// comparison without going through JsLessThan.
    /// </summary>
    private bool IsNumericComparison(Expr.Binary b)
    {
        if (_ctx.TypeMap == null) return false;
        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);
        return IsNumericTypeInfo(leftType) && IsNumericTypeInfo(rightType);
    }

    private static bool IsNumericTypeInfo(TypeSystem.TypeInfo? type) => type switch
    {
        TypeSystem.TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } => true,
        TypeSystem.TypeInfo.NumberLiteral => true,
        TypeSystem.TypeInfo.Union u => u.FlattenedTypes.All(IsNumericTypeInfo),
        _ => false
    };

    /// <summary>
    /// Fast path for <c>x === null</c> / <c>x === undefined</c> (and the loose and
    /// negated forms) when exactly one operand is the statically-typed <c>null</c> or
    /// <c>undefined</c> literal. In compiled output JS <c>null</c> is CLR null and JS
    /// <c>undefined</c> is the <c>$Undefined</c> singleton, so the comparison is a
    /// direct reference check (<c>ceq</c>) instead of boxing the value operand and
    /// dispatching <c>Object.Equals</c>/<c>runtime.Equals</c>.
    ///
    /// <para>Strict <c>===</c> distinguishes null from undefined (reference identity);
    /// loose <c>==</c> against either is the nullish test (null OR undefined). NaN is
    /// never null/undefined, so a NaN value operand correctly compares unequal with no
    /// special handling. When both sides are nullish-typed (e.g. <c>null === undefined</c>)
    /// this returns false and the caller falls through to the existing boxed path.</para>
    /// </summary>
    private bool TryEmitNullishEqualityFastPath(Expr.Binary b, bool isStrict, bool isNegated)
    {
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);
        bool leftNullish = leftType is TypeSystem.TypeInfo.Null or TypeSystem.TypeInfo.Undefined;
        bool rightNullish = rightType is TypeSystem.TypeInfo.Null or TypeSystem.TypeInfo.Undefined;

        // Need exactly one side to be the literal; the other is the value being tested.
        if (leftNullish == rightNullish) return false;

        Expr valueExpr = leftNullish ? b.Right : b.Left;
        var literalType = leftNullish ? leftType : rightType;

        // Emit the value operand boxed (matches the boxed path's operand handling).
        EmitExpression(valueExpr);
        EmitBoxIfNeeded(valueExpr);

        if (isStrict)
        {
            // x === null      -> reference-equal to CLR null
            // x === undefined -> reference-equal to the $Undefined singleton
            if (literalType is TypeSystem.TypeInfo.Null)
            {
                IL.Emit(OpCodes.Ldnull);
            }
            else
            {
                EmitUndefinedConstant();
            }
            IL.Emit(OpCodes.Ceq);
        }
        else
        {
            // Loose == / != against null or undefined is the nullish test: the value is
            // CLR null OR the $Undefined singleton.
            var valueLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, valueLocal);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Ceq);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            EmitUndefinedConstant();
            IL.Emit(OpCodes.Ceq);
            IL.Emit(OpCodes.Or);
        }

        if (isNegated)
        {
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Ceq);
        }

        SetStackType(StackType.Boolean);
        return true;
    }

    private bool IsBigIntOperation(Expr.Binary b)
    {
        // Check if either operand has bigint type from the type map
        if (_ctx.TypeMap == null) return false;

        var leftType = _ctx.TypeMap.Get(b.Left);
        var rightType = _ctx.TypeMap.Get(b.Right);

        return leftType is TypeInfo.BigInt or TypeInfo.BigIntLiteral
            || rightType is TypeInfo.BigInt or TypeInfo.BigIntLiteral;
    }

    private bool IsBigIntExpr(Expr expr)
    {
        if (_ctx.TypeMap == null) return false;
        var type = _ctx.TypeMap.Get(expr);
        return type is TypeInfo.BigInt or TypeInfo.BigIntLiteral;
    }

    private bool IsBothBigInt(Expr.Binary b)
    {
        if (_ctx.TypeMap == null) return false;
        return _ctx.TypeMap.Get(b.Left) is TypeInfo.BigInt or TypeInfo.BigIntLiteral
            && _ctx.TypeMap.Get(b.Right) is TypeInfo.BigInt or TypeInfo.BigIntLiteral;
    }

    /// <summary>
    /// Emits a mixed bigint/non-bigint equality op per ECMA-262 7.2.15. Strict
    /// <c>===</c>/<c>!==</c> with differing types is constant-false/true, computed by
    /// the general strict path (Object.Equals(BigInteger, Double) is false). Loose
    /// <c>==</c>/<c>!=</c> coerces via <c>$Runtime.BigIntLooseEquals</c>. Returns false
    /// for non-equality operators (e.g. mixed <c>+</c>), which fall through to the
    /// general operator path. Assumes the operands are not both bigint.
    /// </summary>
    private bool TryEmitMixedBigIntEquality(Expr.Binary b)
    {
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.EQUAL_EQUAL_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                EmitEqualityBinary(b, isStrict: true, isNegated: op == TokenType.BANG_EQUAL_EQUAL);
                return true;

            case TokenType.EQUAL_EQUAL:
            case TokenType.BANG_EQUAL:
                EmitExpression(b.Left);
                EmitBoxIfNeeded(b.Left);
                EmitExpression(b.Right);
                EmitBoxIfNeeded(b.Right);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntLooseEquals);
                if (op == TokenType.BANG_EQUAL)
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ceq);
                }
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                return true;

            default:
                return false;
        }
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
        // Get the appropriate String.Concat overload (using string params, not object)
        var paramTypes = new Type[parts.Count];
        for (int i = 0; i < parts.Count; i++)
            paramTypes[i] = _ctx.Types.String;

        var concatMethod = _ctx.Types.String.GetMethod("Concat", paramTypes);

        // If we can't find the specific overload (e.g., for 4 args), use array version
        if (concatMethod == null)
        {
            EmitStringConcatWithArray(parts);
            return;
        }

        // Emit each part and call Stringify for JS-compatible string conversion
        foreach (var part in parts)
        {
            // String literals can be emitted directly
            if (part is Expr.Literal { Value: string s })
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            else
            {
                EmitExpression(part);
                EmitBoxIfNeeded(part);
                // StringifyCoerce: `+` concat is an implicit ToString coercion —
                // Symbol operands throw TypeError (ECMA-262 §7.1.17).
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);
            }
        }

        IL.Emit(OpCodes.Call, concatMethod);
    }

    /// <summary>
    /// Emits String.Concat using the params string[] overload for 5+ arguments.
    /// </summary>
    private void EmitStringConcatWithArray(List<Expr> parts)
    {
        // Create array: new string[parts.Count]
        IL.Emit(OpCodes.Ldc_I4, parts.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.String);

        // Fill array with parts (Stringify each for JS-compatible conversion)
        for (int i = 0; i < parts.Count; i++)
        {
            IL.Emit(OpCodes.Dup);           // Duplicate array reference
            IL.Emit(OpCodes.Ldc_I4, i);     // Index

            // String literals can be emitted directly
            if (parts[i] is Expr.Literal { Value: string s })
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            else
            {
                EmitExpression(parts[i]);       // Value
                EmitBoxIfNeeded(parts[i]);
                // StringifyCoerce: throws TypeError for Symbol operands (§7.1.17).
                IL.Emit(OpCodes.Call, _ctx.Runtime!.StringifyCoerce);
            }

            IL.Emit(OpCodes.Stelem_Ref);    // Store in array
        }

        // Call String.Concat(string[])
        var concatMethod = _ctx.Types.String.GetMethod("Concat", [_ctx.Types.StringArray]);
        IL.Emit(OpCodes.Call, concatMethod!);
    }

    /// <summary>
    /// Checks if an expression is a built-in module method or static type method access
    /// that should return "function" for typeof.
    /// </summary>
    private bool IsTypeofOnBuiltInMethod(Expr expr)
    {
        if (expr is not Expr.Get g || g.Object is not Expr.Variable v)
            return false;

        var varName = v.Name.Lexeme;

        // globalThis is a general-purpose object — typeof globalThis.X must evaluate at runtime
        // because properties can be objects, functions, or undefined depending on the name.
        if (varName == "globalThis")
            return false;

        // If the type checker resolved this expression as a function type, it's a method
        if (_ctx.TypeMap != null)
        {
            var memberType = _ctx.TypeMap.Get(expr);
            if (memberType is TypeSystem.TypeInfo.Function)
                return true;
        }

        var memberName = g.Name.Lexeme;

        // Check module namespace methods (util.format, readline.questionSync, etc.)
        // Exclude non-method exports (e.g., http.globalAgent, perf.performance)
        if (_ctx.BuiltInModuleNamespaces != null &&
            _ctx.BuiltInModuleNamespaces.TryGetValue(varName, out var moduleName) &&
            _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(moduleName) is { } moduleEmitter)
        {
            var members = moduleEmitter.GetExportedMembers();
            if (members.Contains(memberName) && !moduleEmitter.IsExportedProperty(memberName))
                return true;
        }

        // Check static type methods (Math.floor, JSON.parse, etc.)
        // Exclude known static properties (Math.PI, Symbol.iterator, Number.MAX_VALUE, etc.)
        // which are not functions.
        if (_ctx.TypeEmitterRegistry != null)
        {
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(varName);
            if (staticStrategy != null && !staticStrategy.HasStaticProperty(memberName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an expression is process.stdin, process.stdout, or process.stderr,
    /// which are represented as marker strings in compiled mode but should return "object" for typeof.
    /// </summary>
    private static bool IsTypeofOnProcessStream(Expr expr)
    {
        if (expr is not Expr.Get g)
            return false;

        if (g.Object is Expr.Variable v && v.Name.Lexeme == "process" &&
            g.Name.Lexeme is "stdin" or "stdout" or "stderr")
            return true;

        return false;
    }
}
