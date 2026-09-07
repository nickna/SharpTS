using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    // Called only at an existing numeric-consumer boundary. Keep the initial
    // capability narrow: synchronous detached calls, four actual native doubles.
    private bool TryEmitTypedNumericIndirectCall(Expr.Call call)
    {
        if (call.Optional || call.Callee is not Expr.Variable variable
            || call.Arguments.Count != 4 || AnyContainsSuspension(call.Arguments)
            || call.Arguments.Any(arg => arg is Expr.Spread || !IsNumericType(_ctx.TypeMap?.Get(arg)))
            || !IsNumericType(_ctx.TypeMap?.Get(call))
            || _ctx.NumericRestCallMethods?.ContainsKey(call) == true)
            return false;

        // Leave statically resolved calls and builtin handlers in their existing
        // dispatch chain. Parameters, mutable locals and live exports are values.
        if (!Resolver.HasVariable(variable.Name.Lexeme))
            return false;

        EmitExpression(call.Callee);
        EnsureBoxed();
        var callee = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, callee);
        var arguments = new LocalBuilder[4];
        bool native = true;
        for (int i = 0; i < 4; i++)
        {
            EmitExpression(call.Arguments[i]);
            bool isDouble = StackType == StackType.Double;
            if (!isDouble) EnsureBoxedArg(call.Arguments[i]);
            arguments[i] = IL.DeclareLocal(isDouble ? _ctx.Types.Double : _ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, arguments[i]);
            native &= isDouble;
        }

        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        if (native)
        {
            var wrapper = IL.DeclareLocal(_ctx.Runtime!.TSFunctionType);
            var entryType = typeof(Func<double, double, double, double, double>);
            var entry = IL.DeclareLocal(entryType);
            IL.Emit(OpCodes.Ldloc, callee);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime.TSFunctionType);
            IL.Emit(OpCodes.Stloc, wrapper);
            IL.Emit(OpCodes.Ldloc, wrapper);
            IL.Emit(OpCodes.Brfalse, fallback);
            IL.Emit(OpCodes.Ldloc, wrapper);
            IL.Emit(OpCodes.Ldfld, _ctx.Runtime.TSFunctionNumericRest4Field);
            IL.Emit(OpCodes.Stloc, entry);
            IL.Emit(OpCodes.Ldloc, entry);
            IL.Emit(OpCodes.Brfalse, fallback);
            IL.Emit(OpCodes.Ldloc, entry);
            foreach (var argument in arguments) IL.Emit(OpCodes.Ldloc, argument);
            IL.Emit(OpCodes.Callvirt, entryType.GetMethod("Invoke")!);
            IL.Emit(OpCodes.Br, done);
        }

        // Reuse the captured callable and evaluated values, including when an
        // argument replaced the source binding or the target has no capability.
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
        IL.Emit(OpCodes.Ldloc, callee);
        IL.Emit(OpCodes.Ldc_I4_4);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < 4; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, arguments[i]);
            if (arguments[i].LocalType == _ctx.Types.Double) IL.Emit(OpCodes.Box, _ctx.Types.Double);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);
        SetStackUnknown();
        EnsureDouble();
        IL.MarkLabel(done);
        SetStackType(StackType.Double);
        return true;
    }
}
