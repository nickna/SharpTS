using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Worker Threads support into the compiled assembly.
/// Provides helper methods for SharedArrayBuffer, TypedArrays, Atomics,
/// MessagePort, MessageChannel, and Worker constructors.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all Worker-related helper methods into the $Runtime class.
    /// </summary>
    private void EmitWorkerHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // ArrayBuffer / SharedArrayBuffer / DataView / TypedArray constructor
        // helpers + Atomics — gated together on HasAnyTypedArray. Without any
        // typed-array kind referenced by the program, none of these helpers'
        // runtime field references would be valid.
        if (_features.HasAnyTypedArray)
        {
            EmitSharedArrayBufferHelper(runtimeType, runtime);
            EmitArrayBufferHelper(runtimeType, runtime);
            EmitDataViewHelper(runtimeType, runtime);
            EmitTypedArrayHelpers(runtimeType, runtime);
            // Atomics static methods (pure-IL with reflection fallback for SharpTS types)
            EmitAtomicsHelpersPure(runtimeType, runtime);
        }

        // MessageChannel/MessagePort moved to RuntimeEmitter.MessageChannel.cs —
        // emitted after EmitRuntimeClass because $MessagePort.PostMessage calls
        // $Runtime.StructuredClone (#222).

        // Worker constructor helper
        EmitWorkerHelper(runtimeType, runtime);

        // StructuredClone helper
        EmitStructuredCloneHelper(runtimeType, runtime);

        // worker_threads module helpers
        EmitWorkerThreadsModuleHelpers(runtimeType, runtime);
    }

    /// <summary>
    /// Emits helper for creating SharedArrayBuffer.
    /// public static object CreateSharedArrayBuffer(double byteLength)
    /// Uses the emitted $SharedArrayBuffer type (pure-IL, no reflection).
    /// </summary>
    private void EmitSharedArrayBufferHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "CreateSharedArrayBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double]
        );

        var il = method.GetILGenerator();

        // return new $SharedArrayBuffer((int)byteLength)
        il.Emit(OpCodes.Ldarg_0);  // byteLength (double)
        il.Emit(OpCodes.Conv_I4);  // convert to int
        il.Emit(OpCodes.Newobj, runtime.SharedArrayBufferCtor);
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferCtor = method;

        // Also emit slice and byteLength helpers
        EmitSharedArrayBufferSlice(runtimeType, runtime);
        EmitSharedArrayBufferByteLength(runtimeType, runtime);
    }

    /// <summary>
    /// Emits SharedArrayBuffer.slice(begin?, end?) helper.
    /// Requires emitted $SharedArrayBuffer type and calls directly.
    /// </summary>
    private void EmitSharedArrayBufferSlice(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SharedArrayBufferSlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32, _types.Int32]
        );

        var il = method.GetILGenerator();

        // Check if it's the emitted $SharedArrayBuffer type
        var emittedPathLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brtrue, emittedPathLabel);

        il.Emit(OpCodes.Ldstr, "SharedArrayBuffer.slice requires emitted SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // Emitted type path - call Slice directly
        // For emitted type, end == int.MaxValue means use buffer length (handled inside Slice)
        il.MarkLabel(emittedPathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferSlice);
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferSlice = method;
    }

    /// <summary>
    /// Emits SharedArrayBuffer.byteLength getter helper.
    /// Requires emitted $SharedArrayBuffer type.
    /// </summary>
    private void EmitSharedArrayBufferByteLength(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SharedArrayBufferByteLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        var emittedPath = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "SharedArrayBuffer.byteLength requires emitted SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferByteLengthGetter = method;
    }

    /// <summary>
    /// Emits helper for creating ArrayBuffer.
    /// public static object CreateArrayBuffer(double byteLength)
    /// Uses the emitted $ArrayBuffer type (pure-IL, no reflection).
    /// </summary>
    private void EmitArrayBufferHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "CreateArrayBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double]
        );

        var il = method.GetILGenerator();

        // return new $ArrayBuffer((int)byteLength)
        il.Emit(OpCodes.Ldarg_0);  // byteLength (double)
        il.Emit(OpCodes.Conv_I4);  // convert to int
        il.Emit(OpCodes.Newobj, runtime.ArrayBufferCtor);
        il.Emit(OpCodes.Ret);

        runtime.TSArrayBufferCtor = method;

        // Also emit slice, byteLength, and isView helpers
        EmitArrayBufferSlice(runtimeType, runtime);
        EmitArrayBufferByteLength(runtimeType, runtime);
        EmitArrayBufferIsView(runtimeType, runtime);
    }

    /// <summary>
    /// Emits ArrayBuffer.slice(begin, end) helper.
    /// Requires the emitted $ArrayBuffer type (pure-IL, no reflection).
    /// </summary>
    private void EmitArrayBufferSlice(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ArrayBufferSlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32, _types.Int32]
        );

        var il = method.GetILGenerator();

        // Check if obj is $ArrayBuffer - if so, call Slice directly
        var emittedTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        il.Emit(OpCodes.Brtrue, emittedTypeLabel);

        il.Emit(OpCodes.Ldstr, "ArrayBuffer.slice requires emitted ArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedTypeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Ldarg_1);  // begin
        il.Emit(OpCodes.Ldarg_2);  // end
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferSlice);
        il.Emit(OpCodes.Ret);

        runtime.TSArrayBufferSlice = method;
    }

    /// <summary>
    /// Emits ArrayBuffer.byteLength getter helper.
    /// Uses the emitted $ArrayBuffer type when possible (pure-IL, no reflection).
    /// </summary>
    private void EmitArrayBufferByteLength(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ArrayBufferByteLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Check if obj is $ArrayBuffer - if so, call ByteLength getter directly
        var notEmittedTypeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        il.Emit(OpCodes.Brfalse, notEmittedTypeLabel);

        // It's our emitted $ArrayBuffer - call ByteLength directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);  // Convert int to double
        il.Emit(OpCodes.Ret);

        // Not our emitted type
        il.MarkLabel(notEmittedTypeLabel);
        il.Emit(OpCodes.Ldstr, "ArrayBuffer.byteLength requires emitted ArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.Emit(OpCodes.Ret);

        runtime.TSArrayBufferByteLengthGetter = method;
    }

    /// <summary>
    /// Emits ArrayBuffer.isView static method helper.
    /// Returns true if the argument is a TypedArray or DataView.
    /// Handles both emitted pure-IL types and interpreter types.
    /// </summary>
    private void EmitArrayBufferIsView(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ArrayBufferIsView",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        var returnTrueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // Check if arg is null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Check if arg is an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // Check if arg is an emitted $DataView
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.DataViewType);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // Non-emitted types are not views in standalone mode.
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        runtime.TSArrayBufferIsView = method;
    }

    /// <summary>
    /// Emits helper for creating DataView.
    /// public static object CreateDataView(object buffer, double byteOffset, object byteLength)
    /// Uses emitted $DataView type for emitted ArrayBuffer types, falls back to reflection for interpreter types.
    /// </summary>
    private void EmitDataViewHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "CreateDataView",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();

        var byteLengthIntLocal = il.DeclareLocal(typeof(int?));
        var unsupportedTypeLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // First, convert byteLength from object to int?
        var hasLengthLabel = il.DefineLabel();
        var afterLengthLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, hasLengthLabel);

        // Has byteLength - unbox double and convert to int?
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, _types.NullableInt32Ctor);
        il.Emit(OpCodes.Stloc, byteLengthIntLocal);
        il.Emit(OpCodes.Br, afterLengthLabel);

        // No byteLength - use null
        il.MarkLabel(hasLengthLabel);
        il.Emit(OpCodes.Ldloca, byteLengthIntLocal);
        il.Emit(OpCodes.Initobj, typeof(int?));

        il.MarkLabel(afterLengthLabel);

        // Check if buffer is $ArrayBuffer or $SharedArrayBuffer (emitted types)
        // If so, create $DataView directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        var notArrayBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notArrayBufferLabel);

        // It's $ArrayBuffer - create $DataView(buffer, byteOffset, byteLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, byteLengthIntLocal);
        il.Emit(OpCodes.Newobj, runtime.DataViewCtor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notArrayBufferLabel);

        // Check if $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brfalse, unsupportedTypeLabel);

        // It's $SharedArrayBuffer - create $DataView(buffer, byteOffset, byteLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, byteLengthIntLocal);
        il.Emit(OpCodes.Newobj, runtime.DataViewCtor);
        il.Emit(OpCodes.Br, endLabel);

        // Non-emitted buffer type in standalone mode.
        il.MarkLabel(unsupportedTypeLabel);
        il.Emit(OpCodes.Ldstr, "DataView constructor requires emitted ArrayBuffer or SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.TSDataViewCtor = method;

        // Emit property getters
        EmitDataViewByteLength(runtimeType, runtime);
        EmitDataViewByteOffset(runtimeType, runtime);
        EmitDataViewBuffer(runtimeType, runtime);

        // Emit getter methods
        EmitDataViewGetter(runtimeType, runtime, "GetInt8", "getInt8", false);
        EmitDataViewGetter(runtimeType, runtime, "GetUint8", "getUint8", false);
        EmitDataViewGetter(runtimeType, runtime, "GetInt16", "getInt16", true);
        EmitDataViewGetter(runtimeType, runtime, "GetUint16", "getUint16", true);
        EmitDataViewGetter(runtimeType, runtime, "GetInt32", "getInt32", true);
        EmitDataViewGetter(runtimeType, runtime, "GetUint32", "getUint32", true);
        EmitDataViewGetter(runtimeType, runtime, "GetFloat32", "getFloat32", true);
        EmitDataViewGetter(runtimeType, runtime, "GetFloat64", "getFloat64", true);
        EmitDataViewBigIntGetter(runtimeType, runtime, "GetBigInt64", "getBigInt64");
        EmitDataViewBigIntGetter(runtimeType, runtime, "GetBigUint64", "getBigUint64");

        // Emit setter methods
        EmitDataViewSetter(runtimeType, runtime, "SetInt8", "setInt8", false);
        EmitDataViewSetter(runtimeType, runtime, "SetUint8", "setUint8", false);
        EmitDataViewSetter(runtimeType, runtime, "SetInt16", "setInt16", true);
        EmitDataViewSetter(runtimeType, runtime, "SetUint16", "setUint16", true);
        EmitDataViewSetter(runtimeType, runtime, "SetInt32", "setInt32", true);
        EmitDataViewSetter(runtimeType, runtime, "SetUint32", "setUint32", true);
        EmitDataViewSetter(runtimeType, runtime, "SetFloat32", "setFloat32", true);
        EmitDataViewSetter(runtimeType, runtime, "SetFloat64", "setFloat64", true);
        EmitDataViewSetter(runtimeType, runtime, "SetBigInt64", "setBigInt64", true);
        EmitDataViewSetter(runtimeType, runtime, "SetBigUint64", "setBigUint64", true);
    }

    private void EmitNullableIntFromObject(ILGenerator il, int argIndex)
    {
        var hasValueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Brfalse, hasValueLabel);

        // Has value - unbox and wrap in nullable
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, _types.NullableInt32Ctor);
        il.Emit(OpCodes.Br, endLabel);

        // Null
        il.MarkLabel(hasValueLabel);
        var localNullableInt = il.DeclareLocal(typeof(int?));
        il.Emit(OpCodes.Ldloca, localNullableInt);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, localNullableInt);

        il.MarkLabel(endLabel);
    }

    private void EmitDataViewByteLength(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "DataViewByteLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        runtime.TSDataViewByteLengthGetter = method;
    }

    private void EmitDataViewByteOffset(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "DataViewByteOffset",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewByteOffsetGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        runtime.TSDataViewByteOffsetGetter = method;
    }

    private void EmitDataViewBuffer(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "DataViewBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewBufferGetter);
        il.Emit(OpCodes.Ret);

        runtime.TSDataViewBufferGetter = method;
    }

    private void EmitDataViewGetter(TypeBuilder runtimeType, EmittedRuntime runtime, string runtimeMethodName, string jsMethodName, bool hasEndianness)
    {
        var paramTypes = hasEndianness
            ? new[] { _types.Object, _types.Int32, _types.Boolean }
            : new[] { _types.Object, _types.Int32 };

        var method = runtimeType.DefineMethod(
            $"DataView{runtimeMethodName}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            paramTypes
        );

        MethodBuilder target = runtimeMethodName switch
        {
            "GetInt8" => runtime.DataViewGetInt8,
            "GetUint8" => runtime.DataViewGetUint8,
            "GetInt16" => runtime.DataViewGetInt16,
            "GetUint16" => runtime.DataViewGetUint16,
            "GetInt32" => runtime.DataViewGetInt32,
            "GetUint32" => runtime.DataViewGetUint32,
            "GetFloat32" => runtime.DataViewGetFloat32,
            "GetFloat64" => runtime.DataViewGetFloat64,
            _ => throw new ArgumentException($"Unknown DataView getter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Ldarg_1);
        if (hasEndianness)
            il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        // Store in runtime
        switch (jsMethodName)
        {
            case "getInt8": runtime.TSDataViewGetInt8 = method; break;
            case "getUint8": runtime.TSDataViewGetUint8 = method; break;
            case "getInt16": runtime.TSDataViewGetInt16 = method; break;
            case "getUint16": runtime.TSDataViewGetUint16 = method; break;
            case "getInt32": runtime.TSDataViewGetInt32 = method; break;
            case "getUint32": runtime.TSDataViewGetUint32 = method; break;
            case "getFloat32": runtime.TSDataViewGetFloat32 = method; break;
            case "getFloat64": runtime.TSDataViewGetFloat64 = method; break;
        }
    }

    private void EmitDataViewBigIntGetter(TypeBuilder runtimeType, EmittedRuntime runtime, string runtimeMethodName, string jsMethodName)
    {
        var method = runtimeType.DefineMethod(
            $"DataView{runtimeMethodName}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, // $DataView instance methods return object (boxed BigInteger)
            [_types.Object, _types.Int32, _types.Boolean]
        );

        MethodBuilder target = runtimeMethodName switch
        {
            "GetBigInt64" => runtime.DataViewGetBigInt64,
            "GetBigUint64" => runtime.DataViewGetBigUint64,
            _ => throw new ArgumentException($"Unknown DataView BigInt getter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        switch (jsMethodName)
        {
            case "getBigInt64": runtime.TSDataViewGetBigInt64 = method; break;
            case "getBigUint64": runtime.TSDataViewGetBigUint64 = method; break;
        }
    }

    private void EmitDataViewSetter(TypeBuilder runtimeType, EmittedRuntime runtime, string runtimeMethodName, string jsMethodName, bool hasEndianness)
    {
        var paramTypes = hasEndianness
            ? new[] { _types.Object, _types.Int32, _types.Object, _types.Boolean }
            : new[] { _types.Object, _types.Int32, _types.Object };

        var method = runtimeType.DefineMethod(
            $"DataView{runtimeMethodName}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            paramTypes
        );

        MethodBuilder target = runtimeMethodName switch
        {
            "SetInt8" => runtime.DataViewSetInt8,
            "SetUint8" => runtime.DataViewSetUint8,
            "SetInt16" => runtime.DataViewSetInt16,
            "SetUint16" => runtime.DataViewSetUint16,
            "SetInt32" => runtime.DataViewSetInt32,
            "SetUint32" => runtime.DataViewSetUint32,
            "SetFloat32" => runtime.DataViewSetFloat32,
            "SetFloat64" => runtime.DataViewSetFloat64,
            "SetBigInt64" => runtime.DataViewSetBigInt64,
            "SetBigUint64" => runtime.DataViewSetBigUint64,
            _ => throw new ArgumentException($"Unknown DataView setter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        if (hasEndianness)
            il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        // Store in runtime
        switch (jsMethodName)
        {
            case "setInt8": runtime.TSDataViewSetInt8 = method; break;
            case "setUint8": runtime.TSDataViewSetUint8 = method; break;
            case "setInt16": runtime.TSDataViewSetInt16 = method; break;
            case "setUint16": runtime.TSDataViewSetUint16 = method; break;
            case "setInt32": runtime.TSDataViewSetInt32 = method; break;
            case "setUint32": runtime.TSDataViewSetUint32 = method; break;
            case "setFloat32": runtime.TSDataViewSetFloat32 = method; break;
            case "setFloat64": runtime.TSDataViewSetFloat64 = method; break;
            case "setBigInt64": runtime.TSDataViewSetBigInt64 = method; break;
            case "setBigUint64": runtime.TSDataViewSetBigUint64 = method; break;
        }
    }

    /// <summary>
    /// Emits helpers for creating TypedArrays.
    /// Uses reflection-based late-binding to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private void EmitTypedArrayHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Helper method for each TypedArray type - use type names instead of typeof()
        EmitTypedArrayHelper(runtimeType, runtime, "Int8Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Uint8Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Uint8ClampedArray");
        EmitTypedArrayHelper(runtimeType, runtime, "Int16Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Uint16Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Int32Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Uint32Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Float32Array");
        EmitTypedArrayHelper(runtimeType, runtime, "Float64Array");
        EmitTypedArrayHelper(runtimeType, runtime, "BigInt64Array");
        EmitTypedArrayHelper(runtimeType, runtime, "BigUint64Array");

        // Get typed array element helper
        EmitTypedArrayGetHelper(runtimeType, runtime);
        EmitTypedArraySetHelper(runtimeType, runtime);

        // General-purpose TypedArray creation from object
        EmitTypedArrayFromObjectHelpers(runtimeType, runtime);

    }

    /// <summary>
    /// Emits TypedArray detection and access helpers that don't depend on SharpTS.dll.
    /// These are called early in the emission order, before GetIndex/SetIndex.
    /// </summary>
    public void EmitTypedArrayDetectionHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // IsTypedArray is always emitted (GetProperty's central dispatch may
        // call it, and tree-shaking the GetProperty arm itself was already done).
        // The body is gated on HasAnyTypedArray inside the helper — when no
        // typed-array type was emitted, IsTypedArray just returns false.
        EmitIsTypedArrayHelper(runtimeType, runtime);
        if (_features.HasAnyTypedArray)
        {
            EmitGetTypedArrayElementHelper(runtimeType, runtime);
            EmitSetTypedArrayElementHelper(runtimeType, runtime);
            EmitGetTypedArrayMemberHelper(runtimeType, runtime);
        }
    }

    /// <summary>
    /// Emits a helper that checks if an object is a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitIsTypedArrayHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "IsTypedArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.IsTypedArrayMethod = method;

        var il = method.GetILGenerator();
        var falseNullObjLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // if (obj == null) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseNullObjLabel);

        // First check if it's an emitted $TypedArray type. When tree-shaking has
        // gated typed arrays off, $TypedArray base type was never emitted —
        // skip the check (the helper just always returns false).
        if (_features.HasAnyTypedArray)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Brtrue, trueLabel);
        }

        // Handle null obj case (stack is empty)
        il.MarkLabel(falseNullObjLabel);
        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that gets an element from a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitGetTypedArrayElementHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "GetTypedArrayElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32]
        );
        runtime.GetTypedArrayElementMethod = method;

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        il.Emit(OpCodes.Ldstr, "TypedArray element access requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that sets an element in a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitSetTypedArrayElementHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTypedArrayElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Int32, _types.Object]
        );
        runtime.SetTypedArrayElementMethod = method;

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        il.Emit(OpCodes.Ldstr, "TypedArray element assignment requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that gets a member from a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitGetTypedArrayMemberHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "GetTypedArrayMember",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetTypedArrayMemberMethod = method;

        var il = method.GetILGenerator();
        var typedArrayBytesPerElementGetter = _types.GetMethod(runtime.TypedArrayBaseType, "get_BytesPerElement");

        var endLabel = il.DefineLabel();
        var checkByteLengthLabel = il.DefineLabel();
        var checkByteOffsetLabel = il.DefineLabel();
        var checkBufferLabel = il.DefineLabel();
        var checkBytesPerElementLabel = il.DefineLabel();
        var checkMethodsLabel = il.DefineLabel();
        var buildWrapperLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();

        // Check if object is an emitted $TypedArray
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // It's an emitted TypedArray - check property name
        // Check "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkByteLengthLabel);

        // Return length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkByteLengthLabel);
        // Check "byteLength"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "byteLength");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkByteOffsetLabel);

        // Return byteLength
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkByteOffsetLabel);
        // Check "byteOffset"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "byteOffset");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkBufferLabel);

        // Return byteOffset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteOffsetGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkBufferLabel);
        // Check "buffer"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "buffer");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkBytesPerElementLabel);

        // Return buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayBufferGetter);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkBytesPerElementLabel);
        // Check "BYTES_PER_ELEMENT"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "BYTES_PER_ELEMENT");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkMethodsLabel);

        // Return BYTES_PER_ELEMENT (call abstract BytesPerElement property)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Callvirt, typedArrayBytesPerElementGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, endLabel);

        // Bulk-method names (#940): return a $BoundTypedArrayMethod bound to (this, name); it is
        // dispatched through InvokeMethodValue/InvokeValue. Mirrors the interpreter's GetMember.
        il.MarkLabel(checkMethodsLabel);
        foreach (var methodName in new[]
                 {
                     "fill", "set", "copyWithin", "reverse", "slice", "subarray",
                     "indexOf", "lastIndexOf", "includes", "join", "toString"
                 })
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, buildWrapperLabel);
        }
        il.Emit(OpCodes.Br, returnNullLabel);

        il.MarkLabel(buildWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.BoundTypedArrayMethodCtor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(returnNullLabel);
        // Unknown property - return null (undefined)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits helpers for creating TypedArrays from an object argument (number or SharedArrayBuffer).
    /// Uses reflection-based late-binding to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private void EmitTypedArrayFromObjectHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int8Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint8Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint8ClampedArray");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int16Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint16Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int32Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint32Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Float32Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Float64Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "BigInt64Array");
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "BigUint64Array");
    }

    /// <summary>
    /// Emits a helper that creates a TypedArray from an object (either a number for length, SharedArrayBuffer, or ArrayBuffer).
    /// Uses emitted pure-IL types for standalone DLL support.
    /// </summary>
    private void EmitTypedArrayFromObjectHelper(TypeBuilder runtimeType, EmittedRuntime runtime, string name)
    {
        // Get the emitted TypedArray constructors
        var (lengthCtor, bufferCtor) = GetEmittedTypedArrayCtors(runtime, name);

        // Create{name}FromObject(object arg) - handles number, SharedArrayBuffer, or ArrayBuffer
        var method = runtimeType.DefineMethod(
            $"Create{name}FromObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        var endLabel = il.DefineLabel();
        var isEmittedSharedArrayBufferLabel = il.DefineLabel();
        var isTSArrayLabel = il.DefineLabel();
        var isTypedArrayLabel = il.DefineLabel();
        var isNumberLabel = il.DefineLabel();
        var unsupportedTypeLabel = il.DefineLabel();
        var argNotNullLabel = il.DefineLabel();

        // Check if arg is null - create with length 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, argNotNullLabel);

        // Arg is null - create with length 0 using emitted length constructor
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, lengthCtor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(argNotNullLabel);

        // Check if arg is $ArrayBuffer (emitted type)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        il.Emit(OpCodes.Brfalse, isEmittedSharedArrayBufferLabel);

        // It's $ArrayBuffer - use emitted buffer constructor
        il.Emit(OpCodes.Ldarg_0);  // buffer
        il.Emit(OpCodes.Ldc_I4_0);  // byteOffset = 0
        var nullableIntLocal = il.DeclareLocal(typeof(int?));
        il.Emit(OpCodes.Ldloca, nullableIntLocal);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, nullableIntLocal);  // length = null
        il.Emit(OpCodes.Newobj, bufferCtor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(isEmittedSharedArrayBufferLabel);

        // Check if arg is $SharedArrayBuffer (emitted type)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brfalse, isTSArrayLabel);

        // It's $SharedArrayBuffer - use emitted buffer constructor
        il.Emit(OpCodes.Ldarg_0);  // buffer
        il.Emit(OpCodes.Ldc_I4_0);  // byteOffset = 0
        il.Emit(OpCodes.Ldloca, nullableIntLocal);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, nullableIntLocal);  // length = null
        il.Emit(OpCodes.Newobj, bufferCtor);
        il.Emit(OpCodes.Br, endLabel);

        // Check if arg is $Array (JS array literal like [1, 2, 3])
        il.MarkLabel(isTSArrayLabel);
        {
            var loopStartLabel = il.DefineLabel();
            var loopDoneLabel = il.DefineLabel();
            var tsArrLocal = il.DeclareLocal(_types.Object);
            var arrLengthLocal = il.DeclareLocal(_types.Int32);
            var arrResultLocal = il.DeclareLocal(_types.Object);
            var arrElemLocal = il.DeclareLocal(_types.Object);
            var arrILocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSArrayType);
            il.Emit(OpCodes.Brfalse, isTypedArrayLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Stloc, tsArrLocal);
            il.Emit(OpCodes.Ldloc, tsArrLocal);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayLengthGetter);
            il.Emit(OpCodes.Stloc, arrLengthLocal);
            il.Emit(OpCodes.Ldloc, arrLengthLocal);
            il.Emit(OpCodes.Newobj, lengthCtor);
            il.Emit(OpCodes.Stloc, arrResultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, arrILocal);
            il.MarkLabel(loopStartLabel);
            il.Emit(OpCodes.Ldloc, arrILocal);
            il.Emit(OpCodes.Ldloc, arrLengthLocal);
            il.Emit(OpCodes.Bge, loopDoneLabel);
            il.Emit(OpCodes.Ldloc, tsArrLocal);
            il.Emit(OpCodes.Ldloc, arrILocal);
            il.Emit(OpCodes.Call, runtime.GetElement);
            il.Emit(OpCodes.Stloc, arrElemLocal);
            il.Emit(OpCodes.Ldloc, arrResultLocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Ldloc, arrILocal);
            il.Emit(OpCodes.Ldloc, arrElemLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
            il.Emit(OpCodes.Ldloc, arrILocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, arrILocal);
            il.Emit(OpCodes.Br, loopStartLabel);
            il.MarkLabel(loopDoneLabel);
            il.Emit(OpCodes.Ldloc, arrResultLocal);
            il.Emit(OpCodes.Br, endLabel);
        }

        // Check if arg is $TypedArray (copy constructor from another typed array)
        il.MarkLabel(isTypedArrayLabel);
        {
            var loopStartLabel = il.DefineLabel();
            var loopDoneLabel = il.DefineLabel();
            var srcTALocal = il.DeclareLocal(_types.Object);
            var taLengthLocal = il.DeclareLocal(_types.Int32);
            var taResultLocal = il.DeclareLocal(_types.Object);
            var taElemLocal = il.DeclareLocal(_types.Object);
            var taILocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Brfalse, isNumberLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Stloc, srcTALocal);
            il.Emit(OpCodes.Ldloc, srcTALocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
            il.Emit(OpCodes.Stloc, taLengthLocal);
            il.Emit(OpCodes.Ldloc, taLengthLocal);
            il.Emit(OpCodes.Newobj, lengthCtor);
            il.Emit(OpCodes.Stloc, taResultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, taILocal);
            il.MarkLabel(loopStartLabel);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldloc, taLengthLocal);
            il.Emit(OpCodes.Bge, loopDoneLabel);
            il.Emit(OpCodes.Ldloc, srcTALocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
            il.Emit(OpCodes.Stloc, taElemLocal);
            il.Emit(OpCodes.Ldloc, taResultLocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldloc, taElemLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, taILocal);
            il.Emit(OpCodes.Br, loopStartLabel);
            il.MarkLabel(loopDoneLabel);
            il.Emit(OpCodes.Ldloc, taResultLocal);
            il.Emit(OpCodes.Br, endLabel);
        }

        il.MarkLabel(isNumberLabel);

        // Check if it's a number (Double, Int32, etc.) - create with that length
        // First check if it's not an array buffer type by checking type name
        var argTypeLocal = il.DeclareLocal(_types.Type);
        var argTypeNameLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, argTypeLocal);
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "get_FullName"));
        il.Emit(OpCodes.Stloc, argTypeNameLocal);

        // Check if it contains "ArrayBuffer" (interpreter types)
        il.Emit(OpCodes.Ldloc, argTypeNameLocal);
        il.Emit(OpCodes.Ldstr, "ArrayBuffer");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.String));
        il.Emit(OpCodes.Brtrue, unsupportedTypeLabel);

        // Not a buffer - treat as length, use emitted length constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, lengthCtor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(unsupportedTypeLabel);
        il.Emit(OpCodes.Ldstr, "TypedArray constructor requires emitted ArrayBuffer/SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        // Store the helper for use by ILEmitter
        runtime.TypedArrayFromObjectHelpers[name] = method;
    }

    /// <summary>
    /// Gets the emitted TypedArray constructors for the given type name.
    /// </summary>
    private (ConstructorBuilder lengthCtor, ConstructorBuilder bufferCtor) GetEmittedTypedArrayCtors(EmittedRuntime runtime, string name)
    {
        return name switch
        {
            "Int8Array" => (runtime.Int8ArrayLengthCtor, runtime.Int8ArrayBufferCtor),
            "Uint8Array" => (runtime.Uint8ArrayLengthCtor, runtime.Uint8ArrayBufferCtor),
            "Uint8ClampedArray" => (runtime.Uint8ClampedArrayLengthCtor, runtime.Uint8ClampedArrayBufferCtor),
            "Int16Array" => (runtime.Int16ArrayLengthCtor, runtime.Int16ArrayBufferCtor),
            "Uint16Array" => (runtime.Uint16ArrayLengthCtor, runtime.Uint16ArrayBufferCtor),
            "Int32Array" => (runtime.Int32ArrayLengthCtor, runtime.Int32ArrayBufferCtor),
            "Uint32Array" => (runtime.Uint32ArrayLengthCtor, runtime.Uint32ArrayBufferCtor),
            "Float32Array" => (runtime.Float32ArrayLengthCtor, runtime.Float32ArrayBufferCtor),
            "Float64Array" => (runtime.Float64ArrayLengthCtor, runtime.Float64ArrayBufferCtor),
            "BigInt64Array" => (runtime.BigInt64ArrayLengthCtor, runtime.BigInt64ArrayBufferCtor),
            "BigUint64Array" => (runtime.BigUint64ArrayLengthCtor, runtime.BigUint64ArrayBufferCtor),
            _ => throw new ArgumentException($"Unknown TypedArray type: {name}")
        };
    }

    private void EmitTypedArrayHelper(TypeBuilder runtimeType, EmittedRuntime runtime, string name)
    {
        // Get the emitted TypedArray constructors
        var (lengthCtor, bufferCtor) = GetEmittedTypedArrayCtors(runtime, name);

        // Create from length: CreateInt8Array(double length)
        // Uses emitted pure-IL types for standalone DLLs
        var methodFromLength = runtimeType.DefineMethod(
            $"Create{name}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double]
        );

        var il = methodFromLength.GetILGenerator();

        // Use emitted length constructor directly
        il.Emit(OpCodes.Ldarg_0);  // length (double)
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, lengthCtor);
        il.Emit(OpCodes.Ret);

        // Create from SharedArrayBuffer: CreateInt8ArrayFromSAB(object sab, double byteOffset, object length)
        // Uses emitted types for emitted buffer types, falls back to reflection for interpreter types
        var methodFromSAB = runtimeType.DefineMethod(
            $"Create{name}FromSAB",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var ilSAB = methodFromSAB.GetILGenerator();
        var endLabel = ilSAB.DefineLabel();
        var isEmittedArrayBufferLabel = ilSAB.DefineLabel();
        var isEmittedSharedArrayBufferLabel = ilSAB.DefineLabel();
        var unsupportedTypeLabel = ilSAB.DefineLabel();

        // Declare nullable int local for emitted buffer constructor
        var nullableIntLocal = ilSAB.DeclareLocal(typeof(int?));

        // Check if sab is $ArrayBuffer (emitted type)
        ilSAB.Emit(OpCodes.Ldarg_0);
        ilSAB.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        ilSAB.Emit(OpCodes.Brfalse, isEmittedSharedArrayBufferLabel);

        // It's $ArrayBuffer - use emitted buffer constructor
        ilSAB.Emit(OpCodes.Ldarg_0);  // buffer
        ilSAB.Emit(OpCodes.Ldarg_1);  // byteOffset (double)
        ilSAB.Emit(OpCodes.Conv_I4);
        // Handle nullable length: arg2 is object, convert to int?
        var hasLengthLabel1 = ilSAB.DefineLabel();
        var afterLength1 = ilSAB.DefineLabel();
        ilSAB.Emit(OpCodes.Ldarg_2);
        ilSAB.Emit(OpCodes.Brfalse, hasLengthLabel1);
        // length is not null
        ilSAB.Emit(OpCodes.Ldarg_2);
        ilSAB.Emit(OpCodes.Unbox_Any, _types.Double);
        ilSAB.Emit(OpCodes.Conv_I4);
        ilSAB.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        ilSAB.Emit(OpCodes.Br, afterLength1);
        ilSAB.MarkLabel(hasLengthLabel1);
        // length is null - use default int? (null)
        ilSAB.Emit(OpCodes.Ldloca, nullableIntLocal);
        ilSAB.Emit(OpCodes.Initobj, typeof(int?));
        ilSAB.Emit(OpCodes.Ldloc, nullableIntLocal);
        ilSAB.MarkLabel(afterLength1);
        ilSAB.Emit(OpCodes.Newobj, bufferCtor);
        ilSAB.Emit(OpCodes.Br, endLabel);

        ilSAB.MarkLabel(isEmittedSharedArrayBufferLabel);

        // Check if sab is $SharedArrayBuffer (emitted type)
        ilSAB.Emit(OpCodes.Ldarg_0);
        ilSAB.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        ilSAB.Emit(OpCodes.Brfalse, unsupportedTypeLabel);

        // It's $SharedArrayBuffer - use emitted buffer constructor
        ilSAB.Emit(OpCodes.Ldarg_0);  // buffer
        ilSAB.Emit(OpCodes.Ldarg_1);  // byteOffset (double)
        ilSAB.Emit(OpCodes.Conv_I4);
        // Handle nullable length
        var hasLengthLabel2 = ilSAB.DefineLabel();
        var afterLength2 = ilSAB.DefineLabel();
        ilSAB.Emit(OpCodes.Ldarg_2);
        ilSAB.Emit(OpCodes.Brfalse, hasLengthLabel2);
        ilSAB.Emit(OpCodes.Ldarg_2);
        ilSAB.Emit(OpCodes.Unbox_Any, _types.Double);
        ilSAB.Emit(OpCodes.Conv_I4);
        ilSAB.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        ilSAB.Emit(OpCodes.Br, afterLength2);
        ilSAB.MarkLabel(hasLengthLabel2);
        ilSAB.Emit(OpCodes.Ldloca, nullableIntLocal);
        ilSAB.Emit(OpCodes.Initobj, typeof(int?));
        ilSAB.Emit(OpCodes.Ldloc, nullableIntLocal);
        ilSAB.MarkLabel(afterLength2);
        ilSAB.Emit(OpCodes.Newobj, bufferCtor);
        ilSAB.Emit(OpCodes.Br, endLabel);

        // Non-emitted buffers are not supported in standalone mode.
        ilSAB.MarkLabel(unsupportedTypeLabel);
        ilSAB.Emit(OpCodes.Ldstr, "TypedArray buffer constructor requires emitted ArrayBuffer/SharedArrayBuffer.");
        ilSAB.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        ilSAB.Emit(OpCodes.Throw);

        ilSAB.MarkLabel(endLabel);
        ilSAB.Emit(OpCodes.Ret);

        // Store the helper for use by ILEmitter
        runtime.TypedArrayFromBufferHelpers[name] = methodFromSAB;
    }

    private void EmitTypedArrayGetHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // public static object TypedArrayGet(object typedArray, double index)
        var method = runtimeType.DefineMethod(
            "TypedArrayGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double]
        );

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "TypedArray get requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Ret);

        _ = method;
    }

    private void EmitTypedArraySetHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // public static void TypedArraySet(object typedArray, double index, object value)
        var method = runtimeType.DefineMethod(
            "TypedArraySet",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "TypedArray set requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        il.Emit(OpCodes.Ret);

        _ = method;
    }

    /// <summary>
    /// Emits Worker constructor helper.
    /// Uses direct constructor invocation.
    /// </summary>
    private void EmitWorkerHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // CreateWorker(string filename, object? options, object? parentInterpreter)
        //
        // Constructs a SharpTSWorker via reflection (keeping the standalone DLL
        // free of a hard SharpTS.dll reference) and binds the emitted $EventLoop
        // singleton's Ref/Unref as the worker's keep-alive handle, so a running
        // worker holds the compiled event loop open by default — Node semantics,
        // and the compiled-mode half of the #329 premature-exit fix (#354). The
        // worker itself runs an interpreter for its child script, so SharpTS.dll
        // must be co-located; the emit site records that via RequireSharpTSRuntime.
        var method = runtimeType.DefineMethod(
            "CreateWorker",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        var typeLocal = il.DeclareLocal(_types.Type);
        var loopLocal = il.DeclareLocal(runtime.EventLoopType);
        var refLocal = il.DeclareLocal(typeof(Action));
        var unrefLocal = il.DeclareLocal(typeof(Action));
        var scheduleLocal = il.DeclareLocal(typeof(Action<Action>));
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var actionCtor = typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!;
        var actionOfActionCtor = typeof(Action<Action>).GetConstructor([_types.Object, typeof(IntPtr)])!;

        // Type t = Type.GetType("SharpTS.Runtime.Types.SharpTSWorker, SharpTS");
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSWorker, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, typeLocal);

        // if (t == null) throw — SharpTS.dll must be co-located for Worker.
        var typeOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, typeOk);
        il.Emit(OpCodes.Ldstr, "Worker requires the SharpTS runtime (SharpTS.dll) to be present. " +
                               "Compile without --standalone so it is co-located with the output.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(typeOk);

        // var loop = $EventLoop.GetInstance();
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Stloc, loopLocal);

        // Action ref = new Action(loop, $EventLoop.Ref);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopRef);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, refLocal);

        // Action unref = new Action(loop, $EventLoop.Unref);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopUnref);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, unrefLocal);

        // Action<Action> schedule = new Action<Action>(loop, $EventLoop.Schedule);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopSchedule);
        il.Emit(OpCodes.Newobj, actionOfActionCtor);
        il.Emit(OpCodes.Stloc, scheduleLocal);

        // object[] args = { filename, options, ref, unref, schedule };
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); // filename
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1); // options
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, refLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldloc, unrefLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloc, scheduleLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // return t.GetMethod("CreateForCompiledLoop").Invoke(null, args);
        // (arg 2, parentInterpreter, is unused in compiled mode — always null.)
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "CreateForCompiledLoop");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        runtime.TSWorkerType = _types.Object;
        runtime.TSWorkerCtor = method;
    }

    /// <summary>
    /// Emits StructuredClone helper.
    /// Accepts either null, a SharpTSArray (transfer list), or a SharpTSObject with { transfer: [...] }.
    /// Uses direct calls.
    /// </summary>
    private void EmitStructuredCloneHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var cloneCore = runtimeType.DefineMethod(
            "StructuredCloneCore",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var coreIl = cloneCore.GetILGenerator();
        var sourceListLocal = coreIl.DeclareLocal(_types.ListOfObject);
        var clonedListLocal = coreIl.DeclareLocal(_types.ListOfObject);
        var sourceDictStringLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);
        var clonedDictStringLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);
        var sourceDictObjectLocal = coreIl.DeclareLocal(_types.DictionaryObjectObject);
        var clonedDictObjectLocal = coreIl.DeclareLocal(_types.DictionaryObjectObject);
        var sourceSetLocal = coreIl.DeclareLocal(_types.HashSetOfObject);
        var clonedSetLocal = coreIl.DeclareLocal(_types.HashSetOfObject);
        var indexLocal = coreIl.DeclareLocal(_types.Int32);
        var valueLocal = coreIl.DeclareLocal(_types.Object);
        var keyLocal = coreIl.DeclareLocal(_types.Object);
        var dictEnumLocal = coreIl.DeclareLocal(_types.IDictionaryEnumerator);
        var setEnumLocal = coreIl.DeclareLocal(_types.IEnumerator);
        var currentLocal = coreIl.DeclareLocal(_types.Object);

        // $Object (plain object literal, possibly with getters/setters) — clone via its
        // Fields dictionary (#1255).
        var objLiteralSourceLocal = coreIl.DeclareLocal(runtime.TSObjectType);
        var objLiteralClonedFieldsLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);

        // $Error hierarchy — clone via name-based reconstruction + Stack preservation (#1255).
        var errSourceLocal = coreIl.DeclareLocal(runtime.TSErrorType);
        var errNameLocal = coreIl.DeclareLocal(_types.String);
        var errMsgLocal = coreIl.DeclareLocal(_types.String);
        var clonedErrLocal = coreIl.DeclareLocal(runtime.TSErrorType);

        var nextCheckNull = coreIl.DefineLabel();
        var checkSharedArrayBuffer = coreIl.DefineLabel();
        var checkArrayBuffer = coreIl.DefineLabel();
        var checkTypedArray = coreIl.DefineLabel();
        var checkList = coreIl.DefineLabel();
        var checkStringDict = coreIl.DefineLabel();
        var checkObjectLiteral = coreIl.DefineLabel();
        var checkObjectDict = coreIl.DefineLabel();
        var checkSet = coreIl.DefineLabel();
        var checkDate = coreIl.DefineLabel();
        var checkRegExp = coreIl.DefineLabel();
        var checkBuffer = coreIl.DefineLabel();
        var checkError = coreIl.DefineLabel();
        var fallbackReturn = coreIl.DefineLabel();
        var returnClonedList = coreIl.DefineLabel();
        var returnClonedStringDict = coreIl.DefineLabel();
        var returnClonedObjectDict = coreIl.DefineLabel();
        var returnClonedSet = coreIl.DefineLabel();
        var listLoopCheck = coreIl.DefineLabel();
        var listLoopBody = coreIl.DefineLabel();
        var stringDictLoopCheck = coreIl.DefineLabel();
        var stringDictLoopBody = coreIl.DefineLabel();
        var objectDictLoopCheck = coreIl.DefineLabel();
        var objectDictLoopBody = coreIl.DefineLabel();
        var setLoopCheck = coreIl.DefineLabel();
        var setLoopBody = coreIl.DefineLabel();

        // if (value == null) return null;
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Brtrue, nextCheckNull);
        coreIl.Emit(OpCodes.Ldnull);
        coreIl.Emit(OpCodes.Ret);
        coreIl.MarkLabel(nextCheckNull);

        // Primitives (number/string/boolean/bigint) and the `undefined` singleton are
        // immutable/shared — return as-is, no cloning needed. Matches the interpreter's
        // early `value is double or string or bool or BigInteger` return in CloneInternal
        // (#1255) — without this, throwing-on-fallback below would wrongly reject every
        // clone of a container holding a boxed number/string/bool/bigint/undefined value.
        var checkPrimitiveString = coreIl.DefineLabel();
        var checkPrimitiveBool = coreIl.DefineLabel();
        var checkPrimitiveBigInt = coreIl.DefineLabel();
        var checkPrimitiveUndefined = coreIl.DefineLabel();

        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.Double);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveString);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveString);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.String);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveBool);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveBool);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.Boolean);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveBigInt);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveBigInt);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.BigInteger);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveUndefined);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveUndefined);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, runtime.UndefinedType);
        coreIl.Emit(OpCodes.Brfalse, checkSharedArrayBuffer);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        // SharedArrayBuffer is transferred by reference. Skip when typed
        // arrays aren't emitted — no SharedArrayBuffer values can exist.
        coreIl.MarkLabel(checkSharedArrayBuffer);
        if (_features.HasAnyTypedArray)
        {
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
            coreIl.Emit(OpCodes.Brfalse, checkArrayBuffer);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            // Always fall through to the next check.
            coreIl.Emit(OpCodes.Br, checkArrayBuffer);
        }

        // $ArrayBuffer / $TypedArray — deep clone (#1255). A non-shared buffer is copied
        // independently; a TypedArray view backed by a SharedArrayBuffer clones to a NEW
        // VIEW over the SAME buffer (sharing is the entire point of SharedArrayBuffer,
        // matching the interpreter's CreateTypedArrayView for the shared case).
        coreIl.MarkLabel(checkArrayBuffer);
        if (_features.HasAnyTypedArray)
        {
            var abLocal = coreIl.DeclareLocal(runtime.ArrayBufferType);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
            coreIl.Emit(OpCodes.Stloc, abLocal);
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Brfalse, checkTypedArray);

            // return source.Slice(0, source.ByteLength)
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.ArrayBufferByteLengthGetter);
            coreIl.Emit(OpCodes.Callvirt, runtime.ArrayBufferSlice);
            coreIl.Emit(OpCodes.Ret);

            coreIl.MarkLabel(checkTypedArray);
            var taLocal = coreIl.DeclareLocal(runtime.TypedArrayBaseType);
            var taSharedLabel = coreIl.DefineLabel();
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
            coreIl.Emit(OpCodes.Stloc, taLocal);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Brfalse, checkList);

            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.TypedArrayBufferGetter);
            coreIl.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
            coreIl.Emit(OpCodes.Brtrue, taSharedLabel);

            // return source.Slice(0, source.Length) — independent copy
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
            coreIl.Emit(OpCodes.Callvirt, runtime.TypedArraySlice);
            coreIl.Emit(OpCodes.Ret);

            // return source.Subarray(0, source.Length) — shares the SharedArrayBuffer
            coreIl.MarkLabel(taSharedLabel);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
            coreIl.Emit(OpCodes.Callvirt, runtime.TypedArraySubarray);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkList);
        }

        // List<object> deep clone.
        coreIl.MarkLabel(checkList);
        // number[] unboxing: materialize a numeric-mode $Array before deep-cloning its base list.
        EmitDeoptArgIfNumericArray(coreIl, runtime, 0);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.ListOfObject);
        coreIl.Emit(OpCodes.Stloc, sourceListLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Brfalse, checkStringDict);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        coreIl.Emit(OpCodes.Stloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ldc_I4_0);
        coreIl.Emit(OpCodes.Stloc, indexLocal);
        coreIl.Emit(OpCodes.Br, listLoopCheck);

        coreIl.MarkLabel(listLoopBody);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);
        coreIl.Emit(OpCodes.Ldloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Ldc_I4_1);
        coreIl.Emit(OpCodes.Add);
        coreIl.Emit(OpCodes.Stloc, indexLocal);

        coreIl.MarkLabel(listLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        coreIl.Emit(OpCodes.Blt, listLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedList);

        // Dictionary<string, object> deep clone.
        coreIl.MarkLabel(checkStringDict);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        coreIl.Emit(OpCodes.Stloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Brfalse, checkObjectLiteral);

        coreIl.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        coreIl.Emit(OpCodes.Stloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IDictionary);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Br, stringDictLoopCheck);

        coreIl.MarkLabel(stringDictLoopBody);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Key"));
        coreIl.Emit(OpCodes.Stloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Value"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);

        coreIl.Emit(OpCodes.Ldloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, keyLocal);
        coreIl.Emit(OpCodes.Castclass, _types.String);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        coreIl.MarkLabel(stringDictLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, stringDictLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedStringDict);

        // $Object (object literal, possibly with getters/setters not present on a plain
        // Dictionary<string,object?>) — clone its Fields dict via a recursive cloneCore
        // call (reuses the Dictionary<string,object?> branch above), then rebuild (#1255).
        // Getters/setters are not preserved — matches the interpreter's CloneObject, which
        // only copies data fields.
        coreIl.MarkLabel(checkObjectLiteral);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, runtime.TSObjectType);
        coreIl.Emit(OpCodes.Stloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Ldloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Brfalse, checkObjectDict);

        coreIl.Emit(OpCodes.Ldloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        coreIl.Emit(OpCodes.Stloc, objLiteralClonedFieldsLocal);
        coreIl.Emit(OpCodes.Ldloc, objLiteralClonedFieldsLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        coreIl.Emit(OpCodes.Ret);

        // Dictionary<object, object> deep clone (Map backing store).
        coreIl.MarkLabel(checkObjectDict);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        coreIl.Emit(OpCodes.Stloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Brfalse, checkSet);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryObjectObject));
        coreIl.Emit(OpCodes.Stloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IDictionary);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Br, objectDictLoopCheck);

        coreIl.MarkLabel(objectDictLoopBody);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Key"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Value"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);

        coreIl.Emit(OpCodes.Ldloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.DictionaryObjectObjectSetItem);

        coreIl.MarkLabel(objectDictLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, objectDictLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedObjectDict);

        // HashSet<object> deep clone.
        coreIl.MarkLabel(checkSet);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.HashSetOfObject);
        coreIl.Emit(OpCodes.Stloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Brfalse, checkDate);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.HashSetOfObject));
        coreIl.Emit(OpCodes.Stloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IEnumerable);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerable, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, setEnumLocal);
        coreIl.Emit(OpCodes.Br, setLoopCheck);

        coreIl.MarkLabel(setLoopBody);
        coreIl.Emit(OpCodes.Ldloc, setEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "get_Current"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, currentLocal);
        coreIl.Emit(OpCodes.Ldloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ldloc, currentLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfObject, "Add", _types.Object));
        coreIl.Emit(OpCodes.Pop);

        coreIl.MarkLabel(setLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, setEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, setLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedSet);

        // $TSDate — deep clone via epoch milliseconds (#1255).
        coreIl.MarkLabel(checkDate);
        if (_features.UsesDate)
        {
            var dateLocal = coreIl.DeclareLocal(runtime.TSDateType);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.TSDateType);
            coreIl.Emit(OpCodes.Stloc, dateLocal);
            coreIl.Emit(OpCodes.Ldloc, dateLocal);
            coreIl.Emit(OpCodes.Brfalse, checkRegExp);

            coreIl.Emit(OpCodes.Ldloc, dateLocal);
            coreIl.Emit(OpCodes.Call, _tsDateGetTimeMethod);
            coreIl.Emit(OpCodes.Newobj, runtime.TSDateCtorMilliseconds);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkRegExp);
        }

        // $RegExp — clone pattern + flags (#1255). Matches the interpreter's CloneInternal,
        // which likewise rebuilds from Source/Flags only (lastIndex is not preserved).
        coreIl.MarkLabel(checkRegExp);
        if (_features.UsesRegExp)
        {
            var regexpLocal = coreIl.DeclareLocal(runtime.TSRegExpType);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            coreIl.Emit(OpCodes.Stloc, regexpLocal);
            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Brfalse, checkBuffer);

            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
            coreIl.Emit(OpCodes.Newobj, runtime.TSRegExpCtorPatternFlags);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkBuffer);
        }

        // $Buffer — deep-copy bytes via the existing Buffer.from(buffer) factory (#1255).
        coreIl.MarkLabel(checkBuffer);
        if (_features.UsesBuffer)
        {
            var bufferLocal = coreIl.DeclareLocal(runtime.TSBufferType);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, runtime.TSBufferType);
            coreIl.Emit(OpCodes.Stloc, bufferLocal);
            coreIl.Emit(OpCodes.Ldloc, bufferLocal);
            coreIl.Emit(OpCodes.Brfalse, checkError);

            coreIl.Emit(OpCodes.Ldloc, bufferLocal);
            coreIl.Emit(OpCodes.Call, runtime.TSBufferFromBuffer);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkError);
        }

        // $Error hierarchy — clone via name-based reconstruction + Stack preservation
        // (#1255), mirroring the interpreter's CloneError. A user-defined class extending
        // Error (`class Foo extends Error`) compiles with $Error as its real CLR base
        // (ILCompiler.Classes.cs) but — like every user class — ALSO implements
        // $IHasFields, which built-in $Error/$TypeError/etc. instances never do. Excluding
        // that case here routes it to the generic uncloneable fallback below, matching the
        // interpreter's narrower CloneError (only the fixed Error/TypeError/RangeError/...
        // hierarchy — a SharpTSInstance user subclass takes a different, field-copying path
        // the compiled side does not replicate).
        coreIl.MarkLabel(checkError);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, runtime.TSErrorType);
        coreIl.Emit(OpCodes.Stloc, errSourceLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Brfalse, fallbackReturn);

        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        coreIl.Emit(OpCodes.Brtrue, fallbackReturn);

        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, runtime.TSErrorNameGetter);
        coreIl.Emit(OpCodes.Stloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, runtime.TSErrorMessageGetter);
        coreIl.Emit(OpCodes.Stloc, errMsgLocal);

        var strEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var isTypeErrorLabel = coreIl.DefineLabel();
        var isRangeErrorLabel = coreIl.DefineLabel();
        var isReferenceErrorLabel = coreIl.DefineLabel();
        var isSyntaxErrorLabel = coreIl.DefineLabel();
        var isURIErrorLabel = coreIl.DefineLabel();
        var isEvalErrorLabel = coreIl.DefineLabel();
        var defaultErrorLabel = coreIl.DefineLabel();
        var haveClonedErrLabel = coreIl.DefineLabel();

        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "TypeError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isTypeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "RangeError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isRangeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "ReferenceError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isReferenceErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "SyntaxError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isSyntaxErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "URIError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isURIErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "EvalError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isEvalErrorLabel);
        coreIl.Emit(OpCodes.Br, defaultErrorLabel);

        coreIl.MarkLabel(isTypeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isRangeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isReferenceErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSReferenceErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isSyntaxErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSSyntaxErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isURIErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSURIErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isEvalErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSEvalErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(defaultErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);

        coreIl.MarkLabel(haveClonedErrLabel);
        coreIl.Emit(OpCodes.Ldloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, runtime.TSErrorStackGetter);
        coreIl.Emit(OpCodes.Callvirt, runtime.TSErrorStackSetter);
        coreIl.Emit(OpCodes.Ldloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Ret);

        // Everything else — functions/closures, Symbols, class instances, Promises,
        // WeakMap/WeakSet, iterators/generators, EventEmitters, etc. — cannot be
        // structured-cloned (#1255). Throw rather than alias by reference, matching the
        // interpreter's exhaustive switch (default arm: "Cannot clone value of type X").
        coreIl.MarkLabel(fallbackReturn);

        // A value whose CLR type is NOT defined in this dynamically-emitted assembly is
        // foreign — most commonly a SharpTS.dll interpreter runtime value (SharpTSObject/
        // SharpTSArray/etc.) crossing in via CompiledMessagePortBridge, which already
        // structured-clones on the interpreter side before handing off to this (compiled)
        // port's PostMessage (see CompiledMessagePortBridge.PostMessage's "pass-through
        // no-op for interpreter-shaped values" comment). Pass it through unchanged rather
        // than throwing — only OUR OWN uncloneable constructs ($TSFunction, $TSSymbol,
        // class instances, $Promise, etc., all defined in this assembly) should throw.
        var sameAssemblyLabel = coreIl.DefineLabel();
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly").GetGetMethod()!);
        coreIl.Emit(OpCodes.Ldtoken, runtimeType);
        coreIl.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly").GetGetMethod()!);
        coreIl.Emit(OpCodes.Ceq);
        coreIl.Emit(OpCodes.Brtrue, sameAssemblyLabel);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(sameAssemblyLabel);
        coreIl.Emit(OpCodes.Ldstr, "Cannot clone value of type ");
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        coreIl.Emit(OpCodes.Call, _types.StringConcat2);
        coreIl.Emit(OpCodes.Newobj, runtime.TSDataCloneErrorCtor);
        coreIl.Emit(OpCodes.Throw);

        coreIl.MarkLabel(returnClonedList);
        coreIl.Emit(OpCodes.Ldloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedStringDict);
        coreIl.Emit(OpCodes.Ldloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedObjectDict);
        coreIl.Emit(OpCodes.Ldloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedSet);
        coreIl.Emit(OpCodes.Ldloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ret);

        var method = runtimeType.DefineMethod(
            "StructuredClone",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, cloneCore);
        il.Emit(OpCodes.Ret);

        runtime.StructuredCloneClone = method;
    }

    // WorkerThreadsReceiveMessageOnPort: defined during EmitWorkerThreadsModuleHelpers, body
    // emitted later by EmitWorkerThreadsReceiveMessageOnPortBody once $MessagePort exists (#1077).
    private MethodBuilder _receiveMessageOnPortMethod = null!;

    /// <summary>
    /// Emits worker_threads module helper methods.
    /// Uses direct calls.
    /// </summary>
    private void EmitWorkerThreadsModuleHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // isMainThread getter
        var isMainThreadMethod = runtimeType.DefineMethod(
            "WorkerThreadsIsMainThread",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            Type.EmptyTypes
        );

        var il = isMainThreadMethod.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        runtime.WorkerThreadsIsMainThread = isMainThreadMethod;

        // threadId getter
        var threadIdMethod = runtimeType.DefineMethod(
            "WorkerThreadsThreadId",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );

        var il2 = threadIdMethod.GetILGenerator();
        il2.Emit(OpCodes.Ldc_R8, 0.0);
        il2.Emit(OpCodes.Ret);
        runtime.WorkerThreadsThreadId = threadIdMethod;

        // receiveMessageOnPort — synchronous main-thread drain (#1077). The method is DEFINED
        // here (so callers can bind runtime.WorkerThreadsReceiveMessageOnPort) but its body is
        // emitted later by EmitWorkerThreadsReceiveMessageOnPortBody, after EmitMessageChannelTypes
        // has created the $MessagePort type — this helper reads that type's _pending queue and
        // _closed/_cloneError fields, which don't exist at this point in emission. The $Runtime
        // type isn't finalized until EmitRuntimeClassFinalize, so filling the body afterward is safe.
        _receiveMessageOnPortMethod = runtimeType.DefineMethod(
            "WorkerThreadsReceiveMessageOnPort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.WorkerThreadsReceiveMessageOnPort = _receiveMessageOnPortMethod;

        // getEnvironmentData / setEnvironmentData — route to the C# per-process
        // WorkerEnvironmentData store via reflection (worker programs co-locate SharpTS.dll;
        // RequireSharpTSRuntime is recorded at the call sites in WorkerThreadsModuleEmitter so
        // a program that never calls these stays standalone). #1000.
        EmitWorkerThreadsEnvironmentData(runtimeType, runtime);
    }

    /// <summary>
    /// Emits the body of <c>WorkerThreadsReceiveMessageOnPort</c> (#1077). Must be called AFTER
    /// <see cref="EmitMessageChannelTypes"/> (so the <c>$MessagePort</c> type and its
    /// <c>_pending</c>/<c>_closed</c>/<c>_cloneError</c> fields exist) and BEFORE
    /// <see cref="EmitRuntimeClassFinalize"/> (which creates the <c>$Runtime</c> type).
    ///
    /// Synchronously drains one message from the port's own queue, matching
    /// <c>SharpTSMessagePort.ReceiveMessageSync</c>: returns <c>{ message }</c> when a value is
    /// queued, or <c>undefined</c> when the argument is not a port, the port is closed, or the
    /// queue is empty. A clone-failure sentinel dequeues as <c>{ message: undefined }</c>.
    /// </summary>
    private void EmitWorkerThreadsReceiveMessageOnPortBody(EmittedRuntime runtime)
    {
        var il = _receiveMessageOnPortMethod.GetILGenerator();
        var portLocal = il.DeclareLocal(_messagePortType);
        var msgLocal = il.DeclareLocal(_types.Object);
        var valueLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var undefinedLabel = il.DefineLabel();
        var afterMarkerLabel = il.DefineLabel();

        // port = arg0 as $MessagePort; if (port == null) return undefined
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _messagePortType);
        il.Emit(OpCodes.Stloc, portLocal);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Brfalse, undefinedLabel);

        // if (port._closed) return undefined
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ldfld, _messagePortClosedField);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // if (!port._pending.TryDequeue(out msg)) return undefined
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ldfld, _messagePortPendingField);
        il.Emit(OpCodes.Ldloca, msgLocal);
        il.Emit(OpCodes.Callvirt, _types.ConcurrentQueueOfObject.GetMethod("TryDequeue", [_types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, undefinedLabel);

        // value = msg; if (msg == _cloneError) value = undefined  (a queued clone-failure
        // sentinel has no cloneable payload — surface it as { message: undefined }).
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Ldsfld, _messagePortCloneErrorField);
        il.Emit(OpCodes.Bne_Un, afterMarkerLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(afterMarkerLabel);

        // return new Dictionary<string, object>() { ["message"] = value }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the getEnvironmentData/setEnvironmentData runtime helpers that reflectively call
    /// <c>SharpTS.Runtime.Types.WorkerEnvironmentData</c>.
    /// </summary>
    private void EmitWorkerThreadsEnvironmentData(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        const string storeType = "SharpTS.Runtime.Types.WorkerEnvironmentData, SharpTS";

        // public static object WorkerThreadsGetEnvironmentData(object key)
        //   => WorkerEnvironmentData.Get(key)   (null when absent — caller maps to undefined)
        var getMethod = runtimeType.DefineMethod(
            "WorkerThreadsGetEnvironmentData",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        var gil = getMethod.GetILGenerator();
        gil.Emit(OpCodes.Ldstr, storeType);
        gil.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        gil.Emit(OpCodes.Ldstr, "Get");
        gil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        gil.Emit(OpCodes.Ldnull); // static method — no instance
        gil.Emit(OpCodes.Ldc_I4_1);
        gil.Emit(OpCodes.Newarr, _types.Object);
        gil.Emit(OpCodes.Dup);
        gil.Emit(OpCodes.Ldc_I4_0);
        gil.Emit(OpCodes.Ldarg_0); // key
        gil.Emit(OpCodes.Stelem_Ref);
        gil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        gil.Emit(OpCodes.Ret);
        runtime.WorkerThreadsGetEnvironmentData = getMethod;

        // public static void WorkerThreadsSetEnvironmentData(object key, object value)
        //   => WorkerEnvironmentData.Set(key, value)
        var setMethod = runtimeType.DefineMethod(
            "WorkerThreadsSetEnvironmentData",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        var sil = setMethod.GetILGenerator();
        sil.Emit(OpCodes.Ldstr, storeType);
        sil.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        sil.Emit(OpCodes.Ldstr, "Set");
        sil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        sil.Emit(OpCodes.Ldnull); // static method — no instance
        sil.Emit(OpCodes.Ldc_I4_2);
        sil.Emit(OpCodes.Newarr, _types.Object);
        sil.Emit(OpCodes.Dup);
        sil.Emit(OpCodes.Ldc_I4_0);
        sil.Emit(OpCodes.Ldarg_0); // key
        sil.Emit(OpCodes.Stelem_Ref);
        sil.Emit(OpCodes.Dup);
        sil.Emit(OpCodes.Ldc_I4_1);
        sil.Emit(OpCodes.Ldarg_1); // value
        sil.Emit(OpCodes.Stelem_Ref);
        sil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        sil.Emit(OpCodes.Pop); // discard Invoke result (Set returns void → null)
        sil.Emit(OpCodes.Ret);
        runtime.WorkerThreadsSetEnvironmentData = setMethod;

        // public static void WorkerThreadsMarkAsUntransferable(object value)
        //   => StructuredClone.MarkUntransferable(value)   (#1002)
        var markMethod = runtimeType.DefineMethod(
            "WorkerThreadsMarkAsUntransferable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        var mil = markMethod.GetILGenerator();
        mil.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.StructuredClone, SharpTS");
        mil.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        mil.Emit(OpCodes.Ldstr, "MarkUntransferable");
        mil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        mil.Emit(OpCodes.Ldnull); // static method — no instance
        mil.Emit(OpCodes.Ldc_I4_1);
        mil.Emit(OpCodes.Newarr, _types.Object);
        mil.Emit(OpCodes.Dup);
        mil.Emit(OpCodes.Ldc_I4_0);
        mil.Emit(OpCodes.Ldarg_0); // value
        mil.Emit(OpCodes.Stelem_Ref);
        mil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        mil.Emit(OpCodes.Pop); // discard Invoke result (MarkUntransferable returns void → null)
        mil.Emit(OpCodes.Ret);
        runtime.WorkerThreadsMarkAsUntransferable = markMethod;
    }
}
