using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // TypedArray bulk instance methods (#940), emitted on the abstract $TypedArray base type so
    // every concrete $XArray inherits them. They mirror the interpreter's GetMember surface in
    // Runtime/Types/SharpTSTypedArray.cs (Fill 216, Set 171, CopyWithin 245, Reverse 269,
    // Slice/Subarray per-subclass, IndexOf 312, LastIndexOf 328, Includes 345, Join/toString
    // 451-470, ElementEquals 363) — including the same clamping/coercion, so interpreter and
    // compiled output match byte-for-byte. Everything here is BCL-only (Buffer.BlockCopy,
    // Array.Copy, Math.Min/Max, string.Join, double.IsNaN, object.Equals) plus the base type's
    // own fields/abstractions, so the emitted token never references SharpTS.dll — standalone
    // DLLs stay standalone. The element-converting paths go through the virtual Get/Set so each
    // concrete type's per-element coercion/clamping applies.
    private void EmitTypedArrayBulkMethods(TypeBuilder t, EmittedRuntime runtime)
    {
        var minI = typeof(Math).GetMethod("Min", [_types.Int32, _types.Int32])!;
        var maxI = typeof(Math).GetMethod("Max", [_types.Int32, _types.Int32])!;
        var blockCopy = typeof(System.Buffer).GetMethod(
            "BlockCopy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!;
        var arrayCopy = typeof(Array).GetMethod(
            "Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!;
        var objToString = _types.GetMethodNoParams(_types.Object, "ToString");

        var elementEquals = EmitTaElementEquals(t);

        EmitTypedArrayFill(t, runtime, minI, maxI, blockCopy);
        EmitTypedArrayCopyWithin(t, runtime, minI, arrayCopy);
        EmitTypedArrayReverse(t, runtime);
        EmitTypedArraySetFrom(t, runtime, blockCopy);
        EmitTypedArrayIndexOf(t, runtime, maxI, elementEquals);
        EmitTypedArrayLastIndexOf(t, runtime, minI, elementEquals);
        EmitTypedArrayIncludes(t, runtime);
        EmitTypedArrayJoin(t, runtime, objToString);
        EmitTypedArrayToStringJoin(t, runtime);
        EmitTypedArraySlice(t, runtime, minI, maxI, blockCopy);
        EmitTypedArraySubarray(t, runtime, minI, maxI);
    }

    /// <summary>
    /// Loads typed-array arg <paramref name="argIndex"/> (an int element index), applies JS
    /// relative-index clamping against <c>_length</c> (`v &lt; 0 ? Max(_length+v,0) : Min(v,_length)`,
    /// matching slice/subarray in the interpreter), and stores into <paramref name="dest"/>.
    /// </summary>
    private void EmitRelativeClampToLocal(ILGenerator il, int argIndex, LocalBuilder dest, MethodInfo minI, MethodInfo maxI)
    {
        var negLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, negLabel);
        // v >= 0: Min(v, _length)
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(negLabel);
        // v < 0: Max(_length + v, 0)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, maxI);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Stloc, dest);
    }

    // object Fill(object value, int start, int end) — clamp start/end into [0,_length] (no
    // negative wraparound, matching the interpreter), coerce the value once via the virtual Set,
    // then byte-replicate it across the range with an exponential-doubling BlockCopy. Returns this.
    private void EmitTypedArrayFill(TypeBuilder t, EmittedRuntime runtime, MethodInfo minI, MethodInfo maxI, MethodInfo blockCopy)
    {
        var m = t.DefineMethod("Fill", MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Object, _types.Int32, _types.Int32]);
        runtime.TypedArrayFill = m;
        var il = m.GetILGenerator();
        var startLoc = il.DeclareLocal(_types.Int32);
        var endLoc = il.DeclareLocal(_types.Int32);
        var bpeLoc = il.DeclareLocal(_types.Int32);
        var baseOffLoc = il.DeclareLocal(_types.Int32);
        var totalLoc = il.DeclareLocal(_types.Int32);
        var filledLoc = il.DeclareLocal(_types.Int32);
        var chunkLoc = il.DeclareLocal(_types.Int32);

        // start = Max(0, Min(start, _length))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, startLoc);
        // end = Max(start, Min(end, _length))
        il.Emit(OpCodes.Ldloc, startLoc);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, endLoc);

        // if (end <= start) return this
        var doFill = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLoc);
        il.Emit(OpCodes.Ldloc, startLoc);
        il.Emit(OpCodes.Bgt, doFill);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(doFill);

        // this.Set(start, value)  (coerce once)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLoc);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);

        // bpe = BytesPerElement
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!);
        il.Emit(OpCodes.Stloc, bpeLoc);
        // baseOff = _byteOffset + start*bpe
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, startLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, baseOffLoc);
        // total = (end - start) * bpe
        il.Emit(OpCodes.Ldloc, endLoc); il.Emit(OpCodes.Ldloc, startLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, totalLoc);
        // filled = bpe
        il.Emit(OpCodes.Ldloc, bpeLoc);
        il.Emit(OpCodes.Stloc, filledLoc);

        // while (filled < total) { chunk = Min(filled, total-filled);
        //   Buffer.BlockCopy(_buffer, baseOff, _buffer, baseOff+filled, chunk); filled += chunk; }
        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldloc, filledLoc);
        il.Emit(OpCodes.Ldloc, totalLoc); il.Emit(OpCodes.Ldloc, filledLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Stloc, chunkLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldloc, baseOffLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldloc, baseOffLoc); il.Emit(OpCodes.Ldloc, filledLoc); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, chunkLoc);
        il.Emit(OpCodes.Call, blockCopy);
        il.Emit(OpCodes.Ldloc, filledLoc); il.Emit(OpCodes.Ldloc, chunkLoc); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, filledLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, filledLoc);
        il.Emit(OpCodes.Ldloc, totalLoc);
        il.Emit(OpCodes.Blt, loopBody);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // object CopyWithin(int target, int start, int end) — clamp, compute count, memmove on the
    // backing buffer with Array.Copy (overlap-safe, matching the interpreter's Span.CopyTo).
    private void EmitTypedArrayCopyWithin(TypeBuilder t, EmittedRuntime runtime, MethodInfo minI, MethodInfo arrayCopy)
    {
        var maxI = typeof(Math).GetMethod("Max", [_types.Int32, _types.Int32])!;
        var m = t.DefineMethod("CopyWithin", MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Int32, _types.Int32, _types.Int32]);
        runtime.TypedArrayCopyWithin = m;
        var il = m.GetILGenerator();
        var targetLoc = il.DeclareLocal(_types.Int32);
        var startLoc = il.DeclareLocal(_types.Int32);
        var endLoc = il.DeclareLocal(_types.Int32);
        var countLoc = il.DeclareLocal(_types.Int32);
        var bpeLoc = il.DeclareLocal(_types.Int32);

        // target = Max(0, Min(target, _length))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI); il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, targetLoc);
        // start = Max(0, Min(start, _length))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI); il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, startLoc);
        // end = Max(start, Min(end, _length))
        il.Emit(OpCodes.Ldloc, startLoc);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Call, minI); il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, endLoc);
        // count = Min(end - start, _length - target)
        il.Emit(OpCodes.Ldloc, endLoc); il.Emit(OpCodes.Ldloc, startLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!); il.Emit(OpCodes.Ldloc, targetLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Stloc, countLoc);

        // if (count <= 0) return this
        var doCopy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, doCopy);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(doCopy);

        // bpe = BytesPerElement
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!);
        il.Emit(OpCodes.Stloc, bpeLoc);
        // Array.Copy(_buffer, _byteOffset+start*bpe, _buffer, _byteOffset+target*bpe, count*bpe)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, startLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, targetLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, countLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Call, arrayCopy);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // object Reverse() — two-pointer element-wise swap via the virtual Get/Set. Returns this.
    private void EmitTypedArrayReverse(TypeBuilder t, EmittedRuntime runtime)
    {
        var m = t.DefineMethod("Reverse", MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, Type.EmptyTypes);
        runtime.TypedArrayReverse = m;
        var il = m.GetILGenerator();
        var leftLoc = il.DeclareLocal(_types.Int32);
        var rightLoc = il.DeclareLocal(_types.Int32);
        var tmpLoc = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, leftLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, rightLoc);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        // tmp = Get(left)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, leftLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Stloc, tmpLoc);
        // Set(left, Get(right))
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, leftLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, rightLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        // Set(right, tmp)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, rightLoc); il.Emit(OpCodes.Ldloc, tmpLoc);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        // left++; right--
        il.Emit(OpCodes.Ldloc, leftLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, leftLoc);
        il.Emit(OpCodes.Ldloc, rightLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, rightLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, leftLoc);
        il.Emit(OpCodes.Ldloc, rightLoc);
        il.Emit(OpCodes.Blt, loopBody);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // object SetFrom(object source, int offset) — source is always another $TypedArray (the
    // wrapper routes array-likes itself). Range-checks (throws guest-catchable RangeError),
    // same-concrete-type → Buffer.BlockCopy fast path (offset >= 0), else element-wise via Get/Set
    // (the negative-offset case falls here and the element setter raises its own bounds error,
    // matching the interpreter). Returns null (interpreter's set returns null).
    private void EmitTypedArraySetFrom(TypeBuilder t, EmittedRuntime runtime, MethodInfo blockCopy)
    {
        var getType = _types.GetMethodNoParams(_types.Object, "GetType");
        var m = t.DefineMethod("SetFrom", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object, _types.Int32]);
        runtime.TypedArraySetFrom = m;
        var il = m.GetILGenerator();
        var tsLoc = il.DeclareLocal(runtime.TypedArrayBaseType);
        var srcLenLoc = il.DeclareLocal(_types.Int32);
        var bpeLoc = il.DeclareLocal(_types.Int32);
        var iLoc = il.DeclareLocal(_types.Int32);

        // ts = ($TypedArray)source
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Stloc, tsLoc);
        // srcLen = ts._length
        il.Emit(OpCodes.Ldloc, tsLoc); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Stloc, srcLenLoc);

        // if (offset + srcLen > _length) throw RangeError
        var okRange = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloc, srcLenLoc); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Ble, okRange);
        il.Emit(OpCodes.Ldstr, "RangeError: Source too large for target");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(okRange);

        // fast path: ts.GetType() == this.GetType() && offset >= 0
        var elementWise = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsLoc); il.Emit(OpCodes.Callvirt, getType);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, getType);
        il.Emit(OpCodes.Bne_Un, elementWise);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Blt, elementWise);
        // bpe = BytesPerElement
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!);
        il.Emit(OpCodes.Stloc, bpeLoc);
        // Buffer.BlockCopy(ts._buffer, ts._byteOffset, _buffer, _byteOffset + offset*bpe, srcLen*bpe)
        il.Emit(OpCodes.Ldloc, tsLoc); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldloc, tsLoc); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, srcLenLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Call, blockCopy);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // element-wise: for (i=0;i<srcLen;i++) this.Set(offset+i, ts.Get(i))
        il.MarkLabel(elementWise);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLoc);
        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, tsLoc); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldloc, srcLenLoc);
        il.Emit(OpCodes.Blt, loopBody);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    // double IndexOf(object value, int fromIndex) — forward linear scan; -1 if not found.
    private void EmitTypedArrayIndexOf(TypeBuilder t, EmittedRuntime runtime, MethodInfo maxI, MethodInfo elementEquals)
    {
        var m = t.DefineMethod("IndexOf", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Double, [_types.Object, _types.Int32]);
        runtime.TypedArrayIndexOf = m;
        var il = m.GetILGenerator();
        var iLoc = il.DeclareLocal(_types.Int32);

        // i = Max(0, fromIndex)
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, iLoc);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, elementEquals);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!);
        il.Emit(OpCodes.Blt, loopBody);

        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    // double LastIndexOf(object value, int fromIndex) — backward scan from Min(fromIndex,_length-1).
    private void EmitTypedArrayLastIndexOf(TypeBuilder t, EmittedRuntime runtime, MethodInfo minI, MethodInfo elementEquals)
    {
        var m = t.DefineMethod("LastIndexOf", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Double, [_types.Object, _types.Int32]);
        runtime.TypedArrayLastIndexOf = m;
        var il = m.GetILGenerator();
        var iLoc = il.DeclareLocal(_types.Int32);

        // i = Min(fromIndex, _length - 1)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, minI);
        il.Emit(OpCodes.Stloc, iLoc);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, elementEquals);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, loopBody);

        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    // bool Includes(object value, int fromIndex) — IndexOf(...) >= 0.
    private void EmitTypedArrayIncludes(TypeBuilder t, EmittedRuntime runtime)
    {
        var m = t.DefineMethod("Includes", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Boolean, [_types.Object, _types.Int32]);
        runtime.TypedArrayIncludes = m;
        var il = m.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayIndexOf);
        // result >= 0  ==  !(result < 0)
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
    }

    // string Join(string sep) — boxed-element ToString() joined by sep (matches interpreter).
    private void EmitTypedArrayJoin(TypeBuilder t, EmittedRuntime runtime, MethodInfo objToString)
    {
        var stringJoin = typeof(string).GetMethod("Join", [_types.String, typeof(string[])])!;
        var m = t.DefineMethod("Join", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.String, [_types.String]);
        runtime.TypedArrayJoin = m;
        var il = m.GetILGenerator();
        var nLoc = il.DeclareLocal(_types.Int32);
        var partsLoc = il.DeclareLocal(typeof(string[]));
        var iLoc = il.DeclareLocal(_types.Int32);
        var elLoc = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayLengthField!); il.Emit(OpCodes.Stloc, nLoc);
        il.Emit(OpCodes.Ldloc, nLoc); il.Emit(OpCodes.Newarr, _types.String); il.Emit(OpCodes.Stloc, partsLoc);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLoc);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var notNull = il.DefineLabel();
        var store = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
        il.Emit(OpCodes.Stloc, elLoc);
        il.Emit(OpCodes.Ldloc, partsLoc); il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldloc, elLoc); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldstr, ""); il.Emit(OpCodes.Br, store);
        il.MarkLabel(notNull);
        il.Emit(OpCodes.Callvirt, objToString);
        il.MarkLabel(store);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldloc, nLoc); il.Emit(OpCodes.Blt, loopBody);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, partsLoc);
        il.Emit(OpCodes.Call, stringJoin);
        il.Emit(OpCodes.Ret);
    }

    // string ToStringJoin() — toString() == Join(",").
    private void EmitTypedArrayToStringJoin(TypeBuilder t, EmittedRuntime runtime)
    {
        var m = t.DefineMethod("ToStringJoin", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        runtime.TypedArrayToStringJoin = m;
        var il = m.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, ","); il.Emit(OpCodes.Callvirt, runtime.TypedArrayJoin);
        il.Emit(OpCodes.Ret);
    }

    // $TypedArray Slice(int begin, int end) — fresh same-kind array containing the copied range.
    private void EmitTypedArraySlice(TypeBuilder t, EmittedRuntime runtime, MethodInfo minI, MethodInfo maxI, MethodInfo blockCopy)
    {
        var m = t.DefineMethod("Slice", MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Int32, _types.Int32]);
        runtime.TypedArraySlice = m;
        var il = m.GetILGenerator();
        var beginLoc = il.DeclareLocal(_types.Int32);
        var endLoc = il.DeclareLocal(_types.Int32);
        var countLoc = il.DeclareLocal(_types.Int32);
        var destLoc = il.DeclareLocal(runtime.TypedArrayBaseType);
        var bpeLoc = il.DeclareLocal(_types.Int32);

        EmitRelativeClampToLocal(il, 1, beginLoc, minI, maxI);
        EmitRelativeClampToLocal(il, 2, endLoc, minI, maxI);
        // count = Max(0, end - begin)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, endLoc); il.Emit(OpCodes.Ldloc, beginLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, countLoc);
        // dest = CreateOfLength(count)
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, countLoc); il.Emit(OpCodes.Callvirt, _typedArrayCreateOfLength!);
        il.Emit(OpCodes.Stloc, destLoc);

        // if (count > 0) BlockCopy(_buffer, _byteOffset+begin*bpe, dest._buffer, dest._byteOffset, count*bpe)
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLoc); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ble, skip);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!); il.Emit(OpCodes.Stloc, bpeLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, beginLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, destLoc); il.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        il.Emit(OpCodes.Ldloc, destLoc); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, countLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Call, blockCopy);
        il.MarkLabel(skip);

        il.Emit(OpCodes.Ldloc, destLoc);
        il.Emit(OpCodes.Ret);
    }

    // $TypedArray Subarray(int begin, int end) — view sharing the backing buffer.
    private void EmitTypedArraySubarray(TypeBuilder t, EmittedRuntime runtime, MethodInfo minI, MethodInfo maxI)
    {
        var m = t.DefineMethod("Subarray", MethodAttributes.Public | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Int32, _types.Int32]);
        runtime.TypedArraySubarray = m;
        var il = m.GetILGenerator();
        var beginLoc = il.DeclareLocal(_types.Int32);
        var endLoc = il.DeclareLocal(_types.Int32);
        var countLoc = il.DeclareLocal(_types.Int32);
        var bpeLoc = il.DeclareLocal(_types.Int32);

        EmitRelativeClampToLocal(il, 1, beginLoc, minI, maxI);
        EmitRelativeClampToLocal(il, 2, endLoc, minI, maxI);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, endLoc); il.Emit(OpCodes.Ldloc, beginLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, maxI);
        il.Emit(OpCodes.Stloc, countLoc);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!); il.Emit(OpCodes.Stloc, bpeLoc);
        // return CreateView(_byteOffset + begin*bpe, count)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _typedArrayByteOffsetField!);
        il.Emit(OpCodes.Ldloc, beginLoc); il.Emit(OpCodes.Ldloc, bpeLoc); il.Emit(OpCodes.Mul); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, countLoc);
        il.Emit(OpCodes.Callvirt, _typedArrayCreateView!);
        il.Emit(OpCodes.Ret);
    }

    // static bool TaElementEquals(object a, object b) — replicates SharpTSTypedArray.ElementEquals:
    // NaN-aware double compare (NaN equals NaN, used by includes' SameValueZero), else object.Equals.
    private MethodBuilder EmitTaElementEquals(TypeBuilder t)
    {
        var objEquals = typeof(object).GetMethod("Equals", [_types.Object, _types.Object])!;
        var isNaN = typeof(double).GetMethod("IsNaN", [_types.Double])!;
        var m = t.DefineMethod("TaElementEquals",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            _types.Boolean, [_types.Object, _types.Object]);
        var il = m.GetILGenerator();
        var daLoc = il.DeclareLocal(_types.Double);
        var dbLoc = il.DeclareLocal(_types.Double);
        var notBoth = il.DefineLabel();
        var checkNaN = il.DefineLabel();
        var retFalse = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, _types.Double); il.Emit(OpCodes.Brfalse, notBoth);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, _types.Double); il.Emit(OpCodes.Brfalse, notBoth);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Unbox_Any, _types.Double); il.Emit(OpCodes.Stloc, daLoc);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Unbox_Any, _types.Double); il.Emit(OpCodes.Stloc, dbLoc);
        // if (da == db) return true
        il.Emit(OpCodes.Ldloc, daLoc); il.Emit(OpCodes.Ldloc, dbLoc); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Brfalse, checkNaN);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        // return IsNaN(da) && IsNaN(db)
        il.MarkLabel(checkNaN);
        il.Emit(OpCodes.Ldloc, daLoc); il.Emit(OpCodes.Call, isNaN); il.Emit(OpCodes.Brfalse, retFalse);
        il.Emit(OpCodes.Ldloc, dbLoc); il.Emit(OpCodes.Call, isNaN); il.Emit(OpCodes.Ret);
        il.MarkLabel(retFalse);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        // return object.Equals(a, b)
        il.MarkLabel(notBoth);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, objEquals); il.Emit(OpCodes.Ret);

        return m;
    }

    // Per-concrete buffer-sharing ctor + CreateOfLength/CreateView overrides backing the base
    // Slice/Subarray (#940). Emitted inside EmitConcreteTypedArrayType, before CreateType.
    private void EmitTypedArrayFactoryMembers(TypeBuilder t, EmittedRuntime runtime, ConstructorBuilder lengthCtor)
    {
        // public $XArray(byte[] buffer, int byteOffset, int length, object arrayBuffer) : base(...)
        var viewCtor = t.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [typeof(byte[]), _types.Int32, _types.Int32, _types.Object]);
        var cil = viewCtor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_1);
        cil.Emit(OpCodes.Ldarg_2);
        cil.Emit(OpCodes.Ldarg_3);
        cil.Emit(OpCodes.Ldarg, 4);
        cil.Emit(OpCodes.Call, runtime.TypedArrayBaseCtor);
        cil.Emit(OpCodes.Ret);

        // protected override $TypedArray CreateOfLength(int length) => new $XArray(length)
        var col = t.DefineMethod("CreateOfLength",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Int32]);
        var colIl = col.GetILGenerator();
        colIl.Emit(OpCodes.Ldarg_1);
        colIl.Emit(OpCodes.Newobj, lengthCtor);
        colIl.Emit(OpCodes.Ret);
        t.DefineMethodOverride(col, _typedArrayCreateOfLength!);

        // protected override $TypedArray CreateView(int byteOffset, int length)
        //   => new $XArray(_buffer, byteOffset, length, _arrayBuffer)
        var cv = t.DefineMethod("CreateView",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            runtime.TypedArrayBaseType, [_types.Int32, _types.Int32]);
        var cvIl = cv.GetILGenerator();
        cvIl.Emit(OpCodes.Ldarg_0); cvIl.Emit(OpCodes.Ldfld, _typedArrayBufferField!);
        cvIl.Emit(OpCodes.Ldarg_1);
        cvIl.Emit(OpCodes.Ldarg_2);
        cvIl.Emit(OpCodes.Ldarg_0); cvIl.Emit(OpCodes.Ldfld, _typedArrayArrayBufferField!);
        cvIl.Emit(OpCodes.Newobj, viewCtor);
        cvIl.Emit(OpCodes.Ret);
        t.DefineMethodOverride(cv, _typedArrayCreateView!);
    }
}
