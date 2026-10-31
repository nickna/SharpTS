using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitVmInputTag(TypeBuilder owner, EmittedRuntime runtime, MethodInfo admits)
    {
        var method = owner.DefineMethod("_VmReadInputToStringTag", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var admitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, admits);
        il.Emit(OpCodes.Brtrue, admitted);
        il.Emit(OpCodes.Ldstr, "VM input tag access requires its actual output value.");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(admitted);
        il.Emit(OpCodes.Ldarg_0);
        // The actual well-known symbol token selects this operation; a guest
        // property name never supplies tag authority or changes the receiver.
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.ToStringTag);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Reflect.Get);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitVmInputTagConfiguration(ILGenerator il, LocalBuilder host, MethodInfo readTag)
    {
        EmitVmCoreInvoke(il, "ConfigureExecutionHostInputToStringTag",
            body => body.Emit(OpCodes.Ldloc, host),
            body => EmitVmStaticDelegate(body, readTag,
                typeof(Func<object, object?, object?>).GetConstructor([_types.Object, _types.IntPtr])!));
        il.Emit(OpCodes.Pop);
    }
}
