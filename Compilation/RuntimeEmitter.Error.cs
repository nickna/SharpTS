using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Error-related runtime emission methods.
/// Uses the emitted $Error class hierarchy for standalone assemblies.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitErrorIsError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ErrorIsError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        runtime.ErrorIsError = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitErrorGetters(typeBuilder, runtime);
        EmitErrorSetters(typeBuilder, runtime);
        EmitErrorGetCause(typeBuilder, runtime);
        EmitErrorSetCause(typeBuilder, runtime);
        EmitErrorToString(typeBuilder, runtime);
        EmitAggregateErrorGetErrors(typeBuilder, runtime);
        // CreateError must come last - it references ErrorSetCause and other helpers
        EmitCreateError(typeBuilder, runtime);
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
        var hasMessageLocal = il.DeclareLocal(_types.Boolean);
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

        // message = args[0] === undefined ? absent : ToString(args[0])
        // ECMA-262 §20.5.1.1 Error step 3: only ToString when message is not
        // undefined. ToJsString throws TypeError on Symbol per §7.1.17 step 2
        // — required by Error/error-message-tostring-symbol.js +
        // error-message-tostring-toprimitive.js. Store args[0] in a local so
        // the dup/branch dance doesn't leak stack into afterMessageLabel.
        var arg0Local = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, arg0Local);

        var argUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, arg0Local);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, argUndefinedLabel);
        // Any non-undefined value, including JS null, is converted. ToJsString
        // maps CLR null to "null" and throws TypeError for Symbol.
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasMessageLocal);
        il.Emit(OpCodes.Ldloc, arg0Local);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Br, afterMessageLabel);
        il.MarkLabel(argUndefinedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasMessageLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, afterMessageLabel);

        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasMessageLocal);
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
        var applyOptionsLabel = il.DefineLabel();
        var applyAggOptionsLabel = il.DefineLabel();

        // Local to hold the created error object and options index
        var errorLocal = il.DeclareLocal(_types.Object);

        // Check for "TypeError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "TypeError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, typeErrorLabel);

        // Check for "RangeError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "RangeError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, rangeErrorLabel);

        // Check for "ReferenceError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "ReferenceError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, referenceErrorLabel);

        // Check for "SyntaxError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "SyntaxError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, syntaxErrorLabel);

        // Check for "URIError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "URIError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, uriErrorLabel);

        // Check for "EvalError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "EvalError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, evalErrorLabel);

        // Check for "AggregateError"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "AggregateError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, aggregateErrorLabel);

        // Default: create base Error
        il.Emit(OpCodes.Br, defaultErrorLabel);

        // Create TypeError
        il.MarkLabel(typeErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create RangeError
        il.MarkLabel(rangeErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create ReferenceError
        il.MarkLabel(referenceErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSReferenceErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create SyntaxError
        il.MarkLabel(syntaxErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSSyntaxErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create URIError
        il.MarkLabel(uriErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSURIErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create EvalError
        il.MarkLabel(evalErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSEvalErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create AggregateError - args[0] = errors, args[1] = message
        // Note: AggregateError constructor takes (errors, message) - errors first!
        il.MarkLabel(aggregateErrorLabel);
        var aggregateErrorsLocal = il.DeclareLocal(_types.Object);
        var aggregateMessageLocal = il.DeclareLocal(_types.String);
        var hasAggregateMessageLocal = il.DeclareLocal(_types.Boolean);
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

        // message = args[1] === undefined ? absent : ToString(args[1])
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        var aggregateMessageArgLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, aggregateMessageArgLocal);
        il.Emit(OpCodes.Ldloc, aggregateMessageArgLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noAggMessageArgLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasAggregateMessageLocal);
        il.Emit(OpCodes.Ldloc, aggregateMessageArgLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Br, afterAggMessageLabel);

        il.MarkLabel(noAggMessageArgLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasAggregateMessageLocal);
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(afterAggMessageLabel);
        il.Emit(OpCodes.Stloc, aggregateMessageLocal);

        // Pass (errors, message) to constructor
        il.Emit(OpCodes.Ldloc, aggregateErrorsLocal);
        il.Emit(OpCodes.Ldloc, aggregateMessageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSAggregateErrorCtor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyAggOptionsLabel);

        // Create base Error
        il.MarkLabel(defaultErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Apply options for non-AggregateError types: options is args[1]
        il.MarkLabel(applyOptionsLabel);
        EmitDefineErrorDataPropertyIfPresent(il, runtime, errorLocal, "message", messageLocal, hasMessageLocal);
        EmitApplyErrorOptions(il, runtime, errorLocal, 1);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ret);

        // Apply options for AggregateError: options is args[2]
        il.MarkLabel(applyAggOptionsLabel);
        EmitDefineErrorDataPropertyIfPresent(il, runtime, errorLocal, "message", aggregateMessageLocal, hasAggregateMessageLocal);
        EmitApplyErrorOptions(il, runtime, errorLocal, 2);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Installs one of the Error constructor's spec-created own data properties.
    /// Both message and cause use writable:true, enumerable:false,
    /// configurable:true; the presence flag distinguishes an omitted/undefined
    /// message from values such as null that stringify to a real message.
    /// </summary>
    private static void EmitDefineErrorDataPropertyIfPresent(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder errorLocal,
        string propertyName,
        LocalBuilder valueLocal,
        LocalBuilder presentLocal)
    {
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, presentLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits IL to extract { cause } from an options argument and set it on the error.
    /// Handles both Dictionary&lt;string, object?&gt; (compiled object literals) and $IHasFields.
    /// </summary>
    private void EmitApplyErrorOptions(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder errorLocal,
        int optionsArgIndex)
    {
        var skipLabel = il.DefineLabel();
        var tryHasFieldsLabel = il.DefineLabel();
        var setCauseLabel = il.DefineLabel();

        // Check if args has enough elements for the options argument
        il.Emit(OpCodes.Ldarg_1); // args
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, optionsArgIndex + 1); // need at least optionsArgIndex+1 elements
        il.Emit(OpCodes.Blt, skipLabel);

        // Get the options object: args[optionsArgIndex]
        var optionsLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, optionsArgIndex);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, optionsLocal);

        // Check if options is not null
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        var causeLocal = il.DeclareLocal(_types.Object);

        // Try Dictionary<string, object?> first (compiled object literals)
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, tryHasFieldsLabel);

        // Dictionary path: dict.TryGetValue("cause", out value)
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Ldloca, causeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")!);
        il.Emit(OpCodes.Brtrue, setCauseLabel);
        il.Emit(OpCodes.Br, skipLabel);

        // Try IHasFields (e.g. $Object instances)
        il.MarkLabel(tryHasFieldsLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Check HasProperty("cause")
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsHasProperty);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Get cause value: options.GetProperty("cause")
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Stloc, causeLocal);

        // Set cause on the error
        il.MarkLabel(setCauseLabel);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldloc, causeLocal);
        il.Emit(OpCodes.Call, runtime.ErrorSetCause);
        // Also install a non-enumerable PDS data descriptor so
        // Object.getOwnPropertyDescriptor(err, "cause") surfaces the slot
        // (ECMA-262 §20.5.8.1 step 1.b → CreateNonEnumerableDataPropertyOrThrow:
        // writable:true, enumerable:false, configurable:true). Unlocks
        // built-ins/Error/cause_property + verifyProperty patterns.
        var causeDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, causeDescLocal);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldloc, causeLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
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

    private void EmitErrorGetCause(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ErrorGetCause(object errorObj) -> object?
        // Returns cause value if HasCause is true, otherwise returns $Undefined.Instance
        var method = typeBuilder.DefineMethod(
            "ErrorGetCause",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ErrorGetCause = method;

        var il = method.GetILGenerator();
        var undefinedLabel = il.DefineLabel();
        var noCauseLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, undefinedLabel);

        // Check HasCause
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorHasCauseGetter);
        il.Emit(OpCodes.Brfalse, noCauseLabel);

        // HasCause is true - return Cause value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCauseGetter);
        il.Emit(OpCodes.Ret);

        // HasCause is false or not an error - return undefined
        il.MarkLabel(noCauseLabel);
        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorSetCause(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ErrorSetCause(object errorObj, object? value) -> void
        var method = typeBuilder.DefineMethod(
            "ErrorSetCause",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.ErrorSetCause = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call Cause property setter (which also sets HasCause)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCauseSetter);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
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
