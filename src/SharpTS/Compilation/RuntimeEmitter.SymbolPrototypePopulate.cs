using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct SymbolPrototypePopulateInputs(
        MethodBuilder CreateException,
        EmittedDescriptorStorageRuntime DescriptorStorage,
        FieldBuilder ObjectPrototypeField,
        EmittedObjectStorageRuntime ObjectStorage,
        MethodBuilder TSFunctionGetOrCreate,
        ConstructorBuilder TSTypeErrorCtor
    );

    private readonly record struct SymbolPrototypeValueOfInputs(
        MethodBuilder CreateException,
        EmittedObjectStorageRuntime ObjectStorage,
        ConstructorBuilder TSTypeErrorCtor
    );

    private void DefineSymbolPrototypePopulateShell(TypeBuilder typeBuilder, EmittedSymbolRuntime symbols)
    {
        symbols.PopulatePrototype = typeBuilder.DefineMethod(
            "_SymbolPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitSymbolPrototypePopulate(
        TypeBuilder typeBuilder,
        EmittedSymbolRuntime symbols,
        SymbolPrototypePopulateInputs inputs
    )
    {
        var valueOf = EmitSymbolPrototypeValueOf(
            typeBuilder,
            symbols,
            new SymbolPrototypeValueOfInputs(inputs.CreateException, inputs.ObjectStorage, inputs.TSTypeErrorCtor)
        );
        var toString = EmitSymbolPrototypeToString(typeBuilder, symbols, valueOf);
        var description = EmitSymbolPrototypeDescription(typeBuilder, symbols, valueOf);
        symbols.PrototypeDescription = description;

        var descriptors = new PrototypeDescriptorInputs(
            inputs.DescriptorStorage.DescriptorConstructor, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!,
            inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!, inputs.DescriptorStorage.DefineProperty);

        var method = symbols.PopulatePrototype;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);
        EmitPrototypePopulateGuard(il, symbols.Prototype);
        var descLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        EmitInstallConstructorDescriptor(il, descriptors, symbols.Prototype, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, symbols.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });
        EmitWirePrototypeMethodDescriptor(il, descriptors, inputs.TSFunctionGetOrCreate, symbols.Prototype, descLocal,
            setItem, "toString", toString, 0);
        EmitWirePrototypeMethodDescriptor(il, descriptors, inputs.TSFunctionGetOrCreate, symbols.Prototype, descLocal,
            setItem, "valueOf", valueOf, 0);

        // description is a configurable, non-enumerable accessor property.
        description.DefineParameter(1, ParameterAttributes.None, "__this");
        var getterLocal = il.DeclareLocal(_types.Object);
        _types.EmitLoadMethodInfo(il, description);
        il.Emit(OpCodes.Ldstr, "get description");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, inputs.TSFunctionGetOrCreate);
        il.Emit(OpCodes.Stloc, getterLocal);
        il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorGetter.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldsfld, symbols.Prototype);
        il.Emit(OpCodes.Ldstr, "description");
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldsfld, symbols.Prototype);
        il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypeField);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitSymbolPrototypeValueOf(
        TypeBuilder typeBuilder,
        EmittedSymbolRuntime symbols,
        SymbolPrototypeValueOfInputs inputs
    )
    {
        var method = typeBuilder.DefineMethod(
            "SymbolPrototypeValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();
        var notPrimitive = il.DefineLabel();
        var throwTypeError = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, symbols.Type);
        il.Emit(OpCodes.Brfalse, notPrimitive);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPrimitive);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        var primitiveLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Callvirt, inputs.ObjectStorage.FieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldloca, primitiveLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Isinst, symbols.Type);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(throwTypeError);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TSTypeErrorCtor,
            "Symbol.prototype.valueOf requires that 'this' be a Symbol");
        return method;
    }

    private MethodBuilder EmitSymbolPrototypeToString(TypeBuilder typeBuilder, EmittedSymbolRuntime symbols, MethodBuilder valueOf)
    {
        var method = typeBuilder.DefineMethod(
            "SymbolPrototypeToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOf);
        il.Emit(OpCodes.Castclass, symbols.Type);
        il.Emit(OpCodes.Callvirt, symbols.ToStringMethod);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitSymbolPrototypeDescription(TypeBuilder typeBuilder, EmittedSymbolRuntime symbols, MethodBuilder valueOf)
    {
        var method = typeBuilder.DefineMethod(
            "SymbolPrototypeDescription",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOf);
        il.Emit(OpCodes.Castclass, symbols.Type);
        il.Emit(OpCodes.Callvirt, symbols.DescriptionGetter);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
