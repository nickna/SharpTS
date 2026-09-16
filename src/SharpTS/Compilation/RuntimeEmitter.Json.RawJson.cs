using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct TSRawJsonClassInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedObjectStorageRuntime ObjectStorage
    );

    private readonly record struct JsonRawJsonMethodsInputs(
        EmittedArrayStorageRuntime ArrayStorage,
        MethodBuilder CreateException,
        EmittedObjectStorageRuntime ObjectStorage,
        EmittedStringCoercionRuntime StringCoercion,
        ConstructorBuilder TSSyntaxErrorCtor
    );

    private void EmitTSRawJsonClass(ModuleBuilder moduleBuilder, EmittedJsonImplementation json, TSRawJsonClassInputs inputs)
    {
        var type = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$RawJSON",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            inputs.ObjectStorage.Type);
        json.RawJsonType = type;

        var textField = type.DefineField("_rawText", _types.String,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = type.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, [_types.String]);
        json.RawJsonConstructor = ctor;
        var il = ctor.GetILGenerator();

        // base(new Dictionary { ["rawJSON"] = text })
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "rawJSON");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject,
            "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Call, inputs.ObjectStorage.Constructor);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, textField);

        // Raw JSON objects have a null prototype and are frozen.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, inputs.ObjectStorage.Freeze);
        il.Emit(OpCodes.Ret);

        var getter = type.DefineMethod("get_RawText",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        json.RawJsonTextGetter = getter;
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, textField);
        getterIl.Emit(OpCodes.Ret);
        type.CreateType();
    }

    private void EmitJsonRawJsonMethods(TypeBuilder typeBuilder, EmittedJsonImplementation json, JsonRawJsonMethodsInputs inputs)
    {
        var raw = typeBuilder.DefineMethod("JsonRawJSON",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        json.RawJson = raw;
        var il = raw.GetILGenerator();
        var text = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
        il.Emit(OpCodes.Stloc, text);

        var invalidBoundary = il.DefineLabel();
        var boundaryOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, invalidBoundary);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Trim"));
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, boundaryOk);
        il.MarkLabel(invalidBoundary);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TSSyntaxErrorCtor, "Invalid raw JSON text");
        il.MarkLabel(boundaryOk);

        // Reuse the real JSON parser for grammar validation. Raw JSON may only
        // contain a primitive JSON value, never an object or array.
        var parsed = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Call, json.Parse);
        il.Emit(OpCodes.Stloc, parsed);
        var primitive = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, parsed);
        il.Emit(OpCodes.Isinst, inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Brtrue, invalidBoundary);
        il.Emit(OpCodes.Ldloc, parsed);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, invalidBoundary);
        il.Emit(OpCodes.Ldloc, parsed);
        il.Emit(OpCodes.Isinst, inputs.ObjectStorage.Type);
        il.Emit(OpCodes.Brfalse, primitive);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TSSyntaxErrorCtor, "Raw JSON text must be a primitive value");
        il.MarkLabel(primitive);

        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Newobj, json.RawJsonConstructor);
        il.Emit(OpCodes.Ret);

        var isRaw = typeBuilder.DefineMethod("JsonIsRawJSON",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        json.IsRawJson = isRaw;
        il = isRaw.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, json.RawJsonType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }
}
