using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct BigIntPrototypeInputs(
        PrototypeDescriptorInputs Descriptors,
        Type DescriptorType,
        MethodInfo WritableSetter,
        MethodInfo ConfigurableSetter,
        MethodInfo FunctionGetOrCreate,
        MethodInfo GetSymbolDictionary,
        FieldInfo SymbolToStringTag,
        FieldInfo ObjectPrototype,
        MethodInfo SetPrototype,
        Type ObjectType,
        MethodInfo ObjectFieldsGetter,
        MethodInfo ToNumber,
        Type UndefinedType,
        MethodInfo CreateException,
        ConstructorInfo TypeErrorCtor);

    private void DefineBigIntPrototypePopulateShell(TypeBuilder typeBuilder, EmittedBigIntRuntime bigInt)
    {
        bigInt.PrototypePopulateMethod = typeBuilder.DefineMethod(
            "_BigIntPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitBigIntPrototypePopulate(TypeBuilder typeBuilder, EmittedBigIntRuntime bigInt,
        MethodInfo toStringRadix, BigIntPrototypeInputs peers)
    {
        var valueOfHelper = EmitBigIntValueOfHelper(typeBuilder, peers.ObjectType, peers.ObjectFieldsGetter, peers.CreateException, peers.TypeErrorCtor);
        var toStringHelper = EmitBigIntPrototypeToStringHelper(typeBuilder, toStringRadix, peers.ToNumber, peers.UndefinedType, valueOfHelper);

        var method = bigInt.PrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, bigInt.PrototypeField);
        var descLocal = il.DeclareLocal(peers.DescriptorType);

        EmitInstallConstructorDescriptor(il, peers.Descriptors, bigInt.PrototypeField, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.BigInteger);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        EmitWirePrototypeMethodDescriptor(il, peers.Descriptors, peers.FunctionGetOrCreate, bigInt.PrototypeField, descLocal,
            setItem, "toString", toStringHelper, 0);
        EmitWirePrototypeMethodDescriptor(il, peers.Descriptors, peers.FunctionGetOrCreate, bigInt.PrototypeField, descLocal,
            setItem, "valueOf", valueOfHelper, 0);

        // %BigInt.prototype% owns @@toStringTag = "BigInt" with the standard
        // { writable:false, enumerable:false, configurable:true } attributes.
        // Store the descriptor in the shared symbol dictionary so user
        // defineProperty/delete operations participate in ordinary lookup.
        il.Emit(OpCodes.Newobj, peers.Descriptors.Ctor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldstr, "BigInt");
        il.Emit(OpCodes.Callvirt, peers.Descriptors.ValueSetter);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, peers.WritableSetter);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, peers.Descriptors.EnumerableSetter);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, peers.ConfigurableSetter);
        il.Emit(OpCodes.Ldsfld, bigInt.PrototypeField);
        il.Emit(OpCodes.Call, peers.GetSymbolDictionary);
        il.Emit(OpCodes.Ldsfld, peers.SymbolToStringTag);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        il.Emit(OpCodes.Ldsfld, bigInt.PrototypeField);
        il.Emit(OpCodes.Ldsfld, peers.ObjectPrototype);
        il.Emit(OpCodes.Call, peers.SetPrototype);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitBigIntValueOfHelper(TypeBuilder typeBuilder, Type objectType,
        MethodInfo objectFieldsGetter, MethodInfo createException, ConstructorInfo typeErrorCtor)
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
        il.Emit(OpCodes.Isinst, objectType);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        var primitiveLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, objectType);
        il.Emit(OpCodes.Callvirt, objectFieldsGetter);
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
        GuestErrorEmitter.ThrowError(il, createException, typeErrorCtor,
            "BigInt.prototype.valueOf requires that 'this' be a BigInt");
        return method;
    }

    private MethodBuilder EmitBigIntPrototypeToStringHelper(
        TypeBuilder typeBuilder, MethodInfo toStringRadix,
        MethodInfo toNumber, Type undefinedType, MethodBuilder valueOfHelper)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntPrototypeToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.ObjectArray]);
        method.DefineParameter(2, ParameterAttributes.None, "args");
        var il = method.GetILGenerator();
        var radixLocal = il.DeclareLocal(_types.Double);
        var useDefault = il.DefineLabel();
        var radixReady = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, useDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, undefinedType);
        il.Emit(OpCodes.Brtrue, useDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, toNumber);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, radixReady);
        il.MarkLabel(useDefault);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.MarkLabel(radixReady);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOfHelper);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Call, toStringRadix);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
