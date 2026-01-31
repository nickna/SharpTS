using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    internal void EmitToPascalCase(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ToPascalCase(string name) -> string
        // Converts "camelCase" to "PascalCase" by upper-casing first character
        var method = typeBuilder.DefineMethod(
            "ToPascalCase",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.ToPascalCase = method;

        var il = method.GetILGenerator();
        var returnOriginalLabel = il.DefineLabel();

        // if (string.IsNullOrEmpty(name)) return name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty"));
        il.Emit(OpCodes.Brtrue, returnOriginalLabel);

        // if (char.IsUpper(name[0])) return name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, typeof(char).GetMethod("IsUpper", [typeof(char)])!);
        il.Emit(OpCodes.Brtrue, returnOriginalLabel);

        // return char.ToString(char.ToUpperInvariant(name[0])) + name.Substring(1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToUpperInvariant", [typeof(char)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", [typeof(char)])!);  // static char.ToString(char)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnOriginalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFieldsProperty(object obj, string name) -> object
        // Uses reflection to access _fields dictionary on class instances
        // Check getter methods first (for standalone execution), then _fields dictionary
        // Walks up the type hierarchy to find fields in parent classes
        var method = typeBuilder.DefineMethod(
            "GetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetFieldsProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var tryMethodLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var tryGetterLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var getterMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var currentTypeLocal = il.DeclareLocal(_types.Type);
        var interfaceLocal = il.DeclareLocal(_types.Type);

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Special case: HashSet<object?>.size for compiled Sets
        // Compiled code uses HashSet<object?> for Sets, and structuredClone returns the same type.
        // When accessing .size, we need to return HashSet.Count.
        var hashSetSizeLabel = il.DefineLabel();
        var afterHashSetSizeLabel = il.DefineLabel();

        // Check if obj is HashSet<object?> and name == "size"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
        il.Emit(OpCodes.Brfalse, afterHashSetSizeLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterHashSetSizeLabel);

        // It's a HashSet and property is "size" - return Count as double
        il.MarkLabel(hashSetSizeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.HashSetOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(afterHashSetSizeLabel);

        // Try ISharpTSPropertyAccessor interface FIRST (for interpreter runtime types like SharpTSKeyObject)
        // Types implementing this interface may have custom property handling (e.g., enum to string conversion)
        // Uses reflection to avoid hard reference to SharpTS.dll for standalone execution

        // var iface = obj.GetType().GetInterface("ISharpTSPropertyAccessor");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "ISharpTSPropertyAccessor");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetInterface", _types.String));
        il.Emit(OpCodes.Stloc, interfaceLocal);

        // if (iface == null) goto tryGetter;
        il.Emit(OpCodes.Ldloc, interfaceLocal);
        il.Emit(OpCodes.Brfalse, tryGetterLabel);

        // var getPropertyMethod = iface.GetMethod("GetProperty");
        var getPropertyMethodLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, interfaceLocal);
        il.Emit(OpCodes.Ldstr, "GetProperty");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, getPropertyMethodLocal);

        // if (getPropertyMethod == null) goto tryGetter;
        il.Emit(OpCodes.Ldloc, getPropertyMethodLocal);
        il.Emit(OpCodes.Brfalse, tryGetterLabel);

        // var value = getPropertyMethod.Invoke(obj, new object[] { name });
        il.Emit(OpCodes.Ldloc, getPropertyMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, valueLocal);

        // if (value == null) goto tryGetter;
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, tryGetterLabel);

        // if (value is BuiltInMethod) goto tryMethod; (skip interpreter-only wrappers)
        // Check by type name to avoid hard reference
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "BuiltInMethod");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Equals", _types.String));
        il.Emit(OpCodes.Brtrue, tryMethodLabel);

        // return value;
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // tryGetter: Check for CLR getter method (for emitted types like $TextEncoder, $TextDecoder)
        // This ensures standalone execution works for types without ISharpTSPropertyAccessor
        il.MarkLabel(tryGetterLabel);

        // Check for getter method: var getterMethod = obj.GetType().GetMethod("get_" + ToPascalCase(name));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "get_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);  // Convert to PascalCase
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, getterMethodLocal);

        // if (getterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // return getterMethod.Invoke(obj, null);
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy
        il.MarkLabel(tryFieldsLabel);

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, currentTypeLocal);

        // Loop through type hierarchy
        var loopStart = il.DefineLabel();
        var nextType = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (currentType == null) goto tryMethod;
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, tryMethodLabel);

        // var fieldsField = currentType.GetField("_fields", DeclaredOnly | Instance | NonPublic);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, _types.BindingFlags));
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // if (dict.TryGetValue(name, out value)) return value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Br, nextType);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Try to find a method with this name and wrap as TSFunction
        il.MarkLabel(tryMethodLabel);

        // First try array methods if it's an array
        var tryReflectionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetArrayMethod);
        var arrayMethodLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, arrayMethodLocal);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Brfalse, tryReflectionLabel);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Ret);

        // Try reflection for regular methods
        il.MarkLabel(tryReflectionLabel);
        var methodLocal = il.DeclareLocal(_types.MethodInfo);
        var typeLocal = il.DeclareLocal(_types.Type);

        // typeLocal = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // First try: methodInfo = type.GetMethod(name);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, methodLocal);

        // if (methodInfo != null) goto wrapMethod;
        var wrapMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brtrue, wrapMethodLabel);

        // Second try: methodInfo = type.GetMethod(ToPascalCase(name));
        // This handles C# methods with PascalCase names (e.g., "Update" for "update")
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, methodLocal);

        // if (methodInfo == null) return null;
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // wrapMethod: return new $TSFunction(obj, methodInfo);
        il.MarkLabel(wrapMethodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // SetFieldsProperty(object obj, string name, object value) -> void
        // Uses reflection to access _fields dictionary on class instances
        // Check setter methods first (for standalone execution), then _fields dictionary
        var method = typeBuilder.DefineMethod(
            "SetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object]
        );
        runtime.SetFieldsProperty = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var trySetterLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setterMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var argsArrayLocal = il.DeclareLocal(_types.ObjectArray);
        var currentTypeLocal = il.DeclareLocal(_types.Type);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Try ISharpTSPropertyAccessor interface FIRST (for interpreter runtime types)
        // Types implementing this interface may have custom property handling
        var interfaceLocal = il.DeclareLocal(_types.Type);

        // var iface = obj.GetType().GetInterface("ISharpTSPropertyAccessor");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "ISharpTSPropertyAccessor");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetInterface", _types.String));
        il.Emit(OpCodes.Stloc, interfaceLocal);

        // if (iface == null) goto trySetter;
        il.Emit(OpCodes.Ldloc, interfaceLocal);
        il.Emit(OpCodes.Brfalse, trySetterLabel);

        // var setPropertyMethod = iface.GetMethod("SetProperty");
        var setPropertyMethodLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, interfaceLocal);
        il.Emit(OpCodes.Ldstr, "SetProperty");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, setPropertyMethodLocal);

        // if (setPropertyMethod == null) goto trySetter;
        il.Emit(OpCodes.Ldloc, setPropertyMethodLocal);
        il.Emit(OpCodes.Brfalse, trySetterLabel);

        // setPropertyMethod.Invoke(obj, new object[] { name, value }); return;
        il.Emit(OpCodes.Ldloc, setPropertyMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        // trySetter: Check for CLR setter method (for emitted types like $TextEncoder, $TextDecoder)
        // This ensures standalone execution works for types without ISharpTSPropertyAccessor
        il.MarkLabel(trySetterLabel);

        // Check for setter method: var setterMethod = obj.GetType().GetMethod("set_" + ToPascalCase(name));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "set_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);  // Convert to PascalCase
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, setterMethodLocal);

        // if (setterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // Create args array: new object[] { value }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);

        // setterMethod.Invoke(obj, args); return;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop); // Discard return value (setters return void but Invoke returns object)
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, currentTypeLocal);

        // Loop through type hierarchy
        var loopStart = il.DefineLabel();
        var nextType = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (currentType == null) return;
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // var fieldsField = currentType.GetField("_fields", DeclaredOnly | Instance | NonPublic);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, _types.BindingFlags));
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SetFieldsPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects.
    /// </summary>
    private void EmitSetFieldsPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetFieldsPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetFieldsPropertyStrict = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var notFrozenLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setterMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var argsArrayLocal = il.DeclareLocal(_types.ObjectArray);
        var frozenCheckLocal = il.DeclareLocal(_types.Object);
        var currentTypeLocal = il.DeclareLocal(_types.Type);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, frozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var frozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, frozenSilentLabel);
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot assign to read only property '");
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Ldstr, "' of object");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(frozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode

        il.MarkLabel(notFrozenLabel);

        // Check for setter method first: var setterMethod = obj.GetType().GetMethod("set_" + ToPascalCase(name));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "set_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);  // Convert to PascalCase
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, setterMethodLocal);

        // if (setterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // Create args array: new object[] { value }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);

        // setterMethod.Invoke(obj, args); return;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop); // Discard return value (setters return void but Invoke returns object)
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, currentTypeLocal);

        // Loop through type hierarchy
        var loopStart = il.DefineLabel();
        var nextType = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (currentType == null) return;
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // var fieldsField = currentType.GetField("_fields", DeclaredOnly | Instance | NonPublic);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, _types.BindingFlags));
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetListProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetListProperty(list: List<object>, name: string) -> object?
        // Forwards to RuntimeTypes.GetListProperty which handles array methods
        var method = typeBuilder.DefineMethod(
            "GetListProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.String]
        );
        runtime.GetListProperty = method;

        var il = method.GetILGenerator();

        // Call RuntimeTypes.GetListProperty(list, name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod(
            "GetListProperty",
            [typeof(List<object>), typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var namespaceLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // $TSNamespace - call ns.Get(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSNamespaceType);
        il.Emit(OpCodes.Brtrue, namespaceLabel);

        // $Object (with getter/setter support) - call obj.GetProperty(name)
        var tsObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // Map (Dictionary<object, object>) - check for "size" property
        var mapLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Brtrue, mapLabel);

        // Dictionary (regular object)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // List - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // $Array - check for "length" (wrapper around List<object?>)
        var sharpTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, sharpTSArrayLabel);

        // String - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // $Buffer - check for "length" and "toString"
        var tsBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, tsBufferLabel);

        // $TSFunction - check for bind/call/apply
        var tsFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionLabel);

        // $BoundTSFunction - also check for bind/call/apply
        var boundFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, boundFunctionLabel);

        // Task<object?> (Promise) - check for then/catch/finally
        var promiseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, promiseLabel);

        // $Promise type (used by fetch, etc.) - check for then/catch/finally
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, promiseLabel);

        // Default - try to access _fields dictionary via reflection for class instances
        var classInstanceLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, classInstanceLabel);

        // Class instance handler - uses reflection to access _fields
        il.MarkLabel(classInstanceLabel);
        // Call GetFieldsProperty(obj, name) helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // Namespace handler - call ns.Get(name)
        il.MarkLabel(namespaceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSNamespaceType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSNamespaceGet);
        il.Emit(OpCodes.Ret);

        // $Object handler - call obj.GetProperty(name) which handles getters
        il.MarkLabel(tsObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // $TSFunction handler - call GetFunctionMethod(func, name)
        il.MarkLabel(tsFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);

        // $BoundTSFunction handler - call GetFunctionMethod(func, name)
        il.MarkLabel(boundFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);

        // Promise (Task<object?> or $Promise) handler - return TSFunction wrappers for then/catch/finally
        il.MarkLabel(promiseLabel);
        // First, extract the underlying Task if this is a $Promise object
        // Store the task in a local variable for use in creating TSFunction wrappers
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var isTSPromiseLabel = il.DefineLabel();
        var haveTaskLabel = il.DefineLabel();

        // Check if obj is $Promise
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, isTSPromiseLabel);

        // It's a raw Task<object?>, use directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, haveTaskLabel);

        // It's a $Promise, extract the Task property
        il.MarkLabel(isTSPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, taskLocal);

        il.MarkLabel(haveTaskLabel);

        // Check for "then"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notPromiseThenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notPromiseThenLabel);
        // Return TSFunction wrapper for PromiseThen with the task as target
        il.Emit(OpCodes.Ldloc, taskLocal);  // target (the underlying task)
        il.Emit(OpCodes.Ldtoken, runtime.PromiseThen);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPromiseThenLabel);

        // Check for "catch"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "catch");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notPromiseCatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notPromiseCatchLabel);
        // Return TSFunction wrapper for PromiseCatch with the task as target
        il.Emit(OpCodes.Ldloc, taskLocal);  // target (the underlying task)
        il.Emit(OpCodes.Ldtoken, runtime.PromiseCatch);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPromiseCatchLabel);

        // Check for "finally"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "finally");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notPromiseFinallyLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notPromiseFinallyLabel);
        // Return TSFunction wrapper for PromiseFinally with the task as target
        il.Emit(OpCodes.Ldloc, taskLocal);  // target (the underlying task)
        il.Emit(OpCodes.Ldtoken, runtime.PromiseFinally);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPromiseFinallyLabel);

        // Unknown promise property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // dict.TryGetValue(name, out value) ? value : null
        var valueLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        // Store the dictionary in a local for later use with BindThis
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);

        // If value is a TSFunction, call BindThis(dict) on it
        // to bind 'this' for object method shorthand
        var notTSFunction = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunction);

        // Call func.BindThis(dict)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionBindThis);

        il.MarkLabel(notTSFunction);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // Map (Dictionary<object, object>) handler - check for "size" property
        il.MarkLabel(mapLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notMapSizeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMapSizeLabel);
        // Return map.Count as double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryObjectObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notMapSizeLabel);
        // For other Map properties, return null (methods like get, set, has are handled by direct calls)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLengthLabel);
        // For other properties on List (like methods push, pop, etc.), use GetListProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetListProperty);
        il.Emit(OpCodes.Ret);

        // $Array handler - access Elements.Count for "length"
        il.MarkLabel(sharpTSArrayLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notSharpTSArrayLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSharpTSArrayLengthLabel);
        // Get arr.Elements.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObjectNullable, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSharpTSArrayLengthLabel);
        // For other properties on $Array, fall through to GetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // $Buffer handler - "length" and "toString"
        il.MarkLabel(tsBufferLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notBufferLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferLenLabel);
        // Get buf.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBufferLenLabel);
        // Check for "toString" - return a wrapper that calls ToEncodedString
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notBufferToStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferToStringLabel);
        // Create a TSFunction wrapper for ToEncodedString
        // For dynamically generated types, we need both method and type tokens
        il.Emit(OpCodes.Ldarg_0);  // target (the buffer)
        il.Emit(OpCodes.Ldtoken, runtime.TSBufferToString);
        il.Emit(OpCodes.Ldtoken, runtime.TSBufferType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBufferToStringLabel);
        // Unknown buffer property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStrLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrLenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrLenLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object]
        );
        runtime.SetProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var tsObjectLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // $Object (with setter support) - call obj.SetProperty(name, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict or $Object - try SetFieldsProperty for class instances
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // $Object handler - call obj.SetProperty(name, value) which handles setters
        il.MarkLabel(tsObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // For dictionaries, check frozen/sealed tables and silently ignore if frozen/sealed
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, nullLabel); // Frozen - silently return

        // Check if sealed and property doesn't exist
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, doSetLabel); // Not sealed, proceed to set

        // Object is sealed - check if property exists
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, nullLabel); // Property doesn't exist, silently return

        // Actually set the property
        il.MarkLabel(doSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SetPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    private void EmitSetPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetPropertyStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if $Object
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict or $Object - fall back to SetFieldsPropertyStrict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetFieldsPropertyStrict);
        il.Emit(OpCodes.Ret);

        // $Object - call SetPropertyStrict
        il.MarkLabel(sharpTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetPropertyStrict);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // For dictionaries, check frozen/sealed tables
        var frozenCheckLabel = il.DefineLabel();
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, sealedCheckLabel); // Not frozen, check sealed

        // Object is frozen - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and frozen - throw TypeError
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot assign to read only property '");
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Ldstr, "' of object");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Check if sealed and property doesn't exist
        il.MarkLabel(sealedCheckLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, doSetLabel); // Not sealed, proceed to set

        // Object is sealed - check if property exists
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel); // Property exists, can modify

        // Property doesn't exist and object is sealed - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and sealed with new property - throw TypeError
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot add property '");
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Ldstr, "' to a sealed object");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Actually set the property
        il.MarkLabel(doSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SetIndexStrict(object obj, object index, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen/sealed arrays.
    /// </summary>
    private void EmitSetIndexStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndexStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Boolean]
        );
        runtime.SetIndexStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var sharpTSArrayLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if $Array (for strict mode support)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, sharpTSArrayLabel);

        // List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default - just return
        il.Emit(OpCodes.Ret);

        // $Array - call SetStrict with index and strictMode
        il.MarkLabel(sharpTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        // Convert index to int: (int)(double)index
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetStrict);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check if frozen - in strict mode, throw TypeError
        var listSetLabel = il.DefineLabel();
        var listFrozenCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, listFrozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var listNotFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotFrozenLabel);
        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var listFrozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listFrozenSilentLabel);
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot assign to read only property of frozen array");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(listFrozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode
        il.MarkLabel(listNotFrozenLabel);
        // Not frozen - set normally
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObjectNullable, "set_Item", _types.Int32, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Dictionary uses string keys - convert index to string and set
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DeleteProperty(object obj, string name) -> bool
    /// Removes a property from an object and returns true if successful.
    /// Returns false for frozen/sealed objects or if the object doesn't support deletion.
    /// </summary>
    private void EmitDeleteProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DeleteProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.DeleteProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null check - return true (deleting from null is allowed in JS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

        // Check if $TSObject
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Other types - cannot delete properties, return true (JS behavior for non-configurable)
        il.Emit(OpCodes.Br, trueLabel);

        // $TSObject - call DeleteProperty instance method
        il.MarkLabel(sharpTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectDeleteProperty);
        il.Emit(OpCodes.Ret);

        // Dictionary - use Remove
        il.MarkLabel(dictLabel);
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Check if sealed
        il.MarkLabel(notFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSealedLabel);

        // Sealed - return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Not frozen/sealed - do the removal
        il.MarkLabel(notSealedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Ret);

        // Return true (default for null and other types)
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
}

