using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitStableNumberIteratorResult(
        ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var builder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$StableNumberIteratorResult",
            TypeAttributes.Public | TypeAttributes.Sealed |
            TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit,
            _types.ValueType);
        var value = builder.DefineField("Value", _types.Double, FieldAttributes.Public);
        var done = builder.DefineField("Done", _types.Boolean, FieldAttributes.Public);
        var ctor = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Double, _types.Boolean]);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Initobj, builder);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, done);
        il.Emit(OpCodes.Ret);

        Type resultType = builder.CreateType()!;
        runtime.StableNumberIteratorResultType = resultType;
        runtime.StableNumberIteratorResultCtor = resultType.GetConstructor(
            [_types.Double, _types.Boolean])!;
        runtime.StableNumberIteratorResultValueField = resultType.GetField("Value")!;
        runtime.StableNumberIteratorResultDoneField = resultType.GetField("Done")!;
    }
}
