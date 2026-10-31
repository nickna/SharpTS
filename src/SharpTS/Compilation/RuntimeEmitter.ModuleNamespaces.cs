using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private EmittedModuleNamespaceRuntime EmitModuleNamespaceStorage(TypeBuilder owner, EmittedObjectStorageRuntime storage, FieldBuilder fieldsField)
    {
        var readerType = _types.MakeGenericType(_types.FuncOpen3, _types.String, _types.Object, _types.Object);
        var readerConstructor = _types.GetConstructor(readerType, _types.Object, _types.IntPtr);
        var readerField = owner.DefineField("_moduleNamespaceReader", readerType, FieldAttributes.Private);
        var create = owner.DefineMethod("$CreateModuleNamespace", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.DictionaryStringObject, readerType]);
        var il = create.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        NativePrototypeFieldsLookup.EmitCarrierCreate(_nativePrototypeFields, il, storage.Constructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, readerField);
        il.Emit(OpCodes.Ret);

        var read = owner.DefineMethod("$ReadModuleNamespaceData", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String, _types.Object]);
        il = read.GetILGenerator();
        var instance = il.DeclareLocal(storage.Type);
        var fallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, storage.Type);
        il.Emit(OpCodes.Stloc, instance);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldfld, readerField);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldfld, readerField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(readerType, "Invoke", _types.String, _types.Object));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);
        var tryRead = owner.DefineMethod("$TryReadModuleNamespaceData", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Boolean, [_types.Object, _types.String, _types.Object.MakeByRefType()]);
        il = tryRead.GetILGenerator();
        instance = il.DeclareLocal(storage.Type);
        var missing = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, storage.Type);
        il.Emit(OpCodes.Stloc, instance);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldfld, readerField);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldfld, readerField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldind_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(readerType, "Invoke", _types.String, _types.Object));
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(missing);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return new(readerType, readerConstructor, create, read, tryRead);
    }

    // Emits one data value from an existing fallback local. This private transport changes
    // only actual namespace data rows; ordinary object values retain their path.
    private static void EmitModuleNamespaceDataRead(ILGenerator il, EmittedModuleNamespaceRuntime? namespaces,
        LocalBuilder value, Action emitTarget, Action emitKey)
    {
        if (namespaces is null) { il.Emit(OpCodes.Ldloc, value); return; }
        emitTarget();
        emitKey();
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, namespaces.Read);
    }
}
