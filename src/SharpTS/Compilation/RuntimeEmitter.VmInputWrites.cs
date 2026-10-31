using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitVmInputWrite(TypeBuilder owner, EmittedRuntime runtime, MethodInfo admits)
    {
        var method = owner.DefineMethod("_VmWriteInputProperty", MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean, [_types.Object, _types.String, _types.Object]);
        var il = method.GetILGenerator();
        var admitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, admits);
        il.Emit(OpCodes.Brtrue, admitted);
        il.Emit(OpCodes.Ldstr, "VM input assignment requires its actual output value.");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(admitted);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Reflect.RequireAssignment().Set);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitVmInputWriteConfiguration(ILGenerator il, LocalBuilder host, MethodInfo set)
    {
        EmitVmCoreInvoke(il, "ConfigureExecutionHostInputPropertyWrites",
            body => body.Emit(OpCodes.Ldloc, host),
            body => EmitVmStaticDelegate(body, set,
                typeof(Func<object, string, object?, bool>).GetConstructor([_types.Object, _types.IntPtr])!));
        il.Emit(OpCodes.Pop);
    }
}
