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
        if (_ctx.ReturnValueLocal != null || _ctx.HasDeferredVoidReturn)
        {
            // Mark the return label and emit the actual return
            // Use builder's MarkLabel since ReturnLabel was defined with builder
            _ctx.ILBuilder.MarkLabel(_ctx.ReturnLabel);
            if (_ctx.ReturnValueLocal != null)
                IL.Emit(OpCodes.Ldloc, _ctx.ReturnValueLocal);
            IL.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Check if the method had returns inside exception blocks that need finalization.
    /// </summary>
    public bool HasDeferredReturns => _ctx.ReturnValueLocal != null || _ctx.HasDeferredVoidReturn;

    /// <summary>
    /// Emit default parameter value checks at function entry.
    /// For each parameter with a default value, checks if arg is the JavaScript
    /// <c>undefined</c> sentinel and assigns the default.
    /// </summary>
    /// <param name="parameters">The parameter list</param>
    /// <param name="isInstanceMethod">True if this is an instance method (has implicit this at arg 0)</param>
    /// <param name="hasOwnThis">True if function has __this as first explicit parameter</param>
    /// <param name="paramTypes">Optional resolved parameter types. When provided, value-type
    /// parameters are skipped — the null-check pattern (<c>ldarg; brtrue</c>) does not apply
    /// to value types since they cannot be null. Value-type defaults are still handled by
    /// <see cref="OverloadGenerator"/> for direct calls; the $TSFunction.Invoke path does
    /// not support user-specified value-type defaults.</param>
    public void EmitDefaultParameters(
        List<Stmt.Parameter> parameters,
        bool isInstanceMethod,
        bool hasOwnThis = false,
        Type[]? paramTypes = null)
        => EmitDefaultParameters(
            parameters,
            argumentOffset: (isInstanceMethod ? 1 : 0) + (hasOwnThis ? 1 : 0),
            paramTypes: paramTypes);

    /// <summary>
    /// Offset-based parameter-default lowering used by the shared function
    /// environment prologue. Unlike the compatibility overload, this directly
    /// describes the emitted signature and therefore also covers display-class
    /// and explicit-<c>this</c> prefixes without reconstructing their shape.
    /// </summary>
    internal void EmitDefaultParameters(
        List<Stmt.Parameter> parameters,
        int argumentOffset,
        Type[]? paramTypes = null)
    {
        var builder = _ctx.ILBuilder;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null) continue;

            // Skip value-type parameters when we know the resolved types: `ldarg; brtrue`
            // is meaningless on a double/bool, which can't hold the `$Undefined` sentinel.
            // A *defaulted* param is widened to an `object` slot upstream (ParameterTypeResolver
            // WidenDefaultedParamsToObject / WidenValueTypeDefaultedMethodParams), so it does NOT
            // hit this guard — its default fires for direct AND $TSFunction.Invoke callers (#925).
            // This only defends a genuinely value-typed slot that reached here without that widening.
            if (paramTypes != null && paramTypes[i].IsValueType) continue;

            int argIndex = i + argumentOffset;

            // JS spec: defaults fire only when the argument is `undefined` — missing
            // arguments are padded with the $Undefined singleton by call emitters.
            // CLR null represents the JavaScript null value and must be preserved.
            var fireDefault = builder.DefineLabel($"fire_default_{i}");
            var skipDefault = builder.DefineLabel($"skip_default_{i}");

            if (_ctx.Runtime?.UndefinedInstance != null)
            {
                IL.Emit(OpCodes.Ldarg, argIndex);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
                IL.Emit(OpCodes.Beq, fireDefault);
            }

            IL.Emit(OpCodes.Br, skipDefault);

            builder.MarkLabel(fireDefault);
            _ctx.DefaultParameterTdzNames = parameters
                .Skip(i)
                .Select(p => p.Name.Lexeme)
                .ToHashSet(StringComparer.Ordinal);
            EmitExpression(param.DefaultValue);
            _ctx.DefaultParameterTdzNames = null;
            EmitBoxIfNeeded(param.DefaultValue);
            IL.Emit(OpCodes.Starg, argIndex);

            builder.MarkLabel(skipDefault);
        }
    }

    /// <summary>
    /// Routes the shared static-dispatch helpers' extra-arg boxing through
    /// EmitBoxIfNeeded so ILEmitter keeps its TypeMap/literal-aware boxing
    /// (the base seam defaults to stack-state-only EnsureBoxed).
    /// </summary>
    protected override void EnsureBoxedArg(Expr arg) => EmitBoxIfNeeded(arg);

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
            if (type is TypeInfo.String
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

        // Expressions may produce unboxed value types: literals, arithmetic (StackType.Double),
        // comparisons (StackType.Boolean), and typed locals. The _stackType checks above
        // handle these cases. The literal check below is a fallback for when _stackType
        // tracking doesn't catch a literal (e.g., in complex expression trees).
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

    /// <summary>
    /// Emits conversion from the current stack value to the target parameter type.
    /// Handles boxing for object, unboxing for value types, and pass-through for matching types.
    /// </summary>
    public new void EmitConversionForParameter(Expr expr, Type targetType)
    {
        // If target is object, box value types
        if (targetType == typeof(object))
        {
            EmitBoxIfNeeded(expr);
            return;
        }

        // Check if the expression produces a matching type
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double && targetType == typeof(double))
            {
                // Already emitted as double, no conversion needed
                return;
            }
            if (lit.Value is bool && targetType == typeof(bool))
            {
                // Already emitted as bool, no conversion needed
                return;
            }
            if (lit.Value is string && targetType == typeof(string))
            {
                // Already emitted as string, no conversion needed
                return;
            }
        }

        // If stack has a known type matching target, no conversion needed
        if (_stackType == StackType.Double && targetType == typeof(double))
            return;
        if (_stackType == StackType.Boolean && targetType == typeof(bool))
            return;

        // Check if target is a union type using marker interface
        if (UnionTypeHelper.IsUnionType(targetType))
        {
            // Determine source type from stack or expression
            Type? sourceType = _stackType switch
            {
                StackType.Double => typeof(double),
                StackType.Boolean => typeof(bool),
                StackType.String => typeof(string),
                _ => null
            };

            // If stack type is unknown, try to determine from expression
            if (sourceType == null && expr is Expr.Literal exprLit)
            {
                sourceType = exprLit.Value switch
                {
                    double => typeof(double),
                    string => typeof(string),
                    bool => typeof(bool),
                    _ => null
                };
            }

            // Try to find an implicit conversion operator
            if (sourceType != null && _ctx.UnionGenerator != null)
            {
                var implicitOp = _ctx.UnionGenerator.GetImplicitConversion(targetType, sourceType);

                if (implicitOp != null)
                {
                    IL.Emit(OpCodes.Call, implicitOp);
                    return;
                }
            }

            // Fallback: box the value and create a default union
            // This won't work correctly but prevents crashes
            EmitBoxIfNeeded(expr);
            var valueLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, valueLocal);
            var unionLocal = IL.DeclareLocal(targetType);
            IL.Emit(OpCodes.Ldloca, unionLocal);
            IL.Emit(OpCodes.Initobj, targetType);
            IL.Emit(OpCodes.Ldloc, unionLocal);
            return;
        }

        // If target is a value type and we have an object, unbox
        if (targetType.IsValueType)
        {
            if (_stackType != StackType.Double && _stackType != StackType.Boolean)
                IL.Emit(OpCodes.Unbox_Any, targetType);
            return;
        }

        // Target is a non-object reference type. EmitExpression can leave a broader `object` on the
        // stack — an `any`-typed value, or a GetIndex/GetProperty result — that the callee's typed
        // string slot would reject at IL verification even though the JIT tolerates it. Emit the
        // downcast the verifier needs (#1246). Only `string` qualifies: it is the one non-object
        // reference slot whose runtime value is genuinely a CLR instance of the slot. Other reference
        // targets carry a boxed-differently runtime value ($TSFunction for a Delegate, $Array for a
        // List, $Promise for a Task — the last widened to `object` at the parameter slot by
        // ParameterTypeResolver), so a castclass would throw; they keep their prior object store.
        if (_ctx.Types.IsString(targetType) && _stackType != StackType.String)
        {
            EmitBoxIfNeeded(expr);
            IL.Emit(OpCodes.Castclass, targetType);
        }
    }

    /// <summary>
    /// Emits a default value for the given type.
    /// Used when padding missing arguments (fallback when no overload matches).
    /// </summary>
    public new void EmitDefaultForType(Type type)
    {
        if (type == typeof(double))
        {
            IL.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else if (type == typeof(int))
        {
            IL.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(bool))
        {
            IL.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(float))
        {
            IL.Emit(OpCodes.Ldc_R4, 0.0f);
        }
        else if (type == typeof(long))
        {
            IL.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (type.IsValueType)
        {
            // For other value types, use initobj with a local
            var local = IL.DeclareLocal(type);
            IL.Emit(OpCodes.Ldloca, local);
            IL.Emit(OpCodes.Initobj, type);
            IL.Emit(OpCodes.Ldloc, local);
        }
        else
        {
            // Reference types default to null
            IL.Emit(OpCodes.Ldnull);
        }
    }

    public override void EmitExpressionAsDouble(Expr expr)
    {
        // A number[] read consumed as a number can keep the numeric-mode $Array
        // result native while its guarded cold arm performs the same ordinary
        // property lookup + ToNumber coercion this method used previously.
        if (expr is Expr.GetIndex getIndex && TryEmitNumberArrayGetIndexAsDouble(getIndex))
            return;

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

    protected override void EmitTruthyCheck()
    {
        if (_ctx.Runtime?.IsTruthy != null)
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime.IsTruthy);
            return;
        }

        // Truthy check for boxed value:
        // - null => false
        // - boxed false => false
        // - everything else => true
        var builder = _ctx.ILBuilder;
        var checkBoolLabel = builder.DefineLabel("truthy_checkbool");
        var falseLabel = builder.DefineLabel("truthy_false");
        var endLabel = builder.DefineLabel("truthy_end");

        // Check for null
        IL.Emit(OpCodes.Dup);
        builder.Emit_Brfalse(falseLabel);

        // Check if it's a boolean
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Boolean);
        builder.Emit_Brfalse(checkBoolLabel);

        // It's a boxed bool - unbox and use the value
        IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(checkBoolLabel);
        // Not null and not bool - always truthy
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(falseLabel);
        // Null - false
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_0);

        builder.MarkLabel(endLabel);
    }

    private static bool IsComparisonOp(TokenType op) =>
        op is TokenType.LESS or TokenType.GREATER or TokenType.LESS_EQUAL or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL;
}
