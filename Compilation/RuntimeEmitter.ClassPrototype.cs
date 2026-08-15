using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the runtime representation of an emitted user class's
    /// <c>prototype</c> property. The object is allocated without invoking the
    /// user constructor, then its per-class dynamic-field dictionaries are
    /// initialized so the normal <c>$IHasFields.GetProperty</c> path can expose
    /// methods and accessors.
    /// </summary>
    private void EmitClassPrototypeSupport(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder cacheField)
    {
        var cacheType = cacheField.FieldType;
        var tryGetValue = _types.GetMethod(
            cacheType, "TryGetValue", [_types.Type, _types.Object.MakeByRefType()])!;
        var add = _types.GetMethod(cacheType, "Add", [_types.Type, _types.Object])!;
        var getUninitializedObject = typeof(RuntimeHelpers).GetMethod(
            nameof(RuntimeHelpers.GetUninitializedObject),
            BindingFlags.Public | BindingFlags.Static,
            [_types.Type])!;
        var getField = _types.GetMethod(
            _types.Type, "GetField", [_types.String, typeof(BindingFlags)])!;
        var setValue = _types.GetMethod(
            typeof(FieldInfo), "SetValue", [_types.Object, _types.Object])!;
        var getBaseType = _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!;
        var isAssignableFrom = _types.GetMethod(
            _types.Type, "IsAssignableFrom", [_types.Type])!;

        var method = typeBuilder.DefineMethod(
            "GetClassPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type]);
        runtime.GetClassPrototypeMethod = method;

        var il = method.GetILGenerator();
        var prototypeLocal = il.DeclareLocal(_types.Object);
        var currentTypeLocal = il.DeclareLocal(_types.Type);
        var fieldsFieldLocal = il.DeclareLocal(typeof(FieldInfo));
        var baseTypeLocal = il.DeclareLocal(_types.Type);
        var basePrototypeLocal = il.DeclareLocal(_types.Object);
        var cacheMiss = il.DefineLabel();
        var fieldLoop = il.DefineLabel();
        var nextBase = il.DefineLabel();
        var fieldsReady = il.DefineLabel();
        var useObjectPrototype = il.DefineLabel();
        var prototypeReady = il.DefineLabel();

        // Return the stable object for repeated C.prototype reads.
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, prototypeLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, cacheMiss);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(cacheMiss);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getUninitializedObject);
        il.Emit(OpCodes.Stloc, prototypeLocal);

        // Publish before recursively resolving a base prototype.
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Callvirt, add);

        // Every emitted class currently owns a private _fields slot. Initialize
        // each slot in the CLR base chain because base GetProperty bodies access
        // their declaring class's slot directly.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.MarkLabel(fieldLoop);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, fieldsReady);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Callvirt, getField);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextBase);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Callvirt, setValue);
        il.MarkLabel(nextBase);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, getBaseType);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, fieldLoop);

        il.MarkLabel(fieldsReady);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getBaseType);
        il.Emit(OpCodes.Stloc, baseTypeLocal);
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Brfalse, useObjectPrototype);
        il.Emit(OpCodes.Ldtoken, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Callvirt, isAssignableFrom);
        il.Emit(OpCodes.Brfalse, useObjectPrototype);
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Stloc, basePrototypeLocal);
        il.Emit(OpCodes.Br, prototypeReady);

        il.MarkLabel(useObjectPrototype);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Stloc, basePrototypeLocal);

        il.MarkLabel(prototypeReady);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldloc, basePrototypeLocal);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ret);
    }
}
