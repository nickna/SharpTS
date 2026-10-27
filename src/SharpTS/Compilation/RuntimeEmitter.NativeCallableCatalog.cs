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
    }
}
