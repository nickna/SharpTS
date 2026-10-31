using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ObjectDescriptorValueFormInputs(
        MethodInfo Lookup, MethodInfo CreateException, ConstructorInfo TypeErrorConstructor,
        Type UndefinedType, ConstructorInfo PadUndefinedConstructor);

    private void EmitObjectGetOwnDescriptorValueForm(EmittedObjectDescriptorRuntime descriptors,
        ObjectDescriptorValueFormInputs inputs)
    {
        var method = descriptors.GetOwnPropertyDescriptorValueForm;
        method.SetCustomAttribute(inputs.PadUndefinedConstructor, CustomAttributeEncoder.EmptyBlob);
        var il = method.GetILGenerator();
        var invalid = il.DefineLabel();
        var valid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, valid);
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor,
            "Object.getOwnPropertyDescriptor called on null or undefined");
        il.MarkLabel(valid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.Lookup);
        il.Emit(OpCodes.Ret);
        descriptors.MarkGetOwnPropertyDescriptorValueFormBodyEmitted();
    }
}
