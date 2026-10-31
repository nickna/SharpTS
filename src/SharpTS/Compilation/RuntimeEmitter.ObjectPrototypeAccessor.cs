using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitLegacyPrototypeAccessor(ILGenerator populate, EmittedObjectPrototypeRuntime prototypes,
        ObjectPrototypePopulateInputs inputs, LocalBuilder descriptor)
    {
        var owner = (TypeBuilder)prototypes.Populate.DeclaringType!;
        var getter = owner.DefineMethod("ObjectProtoGetPrototype", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        getter.DefineParameter(1, ParameterAttributes.None, "__this");
        var get = getter.GetILGenerator();
        get.Emit(OpCodes.Ldarg_0);
        get.Emit(OpCodes.Call, prototypes.GetPrototypeOf);
        get.Emit(OpCodes.Ret);

        var setter = owner.DefineMethod("ObjectProtoSetPrototype", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        setter.DefineParameter(1, ParameterAttributes.None, "__this");
        setter.DefineParameter(2, ParameterAttributes.None, "prototype");
        var set = setter.GetILGenerator();
        var invalidReceiver = set.DefineLabel();
        var receiverPresent = set.DefineLabel();
        var objectPrototype = set.DefineLabel();
        var done = set.DefineLabel();
        set.Emit(OpCodes.Ldarg_0);
        set.Emit(OpCodes.Brfalse, invalidReceiver);
        set.Emit(OpCodes.Ldarg_0);
        set.Emit(OpCodes.Isinst, inputs.UndefinedType);
        set.Emit(OpCodes.Brfalse, receiverPresent);
        set.MarkLabel(invalidReceiver);
        GuestErrorEmitter.ThrowError(set, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor,
            "Cannot set prototype of null or undefined");
        set.MarkLabel(receiverPresent);
        var kind = set.DeclareLocal(_types.String);
        set.Emit(OpCodes.Ldarg_1);
        set.Emit(OpCodes.Call, inputs.TypeOf);
        set.Emit(OpCodes.Stloc, kind);
        foreach (var objectKind in new[] { "object", "function" })
        {
            set.Emit(OpCodes.Ldloc, kind);
            set.Emit(OpCodes.Ldstr, objectKind);
            set.Emit(OpCodes.Call, _types.StringOpEquality);
            set.Emit(OpCodes.Brtrue, objectPrototype);
        }
        set.Emit(OpCodes.Br, done);
        set.MarkLabel(objectPrototype);
        set.Emit(OpCodes.Ldarg_0);
        set.Emit(OpCodes.Ldarg_1);
        set.Emit(OpCodes.Call, prototypes.SetPrototypeOf);
        set.Emit(OpCodes.Pop);
        set.MarkLabel(done);
        set.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        set.Emit(OpCodes.Ret);

        populate.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
        populate.Emit(OpCodes.Stloc, descriptor);
        void Accessor(MethodBuilder method, string name, int arity, MethodInfo store)
        {
            populate.Emit(OpCodes.Ldloc, descriptor);
            var parameters = EmitReflectedMethodLiteral(populate, method);
            populate.Emit(OpCodes.Ldstr, name);
            populate.Emit(OpCodes.Ldc_I4, arity);
            EmitReflectedFunctionFactory(populate, inputs.TSFunctionGetOrCreate, parameters);
            populate.Emit(OpCodes.Callvirt, store);
        }
        Accessor(getter, "get __proto__", 0, inputs.DescriptorStorage.DescriptorGetter.GetSetMethod()!);
        Accessor(setter, "set __proto__", 1, inputs.DescriptorStorage.DescriptorSetter.GetSetMethod()!);
        populate.Emit(OpCodes.Ldloc, descriptor);
        populate.Emit(OpCodes.Ldc_I4_0);
        populate.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        populate.Emit(OpCodes.Ldsfld, prototypes.Prototype);
        populate.Emit(OpCodes.Ldstr, "__proto__");
        populate.Emit(OpCodes.Ldloc, descriptor);
        populate.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
        populate.Emit(OpCodes.Pop);
    }
}
