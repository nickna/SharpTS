using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitJsonParseWithReviver(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the ApplyReviver helper
        var applyReviver = EmitApplyReviverHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonParseWithReviver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
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

    private MethodBuilder EmitApplyReviverHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ApplyReviver(object value, object key, object reviver) -> object
        var method = typeBuilder.DefineMethod(
            "ApplyReviver",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Locals - keep nested enumerator types as typeof() to avoid BadImageFormatException
        var newDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var newListLocal = il.DeclareLocal(_types.ListOfObject);
        var enumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var listEnumLocal = il.DeclareLocal(typeof(List<object>.Enumerator));
        var kvpLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var elemLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.Object);
        var valueLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

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
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // It's a dictionary - process it
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, newDictLocal);

        // Get enumerator - keep typeof() for nested enumerator types
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
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
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // If result is not null, add to new dict
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brfalse, dictLoopStart);

        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloc, newDictLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, invokeReviverLabel);

        // Check if value is List<object>
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        // It's a list - process it
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, newListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Get list enumerator - keep typeof() for nested enumerator types
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
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
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add result to new list
        il.Emit(OpCodes.Ldloc, newListLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));

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
        il.Emit(OpCodes.Newarr, _types.Object);
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
}

