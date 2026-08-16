using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$CountQueuingStrategy</c> and
/// <c>$ByteLengthQueuingStrategy</c> classes for the Web Streams API.
/// </summary>
/// <remarks>
/// Both are simple data classes: a single <c>highWaterMark</c> field plus a
/// <c>size(chunk)</c> method. They're the simplest pure-IL emission target in
/// the Web Streams family and serve as a proof-of-concept for the larger
/// $ReadableStream/$WritableStream/$TransformStream emission that follows.
///
/// Compiled-mode property dispatch (<c>GetFieldsProperty</c>) finds these via
/// PascalCase reflection — <c>Object.GetType().GetProperty("HighWaterMark", IgnoreCase)</c>
/// matches <c>"highWaterMark"</c> from JS, and <c>GetMethod("Size", IgnoreCase)</c>
/// matches <c>size(chunk)</c>.
/// </remarks>
public partial class RuntimeEmitter
{
    private void EmitQueuingStrategyClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitCountQueuingStrategy(moduleBuilder, runtime);
        EmitByteLengthQueuingStrategy(moduleBuilder, runtime);
    }

    private void EmitCountQueuingStrategy(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$CountQueuingStrategy",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var hwmField = typeBuilder.DefineField("_highWaterMark", _types.Double, FieldAttributes.Private);

        // Constructor: $CountQueuingStrategy(object? options)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]);

        var ctorIL = ctor.GetILGenerator();
        // base()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // _highWaterMark = ExtractHighWaterMark(options)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        EmitExtractHighWaterMarkInline(ctorIL);
        ctorIL.Emit(OpCodes.Stfld, hwmField);
        ctorIL.Emit(OpCodes.Ret);

        EmitHighWaterMarkProperty(typeBuilder, hwmField);

        // Method: Size(object? chunk) -> double
        // CountQueuingStrategy always returns 1.0 regardless of chunk.
        var sizeMethod = typeBuilder.DefineMethod(
            "Size",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object]);
        var sizeIL = sizeMethod.GetILGenerator();
        sizeIL.Emit(OpCodes.Ldc_R8, 1.0);
        sizeIL.Emit(OpCodes.Ret);

        var createdType = typeBuilder.CreateType()!;
        _ = createdType;
        runtime.CountQueuingStrategyCtor = ctor;
    }

    private void EmitByteLengthQueuingStrategy(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ByteLengthQueuingStrategy",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var hwmField = typeBuilder.DefineField("_highWaterMark", _types.Double, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]);

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        EmitExtractHighWaterMarkInline(ctorIL);
        ctorIL.Emit(OpCodes.Stfld, hwmField);
        ctorIL.Emit(OpCodes.Ret);

        EmitHighWaterMarkProperty(typeBuilder, hwmField);

        // Method: Size(object? chunk) -> double
        // Returns chunk.byteLength for byte[] or $Buffer; 0 otherwise.
        var sizeMethod = typeBuilder.DefineMethod(
            "Size",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object]);
        var sizeIL = sizeMethod.GetILGenerator();

        var notByteArrayLabel = sizeIL.DefineLabel();
        var notBufferLabel = sizeIL.DefineLabel();
        var returnZeroLabel = sizeIL.DefineLabel();

        // if (chunk is byte[] arr) return arr.Length;
        sizeIL.Emit(OpCodes.Ldarg_1);
        sizeIL.Emit(OpCodes.Isinst, typeof(byte[]));
        sizeIL.Emit(OpCodes.Brfalse, notByteArrayLabel);
        sizeIL.Emit(OpCodes.Ldarg_1);
        sizeIL.Emit(OpCodes.Castclass, typeof(byte[]));
        sizeIL.Emit(OpCodes.Ldlen);
        sizeIL.Emit(OpCodes.Conv_R8);
        sizeIL.Emit(OpCodes.Ret);

        // if (chunk is $Buffer buf) return (double)buf.Length;
        // Skipped when $Buffer wasn't emitted — without UsesBuffer the type
        // can't appear, and the Isinst would NRE on a null type token.
        sizeIL.MarkLabel(notByteArrayLabel);
        if (_features.UsesBuffer)
        {
            sizeIL.Emit(OpCodes.Ldarg_1);
            sizeIL.Emit(OpCodes.Isinst, runtime.TSBufferType);
            sizeIL.Emit(OpCodes.Brfalse, notBufferLabel);
            sizeIL.Emit(OpCodes.Ldarg_1);
            sizeIL.Emit(OpCodes.Castclass, runtime.TSBufferType);
            sizeIL.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            sizeIL.Emit(OpCodes.Ldlen);
            sizeIL.Emit(OpCodes.Conv_R8);
            sizeIL.Emit(OpCodes.Ret);
        }

        sizeIL.MarkLabel(notBufferLabel);
        sizeIL.MarkLabel(returnZeroLabel);
        sizeIL.Emit(OpCodes.Ldc_R8, 0.0);
        sizeIL.Emit(OpCodes.Ret);

        var createdType = typeBuilder.CreateType()!;
        _ = createdType;
        runtime.ByteLengthQueuingStrategyCtor = ctor;
    }

    /// <summary>
    /// Emits a public read-only <c>HighWaterMark</c> property over the given
    /// double field. PascalCase so the JS-facing reflection lookup
    /// (<c>Object.GetProperty("HighWaterMark", IgnoreCase)</c>) finds it when
    /// JS code reads <c>strategy.highWaterMark</c>.
    /// </summary>
    private void EmitHighWaterMarkProperty(TypeBuilder typeBuilder, FieldBuilder hwmField)
    {
        var prop = typeBuilder.DefineProperty(
            "HighWaterMark",
            PropertyAttributes.None,
            _types.Double,
            Type.EmptyTypes);

        var getter = typeBuilder.DefineMethod(
            "get_HighWaterMark",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Double,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hwmField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits IL that pops an <c>object?</c> off the stack (a JS options dict)
    /// and pushes a <c>double</c> highWaterMark. Extracts the value from a
    /// <c>Dictionary&lt;string, object?&gt;</c>'s <c>"highWaterMark"</c> entry,
    /// converting int/long to double if needed. Returns 0.0 if anything is
    /// missing or the wrong shape.
    /// </summary>
    private void EmitExtractHighWaterMarkInline(ILGenerator il)
    {
        // Stack: [options]
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);

        var notDictLabel = il.DefineLabel();
        var lookupFailedLabel = il.DefineLabel();
        var checkIntLabel = il.DefineLabel();
        var checkLongLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (options is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // dict.TryGetValue("highWaterMark", out value)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "highWaterMark");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, lookupFailedLabel);

        // if (value is double d) return d
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkIntLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Br, doneLabel);

        // else if (value is int i) return (double)i
        il.MarkLabel(checkIntLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brfalse, checkLongLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, doneLabel);

        // else if (value is long l) return (double)l
        il.MarkLabel(checkLongLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Int64);
        il.Emit(OpCodes.Brfalse, lookupFailedLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Int64);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, doneLabel);

        // Default: 0.0
        il.MarkLabel(notDictLabel);
        il.MarkLabel(lookupFailedLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);

        il.MarkLabel(doneLabel);
        // Stack: [hwm:double]
    }
}
