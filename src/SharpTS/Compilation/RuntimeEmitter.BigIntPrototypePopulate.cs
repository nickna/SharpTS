using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineBigIntPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.BigIntPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_BigIntPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitBigIntPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var valueOfHelper = EmitBigIntValueOfHelper(typeBuilder, runtime);
        var toStringHelper = EmitBigIntPrototypeToStringHelper(typeBuilder, runtime, valueOfHelper);

        var method = runtime.BigIntPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.BigIntPrototypeField);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        EmitInstallConstructor(il, runtime, runtime.BigIntPrototypeField, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.BigInteger);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        EmitWirePrototypeMethod(il, runtime, runtime.BigIntPrototypeField, descLocal,
            setItem, "toString", toStringHelper, 1);
        EmitWirePrototypeMethod(il, runtime, runtime.BigIntPrototypeField, descLocal,
            setItem, "valueOf", valueOfHelper, 0);

        // %BigInt.prototype% owns @@toStringTag = "BigInt" with the standard
        // { writable:false, enumerable:false, configurable:true } attributes.
        // Store the descriptor in the shared symbol dictionary so user
        // defineProperty/delete operations participate in ordinary lookup.
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldstr, "BigInt");
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldsfld, runtime.BigIntPrototypeField);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToStringTag);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        il.Emit(OpCodes.Ldsfld, runtime.BigIntPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitBigIntValueOfHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();
        var notPrimitive = il.DefineLabel();
        var throwTypeError = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notPrimitive);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPrimitive);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        var primitiveLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldloca, primitiveLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwTypeError);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "BigInt.prototype.valueOf requires that 'this' be a BigInt");
        return method;
    }

    private MethodBuilder EmitBigIntPrototypeToStringHelper(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder valueOfHelper)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntPrototypeToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var radixLocal = il.DeclareLocal(_types.Double);
        var useDefault = il.DefineLabel();
        var radixReady = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, useDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, useDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, radixReady);
        il.MarkLabel(useDefault);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.MarkLabel(radixReady);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOfHelper);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Call, runtime.BigIntToStringRadix);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
