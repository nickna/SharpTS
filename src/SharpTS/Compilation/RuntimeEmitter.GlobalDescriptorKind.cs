using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitConfigurableGlobalIntrinsicNames(ILGenerator il, FieldInfo namesField)
    {
        var names = _features.UsesConsoleObject
            ? BuiltInNames.ConfigurableGlobalIntrinsicNames.Append(BuiltInNames.Console)
            : BuiltInNames.ConfigurableGlobalIntrinsicNames;

        // Descriptor lookup and ordered own-key enumeration share this table.
        il.Emit(OpCodes.Ldstr, string.Join(' ', names));
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)' ');
        il.Emit(OpCodes.Ldc_I4_0); // StringSplitOptions.None
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", _types.Char, _types.StringSplitOptions));
        il.Emit(OpCodes.Stsfld, namesField);
    }

    private void EmitGlobalThisIntrinsicDescriptorKind(EmittedGlobalObjectRuntime globalObject)
    {
        var il = globalObject.IntrinsicDescriptorKind.GetILGenerator();
        var writable = il.DefineLabel();
        var constant = il.DefineLabel();
        // The table carries property attributes only. Callers check live presence.
        var arrayType = _types.ArrayType;
        il.Emit(OpCodes.Ldsfld, globalObject.ConfigurableIntrinsicNames);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(arrayType, "IndexOf", arrayType, _types.Object));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge_S, writable);
        foreach (var name in BuiltInNames.GlobalConstantNames) EmitName(name, constant);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(writable);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(constant);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        void EmitName(string name, Label matched)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, matched);
        }
    }
}
