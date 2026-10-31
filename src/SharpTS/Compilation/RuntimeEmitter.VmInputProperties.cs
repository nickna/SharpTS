using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct VmInputPropertyEmission(MethodBuilder Admits, MethodBuilder Read, MethodBuilder Role);

    private VmInputPropertyEmission EmitVmInputProperties(TypeBuilder owner, EmittedRuntime runtime)
    {
        var role = owner.DefineMethod("_VmGetInputPropertyRole", MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32, [_types.Object]);
        var il = role.GetILGenerator();
        var unknown = il.DefineLabel();
        var array = il.DefineLabel();
        var error = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, unknown);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldtoken, runtime.ArrayStorage.Type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Beq, array);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Errors.Type);
        il.Emit(OpCodes.Brtrue, error);
        il.MarkLabel(unknown);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(array);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(error);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);

        var admits = owner.DefineMethod("_VmAdmitsInputPropertyValue", MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        il = admits.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, role);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ret);

        var read = owner.DefineMethod("_VmReadInputProperty", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String]);
        il = read.GetILGenerator();
        var admitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, admits);
        il.Emit(OpCodes.Brtrue, admitted);
        il.Emit(OpCodes.Ldstr, "VM input property access requires its actual output value.");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(admitted);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Ret);
        return new(admits, read, role);
    }

    private void EmitVmInputPropertyConfiguration(ILGenerator il, EmittedRuntime runtime,
        VmInputPropertyEmission input, LocalBuilder host)
    {
        Action<ILGenerator> Delegate(MethodInfo method, Type type) => body =>
            EmitVmStaticDelegate(body, method, type.GetConstructor([_types.Object, _types.IntPtr])!);
        EmitVmCoreInvoke(il, "ConfigureExecutionHostInputPropertyAccess",
            body => body.Emit(OpCodes.Ldloc, host),
            body =>
            {
                body.Emit(OpCodes.Ldtoken, runtime.ObjectStorage.Type);
                body.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            },
            Delegate(input.Admits, typeof(Func<object, bool>)),
            Delegate(input.Read, typeof(Func<object, string, object?>)),
            Delegate(input.Role, typeof(Func<object, int>)));
        il.Emit(OpCodes.Pop);
    }
}
