using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Number static method calls and property access.
/// Handles Number.parseInt(), Number.parseFloat(), Number.isNaN(), etc.
/// and Number.MAX_VALUE, Number.MIN_VALUE, Number.NaN, etc.
/// </summary>
public sealed class NumberStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Number static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "parseInt":
                EmitParseInt(emitter, arguments,
                    EmitsUnboxedDecimalParseInt(emitter, arguments));
                return true;
            case "parseFloat":
                EmitParseFloat(emitter, arguments);
                return true;
            case "isNaN":
                // Number.isNaN is stricter than global isNaN - only returns true for actual NaN
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsNaN);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isFinite":
                // Number.isFinite is stricter than global isFinite
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsFinite);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isInteger":
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsInteger);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isSafeInteger":
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsSafeInteger);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a Number static property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // `Number.prototype` — singleton dict, populated lazily with $TSFunction
        // wrappers for toFixed/toPrecision/toExponential (and stubs for
        // toString/valueOf/toLocaleString). Required for Test262
        // `not-a-constructor.js` probes.
        if (propertyName == "prototype")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.NumberPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, ctx.Runtime!.NumberPrototypeField);
            return true;
        }

        switch (propertyName)
        {
            case "MAX_VALUE":
                il.Emit(OpCodes.Ldc_R8, double.MaxValue);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MIN_VALUE":
                il.Emit(OpCodes.Ldc_R8, double.Epsilon); // JS MIN_VALUE = smallest positive
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "NaN":
                il.Emit(OpCodes.Ldc_R8, double.NaN);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "POSITIVE_INFINITY":
                il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "NEGATIVE_INFINITY":
                il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MAX_SAFE_INTEGER":
                il.Emit(OpCodes.Ldc_R8, 9007199254740991.0); // 2^53 - 1
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MIN_SAFE_INTEGER":
                il.Emit(OpCodes.Ldc_R8, -9007199254740991.0); // -(2^53 - 1)
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "EPSILON":
                il.Emit(OpCodes.Ldc_R8, 2.220446049250313e-16); // 2^-52
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            // Constructor metadata properties (ECMA-262 §21.1.2): Number.length is 1, name is "Number".
            case "length":
                il.Emit(OpCodes.Ldc_R8, 1.0);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "name":
                il.Emit(OpCodes.Ldstr, "Number");
                return true;
        }

        // Method references as values (issue #60). Wrap the matching $Runtime
        // helper in a $TSFunction so `var isInt = Number.isInteger; isInt(42)`
        // dispatches correctly.
        var runtime = ctx.Runtime!;
        MethodInfo? method = propertyName switch
        {
            "isNaN"         => runtime.NumberIsNaN,
            "isFinite"      => runtime.NumberIsFinite,
            "isInteger"     => runtime.NumberIsInteger,
            "isSafeInteger" => runtime.NumberIsSafeInteger,
            // Stage 4y: parseInt/parseFloat exposed as values too. The runtime
            // helpers already exist (used by the static-call path); just wrap
            // them as $TSFunction so `let p = Number.parseInt; p("42")` works.
            "parseInt"      => runtime.NumberParseInt,
            "parseFloat"    => runtime.NumberParseFloat,
            _ => null
        };
        if (method == null) return false;

        // ECMA-262 §17 built-in `name` + spec `length`. `parseInt(string, radix)`
        // is the only Number static with arity 2.
        int specLength = propertyName == "parseInt" ? 2 : 1;
        ctx.Types.EmitLoadMethodInfo(il, method);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldc_I4, specLength);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

    #region Helper Methods

    private static void EmitSingleArgMethod(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    internal static bool EmitsUnboxedDecimalParseInt(
        IEmitterContext emitter,
        IReadOnlyList<Expr> arguments)
        => emitter.Context.RuntimeFeatures?.UsesNumberConstructorMutation != true
            && !emitter.HasVariable("Number")
            && arguments.Count == 2
            && (IsStaticallyString(emitter.Context.TypeMap?.Get(arguments[0]))
                || emitter is ExpressionEmitterBase expressionEmitter
                    && expressionEmitter.CanEmitStableIntegerCounterParseInt(
                        arguments[0]))
            && ExpressionEmitterBase.TryGetInt32Literal(arguments[1], out int radix)
            && radix == 10;

    private static bool IsStaticallyString(TypeInfo? type) => type switch
    {
        TypeInfo.String => true,
        TypeInfo.StringLiteral => true,
        TypeInfo.Union union => union.Types.Count > 0
            && union.Types.All(IsStaticallyString),
        _ => false
    };

    private static void EmitParseInt(
        IEmitterContext emitter,
        List<Expr> arguments,
        bool emitDecimalFastPath)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (emitDecimalFastPath)
        {
            if (emitter is ExpressionEmitterBase expressionEmitter
                && expressionEmitter.TryEmitStableIntegerCounterParseInt(
                    arguments[0]))
            {
                return;
            }

            emitter.EmitExpression(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
            il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseIntDecimalString);
            return;
        }

        // Emit string argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit radix (default 10)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, 10);
            il.Emit(OpCodes.Box, ctx.Types.Int32);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseInt);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitParseFloat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseFloat);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    #endregion

    public bool HasStaticProperty(string memberName) => memberName is
        "MAX_VALUE" or "MIN_VALUE" or "NaN" or "POSITIVE_INFINITY" or
        "NEGATIVE_INFINITY" or "MAX_SAFE_INTEGER" or "MIN_SAFE_INTEGER" or "EPSILON"
        or "isNaN" or "isFinite" or "isInteger" or "isSafeInteger"
        or "parseInt" or "parseFloat"
        or "length" or "name" or "prototype";
}
