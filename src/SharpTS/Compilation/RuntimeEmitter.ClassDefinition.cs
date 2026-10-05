using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineClassDefinitionTypes(ModuleBuilder module, EmittedClassDefinitionRuntime definitions)
    {
        var contract = module.DefineType("$IClassDefinitionInstance",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var getter = contract.DefineMethod("$GetClassDefinition",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
            _types.Object, Type.EmptyTypes);
        var instanceInterface = contract.CreateType()!;
        var type = module.DefineType("$ClassDefinition", TypeAttributes.Public | TypeAttributes.Sealed);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ret);
        var factoryType = typeof(Func<object[], object, object>);
        var template = type.DefineField("Template", _types.Type, FieldAttributes.Assembly);
        var factory = type.DefineField("Factory", factoryType, FieldAttributes.Assembly);
        var prototype = type.DefineField("Prototype", _types.Object, FieldAttributes.Assembly);
        var keys = type.DefineField("Keys", _types.ObjectArray, FieldAttributes.Assembly);
        var parent = type.DefineField("Parent", _types.Object, FieldAttributes.Assembly);
        var captures = type.DefineField("Captures", _types.ObjectArray, FieldAttributes.Assembly);
        var create = type.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static,
            type, [_types.Type, factoryType, _types.String, _types.Double, _types.Object, _types.ObjectArray, _types.ObjectArray]);
        var construct = type.DefineMethod("Construct", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [type, _types.ObjectArray]);
        var read = type.DefineMethod("ReadProperty", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [type, _types.String]);
        var invoke = type.DefineMethod("Invoke", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        var captureType = module.DefineType("$ClassCapture", TypeAttributes.Public | TypeAttributes.Sealed);
        var captureOwner = captureType.DefineField("Owner", _types.Object, FieldAttributes.Assembly);
        var captureField = captureType.DefineField("Field", _types.FieldInfo, FieldAttributes.Assembly);
        var captureConstructor = captureType.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [_types.Object, _types.FieldInfo]);
        il = captureConstructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, captureOwner);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, captureField);
        il.Emit(OpCodes.Ret);
        var readCapture = type.DefineMethod("ReadCapture", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        var writeCapture = type.DefineMethod("WriteCapture", MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, [type, _types.Int32, _types.Object]);
        var findEnvironment = type.DefineMethod("FindEnvironment", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [type, _types.Type]);
        EmitClassCaptureBodies(captureType, captureOwner, captureField, captures, readCapture, writeCapture, findEnvironment);
        captureType.CreateType();
        definitions.Declare(new(type, instanceInterface, instanceInterface.GetMethod(getter.Name)!,
            constructor, template, factory, prototype, keys, parent, captures, create, construct, read, invoke,
            captureType, captureConstructor, captureOwner, captureField, readCapture, writeCapture, findEnvironment));
    }

    private void EmitClassDefinitionBodies(EmittedRuntime runtime)
    {
        var definitions = runtime.ClassDefinitions;
        var storage = runtime.DescriptorStorage;
        var il = definitions.Create.GetILGenerator();
        var definition = il.DeclareLocal(definitions.Type);
        var prototype = il.DeclareLocal(_types.Object);
        var templatePrototype = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Newobj, definitions.Constructor);
        il.Emit(OpCodes.Stloc, definition);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, definitions.Template);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, definitions.Factory);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stfld, definitions.Keys);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, definitions.Parent);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_S, (byte)6);
        il.Emit(OpCodes.Stfld, definitions.Captures);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.ObjectStorage.Constructor);
        il.Emit(OpCodes.Stloc, prototype);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Stfld, definitions.Prototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ClassPrototypes.Get);
        il.Emit(OpCodes.Stloc, templatePrototype);

        // Copy descriptors into a fresh prototype; descriptor flags must not be
        // shared with another evaluation of the same syntax node.
        var keys = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, templatePrototype);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Call, storage.GetAllExtraKeys);
        il.Emit(OpCodes.Stloc, keys);
        var index = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.String);
        var original = il.DeclareLocal(storage.DescriptorType);
        var copy = il.DeclareLocal(storage.DescriptorType);
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, templatePrototype);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, storage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, original);
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, copy);
        foreach (var property in new[] { storage.DescriptorValue, storage.DescriptorGetter,
                     storage.DescriptorSetter, storage.DescriptorWritable, storage.DescriptorEnumerable,
                     storage.DescriptorConfigurable })
        {
            il.Emit(OpCodes.Ldloc, copy);
            il.Emit(OpCodes.Ldloc, original);
            il.Emit(OpCodes.Callvirt, property.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
        }
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloc, copy);
        il.Emit(OpCodes.Call, storage.DefineProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);

        var basePrototype = il.DeclareLocal(_types.Object);
        var noParent = il.DefineLabel();
        var haveParent = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Brfalse, noParent);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, basePrototype);
        il.Emit(OpCodes.Ldloc, basePrototype);
        il.Emit(OpCodes.Brfalse, noParent);
        il.Emit(OpCodes.Ldloc, basePrototype);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, noParent);
        il.Emit(OpCodes.Br, haveParent);
        il.MarkLabel(noParent);
        il.Emit(OpCodes.Ldloc, templatePrototype);
        il.Emit(OpCodes.Call, runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, basePrototype);
        il.MarkLabel(haveParent);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Ldloc, basePrototype);
        il.Emit(OpCodes.Call, storage.SetPrototype);
        EmitDataDescriptor(prototype, "constructor", () => il.Emit(OpCodes.Ldloc, definition), true);
        EmitDataDescriptor(definition, "prototype", () => il.Emit(OpCodes.Ldloc, prototype), false, false);
        EmitDataDescriptor(definition, "name", () => il.Emit(OpCodes.Ldarg_2), false);
        EmitDataDescriptor(definition, "length", () =>
        {
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Box, _types.Double);
        }, false);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        var parentPresent = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, parentPresent);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypes.Prototype);
        il.MarkLabel(parentPresent);
        il.Emit(OpCodes.Call, storage.SetPrototype);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ret);

        void EmitDataDescriptor(LocalBuilder owner, string name, Action value, bool writable, bool configurable = true)
        {
            var descriptor = il.DeclareLocal(storage.DescriptorType);
            il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor);
            value();
            il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
            foreach (var (property, flag) in new[] { (storage.DescriptorWritable, writable),
                         (storage.DescriptorEnumerable, false), (storage.DescriptorConfigurable, configurable) })
            {
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(flag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
            }
            il.Emit(OpCodes.Ldloc, owner);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, storage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        il = definitions.Construct.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, definitions.Factory);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(Func<object[], object, object>), "Invoke", _types.ObjectArray, _types.Object));
        il.Emit(OpCodes.Ret);

        il = definitions.ReadProperty.GetILGenerator();
        var ownDescriptor = il.DeclareLocal(storage.DescriptorType);
        var inheritedProperty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, storage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, ownDescriptor);
        il.Emit(OpCodes.Ldloc, ownDescriptor);
        il.Emit(OpCodes.Brfalse, inheritedProperty);
        var dataProperty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ownDescriptor);
        il.Emit(OpCodes.Callvirt, storage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, dataProperty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ownDescriptor);
        il.Emit(OpCodes.Callvirt, storage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dataProperty);
        il.Emit(OpCodes.Ldloc, ownDescriptor);
        il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(inheritedProperty);
        var ordinary = il.DefineLabel();
        foreach (var name in new[] { "bind", "call", "apply", "name", "length", "prototype" })
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, ordinary);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, definitions.Template);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinary);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FunctionIntrospection.GetProperty);
        il.Emit(OpCodes.Ret);

        il = definitions.Invoke.GetILGenerator();
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Class constructor cannot be invoked without 'new'");
        definitions.Type.CreateType();
        definitions.CompleteEmission();
    }

}
