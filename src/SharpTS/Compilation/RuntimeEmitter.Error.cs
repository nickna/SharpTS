using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Error-related runtime emission methods.
/// Uses the emitted $Error class hierarchy for standalone assemblies.
/// </summary>
public partial class RuntimeEmitter
{
    private readonly record struct ErrorMethodsInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        MethodBuilder GetProperty,
        MethodInfo IHasFieldsGetProperty,
        MethodInfo IHasFieldsHasProperty,
        Type IHasFieldsInterface,
        ProxyHasInputs ProxyHas,
        EmittedStringCoercionRuntime StringCoercion,
        FieldInfo UndefinedInstance,
        Type UndefinedType
    );

    private readonly record struct CreateErrorInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        MethodBuilder GetProperty,
        MethodInfo IHasFieldsGetProperty,
        MethodInfo IHasFieldsHasProperty,
        Type IHasFieldsInterface,
        ProxyHasInputs ProxyHas,
        EmittedStringCoercionRuntime StringCoercion,
        Type UndefinedType
    );

    private readonly record struct ApplyErrorOptionsInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        MethodBuilder GetProperty,
        MethodInfo IHasFieldsGetProperty,
        MethodInfo IHasFieldsHasProperty,
        Type IHasFieldsInterface,
        ProxyHasInputs ProxyHas
    );

    private void EmitErrorIsError(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        var method = typeBuilder.DefineMethod(
            "ErrorIsError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        errors.IsError = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errors.Type);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorMethods(TypeBuilder typeBuilder, EmittedErrorRuntime errors, ErrorMethodsInputs inputs)
    {
        EmitErrorGetters(typeBuilder, errors);
        EmitErrorSetters(typeBuilder, errors);
        EmitErrorGetCause(typeBuilder, errors, inputs.UndefinedInstance);
        EmitErrorSetCause(typeBuilder, errors);
        EmitAggregateErrorGetErrors(typeBuilder, errors);
        EmitErrorDefineMessageProperty(typeBuilder, errors, inputs.DescriptorStorage);
        // CreateError must come last - it references ErrorSetCause and other helpers
        EmitCreateError(
            typeBuilder,
            errors,
            new CreateErrorInputs(
                inputs.DescriptorStorage,
                inputs.GetProperty,
                inputs.IHasFieldsGetProperty,
                inputs.IHasFieldsHasProperty,
                inputs.IHasFieldsInterface,
                inputs.ProxyHas,
                inputs.StringCoercion,
                inputs.UndefinedType
            )
        );
        EmitCreateErrorFromTypeOrNull(errors);
    }

    /// <summary>
    /// Adapts emitted native-error Type tokens to the JavaScript-facing error
    /// factory. Dynamic <c>new</c> and Reflect.construct otherwise select CLR
    /// constructors directly, losing optional arguments and own descriptors.
    /// Returns null for non-error types so the caller can keep its normal path.
    /// </summary>
    private void EmitCreateErrorFromTypeOrNull(EmittedErrorRuntime errors)
    {
        var method = errors.CreateErrorFromTypeOrNull;

        var il = method.GetILGenerator();
        var getTypeFromHandle = _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);
        var typeEquality = _types.GetMethod(
            _types.Type, "op_Equality", _types.Type, _types.Type);

        void EmitCase(Type errorType, string jsName)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, errorType);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Call, typeEquality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, errors.CreateError);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        EmitCase(errors.Type, "Error");
        EmitCase(errors.TypeErrorType, "TypeError");
        EmitCase(errors.RangeErrorType, "RangeError");
        EmitCase(errors.ReferenceErrorType, "ReferenceError");
        EmitCase(errors.SyntaxErrorType, "SyntaxError");
        EmitCase(errors.URIErrorType, "URIError");
        EmitCase(errors.EvalErrorType, "EvalError");
        EmitCase(errors.AggregateErrorType, "AggregateError");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        errors.MarkCreateFromTypeBodyEmitted();
    }

    /// <summary>
    /// Installs the spec-created own <c>message</c> data property after an
    /// emitted class constructor chains directly to a native Error base ctor.
    /// The ordinary Error factory performs the same operation inline, but a
    /// CLR base-constructor call otherwise bypasses that factory.
    /// </summary>
    private void EmitErrorDefineMessageProperty(
        TypeBuilder typeBuilder,
        EmittedErrorRuntime errors,
        EmittedDescriptorStorageRuntime descriptorStorage
    )
    {
        var method = typeBuilder.DefineMethod(
            "ErrorDefineMessageProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String]);
        errors.DefineMessageProperty = method;

        var il = method.GetILGenerator();
        var descriptorLocal = il.DeclareLocal(descriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, descriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, descriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateError(TypeBuilder typeBuilder, EmittedErrorRuntime errors, CreateErrorInputs inputs)
    {
        // CreateError(string errorTypeName, object[] args) -> object
        // Creates the appropriate error type based on the name
        var method = typeBuilder.DefineMethod(
            "CreateError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.ObjectArray]
        );
        errors.CreateError = method;

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
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, argUndefinedLabel);
        // Any non-undefined value, including JS null, is converted. ToJsString
        // maps CLR null to "null" and throws TypeError for Symbol.
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasMessageLocal);
        il.Emit(OpCodes.Ldloc, arg0Local);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
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
        il.Emit(OpCodes.Newobj, errors.TypeErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create RangeError
        il.MarkLabel(rangeErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.RangeErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create ReferenceError
        il.MarkLabel(referenceErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.ReferenceErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create SyntaxError
        il.MarkLabel(syntaxErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.SyntaxErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create URIError
        il.MarkLabel(uriErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.URIErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Create EvalError
        il.MarkLabel(evalErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.EvalErrorConstructor);
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
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, noAggMessageArgLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasAggregateMessageLocal);
        il.Emit(OpCodes.Ldloc, aggregateMessageArgLocal);
        il.Emit(OpCodes.Call, inputs.StringCoercion.ToJsString);
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
        il.Emit(OpCodes.Newobj, errors.AggregateErrorConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyAggOptionsLabel);

        // Create base Error
        il.MarkLabel(defaultErrorLabel);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Newobj, errors.MessageConstructor);
        il.Emit(OpCodes.Stloc, errorLocal);
        il.Emit(OpCodes.Br, applyOptionsLabel);

        // Apply options for non-AggregateError types: options is args[1]
        il.MarkLabel(applyOptionsLabel);
        EmitDefineErrorDataPropertyIfPresent(
            il,
            inputs.DescriptorStorage,
            errorLocal,
            "message",
            messageLocal,
            hasMessageLocal
        );
        EmitApplyErrorOptions(
            il,
            errors,
            new ApplyErrorOptionsInputs(
                inputs.DescriptorStorage,
                inputs.GetProperty,
                inputs.IHasFieldsGetProperty,
                inputs.IHasFieldsHasProperty,
                inputs.IHasFieldsInterface,
                inputs.ProxyHas
            ),
            errorLocal,
            1
        );
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ret);

        // Apply options for AggregateError: options is args[2]
        il.MarkLabel(applyAggOptionsLabel);
        EmitDefineErrorDataPropertyIfPresent(
            il,
            inputs.DescriptorStorage,
            errorLocal,
            "message",
            aggregateMessageLocal,
            hasAggregateMessageLocal
        );
        EmitApplyErrorOptions(
            il,
            errors,
            new ApplyErrorOptionsInputs(
                inputs.DescriptorStorage,
                inputs.GetProperty,
                inputs.IHasFieldsGetProperty,
                inputs.IHasFieldsHasProperty,
                inputs.IHasFieldsInterface,
                inputs.ProxyHas
            ),
            errorLocal,
            2
        );
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
        EmittedDescriptorStorageRuntime descriptorStorage,
        LocalBuilder errorLocal,
        string propertyName,
        LocalBuilder valueLocal,
        LocalBuilder presentLocal
    )
    {
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, presentLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        var descriptorLocal = il.DeclareLocal(descriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, descriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, descriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, descriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits IL to extract { cause } from an options argument and set it on the error.
    /// Handles both Dictionary&lt;string, object?&gt; (compiled object literals) and $IHasFields.
    /// </summary>
    private void EmitApplyErrorOptions(
        ILGenerator il,
        EmittedErrorRuntime errors,
        ApplyErrorOptionsInputs inputs,
        LocalBuilder errorLocal,
        int optionsArgIndex
    )
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

        // InstallErrorCause performs HasProperty before Get.  A Proxy's `has`
        // trap is observable and may complete abruptly, so dispatch it before
        // the ordinary dictionary/$Object storage paths.
        var notProxyLabel = il.DefineLabel();
        EmitProxyHasResult(
            il,
            () => il.Emit(OpCodes.Ldloc, optionsLocal),
            () => il.Emit(OpCodes.Ldstr, "cause"),
            notProxyLabel,
            inputs.ProxyHas);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, causeLocal);
        il.Emit(OpCodes.Br, setCauseLabel);

        il.MarkLabel(notProxyLabel);

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
        il.Emit(OpCodes.Isinst, inputs.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Check HasProperty("cause")
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, inputs.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Callvirt, inputs.IHasFieldsHasProperty);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Get cause value: options.GetProperty("cause")
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, inputs.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Callvirt, inputs.IHasFieldsGetProperty);
        il.Emit(OpCodes.Stloc, causeLocal);

        // Set cause on the error
        il.MarkLabel(setCauseLabel);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldloc, causeLocal);
        il.Emit(OpCodes.Call, errors.SetCause);
        // Also install a non-enumerable PDS data descriptor so
        // Object.getOwnPropertyDescriptor(err, "cause") surfaces the slot
        // (ECMA-262 §20.5.8.1 step 1.b → CreateNonEnumerableDataPropertyOrThrow:
        // writable:true, enumerable:false, configurable:true). Unlocks
        // built-ins/Error/cause_property + verifyProperty patterns.
        var causeDescLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, inputs.DescriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, causeDescLocal);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldloc, causeLocal);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, inputs.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, errorLocal);
        il.Emit(OpCodes.Ldstr, "cause");
        il.Emit(OpCodes.Ldloc, causeDescLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    private void EmitErrorGetters(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        // ErrorGetName
        errors.GetName = EmitErrorPropertyGetter(typeBuilder, "ErrorGetName", errors.Type, errors.NameGetter);

        // ErrorGetMessage
        errors.GetMessage = EmitErrorPropertyGetter(typeBuilder, "ErrorGetMessage", errors.Type, errors.MessageGetter);

        // ErrorGetStack
        errors.GetStack = EmitErrorPropertyGetter(typeBuilder, "ErrorGetStack", errors.Type, errors.StackGetter);
    }

    private MethodBuilder EmitErrorPropertyGetter(TypeBuilder typeBuilder, string methodName, Type errorType, MethodBuilder propertyGetter)
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

    private void EmitErrorSetters(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        // ErrorSetName
        errors.SetName = EmitErrorPropertySetter(typeBuilder, "ErrorSetName", errors.Type, errors.NameSetter);

        // ErrorSetMessage
        errors.SetMessage = EmitErrorPropertySetter(typeBuilder, "ErrorSetMessage", errors.Type, errors.MessageSetter);

        // ErrorSetStack
        errors.SetStack = EmitErrorPropertySetter(typeBuilder, "ErrorSetStack", errors.Type, errors.StackSetter);
    }

    private MethodBuilder EmitErrorPropertySetter(TypeBuilder typeBuilder, string methodName, Type errorType, MethodBuilder propertySetter)
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

    private void EmitErrorGetCause(TypeBuilder typeBuilder, EmittedErrorRuntime errors, FieldInfo undefinedInstance)
    {
        // ErrorGetCause(object errorObj) -> object?
        // Returns cause value if HasCause is true, otherwise returns $Undefined.Instance
        var method = typeBuilder.DefineMethod(
            "ErrorGetCause",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        errors.GetCause = method;

        var il = method.GetILGenerator();
        var undefinedLabel = il.DefineLabel();
        var noCauseLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errors.Type);
        il.Emit(OpCodes.Brfalse, undefinedLabel);

        // Check HasCause
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errors.Type);
        il.Emit(OpCodes.Callvirt, errors.HasCauseGetter);
        il.Emit(OpCodes.Brfalse, noCauseLabel);

        // HasCause is true - return Cause value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errors.Type);
        il.Emit(OpCodes.Callvirt, errors.CauseGetter);
        il.Emit(OpCodes.Ret);

        // HasCause is false or not an error - return undefined
        il.MarkLabel(noCauseLabel);
        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldsfld, undefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitErrorSetCause(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        // ErrorSetCause(object errorObj, object? value) -> void
        var method = typeBuilder.DefineMethod(
            "ErrorSetCause",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        errors.SetCause = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        // Check if arg is $Error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errors.Type);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call Cause property setter (which also sets HasCause)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errors.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, errors.CauseSetter);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAggregateErrorGetErrors(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        var method = typeBuilder.DefineMethod(
            "AggregateErrorGetErrors",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        errors.AggregateErrorGetErrors = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // Check if arg is $AggregateError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, errors.AggregateErrorType);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Call Errors property getter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, errors.AggregateErrorType);
        il.Emit(OpCodes.Callvirt, errors.AggregateErrorErrorsGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
