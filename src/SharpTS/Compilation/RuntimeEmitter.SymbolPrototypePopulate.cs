using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineSymbolPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.SymbolPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_SymbolPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitSymbolPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var valueOf = EmitSymbolPrototypeValueOf(typeBuilder, runtime);
        var toString = EmitSymbolPrototypeToString(typeBuilder, runtime, valueOf);
        var description = EmitSymbolPrototypeDescription(typeBuilder, runtime, valueOf);
        runtime.SymbolPrototypeValueOf = valueOf;
        runtime.SymbolPrototypeToString = toString;
        runtime.SymbolPrototypeDescription = description;

        var method = runtime.SymbolPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);
        EmitPrototypePopulateGuard(il, runtime.SymbolPrototypeField);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        EmitInstallConstructor(il, runtime, runtime.SymbolPrototypeField, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, runtime.TSSymbolType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });
        EmitWirePrototypeMethod(il, runtime, runtime.SymbolPrototypeField, descLocal,
            setItem, "toString", toString, 0);
        EmitWirePrototypeMethod(il, runtime, runtime.SymbolPrototypeField, descLocal,
            setItem, "valueOf", valueOf, 0);

        // description is a configurable, non-enumerable accessor property.
        description.DefineParameter(1, ParameterAttributes.None, "__this");
        var getterLocal = il.DeclareLocal(_types.Object);
        _types.EmitLoadMethodInfo(il, description);
        il.Emit(OpCodes.Ldstr, "get description");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        il.Emit(OpCodes.Stloc, getterLocal);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolPrototypeField);
        il.Emit(OpCodes.Ldstr, "description");
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldsfld, runtime.SymbolPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitSymbolPrototypeValueOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
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
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, throwTypeError);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(throwTypeError);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Symbol.prototype.valueOf requires that 'this' be a Symbol");
        return method;
    }

    private MethodBuilder EmitSymbolPrototypeToString(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder valueOf)
    {
        var method = typeBuilder.DefineMethod(
            "SymbolPrototypeToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOf);
        il.Emit(OpCodes.Castclass, runtime.TSSymbolType);
        il.Emit(OpCodes.Callvirt, runtime.SymbolToStringMethod);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitSymbolPrototypeDescription(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder valueOf)
    {
        var method = typeBuilder.DefineMethod(
            "SymbolPrototypeDescription",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueOf);
        il.Emit(OpCodes.Castclass, runtime.TSSymbolType);
        il.Emit(OpCodes.Callvirt, runtime.SymbolDescriptionGetter);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
