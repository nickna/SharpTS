using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // $BoundTypedArrayMethod (#940): the callable returned by GetTypedArrayMember for a typed-array
    // bulk-method name. Stores (receiver, methodName); its Invoke(object[]) coerces args and
    // dispatches to the matching $TypedArray base method. Mirrors $BoundArrayMethod (the array
    // analog) and is dispatched through InvokeMethodValue / InvokeValue. Two-phase like the array
    // wrapper: the type/ctor/Invoke signature is defined before EmitRuntimeClass (so the invocation
    // helpers and GetTypedArrayMember can reference it); the Invoke body is finalized afterward.

    /// <summary>Phase 1: define $BoundTypedArrayMethod, its fields, ctor, and Invoke signature.</summary>
    internal void EmitBoundTypedArrayMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedTypedArrayImplementation arrays,
        EmittedNativeCallableRuntime? nativeCallables = null)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundTypedArrayMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        arrays.BoundMethodType = typeBuilder;

        var arrayField = typeBuilder.DefineField("_array", arrays.BaseType, FieldAttributes.Assembly);
        var nameField = typeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Assembly);
        arrays.BoundMethodArrayField = arrayField;
        arrays.BoundMethodNameField = nameField;

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [arrays.BaseType, _types.String]);
        arrays.BoundMethodCtor = ctor;
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_1); cil.Emit(OpCodes.Stfld, arrayField);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_2); cil.Emit(OpCodes.Stfld, nameField);
        cil.Emit(OpCodes.Ret);

        // Body emitted in Phase 2; signature now so InvokeValue/InvokeMethodValue can reference it.
        var invoke = typeBuilder.DefineMethod(
            "Invoke", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        arrays.BoundMethodInvoke = invoke;
        nativeCallables?.Register(typeBuilder, invoke);
    }

    /// <summary>Phase 2: emit Invoke body and create the type. Must run after EmitRuntimeClass
    /// (uses GetElement / TSArrayLengthGetter) and after the base bulk methods are defined.</summary>
    internal void EmitBoundTypedArrayMethodFinalize(EmittedRuntime runtime)
    {
        var arrays = runtime.TypedArrays.RequireImplementation();
        var typeBuilder = arrays.BoundMethodType;
        var arrayField = arrays.BoundMethodArrayField;
        var nameField = arrays.BoundMethodNameField;
        var il = arrays.BoundMethodInvoke.GetILGenerator();

        var stringEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var exCtor = _types.GetConstructor(_types.Exception, _types.String);

        // Locals reused across the set arm.
        var sourceLoc = il.DeclareLocal(_types.Object);
        var offsetLoc = il.DeclareLocal(_types.Int32);
        var lenLoc = il.DeclareLocal(_types.Int32);
        var iLoc = il.DeclareLocal(_types.Int32);
        var vLoc = il.DeclareLocal(_types.Object);
        var capturedLengthLoc = il.DeclareLocal(_types.Int32);

        void LoadArray()
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arrayField);
        }
        void LoadLength() => il.Emit(OpCodes.Ldloc, capturedLengthLoc);
        // ToIntegerOrInfinity represented by a saturating int; relative bounds are clamped by the method.
        void EmitArgAsInt(int index, Action loadDefault, bool undefinedUsesDefault = true)
        {
            var useDefault = il.DefineLabel();
            var done = il.DefineLabel();
            var number = il.DeclareLocal(_types.Double);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ble, useDefault);
            if (undefinedUsesDefault)
            {
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType); il.Emit(OpCodes.Brtrue, useDefault);
            }
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber); il.Emit(OpCodes.Stloc, number);
            var notNaN = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, number);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
            il.Emit(OpCodes.Brfalse, notNaN); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Br, done);
            il.MarkLabel(notNaN);
            var belowMax = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, number); il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
            il.Emit(OpCodes.Blt, belowMax); il.Emit(OpCodes.Ldc_I4, int.MaxValue); il.Emit(OpCodes.Br, done);
            il.MarkLabel(belowMax);
            var aboveMin = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, number); il.Emit(OpCodes.Ldc_R8, (double)int.MinValue);
            il.Emit(OpCodes.Bgt, aboveMin); il.Emit(OpCodes.Ldc_I4, int.MinValue); il.Emit(OpCodes.Br, done);
            il.MarkLabel(aboveMin);
            il.Emit(OpCodes.Ldloc, number); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDefault); loadDefault();
            il.MarkLabel(done);
        }
        void EmitRelativeArgAsInt(int index, Action loadDefault)
        {
            EmitArgAsInt(index, loadDefault);
            il.Emit(OpCodes.Stloc, offsetLoc);
            var nonnegative = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, nonnegative);
            LoadLength(); il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.MathMaxInt32);
            il.Emit(OpCodes.Stloc, offsetLoc); il.MarkLabel(nonnegative);
            il.Emit(OpCodes.Ldloc, offsetLoc);
        }
        void EmitArgOrUndefined(int index)
        {
            var useNull = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ble, useNull);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(useNull); il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.MarkLabel(done);
        }

        LoadArray();
        il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
        il.Emit(OpCodes.Stloc, capturedLengthLoc);

        // Dispatch chain on _methodName.
        var lFill = il.DefineLabel();
        var lCopyWithin = il.DefineLabel();
        var lReverse = il.DefineLabel();
        var lSet = il.DefineLabel();
        var lSlice = il.DefineLabel();
        var lSubarray = il.DefineLabel();
        var lIndexOf = il.DefineLabel();
        var lLastIndexOf = il.DefineLabel();
        var lIncludes = il.DefineLabel();
        var lJoin = il.DefineLabel();
        var lToString = il.DefineLabel();

        void Dispatch(string name, Label target)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, nameField);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, stringEquals);
            il.Emit(OpCodes.Brtrue, target);
        }
        Dispatch("fill", lFill);
        Dispatch("copyWithin", lCopyWithin);
        Dispatch("reverse", lReverse);
        Dispatch("set", lSet);
        Dispatch("slice", lSlice);
        Dispatch("subarray", lSubarray);
        Dispatch("indexOf", lIndexOf);
        Dispatch("lastIndexOf", lLastIndexOf);
        Dispatch("includes", lIncludes);
        Dispatch("join", lJoin);
        Dispatch("toString", lToString);
        // Unknown — return null (should not happen; GetTypedArrayMember only binds known names).
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // fill(value, start=0, end=length)
        il.MarkLabel(lFill);
        EmitArgOrUndefined(0); il.Emit(OpCodes.Stloc, vLoc);
        var numericFill = il.DefineLabel();
        var fillValueReady = il.DefineLabel();
        if (runtime.BigInt.Implementation is { } bigInt)
        {
            var bigintFill = il.DefineLabel();
            LoadArray(); il.Emit(OpCodes.Isinst, arrays.BigInt64ArrayType); il.Emit(OpCodes.Brtrue, bigintFill);
            LoadArray(); il.Emit(OpCodes.Isinst, arrays.BigUint64ArrayType); il.Emit(OpCodes.Brfalse, numericFill);
            il.MarkLabel(bigintFill);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Call, bigInt.ToBigInt); il.Emit(OpCodes.Stloc, vLoc);
            il.Emit(OpCodes.Br, fillValueReady);
        }
        il.MarkLabel(numericFill);
        il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber);
        il.Emit(OpCodes.Box, _types.Double); il.Emit(OpCodes.Stloc, vLoc);
        il.MarkLabel(fillValueReady);
        LoadArray();
        il.Emit(OpCodes.Ldloc, vLoc);
        EmitRelativeArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitRelativeArgAsInt(2, LoadLength);
        il.Emit(OpCodes.Callvirt, arrays.Fill);
        il.Emit(OpCodes.Ret);

        // copyWithin(target=0, start=0, end=length)
        il.MarkLabel(lCopyWithin);
        LoadArray();
        EmitRelativeArgAsInt(0, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitRelativeArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitRelativeArgAsInt(2, LoadLength);
        il.Emit(OpCodes.Callvirt, arrays.CopyWithin);
        il.Emit(OpCodes.Ret);

        // reverse()
        il.MarkLabel(lReverse);
        LoadArray();
        il.Emit(OpCodes.Callvirt, arrays.Reverse);
        il.Emit(OpCodes.Ret);

        // set(source, offset=0)
        il.MarkLabel(lSet);
        EmitArgOrUndefined(0); il.Emit(OpCodes.Stloc, sourceLoc);
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0)); il.Emit(OpCodes.Stloc, offsetLoc);
        var nonnegativeOffset = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, nonnegativeOffset);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor,
            "Offset is outside the target");
        il.MarkLabel(nonnegativeOffset);
        LoadArray(); il.Emit(OpCodes.Call, arrays.GetBuffer); il.Emit(OpCodes.Pop);
        LoadArray(); il.Emit(OpCodes.Callvirt, arrays.LengthGetter); il.Emit(OpCodes.Stloc, capturedLengthLoc);
        var offsetInBounds = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, offsetLoc); LoadLength(); il.Emit(OpCodes.Ble, offsetInBounds);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor,
            "Offset is outside the target");
        il.MarkLabel(offsetInBounds);
        // if (source is $TypedArray) -> base SetFrom handles range-check + fast/element-wise copy
        var notTyped = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Isinst, arrays.BaseType); il.Emit(OpCodes.Brfalse, notTyped);
        LoadArray();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Ldloc, offsetLoc);
        il.Emit(OpCodes.Callvirt, arrays.SetFrom);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        // else if (source is $Array) -> element-wise via GetElement (coerced by the element setter)
        il.MarkLabel(notTyped);
        var notArray = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Isinst, runtime.ArrayStorage.Type); il.Emit(OpCodes.Brfalse, notArray);
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Castclass, runtime.ArrayStorage.Type);
        il.Emit(OpCodes.Callvirt, runtime.ArrayStorage.LengthGetter); il.Emit(OpCodes.Stloc, lenLoc);
        // if (offset + len > _array.Length) throw RangeError
        var okRange = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenLoc); LoadLength();
        il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ble, okRange);
        il.Emit(OpCodes.Ldstr, "RangeError: Source too large for target");
        il.Emit(OpCodes.Newobj, exCtor); il.Emit(OpCodes.Throw);
        il.MarkLabel(okRange);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLoc);
        var setLoopCond = il.DefineLabel();
        var setLoopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, setLoopCond);
        il.MarkLabel(setLoopBody);
        LoadArray();
        il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Call, runtime.ObjectRead.Element);
        il.Emit(OpCodes.Callvirt, arrays.ElementSet);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(setLoopCond);
        il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Ldloc, lenLoc); il.Emit(OpCodes.Blt, setLoopBody);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        // else throw TypeError
        il.MarkLabel(notArray);
        il.Emit(OpCodes.Ldstr, "TypeError: Invalid source type for TypedArray.set");
        il.Emit(OpCodes.Newobj, exCtor); il.Emit(OpCodes.Throw);

        // slice(begin=0, end=length)
        il.MarkLabel(lSlice);
        LoadArray();
        EmitArgAsInt(0, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(1, LoadLength);
        il.Emit(OpCodes.Callvirt, arrays.Slice);
        il.Emit(OpCodes.Ret);

        // subarray(begin=0, end=length)
        il.MarkLabel(lSubarray);
        LoadArray();
        EmitArgAsInt(0, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(1, LoadLength);
        il.Emit(OpCodes.Callvirt, arrays.Subarray);
        il.Emit(OpCodes.Ret);

        void EmitSearch(Label label, MethodBuilder method, bool backwards, bool includes)
        {
            il.MarkLabel(label);
            var nonempty = il.DefineLabel();
            LoadLength(); il.Emit(OpCodes.Brtrue, nonempty);
            if (includes) { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Box, _types.Boolean); }
            else { il.Emit(OpCodes.Ldc_R8, -1.0); il.Emit(OpCodes.Box, _types.Double); }
            il.Emit(OpCodes.Ret); il.MarkLabel(nonempty);
            LoadArray(); EmitArgOrUndefined(0);
            EmitArgAsInt(1, () =>
            {
                if (backwards) { LoadLength(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); }
                else il.Emit(OpCodes.Ldc_I4_0);
            }, undefinedUsesDefault: false);
            il.Emit(OpCodes.Stloc, offsetLoc);
            var nonnegative = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bge, nonnegative);
            LoadLength(); il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Add);
            if (!backwards)
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
            }
            il.Emit(OpCodes.Stloc, offsetLoc); il.MarkLabel(nonnegative);
            il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Callvirt, method);
            il.Emit(OpCodes.Box, includes ? _types.Boolean : _types.Double); il.Emit(OpCodes.Ret);
        }
        EmitSearch(lIndexOf, arrays.IndexOf, backwards: false, includes: false);
        EmitSearch(lLastIndexOf, arrays.LastIndexOf, backwards: true, includes: false);
        EmitSearch(lIncludes, arrays.Includes, backwards: false, includes: true);

        // join(separator=",")
        il.MarkLabel(lJoin);
        LoadArray();
        {
            var useDefault = il.DefineLabel();
            var doneSep = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ble, useDefault);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Stloc, vLoc);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType); il.Emit(OpCodes.Brtrue, useDefault);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Call, runtime.StringCoercion.ToJsString); il.Emit(OpCodes.Br, doneSep);
            il.MarkLabel(useDefault); il.Emit(OpCodes.Ldstr, ",");
            il.MarkLabel(doneSep);
        }
        il.Emit(OpCodes.Callvirt, arrays.Join);
        il.Emit(OpCodes.Ret);

        // toString()
        il.MarkLabel(lToString);
        LoadArray();
        il.Emit(OpCodes.Callvirt, arrays.ToStringJoin);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
