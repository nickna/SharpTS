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
    internal void EmitBoundTypedArrayMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundTypedArrayMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundTypedArrayMethodType = typeBuilder;

        var arrayField = typeBuilder.DefineField("_array", runtime.TypedArrayBaseType, FieldAttributes.Assembly);
        var nameField = typeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Assembly);
        runtime.BoundTypedArrayMethodArrayField = arrayField;
        runtime.BoundTypedArrayMethodNameField = nameField;

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [runtime.TypedArrayBaseType, _types.String]);
        runtime.BoundTypedArrayMethodCtor = ctor;
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_1); cil.Emit(OpCodes.Stfld, arrayField);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_2); cil.Emit(OpCodes.Stfld, nameField);
        cil.Emit(OpCodes.Ret);

        // Body emitted in Phase 2; signature now so InvokeValue/InvokeMethodValue can reference it.
        var invoke = typeBuilder.DefineMethod(
            "Invoke", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        runtime.BoundTypedArrayMethodInvoke = invoke;
    }

    /// <summary>Phase 2: emit Invoke body and create the type. Must run after EmitRuntimeClass
    /// (uses GetElement / TSArrayLengthGetter) and after the base bulk methods are defined.</summary>
    internal void EmitBoundTypedArrayMethodFinalize(EmittedRuntime runtime)
    {
        var typeBuilder = runtime.BoundTypedArrayMethodType;
        var arrayField = runtime.BoundTypedArrayMethodArrayField;
        var nameField = runtime.BoundTypedArrayMethodNameField;
        var il = runtime.BoundTypedArrayMethodInvoke.GetILGenerator();

        var stringEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var objToString = _types.GetMethodNoParams(_types.Object, "ToString");
        var exCtor = _types.GetConstructor(_types.Exception, _types.String);

        // Locals reused across the set arm.
        var sourceLoc = il.DeclareLocal(_types.Object);
        var offsetLoc = il.DeclareLocal(_types.Int32);
        var lenLoc = il.DeclareLocal(_types.Int32);
        var iLoc = il.DeclareLocal(_types.Int32);
        var vLoc = il.DeclareLocal(_types.Object);

        void LoadArray()
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arrayField);
        }
        void LoadLength()
        {
            LoadArray();
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
        }
        // args[index] coerced to int (truncate toward zero), or loadDefault() when absent/non-number.
        void EmitArgAsInt(int index, Action loadDefault)
        {
            var useDefault = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ble, useDefault);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Isinst, _types.Double); il.Emit(OpCodes.Brfalse, useDefault);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Double); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDefault); loadDefault();
            il.MarkLabel(done);
        }
        void EmitArgZeroOrNull(int index)
        {
            var useNull = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ble, useNull);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(useNull); il.Emit(OpCodes.Ldnull);
            il.MarkLabel(done);
        }

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
        LoadArray();
        EmitArgZeroOrNull(0);
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(2, LoadLength);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayFill);
        il.Emit(OpCodes.Ret);

        // copyWithin(target=0, start=0, end=length)
        il.MarkLabel(lCopyWithin);
        LoadArray();
        EmitArgAsInt(0, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(2, LoadLength);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayCopyWithin);
        il.Emit(OpCodes.Ret);

        // reverse()
        il.MarkLabel(lReverse);
        LoadArray();
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayReverse);
        il.Emit(OpCodes.Ret);

        // set(source, offset=0)
        il.MarkLabel(lSet);
        EmitArgZeroOrNull(0); il.Emit(OpCodes.Stloc, sourceLoc);
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0)); il.Emit(OpCodes.Stloc, offsetLoc);
        // if (source is $TypedArray) -> base SetFrom handles range-check + fast/element-wise copy
        var notTyped = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType); il.Emit(OpCodes.Brfalse, notTyped);
        LoadArray();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Ldloc, offsetLoc);
        il.Emit(OpCodes.Callvirt, runtime.TypedArraySetFrom);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        // else if (source is $Array) -> element-wise via GetElement (coerced by the element setter)
        il.MarkLabel(notTyped);
        var notArray = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Isinst, runtime.TSArrayType); il.Emit(OpCodes.Brfalse, notArray);
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLengthGetter); il.Emit(OpCodes.Stloc, lenLoc);
        // if (offset + len > _array.Length) throw RangeError
        var okRange = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, offsetLoc); il.Emit(OpCodes.Ldloc, lenLoc); il.Emit(OpCodes.Add);
        LoadLength();
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
        il.Emit(OpCodes.Ldloc, sourceLoc); il.Emit(OpCodes.Ldloc, iLoc); il.Emit(OpCodes.Call, runtime.GetElement);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementSet);
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
        il.Emit(OpCodes.Callvirt, runtime.TypedArraySlice);
        il.Emit(OpCodes.Ret);

        // subarray(begin=0, end=length)
        il.MarkLabel(lSubarray);
        LoadArray();
        EmitArgAsInt(0, () => il.Emit(OpCodes.Ldc_I4_0));
        EmitArgAsInt(1, LoadLength);
        il.Emit(OpCodes.Callvirt, runtime.TypedArraySubarray);
        il.Emit(OpCodes.Ret);

        // indexOf(value, fromIndex=0) -> double
        il.MarkLabel(lIndexOf);
        LoadArray();
        EmitArgZeroOrNull(0);
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayIndexOf);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // lastIndexOf(value, fromIndex=length-1) -> double
        il.MarkLabel(lLastIndexOf);
        LoadArray();
        EmitArgZeroOrNull(0);
        EmitArgAsInt(1, () => { LoadLength(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Sub); });
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayLastIndexOf);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // includes(value, fromIndex=0) -> bool
        il.MarkLabel(lIncludes);
        LoadArray();
        EmitArgZeroOrNull(0);
        EmitArgAsInt(1, () => il.Emit(OpCodes.Ldc_I4_0));
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayIncludes);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // join(separator=",")
        il.MarkLabel(lJoin);
        LoadArray();
        {
            var useDefault = il.DefineLabel();
            var doneSep = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ble, useDefault);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Stloc, vLoc);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Brfalse, useDefault);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Isinst, runtime.UndefinedType); il.Emit(OpCodes.Brtrue, useDefault);
            il.Emit(OpCodes.Ldloc, vLoc); il.Emit(OpCodes.Callvirt, objToString); il.Emit(OpCodes.Br, doneSep);
            il.MarkLabel(useDefault); il.Emit(OpCodes.Ldstr, ",");
            il.MarkLabel(doneSep);
        }
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayJoin);
        il.Emit(OpCodes.Ret);

        // toString()
        il.MarkLabel(lToString);
        LoadArray();
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayToStringJoin);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
