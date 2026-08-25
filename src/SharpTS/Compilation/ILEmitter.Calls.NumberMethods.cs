using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Number instance method dispatch for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override bool TryEmitIntegerCounterDecimalString(Expr expression)
    {
        if (!IsIntegerCounterValueI8(expression))
            return false;

        IL.Emit(OpCodes.Ldstr, "");
        _ = TryEmitIntegerCounterValueI8(expression);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ConcatStringInt64);
        SetStackType(StackType.String);
        return true;
    }

    /// <summary>
    /// Emits a BigInt instance-method call (receiver statically bigint). toString takes
    /// an optional radix (ECMA-262 21.2.3.3); toLocaleString is the decimal form;
    /// valueOf returns the bigint itself.
    /// </summary>
    private void EmitBigIntMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        EmitExpression(obj);
        EmitBoxIfNeeded(obj); // boxed BigInteger receiver

        switch (methodName)
        {
            case "valueOf":
                // Returns the bigint itself (already boxed on the stack).
                SetStackUnknown();
                break;

            case "toLocaleString":
                // No Intl options — plain decimal (radix 10).
                IL.Emit(OpCodes.Ldc_R8, 10.0);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntToStringRadix);
                SetStackType(StackType.String);
                break;

            case "toString":
                if (arguments.Count > 0)
                    EmitExpressionAsDouble(arguments[0]);
                else
                    IL.Emit(OpCodes.Ldc_R8, 10.0);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.BigIntToStringRadix);
                SetStackType(StackType.String);
                break;
        }
    }

    /// <summary>
    /// Emits a number method call when we know the receiver is a number at compile time.
    /// </summary>
    private void EmitNumberMethodCallDirect(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the number value
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        // Emit appropriate runtime method call based on method name
        switch (methodName)
        {
            case "toFixed":
                // Emit digits argument (default 0)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToFixed);
                break;

            case "toPrecision":
                // Emit precision argument (default null for default behavior)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToPrecision);
                break;

            case "toExponential":
                // 0-arg call signals JS `undefined` (shortest-form branch), not
                // null (which ToInteger-coerces to 0). Without this distinction,
                // `(123.456).toExponential()` produced "1e+2" instead of
                // "1.23456e+2" per ECMA-262 §21.1.3.2 step 9.a.
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToExponential);
                break;

            case "valueOf":
                // valueOf just returns the number itself (already on stack, boxed)
                break;

            case "toString":
                // Emit radix argument (default 10)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4, 10);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToStringRadix);
                break;
        }
    }

    /// <summary>
    /// Emits a number method call with runtime type checking for any/unknown types.
    /// Checks if the receiver is a Double at runtime and dispatches accordingly.
    /// </summary>
    private void EmitNumberMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object and store in local
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        var objLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocal);

        // This dispatcher also owns the any/unknown `toString` and `valueOf`
        // spellings. Enforce the member-call RequireObjectCoercible step here
        // before either lookup or argument evaluation; otherwise
        // `undefined.toString()` falls through GetProperty and returns
        // undefined instead of throwing TypeError.
        EmitThrowIfReceiverUndefined(objLocal, methodName);

        var builder = _ctx.ILBuilder;
        var isDoubleLabel = builder.DefineLabel("number_method_double");
        var fallbackLabel = builder.DefineLabel("number_method_fallback");
        var doneLabel = builder.DefineLabel("number_method_done");

        // Check if it's a Double
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Double);
        builder.Emit_Brtrue(isDoubleLabel);

        // Fall through to dynamic dispatch
        builder.Emit_Br(fallbackLabel);

        // Double path - call the appropriate number method
        builder.MarkLabel(isDoubleLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);

        switch (methodName)
        {
            case "toFixed":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToFixed);
                break;

            case "toPrecision":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToPrecision);
                break;

            case "toExponential":
                // 0-arg → JS `undefined` (shortest-form branch). Push
                // UndefinedInstance, not Ldnull (null coerces to 0). Same
                // distinction as the typed-double dispatch site above.
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToExponential);
                break;

            case "valueOf":
                // valueOf just returns the number (already loaded)
                break;

            case "toString":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_I4, 10);
                    IL.Emit(OpCodes.Box, _ctx.Types.Int32);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberToStringRadix);
                break;
        }
        builder.Emit_Br(doneLabel);

        // Fallback path - use dynamic dispatch via GetProperty/InvokeMethodValue
        builder.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);  // receiver
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

        // Create args array
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

        builder.MarkLabel(doneLabel);
    }
}
