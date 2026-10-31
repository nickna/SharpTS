using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitVmNativePromiseCallbackRole(TypeBuilder owner, EmittedRuntime runtime, FieldBuilder sources)
    {
        var method = owner.DefineMethod("_VmGetNativePromiseCallbackRole", MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32, [_types.Object]);
        var il = method.GetILGenerator();
        var source = il.DeclareLocal(_types.Object);
        var unknown = il.DefineLabel();
        var resolve = il.DefineLabel();
        var reject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, unknown);
        il.Emit(OpCodes.Ldsfld, sources);
        il.Emit(OpCodes.Brfalse, unknown);
        il.Emit(OpCodes.Ldsfld, sources);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, source);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTableObjectObject,
            "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, unknown);
        // The private inverse table contains only wrappers produced from the
        // completed literal endpoints below; public names never select a role.
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, runtime.RequirePromise().ResolveCallbackType);
        il.Emit(OpCodes.Brtrue, resolve);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, runtime.RequirePromise().RejectCallbackType);
        il.Emit(OpCodes.Brtrue, reject);
        il.MarkLabel(unknown);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(resolve);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(reject);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitVmNativePromiseCallbackWrapping(ILGenerator il, EmittedRuntime runtime,
        FieldBuilder cache, FieldBuilder sources)
    {
        var promise = runtime.RequirePromise();
        var table = _types.ConditionalWeakTableObjectObject;
        var tryGet = _types.GetMethod(table, "TryGetValue", _types.Object, _types.Object.MakeByRefType());
        var add = _types.GetMethod(table, "Add", _types.Object, _types.Object);
        foreach (var (owner, invoke) in new[]
        {
            (promise.ResolveCallbackType, promise.ResolveCallbackInvoke),
            (promise.RejectCallbackType, promise.RejectCallbackInvoke)
        })
        {
            if (!ReferenceEquals(invoke.DeclaringType, owner) || !owner.IsCreated()
                || !owner.IsSealed || owner.ContainsGenericParameters || !invoke.IsPublic || invoke.IsStatic
                || invoke.IsGenericMethod || (invoke.CallingConvention & CallingConventions.VarArgs) != 0
                || invoke.ReturnType != _types.Object
                || !invoke.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.ObjectArray])
                || invoke.GetILGenerator().ILOffset == 0)
                throw new InvalidOperationException("VM promise callback transport requires its completed actual endpoint.");
            var next = il.DefineLabel();
            var ready = il.DefineLabel();
            var create = il.DefineLabel();
            var wrapper = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, owner);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldsfld, cache);
            il.Emit(OpCodes.Brtrue, ready);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(table));
            il.Emit(OpCodes.Stsfld, cache);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(table));
            il.Emit(OpCodes.Stsfld, sources);
            il.MarkLabel(ready);
            il.Emit(OpCodes.Ldsfld, cache);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, wrapper);
            il.Emit(OpCodes.Callvirt, tryGet);
            il.Emit(OpCodes.Brfalse, create);
            il.Emit(OpCodes.Ldloc, wrapper);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(create);
            il.Emit(OpCodes.Ldarg_0);
            EmitReflectedMethodLiteral(il, invoke);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.CachedConstructor);
            il.Emit(OpCodes.Stloc, wrapper);
            il.Emit(OpCodes.Ldsfld, cache);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, wrapper);
            il.Emit(OpCodes.Callvirt, add);
            il.Emit(OpCodes.Ldsfld, sources);
            il.Emit(OpCodes.Ldloc, wrapper);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, add);
            il.Emit(OpCodes.Ldloc, wrapper);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
    }
}
