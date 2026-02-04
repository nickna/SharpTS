using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// StringDecoder module support for standalone assemblies.
/// Provides the $StringDecoder class for decoding Buffer chunks into strings.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSStringDecoderClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $StringDecoder
        var typeBuilder = moduleBuilder.DefineType(
            "$StringDecoder",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSStringDecoderType = typeBuilder;

        // Fields
        var encodingField = typeBuilder.DefineField(
            "_encoding",
            _types.String,
            FieldAttributes.Private
        );

        var textEncodingField = typeBuilder.DefineField(
            "_textEncoding",
            typeof(Encoding),
            FieldAttributes.Private
        );

        var pendingBytesField = typeBuilder.DefineField(
            "_pendingBytes",
            typeof(byte[]),
            FieldAttributes.Private
        );

        // Constructor: public $StringDecoder(string encoding = "utf8")
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSStringDecoderCtor = ctor;

        var ctorIL = ctor.GetILGenerator();

        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // Normalize and store encoding
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Call, typeof(string).GetMethod("ToLowerInvariant")!);
        ctorIL.Emit(OpCodes.Stfld, encodingField);

        // Initialize pending bytes as empty array
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Newarr, typeof(byte));
        ctorIL.Emit(OpCodes.Stfld, pendingBytesField);

        // Set text encoding based on encoding string
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, encodingField);
        ctorIL.Emit(OpCodes.Call, typeof(RuntimeEmitter).GetMethod("GetTextEncodingHelper", BindingFlags.Public | BindingFlags.Static)!);
        ctorIL.Emit(OpCodes.Stfld, textEncodingField);

        ctorIL.Emit(OpCodes.Ret);

        // Property: encoding (getter only)
        var encodingProperty = typeBuilder.DefineProperty(
            "encoding",
            PropertyAttributes.None,
            _types.String,
            Type.EmptyTypes
        );

        var encodingGetter = typeBuilder.DefineMethod(
            "get_Encoding",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSStringDecoderEncodingGetter = encodingGetter;

        var encodingGetterIL = encodingGetter.GetILGenerator();
        encodingGetterIL.Emit(OpCodes.Ldarg_0);
        encodingGetterIL.Emit(OpCodes.Ldfld, encodingField);
        encodingGetterIL.Emit(OpCodes.Ret);

        encodingProperty.SetGetMethod(encodingGetter);

        // Method: write(object? buffer) -> string
        var writeMethod = typeBuilder.DefineMethod(
            "write",
            MethodAttributes.Public,
            _types.String,
            [_types.Object]
        );
        runtime.TSStringDecoderWrite = writeMethod;

        EmitStringDecoderWriteBody(writeMethod, pendingBytesField, textEncodingField, encodingField, runtime);

        // Method: end(object? buffer = null) -> string
        var endMethod = typeBuilder.DefineMethod(
            "end",
            MethodAttributes.Public,
            _types.String,
            [_types.Object]
        );
        runtime.TSStringDecoderEnd = endMethod;

        EmitStringDecoderEndBody(endMethod, pendingBytesField, textEncodingField, writeMethod);

        // Create the type
        runtime.TSStringDecoderType = typeBuilder.CreateType()!;
    }

    private void EmitStringDecoderWriteBody(
        MethodBuilder method,
        FieldBuilder pendingBytesField,
        FieldBuilder textEncodingField,
        FieldBuilder encodingField,
        EmittedRuntime runtime)
    {
        var il = method.GetILGenerator();

        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var combinedLocal = il.DeclareLocal(typeof(byte[]));
        var resultLocal = il.DeclareLocal(_types.String);

        var returnEmptyLabel = il.DefineLabel();
        var afterBufferCheckLabel = il.DefineLabel();
        var decodeLabel = il.DefineLabel();

        // Get bytes from buffer argument
        // if (buffer is $Buffer tsBuffer) bytes = tsBuffer.GetData();
        // else if (buffer is byte[] byteArr) bytes = byteArr;
        // else return "";
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, afterBufferCheckLabel);

        // It's a $Buffer
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Br_S, decodeLabel);

        il.MarkLabel(afterBufferCheckLabel);
        il.Emit(OpCodes.Pop);

        // Try byte[]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(byte[]));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, returnEmptyLabel);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Br_S, decodeLabel);

        // Return empty string for invalid input
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(decodeLabel);

        // Combine pending bytes with new bytes
        // For simplicity, just decode the new bytes and clear pending
        // A full implementation would handle multi-byte sequences across calls
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, textEncodingField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", [typeof(byte[])])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Clear pending bytes (simplified)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stfld, pendingBytesField);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringDecoderEndBody(
        MethodBuilder method,
        FieldBuilder pendingBytesField,
        FieldBuilder textEncodingField,
        MethodBuilder writeMethod)
    {
        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.String);
        var pendingResultLocal = il.DeclareLocal(_types.String);
        var hasBufferLabel = il.DefineLabel();
        var afterWriteLabel = il.DefineLabel();

        // If buffer argument is provided, write it first
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse_S, afterWriteLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, writeMethod);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br_S, hasBufferLabel);

        il.MarkLabel(afterWriteLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(hasBufferLabel);

        // Decode any pending bytes
        var hasPendingLabel = il.DefineLabel();
        var noPendingLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pendingBytesField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq_S, noPendingLabel);

        // Decode pending bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, textEncodingField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pendingBytesField);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", [typeof(byte[])])!);
        il.Emit(OpCodes.Stloc, pendingResultLocal);

        // Clear pending
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stfld, pendingBytesField);

        // Concatenate result + pending
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pendingResultLocal);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noPendingLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a factory method that creates new $StringDecoder instances.
    /// Signature: object StringDecoderCreate(object[] args)
    /// </summary>
    private MethodBuilder EmitStringDecoderCreateMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringDecoderCreate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var encodingLocal = il.DeclareLocal(_types.String);
        var useDefaultLabel = il.DefineLabel();
        var useProvidedLabel = il.DefineLabel();
        var createInstanceLabel = il.DefineLabel();

        // Extract encoding from args[0] or default to "utf8"
        // if (args == null) goto useDefault
        il.Emit(OpCodes.Ldarg_0); // args
        il.Emit(OpCodes.Brfalse_S, useDefaultLabel);

        // if (args.Length == 0) goto useDefault
        il.Emit(OpCodes.Ldarg_0); // args
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, useDefaultLabel);

        // var arg0 = args[0] as string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        // if (arg0 != null) goto useProvided
        il.Emit(OpCodes.Brtrue_S, useProvidedLabel);

        // Not a string, fall through to default
        il.Emit(OpCodes.Pop);

        // useDefault: encoding = "utf8"
        il.MarkLabel(useDefaultLabel);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stloc, encodingLocal);
        il.Emit(OpCodes.Br_S, createInstanceLabel);

        // useProvided: encoding = (string)args[0]
        il.MarkLabel(useProvidedLabel);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // createInstance: return new $StringDecoder(encoding)
        il.MarkLabel(createInstanceLabel);
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Newobj, runtime.TSStringDecoderCtor);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitStringDecoderGetConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the factory method
        var createMethod = EmitStringDecoderCreateMethod(typeBuilder, runtime);

        // Creates a wrapper function that returns a $TSFunction for the StringDecoder constructor
        var method = typeBuilder.DefineMethod(
            "StringDecoderGetConstructor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.StringDecoderGetConstructor = method;

        var il = method.GetILGenerator();

        // Create a $TSFunction wrapping the StringDecoderCreate method
        // new $TSFunction(null, MethodInfo)
        il.Emit(OpCodes.Ldnull); // target (null for static method)
        il.Emit(OpCodes.Ldtoken, createMethod);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper method called from emitted code to get the appropriate System.Text.Encoding.
    /// </summary>
    public static Encoding GetTextEncodingHelper(string encoding)
    {
        return encoding switch
        {
            "utf8" or "utf-8" => Encoding.UTF8,
            "utf16le" or "utf-16le" or "ucs2" or "ucs-2" => Encoding.Unicode,
            "latin1" or "binary" => Encoding.Latin1,
            "ascii" => Encoding.ASCII,
            _ => Encoding.UTF8
        };
    }
}
