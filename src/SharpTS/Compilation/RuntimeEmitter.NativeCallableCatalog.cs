using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DeclareNativeCallableCatalog(ModuleBuilder module, EmittedRuntime runtime)
    {
        var contract = module.DefineType("$INativeCallable",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var invoke = contract.DefineMethod("Invoke",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot, _types.Object, [_types.ObjectArray]);
        var type = contract.CreateType()!;
        runtime.BeginNativeCallablesEmission(new(type, invoke, _types.Object, _types.ObjectArray));
    }

    /// <summary>
    /// Recognizes a generated callable before the managed reflection bridge. The
    /// legacy branch remains reachable until CLR-carrier admission is complete.
    /// </summary>
    private void EmitNativeCallableFastPath(ILGenerator il, EmittedNativeCallableRuntime catalog,
        Action loadCallable, Action loadArguments)
    {
        var miss = il.DefineLabel();
        var callable = il.DeclareLocal(catalog.Contract);
        loadCallable();
        il.Emit(OpCodes.Isinst, catalog.Contract);
        il.Emit(OpCodes.Stloc, callable);
        il.Emit(OpCodes.Ldloc, callable);
        il.Emit(OpCodes.Brfalse, miss);
        il.Emit(OpCodes.Ldloc, callable);
        loadArguments();
        il.Emit(OpCodes.Callvirt, catalog.Invoke);
        EmitNormalizeClrNumericReturn(il);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(miss);

        // This exact delegate ABI is already emitted by CreateBoundMethod and
        // accepted by InvokeValue. Other Delegate signatures retain the CLR
        // bridge: DynamicInvoke would require signature provenance we do not
        // yet have, and treating them as this delegate would change semantics.
        var notDelegate = il.DefineLabel();
        var callback = il.DeclareLocal(_types.FuncObjectArrayToObject);
        loadCallable();
        il.Emit(OpCodes.Isinst, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Stloc, callback);
        il.Emit(OpCodes.Ldloc, callback);
        il.Emit(OpCodes.Brfalse, notDelegate);
        il.Emit(OpCodes.Ldloc, callback);
        loadArguments();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FuncObjectArrayToObject, "Invoke", _types.ObjectArray));
        EmitNormalizeClrNumericReturn(il);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDelegate);
    }
}
