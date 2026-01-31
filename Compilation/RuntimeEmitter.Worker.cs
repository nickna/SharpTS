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
        // SharedArrayBuffer constructor helper
        EmitSharedArrayBufferHelper(runtimeType, runtime);

        // TypedArray constructor helpers
        EmitTypedArrayHelpers(runtimeType, runtime);

        // Atomics static methods
        EmitAtomicsHelpers(runtimeType, runtime);

        // MessageChannel constructor helper
        EmitMessageChannelHelper(runtimeType, runtime);

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

        // Get the SharpTSSharedArrayBuffer type and constructor
        var sabType = typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer);
        var sabCtor = sabType.GetConstructor([typeof(int)])!;

        // Convert double to int and create new SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_0);          // Load byteLength (double)
        il.Emit(OpCodes.Conv_I4);           // Convert to int
        il.Emit(OpCodes.Newobj, sabCtor);   // new SharpTSSharedArrayBuffer(byteLength)
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferCtor = method;

        // Also emit slice and byteLength helpers
        EmitSharedArrayBufferSlice(runtimeType, runtime);
        EmitSharedArrayBufferByteLength(runtimeType, runtime);
    }

    /// <summary>
    /// Emits SharedArrayBuffer.slice(begin?, end?) helper.
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
        var sabType = typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer);
        var sliceMethod = sabType.GetMethod("Slice", [typeof(int), typeof(int?)])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, sabType);
        il.Emit(OpCodes.Ldarg_1);  // begin

        // Convert end to nullable int (if end == int.MaxValue, treat as null for "use full length")
        var endLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Beq, endLabel);

        // end is a real value - wrap in nullable
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Br, callLabel);

        // end is MaxValue - use null
        il.MarkLabel(endLabel);
        var localNullableInt = il.DeclareLocal(typeof(int?));
        il.Emit(OpCodes.Ldloca, localNullableInt);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, localNullableInt);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Callvirt, sliceMethod);
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferSlice = method;
    }

    /// <summary>
    /// Emits SharedArrayBuffer.byteLength getter helper.
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
        var sabType = typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer);
        var byteLengthGetter = sabType.GetProperty("ByteLength")!.GetGetMethod()!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, sabType);
        il.Emit(OpCodes.Callvirt, byteLengthGetter);
        il.Emit(OpCodes.Conv_R8);  // Convert int to double
        il.Emit(OpCodes.Ret);

        runtime.TSSharedArrayBufferByteLengthGetter = method;
    }

    /// <summary>
    /// Emits helpers for creating TypedArrays.
    /// </summary>
    private void EmitTypedArrayHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Helper method for each TypedArray type
        EmitTypedArrayHelper(runtimeType, runtime, "Int8Array", typeof(SharpTS.Runtime.Types.SharpTSInt8Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Uint8Array", typeof(SharpTS.Runtime.Types.SharpTSUint8Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Uint8ClampedArray", typeof(SharpTS.Runtime.Types.SharpTSUint8ClampedArray));
        EmitTypedArrayHelper(runtimeType, runtime, "Int16Array", typeof(SharpTS.Runtime.Types.SharpTSInt16Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Uint16Array", typeof(SharpTS.Runtime.Types.SharpTSUint16Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Int32Array", typeof(SharpTS.Runtime.Types.SharpTSInt32Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Uint32Array", typeof(SharpTS.Runtime.Types.SharpTSUint32Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Float32Array", typeof(SharpTS.Runtime.Types.SharpTSFloat32Array));
        EmitTypedArrayHelper(runtimeType, runtime, "Float64Array", typeof(SharpTS.Runtime.Types.SharpTSFloat64Array));
        EmitTypedArrayHelper(runtimeType, runtime, "BigInt64Array", typeof(SharpTS.Runtime.Types.SharpTSBigInt64Array));
        EmitTypedArrayHelper(runtimeType, runtime, "BigUint64Array", typeof(SharpTS.Runtime.Types.SharpTSBigUint64Array));

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
        EmitIsTypedArrayHelper(runtimeType, runtime);
        EmitGetTypedArrayElementHelper(runtimeType, runtime);
        EmitSetTypedArrayElementHelper(runtimeType, runtime);
    }

    /// <summary>
    /// Emits a helper that checks if an object is a TypedArray by examining its type name.
    /// This avoids a hard dependency on SharpTS.dll for standalone compilation.
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
        var falseNullBaseTypeLabel = il.DefineLabel();
        var falseNullFullNameLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // if (obj == null) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseNullObjLabel);

        // Check if type name contains "SharpTSTypedArray" (base class name)
        // obj.GetType().BaseType?.FullName?.Contains("SharpTSTypedArray") == true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "get_BaseType"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, falseNullBaseTypeLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "get_FullName"));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, falseNullFullNameLabel);
        il.Emit(OpCodes.Ldstr, "SharpTSTypedArray");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.String));
        il.Emit(OpCodes.Brtrue_S, trueLabel);
        // Contains returned false, go to return false
        il.Emit(OpCodes.Br_S, returnFalseLabel);

        // Handle null obj case (stack is empty)
        il.MarkLabel(falseNullObjLabel);
        il.Emit(OpCodes.Br_S, returnFalseLabel);

        // Handle null BaseType case (pop the null BaseType from stack)
        il.MarkLabel(falseNullBaseTypeLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br_S, returnFalseLabel);

        // Handle null FullName case (pop the null FullName from stack)
        il.MarkLabel(falseNullFullNameLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br_S, returnFalseLabel);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that gets an element from a TypedArray using reflection.
    /// This avoids a hard dependency on SharpTS.dll for standalone compilation.
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

        // Use reflection: obj.GetType().GetProperty("Item").GetValue(obj, new object[] { index })
        var typeLocal = il.DeclareLocal(_types.Type);
        var propInfoLocal = il.DeclareLocal(_types.PropertyInfo);
        var indexArrayLocal = il.DeclareLocal(_types.ObjectArray);

        // var type = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // var propInfo = type.GetProperty("Item");
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Item");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Stloc, propInfoLocal);

        // var indexArray = new object[] { index };
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, indexArrayLocal);

        // return propInfo.GetValue(obj, indexArray);
        il.Emit(OpCodes.Ldloc, propInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper that sets an element in a TypedArray using reflection.
    /// This avoids a hard dependency on SharpTS.dll for standalone compilation.
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

        // Use reflection: obj.GetType().GetProperty("Item").SetValue(obj, value, new object[] { index })
        var typeLocal = il.DeclareLocal(_types.Type);
        var propInfoLocal = il.DeclareLocal(_types.PropertyInfo);
        var indexArrayLocal = il.DeclareLocal(_types.ObjectArray);

        // var type = obj.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // var propInfo = type.GetProperty("Item");
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Item");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Stloc, propInfoLocal);

        // var indexArray = new object[] { index };
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, indexArrayLocal);

        // propInfo.SetValue(obj, value, indexArray);
        il.Emit(OpCodes.Ldloc, propInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "SetValue", _types.Object, _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits helpers for creating TypedArrays from an object argument (number or SharedArrayBuffer).
    /// </summary>
    private void EmitTypedArrayFromObjectHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int8Array", typeof(SharpTS.Runtime.Types.SharpTSInt8Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint8Array", typeof(SharpTS.Runtime.Types.SharpTSUint8Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint8ClampedArray", typeof(SharpTS.Runtime.Types.SharpTSUint8ClampedArray));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int16Array", typeof(SharpTS.Runtime.Types.SharpTSInt16Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint16Array", typeof(SharpTS.Runtime.Types.SharpTSUint16Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Int32Array", typeof(SharpTS.Runtime.Types.SharpTSInt32Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Uint32Array", typeof(SharpTS.Runtime.Types.SharpTSUint32Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Float32Array", typeof(SharpTS.Runtime.Types.SharpTSFloat32Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "Float64Array", typeof(SharpTS.Runtime.Types.SharpTSFloat64Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "BigInt64Array", typeof(SharpTS.Runtime.Types.SharpTSBigInt64Array));
        EmitTypedArrayFromObjectHelper(runtimeType, runtime, "BigUint64Array", typeof(SharpTS.Runtime.Types.SharpTSBigUint64Array));
    }

    /// <summary>
    /// Emits a helper that creates a TypedArray from an object (either a number for length, or a SharedArrayBuffer).
    /// </summary>
    private void EmitTypedArrayFromObjectHelper(TypeBuilder runtimeType, EmittedRuntime runtime, string name, Type arrayType)
    {
        // Create{name}FromObject(object arg) - handles number or SharedArrayBuffer
        var method = runtimeType.DefineMethod(
            $"Create{name}FromObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var sabType = typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer);
        var lengthCtor = arrayType.GetConstructor([typeof(int)])!;
        var sabCtor = arrayType.GetConstructor([sabType, typeof(int), typeof(int?)])!;

        var isSabLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if arg is SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, sabType);
        il.Emit(OpCodes.Brtrue, isSabLabel);

        // Not a SharedArrayBuffer - treat as length
        // Convert to double first, then to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, lengthCtor);
        il.Emit(OpCodes.Br, endLabel);

        // Is a SharedArrayBuffer - create view with default offset and length
        il.MarkLabel(isSabLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, sabType);
        il.Emit(OpCodes.Ldc_I4_0);  // byteOffset = 0
        var localNullableInt = il.DeclareLocal(typeof(int?));
        il.Emit(OpCodes.Ldloca, localNullableInt);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, localNullableInt);  // length = null (use entire buffer)
        il.Emit(OpCodes.Newobj, sabCtor);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        // Store the helper for use by ILEmitter
        runtime.TypedArrayFromObjectHelpers[name] = method;
    }

    private void EmitTypedArrayHelper(TypeBuilder runtimeType, EmittedRuntime runtime, string name, Type arrayType)
    {
        // Create from length: CreateInt8Array(double length)
        var methodFromLength = runtimeType.DefineMethod(
            $"Create{name}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double]
        );

        var il = methodFromLength.GetILGenerator();
        var ctor = arrayType.GetConstructor([typeof(int)])!;

        il.Emit(OpCodes.Ldarg_0);         // Load length (double)
        il.Emit(OpCodes.Conv_I4);          // Convert to int
        il.Emit(OpCodes.Newobj, ctor);     // new TypedArray(length)
        il.Emit(OpCodes.Ret);

        // Create from SharedArrayBuffer: CreateInt8ArrayFromSAB(object sab, double byteOffset, object length)
        var methodFromSAB = runtimeType.DefineMethod(
            $"Create{name}FromSAB",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object]
        );

        var ilSAB = methodFromSAB.GetILGenerator();
        var ctorSAB = arrayType.GetConstructor([typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer), typeof(int), typeof(int?)])!;

        ilSAB.Emit(OpCodes.Ldarg_0);                                      // Load sab
        ilSAB.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSSharedArrayBuffer));
        ilSAB.Emit(OpCodes.Ldarg_1);                                      // Load byteOffset
        ilSAB.Emit(OpCodes.Conv_I4);                                       // Convert to int

        // Handle nullable length parameter
        var lblHasLength = ilSAB.DefineLabel();
        var lblEndLength = ilSAB.DefineLabel();

        ilSAB.Emit(OpCodes.Ldarg_2);                                      // Load length
        ilSAB.Emit(OpCodes.Brfalse, lblHasLength);                        // if null, branch

        // length is not null - convert and wrap
        ilSAB.Emit(OpCodes.Ldarg_2);
        ilSAB.Emit(OpCodes.Unbox_Any, _types.Double);
        ilSAB.Emit(OpCodes.Conv_I4);
        ilSAB.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        ilSAB.Emit(OpCodes.Br, lblEndLength);

        ilSAB.MarkLabel(lblHasLength);
        // length is null
        var localNullableInt = ilSAB.DeclareLocal(typeof(int?));
        ilSAB.Emit(OpCodes.Ldloca, localNullableInt);
        ilSAB.Emit(OpCodes.Initobj, typeof(int?));
        ilSAB.Emit(OpCodes.Ldloc, localNullableInt);

        ilSAB.MarkLabel(lblEndLength);
        ilSAB.Emit(OpCodes.Newobj, ctorSAB);
        ilSAB.Emit(OpCodes.Ret);
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
        var indexerGetter = typeof(SharpTS.Runtime.Types.SharpTSTypedArray).GetProperty("Item")!.GetGetMethod()!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, indexerGetter);
        il.Emit(OpCodes.Ret);

        runtime.TSTypedArrayGet = method;
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
        var indexerSetter = typeof(SharpTS.Runtime.Types.SharpTSTypedArray).GetProperty("Item")!.GetSetMethod()!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, indexerSetter);
        il.Emit(OpCodes.Ret);

        runtime.TSTypedArraySet = method;
    }

    /// <summary>
    /// Emits Atomics static method helpers.
    /// </summary>
    private void EmitAtomicsHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var atomicsType = typeof(SharpTS.Runtime.Types.SharpTSAtomics);

        // Atomics.load(typedArray, index)
        runtime.AtomicsLoad = EmitAtomicsMethod(runtimeType, "AtomicsLoad", atomicsType, "Load",
            [_types.Object, _types.Double], _types.Object);

        // Atomics.store(typedArray, index, value)
        runtime.AtomicsStore = EmitAtomicsMethod(runtimeType, "AtomicsStore", atomicsType, "Store",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.add(typedArray, index, value)
        runtime.AtomicsAdd = EmitAtomicsMethod(runtimeType, "AtomicsAdd", atomicsType, "Add",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.sub(typedArray, index, value)
        runtime.AtomicsSub = EmitAtomicsMethod(runtimeType, "AtomicsSub", atomicsType, "Sub",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.and(typedArray, index, value)
        runtime.AtomicsAnd = EmitAtomicsMethod(runtimeType, "AtomicsAnd", atomicsType, "And",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.or(typedArray, index, value)
        runtime.AtomicsOr = EmitAtomicsMethod(runtimeType, "AtomicsOr", atomicsType, "Or",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.xor(typedArray, index, value)
        runtime.AtomicsXor = EmitAtomicsMethod(runtimeType, "AtomicsXor", atomicsType, "Xor",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.exchange(typedArray, index, value)
        runtime.AtomicsExchange = EmitAtomicsMethod(runtimeType, "AtomicsExchange", atomicsType, "Exchange",
            [_types.Object, _types.Double, _types.Object], _types.Object);

        // Atomics.compareExchange(typedArray, index, expectedValue, replacementValue)
        runtime.AtomicsCompareExchange = EmitAtomicsCompareExchangeMethod(runtimeType, atomicsType);

        // Atomics.wait(typedArray, index, value, timeout?)
        runtime.AtomicsWait = EmitAtomicsWaitMethod(runtimeType, atomicsType);

        // Atomics.notify(typedArray, index, count?)
        runtime.AtomicsNotify = EmitAtomicsNotifyMethod(runtimeType, atomicsType);

        // Atomics.isLockFree(size)
        runtime.AtomicsIsLockFree = EmitAtomicsIsLockFreeMethod(runtimeType, atomicsType);
    }

    private MethodBuilder EmitAtomicsMethod(TypeBuilder runtimeType, string methodName, Type atomicsType,
        string runtimeMethodName, Type[] paramTypes, Type returnType)
    {
        var method = runtimeType.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            paramTypes
        );

        var il = method.GetILGenerator();

        // Get the runtime method with matching signature
        var runtimeMethod = atomicsType.GetMethod(runtimeMethodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(SharpTS.Runtime.Types.SharpTSTypedArray), typeof(int), typeof(object)],
            null);

        if (runtimeMethod == null)
        {
            // Try without value parameter (for load)
            runtimeMethod = atomicsType.GetMethod(runtimeMethodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(SharpTS.Runtime.Types.SharpTSTypedArray), typeof(int)],
                null);
        }

        // Load and convert arguments
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);

        if (paramTypes.Length > 2)
        {
            il.Emit(OpCodes.Ldarg_2);  // value
        }

        il.Emit(OpCodes.Call, runtimeMethod!);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitAtomicsCompareExchangeMethod(TypeBuilder runtimeType, Type atomicsType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsCompareExchange",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Double, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        var runtimeMethod = atomicsType.GetMethod("CompareExchange",
            BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);  // expectedValue
        il.Emit(OpCodes.Ldarg_3);  // replacementValue
        il.Emit(OpCodes.Call, runtimeMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitAtomicsWaitMethod(TypeBuilder runtimeType, Type atomicsType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsWait",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Double, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        var runtimeMethod = atomicsType.GetMethod("Wait",
            BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);  // expectedValue

        // Handle nullable timeout
        var lblHasTimeout = il.DefineLabel();
        var lblEndTimeout = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, lblHasTimeout);

        // Has timeout - unbox and wrap in nullable
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Newobj, typeof(double?).GetConstructor([typeof(double)])!);
        il.Emit(OpCodes.Br, lblEndTimeout);

        il.MarkLabel(lblHasTimeout);
        var localNullableDouble = il.DeclareLocal(typeof(double?));
        il.Emit(OpCodes.Ldloca, localNullableDouble);
        il.Emit(OpCodes.Initobj, typeof(double?));
        il.Emit(OpCodes.Ldloc, localNullableDouble);

        il.MarkLabel(lblEndTimeout);
        il.Emit(OpCodes.Call, runtimeMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitAtomicsNotifyMethod(TypeBuilder runtimeType, Type atomicsType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsNotify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Double, _types.Object]
        );

        var il = method.GetILGenerator();

        var runtimeMethod = atomicsType.GetMethod("Notify",
            BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSTypedArray));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);

        // Handle nullable count
        var lblHasCount = il.DefineLabel();
        var lblEndCount = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, lblHasCount);

        // Has count - unbox and wrap in nullable
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, typeof(int?).GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Br, lblEndCount);

        il.MarkLabel(lblHasCount);
        var localNullableInt = il.DeclareLocal(typeof(int?));
        il.Emit(OpCodes.Ldloca, localNullableInt);
        il.Emit(OpCodes.Initobj, typeof(int?));
        il.Emit(OpCodes.Ldloc, localNullableInt);

        il.MarkLabel(lblEndCount);
        il.Emit(OpCodes.Call, runtimeMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitAtomicsIsLockFreeMethod(TypeBuilder runtimeType, Type atomicsType)
    {
        var method = runtimeType.DefineMethod(
            "AtomicsIsLockFree",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Double]
        );

        var il = method.GetILGenerator();

        var runtimeMethod = atomicsType.GetMethod("IsLockFree",
            BindingFlags.Public | BindingFlags.Static)!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, runtimeMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits MessageChannel constructor helper.
    /// </summary>
    private void EmitMessageChannelHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "CreateMessageChannel",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var ctor = typeof(SharpTS.Runtime.Types.SharpTSMessageChannel).GetConstructor(Type.EmptyTypes)!;

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);

        runtime.TSMessageChannelCtor = method;
    }

    /// <summary>
    /// Emits Worker constructor helper.
    /// </summary>
    private void EmitWorkerHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // CreateWorker(string filename, object? options, Interpreter? parentInterpreter)
        var method = runtimeType.DefineMethod(
            "CreateWorker",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var ctor = typeof(SharpTS.Runtime.Types.SharpTSWorker).GetConstructor(
            [typeof(string), typeof(SharpTS.Runtime.Types.SharpTSObject), typeof(SharpTS.Execution.Interpreter)])!;

        il.Emit(OpCodes.Ldarg_0);  // filename

        // Cast options to SharpTSObject (or null)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSObject));

        // Cast parentInterpreter
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, typeof(SharpTS.Execution.Interpreter));

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);

        runtime.TSWorkerCtor = method;
    }

    /// <summary>
    /// Emits StructuredClone helper.
    /// Accepts either null, a SharpTSArray (transfer list), or a SharpTSObject with { transfer: [...] }.
    /// </summary>
    private void EmitStructuredCloneHelper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "StructuredClone",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var cloneMethod = typeof(SharpTS.Runtime.Types.StructuredClone).GetMethod("Clone",
            BindingFlags.Public | BindingFlags.Static)!;
        var sharpTSObjectType = typeof(SharpTS.Runtime.Types.SharpTSObject);
        var sharpTSArrayType = typeof(SharpTS.Runtime.Types.SharpTSArray);

        var transferLocal = il.DeclareLocal(sharpTSArrayType);
        var isObjectLabel = il.DefineLabel();
        var isArrayLabel = il.DefineLabel();
        var callCloneLabel = il.DefineLabel();

        // Initialize transfer to null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, transferLocal);

        // Check if arg1 is null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, callCloneLabel);

        // Check if arg1 is SharpTSObject (options object)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, sharpTSObjectType);
        il.Emit(OpCodes.Brtrue, isObjectLabel);

        // Check if arg1 is SharpTSArray (transfer array directly)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, sharpTSArrayType);
        il.Emit(OpCodes.Brtrue, isArrayLabel);

        // Neither - just call with null transfer
        il.Emit(OpCodes.Br, callCloneLabel);

        // Handle SharpTSArray - use it directly as transfer
        il.MarkLabel(isArrayLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, sharpTSArrayType);
        il.Emit(OpCodes.Stloc, transferLocal);
        il.Emit(OpCodes.Br, callCloneLabel);

        // Handle SharpTSObject - extract "transfer" field
        il.MarkLabel(isObjectLabel);
        var valueLocal = il.DeclareLocal(_types.Object);
        var fieldsProperty = sharpTSObjectType.GetProperty("Fields")!.GetGetMethod()!;
        var tryGetValue = typeof(Dictionary<string, object?>).GetMethod("TryGetValue")!;

        // Get the Fields dictionary
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, sharpTSObjectType);
        il.Emit(OpCodes.Callvirt, fieldsProperty);

        // Try to get "transfer" from Fields
        il.Emit(OpCodes.Ldstr, "transfer");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, callCloneLabel);  // If not found, use null

        // Found transfer - cast to SharpTSArray
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, sharpTSArrayType);
        il.Emit(OpCodes.Stloc, transferLocal);

        // Call Clone(value, transfer)
        il.MarkLabel(callCloneLabel);
        il.Emit(OpCodes.Ldarg_0);       // value
        il.Emit(OpCodes.Ldloc, transferLocal);  // transfer (SharpTSArray or null)
        il.Emit(OpCodes.Call, cloneMethod);
        il.Emit(OpCodes.Ret);

        runtime.StructuredCloneClone = method;
    }

    /// <summary>
    /// Emits worker_threads module helper methods.
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
        var prop = typeof(SharpTS.Runtime.Types.WorkerThreads).GetProperty("IsMainThread")!.GetGetMethod()!;
        il.Emit(OpCodes.Call, prop);
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
        var prop2 = typeof(SharpTS.Runtime.Types.WorkerThreads).GetProperty("ThreadId")!.GetGetMethod()!;
        il2.Emit(OpCodes.Call, prop2);
        il2.Emit(OpCodes.Ret);
        runtime.WorkerThreadsThreadId = threadIdMethod;

        // receiveMessageOnPort
        var receiveMethod = runtimeType.DefineMethod(
            "WorkerThreadsReceiveMessageOnPort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il3 = receiveMethod.GetILGenerator();
        var receiveRuntimeMethod = typeof(SharpTS.Runtime.Types.WorkerThreads).GetMethod("ReceiveMessageOnPort")!;
        il3.Emit(OpCodes.Ldarg_0);
        il3.Emit(OpCodes.Castclass, typeof(SharpTS.Runtime.Types.SharpTSMessagePort));
        il3.Emit(OpCodes.Call, receiveRuntimeMethod);
        il3.Emit(OpCodes.Ret);
        runtime.WorkerThreadsReceiveMessageOnPort = receiveMethod;
    }
}
