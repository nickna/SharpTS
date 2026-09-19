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
            EmitSharedArrayBufferHelper(runtimeType, runtime.RequireSharedArrayBuffer());
            EmitArrayBufferHelper(runtimeType, runtime);
            // DataView adapters are emitted before GetProperty in
            // EmitRuntimeClass because dynamic method values bind them.
            EmitTypedArrayHelpers(runtimeType, runtime);
            // Atomics static methods (pure-IL with reflection fallback for SharpTS types)
            EmitAtomicsHelpersPure(runtimeType, runtime.RequireAtomics(), runtime.TypedArrays.RequireImplementation(),
                runtime.Sentinels.UndefinedType, runtime.Sentinels.UndefinedInstance, runtime.Errors.TypeErrorConstructor, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor);
        }

        // MessageChannel/MessagePort moved to RuntimeEmitter.MessageChannel.cs —
        // emitted after EmitRuntimeClass because $MessagePort.PostMessage calls
        // $Runtime.StructuredClone (#222).

        // Worker constructor helper
        EmitWorkerHelper(runtimeType, runtime.Workers, runtime.EventLoop);

        // StructuredClone helper
        EmitStructuredCloneHelper(runtimeType, runtime.StructuredClone, runtime.ArrayStorage, runtime.Sentinels.UndefinedType,
            new StructuredCloneObjectInputs(runtime.ObjectStorage.Type, runtime.ObjectStorage.Constructor, runtime.ObjectStorage.FieldsGetter),
            new StructuredCloneErrorInputs(runtime.Errors.Type, runtime.ObjectFields.Interface,
                runtime.Errors.NameGetter, runtime.Errors.MessageGetter, runtime.Errors.StackGetter, runtime.Errors.StackSetter,
                runtime.Errors.MessageConstructor, runtime.Errors.TypeErrorConstructor, runtime.Errors.RangeErrorConstructor, runtime.Errors.ReferenceErrorConstructor,
                runtime.Errors.SyntaxErrorConstructor, runtime.Errors.URIErrorConstructor, runtime.Errors.EvalErrorConstructor),
            _features.HasAnyTypedArray ? new StructuredCloneBinaryInputs(runtime.RequireArrayBuffer(),
                runtime.RequireSharedArrayBuffer(), runtime.TypedArrays.RequireImplementation()) : null,
            runtime.Dates.Implementation is not null ? new StructuredCloneDateInputs(runtime.Dates.RequireImplementation().Type, runtime.Dates.RequireImplementation().MillisecondsConstructor, runtime.Dates.RequireImplementation().GetInstanceMethod("GetTime")) : null,
            runtime.RegExps.Implementation is not null ? new StructuredCloneRegExpInputs(runtime.RegExps.RequireImplementation().Type, runtime.RegExps.RequireImplementation().PatternFlagsConstructor,
                runtime.RegExps.RequireImplementation().SourceGetter, runtime.RegExps.RequireImplementation().FlagsGetter) : null,
            _features.UsesBuffer ? runtime.RequireBuffer() : null);

        // worker_threads module helpers
        EmitWorkerThreadsModuleHelpers(runtimeType, runtime.Workers);
    }

    /// <summary>
    /// Emits helper for creating SharedArrayBuffer.
    /// public static object CreateSharedArrayBuffer(double byteLength)
    /// Uses the emitted $SharedArrayBuffer type (pure-IL, no reflection).
    /// </summary>
    private void EmitSharedArrayBufferHelper(TypeBuilder runtimeType, EmittedSharedArrayBufferRuntime buffer)
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
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);

        buffer.Create = method;

        // Also emit slice and byteLength helpers
        EmitSharedArrayBufferSlice(runtimeType, buffer);
        EmitSharedArrayBufferByteLength(runtimeType, buffer);
    }

    /// <summary>
    /// Emits SharedArrayBuffer.slice(begin?, end?) helper.
    /// Requires emitted $SharedArrayBuffer type and calls directly.
    /// </summary>
    private void EmitSharedArrayBufferSlice(TypeBuilder runtimeType, EmittedSharedArrayBufferRuntime buffer)
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
        il.Emit(OpCodes.Isinst, buffer.Type);
        il.Emit(OpCodes.Brtrue, emittedPathLabel);

        il.Emit(OpCodes.Ldstr, "SharedArrayBuffer.slice requires emitted SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // Emitted type path - call Slice directly
        // For emitted type, end == int.MaxValue means use buffer length (handled inside Slice)
        il.MarkLabel(emittedPathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, buffer.Slice);
        il.Emit(OpCodes.Ret);

        buffer.SliceObject = method;
    }

    /// <summary>
    /// Emits SharedArrayBuffer.byteLength getter helper.
    /// Requires emitted $SharedArrayBuffer type.
    /// </summary>
    private void EmitSharedArrayBufferByteLength(TypeBuilder runtimeType, EmittedSharedArrayBufferRuntime buffer)
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
        il.Emit(OpCodes.Isinst, buffer.Type);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "SharedArrayBuffer.byteLength requires emitted SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Callvirt, buffer.ByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        buffer.GetByteLength = method;
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
        il.Emit(OpCodes.Newobj, runtime.RequireArrayBuffer().Ctor);
        il.Emit(OpCodes.Ret);

        runtime.RequireArrayBuffer().Create = method;

        // Also emit slice, byteLength, and isView helpers
        EmitArrayBufferSliceHelper(runtimeType, runtime.RequireArrayBuffer());
        EmitArrayBufferByteLengthHelper(runtimeType, runtime.RequireArrayBuffer());
        EmitArrayBufferIsView(runtimeType, runtime);
    }

    /// <summary>
    /// Emits ArrayBuffer.slice(begin, end) helper.
    /// Requires the emitted $ArrayBuffer type (pure-IL, no reflection).
    /// </summary>
    private void EmitArrayBufferSliceHelper(TypeBuilder runtimeType, EmittedArrayBufferRuntime arrayBuffer)
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
        il.Emit(OpCodes.Isinst, arrayBuffer.Type);
        il.Emit(OpCodes.Brtrue, emittedTypeLabel);

        il.Emit(OpCodes.Ldstr, "ArrayBuffer.slice requires emitted ArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedTypeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrayBuffer.Type);
        il.Emit(OpCodes.Ldarg_1);  // begin
        il.Emit(OpCodes.Ldarg_2);  // end
        il.Emit(OpCodes.Callvirt, arrayBuffer.Slice);
        il.Emit(OpCodes.Ret);

        arrayBuffer.SliceObject = method;
    }

    /// <summary>
    /// Emits ArrayBuffer.byteLength getter helper.
    /// Uses the emitted $ArrayBuffer type when possible (pure-IL, no reflection).
    /// </summary>
    private void EmitArrayBufferByteLengthHelper(TypeBuilder runtimeType, EmittedArrayBufferRuntime arrayBuffer)
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
        il.Emit(OpCodes.Isinst, arrayBuffer.Type);
        il.Emit(OpCodes.Brfalse, notEmittedTypeLabel);

        // It's our emitted $ArrayBuffer - call ByteLength directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrayBuffer.Type);
        il.Emit(OpCodes.Callvirt, arrayBuffer.ByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);  // Convert int to double
        il.Emit(OpCodes.Ret);

        // Not our emitted type
        il.MarkLabel(notEmittedTypeLabel);
        il.Emit(OpCodes.Ldstr, "ArrayBuffer.byteLength requires emitted ArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.Emit(OpCodes.Ret);

        arrayBuffer.GetByteLength = method;
    }

    /// <summary>
    /// Emits ArrayBuffer.isView static method helper.
    /// Returns true if the argument is a TypedArray or DataView.
    /// Handles both emitted pure-IL types and interpreter types.
    /// </summary>
    private void EmitArrayBufferIsView(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
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
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // Check if arg is an emitted $DataView
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.RequireDataView().Type);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // Non-emitted types are not views in standalone mode.
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        runtime.RequireArrayBuffer().IsView = method;
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
        il.Emit(OpCodes.Isinst, runtime.RequireArrayBuffer().Type);
        var notArrayBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notArrayBufferLabel);

        // It's $ArrayBuffer - create $DataView(buffer, byteOffset, byteLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, byteLengthIntLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireDataView().Ctor);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notArrayBufferLabel);

        // Check if $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.RequireSharedArrayBuffer().Type);
        il.Emit(OpCodes.Brfalse, unsupportedTypeLabel);

        // It's $SharedArrayBuffer - create $DataView(buffer, byteOffset, byteLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, byteLengthIntLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireDataView().Ctor);
        il.Emit(OpCodes.Br, endLabel);

        // Non-emitted buffer type in standalone mode.
        il.MarkLabel(unsupportedTypeLabel);
        il.Emit(OpCodes.Ldstr, "DataView constructor requires emitted ArrayBuffer or SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.RequireDataView().Create = method;

        // Emit property getters
        EmitDataViewByteLength(runtimeType, runtime.RequireDataView());
        EmitDataViewByteOffset(runtimeType, runtime.RequireDataView());
        EmitDataViewBuffer(runtimeType, runtime.RequireDataView());

        // Emit getter methods
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetInt8", "getInt8", false);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetUint8", "getUint8", false);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetInt16", "getInt16", true);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetUint16", "getUint16", true);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetInt32", "getInt32", true);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetUint32", "getUint32", true);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetFloat32", "getFloat32", true);
        EmitDataViewGetter(runtimeType, runtime.RequireDataView(), "GetFloat64", "getFloat64", true);
        EmitDataViewBigIntGetter(runtimeType, runtime.RequireDataView(), "GetBigInt64", "getBigInt64");
        EmitDataViewBigIntGetter(runtimeType, runtime.RequireDataView(), "GetBigUint64", "getBigUint64");

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

    private void EmitDataViewByteLength(TypeBuilder runtimeType, EmittedDataViewRuntime view)
    {
        var method = runtimeType.DefineMethod(
            "DataViewByteLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, view.Type);
        il.Emit(OpCodes.Callvirt, view.ByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        view.GetByteLength = method;
    }

    private void EmitDataViewByteOffset(TypeBuilder runtimeType, EmittedDataViewRuntime view)
    {
        var method = runtimeType.DefineMethod(
            "DataViewByteOffset",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, view.Type);
        il.Emit(OpCodes.Callvirt, view.ByteOffsetGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        view.GetByteOffset = method;
    }

    private void EmitDataViewBuffer(TypeBuilder runtimeType, EmittedDataViewRuntime view)
    {
        var method = runtimeType.DefineMethod(
            "DataViewBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, view.Type);
        il.Emit(OpCodes.Callvirt, view.BufferGetter);
        il.Emit(OpCodes.Ret);

        view.GetBuffer = method;
    }

    private void EmitDataViewGetter(TypeBuilder runtimeType, EmittedDataViewRuntime view, string runtimeMethodName, string jsMethodName, bool hasEndianness)
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
            "GetInt8" => view.GetInt8,
            "GetUint8" => view.GetUint8,
            "GetInt16" => view.GetInt16,
            "GetUint16" => view.GetUint16,
            "GetInt32" => view.GetInt32,
            "GetUint32" => view.GetUint32,
            "GetFloat32" => view.GetFloat32,
            "GetFloat64" => view.GetFloat64,
            _ => throw new ArgumentException($"Unknown DataView getter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, view.Type);
        il.Emit(OpCodes.Ldarg_1);
        if (hasEndianness)
            il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        // Store in runtime
        switch (jsMethodName)
        {
            case "getInt8": view.GetInt8Object = method; break;
            case "getUint8": view.GetUint8Object = method; break;
            case "getInt16": view.GetInt16Object = method; break;
            case "getUint16": view.GetUint16Object = method; break;
            case "getInt32": view.GetInt32Object = method; break;
            case "getUint32": view.GetUint32Object = method; break;
            case "getFloat32": view.GetFloat32Object = method; break;
            case "getFloat64": view.GetFloat64Object = method; break;
        }
    }

    private void EmitDataViewBigIntGetter(TypeBuilder runtimeType, EmittedDataViewRuntime view, string runtimeMethodName, string jsMethodName)
    {
        var method = runtimeType.DefineMethod(
            $"DataView{runtimeMethodName}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, // $DataView instance methods return object (boxed BigInteger)
            [_types.Object, _types.Int32, _types.Boolean]
        );

        MethodBuilder target = runtimeMethodName switch
        {
            "GetBigInt64" => view.GetBigInt64,
            "GetBigUint64" => view.GetBigUint64,
            _ => throw new ArgumentException($"Unknown DataView BigInt getter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, view.Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        switch (jsMethodName)
        {
            case "getBigInt64": view.GetBigInt64Object = method; break;
            case "getBigUint64": view.GetBigUint64Object = method; break;
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
            _types.Object,
            paramTypes
        );

        MethodBuilder target = runtimeMethodName switch
        {
            "SetInt8" => runtime.RequireDataView().SetInt8,
            "SetUint8" => runtime.RequireDataView().SetUint8,
            "SetInt16" => runtime.RequireDataView().SetInt16,
            "SetUint16" => runtime.RequireDataView().SetUint16,
            "SetInt32" => runtime.RequireDataView().SetInt32,
            "SetUint32" => runtime.RequireDataView().SetUint32,
            "SetFloat32" => runtime.RequireDataView().SetFloat32,
            "SetFloat64" => runtime.RequireDataView().SetFloat64,
            "SetBigInt64" => runtime.RequireDataView().SetBigInt64,
            "SetBigUint64" => runtime.RequireDataView().SetBigUint64,
            _ => throw new ArgumentException($"Unknown DataView setter: {runtimeMethodName}")
        };

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.RequireDataView().Type);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        if (runtimeMethodName is "SetBigInt64" or "SetBigUint64")
        {
            // BigInt helpers are feature-gated. A DataView-only program still
            // emits these adapters, so preserve the raw value when the BigInt
            // conversion helper is absent (the methods are unreachable there).
            if (runtime.BigInt.Implementation is { } bigInt)
                il.Emit(OpCodes.Call, bigInt.ToBigInt);
        }
        else
        {
            // DataView numeric setters perform ToNumber before converting to
            // their element representation. This supplies NaN for undefined
            // and raises the guest TypeError for Symbol values.
            il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber);
            il.Emit(OpCodes.Box, _types.Double);
        }
        if (hasEndianness)
            il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, target);
        il.Emit(OpCodes.Ret);

        // Store in runtime
        switch (jsMethodName)
        {
            case "setInt8": runtime.RequireDataView().SetInt8Object = method; break;
            case "setUint8": runtime.RequireDataView().SetUint8Object = method; break;
            case "setInt16": runtime.RequireDataView().SetInt16Object = method; break;
            case "setUint16": runtime.RequireDataView().SetUint16Object = method; break;
            case "setInt32": runtime.RequireDataView().SetInt32Object = method; break;
            case "setUint32": runtime.RequireDataView().SetUint32Object = method; break;
            case "setFloat32": runtime.RequireDataView().SetFloat32Object = method; break;
            case "setFloat64": runtime.RequireDataView().SetFloat64Object = method; break;
            case "setBigInt64": runtime.RequireDataView().SetBigInt64Object = method; break;
            case "setBigUint64": runtime.RequireDataView().SetBigUint64Object = method; break;
        }
    }

    /// <summary>
    /// Emits helpers for creating TypedArrays.
    /// Uses reflection-based late-binding to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private void EmitTypedArrayHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
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
        EmitTypedArrayGetHelper(runtimeType, arrays);
        EmitTypedArraySetHelper(runtimeType, arrays);

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
        // The body follows arrays.Implementation availability inside the helper.
        // Without an emitted typed-array type, IsTypedArray just returns false.
        EmitIsTypedArrayHelper(runtimeType, runtime.TypedArrays);
        if (_features.HasAnyTypedArray)
        {
            EmitGetTypedArrayElementHelper(runtimeType, runtime.TypedArrays.RequireImplementation());
            EmitSetTypedArrayElementHelper(runtimeType, runtime.TypedArrays.RequireImplementation());
            EmitGetTypedArrayMemberHelper(runtimeType, runtime.TypedArrays.RequireImplementation());
        }
    }

    /// <summary>
    /// Emits a helper that checks if an object is a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitIsTypedArrayHelper(TypeBuilder runtimeType, EmittedTypedArrayRuntime arrays)
    {
        var method = runtimeType.DefineMethod(
            "IsTypedArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        arrays.IsTypedArray = method;

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
        if (arrays.Implementation is not null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, arrays.RequireImplementation().BaseType);
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
    private void EmitGetTypedArrayElementHelper(TypeBuilder runtimeType, EmittedTypedArrayImplementation arrays)
    {
        var method = runtimeType.DefineMethod(
            "GetTypedArrayElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32]
        );
        arrays.GetElement = method;

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        il.Emit(OpCodes.Ldstr, "TypedArray element access requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, arrays.ElementGet);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that sets an element in a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitSetTypedArrayElementHelper(TypeBuilder runtimeType, EmittedTypedArrayImplementation arrays)
    {
        var method = runtimeType.DefineMethod(
            "SetTypedArrayElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Int32, _types.Object]
        );
        arrays.SetElement = method;

        var il = method.GetILGenerator();
        var emittedPath = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);

        il.Emit(OpCodes.Ldstr, "TypedArray element assignment requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, arrays.ElementSet);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that gets a member from a TypedArray.
    /// Handles both emitted pure-IL TypedArray types and interpreter TypedArrays.
    /// </summary>
    private void EmitGetTypedArrayMemberHelper(TypeBuilder runtimeType, EmittedTypedArrayImplementation arrays)
    {
        var method = runtimeType.DefineMethod(
            "GetTypedArrayMember",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        arrays.GetMember = method;

        var il = method.GetILGenerator();

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
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // It's an emitted TypedArray - check property name
        // Check "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkByteLengthLabel);

        // Return length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
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
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.ByteLengthGetter);
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
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.ByteOffsetGetter);
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
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.BufferGetter);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(checkBytesPerElementLabel);
        // Check "BYTES_PER_ELEMENT"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "BYTES_PER_ELEMENT");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, checkMethodsLabel);

        // Return BYTES_PER_ELEMENT (call abstract BytesPerElement property)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.BytesPerElementGetter);
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
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, arrays.BoundMethodCtor);
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
        var arrays = runtime.TypedArrays.RequireImplementation();
        // Get the emitted TypedArray constructors
        var (lengthCtor, bufferCtor) = GetEmittedTypedArrayCtors(arrays, name);

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
        var constructSharedArrayBufferLabel = il.DefineLabel();
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
        il.Emit(OpCodes.Isinst, runtime.RequireArrayBuffer().Type);
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
        il.Emit(OpCodes.Isinst, runtime.RequireSharedArrayBuffer().Type);
        il.Emit(OpCodes.Brtrue, constructSharedArrayBufferLabel);
        il.Emit(OpCodes.Br, isTSArrayLabel);

        // It's $SharedArrayBuffer - use emitted buffer constructor
        il.MarkLabel(constructSharedArrayBufferLabel);
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
            il.Emit(OpCodes.Isinst, runtime.ArrayStorage.Type);
            il.Emit(OpCodes.Brfalse, isTypedArrayLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.ArrayStorage.Type);
            il.Emit(OpCodes.Stloc, tsArrLocal);
            il.Emit(OpCodes.Ldloc, tsArrLocal);
            il.Emit(OpCodes.Castclass, runtime.ArrayStorage.Type);
            il.Emit(OpCodes.Callvirt, runtime.ArrayStorage.LengthGetter);
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
            il.Emit(OpCodes.Call, runtime.ObjectRead.Element);
            il.Emit(OpCodes.Stloc, arrElemLocal);
            il.Emit(OpCodes.Ldloc, arrResultLocal);
            il.Emit(OpCodes.Castclass, arrays.BaseType);
            il.Emit(OpCodes.Ldloc, arrILocal);
            il.Emit(OpCodes.Ldloc, arrElemLocal);
            il.Emit(OpCodes.Callvirt, arrays.ElementSet);
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
            il.Emit(OpCodes.Isinst, arrays.BaseType);
            il.Emit(OpCodes.Brfalse, isNumberLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, arrays.BaseType);
            il.Emit(OpCodes.Stloc, srcTALocal);
            il.Emit(OpCodes.Ldloc, srcTALocal);
            il.Emit(OpCodes.Castclass, arrays.BaseType);
            il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
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
            il.Emit(OpCodes.Castclass, arrays.BaseType);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Callvirt, arrays.ElementGet);
            il.Emit(OpCodes.Stloc, taElemLocal);
            il.Emit(OpCodes.Ldloc, taResultLocal);
            il.Emit(OpCodes.Castclass, arrays.BaseType);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldloc, taElemLocal);
            il.Emit(OpCodes.Callvirt, arrays.ElementSet);
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

        // A parent compiled realm's $SharedArrayBuffer has the same stable emitted shape but
        // a different CLR identity. The concrete buffer constructor validates the shape and
        // recovers its shared byte[] backing store.
        il.Emit(OpCodes.Ldloc, argTypeNameLocal);
        il.Emit(OpCodes.Ldstr, "$SharedArrayBuffer");
        il.Emit(OpCodes.Call, typeof(string).GetMethod(
            "op_Equality", BindingFlags.Public | BindingFlags.Static,
            binder: null, [typeof(string), typeof(string)], modifiers: null)!);
        il.Emit(OpCodes.Brtrue, constructSharedArrayBufferLabel);

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
        arrays.RegisterFromObject(name, method);
    }

    /// <summary>
    /// Gets the emitted TypedArray constructors for the given type name.
    /// </summary>
    private (ConstructorBuilder lengthCtor, ConstructorBuilder bufferCtor) GetEmittedTypedArrayCtors(EmittedTypedArrayImplementation arrays, string name)
    {
        return name switch
        {
            "Int8Array" => (arrays.Int8ArrayLengthCtor, arrays.Int8ArrayBufferCtor),
            "Uint8Array" => (arrays.Uint8ArrayLengthCtor, arrays.Uint8ArrayBufferCtor),
            "Uint8ClampedArray" => (arrays.Uint8ClampedArrayLengthCtor, arrays.Uint8ClampedArrayBufferCtor),
            "Int16Array" => (arrays.Int16ArrayLengthCtor, arrays.Int16ArrayBufferCtor),
            "Uint16Array" => (arrays.Uint16ArrayLengthCtor, arrays.Uint16ArrayBufferCtor),
            "Int32Array" => (arrays.Int32ArrayLengthCtor, arrays.Int32ArrayBufferCtor),
            "Uint32Array" => (arrays.Uint32ArrayLengthCtor, arrays.Uint32ArrayBufferCtor),
            "Float32Array" => (arrays.Float32ArrayLengthCtor, arrays.Float32ArrayBufferCtor),
            "Float64Array" => (arrays.Float64ArrayLengthCtor, arrays.Float64ArrayBufferCtor),
            "BigInt64Array" => (arrays.BigInt64ArrayLengthCtor, arrays.BigInt64ArrayBufferCtor),
            "BigUint64Array" => (arrays.BigUint64ArrayLengthCtor, arrays.BigUint64ArrayBufferCtor),
            _ => throw new ArgumentException($"Unknown TypedArray type: {name}")
        };
    }

    private void EmitTypedArrayHelper(TypeBuilder runtimeType, EmittedRuntime runtime, string name)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
        // Get the emitted TypedArray constructors
        var (lengthCtor, bufferCtor) = GetEmittedTypedArrayCtors(arrays, name);

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
        ilSAB.Emit(OpCodes.Isinst, runtime.RequireArrayBuffer().Type);
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
        ilSAB.Emit(OpCodes.Isinst, runtime.RequireSharedArrayBuffer().Type);
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
        arrays.RegisterFromBuffer(name, methodFromSAB);
    }

    private void EmitTypedArrayGetHelper(TypeBuilder runtimeType, EmittedTypedArrayImplementation arrays)
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
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "TypedArray get requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, arrays.ElementGet);
        il.Emit(OpCodes.Ret);

        _ = method;
    }

    private void EmitTypedArraySetHelper(TypeBuilder runtimeType, EmittedTypedArrayImplementation arrays)
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
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brtrue, emittedPath);
        il.Emit(OpCodes.Ldstr, "TypedArray set requires emitted typed arrays.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(emittedPath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, arrays.ElementSet);
        il.Emit(OpCodes.Ret);

        _ = method;
    }

    /// <summary>
    /// Emits Worker constructor helper.
    /// Uses direct constructor invocation.
    /// </summary>
    private void EmitWorkerHelper(TypeBuilder runtimeType, EmittedWorkerRuntime workers, EmittedEventLoopRuntime eventLoop)
    {
        // CreateWorker(string filename, object? options, object? parentInterpreter)
        //
        // Constructs a SharpTSWorker via reflection (keeping the standalone DLL
        // free of a hard SharpTS.dll reference) and binds the emitted $EventLoop
        // singleton's Ref/Unref as the worker's keep-alive handle, so a running
        // worker holds the compiled event loop open by default — Node semantics,
        // and the compiled-mode half of the #329 premature-exit fix (#354). The
        // worker compiles and loads its child module graph into an isolated realm, so
        // SharpTS.dll and its managed dependency closure must be co-located; the emit site
        // records those deployment capabilities via Deployment.Require.
        var method = runtimeType.DefineMethod(
            "CreateWorker",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        var typeLocal = il.DeclareLocal(_types.Type);
        var loopLocal = il.DeclareLocal(eventLoop.Type);
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
        il.Emit(OpCodes.Call, eventLoop.GetInstance);
        il.Emit(OpCodes.Stloc, loopLocal);

        // Action ref = new Action(loop, $EventLoop.Ref);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, eventLoop.Ref);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, refLocal);

        // Action unref = new Action(loop, $EventLoop.Unref);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, eventLoop.Unref);
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Stloc, unrefLocal);

        // Action<Action> schedule = new Action<Action>(loop, $EventLoop.Schedule);
        il.Emit(OpCodes.Ldloc, loopLocal);
        il.Emit(OpCodes.Ldftn, eventLoop.Schedule);
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

        workers.Create = method;
    }

    /// <summary>
    /// Emits worker_threads module helper methods.
    /// Uses direct calls.
    /// </summary>
    private void EmitWorkerThreadsModuleHelpers(TypeBuilder runtimeType, EmittedWorkerRuntime workers)
    {
        // Every compiled worker is loaded into its own AssemblyLoadContext. These static
        // fields are therefore realm-local even though the emitted runtime uses statics for
        // module state and intrinsics. The worker host configures them before $Program.Main.
        var isWorkerField = runtimeType.DefineField(
            "_workerContextEnabled", _types.Boolean, FieldAttributes.Private | FieldAttributes.Static);
        var workerThreadIdField = runtimeType.DefineField(
            "_workerThreadId", _types.Double, FieldAttributes.Private | FieldAttributes.Static);
        var workerDataField = runtimeType.DefineField(
            "_workerData", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var parentPortField = runtimeType.DefineField(
            "_workerParentPort", _types.Object, FieldAttributes.Private | FieldAttributes.Static);

        // Called reflectively by SharpTSWorker after loading the worker artifact.
        var configureContext = runtimeType.DefineMethod(
            "ConfigureWorkerContext",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [_types.Double, _types.Object, _types.Object]);
        var cil = configureContext.GetILGenerator();
        cil.Emit(OpCodes.Ldc_I4_1);
        cil.Emit(OpCodes.Stsfld, isWorkerField);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Stsfld, workerThreadIdField);
        cil.Emit(OpCodes.Ldarg_1);
        cil.Emit(OpCodes.Stsfld, workerDataField);
        cil.Emit(OpCodes.Ldarg_2);
        cil.Emit(OpCodes.Stsfld, parentPortField);
        cil.Emit(OpCodes.Ret);

        // Clears host objects before the collectible realm is unloaded. This is primarily
        // hygiene for failed unload diagnostics; the fields belong to the collectible type.
        var clearContext = runtimeType.DefineMethod(
            "ClearWorkerContext",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        var ccil = clearContext.GetILGenerator();
        ccil.Emit(OpCodes.Ldc_I4_0);
        ccil.Emit(OpCodes.Stsfld, isWorkerField);
        ccil.Emit(OpCodes.Ldc_R8, 0.0);
        ccil.Emit(OpCodes.Stsfld, workerThreadIdField);
        ccil.Emit(OpCodes.Ldnull);
        ccil.Emit(OpCodes.Stsfld, workerDataField);
        ccil.Emit(OpCodes.Ldnull);
        ccil.Emit(OpCodes.Stsfld, parentPortField);
        ccil.Emit(OpCodes.Ret);

        // isMainThread getter: true for an ordinary compiled program, false only after
        // ConfigureWorkerContext has initialized this isolated worker realm.
        var isMainThreadMethod = runtimeType.DefineMethod(
            "WorkerThreadsIsMainThread",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            Type.EmptyTypes
        );

        var il = isMainThreadMethod.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, isWorkerField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
        workers.IsMainThread = isMainThreadMethod;

        // threadId getter
        var threadIdMethod = runtimeType.DefineMethod(
            "WorkerThreadsThreadId",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );

        var il2 = threadIdMethod.GetILGenerator();
        var hasWorkerContext = il2.DefineLabel();
        il2.Emit(OpCodes.Ldsfld, isWorkerField);
        il2.Emit(OpCodes.Brtrue, hasWorkerContext);
        il2.Emit(OpCodes.Ldc_R8, 0.0);
        il2.Emit(OpCodes.Ret);
        il2.MarkLabel(hasWorkerContext);
        il2.Emit(OpCodes.Ldsfld, workerThreadIdField);
        il2.Emit(OpCodes.Ret);
        workers.ThreadId = threadIdMethod;

        var workerDataMethod = runtimeType.DefineMethod(
            "WorkerThreadsWorkerData",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        var wdil = workerDataMethod.GetILGenerator();
        wdil.Emit(OpCodes.Ldsfld, workerDataField);
        wdil.Emit(OpCodes.Ret);
        workers.WorkerData = workerDataMethod;

        var parentPortMethod = runtimeType.DefineMethod(
            "WorkerThreadsParentPort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        var ppil = parentPortMethod.GetILGenerator();
        ppil.Emit(OpCodes.Ldsfld, parentPortField);
        ppil.Emit(OpCodes.Ret);
        workers.ParentPort = parentPortMethod;

        // receiveMessageOnPort — synchronous main-thread drain (#1077). The method is DEFINED
        // here (so callers can bind workers.ReceiveMessageOnPort) but its body is
        // emitted later by EmitWorkerThreadsReceiveMessageOnPortBody, after EmitMessageChannelTypes
        // has created the $MessagePort type — this helper reads that type's _pending queue and
        // _closed/_cloneError fields, which don't exist at this point in emission. The $Runtime
        // type isn't finalized until EmitRuntimeClassFinalize, so filling the body afterward is safe.
        workers.ReceiveMessageOnPort = runtimeType.DefineMethod(
            "WorkerThreadsReceiveMessageOnPort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        workers.ForeignReceiveType = runtimeType.DefineField(
            "_receiveMessageOnPortForeignType",
            _types.Type,
            FieldAttributes.Private | FieldAttributes.Static);
        workers.ForeignReceiveMethod = runtimeType.DefineField(
            "_receiveMessageOnPortForeignMethod",
            _types.MethodInfo,
            FieldAttributes.Private | FieldAttributes.Static);

        // getEnvironmentData / setEnvironmentData — route to the C# per-process
        // WorkerEnvironmentData store via reflection (worker programs co-locate SharpTS.dll;
        // Deployment.Require is recorded at the call sites in WorkerThreadsModuleEmitter so
        // a program that never calls these stays standalone). #1000.
        EmitWorkerThreadsEnvironmentData(runtimeType, workers);
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
    private void EmitWorkerThreadsReceiveMessageOnPortBody(
        EmittedWorkerRuntime workers, EmittedMessagePortRuntime port, FieldInfo undefinedInstance)
    {
        var il = workers.ReceiveMessageOnPort.GetILGenerator();
        var portLocal = il.DeclareLocal(port.Type);
        var msgLocal = il.DeclareLocal(_types.Object);
        var valueLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var foreignPortTypeLocal = il.DeclareLocal(_types.Type);
        var foreignReceiveMethodLocal = il.DeclareLocal(_types.MethodInfo);
        var foreignReceiveResultLocal = il.DeclareLocal(_types.Object);
        var undefinedLabel = il.DefineLabel();
        var emittedPortLabel = il.DefineLabel();
        var foreignMethodCachedLabel = il.DefineLabel();
        var foreignMethodReadyLabel = il.DefineLabel();
        var afterMarkerLabel = il.DefineLabel();

        // port = arg0 as $MessagePort; realm-local ports use the direct queue path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, port.Type);
        il.Emit(OpCodes.Stloc, portLocal);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Brtrue, emittedPortLabel);

        // A compiled-parent MessagePort reaches the isolated worker as a host bridge.
        // Discover its narrow synchronous receive ABI by name so standalone output keeps
        // no static SharpTS.dll dependency. The bridge returns Dictionary<string, object?>,
        // which is the emitted runtime's native object-literal representation.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, undefinedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, foreignPortTypeLocal);
        il.Emit(OpCodes.Ldsfld, workers.ForeignReceiveType);
        il.Emit(OpCodes.Ldloc, foreignPortTypeLocal);
        il.Emit(OpCodes.Beq, foreignMethodCachedLabel);

        il.Emit(OpCodes.Ldloc, foreignPortTypeLocal);
        il.Emit(OpCodes.Ldstr, "ReceiveMessageSyncForCompiled");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, foreignReceiveMethodLocal);
        il.Emit(OpCodes.Ldloc, foreignReceiveMethodLocal);
        il.Emit(OpCodes.Stsfld, workers.ForeignReceiveMethod);
        il.Emit(OpCodes.Ldloc, foreignPortTypeLocal);
        il.Emit(OpCodes.Stsfld, workers.ForeignReceiveType);
        il.Emit(OpCodes.Br, foreignMethodReadyLabel);

        il.MarkLabel(foreignMethodCachedLabel);
        il.Emit(OpCodes.Ldsfld, workers.ForeignReceiveMethod);
        il.Emit(OpCodes.Stloc, foreignReceiveMethodLocal);

        il.MarkLabel(foreignMethodReadyLabel);
        il.Emit(OpCodes.Ldloc, foreignReceiveMethodLocal);
        il.Emit(OpCodes.Brfalse, undefinedLabel);
        il.Emit(OpCodes.Ldloc, foreignReceiveMethodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, foreignReceiveResultLocal);
        il.Emit(OpCodes.Ldloc, foreignReceiveResultLocal);
        il.Emit(OpCodes.Brfalse, undefinedLabel);
        il.Emit(OpCodes.Ldloc, foreignReceiveResultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emittedPortLabel);

        // if (port._closed) return undefined
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // if (!port._pending.TryDequeue(out msg)) return undefined
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ldfld, port.Pending);
        il.Emit(OpCodes.Ldloca, msgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConcurrentQueueOfObject, "TryDequeue", [_types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, undefinedLabel);

        // value = msg; if (msg == _cloneError) value = undefined  (a queued clone-failure
        // sentinel has no cloneable payload — surface it as { message: undefined }).
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Ldsfld, port.CloneError);
        il.Emit(OpCodes.Bne_Un, afterMarkerLabel);
        il.Emit(OpCodes.Ldsfld, undefinedInstance);
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
        il.Emit(OpCodes.Ldsfld, undefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the getEnvironmentData/setEnvironmentData runtime helpers that reflectively call
    /// <c>SharpTS.Runtime.Types.WorkerEnvironmentData</c>.
    /// </summary>
    private void EmitWorkerThreadsEnvironmentData(TypeBuilder runtimeType, EmittedWorkerRuntime workers)
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
        workers.GetEnvironmentData = getMethod;

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
        workers.SetEnvironmentData = setMethod;

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
        workers.MarkAsUntransferable = markMethod;
    }
}
