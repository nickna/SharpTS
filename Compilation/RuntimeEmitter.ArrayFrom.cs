using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits ArrayFrom: creates an array from an iterable with optional map function.
    /// Signature: List&lt;object&gt; ArrayFrom(object iterable, object mapFn, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitArrayFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.Object, runtime.TSSymbolType, _types.Type]
        );
        runtime.ArrayFrom = method;

        var il = method.GetILGenerator();

        // Locals
        var resultLocal = il.DeclareLocal(_types.ListOfObject);     // The result list from IterateToList or the mapped result
        var indexLocal = il.DeclareLocal(_types.Int32);             // Loop counter
        var mappedResultLocal = il.DeclareLocal(_types.ListOfObject); // Mapped result when mapFn is provided

        // Labels
        var noMapFnLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Call IterateToList(iterable, iteratorSymbol, runtimeType) to get the initial list
        il.Emit(OpCodes.Ldarg_0);  // iterable
        il.Emit(OpCodes.Ldarg_2);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_3);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (mapFn == null) return result;
        il.Emit(OpCodes.Ldarg_1);  // mapFn
        il.Emit(OpCodes.Brfalse, noMapFnLabel);

        // Create mapped result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, mappedResultLocal);

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: for (int i = 0; i < result.Count; i++)
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // Create args array: [result[i], (double)i]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);

        // args[0] = result[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // Store args array
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call mapFn with args via InvokeValue
        il.Emit(OpCodes.Ldarg_1);  // mapFn
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Add result to mappedResult
        var callResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, callResultLocal);
        il.Emit(OpCodes.Ldloc, mappedResultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        // Return mapped result
        il.Emit(OpCodes.Ldloc, mappedResultLocal);
        il.Emit(OpCodes.Br, returnLabel);

        // No map function - return the original result from IterateToList
        il.MarkLabel(noMapFnLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }
}
