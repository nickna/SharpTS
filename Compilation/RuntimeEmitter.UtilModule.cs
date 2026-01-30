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
        runtime.TSTextEncoderEncodingGetter = encodingGetter;

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
        runtime.TSTextEncoderEncode = encodeMethod;

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
        runtime.TSTextDecoderEncodingGetter = encodingGetter;

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
        runtime.TSTextDecoderFatalGetter = fatalGetter;

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
        runtime.TSTextDecoderIgnoreBOMGetter = ignoreBOMGetter;

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
        decodeIL.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", [typeof(byte[])])!);
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

        // Find and call Invoke on wrapped object via reflection
        // var invokeMethod = _wrapped.GetType().GetMethod("Invoke");
        // if (invokeMethod != null) return invokeMethod.Invoke(_wrapped, new object[] { args });
        var methodInfoLocal = invokeIL.DeclareLocal(_types.MethodInfo);
        var resultLocal = invokeIL.DeclareLocal(_types.Object);
        var noInvokeLabel = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        invokeIL.Emit(OpCodes.Ldstr, "Invoke");
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        invokeIL.Emit(OpCodes.Stloc, methodInfoLocal);

        invokeIL.Emit(OpCodes.Ldloc, methodInfoLocal);
        invokeIL.Emit(OpCodes.Brfalse, noInvokeLabel);

        // return methodInfo.Invoke(_wrapped, new object[] { args })
        invokeIL.Emit(OpCodes.Ldloc, methodInfoLocal);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Dup);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Stelem_Ref);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        invokeIL.Emit(OpCodes.Ret);

        invokeIL.MarkLabel(noInvokeLabel);
        // Return null if no Invoke method found
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
        toStringIL.Emit(OpCodes.Ldstr, "[Function: deprecated]");
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
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
        // newArgs[args.Length] = new PromisifyCallback(tcs);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Callvirt, arrayLengthMethod);
        invokeIL.Emit(OpCodes.Ldloc, tcsLocal);
        invokeIL.Emit(OpCodes.Newobj, typeof(PromisifyCallback).GetConstructor([tcsType])!);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        // Call wrapped function via reflection (like $DeprecatedFunction does)
        // var invokeMethod = _wrapped.GetType().GetMethod("Invoke");
        var methodInfoLocal = invokeIL.DeclareLocal(_types.MethodInfo);
        var noInvokeLabel = invokeIL.DefineLabel();
        var afterInvokeLabel = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        invokeIL.Emit(OpCodes.Ldstr, "Invoke");
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        invokeIL.Emit(OpCodes.Stloc, methodInfoLocal);

        invokeIL.Emit(OpCodes.Ldloc, methodInfoLocal);
        invokeIL.Emit(OpCodes.Brfalse, noInvokeLabel);

        // invokeMethod.Invoke(_wrapped, new object[] { newArgs })
        invokeIL.Emit(OpCodes.Ldloc, methodInfoLocal);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, wrappedField);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Dup);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldloc, newArgsLocal);
        invokeIL.Emit(OpCodes.Stelem_Ref);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        invokeIL.Emit(OpCodes.Pop); // Discard return value
        invokeIL.Emit(OpCodes.Br, afterInvokeLabel);

        invokeIL.MarkLabel(noInvokeLabel);
        // No Invoke method found - just resolve with null
        invokeIL.Emit(OpCodes.Ldloc, tcsLocal);
        invokeIL.Emit(OpCodes.Ldnull);
        var setResultMethod = tcsType.GetMethod("SetResult", [typeof(object)])!;
        invokeIL.Emit(OpCodes.Callvirt, setResultMethod);

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
        runtime.TSTextDecoderDecodeMethodCtor = ctor;

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
