using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Error-related runtime emission methods.
/// Creates Error objects using the SharpTSError classes.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitErrorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateError(typeBuilder, runtime);
        EmitErrorGetters(typeBuilder, runtime);
        EmitErrorSetters(typeBuilder, runtime);
        EmitErrorToString(typeBuilder, runtime);
        EmitAggregateErrorGetErrors(typeBuilder, runtime);
    }

    private void EmitCreateError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // CreateError(string errorTypeName, object[] args) -> object
        var method = typeBuilder.DefineMethod(
            "CreateError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.ObjectArray]
        );
        runtime.CreateError = method;

        var il = method.GetILGenerator();

        // Local for the List<object?>
        var listLocal = il.DeclareLocal(typeof(List<object?>));

        // Create new List<object?>()
        il.Emit(OpCodes.Newobj, typeof(List<object?>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, listLocal);

        // Copy elements from args array to list
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var indexLocal = il.DeclareLocal(typeof(int));

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);

        // if (index >= args.Length) goto loopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // list.Add(args[index])
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(List<object?>).GetMethod("Add")!);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call ErrorBuiltIns.CreateError(errorTypeName, list)
        il.Emit(OpCodes.Ldarg_0); // errorTypeName
        il.Emit(OpCodes.Ldloc, listLocal); // list

        var createErrorMethod = typeof(ErrorBuiltIns).GetMethod("CreateError",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(List<object?>)])!;
        il.Emit(OpCodes.Call, createErrorMethod);

        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorGetters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ErrorGetName
        runtime.ErrorGetName = EmitErrorPropertyGetter(typeBuilder, "ErrorGetName",
            typeof(SharpTSError).GetProperty("Name")!.GetGetMethod()!);

        // ErrorGetMessage
        runtime.ErrorGetMessage = EmitErrorPropertyGetter(typeBuilder, "ErrorGetMessage",
            typeof(SharpTSError).GetProperty("Message")!.GetGetMethod()!);

        // ErrorGetStack
        runtime.ErrorGetStack = EmitErrorPropertyGetter(typeBuilder, "ErrorGetStack",
            typeof(SharpTSError).GetProperty("Stack")!.GetGetMethod()!);
    }

    private MethodBuilder EmitErrorPropertyGetter(TypeBuilder typeBuilder, string methodName, MethodInfo propertyGetter)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Check if arg is SharpTSError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(SharpTSError));
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call property getter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTSError));
        il.Emit(OpCodes.Callvirt, propertyGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitErrorSetters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ErrorSetName
        runtime.ErrorSetName = EmitErrorPropertySetter(typeBuilder, "ErrorSetName",
            typeof(SharpTSError).GetProperty("Name")!.GetSetMethod()!);

        // ErrorSetMessage
        runtime.ErrorSetMessage = EmitErrorPropertySetter(typeBuilder, "ErrorSetMessage",
            typeof(SharpTSError).GetProperty("Message")!.GetSetMethod()!);

        // ErrorSetStack
        runtime.ErrorSetStack = EmitErrorPropertySetter(typeBuilder, "ErrorSetStack",
            typeof(SharpTSError).GetProperty("Stack")!.GetSetMethod()!);
    }

    private MethodBuilder EmitErrorPropertySetter(TypeBuilder typeBuilder, string methodName, MethodInfo propertySetter)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String]
        );

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        // Check if arg is SharpTSError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(SharpTSError));
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call property setter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTSError));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, propertySetter);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitErrorToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ErrorToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.ErrorToString = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Check if arg is SharpTSError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(SharpTSError));
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTSError));
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitAggregateErrorGetErrors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AggregateErrorGetErrors",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.AggregateErrorGetErrors = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Check if arg is SharpTSAggregateError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(SharpTSAggregateError));
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call Errors property getter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTSAggregateError));
        il.Emit(OpCodes.Callvirt, typeof(SharpTSAggregateError).GetProperty("Errors")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
