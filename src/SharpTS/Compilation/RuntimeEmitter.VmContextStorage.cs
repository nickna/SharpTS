using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct VmContextStorageEmission(MethodBuilder Admits, MethodBuilder Keys,
        MethodBuilder Descriptor, MethodBuilder Set, MethodBuilder Define, MethodBuilder Delete,
        MethodBuilder IsExtensible, MethodBuilder PreventExtensions);

    // This protocol belongs only to the actual plain-object implementation in
    // this output. The Core context marker stores static capabilities and data,
    // never an execution host, evaluator, or converted accessor.
    private VmContextStorageEmission EmitVmContextStorageCallbacks(TypeBuilder owner, EmittedRuntime runtime)
    {
        MethodBuilder Declare(string name, Type result, params Type[] parameters) =>
            owner.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static, result, parameters);
        var sourceAllocation = EmitVmSourceObjectTracking(owner, runtime);
        var admits = Declare("_VmAdmitsContextStorage", _types.Boolean, _types.Object);
        var il = admits.GetILGenerator();
        var present = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, present);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(present);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldtoken, runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle"));
        var exactObject = il.DefineLabel();
        il.Emit(OpCodes.Beq, exactObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, sourceAllocation);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(exactObject);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        void RequireTarget(ILGenerator body)
        {
            var admitted = body.DefineLabel();
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Call, admits);
            body.Emit(OpCodes.Brtrue, admitted);
            body.Emit(OpCodes.Ldstr, "VM context storage requires its actual output object.");
            body.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
            body.Emit(OpCodes.Throw);
            body.MarkLabel(admitted);
        }

        var keys = Declare("_VmContextStorageKeys", typeof(string[]), _types.Object);
        il = keys.GetILGenerator();
        RequireTarget(il);
        var names = il.DeclareLocal(_types.ListOfObject);
        var result = il.DeclareLocal(typeof(string[]));
        var index = il.DeclareLocal(_types.Int32);
        var length = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectKeys.Names);
        il.Emit(OpCodes.Stloc, names);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Newarr, _types.String);
        il.Emit(OpCodes.Stloc, result);
        var copy = il.DefineLabel();
        var check = il.DefineLabel();
        il.Emit(OpCodes.Br, check);
        il.MarkLabel(copy);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Blt, copy);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        var fields = new[] { ("value", 1, 1), ("writable", 2, 2), ("enumerable", 4, 3),
            ("configurable", 8, 4), ("get", 16, 5), ("set", 32, 6) };
        var descriptor = Declare("_VmContextStorageDescriptor", _types.ObjectArray, _types.Object, _types.String);
        il = descriptor.GetILGenerator();
        RequireTarget(il);
        var snapshot = il.DeclareLocal(_types.DictionaryStringObject);
        var wire = il.DeclareLocal(_types.ObjectArray);
        var value = il.DeclareLocal(_types.Object);
        var mask = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectDescriptors.GetOwnPropertyDescriptor);
        // Only this original endpoint's freshly produced descriptor dictionary
        // is inspected. These fixed dictionary reads cannot invoke its target.
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, snapshot);
        var found = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, snapshot);
        il.Emit(OpCodes.Brtrue, found);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(found);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, wire);
        foreach (int slot in new[] { 2, 3, 4 })
        {
            il.Emit(OpCodes.Ldloc, wire);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Stelem_Ref);
        }
        foreach (var (name, bit, slot) in fields)
        {
            var absent = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, snapshot);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject,
                "TryGetValue", _types.String, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, absent);
            il.Emit(OpCodes.Ldloc, mask);
            il.Emit(OpCodes.Ldc_I4, bit);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, mask);
            il.Emit(OpCodes.Ldloc, wire);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stelem_Ref);
            il.MarkLabel(absent);
        }
        il.Emit(OpCodes.Ldloc, wire);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, mask);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, wire);
        il.Emit(OpCodes.Ret);

        var set = Declare("_VmContextStorageSet", _types.Boolean, _types.Object, _types.String, _types.Object);
        il = set.GetILGenerator();
        RequireTarget(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Reflect.RequireAssignment().Set);
        il.Emit(OpCodes.Ret);

        var define = Declare("_VmContextStorageDefine", _types.Boolean, _types.Object, _types.String, _types.ObjectArray);
        il = define.GetILGenerator();
        RequireTarget(il);
        var inputMask = il.DeclareLocal(_types.Int32);
        var partial = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, inputMask);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, partial);
        il.Emit(OpCodes.Ldloc, partial);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.SetPrototype);
        foreach (var (name, bit, slot) in fields)
        {
            var absent = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, inputMask);
            il.Emit(OpCodes.Ldc_I4, bit);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, absent);
            il.Emit(OpCodes.Ldloc, partial);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject,
                "set_Item", _types.String, _types.Object));
            il.MarkLabel(absent);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, partial);
        il.Emit(OpCodes.Call, runtime.Reflect.RequireAssignment().DefineProperty);
        il.Emit(OpCodes.Ret);

        var delete = Declare("_VmContextStorageDelete", _types.Boolean, _types.Object, _types.String);
        il = delete.GetILGenerator();
        RequireTarget(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectDeletion.Property);
        il.Emit(OpCodes.Ret);
        var extensible = Declare("_VmContextStorageIsExtensible", _types.Boolean, _types.Object);
        il = extensible.GetILGenerator();
        RequireTarget(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectState.IsExtensible);
        il.Emit(OpCodes.Ret);
        var prevent = Declare("_VmContextStoragePreventExtensions", _types.Void, _types.Object);
        il = prevent.GetILGenerator();
        RequireTarget(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectState.PreventExtensions);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return new(admits, keys, descriptor, set, define, delete, extensible, prevent);
    }

    private void EmitVmContextStorageConfiguration(ILGenerator il, EmittedRuntime runtime,
        VmContextStorageEmission storage, LocalBuilder host)
    {
        Action<ILGenerator> Delegate(MethodInfo method, ConstructorInfo constructor) => body =>
            EmitVmStaticDelegate(body, method, constructor);
        EmitVmCoreInvoke(il, "ConfigureExecutionHostContextStorage",
            body => body.Emit(OpCodes.Ldloc, host),
            body =>
            {
                body.Emit(OpCodes.Ldtoken, runtime.ObjectStorage.Type);
                body.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle"));
            },
            Delegate(storage.Admits, typeof(Func<object, bool>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.Keys, typeof(Func<object, string[]>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.Descriptor, typeof(Func<object, string, object?[]?>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.Set, typeof(Func<object, string, object?, bool>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.Define, typeof(Func<object, string, object?[], bool>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.Delete, typeof(Func<object, string, bool>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.IsExtensible, typeof(Func<object, bool>).GetConstructor([_types.Object, _types.IntPtr])!),
            Delegate(storage.PreventExtensions, typeof(Action<object>).GetConstructor([_types.Object, _types.IntPtr])!));
        il.Emit(OpCodes.Pop);
    }

}
