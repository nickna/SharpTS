using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for RegExp method calls and property access.
/// Handles TypeScript RegExp methods (test, exec) and properties (source, flags, global, etc.).
/// </summary>
public sealed class RegExpEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a RegExp receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Bail before emitting the receiver for unknown methods — emitting
        // first and returning false would leave an orphaned stack value.
        if (methodName is not ("test" or "exec"))
            return false;

        if (methodName == "test" && IsStableIntrinsicTest(emitter, receiver, arguments))
        {
            // The receiver is the exact non-global/non-sticky literal node that
            // RegexLiteralHoistAnalyzer proved cannot escape. With the prototype
            // bindings also stable, call $RegExp.Test directly: its non-stateful
            // branch is Regex.IsMatch and returns a native IL bool.
            emitter.EmitExpression(receiver);
            emitter.EmitBoxIfNeeded(receiver);
            il.Emit(OpCodes.Castclass, ctx.Runtime!.TSRegExpType);
            EmitStableStringArgument(emitter, arguments);
            il.Emit(OpCodes.Callvirt, ctx.Runtime.TSRegExpTestMethod);
            emitter.SetStackType(StackType.Boolean);
            return true;
        }

        // Escaped/aliased receivers and every observable or ambiguous case use
        // ordinary property lookup. This preserves own/prototype method and
        // accessor overrides, then lets RegExp.prototype.test perform the full
        // RegExpExec operation (including a custom exec and strict lastIndex).
        EmitDynamicMethodCall(emitter, receiver, methodName, arguments);
        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a RegExp receiver.
    /// Handles: source, flags, global, ignoreCase, multiline, lastIndex.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Resolve the handler BEFORE emitting the receiver: returning false
        // after emitting would leave an orphaned value on the IL stack and
        // produce an invalid program when the caller falls back to another
        // emission path.
        var getter = propertyName switch
        {
            "source" => ctx.Runtime!.RegExpGetSource,
            "flags" => ctx.Runtime!.RegExpGetFlags,
            "global" => ctx.Runtime!.RegExpGetGlobal,
            "ignoreCase" => ctx.Runtime!.RegExpGetIgnoreCase,
            "multiline" => ctx.Runtime!.RegExpGetMultiline,
            "sticky" => ctx.Runtime!.RegExpGetSticky,
            "unicode" => ctx.Runtime!.RegExpGetUnicode,
            "dotAll" => ctx.Runtime!.RegExpGetDotAll,
            "hasIndices" => ctx.Runtime!.RegExpGetHasIndices,
            "unicodeSets" => ctx.Runtime!.RegExpGetUnicodeSets,
            // lastIndex may hold any assigned JS value until RegExpBuiltinExec
            // performs ToLength, so it must use the object-valued property path.
            "lastIndex" => ctx.Runtime!.GetProperty,
            _ => null
        };
        if (getter is null)
            return false;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        if (propertyName == "lastIndex")
            il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, getter);

        switch (propertyName)
        {
            case "source":
            case "flags":
                break; // already a string reference
            case "lastIndex":
                break; // GetProperty already returns a boxed JS value.
            default:
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                break;
        }
        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a RegExp receiver.
    /// RegExp writes use the generic descriptor-aware property path.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    private static bool IsStableIntrinsicTest(
        IEmitterContext emitter, Expr receiver, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        if (ctx.RuntimeFeatures?.UsesRegExpPrototypeMutation != false
            || receiver is not Expr.RegexLiteral literal
            || ctx.Runtime?.RegexHoistFields?.ContainsKey(literal) != true
            || arguments.Count > 1)
            return false;

        return arguments.Count == 0 || IsSideEffectFreeString(arguments[0], ctx);
    }

    private static bool IsSideEffectFreeString(Expr expression, CompilationContext ctx)
    {
        while (true)
        {
            switch (expression)
            {
                case Expr.Grouping grouping:
                    expression = grouping.Expression;
                    continue;
                case Expr.TypeAssertion assertion:
                    expression = assertion.Expression;
                    continue;
                case Expr.Satisfies satisfies:
                    expression = satisfies.Expression;
                    continue;
                case Expr.NonNullAssertion nonNull:
                    expression = nonNull.Expression;
                    continue;
            }

            break;
        }

        if (expression is not Expr.Variable and not Expr.Literal { Value: string })
            return false;

        return ctx.TypeMap?.Get(expression) is TypeSystem.TypeInfo.String
            or TypeSystem.TypeInfo.StringLiteral;
    }

    private static void EmitStableStringArgument(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Call, ctx.Runtime!.Stringify);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "undefined");
        }
    }

    private static void EmitDynamicMethodCall(
        IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        var receiverLocal = emitter.SpillStackToObjectLocal();

        // Property lookup precedes ArgumentListEvaluation.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Call, ctx.Runtime!.GetProperty);
        var functionLocal = emitter.SpillStackToObjectLocal();

        if (arguments.Any(argument => argument is Expr.Spread))
            emitter.EmitArgsArrayWithSpread(arguments);
        else
            emitter.EmitArgsArray(arguments);
        var argumentsLocal = emitter.SpillStackToObjectLocal();

        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldloc, functionLocal);
        il.Emit(OpCodes.Ldloc, argumentsLocal);
        il.Emit(OpCodes.Castclass, ctx.Types.ObjectArray);
        il.Emit(OpCodes.Call, ctx.Runtime.InvokeMethodValue);
        emitter.SetStackUnknown();
    }

    #endregion
}
