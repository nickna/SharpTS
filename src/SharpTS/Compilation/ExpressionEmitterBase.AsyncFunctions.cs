using System.Reflection.Emit;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    // Consumes the existing capture/outer target and leaves one function value.
    protected void EmitAsyncFunctionValue(AsyncArrowStateMachineBuilder builder)
    {
        if (builder.PrivateSelfField == null)
        {
            Types.EmitLoadMethodInfoViaHandle(IL, builder.StubMethod);
            NativeSourceFunctionEmission.EmitConstructor(IL, Types, Ctx.Runtime!, builder.StubMethod,
                generics: Ctx.FunctionGenericParameters);
            SetStackUnknown();
            return;
        }

        var target = IL.DeclareLocal(Types.Object);
        var environment = IL.DeclareLocal(Types.ObjectArray);
        var function = IL.DeclareLocal(Ctx.Runtime!.FunctionValues.Type);
        IL.Emit(OpCodes.Stloc, target);
        IL.Emit(OpCodes.Ldc_I4_2);
        IL.Emit(OpCodes.Newarr, Types.Object);
        IL.Emit(OpCodes.Stloc, environment);
        IL.Emit(OpCodes.Ldloc, environment);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ldloc, target);
        IL.Emit(OpCodes.Stelem_Ref);
        IL.Emit(OpCodes.Ldloc, environment);
        Types.EmitLoadMethodInfoViaHandle(IL, builder.StubMethod);
        NativeSourceFunctionEmission.EmitConstructor(IL, Types, Ctx.Runtime, builder.StubMethod,
            generics: Ctx.FunctionGenericParameters);
        IL.Emit(OpCodes.Stloc, function);
        IL.Emit(OpCodes.Ldloc, environment);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Stelem_Ref);
        IL.Emit(OpCodes.Ldloc, function);
        SetStackUnknown();
    }
}
