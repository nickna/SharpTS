using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitPromiseWithResolversStaticBody(EmittedRuntime runtime)
    {
        var promise = runtime.RequirePromise();
        var il = promise.WithResolversStatic.GetILGenerator();
        var capability = il.DeclareLocal(promise.CapabilityType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, promise.PreparePromiseCapabilityMethod);
        il.Emit(OpCodes.Castclass, promise.CapabilityType);
        il.Emit(OpCodes.Stloc, capability);
        var result = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, result);
        foreach (var (name, field) in new[]
        {
            ("promise", promise.CapabilityInstanceField),
            ("resolve", promise.CapabilityResolveField),
            ("reject", promise.CapabilityRejectField)
        })
        {
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloc, capability);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        }
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Newobj, runtime.ObjectStorage.Constructor);
        il.Emit(OpCodes.Ret);
    }
}
