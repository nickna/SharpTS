using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitVmPromiseHandling(TypeBuilder owner, EmittedRuntime runtime)
    {
        var vm = runtime.RequireVm();
        var table = _types.ConditionalWeakTableObjectObject;
        var sources = owner.DefineField("_vmPromiseHandlingSources", table,
            FieldAttributes.Private | FieldAttributes.Static);
        var tryGet = _types.GetMethod(table, "TryGetValue", _types.Object, _types.Object.MakeByRefType());
        foreach (var (method, operation, query) in new[]
        {
            (vm.IsPromiseRejectionHandled, "IsVmPromiseRejectionHandled", true),
            (vm.MarkPromiseRejectionHandled, "MarkVmPromiseRejectionHandled", false)
        })
        {
            var il = method.GetILGenerator();
            var source = il.DeclareLocal(_types.Object);
            var absent = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, absent);
            il.Emit(OpCodes.Ldsfld, sources);
            il.Emit(OpCodes.Brfalse, absent);
            il.Emit(OpCodes.Ldsfld, sources);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, source);
            il.Emit(OpCodes.Callvirt, tryGet);
            il.Emit(OpCodes.Brfalse, absent);
            EmitVmCoreInvoke(il, operation, body => body.Emit(OpCodes.Ldloc, source));
            if (query) il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            else il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(absent);
            if (query) il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        var register = owner.DefineMethod("_RegisterVmPromiseHandling",
            MethodAttributes.Private | MethodAttributes.Static, _types.Void,
            [_types.TaskOfObject, _types.Object]);
        var body = register.GetILGenerator();
        var ready = body.DefineLabel();
        body.Emit(OpCodes.Ldsfld, sources);
        body.Emit(OpCodes.Brtrue, ready);
        body.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(table));
        body.Emit(OpCodes.Stsfld, sources);
        body.MarkLabel(ready);
        body.Emit(OpCodes.Ldsfld, sources);
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Ldarg_1);
        body.Emit(OpCodes.Callvirt, _types.GetMethod(table, "Add", _types.Object, _types.Object));
        body.Emit(OpCodes.Ret);
        return register;
    }
}
