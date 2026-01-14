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
            // Use builder's MarkLabel since ReturnLabel was defined with builder
            _ctx.ILBuilder.MarkLabel(_ctx.ReturnLabel);
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
        var builder = _ctx.ILBuilder;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null) continue;

            int argIndex = i + argOffset;

            // if (arg == null) { arg = <default>; }
            var skipDefault = builder.DefineLabel($"skip_default_{i}");

            // Load argument and check if null
            IL.Emit(OpCodes.Ldarg, argIndex);
            builder.Emit_Brtrue(skipDefault);

            // Argument is null, emit default value and store
            EmitExpression(param.DefaultValue);
            EmitBoxIfNeeded(param.DefaultValue);
            IL.Emit(OpCodes.Starg, argIndex);

            builder.MarkLabel(skipDefault);
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

    /// <summary>
    /// Emits conversion from the current stack value to the target parameter type.
    /// Handles boxing for object, unboxing for value types, and pass-through for matching types.
    /// </summary>
    public void EmitConversionForParameter(Expr expr, Type targetType)
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
        if (targetType.IsValueType && _stackType != StackType.Double && _stackType != StackType.Boolean)
        {
            IL.Emit(OpCodes.Unbox_Any, targetType);
        }
    }

    /// <summary>
    /// Emits a default value for the given type.
    /// Used when padding missing arguments (fallback when no overload matches).
    /// </summary>
    public void EmitDefaultForType(Type type)
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

    public void EmitExpressionAsDouble(Expr expr)
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
