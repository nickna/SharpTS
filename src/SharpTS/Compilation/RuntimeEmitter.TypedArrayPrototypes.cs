using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct TypedArrayPrototypesInputs(
        EmittedTypedArrayRuntime TypedArrays, EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedDescriptorStorageRuntime DescriptorStorage);

    private void EmitTypedArrayPrototypes(TypeBuilder owner, TypedArrayPrototypesInputs inputs, ILGenerator initializer)
    {
        if (inputs.TypedArrays.Implementation is not { } arrays) return;
        var shared = owner.DefineField("_typedArrayPrototype", _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static);
        initializer.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        initializer.Emit(OpCodes.Stsfld, shared);
        initializer.Emit(OpCodes.Ldsfld, shared);
        initializer.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypes.Prototype);
        initializer.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);

        var getter = owner.DefineMethod("TypedArrayIntrinsicPrototype", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        arrays.IntrinsicPrototype = getter;
        var il = getter.GetILGenerator();
        var descriptor = initializer.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
        foreach (var kind in new TypeBuilder[]
        {
            arrays.Int8ArrayType, arrays.Uint8ArrayType, arrays.Uint8ClampedArrayType,
            arrays.Int16ArrayType, arrays.Uint16ArrayType, arrays.Int32ArrayType, arrays.Uint32ArrayType,
            arrays.Float32ArrayType, arrays.Float64ArrayType, arrays.BigInt64ArrayType, arrays.BigUint64ArrayType
        })
        {
            var prototype = owner.DefineField("_prototype" + kind.Name, _types.DictionaryStringObject,
                FieldAttributes.Private | FieldAttributes.Static);
            initializer.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            initializer.Emit(OpCodes.Stsfld, prototype);
            initializer.Emit(OpCodes.Ldsfld, prototype);
            initializer.Emit(OpCodes.Ldsfld, shared);
            initializer.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
            initializer.Emit(OpCodes.Ldsfld, prototype);
            initializer.Emit(OpCodes.Ldstr, "constructor");
            initializer.Emit(OpCodes.Ldtoken, kind);
            initializer.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            initializer.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
            initializer.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
            initializer.Emit(OpCodes.Stloc, descriptor);
            initializer.Emit(OpCodes.Ldloc, descriptor);
            initializer.Emit(OpCodes.Ldtoken, kind);
            initializer.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            initializer.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorValue.SetMethod!);
            foreach (var (property, value) in new[]
            {
                (inputs.DescriptorStorage.DescriptorWritable, true),
                (inputs.DescriptorStorage.DescriptorEnumerable, false),
                (inputs.DescriptorStorage.DescriptorConfigurable, true)
            })
            {
                initializer.Emit(OpCodes.Ldloc, descriptor);
                initializer.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                initializer.Emit(OpCodes.Callvirt, property.SetMethod!);
            }
            initializer.Emit(OpCodes.Ldsfld, prototype);
            initializer.Emit(OpCodes.Ldstr, "constructor");
            initializer.Emit(OpCodes.Ldloc, descriptor);
            initializer.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
            initializer.Emit(OpCodes.Pop);

            var match = il.DefineLabel();
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, kind);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Beq, match);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, kind);
            il.Emit(OpCodes.Brfalse, next);
            il.MarkLabel(match);
            il.Emit(OpCodes.Ldsfld, prototype);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
