using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits util module types and helper methods for standalone compiled assemblies.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits $TextEncoder type for standalone util support.
    /// </summary>
    internal void EmitTSTextEncoderClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TextEncoder",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSTextEncoderType = typeBuilder;

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);
        runtime.TSTextEncoderCtor = ctor;

        // Property: encoding (always "utf-8")
        var encodingGetter = typeBuilder.DefineMethod(
            "get_Encoding",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var encodingIL = encodingGetter.GetILGenerator();
        encodingIL.Emit(OpCodes.Ldstr, "utf-8");
        encodingIL.Emit(OpCodes.Ret);
        _ = encodingGetter;

        var encodingProp = typeBuilder.DefineProperty(
            "encoding",
            PropertyAttributes.None,
            _types.String,
            null
        );
        encodingProp.SetGetMethod(encodingGetter);

        // Method: encode(input: string) -> $Buffer
        var encodeMethod = typeBuilder.DefineMethod(
            "Encode",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TSBufferType,
            [_types.String]
        );
        _ = encodeMethod;

        var encodeIL = encodeMethod.GetILGenerator();
        var inputLocal = encodeIL.DeclareLocal(_types.String);
        var bytesLocal = encodeIL.DeclareLocal(typeof(byte[]));

        // input = arg1 ?? ""
        encodeIL.Emit(OpCodes.Ldarg_1);
        encodeIL.Emit(OpCodes.Dup);
        var notNullLabel = encodeIL.DefineLabel();
        encodeIL.Emit(OpCodes.Brtrue, notNullLabel);
        encodeIL.Emit(OpCodes.Pop);
        encodeIL.Emit(OpCodes.Ldstr, "");
        encodeIL.MarkLabel(notNullLabel);
        encodeIL.Emit(OpCodes.Stloc, inputLocal);

        // bytes = Encoding.UTF8.GetBytes(input)
        encodeIL.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        encodeIL.Emit(OpCodes.Ldloc, inputLocal);
        encodeIL.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", [typeof(string)])!);
        encodeIL.Emit(OpCodes.Stloc, bytesLocal);

        // return new $Buffer(bytes)
        encodeIL.Emit(OpCodes.Ldloc, bytesLocal);
        encodeIL.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        encodeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[object TextEncoder]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $TextDecoder type for standalone util support.
    /// </summary>
    internal void EmitTSTextDecoderClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TextDecoder",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSTextDecoderType = typeBuilder;

        // Fields
        var encodingField = typeBuilder.DefineField("_encoding", typeof(Encoding), FieldAttributes.Private);
        var encodingNameField = typeBuilder.DefineField("_encodingName", _types.String, FieldAttributes.Private);
        var fatalField = typeBuilder.DefineField("_fatal", _types.Boolean, FieldAttributes.Private);
        var ignoreBOMField = typeBuilder.DefineField("_ignoreBOM", _types.Boolean, FieldAttributes.Private);

        // Constructor(encoding, fatal, ignoreBOM)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.Boolean, _types.Boolean]
        );
        runtime.TSTextDecoderCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // Store fatal and ignoreBOM
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, fatalField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_3);
        ctorIL.Emit(OpCodes.Stfld, ignoreBOMField);

        // Normalize and store encoding name
        // For simplicity, just store the encoding as-is (proper normalization would be complex in IL)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Dup);
        var hasEncLabel = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Brtrue, hasEncLabel);
        ctorIL.Emit(OpCodes.Pop);
        ctorIL.Emit(OpCodes.Ldstr, "utf-8");
        ctorIL.MarkLabel(hasEncLabel);
        ctorIL.Emit(OpCodes.Stfld, encodingNameField);

        // Get encoding object - use UTF8 for now (simplification)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        ctorIL.Emit(OpCodes.Stfld, encodingField);

        ctorIL.Emit(OpCodes.Ret);

        // Property: encoding
        var encodingGetter = typeBuilder.DefineMethod(
            "get_Encoding",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var encodingGetterIL = encodingGetter.GetILGenerator();
        encodingGetterIL.Emit(OpCodes.Ldarg_0);
        encodingGetterIL.Emit(OpCodes.Ldfld, encodingNameField);
        encodingGetterIL.Emit(OpCodes.Ret);
        _ = encodingGetter;

        var encodingProp = typeBuilder.DefineProperty(
            "Encoding",
            PropertyAttributes.None,
            _types.String,
            null
        );
        encodingProp.SetGetMethod(encodingGetter);

        // Property: fatal
        var fatalGetter = typeBuilder.DefineMethod(
            "get_Fatal",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        var fatalGetterIL = fatalGetter.GetILGenerator();
        fatalGetterIL.Emit(OpCodes.Ldarg_0);
        fatalGetterIL.Emit(OpCodes.Ldfld, fatalField);
        fatalGetterIL.Emit(OpCodes.Ret);
        _ = fatalGetter;

        // Property: ignoreBOM
        var ignoreBOMGetter = typeBuilder.DefineMethod(
            "get_IgnoreBOM",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        var ignoreBOMGetterIL = ignoreBOMGetter.GetILGenerator();
        ignoreBOMGetterIL.Emit(OpCodes.Ldarg_0);
        ignoreBOMGetterIL.Emit(OpCodes.Ldfld, ignoreBOMField);
        ignoreBOMGetterIL.Emit(OpCodes.Ret);
        _ = ignoreBOMGetter;

        // Method: Decode(object input) -> string
        // Accepts $Buffer, byte[], or null
        var decodeMethod = typeBuilder.DefineMethod(
            "Decode",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.String,
            [_types.Object]
        );
        runtime.TSTextDecoderDecode = decodeMethod;

        var decodeIL = decodeMethod.GetILGenerator();
        var returnEmptyLabel = decodeIL.DefineLabel();
        var isBufferLabel = decodeIL.DefineLabel();
        var isByteArrayLabel = decodeIL.DefineLabel();
        var decodeLabel = decodeIL.DefineLabel();
        var bytesLocal = decodeIL.DeclareLocal(typeof(byte[]));

        // if (input == null) return ""
        decodeIL.Emit(OpCodes.Ldarg_1);
        decodeIL.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // Check if input is $Buffer
        decodeIL.Emit(OpCodes.Ldarg_1);
        decodeIL.Emit(OpCodes.Isinst, runtime.TSBufferType);
        decodeIL.Emit(OpCodes.Brtrue, isBufferLabel);

        // Check if input is byte[]
        decodeIL.Emit(OpCodes.Ldarg_1);
        decodeIL.Emit(OpCodes.Isinst, typeof(byte[]));
        decodeIL.Emit(OpCodes.Brtrue, isByteArrayLabel);

        // Neither - return empty string
        decodeIL.Emit(OpCodes.Br, returnEmptyLabel);

        // isBuffer: bytes = (($Buffer)input).Data
        decodeIL.MarkLabel(isBufferLabel);
        decodeIL.Emit(OpCodes.Ldarg_1);
        decodeIL.Emit(OpCodes.Castclass, runtime.TSBufferType);
        decodeIL.Emit(OpCodes.Call, runtime.TSBufferGetData);
        decodeIL.Emit(OpCodes.Stloc, bytesLocal);
        decodeIL.Emit(OpCodes.Br, decodeLabel);

        // isByteArray: bytes = (byte[])input
        decodeIL.MarkLabel(isByteArrayLabel);
        decodeIL.Emit(OpCodes.Ldarg_1);
        decodeIL.Emit(OpCodes.Castclass, typeof(byte[]));
        decodeIL.Emit(OpCodes.Stloc, bytesLocal);
        decodeIL.Emit(OpCodes.Br, decodeLabel);

        decodeIL.MarkLabel(returnEmptyLabel);
        decodeIL.Emit(OpCodes.Ldstr, "");
        decodeIL.Emit(OpCodes.Ret);

        // decode: return _encoding.GetString(bytes)
        decodeIL.MarkLabel(decodeLabel);
        decodeIL.Emit(OpCodes.Ldloc, bytesLocal);
        decodeIL.Emit(OpCodes.Brfalse, returnEmptyLabel); // bytes may be null if Data is null
        decodeIL.Emit(OpCodes.Ldloc, bytesLocal);
        decodeIL.Emit(OpCodes.Ldlen);
        decodeIL.Emit(OpCodes.Brfalse, returnEmptyLabel); // empty array
        decodeIL.Emit(OpCodes.Ldarg_0);
        decodeIL.Emit(OpCodes.Ldfld, encodingField);
        decodeIL.Emit(OpCodes.Ldloc, bytesLocal);
        decodeIL.Emit(OpCodes.Callvirt, _types.EncodingGetStringFromBytes);
        decodeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[object TextDecoder]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $DeprecatedFunction type for util.deprecate support.
    /// </summary>
    internal void EmitTSDeprecatedFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$DeprecatedFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSDeprecatedFunctionType = typeBuilder;

        // Fields
        var wrappedField = typeBuilder.DefineField("_wrapped", _types.Object, FieldAttributes.Private);
        var messageField = typeBuilder.DefineField("_message", _types.String, FieldAttributes.Private);
        var warnedField = typeBuilder.DefineField("_warned", _types.Boolean, FieldAttributes.Private);

        // Constructor(wrapped, message)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.String]
        );
        runtime.TSDeprecatedFunctionCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, wrappedField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, messageField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, warnedField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: Invoke(params object[] args) -> object
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSDeprecatedFunctionInvoke = invokeMethod;

        var invokeIL = invokeMethod.GetILGenerator();
        var alreadyWarnedLabel = invokeIL.DefineLabel();
        var invokeWrappedLabel = invokeIL.DefineLabel();

        // if (!_warned) { _warned = true; Console.Error.WriteLine("DeprecationWarning: " + _message); }
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, warnedField);
        invokeIL.Emit(OpCodes.Brtrue, alreadyWarnedLabel);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Stfld, warnedField);

        // Console.Error.WriteLine("DeprecationWarning: " + message)
        invokeIL.Emit(OpCodes.Call, typeof(Console).GetProperty("Error")!.GetGetMethod()!);
        invokeIL.Emit(OpCodes.Ldstr, "DeprecationWarning: ");
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, messageField);
        invokeIL.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        invokeIL.Emit(OpCodes.Callvirt, typeof(TextWriter).GetMethod("WriteLine", [typeof(string)])!);

        invokeIL.MarkLabel(alreadyWarnedLabel);

        // Invoke wrapped callable through runtime dispatcher (pure IL dispatch).
        var isTSFunctionLabel = invokeIL.DefineLabel();
        var isBoundFunctionLabel = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, isBoundFunctionLabel);

        invokeIL.Emit(OpCodes.Ldstr, "Cannot invoke deprecated function: wrapped value is not callable.");
        invokeIL.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        invokeIL.Emit(OpCodes.Throw);

        invokeIL.MarkLabel(isTSFunctionLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        invokeIL.Emit(OpCodes.Ret);

        invokeIL.MarkLabel(isBoundFunctionLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        invokeIL.Emit(OpCodes.Ret);

        invokeIL.Emit(OpCodes.Ldstr, "Cannot invoke deprecated function: wrapped value is not callable.");
        invokeIL.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        invokeIL.Emit(OpCodes.Throw);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: deprecated]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $PromisifyCallback type for use by $PromisifiedFunction.
    /// This callback receives (err, value) and resolves/rejects the TaskCompletionSource.
    /// Must be emitted BEFORE $PromisifiedFunction.
    /// </summary>
    internal void EmitPromisifyCallbackClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tcsType = typeof(TaskCompletionSource<object?>);

        var typeBuilder = moduleBuilder.DefineType(
            "$PromisifyCallback",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Field: private readonly TaskCompletionSource<object?> _tcs
        var tcsField = typeBuilder.DefineField("_tcs", tcsType, FieldAttributes.Private | FieldAttributes.InitOnly);

        // Constructor: public $PromisifyCallback(TaskCompletionSource<object?> tcs)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [tcsType]
        );
        runtime.TSPromisifyCallbackCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, tcsField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: public object? Invoke(params object?[] args)
        // Called with (err, value) - resolves or rejects the Task accordingly
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.ObjectArray]
        );

        var invokeIL = invokeMethod.GetILGenerator();
        var errLocal = invokeIL.DeclareLocal(_types.Object);
        var valueLocal = invokeIL.DeclareLocal(_types.Object);
        var hasErrorLabel = invokeIL.DefineLabel();
        var resolveLabel = invokeIL.DefineLabel();
        var returnLabel = invokeIL.DefineLabel();

        // err = args?.Length > 0 ? args[0] : null
        invokeIL.Emit(OpCodes.Ldarg_1);
        var noArgsLabel = invokeIL.DefineLabel();
        var getErrLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, noArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ble, noArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Br, getErrLabel);
        invokeIL.MarkLabel(noArgsLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.MarkLabel(getErrLabel);
        invokeIL.Emit(OpCodes.Stloc, errLocal);

        // value = args?.Length > 1 ? args[1] : null
        invokeIL.Emit(OpCodes.Ldarg_1);
        var noValueLabel = invokeIL.DefineLabel();
        var getValueLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, noValueLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ble, noValueLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Br, getValueLabel);
        invokeIL.MarkLabel(noValueLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.MarkLabel(getValueLabel);
        invokeIL.Emit(OpCodes.Stloc, valueLocal);

        // Check if err is truthy (hasError)
        // hasError = err is not (null | false | "" | 0.0 | 0)
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Brfalse, resolveLabel);  // null -> no error

        // Check if err is false (bool)
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Isinst, _types.Boolean);
        var notBoolLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, notBoolLabel);
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Unbox_Any, _types.Boolean);
        invokeIL.Emit(OpCodes.Brfalse, resolveLabel);  // false -> no error
        invokeIL.Emit(OpCodes.Br, hasErrorLabel);      // true -> has error

        invokeIL.MarkLabel(notBoolLabel);

        // Check if err is empty string
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Isinst, _types.String);
        var notStringLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, notStringLabel);
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Castclass, _types.String);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        invokeIL.Emit(OpCodes.Brfalse, resolveLabel);  // "" -> no error
        invokeIL.Emit(OpCodes.Br, hasErrorLabel);      // non-empty string -> has error

        invokeIL.MarkLabel(notStringLabel);

        // Check if err is 0 (int or double)
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, notDoubleLabel);
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Unbox_Any, _types.Double);
        invokeIL.Emit(OpCodes.Ldc_R8, 0.0);
        invokeIL.Emit(OpCodes.Ceq);
        invokeIL.Emit(OpCodes.Brtrue, resolveLabel);   // 0.0 -> no error
        invokeIL.Emit(OpCodes.Br, hasErrorLabel);      // non-zero -> has error

        invokeIL.MarkLabel(notDoubleLabel);

        // Check if err is 0 (int32)
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Isinst, _types.Int32);
        var notIntLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Brfalse, notIntLabel);
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        invokeIL.Emit(OpCodes.Unbox_Any, _types.Int32);
        invokeIL.Emit(OpCodes.Brfalse, resolveLabel);  // 0 -> no error
        invokeIL.Emit(OpCodes.Br, hasErrorLabel);      // non-zero -> has error

        invokeIL.MarkLabel(notIntLabel);

        // Any other truthy value -> has error
        invokeIL.Emit(OpCodes.Br, hasErrorLabel);

        // hasError: _tcs.TrySetException(new Exception(err?.ToString() ?? "Unknown error"))
        invokeIL.MarkLabel(hasErrorLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, tcsField);

        // Build error message: err?.ToString() ?? "Unknown error"
        invokeIL.Emit(OpCodes.Ldloc, errLocal);
        var errNullLabel = invokeIL.DefineLabel();
        var errMsgDoneLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Dup);
        invokeIL.Emit(OpCodes.Brfalse, errNullLabel);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        invokeIL.Emit(OpCodes.Br, errMsgDoneLabel);
        invokeIL.MarkLabel(errNullLabel);
        invokeIL.Emit(OpCodes.Pop);
        invokeIL.Emit(OpCodes.Ldstr, "Unknown error");
        invokeIL.MarkLabel(errMsgDoneLabel);

        // new Exception(message)
        invokeIL.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        invokeIL.Emit(OpCodes.Callvirt, tcsType.GetMethod("TrySetException", [typeof(Exception)])!);
        invokeIL.Emit(OpCodes.Pop);  // Discard bool result
        invokeIL.Emit(OpCodes.Br, returnLabel);

        // resolve: _tcs.TrySetResult(value)
        invokeIL.MarkLabel(resolveLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, tcsField);
        invokeIL.Emit(OpCodes.Ldloc, valueLocal);
        invokeIL.Emit(OpCodes.Callvirt, tcsType.GetMethod("TrySetResult", [typeof(object)])!);
        invokeIL.Emit(OpCodes.Pop);  // Discard bool result

        invokeIL.MarkLabel(returnLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: promisifyCallback]");
        toStringIL.Emit(OpCodes.Ret);

        runtime.TSPromisifyCallbackInvoke = invokeMethod;
        runtime.TSPromisifyCallbackType = typeBuilder.CreateType()!;
    }

    /// <summary>
    /// Emits $PromisifiedFunction type for util.promisify support.
    /// This wraps a callback-style function and converts it to return a Promise.
    /// </summary>
    internal void EmitTSPromisifiedFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$PromisifiedFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSPromisifiedFunctionType = typeBuilder;

        // Field: _wrapped
        var wrappedField = typeBuilder.DefineField("_wrapped", _types.Object, FieldAttributes.Private);

        // Constructor(wrapped)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.TSPromisifiedFunctionCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, wrappedField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: Invoke(params object[] args) -> object (returns Task<object?>)
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSPromisifiedFunctionInvoke = invokeMethod;

        var invokeIL = invokeMethod.GetILGenerator();

        // var tcs = new TaskCompletionSource<object?>();
        var tcsType = typeof(TaskCompletionSource<object?>);
        var tcsLocal = invokeIL.DeclareLocal(tcsType);
        invokeIL.Emit(OpCodes.Newobj, tcsType.GetConstructor(Type.EmptyTypes)!);
        invokeIL.Emit(OpCodes.Stloc, tcsLocal);

        // var callback = new $PromisifyCallback(tcs);
        // We need to emit the callback class too, but for simplicity let's use a delegate approach
        // Create a callback that resolves the TaskCompletionSource

        // Build args array with callback appended
        // var newArgs = new object[args.Length + 1];
        var newArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        invokeIL.Emit(OpCodes.Ldarg_1);
        var arrayLengthMethod = typeof(Array).GetProperty("Length")!.GetGetMethod()!;
        invokeIL.Emit(OpCodes.Callvirt, arrayLengthMethod);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Add);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, newArgsLocal);

        // Array.Copy(args, newArgs, args.Length);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Callvirt, arrayLengthMethod);
        var arrayCopyMethod = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!;
        invokeIL.Emit(OpCodes.Call, arrayCopyMethod);

        // Create callback function that resolves TCS
        // newArgs[args.Length] = new $PromisifyCallback(tcs);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Callvirt, arrayLengthMethod);
        invokeIL.Emit(OpCodes.Ldloc, tcsLocal);
        invokeIL.Emit(OpCodes.Newobj, runtime.TSPromisifyCallbackCtor);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        // Call wrapped function via runtime dispatcher.
        var invokeResultLocal = invokeIL.DeclareLocal(_types.Object);
        var callableLabel = invokeIL.DefineLabel();
        var afterInvokeLabel = invokeIL.DefineLabel();

        // If wrapped is not recognized callable type, resolve with null.
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, callableLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, callableLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSDeprecatedFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, callableLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSPromisifiedFunctionType);
        invokeIL.Emit(OpCodes.Brtrue, callableLabel);

        // No callable found - just resolve with null
        invokeIL.Emit(OpCodes.Ldloc, tcsLocal);
        invokeIL.Emit(OpCodes.Ldnull);
        var setResultMethod = tcsType.GetMethod("SetResult", [typeof(object)])!;
        invokeIL.Emit(OpCodes.Callvirt, setResultMethod);
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(callableLabel);
        var invokeBoundLabel = invokeIL.DefineLabel();
        var invokeDeprecatedWrapperLabel = invokeIL.DefineLabel();
        var invokePromisifiedWrapperLabel = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        invokeIL.Emit(OpCodes.Brfalse, invokeBoundLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        invokeIL.Emit(OpCodes.Stloc, invokeResultLocal);
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(invokeBoundLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        invokeIL.Emit(OpCodes.Brfalse, invokeDeprecatedWrapperLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        invokeIL.Emit(OpCodes.Stloc, invokeResultLocal);
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(invokeDeprecatedWrapperLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSDeprecatedFunctionType);
        invokeIL.Emit(OpCodes.Brfalse, invokePromisifiedWrapperLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.TSDeprecatedFunctionType);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSDeprecatedFunctionInvoke);
        invokeIL.Emit(OpCodes.Stloc, invokeResultLocal);
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(invokePromisifiedWrapperLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Castclass, runtime.TSPromisifiedFunctionType);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSPromisifiedFunctionInvoke);
        invokeIL.Emit(OpCodes.Stloc, invokeResultLocal);
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(afterInvokeLabel);

        // return tcs.Task;
        invokeIL.Emit(OpCodes.Ldloc, tcsLocal);
        var taskProperty = tcsType.GetProperty("Task")!.GetGetMethod()!;
        invokeIL.Emit(OpCodes.Callvirt, taskProperty);
        invokeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: promisified]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $CallbackifiedFunction type for util.callbackify support.
    /// Wraps a function so that it accepts a callback as the last argument.
    /// On success: callback(null, result). On error: callback(error, null).
    /// </summary>
    internal void EmitTSCallbackifiedFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$CallbackifiedFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSCallbackifiedFunctionType = typeBuilder;

        // Fields
        var wrappedField = typeBuilder.DefineField("_wrapped", _types.Object, FieldAttributes.Private);

        // Constructor(wrapped)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.TSCallbackifiedFunctionCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, wrappedField);
        ctorIL.Emit(OpCodes.Ret);

        // Private static helper: CallFn(object callable, object[] args) -> object
        // Dispatches to TSFunction or BoundTSFunction
        var callFnMethod = typeBuilder.DefineMethod(
            "CallFn",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );

        var callFnIL = callFnMethod.GetILGenerator();
        var cfNotTSFunctionLabel = callFnIL.DefineLabel();
        var cfNotBoundLabel = callFnIL.DefineLabel();

        // if (callable is $TSFunction) return ((TSFunction)callable).Invoke(args)
        callFnIL.Emit(OpCodes.Ldarg_0);
        callFnIL.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        callFnIL.Emit(OpCodes.Brfalse, cfNotTSFunctionLabel);
        callFnIL.Emit(OpCodes.Ldarg_0);
        callFnIL.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        callFnIL.Emit(OpCodes.Ldarg_1);
        callFnIL.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        callFnIL.Emit(OpCodes.Ret);

        // if (callable is $BoundTSFunction) return ((BoundTSFunction)callable).Invoke(args)
        callFnIL.MarkLabel(cfNotTSFunctionLabel);
        callFnIL.Emit(OpCodes.Ldarg_0);
        callFnIL.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        callFnIL.Emit(OpCodes.Brfalse, cfNotBoundLabel);
        callFnIL.Emit(OpCodes.Ldarg_0);
        callFnIL.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        callFnIL.Emit(OpCodes.Ldarg_1);
        callFnIL.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        callFnIL.Emit(OpCodes.Ret);

        // Fallback: throw
        callFnIL.MarkLabel(cfNotBoundLabel);
        callFnIL.Emit(OpCodes.Ldstr, "Cannot invoke callbackified function: value is not callable.");
        callFnIL.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        callFnIL.Emit(OpCodes.Throw);

        // Method: Invoke(params object[] args) -> object
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSCallbackifiedFunctionInvoke = invokeMethod;

        var invokeIL = invokeMethod.GetILGenerator();
        var resultLocal = invokeIL.DeclareLocal(_types.Object);
        var callbackLocal = invokeIL.DeclareLocal(_types.Object);
        var innerArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var callbackArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var exLocal = invokeIL.DeclareLocal(typeof(Exception));

        // callback = args[args.Length - 1]
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Sub);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Stloc, callbackLocal);

        // innerArgs = new object[args.Length - 1]
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Sub);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, innerArgsLocal);

        // if (innerArgs.Length > 0) Array.Copy(args, innerArgs, innerArgs.Length)
        var skipCopyLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, innerArgsLocal);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Brfalse, skipCopyLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldloc, innerArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, innerArgsLocal);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        var arrayCopyMethod = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), typeof(int)])!;
        invokeIL.Emit(OpCodes.Call, arrayCopyMethod);
        invokeIL.MarkLabel(skipCopyLabel);

        // try { result = CallFn(_wrapped, innerArgs); callback(null, result); }
        var endLabel = invokeIL.DefineLabel();
        invokeIL.BeginExceptionBlock();

        // result = CallFn(_wrapped, innerArgs)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Ldloc, innerArgsLocal);
        invokeIL.Emit(OpCodes.Call, callFnMethod);
        invokeIL.Emit(OpCodes.Stloc, resultLocal);

        // callbackArgs = new object[] { null, result }
        invokeIL.Emit(OpCodes.Ldc_I4_2);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stelem_Ref);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ldloc, resultLocal);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        // CallFn(callback, callbackArgs)
        invokeIL.Emit(OpCodes.Ldloc, callbackLocal);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Call, callFnMethod);
        invokeIL.Emit(OpCodes.Pop);

        invokeIL.Emit(OpCodes.Leave, endLabel);

        // catch (Exception ex)
        invokeIL.BeginCatchBlock(typeof(Exception));
        invokeIL.Emit(OpCodes.Stloc, exLocal);

        // callbackArgs = new object[] { errorMessage, null }
        invokeIL.Emit(OpCodes.Ldc_I4_2);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        // Use InnerException?.Message ?? Message (TargetInvocationException wrapping)
        invokeIL.Emit(OpCodes.Ldloc, exLocal);
        invokeIL.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("InnerException")!.GetGetMethod()!);
        var noInnerLabel = invokeIL.DefineLabel();
        var errorMsgDoneLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Dup);
        invokeIL.Emit(OpCodes.Brfalse, noInnerLabel);
        invokeIL.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        invokeIL.Emit(OpCodes.Br, errorMsgDoneLabel);
        invokeIL.MarkLabel(noInnerLabel);
        invokeIL.Emit(OpCodes.Pop);
        invokeIL.Emit(OpCodes.Ldloc, exLocal);
        invokeIL.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        invokeIL.MarkLabel(errorMsgDoneLabel);
        invokeIL.Emit(OpCodes.Stelem_Ref);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        // CallFn(callback, callbackArgs)
        invokeIL.Emit(OpCodes.Ldloc, callbackLocal);
        invokeIL.Emit(OpCodes.Ldloc, callbackArgsLocal);
        invokeIL.Emit(OpCodes.Call, callFnMethod);
        invokeIL.Emit(OpCodes.Pop);

        invokeIL.Emit(OpCodes.Leave, endLabel);

        invokeIL.EndExceptionBlock();

        invokeIL.MarkLabel(endLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: callbackified]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $TextDecoderDecodeMethod wrapper for compiled mode decode calls.
    /// </summary>
    internal void EmitTSTextDecoderDecodeMethodClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TextDecoderDecodeMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSTextDecoderDecodeMethodType = typeBuilder;

        // Field: _decoder
        var decoderField = typeBuilder.DefineField("_decoder", runtime.TSTextDecoderType, FieldAttributes.Private);

        // Constructor(decoder)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSTextDecoderType]
        );
        _ = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, decoderField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: Invoke(params object[] args) -> object
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSTextDecoderDecodeMethodInvoke = invokeMethod;

        var invokeIL = invokeMethod.GetILGenerator();
        var bytesLocal = invokeIL.DeclareLocal(typeof(byte[]));
        var noArgsLabel = invokeIL.DefineLabel();
        var hasArgsLabel = invokeIL.DefineLabel();
        var isBufferLabel = invokeIL.DefineLabel();
        var callDecodeLabel = invokeIL.DefineLabel();

        // if (args == null || args.Length == 0 || args[0] == null) bytes = null
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Brfalse, noArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Brfalse, noArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Brfalse, noArgsLabel);
        invokeIL.Emit(OpCodes.Br, hasArgsLabel);

        invokeIL.MarkLabel(noArgsLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stloc, bytesLocal);
        invokeIL.Emit(OpCodes.Br, callDecodeLabel);

        invokeIL.MarkLabel(hasArgsLabel);
        // Check if args[0] is $Buffer
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Isinst, runtime.TSBufferType);
        invokeIL.Emit(OpCodes.Brtrue, isBufferLabel);

        // Not a buffer - try to cast to byte[]
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Castclass, typeof(byte[]));
        invokeIL.Emit(OpCodes.Stloc, bytesLocal);
        invokeIL.Emit(OpCodes.Br, callDecodeLabel);

        invokeIL.MarkLabel(isBufferLabel);
        // Is a buffer - get its Data property
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Castclass, runtime.TSBufferType);
        invokeIL.Emit(OpCodes.Call, runtime.TSBufferGetData);
        invokeIL.Emit(OpCodes.Stloc, bytesLocal);

        invokeIL.MarkLabel(callDecodeLabel);
        // return _decoder.Decode(bytes)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, decoderField);
        invokeIL.Emit(OpCodes.Ldloc, bytesLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSTextDecoderDecode);
        invokeIL.Emit(OpCodes.Ret);

        // Override ToString
        var toStringMethod = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringMethod.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: decode]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
