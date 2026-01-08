using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitGetArrayMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetArrayMethod(object arr, string methodName) -> TSFunction or null
        // Maps TypeScript array method names to .NET List methods
        var method = typeBuilder.DefineMethod(
            "GetArrayMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetArrayMethod = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();

        // Check if obj is List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notArrayLabel);

        // Map TypeScript method name to .NET method name
        // push -> Add, pop -> RemoveAt(Count-1), etc.
        var pushLabel = il.DefineLabel();
        var popLabel = il.DefineLabel();
        var shiftLabel = il.DefineLabel();

        // Check for "push"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "push");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, pushLabel);

        // Check for "pop"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pop");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, popLabel);

        // Unknown array method - return null
        il.Emit(OpCodes.Br, nullLabel);

        // Handle push - wrap List.Add as TSFunction
        il.MarkLabel(pushLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
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

    private static void EmitInvokeValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]
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
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Dup);
        var noInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noInvokeLabel);

        // Has Invoke - call it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noInvokeLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitInvokeMethodValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeMethodValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.ObjectArray]  // receiver, function, args
        );
        runtime.InvokeMethodValue = method;

        var il = method.GetILGenerator();
        // Check if value is $TSFunction and call InvokeWithThis
        // arg0 = receiver, arg1 = function, arg2 = args
        var nullLabel = il.DefineLabel();
        var notTSFunctionLabel = il.DefineLabel();

        // if (function == null) return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (function is $TSFunction tsFunc)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // return tsFunc.InvokeWithThis(receiver, args)
        il.Emit(OpCodes.Ldarg_0);  // receiver
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        // Not a TSFunction - try InvokeValue fallback
        il.MarkLabel(notTSFunctionLabel);
        il.Emit(OpCodes.Pop);  // Pop the null from isinst

        // Fall back to InvokeValue(function, args)
        il.Emit(OpCodes.Ldarg_1);  // function
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetSuperMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetSuperMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetSuperMethod = method;

        var il = method.GetILGenerator();
        var methodInfoLocal = il.DeclareLocal(_types.MethodInfo);
        var baseTypeLocal = il.DeclareLocal(_types.Type);
        var nullLabel = il.DefineLabel();

        // Check if instance is null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Get base type and store it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, baseTypeLocal);

        // Check if baseType is null
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Get method from base type
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
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
}
