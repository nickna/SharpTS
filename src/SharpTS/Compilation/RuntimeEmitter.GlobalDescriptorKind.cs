using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private IEnumerable<string> ConfigurableGlobalIntrinsicNames => _features.UsesConsoleObject
        ? BuiltInNames.ConfigurableGlobalIntrinsicNames.Append(BuiltInNames.Console)
        : BuiltInNames.ConfigurableGlobalIntrinsicNames;

    private void EmitGlobalThisIntrinsicDescriptorKind(EmittedGlobalObjectRuntime globalObject)
    {
        var il = globalObject.IntrinsicDescriptorKind.GetILGenerator();
        var writable = il.DefineLabel();
        var constant = il.DefineLabel();
        // This is the existing JavaScript descriptor-name table. It carries
        // property attributes only, with no CLR type or callable admission.
        foreach (var name in ConfigurableGlobalIntrinsicNames)
            EmitName(name, writable);
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
