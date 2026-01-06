using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitJsonParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonParse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.JsonParse = method;

        // We need to emit a call to a method that converts JSON to Dict/List
        // Since this requires recursive conversion, we emit a helper method
        var il = method.GetILGenerator();

        // Call JsonParseHelper which we'll emit separately
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseHelper(typeBuilder));
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonParseHelper(TypeBuilder typeBuilder)
    {
        // Parse JSON using RuntimeTypes helper
        var method = typeBuilder.DefineMethod(
            "JsonParseHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );

        var il = method.GetILGenerator();

        // Call RuntimeTypes.JsonParse directly - this method exists in the emitted assembly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseStaticHelper(typeBuilder));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static MethodBuilder EmitJsonParseStaticHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ParseJsonValue",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );

        var il = method.GetILGenerator();

        // Simple implementation: just call ToString and return
        // This won't properly parse, but at least we can test the infrastructure
        var strLocal = il.DeclareLocal(typeof(string));
        var notNullLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Parse using JsonDocument - need to include JsonDocumentOptions parameter
        var parseMethod = typeof(System.Text.Json.JsonDocument).GetMethod("Parse",
            [typeof(string), typeof(System.Text.Json.JsonDocumentOptions)])!;
        var optionsLocal = il.DeclareLocal(typeof(System.Text.Json.JsonDocumentOptions));
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Initobj, typeof(System.Text.Json.JsonDocumentOptions));
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, parseMethod);

        // Get RootElement
        var docLocal = il.DeclareLocal(typeof(System.Text.Json.JsonDocument));
        il.Emit(OpCodes.Stloc, docLocal);
        il.Emit(OpCodes.Ldloc, docLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Json.JsonDocument).GetProperty("RootElement")!.GetGetMethod()!);

        // Store element first before calling Clone (Clone is an instance method on struct)
        var elemLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));
        il.Emit(OpCodes.Stloc, elemLocal);

        // Clone the element to avoid disposal issues (need address for struct instance method)
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("Clone")!);

        // Store cloned element and convert it
        var clonedLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.Emit(OpCodes.Ldloca, clonedLocal);
        il.Emit(OpCodes.Call, EmitConvertJsonElementHelper(typeBuilder));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static MethodBuilder EmitConvertJsonElementHelper(TypeBuilder typeBuilder)
    {
        // Convert JsonElement to appropriate runtime type
        var method = typeBuilder.DefineMethod(
            "ConvertJsonElement",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(System.Text.Json.JsonElement).MakeByRefType()] // byref for struct
        );

        var il = method.GetILGenerator();

        var resultDictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var resultListLocal = il.DeclareLocal(typeof(List<object>));
        var propEnumLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement.ObjectEnumerator));
        var arrEnumLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement.ArrayEnumerator));
        var propLocal = il.DeclareLocal(typeof(System.Text.Json.JsonProperty));
        var elemLocal = il.DeclareLocal(typeof(System.Text.Json.JsonElement));

        var undefinedLabel = il.DefineLabel();
        var objectLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var trueLiteralLabel = il.DefineLabel();
        var falseLiteralLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();
        var objLoopStart = il.DefineLabel();
        var objLoopEnd = il.DefineLabel();
        var arrLoopStart = il.DefineLabel();
        var arrLoopEnd = il.DefineLabel();

        // switch on element.ValueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetProperty("ValueKind")!.GetGetMethod()!);

        // Check each value kind
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Object);
        il.Emit(OpCodes.Beq, objectLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Array);
        il.Emit(OpCodes.Beq, arrayLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.String);
        il.Emit(OpCodes.Beq, stringLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.Number);
        il.Emit(OpCodes.Beq, numberLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.True);
        il.Emit(OpCodes.Beq, trueLiteralLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonValueKind.False);
        il.Emit(OpCodes.Beq, falseLiteralLabel);

        il.Emit(OpCodes.Pop); // pop the valueKind we've been checking
        il.Emit(OpCodes.Br, nullLabel);

        // Object
        il.MarkLabel(objectLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("EnumerateObject")!);
        il.Emit(OpCodes.Stloc, propEnumLocal);

        il.MarkLabel(objLoopStart);
        il.Emit(OpCodes.Ldloca, propEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ObjectEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, objLoopEnd);

        il.Emit(OpCodes.Ldloca, propEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ObjectEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, propLocal);

        // dict[name] = ConvertJsonElement(ref value)
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldloca, propLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonProperty).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, propLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonProperty).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Br, objLoopStart);

        il.MarkLabel(objLoopEnd);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ret);

        // Array
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("EnumerateArray")!);
        il.Emit(OpCodes.Stloc, arrEnumLocal);

        il.MarkLabel(arrLoopStart);
        il.Emit(OpCodes.Ldloca, arrEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ArrayEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, arrLoopEnd);

        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ldloca, arrEnumLocal);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement.ArrayEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Ldloca, elemLocal);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Br, arrLoopStart);

        il.MarkLabel(arrLoopEnd);
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ret);

        // String
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("GetString")!);
        il.Emit(OpCodes.Ret);

        // Number
        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.Text.Json.JsonElement).GetMethod("GetDouble")!);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);

        // True
        il.MarkLabel(trueLiteralLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        // False
        il.MarkLabel(falseLiteralLabel);
        il.Emit(OpCodes.Pop); // pop valueKind
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        // Null/Undefined
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static void EmitJsonParseWithReviver(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the ApplyReviver helper
        var applyReviver = EmitApplyReviverHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonParseWithReviver",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.JsonParseWithReviver = method;

        var il = method.GetILGenerator();
        var noReviverLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if reviver is null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noReviverLabel);

        // Parse JSON
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        // Apply reviver: ApplyReviver(parsed, "", reviver)
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, applyReviver);
        il.Emit(OpCodes.Br, endLabel);

        // No reviver - just parse
        il.MarkLabel(noReviverLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitApplyReviverHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ApplyReviver(object value, object key, object reviver) -> object
        var method = typeBuilder.DefineMethod(
            "ApplyReviver",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object), typeof(object)]
        );

        var il = method.GetILGenerator();

        // Locals
        var newDictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var newListLocal = il.DeclareLocal(typeof(List<object>));
        var enumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var listEnumLocal = il.DeclareLocal(typeof(List<object>.Enumerator));
        var kvpLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var elemLocal = il.DeclareLocal(typeof(object));
        var resultLocal = il.DeclareLocal(typeof(object));
        var valueLocal = il.DeclareLocal(typeof(object));
        var argsLocal = il.DeclareLocal(typeof(object[]));

        var notDictLabel = il.DefineLabel();
        var notListLabel = il.DefineLabel();
        var invokeReviverLabel = il.DefineLabel();
        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();

        // Store value in local for modification
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Check if value is Dictionary<string, object>
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // It's a dictionary - process it
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, newDictLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumLocal);

        // Loop
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get current kvp
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // Recursively apply reviver: result = ApplyReviver(kvp.Value, kvp.Key, reviver)
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // If result is not null, add to new dict
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brfalse, dictLoopStart);

        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, invokeReviverLabel);

        // Check if value is List<object>
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notListLabel);

        // It's a list - process it
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, newListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Get list enumerator
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, listEnumLocal);

        // Loop
        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloca, listEnumLocal);
        il.Emit(OpCodes.Call, typeof(List<object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, listLoopEnd);

        // Get current element
        il.Emit(OpCodes.Ldloca, listEnumLocal);
        il.Emit(OpCodes.Call, typeof(List<object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);

        // Recursively apply reviver: result = ApplyReviver(elem, index, reviver)
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add result to new list
        il.Emit(OpCodes.Ldloc, newListLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // Increment index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Ldloc, newListLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, invokeReviverLabel);

        // Not dict or list - just call reviver on the value
        il.MarkLabel(notListLabel);

        // Call reviver for this node
        il.MarkLabel(invokeReviverLabel);

        // Create args array [key, value]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // Call reviver.Invoke(args)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private static void EmitJsonStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.JsonStringify = method;

        var il = method.GetILGenerator();

        // Call the stringify helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0); // indent = 0
        il.Emit(OpCodes.Ldc_I4_0); // depth = 0
        il.Emit(OpCodes.Call, EmitJsonStringifyHelper(typeBuilder));
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonStringifyHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "StringifyValue",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(int), typeof(int)] // value, indent, depth
        );

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumber(il);

        // string - use JsonSerializer.Serialize<string>(value, null)
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldnull); // options = null
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array
        il.MarkLabel(listLabel);
        EmitStringifyArray(il, method);

        // Dictionary<string, object> - stringify object
        il.MarkLabel(dictLabel);
        EmitStringifyObject(il, method);

        return method;
    }

    private static void EmitFormatNumber(ILGenerator il)
    {
        var local = il.DeclareLocal(typeof(double));
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, local);

        // Check NaN/Infinity
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        // Check if integer
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        // Float format
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        // NaN/Infinity -> "null"
        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // Integer format
        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, typeof(long).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyArray(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var arrLocal = il.DeclareLocal(typeof(List<object>));
        var iLocal = il.DeclareLocal(typeof(int));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(StringifyValue(arr[i], indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyObject(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var firstLocal = il.DeclareLocal(typeof(bool));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(JsonSerializer.Serialize<string>(key, null));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldnull); // options = null
        var serializeKeyMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeKeyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValue(value, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, typeof(Dictionary<string, object>.Enumerator));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitJsonStringifyFull(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the helper that supports allowedKeys
        var stringifyWithKeys = EmitJsonStringifyWithKeysHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "JsonStringifyFull",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object), typeof(object)]
        );
        runtime.JsonStringifyFull = method;

        var il = method.GetILGenerator();

        // Declare ALL locals upfront
        var indentLocal = il.DeclareLocal(typeof(int));
        var allowedKeysLocal = il.DeclareLocal(typeof(HashSet<string>));
        var listLocal = il.DeclareLocal(typeof(List<object>));
        var listIdxLocal = il.DeclareLocal(typeof(int));

        // Define labels
        var notDoubleLabel = il.DefineLabel();
        var notStringLabel = il.DefineLabel();
        var doneIndentLabel = il.DefineLabel();
        var notListLabel = il.DefineLabel();
        var doneKeysLabel = il.DefineLabel();
        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        var notStringItem = il.DefineLabel();

        // Parse space argument to get indent value
        // if (space is double d)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // indent = (int)Math.Min(d, 10)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(double), typeof(double)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indentLocal);
        il.Emit(OpCodes.Br, doneIndentLabel);

        // else if (space is string s)
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // indent = Math.Min(s.Length, 10)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, indentLocal);
        il.Emit(OpCodes.Br, doneIndentLabel);

        // else indent = 0
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indentLocal);

        il.MarkLabel(doneIndentLabel);

        // Parse replacer argument to get allowedKeys
        // if (replacer is List<object?> list)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notListLabel);

        // allowedKeys = new HashSet<string>()
        il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        // Loop through list and add strings
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, listIdxLocal);

        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, listLoopEnd);

        // if (list[i] is string s) allowedKeys.Add(s)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringItem);

        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notStringItem);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, listIdxLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Br, doneKeysLabel);

        // else allowedKeys = null
        il.MarkLabel(notListLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        il.MarkLabel(doneKeysLabel);

        // Call StringifyWithKeys(value, allowedKeys, indent, depth=0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, indentLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, stringifyWithKeys);
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonStringifyWithKeysHelper(TypeBuilder typeBuilder)
    {
        // StringifyWithKeys(object value, HashSet<string> allowedKeys, int indent, int depth) -> string
        var method = typeBuilder.DefineMethod(
            "StringifyValueWithKeys",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(HashSet<string>), typeof(int), typeof(int)]
        );

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumberForFullStringify(il);

        // string
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array with indent
        il.MarkLabel(listLabel);
        EmitStringifyArrayWithIndent(il, method);

        // Dictionary<string, object> - stringify object with indent and allowedKeys
        il.MarkLabel(dictLabel);
        EmitStringifyObjectWithKeysAndIndent(il, method);

        return method;
    }

    private static void EmitFormatNumberForFullStringify(ILGenerator il)
    {
        var local = il.DeclareLocal(typeof(double));
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, local);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, typeof(long).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyArrayWithIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var arrLocal = il.DeclareLocal(typeof(List<object>));
        var iLocal = il.DeclareLocal(typeof(int));
        var newlineLocal = il.DeclareLocal(typeof(string));
        var closeLocal = il.DeclareLocal(typeof(string));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();
        var doneFormatting = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // if (indent > 0) compute newline/close strings
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        // newline = "\n" + new string(' ', indent * (depth + 1))
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, newlineLocal);

        // close = "\n" + new string(' ', indent * depth)
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(arr[i], allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyObjectWithKeysAndIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object>));
        var firstLocal = il.DeclareLocal(typeof(bool));
        var newlineLocal = il.DeclareLocal(typeof(string));
        var closeLocal = il.DeclareLocal(typeof(string));
        var keyStrLocal = il.DeclareLocal(typeof(string));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object>));
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // Compute newline/close if indent > 0
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, newlineLocal);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, typeof(string).GetConstructor([typeof(char), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // first = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // foreach (var kv in dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (allowedKeys != null && !allowedKeys.Contains(kv.Key)) continue;
        var skipKeyCheck = il.DefineLabel();
        var keyAllowed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Brfalse, skipKeyCheck);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Contains", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, keyAllowed);
        il.Emit(OpCodes.Br, loopStart); // skip this key

        il.MarkLabel(skipKeyCheck);
        il.MarkLabel(keyAllowed);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // first = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // keyStr = JsonSerializer.Serialize(kv.Key)
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Stloc, keyStrLocal);

        // sb.Append(keyStr);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(indent > 0 ? ": " : ":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_2);
        var colonNoSpace = il.DefineLabel();
        var colonDone = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, colonNoSpace);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Br, colonDone);
        il.MarkLabel(colonNoSpace);
        il.Emit(OpCodes.Ldstr, ":");
        il.MarkLabel(colonDone);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(kv.Value, allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, typeof(KeyValuePair<string, object>).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }
}
