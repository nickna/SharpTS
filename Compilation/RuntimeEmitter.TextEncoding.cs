using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $TextEncoder / $TextDecoder / $TextDecoderDecodeMethod types for
/// standalone compiled assemblies (gated on UsesTextEncoding). Extracted from
/// the retired RuntimeEmitter.UtilModule.cs when the rest of the emitted util
/// surface died with the stdlib/node/util.ts migration (2026-07 cleanup).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits $TextEncoder type for standalone util support.
    /// </summary>
    internal void EmitTSTextEncoderClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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
    /// Emits $TextDecoderDecodeMethod wrapper for compiled mode decode calls.
    /// </summary>
    internal void EmitTSTextDecoderDecodeMethodClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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
