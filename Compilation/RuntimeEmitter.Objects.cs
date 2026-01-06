using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitCreateObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateObject",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<string, object>),
            [typeof(Dictionary<string, object>)]
        );
        runtime.CreateObject = method;

        var il = method.GetILGenerator();
        // Just return the dictionary as-is (it's already created)
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
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetFieldsProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var tryMethodLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var valueLocal = il.DeclareLocal(typeof(object));
        var getterMethodLocal = il.DeclareLocal(typeof(MethodInfo));
        var currentTypeLocal = il.DeclareLocal(typeof(Type));

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check for getter method first: var getterMethod = obj.GetType().GetMethod("get_" + name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "get_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, getterMethodLocal);

        // if (getterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // return getterMethod.Invoke(obj, null);
        il.Emit(OpCodes.Ldloc, getterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy
        il.MarkLabel(tryFieldsLabel);

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
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
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // if (dict.TryGetValue(name, out value)) return value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Br, nextType);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Try to find a method with this name and wrap as TSFunction
        il.MarkLabel(tryMethodLabel);

        // First try array methods if it's an array
        var tryReflectionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetArrayMethod);
        var arrayMethodLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, arrayMethodLocal);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Brfalse, tryReflectionLabel);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Ret);

        // Try reflection for regular methods
        il.MarkLabel(tryReflectionLabel);
        // var methodInfo = obj.GetType().GetMethod(name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        var methodLocal = il.DeclareLocal(typeof(MethodInfo));
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
            typeof(void),
            [typeof(object), typeof(string), typeof(object)]
        );
        runtime.SetFieldsProperty = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var fieldsFieldLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var setterMethodLocal = il.DeclareLocal(typeof(MethodInfo));
        var argsArrayLocal = il.DeclareLocal(typeof(object[]));

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check for setter method first: var setterMethod = obj.GetType().GetMethod("set_" + name);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "set_");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, setterMethodLocal);

        // if (setterMethod == null) goto tryFields;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Brfalse, tryFieldsLabel);

        // Create args array: new object[] { value }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);

        // setterMethod.Invoke(obj, args); return;
        il.Emit(OpCodes.Ldloc, setterMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Pop); // Discard return value (setters return void but Invoke returns object)
        il.Emit(OpCodes.Ret);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        // Add currentType local
        var currentTypeLocal = il.DeclareLocal(typeof(Type));

        // currentType = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
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
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var fields = fieldsField.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto nextType;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto nextType;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, nextType);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        // nextType: currentType = currentType.BaseType; goto loopStart;
        il.MarkLabel(nextType);
        il.Emit(OpCodes.Ldloc, currentTypeLocal);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentTypeLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetArrayMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetArrayMethod(object arr, string methodName) -> TSFunction or null
        // Maps TypeScript array method names to .NET List methods
        var method = typeBuilder.DefineMethod(
            "GetArrayMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetArrayMethod = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();

        // Check if obj is List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notArrayLabel);

        // Map TypeScript method name to .NET method name
        // push -> Add, pop -> RemoveAt(Count-1), etc.
        var pushLabel = il.DefineLabel();
        var popLabel = il.DefineLabel();
        var shiftLabel = il.DefineLabel();

        // Check for "push"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "push");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, pushLabel);

        // Check for "pop"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pop");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, popLabel);

        // Unknown array method - return null
        il.Emit(OpCodes.Br, nullLabel);

        // Handle push - wrap List.Add as TSFunction
        il.MarkLabel(pushLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, typeof(List<object>).GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Ldtoken, typeof(List<object>));
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        // Handle pop - need special handling since pop returns removed element
        il.MarkLabel(popLabel);
        // For pop, we'll create a TSFunction that wraps a helper method
        // For now, return null and handle pop differently
        il.Emit(OpCodes.Br, nullLabel);

        il.MarkLabel(notArrayLabel);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
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
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // List - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
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
        var valueLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
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
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        var notStrLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrLenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
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
            typeof(void),
            [typeof(object), typeof(string), typeof(object)]
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
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
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
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitMergeIntoObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MergeIntoObject",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(Dictionary<string, object>), typeof(object)]
        );
        runtime.MergeIntoObject = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Check if source is dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - do nothing
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Iterate and copy
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current and add to target
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        var kvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.GetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Dict with string key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use GetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: use GetSymbolDict(obj).TryGetValue(index, out value)
        il.MarkLabel(symbolKeyLabel);
        var symbolDictLocal = il.DeclareLocal(typeof(Dictionary<object, object?>));
        var symbolValueLocal = il.DeclareLocal(typeof(object));
        // var symbolDict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);
        // if (symbolDict.TryGetValue(index, out value)) return value; else return null;
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, symbolValueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object?>).GetMethod("TryGetValue")!);
        var symbolFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, symbolFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolFoundLabel);
        il.Emit(OpCodes.Ldloc, symbolValueLocal);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use GetFieldsProperty(obj, index as string)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise return null (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        var valueLocal = il.DeclareLocal(typeof(object));

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        var foundNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundNumLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundNumLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object), typeof(object), typeof(object)]
        );
        runtime.SetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use SetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return (ignore)
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: GetSymbolDict(obj)[index] = value
        il.MarkLabel(symbolKeyLabel);
        // GetSymbolDict(obj)[index] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object?>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use SetFieldsProperty(obj, index as string, value)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("set_Item", [typeof(int), typeof(object)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise ignore (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitInvokeValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeValue",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object[])]
        );
        runtime.InvokeValue = method;

        var il = method.GetILGenerator();
        // Check if value is $TSFunction and call Invoke
        // For now, use reflection
        il.Emit(OpCodes.Ldarg_0);
        var nullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Try to find and call Invoke method
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Dup);
        var noInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noInvokeLabel);

        // Has Invoke - call it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noInvokeLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetSuperMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetSuperMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(string)]
        );
        runtime.GetSuperMethod = method;

        var il = method.GetILGenerator();
        var methodInfoLocal = il.DeclareLocal(typeof(MethodInfo));
        var baseTypeLocal = il.DeclareLocal(typeof(Type));
        var nullLabel = il.DefineLabel();

        // Check if instance is null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Get base type and store it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("BaseType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, baseTypeLocal);

        // Check if baseType is null
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Get method from base type
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // Check if method was found
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Create $TSFunction(instance, methodInfo) - a callable wrapper
        il.Emit(OpCodes.Ldarg_0);  // instance (target)
        il.Emit(OpCodes.Ldloc, methodInfoLocal);  // methodInfo
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Exception),
            [typeof(object)]
        );
        runtime.CreateException = method;

        var il = method.GetILGenerator();
        var exLocal = il.DeclareLocal(typeof(Exception));

        // var ex = new Exception(value?.ToString() ?? "null")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, exLocal);

        // ex.Data["__tsValue"] = value;  (preserve original value)
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("set_Item")!);

        // return ex;
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitWrapException(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(Exception)]
        );
        runtime.WrapException = method;

        var il = method.GetILGenerator();
        var fallbackLabel = il.DefineLabel();
        var tsValueLocal = il.DeclareLocal(typeof(object));

        // Check if ex.Data contains "__tsValue" (TypeScript throw value)
        // if (ex.Data.Contains("__tsValue")) return ex.Data["__tsValue"];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("Contains")!);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // Return the original TypeScript value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Data")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IDictionary).GetMethod("get_Item")!);
        il.Emit(OpCodes.Ret);

        // Fallback: wrap standard .NET exceptions as Dictionary
        il.MarkLabel(fallbackLabel);
        // return new Dictionary<string, object> { ["message"] = ex.Message, ["name"] = ex.GetType().Name }
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRandom(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder randomField)
    {
        var method = typeBuilder.DefineMethod(
            "Random",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            Type.EmptyTypes
        );
        runtime.Random = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, randomField);
        il.Emit(OpCodes.Callvirt, typeof(Random).GetMethod("NextDouble")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetEnumMemberName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEnumMemberName",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(string), typeof(double), typeof(double[]), typeof(string[])]
        );
        runtime.GetEnumMemberName = method;

        var il = method.GetILGenerator();
        // Simple linear search through keys to find matching value
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if keys[i] == value
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatchLabel);

        // Found - return values[i]
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatchLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Not found - throw
        il.Emit(OpCodes.Ldstr, "Value not found in enum");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    private static void EmitConcatTemplate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatTemplate",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object[])]
        );
        runtime.ConcatTemplate = method;

        var il = method.GetILGenerator();

        // Use StringBuilder to concatenate stringified parts
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        var indexLocal = il.DeclareLocal(typeof(int));
        var lengthLocal = il.DeclareLocal(typeof(int));

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // length = parts.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= length) goto end
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // sb.Append(Stringify(parts[index]))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop); // discard StringBuilder return value

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitObjectRest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectRest",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<string, object>),
            [typeof(Dictionary<string, object>), typeof(List<object>)]
        );
        runtime.ObjectRest = method;

        var il = method.GetILGenerator();

        // Create result dictionary
        var resultLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Create HashSet<string> from excludeKeys
        var excludeSetLocal = il.DeclareLocal(typeof(HashSet<string>));
        il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, excludeSetLocal);

        // Add each exclude key to the set
        var excludeIndexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);

        var excludeLoopStart = il.DefineLabel();
        var excludeLoopEnd = il.DefineLabel();

        il.MarkLabel(excludeLoopStart);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, excludeLoopEnd);

        // Get excludeKeys[i] and add to set if not null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        var keyLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, keyLocal);

        var skipAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add", [typeof(string)])!);
        il.Emit(OpCodes.Pop); // discard bool return

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);
        il.Emit(OpCodes.Br, excludeLoopStart);

        il.MarkLabel(excludeLoopEnd);

        // Iterate over source dictionary keys
        // Get enumerator from source.Keys
        var keysEnumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.KeyCollection.Enumerator));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);
        // MoveNext
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get Current key
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.KeyCollection.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        var currentKeyLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // Check if key is in excludeSet
        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Contains", [typeof(string)])!);
        var skipKey = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipKey);

        // Add to result: result[key] = source[key]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Item")!.GetSetMethod()!);

        il.MarkLabel(skipKey);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.KeyCollection.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetValues = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var emptyLabel = il.DefineLabel();

        // Locals for class instance path
        var fieldInfoLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));

        // Check if Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if obj is not null (for class instance path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Class instance path: get _fields via reflection
        // var fieldInfo = obj.GetType().GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // if (fieldInfo == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var fields = fieldInfo.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto empty
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // return new List<object>(dict.Values);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Values")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);

        // Empty list fallback
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // Dictionary path
        il.MarkLabel(dictLabel);
        // Get values: new List<object>(dict.Values)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Values")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetEntries is more complex - we need to iterate and create [key, value] pairs
        var method = typeBuilder.DefineMethod(
            "GetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetEntries = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var classLoopStart = il.DefineLabel();
        var classLoopEnd = il.DefineLabel();

        // Locals for common use
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var entryLocal = il.DeclareLocal(typeof(List<object>));

        // Locals for class instance path
        var fieldInfoLocal = il.DeclareLocal(typeof(FieldInfo));
        var fieldsLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));

        // Check if Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if obj is not null (for class instance path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Class instance path: get _fields via reflection
        // var fieldInfo = obj.GetType().GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // if (fieldInfo == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var fields = fieldInfo.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var dict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto empty
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Create result list for class instance path
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get enumerator from _fields dict
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Class instance loop start
        il.MarkLabel(classLoopStart);

        // MoveNext
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, classLoopEnd);

        // Get current
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // Create entry [key, value]
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);

        // Add key
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add value
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add entry to result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Loop back
        il.Emit(OpCodes.Br, classLoopStart);

        il.MarkLabel(classLoopEnd);

        // Dispose enumerator for class instance path
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // Return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Empty list fallback
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // Dictionary path
        il.MarkLabel(dictLabel);

        // Create result list
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop start
        il.MarkLabel(loopStart);

        // MoveNext
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // Create entry [key, value]
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);

        // Add key
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add value
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Add entry to result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Loop back
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // Return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIsArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, trueLabel);

        // False
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, endLabel);

        // True
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
