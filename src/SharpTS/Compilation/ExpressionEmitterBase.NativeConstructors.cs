using System.Reflection.Emit;
using SharpTS.Parsing;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private void EmitNativeConstructorClosure(LocalBuilder constructor, Expr.New expression,
        TypeInfo.InstantiatedGeneric? checkedArguments, Type[] arguments,
        EmittedNativeConstructorMetadataRuntime native)
    {
        var done = IL.DefineLabel();
        var type = IL.DeclareLocal(Types.Type);
        IL.Emit(OpCodes.Ldloc, constructor);
        IL.Emit(OpCodes.Isinst, Types.Type);
        IL.Emit(OpCodes.Stloc, type);
        IL.Emit(OpCodes.Ldloc, type); IL.Emit(OpCodes.Brfalse, done);
        IL.Emit(OpCodes.Ldloc, type);
        if (arguments.Length == 0) IL.Emit(OpCodes.Ldnull);
        else
        {
            IL.Emit(OpCodes.Ldc_I4, arguments.Length); IL.Emit(OpCodes.Newarr, Types.Type);
            for (int index = 0; index < arguments.Length; index++)
            {
                IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, index);
                IL.Emit(OpCodes.Ldtoken, Types.IsVoid(arguments[index]) ? Types.Object : arguments[index]);
                IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle); IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        // These remain the legacy argument-selection inputs, never an ownership
        // certificate. The helper selects only from actual value producers.
        var checkedDefinition = checkedArguments?.GenericDefinition
            ?? Ctx.TypeMap?.Get(ExpressionUnwrapper.Unwrap(expression.Callee));
        Type checkedOwner = checkedDefinition == null ? Types.Object : Ctx.TypeMapper.MapTypeInfoStrict(checkedDefinition);
        IL.Emit(OpCodes.Ldtoken, checkedOwner); IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, native.CloseEvaluated); IL.Emit(OpCodes.Stloc, constructor);
        IL.MarkLabel(done);
    }
}