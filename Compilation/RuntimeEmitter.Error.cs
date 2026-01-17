using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Error-related runtime emission methods.
/// Uses the emitted $Error class hierarchy for standalone assemblies.
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
        // Creates the appropriate error type based on the name
        var method = typeBuilder.DefineMethod(
            "CreateError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.ObjectArray]
        );
        runtime.CreateError = method;

        var il = method.GetILGenerator();

        // Get message from args[0] if provided
        var messageLocal = il.DeclareLocal(_types.String);
        var noArgsLabel = il.DefineLabel();
        var afterMessageLabel = il.DefineLabel();

        // if (args == null || args.Length == 0) message = null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noArgsLabel);

        // message = args[0]?.ToString()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        var argNotNull = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, argNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, afterMessageLabel);
        il.MarkLabel(argNotNull);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, afterMessageLabel);

        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(afterMessageLabel);
        il.Emit(OpCodes.Stloc, messageLocal);

        // Switch on errorTypeName to create the appropriate error type
        var typeErrorLabel = il.DefineLabel();
        var rangeErrorLabel = il.DefineLabel();
        var referenceErrorLabel = il.DefineLabel();
        var syntaxErrorLabel = il.DefineLabel();
        var uriErrorLabel = il.DefineLabel();
        var evalErrorLabel = il.DefineLabel();
        var aggregateErrorLabel = il.DefineLabel();
        var defaultErrorLabel = il.DefineLabel();

        // Check for "TypeError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "TypeError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, typeErrorLabel);

        // Check for "RangeError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "RangeError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, rangeErrorLabel);

        // Check for "ReferenceError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "ReferenceError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, referenceErrorLabel);

        // Check for "SyntaxError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "SyntaxError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, syntaxErrorLabel);

        // Check for "URIError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "URIError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, uriErrorLabel);

        // Check for "EvalError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "EvalError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, evalErrorLabel);

        // Check for "AggregateError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "AggregateError");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, aggregateErrorLabel);

        // Default: create base Error
        il.Emit(OpCodes.Br, defaultErrorLabel);

        // Create TypeError
        il.MarkLabel(typeErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create RangeError
        il.MarkLabel(rangeErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create ReferenceError
        il.MarkLabel(referenceErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSReferenceErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create SyntaxError
        il.MarkLabel(syntaxErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSSyntaxErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create URIError
        il.MarkLabel(uriErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSURIErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create EvalError
        il.MarkLabel(evalErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSEvalErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create AggregateError - args[0] = errors, args[1] = message
        // Note: AggregateError constructor takes (errors, message) - errors first!
        il.MarkLabel(aggregateErrorLabel);
        var aggregateErrorsLocal = il.DeclareLocal(_types.Object);
        var aggregateMessageLocal = il.DeclareLocal(_types.String);
        var noAggErrorsArgLabel = il.DefineLabel();
        var afterAggErrorsLabel = il.DefineLabel();
        var noAggMessageArgLabel = il.DefineLabel();
        var afterAggMessageLabel = il.DefineLabel();

        // Get errors from args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noAggErrorsArgLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noAggErrorsArgLabel);

        // errors = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Br, afterAggErrorsLabel);

        il.MarkLabel(noAggErrorsArgLabel);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(afterAggErrorsLabel);
        il.Emit(OpCodes.Stloc, aggregateErrorsLocal);

        // Get message from args[1] if available
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noAggMessageArgLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, noAggMessageArgLabel);

        // message = args[1]?.ToString()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        var aggArgNotNull = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, aggArgNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, afterAggMessageLabel);
        il.MarkLabel(aggArgNotNull);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, afterAggMessageLabel);

        il.MarkLabel(noAggMessageArgLabel);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(afterAggMessageLabel);
        il.Emit(OpCodes.Stloc, aggregateMessageLocal);

        // Pass (errors, message) to constructor
        il.Emit(OpCodes.Ldloc, aggregateErrorsLocal);
        il.Emit(OpCodes.Ldloc, aggregateMessageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSAggregateErrorCtor);
        il.Emit(OpCodes.Ret);

        // Create base Error
        il.MarkLabel(defaultErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorGetters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ErrorGetName
        runtime.ErrorGetName = EmitErrorPropertyGetter(typeBuilder, runtime, "ErrorGetName",
            runtime.TSErrorType, runtime.TSErrorNameGetter);

        // ErrorGetMessage
        runtime.ErrorGetMessage = EmitErrorPropertyGetter(typeBuilder, runtime, "ErrorGetMessage",
            runtime.TSErrorType, runtime.TSErrorMessageGetter);

        // ErrorGetStack
        runtime.ErrorGetStack = EmitErrorPropertyGetter(typeBuilder, runtime, "ErrorGetStack",
            runtime.TSErrorType, runtime.TSErrorStackGetter);
    }

    private MethodBuilder EmitErrorPropertyGetter(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        Type errorType,
        MethodBuilder propertyGetter)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errorType);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call property getter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errorType);
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
        runtime.ErrorSetName = EmitErrorPropertySetter(typeBuilder, runtime, "ErrorSetName",
            runtime.TSErrorType, runtime.TSErrorNameSetter);

        // ErrorSetMessage
        runtime.ErrorSetMessage = EmitErrorPropertySetter(typeBuilder, runtime, "ErrorSetMessage",
            runtime.TSErrorType, runtime.TSErrorMessageSetter);

        // ErrorSetStack
        runtime.ErrorSetStack = EmitErrorPropertySetter(typeBuilder, runtime, "ErrorSetStack",
            runtime.TSErrorType, runtime.TSErrorStackSetter);
    }

    private MethodBuilder EmitErrorPropertySetter(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        Type errorType,
        MethodBuilder propertySetter)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String]
        );

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errorType);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call property setter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errorType);
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

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
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

        // Check if arg is $AggregateError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSAggregateErrorType);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call Errors property getter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSAggregateErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSAggregateErrorErrorsGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
