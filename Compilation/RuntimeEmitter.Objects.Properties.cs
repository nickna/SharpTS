using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    internal static void EmitToPascalCase(TypeBuilder typeBuilder, EmittedRuntime runtime)
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

    private static void EmitGetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFieldsProperty(object obj, string name) -> object
        // Uses reflection to access _fields dictionary on class instances
        // IMPORTANT: Check for getter methods (get_<name>) first, then fall back to _fields
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

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var getterMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var currentTypeLocal = il.DeclareLocal(_types.Type);

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check for getter method first: var getterMethod = obj.GetType().GetMethod("get_" + ToPascalCase(name));
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
        // var methodInfo = obj.GetType().GetMethod(name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        var methodLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Stloc, methodLocal);

        // if (methodInfo == null) return null;
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // return new $TSFunction(obj, methodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // SetFieldsProperty(object obj, string name, object value) -> void
        // Uses reflection to access _fields dictionary on class instances
        // IMPORTANT: Check for setter methods (set_<name>) first, then fall back to _fields
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

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setterMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var argsArrayLocal = il.DeclareLocal(_types.ObjectArray);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

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

        // Add currentType local
        var currentTypeLocal = il.DeclareLocal(_types.Type);

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

    private static void EmitGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // List - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // String - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

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

        il.MarkLabel(nullLabel);
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
        // For other properties on List (like methods push, pop, etc.), use GetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
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

    private static void EmitSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - try SetFieldsProperty for class instances
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }
}
