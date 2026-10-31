using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private const string VmCoreTypeName =
        "SharpTS.Runtime.BuiltIns.Modules.Interpreter.VmModuleInterpreter, SharpTS";

    private void EmitVmCoreInvoke(ILGenerator il, string name, params Action<ILGenerator>[] arguments)
    {
        il.Emit(OpCodes.Ldstr, VmCoreTypeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        var type = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, type);
        var available = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, type);
        il.Emit(OpCodes.Brtrue, available);
        EmitThrowVmNotStandalone(il);
        il.MarkLabel(available);
        il.Emit(OpCodes.Ldloc, type);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4, arguments.Length);
        il.Emit(OpCodes.Newarr, _types.Object);
        for (var i = 0; i < arguments.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            arguments[i](il);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
    }

    private void EmitVmBoundaryBody(ILGenerator il, EmittedVmRuntime vm, Action emitInvocation, bool enterScope = true)
    {
        var scope = enterScope ? il.DeclareLocal(typeof(IDisposable)) : null;
        var result = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();
        if (enterScope)
        {
            il.Emit(OpCodes.Call, vm.EnterExecutionScope);
            il.Emit(OpCodes.Stloc, scope!);
        }
        il.BeginExceptionBlock();
        emitInvocation();
        il.Emit(OpCodes.Call, vm.NormalizeValue);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Call, vm.ConvertException);
        il.Emit(OpCodes.Throw);
        if (enterScope)
        {
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, scope!);
            il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);
        }
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private void EmitVmExceptionBridge(EmittedRuntime runtime)
    {
        var vm = runtime.RequireVm();
        var il = vm.ConvertException.GetILGenerator();
        var exception = il.DeclareLocal(_types.Exception);
        var inner = il.DeclareLocal(_types.Exception);
        var unwrap = il.DefineLabel();
        var inspect = il.DefineLabel();
        var ordinary = il.DefineLabel();
        var normalize = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, exception);
        il.MarkLabel(unwrap);
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Isinst, _types.TargetInvocationException);
        il.Emit(OpCodes.Brfalse, inspect);
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, inner);
        il.Emit(OpCodes.Ldloc, inner);
        il.Emit(OpCodes.Brfalse, inspect);
        il.Emit(OpCodes.Ldloc, inner);
        il.Emit(OpCodes.Stloc, exception);
        il.Emit(OpCodes.Br, unwrap);
        il.MarkLabel(inspect);
        EmitVmCoreInvoke(il, "IsVmExceptionValue", arg => arg.Emit(OpCodes.Ldloc, exception));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, ordinary);
        EmitVmCoreInvoke(il, "GetVmExceptionValue", arg => arg.Emit(OpCodes.Ldloc, exception));
        il.Emit(OpCodes.Br, normalize);
        il.MarkLabel(ordinary);
        // This fallback runs only inside an actual VM transport boundary. The
        // general exception bridge and arbitrary CLR callers remain unchanged.
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Call, runtime.Errors.WrapException);
        il.MarkLabel(normalize);
        il.Emit(OpCodes.Call, vm.NormalizeValue);
        il.Emit(OpCodes.Call, runtime.Errors.CreateException);
        il.Emit(OpCodes.Ret);
    }

    private void EmitVmErrorBridge(EmittedRuntime runtime)
    {
        var vm = runtime.RequireVm();
        foreach (var (method, name) in new[] { (vm.IsErrorValue, "IsVmErrorValue"), (vm.IsRelatedValue, "IsVmValue") })
        {
            var predicate = method.GetILGenerator();
            var available = predicate.DefineLabel();
            predicate.Emit(OpCodes.Ldstr, VmCoreTypeName);
            predicate.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
            predicate.Emit(OpCodes.Brtrue, available);
            predicate.Emit(OpCodes.Ldc_I4_0);
            predicate.Emit(OpCodes.Ret);
            predicate.MarkLabel(available);
            EmitVmCoreInvoke(predicate, name, arg => arg.Emit(OpCodes.Ldarg_0));
            predicate.Emit(OpCodes.Unbox_Any, _types.Boolean);
            predicate.Emit(OpCodes.Ret);
        }
        var il = vm.ReadErrorProperty.GetILGenerator();
        EmitVmBoundaryBody(il, vm, () => EmitVmCoreInvoke(il, "ReadVmErrorProperty",
            arg => arg.Emit(OpCodes.Ldarg_0), arg => arg.Emit(OpCodes.Ldarg_1)), enterScope: false);
    }
}
