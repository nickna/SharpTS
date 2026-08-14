using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitRequireWritableArrayLength(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder receiver)
    {
        var descriptor = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var writable = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Brfalse, writable);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, writable);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Cannot assign to read only array length");
        il.MarkLabel(writable);
    }

    /// <summary>
    /// Emits frozen/sealed/extensibility check for array mutation methods.
    /// If frozen (or sealed when checkSealed=true, or non-extensible when checkExtensible=true), branches to returnLabel.
    /// </summary>
    private void EmitArrayFrozenSealedCheck(
        ILGenerator il,
        EmittedRuntime runtime,
        Label returnLabel,
        bool checkSealed = true,
        bool checkExtensible = true)
    {
        var checkLocal = il.DeclareLocal(_types.Object);

        // Check frozen
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);  // list
        il.Emit(OpCodes.Ldloca, checkLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ConditionalWeakTable, "TryGetValue",
            _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, returnLabel);

        if (checkSealed)
        {
            // Check sealed
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, checkLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.ConditionalWeakTable, "TryGetValue",
                _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brtrue, returnLabel);
        }

        if (checkExtensible)
        {
            // Check extensibility via $PropertyDescriptorStore.IsExtensible - fully standalone, no reflection
            // If NOT extensible, branch to return
            il.Emit(OpCodes.Ldarg_0);  // obj
            il.Emit(OpCodes.Call, runtime.PDSIsExtensible);
            il.Emit(OpCodes.Brfalse, returnLabel);
        }
    }

    /// <summary>
    /// Emits frozen/sealed check that throws TypeError if array is frozen/sealed.
    /// </summary>
    private void EmitArrayFrozenSealedThrowCheck(
        ILGenerator il,
        EmittedRuntime runtime,
        bool checkSealed = true)
    {
        var checkLocal = il.DeclareLocal(_types.Object);
        var notFrozenLabel = il.DefineLabel();
        var notSealedLabel = il.DefineLabel();

        // Check frozen
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, checkLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ConditionalWeakTable, "TryGetValue",
            _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Throw TypeError
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot modify a frozen or sealed array");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFrozenLabel);

        if (checkSealed)
        {
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, checkLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.ConditionalWeakTable, "TryGetValue",
                _types.Object, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, notSealedLabel);

            il.Emit(OpCodes.Ldstr, "TypeError: Cannot modify a frozen or sealed array");
            il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notSealedLabel);
        }
    }

    /// <summary>
    /// Emits ToObject(receiver) and LengthOfArrayLike using the full safe-integer
    /// range. Mutating prototype methods keep this original object rather than
    /// materializing a detached List, because their indexed writes and deletes
    /// are observable on generic receivers.
    /// </summary>
    private (LocalBuilder Receiver, LocalBuilder Length) EmitGenericArrayReceiverAndLength(
        ILGenerator il, EmittedRuntime runtime)
    {
        var receiver = il.DeclareLocal(_types.Object);
        var length = il.DeclareLocal(_types.Double);

        var receiverPresent = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, receiverPresent);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array method called on null or undefined");
        il.MarkLabel(receiverPresent);
        var receiverDefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, receiverDefined);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array method called on null or undefined");
        il.MarkLabel(receiverDefined);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToObjectMethod);
        il.Emit(OpCodes.Stloc, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, length);

        var useZero = il.DefineLabel();
        var finitePositive = il.DefineLabel();
        var done = il.DefineLabel();
        // NaN and non-positive values become zero.
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, useZero);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, finitePositive);
        il.Emit(OpCodes.Br, useZero);

        il.MarkLabel(finitePositive);
        // +Infinity and larger finite values clamp to 2^53 - 1.
        var truncate = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 9007199254740991.0);
        il.Emit(OpCodes.Blt, truncate);
        il.Emit(OpCodes.Ldc_R8, 9007199254740991.0);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(truncate);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(useZero);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(done);
        return (receiver, length);
    }

    /// <summary>
    /// Emits the relative-index normalization shared by generic Array
    /// prototype algorithms. The result is clamped to [0, length] while
    /// retaining the full Number safe-integer range.
    /// </summary>
    private void EmitGenericRelativeArrayIndex(
        ILGenerator il,
        EmittedRuntime runtime,
        Action emitArgument,
        LocalBuilder length,
        LocalBuilder destination)
    {
        var number = il.DeclareLocal(_types.Double);
        emitArgument();
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, number);

        var useZero = il.DefineLabel();
        var finite = il.DefineLabel();
        var nonNegative = il.DefineLabel();
        var done = il.DefineLabel();

        // NaN becomes +0.
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, useZero);

        // -Infinity and all finite negative values share the lower-clamp path.
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
        il.Emit(OpCodes.Beq, useZero);

        // +Infinity becomes length.
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Bne_Un, finite);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(finite);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, number);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bge, nonNegative);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(nonNegative);
        il.Emit(OpCodes.Ldloc, number);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Double, _types.Double));
        il.Emit(OpCodes.Stloc, destination);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(useZero);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, destination);
        il.MarkLabel(done);
    }

    private void EmitArrayPop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPop",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayPop = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();
        var frozenLabel = il.DefineLabel();

        // Check frozen/sealed - pop removes an element (changes length)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // Read the observable final property (including inherited getters),
        // then perform DeletePropertyOrThrow before shortening length.
        var lastLocal = il.DeclareLocal(_types.Object);
        var lastIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, lastIndexLocal);
        il.Emit(OpCodes.Ldloc, lastIndexLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, lastLocal);

        // The getter above may freeze or seal the receiver. Re-check before
        // DeletePropertyOrThrow; non-extensibility alone does not prevent pop.
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel,
            checkSealed: true, checkExtensible: false);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, lastIndexLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeleteIndexStrict);
        il.Emit(OpCodes.Brfalse, frozenLabel);

        // A non-writable length rejects the final Set even when deletion of the
        // element succeeded.
        var lengthDescriptor = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var lengthWritable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, lengthDescriptor);
        il.Emit(OpCodes.Ldloc, lengthDescriptor);
        il.Emit(OpCodes.Brfalse, lengthWritable);
        il.Emit(OpCodes.Ldloc, lengthDescriptor);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, frozenLabel);
        il.MarkLabel(lengthWritable);

        var plainList = il.DefineLabel();
        var shortened = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, plainList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, lastIndexLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
        il.Emit(OpCodes.Br, shortened);
        il.MarkLabel(plainList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, lastIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveAt", _types.Int32));
        il.MarkLabel(shortened);

        // return last
        il.Emit(OpCodes.Ldloc, lastLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        var emptyLengthDescriptor = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var emptyLengthWritable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, emptyLengthDescriptor);
        il.Emit(OpCodes.Ldloc, emptyLengthDescriptor);
        il.Emit(OpCodes.Brfalse, emptyLengthWritable);
        il.Emit(OpCodes.Ldloc, emptyLengthDescriptor);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, frozenLabel);
        il.MarkLabel(emptyLengthWritable);
        // ECMA-262 23.1.3.20 Array.prototype.pop: returns undefined for empty
        // arrays (was null → broke `arr.pop() === undefined` checks).
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        // Array mutator algorithms use Delete/Set with Throw=true, so an
        // integrity-level failure throws even when the caller is non-strict.
        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");
    }

    private void EmitArrayPopProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPopProto", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.ArrayPopProto = method;
        var il = method.GetILGenerator();

        var generic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, generic);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Call, runtime.ArrayPop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(generic);
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var nonEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, nonEmpty);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nonEmpty);
        var newLength = il.DeclareLocal(_types.Double);
        var key = il.DeclareLocal(_types.String);
        var element = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, newLength);
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, element);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, element);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayShift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayShift",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayShift = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();
        var frozenLabel = il.DefineLabel();

        // Check frozen/sealed - shift removes an element (changes length)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var first = list[0]
        var firstLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, firstLocal);

        // list.RemoveAt(0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveAt", _types.Int32));

        // return first
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        // ECMA-262 23.1.3.21 Array.prototype.shift: returns undefined for empty.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");
    }

    private void EmitArrayShiftProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayShiftProto", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.ArrayShiftProto = method;
        var il = method.GetILGenerator();

        var generic = il.DefineLabel();
        var fastList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, fastList);
        il.Emit(OpCodes.Br, generic);
        il.MarkLabel(fastList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, generic);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Call, runtime.ArrayShift);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(generic);
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var nonEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, nonEmpty);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nonEmpty);
        var first = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, first);

        var k = il.DeclareLocal(_types.Double);
        var fromKey = il.DeclareLocal(_types.String);
        var toKey = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);
        var loop = il.DefineLabel();
        var loopDone = il.DefineLabel();
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, loopDone);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);

        var deleteTarget = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(loopDone);
        var newLength = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, newLength);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        EmitRequireWritableArrayLength(il, runtime, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, first);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayUnshift",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayUnshift = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();

        // Check frozen/sealed - unshift adds an element (changes length)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);

        // list.Insert(0, element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Insert", _types.Int32, _types.Object));

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");
    }

    // Typed push for promoted number[]/boolean[] locals (#857/#860): appends an
    // unboxed double/bool to a bare List<T> and returns the new length. No frozen/
    // sealed check — a promoted local is provably non-escaping and so can never be
    // Object.freeze'd (that needs an argument-pass escape, which disqualifies promotion).
    private void EmitArrayPushTyped(TypeBuilder typeBuilder, EmittedRuntime runtime, ArrayElementsDescriptor desc)
    {
        var listType = desc.GetListType(_types);
        var elemType = desc.GetElementType(_types);
        var method = typeBuilder.DefineMethod(
            $"ArrayPush{desc.Kind}",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [listType, elemType]
        );
        if (desc.Kind == ArrayElementsKind.Double) runtime.ArrayPushDouble = method;
        else runtime.ArrayPushBool = method;

        var il = method.GetILGenerator();
        // list.Add(value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [elemType])!);
        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayPush(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPush",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayPush = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();

        // Check frozen/sealed - push adds an element (changes length)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);

        // list.Add(element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");
    }

    // Variadic ArrayPush wired into Array.prototype as a $TSFunction. Inline
    // emission of `arr.push(x)` calls ArrayPush(list, item) per element, but the
    // prototype wrapper is invoked via $TSFunction reflection, where a single
    // `object` second param can only receive one argument. ECMA-262 push is
    // variadic, and Array.prototype.push.apply(arr, items) MUST spread. Mark
    // the second param `[ParamArrayAttribute]` so $TSFunction packs trailing args.
    private void EmitArrayPushProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPushProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        var paramArrayCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!;
        method.DefineParameter(2, System.Reflection.ParameterAttributes.None, "items")
            .SetCustomAttribute(paramArrayCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayPushProto = method;

        var il = method.GetILGenerator();
        var generic = il.DefineLabel();
        var fastList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, fastList);
        il.Emit(OpCodes.Br, generic);
        il.MarkLabel(fastList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, generic);

        var frozenLabel = il.DefineLabel();
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);
        var idx = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idx);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idx);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");

        il.MarkLabel(generic);
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var itemCount = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, itemCount);
        var withinLimit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 9007199254740991.0);
        il.Emit(OpCodes.Ble, withinLimit);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.prototype.push exceeded the safe integer limit");
        il.MarkLabel(withinLimit);

        var genericIdx = il.DeclareLocal(_types.Int32);
        var genericLoop = il.DefineLabel();
        var genericDone = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, genericIdx);
        il.MarkLabel(genericLoop);
        il.Emit(OpCodes.Ldloc, genericIdx);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, genericDone);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, genericIdx);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, genericIdx);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, genericIdx);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, genericIdx);
        il.Emit(OpCodes.Br, genericLoop);

        il.MarkLabel(genericDone);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        var finalLength = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, finalLength);
        EmitRequireWritableArrayLength(il, runtime, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, finalLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, finalLength);
        il.Emit(OpCodes.Ret);
    }

    // Variadic ArrayUnshift wired into Array.prototype. Per ECMA-262 unshift
    // takes ...items and inserts them at the start preserving order: for items
    // [a, b, c], result has [a, b, c, ...rest]. We achieve this by inserting
    // each item at index i (so a→0, b→1, c→2).
    private void EmitArrayUnshiftProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayUnshiftProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.ObjectArray]
        );
        var paramArrayCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!;
        method.DefineParameter(2, System.Reflection.ParameterAttributes.None, "items")
            .SetCustomAttribute(paramArrayCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayUnshiftProto = method;

        var il = method.GetILGenerator();
        var generic = il.DefineLabel();
        var fastList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, fastList);
        il.Emit(OpCodes.Br, generic);
        il.MarkLabel(fastList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, generic);

        var frozenLabel = il.DefineLabel();
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: true);
        var idx = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idx);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Insert", _types.Int32, _types.Object));

        il.Emit(OpCodes.Ldloc, idx);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idx);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(frozenLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot modify a frozen or sealed array");

        il.MarkLabel(generic);
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var itemCount = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, itemCount);
        var withinLimit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 9007199254740991.0);
        il.Emit(OpCodes.Ble, withinLimit);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.prototype.unshift exceeded the safe integer limit");
        il.MarkLabel(withinLimit);

        var itemDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, itemDone);

        var k = il.DeclareLocal(_types.Double);
        var fromKey = il.DeclareLocal(_types.String);
        var toKey = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);
        var shiftLoop = il.DefineLabel();
        var shiftDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(shiftLoop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble, shiftDone);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);

        var deleteTarget = il.DefineLabel();
        var shiftNext = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, shiftNext);
        il.MarkLabel(deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(shiftNext);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, shiftLoop);

        il.MarkLabel(shiftDone);
        var itemIdx = il.DeclareLocal(_types.Int32);
        var itemLoop = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, itemIdx);
        il.MarkLabel(itemLoop);
        il.Emit(OpCodes.Ldloc, itemIdx);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, itemDone);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, itemIdx);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, itemIdx);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, itemIdx);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, itemIdx);
        il.Emit(OpCodes.Br, itemLoop);

        il.MarkLabel(itemDone);
        var finalLength = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, finalLength);
        EmitRequireWritableArrayLength(il, runtime, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, finalLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, finalLength);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.ObjectArray]
        );
        runtime.ArraySlice = method;

        var il = method.GetILGenerator();
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var start = il.DeclareLocal(_types.Double);
        var end = il.DeclareLocal(_types.Double);

        var defaultStart = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, defaultStart);
        EmitGenericRelativeArrayIndex(
            il,
            runtime,
            () =>
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
            },
            length,
            start);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(defaultStart);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, start);
        il.MarkLabel(startDone);

        // An omitted or explicitly undefined end means len. Null remains an
        // explicit bound and therefore coerces to zero.
        var parseEnd = il.DefineLabel();
        var defaultEnd = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, defaultEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, parseEnd);
        il.Emit(OpCodes.Br, defaultEnd);
        il.MarkLabel(parseEnd);
        EmitGenericRelativeArrayIndex(
            il,
            runtime,
            () =>
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldelem_Ref);
            },
            length,
            end);
        il.Emit(OpCodes.Br, endDone);
        il.MarkLabel(defaultEnd);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, end);
        il.MarkLabel(endDone);

        var count = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Stloc, count);
        var countValid = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldc_R8, 4294967295.0);
        il.Emit(OpCodes.Ble, countValid);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Invalid array length");
        il.MarkLabel(countValid);

        var result = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, result);
        var k = il.DeclareLocal(_types.Double);
        var key = il.DeclareLocal(_types.String);
        var loop = il.DefineLabel();
        var hole = il.DefineLabel();
        var next = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, hole);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(hole);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayReverse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReverse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject]
        );
        runtime.ArrayReverse = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();

        // Check frozen ONLY (sealed/non-extensible allows reordering, no length change)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: false, checkExtensible: false);

        // list.Reverse()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Reverse", _types.EmptyTypes));

        // return list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // Frozen return path - return unchanged list
        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayReverseProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReverseProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ArrayReverseProto = method;

        var il = method.GetILGenerator();
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var middle = il.DeclareLocal(_types.Double);
        var lower = il.DeclareLocal(_types.Double);
        var upper = il.DeclareLocal(_types.Double);
        var lowerKey = il.DeclareLocal(_types.String);
        var upperKey = il.DeclareLocal(_types.String);
        var lowerExists = il.DeclareLocal(_types.Boolean);
        var upperExists = il.DeclareLocal(_types.Boolean);
        var lowerValue = il.DeclareLocal(_types.Object);
        var upperValue = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 2.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, middle);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, lower);

        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, lower);
        il.Emit(OpCodes.Ldloc, middle);
        il.Emit(OpCodes.Bge, done);

        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, lower);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, upper);
        il.Emit(OpCodes.Ldloc, lower);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, lowerKey);
        il.Emit(OpCodes.Ldloc, upper);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, upperKey);

        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, lowerKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Stloc, lowerExists);
        var noLowerValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lowerExists);
        il.Emit(OpCodes.Brfalse, noLowerValue);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, lowerKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, lowerValue);
        il.MarkLabel(noLowerValue);

        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, upperKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Stloc, upperExists);
        var noUpperValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, upperExists);
        il.Emit(OpCodes.Brfalse, noUpperValue);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, upperKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, upperValue);
        il.MarkLabel(noUpperValue);

        var lowerMissing = il.DefineLabel();
        var upperMissingWithLower = il.DefineLabel();
        var bothMissing = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lowerExists);
        il.Emit(OpCodes.Brfalse, lowerMissing);
        il.Emit(OpCodes.Ldloc, upperExists);
        il.Emit(OpCodes.Brfalse, upperMissingWithLower);
        // Both exist: exchange the values.
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, lowerKey);
        il.Emit(OpCodes.Ldloc, upperValue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, upperKey);
        il.Emit(OpCodes.Ldloc, lowerValue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, next);

        il.MarkLabel(upperMissingWithLower);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, lowerKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, upperKey);
        il.Emit(OpCodes.Ldloc, lowerValue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, next);

        il.MarkLabel(lowerMissing);
        il.Emit(OpCodes.Ldloc, upperExists);
        il.Emit(OpCodes.Brfalse, bothMissing);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, lowerKey);
        il.Emit(OpCodes.Ldloc, upperValue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, upperKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, next);

        il.MarkLabel(bothMissing);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, lower);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, lower);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayFlat(List<object> list, object? depthArg) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayFlat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFlat = method;

        var il = method.GetILGenerator();

        // Parse depth: default 1, Infinity -> int.MaxValue
        var depthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);

        // if (depthArg == null) depth = 1
        var depthNotNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, depthNotNull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, depthLocal);
        var depthDone = il.DefineLabel();
        il.Emit(OpCodes.Br, depthDone);

        il.MarkLabel(depthNotNull);
        // depth = (int)(double)depthArg, handle Infinity
        var notInfinity = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.DoubleIsPositiveInfinity);
        il.Emit(OpCodes.Brfalse, notInfinity);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Br, depthDone);

        il.MarkLabel(notInfinity);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, depthLocal);

        il.MarkLabel(depthDone);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Call helper: FlattenHelper(list, result, depth)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, depthLocal);
        il.Emit(OpCodes.Call, runtime.ArrayFlatHelper);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlatHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // FlattenHelper(List<object> source, List<object> result, int depth) -> void
        var method = typeBuilder.DefineMethod(
            "ArrayFlatHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ListOfObject, _types.ListOfObject, _types.Int32]
        );
        runtime.ArrayFlatHelper = method;

        var il = method.GetILGenerator();

        var iLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);
        var listAsListLocal = il.DeclareLocal(_types.ListOfObject);

        // for (int i = 0; i < source.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);
        // item = source[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, itemLocal);

        // number[] unboxing deopt: a numeric-mode $Array element masquerades as an EMPTY base
        // List<object?> (it inherits List<object?> but its elements live unboxed in _numStore), so
        // the `isinst List<object>` recursion below would flatten nothing. Materialize it first so its
        // elements are visible. Self-guarded — a no-op for boxed/scalar items. (#918 deopt-completeness
        // gap; exposed broadly once #927 keeps loop-built number[] numeric instead of deopting it.)
        EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldloc, itemLocal));

        // ECMA-262 23.1.3.12 FlattenIntoArray: skip holes (only kPresent
        // slots are flattened). Without this the hole sentinel would be
        // Add()'d directly into the result list.
        var flatContinueLoop = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, flatContinueLoop);

        // if (depth > 0 && item is List<object> nestedList)
        var addDirectly = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); // depth
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, addDirectly);

        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, listAsListLocal);
        il.Emit(OpCodes.Brfalse, addDirectly);

        // FlattenHelper(nestedList, result, depth - 1)
        il.Emit(OpCodes.Ldloc, listAsListLocal);
        il.Emit(OpCodes.Ldarg_1); // result
        il.Emit(OpCodes.Ldarg_2); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, method); // recursive call
        il.Emit(OpCodes.Br, flatContinueLoop);

        // else: result.Add(item)
        il.MarkLabel(addDirectly);
        il.Emit(OpCodes.Ldarg_1); // result
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(flatContinueLoop);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        // i < source.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        il.Emit(OpCodes.Blt, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlatMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayFlatMap(List<object> list, object callback) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayFlatMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFlatMap = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.flatMap");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var callResultLocal = il.DeclareLocal(_types.Object);
        var nestedListLocal = il.DeclareLocal(_types.ListOfObject);

        // result = new List<object>(list.Count). FlatMap's output is variable
        // (depends on inner array sizes), but pre-sizing to source length is a
        // reasonable lower bound — at minimum 1 element per source slot when
        // callbacks return scalars. Avoids the first few doublings.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Hoist args[3] allocation once per call; pre-fill args[2] = list
        // (constant for the helper invocation). Per-iter writes only touch
        // args[0] (element) and args[1] (boxed index).
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // for (int i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);

        // ECMA-262 23.1.3.12: flatMap skips holes at the SOURCE level (no
        // callback invocation for a hole source slot). Lazy-aware
        // (issue #90): for array-like receivers (Dict / $Object) the
        // placeholder list is null at present slots — LoadArrayLikeElement
        // re-reads them via GetProperty so getter side effects propagate
        // and structurally-absent slots return $ArrayHole.
        var flatMapContinue = il.DefineLabel();
        EmitElementLoad(il, iLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, flatMapContinue);

        // args[0] = LoadArrayLikeElement(list, i) — lazy-aware
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        EmitElementLoad(il, iLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // callResult = InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1); // callback - first arg
        il.Emit(OpCodes.Ldloc, argsLocal); // args - second arg
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, callResultLocal);

        // number[] unboxing deopt: a numeric-mode $Array callback result is an EMPTY base list
        // (its elements live unboxed in _numStore), so the single-level flatten below would add
        // nothing. Materialize it so its elements are visible. Self-guarded — a no-op for boxed/scalar
        // results. (#918 deopt-completeness gap, same shape as ArrayFlatHelper above.)
        EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldloc, callResultLocal));

        // if (callResult is List<object> nestedList)
        var addDirectly = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, nestedListLocal);
        il.Emit(OpCodes.Brfalse, addDirectly);

        // ECMA-262: inner arrays also have their holes skipped during the
        // single-level flatten (CreateDataPropertyOrThrow fires only when
        // kPresent). Replace the plain AddRange with a hole-skipping loop.
        {
            var innerI = il.DeclareLocal(_types.Int32);
            var innerStart = il.DefineLabel();
            var innerEnd = il.DefineLabel();
            var innerSkip = il.DefineLabel();
            var innerElement = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, innerI);
            il.MarkLabel(innerStart);
            il.Emit(OpCodes.Ldloc, innerI);
            il.Emit(OpCodes.Ldloc, nestedListLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Bge, innerEnd);
            il.Emit(OpCodes.Ldloc, nestedListLocal);
            il.Emit(OpCodes.Ldloc, innerI);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
            il.Emit(OpCodes.Stloc, innerElement);
            il.Emit(OpCodes.Ldloc, innerElement);
            il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
            il.Emit(OpCodes.Brtrue, innerSkip);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, innerElement);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.MarkLabel(innerSkip);
            il.Emit(OpCodes.Ldloc, innerI);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, innerI);
            il.Emit(OpCodes.Br, innerStart);
            il.MarkLabel(innerEnd);
        }
        il.Emit(OpCodes.Br, flatMapContinue);

        // else: result.Add(callResult)
        il.MarkLabel(addDirectly);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(flatMapContinue);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        // i < list.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        il.Emit(OpCodes.Blt, loopStart);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySort(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArraySort(List<object> list, object? compareFn) -> List<object>
        // Mutates the list in-place, returns the same list reference
        var method = typeBuilder.DefineMethod(
            "ArraySort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArraySort = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(
            il, runtime, 1, "Array.prototype.sort comparator", allowUndefined: true);
        var frozenLabel = il.DefineLabel();

        // Check frozen ONLY (sealed/non-extensible allows reordering, no length change)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: false, checkExtensible: false);

        // Stable bottom-up merge sort (Θ(n log n)) — see EmitSortBodyOnLocal (#877).
        EmitSortBody(il, runtime, mutateInPlace: true);

        // Frozen return path - return unchanged list
        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayToSorted(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToSorted(List<object> list, object? compareFn) -> List<object>
        // Returns a NEW sorted list, original is unchanged
        var method = typeBuilder.DefineMethod(
            "ArrayToSorted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayToSorted = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(
            il, runtime, 1, "Array.prototype.toSorted comparator", allowUndefined: true);

        // Snapshot the source length before any indexed getter runs, then
        // create the dense copy through observable property reads. A getter
        // may grow the source, but toSorted only reads the original range.
        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        var sourceLengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, sourceLengthLocal);

        var copyLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, sourceLengthLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, copyLocal);

        var copyIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, copyIndexLocal);
        var copyLoopLabel = il.DefineLabel();
        var copyDoneLabel = il.DefineLabel();
        il.MarkLabel(copyLoopLabel);
        il.Emit(OpCodes.Ldloc, copyIndexLocal);
        il.Emit(OpCodes.Ldloc, sourceLengthLocal);
        il.Emit(OpCodes.Bge, copyDoneLabel);
        il.Emit(OpCodes.Ldloc, copyLocal);
        EmitLoadElementUnholed(il, copyIndexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, copyIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, copyIndexLocal);
        il.Emit(OpCodes.Br, copyLoopLabel);
        il.MarkLabel(copyDoneLabel);

        // Now sort the copy using the same logic as EmitArraySort
        // We need to emit sort body but use copyLocal instead of arg0
        EmitSortBodyOnLocal(il, runtime, copyLocal, observeProperties: false);
    }

    /// <summary>
    /// Generic Array.prototype.toSorted.call receiver boundary. It keeps the
    /// observable array-like receiver scoped across lazy materialization and
    /// sorting, restoring nested-call state even when a getter or comparator
    /// completes abruptly.
    /// </summary>
    private void EmitArrayToSortedGeneric(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayToSortedGeneric",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.Object]);
        runtime.ArrayToSortedGeneric = method;

        var il = method.GetILGenerator();
        var previousReceiver = il.DeclareLocal(_types.Object);
        var result = il.DeclareLocal(_types.ListOfObject);

        EmitThrowIfCallbackNotCallable(
            il, runtime, 1, "Array.prototype.toSorted comparator", allowUndefined: true);

        il.Emit(OpCodes.Ldsfld, runtime.CurrentArrayLikeReceiverField);
        il.Emit(OpCodes.Stloc, previousReceiver);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, runtime.CurrentArrayLikeReceiverField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ArrayLikeMaterializeForCopy);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ArrayToSorted);
        il.Emit(OpCodes.Stloc, result);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, previousReceiver);
        il.Emit(OpCodes.Stsfld, runtime.CurrentArrayLikeReceiverField);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the body of the sort algorithm (stable bottom-up merge sort, Θ(n log n)).
    /// When mutateInPlace is true, sorts arg0 and returns arg0.
    /// </summary>
    private void EmitSortBody(ILGenerator il, EmittedRuntime runtime, bool mutateInPlace)
    {
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, listLocal);

        EmitSortBodyOnLocal(il, runtime, listLocal, observeProperties: mutateInPlace);
    }

    /// <summary>
    /// Emits the sort body operating on a local variable (for toSorted which creates a copy).
    /// JavaScript spec: undefined values are always moved to end, never passed to compareFn.
    /// </summary>
    private void EmitSortBodyOnLocal(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder listLocal,
        bool observeProperties)
    {
        // JavaScript sort algorithm:
        // 1. Partition: separate defined values from undefined values
        // 2. Sort only the defined values
        // 3. Append undefined values at the end

        var definedLocal = il.DeclareLocal(_types.ListOfObject);      // List of defined elements
        var undefinedCountLocal = il.DeclareLocal(_types.Int32);       // Count of undefined elements
        var holeCountLocal = il.DeclareLocal(_types.Int32);
        var sortLengthLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var jLocal = il.DeclareLocal(_types.Int32);
        var compareResultLocal = il.DeclareLocal(_types.Int32);
        var str1Local = il.DeclareLocal(_types.String);
        var str2Local = il.DeclareLocal(_types.String);
        var elementLocal = il.DeclareLocal(_types.Object);
        LocalBuilder? isLazyLocal = null;
        if (observeProperties)
            EmitHoistedLazyCheck(il, runtime, out isLazyLocal, out _);

        // === Phase 1: Partition defined vs undefined ===
        // defined = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, definedLocal);

        // undefinedCount = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, undefinedCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, holeCountLocal);

        // SortIndexedProperties snapshots len before any indexed getter runs.
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, sortLengthLocal);

        // for (i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var partitionLoopStart = il.DefineLabel();
        var partitionLoopCondition = il.DefineLabel();
        var isUndefinedLabel = il.DefineLabel();
        var isHoleLabel = il.DefineLabel();
        var partitionNext = il.DefineLabel();

        il.Emit(OpCodes.Br, partitionLoopCondition);

        il.MarkLabel(partitionLoopStart);
        // element = Get(O, i), preserving live accessor/prototype reads for sort.
        if (observeProperties)
            EmitElementLoad(il, iLocal, runtime, isLazyLocal!);
        else
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        }
        il.Emit(OpCodes.Stloc, elementLocal);

        // if (element is $Undefined || element is $ArrayHole) undefinedCount++ else defined.Add(element)
        // ECMA-262 sort moves both undefined values AND holes to the end, regardless
        // of comparefn. Pre-fix only $Undefined was partitioned; holes (sentinel
        // distinct from undefined) stayed at their original positions, so
        // `new Array(2); x[1]=1; x.sort(cmp)` left `[<hole>, 1]` rather than
        // `[1, undefined]`.
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, isUndefinedLabel);

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, observeProperties ? isHoleLabel : isUndefinedLabel);

        // Not undefined or hole: defined.Add(element)
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, partitionNext);

        // Is undefined: undefinedCount++
        il.MarkLabel(isUndefinedLabel);
        il.Emit(OpCodes.Ldloc, undefinedCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, undefinedCountLocal);
        il.Emit(OpCodes.Br, partitionNext);

        // Sort preserves holes as absent properties at the tail. Copying
        // toSorted deliberately stays dense, so only the mutating path uses
        // this separate counter.
        il.MarkLabel(isHoleLabel);
        il.Emit(OpCodes.Ldloc, holeCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, holeCountLocal);

        il.MarkLabel(partitionNext);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(partitionLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, sortLengthLocal);
        il.Emit(OpCodes.Blt, partitionLoopStart);

        // === Phase 2: Sort defined elements — stable bottom-up merge sort (Θ(n log n), #877) ===
        // Replaces the prior in-place insertion sort (Θ(n²)), which made compiled
        // sort slower than the interpreter on large inputs. Merge sort is stable:
        // on a tie the LEFT run's element (smaller original index) is taken first.
        // The per-pair comparison is identical to the old one — a custom compareFn
        // (double → sign; NaN/non-number ⇒ 0/equal) or, when the comparator is
        // absent/undefined, the ECMA-262 default ToJsString/CompareOrdinal. Pure IL,
        // no SharpTS.dll dependency. Ping-pongs between two object[] buffers.
        var nLocal = il.DeclareLocal(_types.Int32);          // defined.Count
        var srcLocal = il.DeclareLocal(_types.ObjectArray);  // current source buffer
        var dstLocal = il.DeclareLocal(_types.ObjectArray);  // merge destination buffer
        var swapArrLocal = il.DeclareLocal(_types.ObjectArray);
        var widthLocal = il.DeclareLocal(_types.Int32);
        var loLocal = il.DeclareLocal(_types.Int32);
        var midLocal = il.DeclareLocal(_types.Int32);
        var hiLocal = il.DeclareLocal(_types.Int32);
        var kLocal = il.DeclareLocal(_types.Int32);
        // Comparator-argument buffer, allocated ONCE below and reused for every comparison.
        // The merge loop previously did `new object[2]` per compared pair — Θ(n log n)
        // throwaway arrays, the dominant cost (and GC-variance source) of compiled sort on
        // large inputs. The guest comparator only ever sees its two parameters, never this
        // backing array, so reusing it across all comparisons is safe (the interpreter's
        // CompareFnComparer reuses its arg list for the same reason).
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // n = defined.Count
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, nLocal);

        // src = new object[n]; defined.CopyTo(src); dst = new object[n]
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, srcLocal);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "CopyTo", _types.ObjectArray));
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, dstLocal);

        // argsBuf = new object[2] — allocated once here; reused for every comparator call.
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        var widthCond = il.DefineLabel();
        var widthBody = il.DefineLabel();
        var loCond = il.DefineLabel();
        var loBody = il.DefineLabel();
        var loNext = il.DefineLabel();
        var midAdd = il.DefineLabel();
        var midDone = il.DefineLabel();
        var hiAdd = il.DefineLabel();
        var hiDone = il.DefineLabel();
        var widthDouble = il.DefineLabel();
        var mergeCond = il.DefineLabel();
        var mergeBody = il.DefineLabel();
        var takeRight = il.DefineLabel();
        var afterTake = il.DefineLabel();
        var drainLeftCond = il.DefineLabel();
        var drainLeftBody = il.DefineLabel();
        var drainRightCond = il.DefineLabel();
        var drainRightBody = il.DefineLabel();
        var wbCond = il.DefineLabel();
        var wbBody = il.DefineLabel();

        // for (width = 1; width < n; width *= 2)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, widthLocal);
        il.Emit(OpCodes.Br, widthCond);

        il.MarkLabel(widthBody);

        // for (lo = 0; lo < n; lo += 2*width)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, loLocal);
        il.Emit(OpCodes.Br, loCond);

        il.MarkLabel(loBody);

        // mid = min(lo + width, n) — overflow-safe: if width >= n - lo then n else lo + width.
        // (n - lo >= 0 always; lo + width is only computed when it is < n, so it can't overflow.)
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldloc, loLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Blt, midAdd);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Stloc, midLocal);
        il.Emit(OpCodes.Br, midDone);
        il.MarkLabel(midAdd);
        il.Emit(OpCodes.Ldloc, loLocal);
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, midLocal);
        il.MarkLabel(midDone);

        // hi = min(mid + width, n) = min(lo + 2*width, n) — overflow-safe via n - mid.
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Blt, hiAdd);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Stloc, hiLocal);
        il.Emit(OpCodes.Br, hiDone);
        il.MarkLabel(hiAdd);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, hiLocal);
        il.MarkLabel(hiDone);

        // i = lo; j = mid; k = lo
        il.Emit(OpCodes.Ldloc, loLocal);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Ldloc, loLocal);
        il.Emit(OpCodes.Stloc, kLocal);

        il.Emit(OpCodes.Br, mergeCond);

        // --- merge body: compare src[i] vs src[j] -> compareResultLocal ---
        il.MarkLabel(mergeBody);

        var hasCompareFn = il.DefineLabel();
        var noCompareFn = il.DefineLabel();
        var checkCompareResult = il.DefineLabel();

        // compareFn is "absent" when null OR $Undefined.Instance (ECMA-262):
        // `arr.sort(undefined)` must use the default comparator, not invoke undefined.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCompareFn);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noCompareFn);
        il.Emit(OpCodes.Br, hasCompareFn);

        il.MarkLabel(noCompareFn);
        // Default: CompareOrdinal(ToJsString(src[i]), ToJsString(src[j])). ToJsString
        // runs the ToPrimitive protocol so `{toString:()=>"-2"}` sorts as "-2".
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, str1Local);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, str2Local);
        il.Emit(OpCodes.Ldloc, str1Local);
        il.Emit(OpCodes.Ldloc, str2Local);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("CompareOrdinal", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        il.MarkLabel(hasCompareFn);
        // result = InvokeValue(compareFn, argsBuf) with argsBuf[0]=src[i], argsBuf[1]=src[j].
        // argsBuf is the single hoisted object[2] declared above — no per-comparison allocation.
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Convert compare result to a sign in compareResultLocal:
        // double -> sign (<0 / 0 / >0); NaN or non-double -> 0 (equal -> take left -> stable).
        var resultIsNotDouble = il.DefineLabel();
        var notNaN = il.DefineLabel();
        var isZero = il.DefineLabel();
        var isPositive = il.DefineLabel();
        var resultLocal = il.DeclareLocal(_types.Object);
        var doubleResultLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, resultIsNotDouble);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, doubleResultLocal);
        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);
        il.MarkLabel(notNaN);
        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, isZero);
        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, isPositive);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);
        il.MarkLabel(isPositive);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);
        il.MarkLabel(isZero);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);
        il.MarkLabel(resultIsNotDouble);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compareResultLocal);

        il.MarkLabel(checkCompareResult);
        // compareResult > 0  => src[i] sorts AFTER src[j] => take right; else take left (stable).
        il.Emit(OpCodes.Ldloc, compareResultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, takeRight);

        // dst[k] = src[i]; i++
        il.Emit(OpCodes.Ldloc, dstLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, afterTake);

        il.MarkLabel(takeRight);
        // dst[k] = src[j]; j++
        il.Emit(OpCodes.Ldloc, dstLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);

        il.MarkLabel(afterTake);
        // k++
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);

        il.MarkLabel(mergeCond);
        // while (i < mid && j < hi) keep merging; otherwise drain.
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Bge, drainLeftCond);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, hiLocal);
        il.Emit(OpCodes.Blt, mergeBody);
        // i<mid && j>=hi: fall through to drain the left run.

        // while (i < mid) dst[k++] = src[i++]
        il.MarkLabel(drainLeftCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Bge, drainRightCond);
        il.MarkLabel(drainLeftBody);
        il.Emit(OpCodes.Ldloc, dstLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, midLocal);
        il.Emit(OpCodes.Blt, drainLeftBody);

        // while (j < hi) dst[k++] = src[j++]
        il.MarkLabel(drainRightCond);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, hiLocal);
        il.Emit(OpCodes.Bge, loNext);
        il.MarkLabel(drainRightBody);
        il.Emit(OpCodes.Ldloc, dstLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, hiLocal);
        il.Emit(OpCodes.Blt, drainRightBody);

        il.MarkLabel(loNext);
        // lo += 2*width. hi already equals min(lo + 2*width, n) computed overflow-safe,
        // so advancing lo to hi is the same step and saturates at n (no overflow).
        il.Emit(OpCodes.Ldloc, hiLocal);
        il.Emit(OpCodes.Stloc, loLocal);
        il.MarkLabel(loCond);
        il.Emit(OpCodes.Ldloc, loLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Blt, loBody);

        // swap src <-> dst (this pass's output becomes next pass's input)
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Stloc, swapArrLocal);
        il.Emit(OpCodes.Ldloc, dstLocal);
        il.Emit(OpCodes.Stloc, srcLocal);
        il.Emit(OpCodes.Ldloc, swapArrLocal);
        il.Emit(OpCodes.Stloc, dstLocal);

        // width *= 2, saturating: if doubling would overflow int, clamp to int.MaxValue
        // (which is >= n, so the loop then exits) instead of wrapping to a negative width.
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue / 2);
        il.Emit(OpCodes.Ble, widthDouble);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, widthLocal);
        il.Emit(OpCodes.Br, widthCond);
        il.MarkLabel(widthDouble);
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, widthLocal);
        il.MarkLabel(widthCond);
        il.Emit(OpCodes.Ldloc, widthLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Blt, widthBody);

        // Write the sorted result (now in src) back into defined: defined[k] = src[k]
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, wbCond);
        il.MarkLabel(wbBody);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, srcLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.MarkLabel(wbCond);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Blt, wbBody);

        // === Phase 3: Rebuild original list with sorted defined + undefined at end ===
        if (observeProperties)
        {
            // Sort writes through ordinary Set/Delete operations so indexed
            // setters, non-writable descriptors, and prototype properties are
            // observed in order. The merge-sort above remains a pure snapshot.
            var writeIndexLocal = il.DeclareLocal(_types.Int32);
            var writeDefined = il.DefineLabel();
            var writeDefinedDone = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, writeIndexLocal);
            il.MarkLabel(writeDefined);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Ldloc, nLocal);
            il.Emit(OpCodes.Bge, writeDefinedDone);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ldloc, definedLocal);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, runtime.SetIndexStrict);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, writeIndexLocal);
            il.Emit(OpCodes.Br, writeDefined);
            il.MarkLabel(writeDefinedDone);

            var undefinedWrittenLocal = il.DeclareLocal(_types.Int32);
            var writeUndefined = il.DefineLabel();
            var writeUndefinedDone = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, undefinedWrittenLocal);
            il.MarkLabel(writeUndefined);
            il.Emit(OpCodes.Ldloc, undefinedWrittenLocal);
            il.Emit(OpCodes.Ldloc, undefinedCountLocal);
            il.Emit(OpCodes.Bge, writeUndefinedDone);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, runtime.SetIndexStrict);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, writeIndexLocal);
            il.Emit(OpCodes.Ldloc, undefinedWrittenLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, undefinedWrittenLocal);
            il.Emit(OpCodes.Br, writeUndefined);
            il.MarkLabel(writeUndefinedDone);

            var deleteTail = il.DefineLabel();
            var deleteTailDone = il.DefineLabel();
            il.MarkLabel(deleteTail);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Ldloc, sortLengthLocal);
            il.Emit(OpCodes.Bge, deleteTailDone);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, runtime.DeleteIndexStrict);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, writeIndexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, writeIndexLocal);
            il.Emit(OpCodes.Br, deleteTail);
            il.MarkLabel(deleteTailDone);

            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ret);
            return;
        }

        // list.Clear()
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Clear"));

        // list.AddRange(defined)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Callvirt, _types.ListObjectAddRange);

        // for (i = 0; i < undefinedCount; i++) list.Add($Undefined.Instance)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var appendLoopStart = il.DefineLabel();
        var appendLoopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, appendLoopCondition);

        il.MarkLabel(appendLoopStart);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(appendLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, undefinedCountLocal);
        il.Emit(OpCodes.Blt, appendLoopStart);

        // Return the list
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper method implementing JavaScript's ToIntegerOrInfinity algorithm.
    /// Used by splice/toSpliced for argument coercion.
    /// </summary>
    private void EmitToIntegerOrInfinityHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ToIntegerOrInfinity(object? value, int defaultValue) -> int
        var method = typeBuilder.DefineMethod(
            "ToIntegerOrInfinity",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object, _types.Int32]
        );
        runtime.ToIntegerOrInfinity = method;

        var il = method.GetILGenerator();

        var returnDefault = il.DefineLabel();
        var isDouble = il.DefineLabel();
        var notNaN = il.DefineLabel();
        var notPosInf = il.DefineLabel();
        var notNegInf = il.DefineLabel();

        // Only JavaScript undefined selects the caller-provided default. CLR
        // null represents JavaScript null here and must flow through ToNumber
        // (where it becomes +0), rather than being mistaken for an omitted
        // argument.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnDefault);

        // ECMA-262 ToPrimitive("number"): for Dictionary/$TSObject receivers,
        // try valueOf then toString. Without this, callers (like
        // Array.prototype.indexOf's fromIndex) silently treat
        // `{ valueOf: () => 0 }` as a non-numeric Dictionary → default,
        // skipping the spec-required side effect.
        var coercedLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, coercedLocal);

        var notObjectLabel = il.DefineLabel();
        var isObjectLikeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        // ECMA-262 ToNumber([1]) → ToPrimitive routes via valueOf/toString,
        // and Array.prototype.toString returns the comma-joined representation.
        // Treat List<object> as object-like here so `(123).toExponential([2])`
        // reaches ToString("2") → ToNumber 2 per spec.
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        il.Emit(OpCodes.Br, notObjectLabel);

        il.MarkLabel(isObjectLikeLabel);

        // The shared ToNumber helper owns the complete ToPrimitive(number)
        // protocol, including Symbol.toPrimitive descriptors and boxed
        // primitive unwrapping. Route those shapes directly to the shared
        // coercion tail instead of partially duplicating the protocol below.
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var symbolMethodLocal = il.DeclareLocal(_types.Object);
        var noSymbolPrimitive = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Brfalse, noSymbolPrimitive);
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToPrimitive);
        il.Emit(OpCodes.Ldloca, symbolMethodLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, notObjectLabel);
        il.MarkLabel(noSymbolPrimitive);

        foreach (var primitiveTag in new[] { "Boolean", "Number", "String", "Symbol", "BigInt" })
        {
            il.Emit(OpCodes.Ldloc, coercedLocal);
            il.Emit(OpCodes.Ldstr, primitiveTag);
            il.Emit(OpCodes.Call, runtime.IsBoxedPrimitiveOfTypeMethod);
            il.Emit(OpCodes.Brtrue, notObjectLabel);
        }

        var primEmptyArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, primEmptyArgsLocal);

        void TryInvokePrim(string name, Label afterLabel)
        {
            var fnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, coercedLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Brfalse, afterLabel);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, afterLabel);

            var invResultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, coercedLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Ldloc, primEmptyArgsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, invResultLocal);

            il.Emit(OpCodes.Ldloc, invResultLocal);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, invResultLocal);
            il.Emit(OpCodes.Isinst, runtime.TSObjectType);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, invResultLocal);
            il.Emit(OpCodes.Stloc, coercedLocal);
        }

        var afterValueOf = il.DefineLabel();
        TryInvokePrim("valueOf", afterValueOf);
        il.MarkLabel(afterValueOf);
        var stillObjectLabel = il.DefineLabel();
        var afterToString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjectLabel);
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, stillObjectLabel);
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, afterToString);
        il.MarkLabel(stillObjectLabel);
        TryInvokePrim("toString", afterToString);
        il.MarkLabel(afterToString);

        // ECMA-262 7.1.1 OrdinaryToPrimitive: if neither valueOf nor toString
        // returned a primitive (still Dictionary or $Object), throw TypeError.
        // This is the abrupt completion that propagates through ToNumber →
        // ToIntegerOrInfinity, surfacing as `assert.throws(TypeError, ...)` in
        // tests like `Number.prototype.toFixed.call(0, {valueOf:undef, toString:undef})`
        // and `arr.indexOf(true, {valueOf:()=>{}, toString:()=>{}})`.
        // Note: this trades a Pass→Fail regression on `(1).toPrecision({})`
        // (spec expects RangeError after `{}.toString` returns "[object Object]"
        // → NaN → 0 → range check fail). Compiled mode lacks proper inheritance
        // of `Object.prototype.toString` via plain Dictionaries — properly
        // walking to ObjectPrototypeField is the right structural fix; net win
        // is +3 Pass / -1 regression at this layer.
        var afterToPrimCheck = il.DefineLabel();
        var stillObjThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjThrowLabel);
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterToPrimCheck);
        il.MarkLabel(stillObjThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(afterToPrimCheck);

        il.MarkLabel(notObjectLabel);

        // Now coercedLocal is hopefully a primitive — coerce via ToNumber.
        var doubleLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, coercedLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, doubleLocal);

        // if (double.IsNaN(d)) return 0
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNaN);
        // if (double.IsPositiveInfinity(d)) return int.MaxValue
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, _types.DoubleIsPositiveInfinity);
        il.Emit(OpCodes.Brfalse, notPosInf);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPosInf);
        // if (double.IsNegativeInfinity(d)) return int.MinValue
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNegativeInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brfalse, notNegInf);
        il.Emit(OpCodes.Ldc_I4, int.MinValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNegInf);
        // return (int)Math.Truncate(d)
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySplice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArraySplice(List<object> list, object[] args) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArraySplice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArraySplice = method;

        var il = method.GetILGenerator();

        // Check frozen/sealed - splice changes length (removes and/or adds elements)
        // Use throwing variant since splice must throw TypeError on frozen/sealed arrays
        EmitArrayFrozenSealedThrowCheck(il, runtime, checkSealed: true);

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);
        var actualStartLocal = il.DeclareLocal(_types.Int32);
        var relStartLocal = il.DeclareLocal(_types.Int32);
        var actualDeleteCountLocal = il.DeclareLocal(_types.Int32);
        var deletedLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (args.Length == 0) return new List<object>()
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArgs);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Parse start: relStart = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);

        // actualStart = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);

        il.MarkLabel(startDone);

        // Parse deleteCount
        var hasDeleteCount = il.DefineLabel();
        var deleteCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasDeleteCount);

        // No deleteCount: delete to end
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Br, deleteCountDone);

        il.MarkLabel(hasDeleteCount);
        // Has deleteCount: dc = ToIntegerOrInfinity(args[1], 0)
        // actualDeleteCount = Max(0, Min(dc, len - actualStart))
        var dcLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, dcLocal);

        // Min(dc, len - actualStart)
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        // Max(0, ...)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualDeleteCountLocal);

        il.MarkLabel(deleteCountDone);

        // deleted = list.GetRange(actualStart, actualDeleteCount)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, deletedLocal);

        // list.RemoveRange(actualStart, actualDeleteCount)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveRange", _types.Int32, _types.Int32));

        // if (args.Length > 2) insert items
        var noInsert = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, noInsert);

        // Insert items from args[2..] at actualStart
        // for (i = args.Length - 1; i >= 2; i--) list.Insert(actualStart, args[i])
        // (Insert in reverse order to maintain order)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var insertLoopStart = il.DefineLabel();
        var insertLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, insertLoopCondition);

        il.MarkLabel(insertLoopStart);
        // list.Insert(actualStart, args[i])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Insert", _types.Int32, _types.Object));

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(insertLoopCondition);
        // i >= 2
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, insertLoopStart);

        il.MarkLabel(noInsert);

        // return deleted
        il.Emit(OpCodes.Ldloc, deletedLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Prototype entry point for Array.prototype.splice. Real arrays retain the
    /// compact List fast path above; all other receivers follow the generic
    /// ECMA-262 algorithm with observable Has/Get/Set/Delete operations.
    /// </summary>
    private void EmitArraySpliceProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySpliceProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.ObjectArray]);
        runtime.ArraySpliceProto = method;

        var il = method.GetILGenerator();
        var generic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, generic);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ArraySplice);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(generic);
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var argCount = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argCount);

        // actualStart = clamp(ToIntegerOrInfinity(start), 0, len).
        var actualStart = il.DeclareLocal(_types.Double);
        var haveStart = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, haveStart);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, actualStart);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(haveStart);
        EmitGenericRelativeArrayIndex(
            il,
            runtime,
            () =>
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
            },
            length,
            actualStart);
        il.MarkLabel(startDone);

        // actualDeleteCount: zero with no args, the tail with only start, or
        // clamp(ToIntegerOrInfinity(deleteCount), 0, len - actualStart).
        var actualDeleteCount = il.DeclareLocal(_types.Double);
        var oneArg = il.DefineLabel();
        var parseDeleteCount = il.DefineLabel();
        var deleteCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Brtrue, oneArg);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, actualDeleteCount);
        il.Emit(OpCodes.Br, deleteCountDone);
        il.MarkLabel(oneArg);
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, parseDeleteCount);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, actualDeleteCount);
        il.Emit(OpCodes.Br, deleteCountDone);

        il.MarkLabel(parseDeleteCount);
        var deleteNumber = il.DeclareLocal(_types.Double);
        var deleteNumberValid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, deleteNumber);
        // NaN becomes +0; Math.Max below handles negative values/infinity.
        il.Emit(OpCodes.Ldloc, deleteNumber);
        il.Emit(OpCodes.Ldloc, deleteNumber);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, deleteNumberValid);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, deleteNumber);
        il.MarkLabel(deleteNumberValid);
        il.Emit(OpCodes.Ldloc, deleteNumber);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Double, _types.Double));
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Double, _types.Double));
        il.Emit(OpCodes.Stloc, actualDeleteCount);
        il.MarkLabel(deleteCountDone);

        // ArraySpeciesCreate receives actualDeleteCount as an Array length.
        // Even for a non-array receiver, lengths above 2^32 - 1 must fail
        // before any indexed mutation or final length setter is observed.
        var deletedLengthValid = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Ldc_R8, 4294967295.0);
        il.Emit(OpCodes.Ble, deletedLengthValid);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Invalid array length");
        il.MarkLabel(deletedLengthValid);

        var itemCount = il.DeclareLocal(_types.Double);
        var noItems = il.DefineLabel();
        var itemCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, noItems);
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, itemCount);
        il.Emit(OpCodes.Br, itemCountDone);
        il.MarkLabel(noItems);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, itemCount);
        il.MarkLabel(itemCountDone);

        var newLength = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, newLength);
        var lengthWithinLimit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Ldc_R8, 9007199254740991.0);
        il.Emit(OpCodes.Ble, lengthWithinLimit);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.prototype.splice exceeded the safe integer limit");
        il.MarkLabel(lengthWithinLimit);

        // Copy deleted properties into a fresh Array, preserving holes.
        var deleted = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, deleted);
        var k = il.DeclareLocal(_types.Double);
        var fromKey = il.DeclareLocal(_types.String);
        var toKey = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);
        var copyDeletedLoop = il.DefineLabel();
        var copyDeletedDone = il.DefineLabel();
        var deletedHole = il.DefineLabel();
        var deletedNext = il.DefineLabel();
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(copyDeletedLoop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Bge, copyDeletedDone);
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, deletedHole);
        il.Emit(OpCodes.Ldloc, deleted);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, deletedNext);
        il.MarkLabel(deletedHole);
        il.Emit(OpCodes.Ldloc, deleted);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(deletedNext);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, copyDeletedLoop);
        il.MarkLabel(copyDeletedDone);

        // Shift the surviving tail left or right according to the size delta.
        var grow = il.DefineLabel();
        var shifted = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Beq, shifted);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Bgt, grow);

        var leftLoop = il.DefineLabel();
        var leftDone = il.DefineLabel();
        var leftDelete = il.DefineLabel();
        var leftNext = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(leftLoop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, leftDone);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, leftDelete);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, leftNext);
        il.MarkLabel(leftDelete);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(leftNext);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, leftLoop);
        il.MarkLabel(leftDone);

        // Delete the now-unused high properties, from the old end downward.
        var trimLoop = il.DefineLabel();
        var trimDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(trimLoop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Ble, trimDone);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, trimLoop);
        il.MarkLabel(trimDone);
        il.Emit(OpCodes.Br, shifted);

        il.MarkLabel(grow);
        var rightLoop = il.DefineLabel();
        var rightDone = il.DefineLabel();
        var rightDelete = il.DefineLabel();
        var rightNext = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, k);
        il.MarkLabel(rightLoop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Ble, rightDone);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, actualDeleteCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, itemCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, rightDelete);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, rightNext);
        il.MarkLabel(rightDelete);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(rightNext);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, rightLoop);
        il.MarkLabel(rightDone);
        il.MarkLabel(shifted);

        // Install the new items, then commit the final length.
        var itemIndex = il.DeclareLocal(_types.Int32);
        var insertLoop = il.DefineLabel();
        var insertDone = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, itemIndex);
        il.MarkLabel(insertLoop);
        il.Emit(OpCodes.Ldloc, itemIndex);
        il.Emit(OpCodes.Ldloc, argCount);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, insertDone);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, actualStart);
        il.Emit(OpCodes.Ldloc, itemIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, itemIndex);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, itemIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, itemIndex);
        il.Emit(OpCodes.Br, insertLoop);
        il.MarkLabel(insertDone);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, newLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, deleted);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayToReversed(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToReversed(List<object> list) -> List<object>
        // Returns a NEW reversed list, original is unchanged
        var method = typeBuilder.DefineMethod(
            "ArrayToReversed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject]
        );
        runtime.ArrayToReversed = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // i = list.Count - 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = list.Count - 1; i >= 0; i--)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        // result.Add(list[i] unholed) — ECMA-262 23.1.3.33 Array.prototype
        // .toReversed uses Get (which unholes) + CreateDataPropertyOrThrow,
        // producing a DENSE output where source holes become undefined.
        il.Emit(OpCodes.Ldloc, resultLocal);
        EmitLoadElementUnholed(il, iLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayWith(List<object> list, object[] args) -> List<object>
        // args[0] = index, args[1] = value
        // Returns a NEW list with element at index replaced
        var method = typeBuilder.DefineMethod(
            "ArrayWith",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayWith = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var lenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var actualIndexLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // index = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, indexLocal);

        // actualIndex = index < 0 ? len + index : index
        var indexNotNegative = il.DefineLabel();
        var indexDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, indexNotNegative);

        // Negative: len + index
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, actualIndexLocal);
        il.Emit(OpCodes.Br, indexDone);

        il.MarkLabel(indexNotNegative);
        // Non-negative: use index directly
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Stloc, actualIndexLocal);

        il.MarkLabel(indexDone);

        // if (actualIndex < 0 || actualIndex >= len) throw RangeError
        var throwRangeError = il.DefineLabel();
        var validIndex = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwRangeError);

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, throwRangeError);
        il.Emit(OpCodes.Br, validIndex);

        // Throw a real $RangeError (not a generic Exception) so guest `instanceof RangeError`
        // and Test262's assert.throws(RangeError, ...) hold. The previous code threw a bare CLR
        // Exception whose message merely began "RangeError:", so WrapException produced a generic
        // $Error on catch. Wrap the $RangeError in a CLR Exception whose Data["__tsValue"] carries
        // it (the inline-throw pattern from EmitArrayConstructor / ArrayFrom).
        il.MarkLabel(throwRangeError);
        var withErr = il.DeclareLocal(_types.Object);
        var withEx = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Ldstr, "Invalid index for with()");
        il.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
        il.Emit(OpCodes.Stloc, withErr);
        il.Emit(OpCodes.Ldstr, "Invalid index for with()");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, withEx);
        il.Emit(OpCodes.Ldloc, withEx);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldloc, withErr);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));
        il.Emit(OpCodes.Ldloc, withEx);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(validIndex);

        // ECMA-262 23.1.3.39 Array.prototype.with: produces a DENSE output
        // where source holes become undefined (uses Get + CreateDataProperty
        // OrThrow, not kPresent). Build the copy with an unholing loop rather
        // than the one-shot `new List(list)` — the latter propagates the raw
        // $ArrayHole sentinel into the result.
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var withI = il.DeclareLocal(_types.Int32);
            var withStart = il.DefineLabel();
            var withEnd = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, withI);
            il.MarkLabel(withStart);
            il.Emit(OpCodes.Ldloc, withI);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Bge, withEnd);
            var addReplacement = il.DefineLabel();
            var addValue = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, withI);
            il.Emit(OpCodes.Ldloc, actualIndexLocal);
            il.Emit(OpCodes.Beq, addReplacement);
            EmitLoadElementUnholed(il, withI, runtime, isLazyLocal);
            il.Emit(OpCodes.Br, addValue);
            il.MarkLabel(addReplacement);
            // The replacement index is never read from the source (spec step
            // 8.c); an accessor there must not run.
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(addValue);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Ldloc, withI);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, withI);
            il.Emit(OpCodes.Br, withStart);
            il.MarkLabel(withEnd);
        }

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayAt(List<object> list, object? indexArg) -> object?
        // Returns element at index (supports negative indices), or null if out of bounds
        var method = typeBuilder.DefineMethod(
            "ArrayAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayAt = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var lenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var actualIndexLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // index = ToIntegerOrInfinity(indexArg, 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, indexLocal);

        // actualIndex = index < 0 ? len + index : index
        var indexNotNegative = il.DefineLabel();
        var indexDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, indexNotNegative);

        // Negative: len + index
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, actualIndexLocal);
        il.Emit(OpCodes.Br, indexDone);

        il.MarkLabel(indexNotNegative);
        // Non-negative: use index directly
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Stloc, actualIndexLocal);

        il.MarkLabel(indexDone);

        // ECMA-262 23.1.3.1: out-of-bounds returns undefined (NOT null).
        var returnUndefined = il.DefineLabel();
        var validIndex = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnUndefined);

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, returnUndefined);
        il.Emit(OpCodes.Br, validIndex);

        il.MarkLabel(returnUndefined);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validIndex);

        // return list[actualIndex] unholed — spec: Get-style read (holes
        // read as undefined at the language boundary).
        EmitLoadElementUnholed(il, actualIndexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayToSpliced(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToSpliced(List<object> list, object[] args) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayToSpliced",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayToSpliced = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);
        var actualStartLocal = il.DeclareLocal(_types.Int32);
        var relStartLocal = il.DeclareLocal(_types.Int32);
        var actualSkipCountLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (args.Length == 0) return new List<object>(list)
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArgs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ListObjectFromEnumerableCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Parse start: relStart = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);

        // actualStart = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);

        il.MarkLabel(startDone);

        // Parse skipCount
        var hasSkipCount = il.DefineLabel();
        var skipCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasSkipCount);

        // No skipCount: skip to end
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, actualSkipCountLocal);
        il.Emit(OpCodes.Br, skipCountDone);

        il.MarkLabel(hasSkipCount);
        // Has skipCount: sc = ToIntegerOrInfinity(args[1], 0)
        // actualSkipCount = Max(0, Min(sc, len - actualStart))
        var scLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, scLocal);

        // Min(sc, len - actualStart)
        il.Emit(OpCodes.Ldloc, scLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        // Max(0, ...)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualSkipCountLocal);

        il.MarkLabel(skipCountDone);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add elements before actualStart: for (i = 0; i < actualStart; i++) result.Add(list[i])
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var beforeLoopStart = il.DefineLabel();
        var beforeLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, beforeLoopCondition);

        il.MarkLabel(beforeLoopStart);
        // ECMA-262 23.1.3.35 toSpliced: dense output — source holes become
        // undefined in the copy (uses Get + CreateDataPropertyOrThrow).
        il.Emit(OpCodes.Ldloc, resultLocal);
        EmitLoadElementUnholed(il, iLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(beforeLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Blt, beforeLoopStart);

        // Add inserted items from args[2..]
        var noInsert = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, noInsert);

        // for (i = 2; i < args.Length; i++) result.Add(args[i])
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, iLocal);

        var insertLoopStart = il.DefineLabel();
        var insertLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, insertLoopCondition);

        il.MarkLabel(insertLoopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(insertLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, insertLoopStart);

        il.MarkLabel(noInsert);

        // Add elements after actualStart + actualSkipCount
        // for (i = actualStart + actualSkipCount; i < len; i++) result.Add(list[i])
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualSkipCountLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        var afterLoopStart = il.DefineLabel();
        var afterLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, afterLoopCondition);

        il.MarkLabel(afterLoopStart);
        // toSpliced after-skip region: same unhole rule as the before loop.
        il.Emit(OpCodes.Ldloc, resultLocal);
        EmitLoadElementUnholed(il, iLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(afterLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Blt, afterLoopStart);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFill(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayFill(List<object> list, object[] args) -> List<object>
        // args[0] = value, args[1] = start (optional), args[2] = end (optional)
        // Mutates the list in-place, returns the same list reference
        var method = typeBuilder.DefineMethod(
            "ArrayFill",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayFill = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();

        // Check frozen ONLY (sealed/non-extensible allows modification of existing elements)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: false, checkExtensible: false);

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Object);
        var relStartLocal = il.DeclareLocal(_types.Int32);
        var actualStartLocal = il.DeclareLocal(_types.Int32);
        var relEndLocal = il.DeclareLocal(_types.Int32);
        var actualEndLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // value = args.Length > 0 ? args[0] : undefined
        var hasValue = il.DefineLabel();
        var valueDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasValue);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, valueDone);
        il.MarkLabel(hasValue);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(valueDone);

        // Parse start: relStart = args.Length > 1 ? ToIntegerOrInfinity(args[1], 0) : 0
        var hasStart = il.DefineLabel();
        var startParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasStart);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, relStartLocal);
        il.Emit(OpCodes.Br, startParseDone);
        il.MarkLabel(hasStart);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);
        il.MarkLabel(startParseDone);

        // actualStart = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);

        il.MarkLabel(startDone);

        // Parse end: relEnd = args.Length > 2 ? ToIntegerOrInfinity(args[2], len) : len
        var hasEnd = il.DefineLabel();
        var endParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Stloc, relEndLocal);
        il.Emit(OpCodes.Br, endParseDone);
        il.MarkLabel(hasEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relEndLocal);
        il.MarkLabel(endParseDone);

        // actualEnd = relEnd < 0 ? Max(len + relEnd, 0) : Min(relEnd, len)
        var endNotNegative = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNegative);

        // Negative: Max(len + relEnd, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualEndLocal);
        il.Emit(OpCodes.Br, endDone);

        il.MarkLabel(endNotNegative);
        // Non-negative: Min(relEnd, len)
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualEndLocal);

        il.MarkLabel(endDone);

        // Fill loop: for (i = actualStart; i < actualEnd; i++) list[i] = value
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);
        // list[i] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetSetMethod()!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        // i < actualEnd
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, actualEndLocal);
        il.Emit(OpCodes.Blt, loopStart);

        // return list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // Frozen return path - return unchanged list
        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFillProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFillProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        var paramArrayCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!;
        method.DefineParameter(2, System.Reflection.ParameterAttributes.None, "args")
            .SetCustomAttribute(paramArrayCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayFillProto = method;

        var il = method.GetILGenerator();
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var value = il.DeclareLocal(_types.Object);
        var start = il.DeclareLocal(_types.Double);
        var end = il.DeclareLocal(_types.Double);
        var k = il.DeclareLocal(_types.Double);

        var hasValue = il.DefineLabel();
        var valueDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasValue);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, valueDone);
        il.MarkLabel(hasValue);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, value);
        il.MarkLabel(valueDone);

        var hasStart = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasStart);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, start);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(hasStart);
        EmitGenericRelativeArrayIndex(il, runtime, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
        }, length, start);
        il.MarkLabel(startDone);

        var hasEnd = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, end);
        il.Emit(OpCodes.Br, endDone);
        il.MarkLabel(hasEnd);
        EmitGenericRelativeArrayIndex(il, runtime, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
        }, length, end);
        il.MarkLabel(endDone);

        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Stloc, k);
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, k);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, k);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayCopyWithin(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayCopyWithin(List<object> list, object[] args) -> List<object>
        // args[0] = target (required), args[1] = start (optional), args[2] = end (optional)
        // Copies a sequence of array elements within the array to target position
        // Mutates the list in-place, returns the same list reference
        var method = typeBuilder.DefineMethod(
            "ArrayCopyWithin",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayCopyWithin = method;

        var il = method.GetILGenerator();
        var frozenLabel = il.DefineLabel();

        // Check frozen ONLY (sealed/non-extensible allows modification of existing elements)
        EmitArrayFrozenSealedCheck(il, runtime, frozenLabel, checkSealed: false, checkExtensible: false);

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);        // len
        var relTargetLocal = il.DeclareLocal(_types.Int32);  // relativeTarget
        var toLocal = il.DeclareLocal(_types.Int32);         // to (actual target)
        var relStartLocal = il.DeclareLocal(_types.Int32);   // relativeStart
        var fromLocal = il.DeclareLocal(_types.Int32);       // from (actual start)
        var relEndLocal = il.DeclareLocal(_types.Int32);     // relativeEnd
        var finalLocal = il.DeclareLocal(_types.Int32);      // final (actual end)
        var countLocal = il.DeclareLocal(_types.Int32);      // count
        var iLocal = il.DeclareLocal(_types.Int32);          // loop counter
        var fromKeyLocal = il.DeclareLocal(_types.String);
        var toKeyLocal = il.DeclareLocal(_types.String);
        var copiedValueLocal = il.DeclareLocal(_types.Object);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // Parse target: relTarget = args.Length > 0 ? ToIntegerOrInfinity(args[0], 0) : 0
        var hasTarget = il.DefineLabel();
        var targetParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasTarget);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, relTargetLocal);
        il.Emit(OpCodes.Br, targetParseDone);
        il.MarkLabel(hasTarget);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relTargetLocal);
        il.MarkLabel(targetParseDone);

        // to = relTarget < 0 ? Max(len + relTarget, 0) : Min(relTarget, len)
        var targetNotNegative = il.DefineLabel();
        var targetDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relTargetLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, targetNotNegative);

        // Negative: Max(len + relTarget, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relTargetLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, toLocal);
        il.Emit(OpCodes.Br, targetDone);

        il.MarkLabel(targetNotNegative);
        // Non-negative: Min(relTarget, len)
        il.Emit(OpCodes.Ldloc, relTargetLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, toLocal);

        il.MarkLabel(targetDone);

        // Parse start: relStart = args.Length > 1 ? ToIntegerOrInfinity(args[1], 0) : 0
        var hasStart = il.DefineLabel();
        var startParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasStart);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, relStartLocal);
        il.Emit(OpCodes.Br, startParseDone);
        il.MarkLabel(hasStart);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);
        il.MarkLabel(startParseDone);

        // from = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, fromLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, fromLocal);

        il.MarkLabel(startDone);

        // Parse end: relEnd = args.Length > 2 ? ToIntegerOrInfinity(args[2], len) : len
        var hasEnd = il.DefineLabel();
        var endParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Stloc, relEndLocal);
        il.Emit(OpCodes.Br, endParseDone);
        il.MarkLabel(hasEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relEndLocal);
        il.MarkLabel(endParseDone);

        // final = relEnd < 0 ? Max(len + relEnd, 0) : Min(relEnd, len)
        var endNotNegative = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNegative);

        // Negative: Max(len + relEnd, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, finalLocal);
        il.Emit(OpCodes.Br, endDone);

        il.MarkLabel(endNotNegative);
        // Non-negative: Min(relEnd, len)
        il.Emit(OpCodes.Ldloc, relEndLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, finalLocal);

        il.MarkLabel(endDone);

        // count = Min(final - from, len - to)
        il.Emit(OpCodes.Ldloc, finalLocal);
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, countLocal);

        // If count <= 0, skip the copy
        var returnList = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, returnList);

        // Check if we need to copy backward to handle overlap
        // if (from < to && to < from + count) copy backward else copy forward
        var copyForward = il.DefineLabel();
        var copyDone = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Bge, copyForward);  // from >= to, copy forward

        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Bge, copyForward);  // to >= from + count, copy forward

        // Copy backward: for (i = count - 1; i >= 0; i--) list[to + i] = list[from + i]
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var backwardLoopStart = il.DefineLabel();
        var backwardLoopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, backwardLoopCondition);

        il.MarkLabel(backwardLoopStart);
        // Copy uses HasProperty/Get/Set/Delete rather than raw List indexing.
        // Argument coercion is observable and may shrink the array after len
        // was captured; property operations therefore must tolerate indexes
        // outside the current dense storage and may grow it again.
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKeyLocal);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKeyLocal);

        var backwardDeleteTarget = il.DefineLabel();
        var backwardNext = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fromKeyLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, backwardDeleteTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fromKeyLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, copiedValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldloc, copiedValueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetIndexStrict);
        il.Emit(OpCodes.Br, backwardNext);
        il.MarkLabel(backwardDeleteTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toKeyLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(backwardNext);

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(backwardLoopCondition);
        // i >= 0
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, backwardLoopStart);

        il.Emit(OpCodes.Br, copyDone);

        // Copy forward: for (i = 0; i < count; i++) list[to + i] = list[from + i]
        il.MarkLabel(copyForward);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var forwardLoopStart = il.DefineLabel();
        var forwardLoopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, forwardLoopCondition);

        il.MarkLabel(forwardLoopStart);
        il.Emit(OpCodes.Ldloc, fromLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKeyLocal);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKeyLocal);

        var forwardDeleteTarget = il.DefineLabel();
        var forwardNext = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fromKeyLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, forwardDeleteTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fromKeyLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, copiedValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ldloc, copiedValueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetIndexStrict);
        il.Emit(OpCodes.Br, forwardNext);
        il.MarkLabel(forwardDeleteTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toKeyLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(forwardNext);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(forwardLoopCondition);
        // i < count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, forwardLoopStart);

        il.MarkLabel(copyDone);

        // return list
        il.MarkLabel(returnList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // Frozen return path - return unchanged list
        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayCopyWithinProto(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayCopyWithinProto",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        var paramArrayCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!;
        method.DefineParameter(2, System.Reflection.ParameterAttributes.None, "args")
            .SetCustomAttribute(paramArrayCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayCopyWithinProto = method;

        var il = method.GetILGenerator();
        var (receiver, length) = EmitGenericArrayReceiverAndLength(il, runtime);
        var to = il.DeclareLocal(_types.Double);
        var from = il.DeclareLocal(_types.Double);
        var final = il.DeclareLocal(_types.Double);
        var count = il.DeclareLocal(_types.Double);
        var direction = il.DeclareLocal(_types.Double);
        var fromKey = il.DeclareLocal(_types.String);
        var toKey = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);

        var hasTarget = il.DefineLabel();
        var targetDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasTarget);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, to);
        il.Emit(OpCodes.Br, targetDone);
        il.MarkLabel(hasTarget);
        EmitGenericRelativeArrayIndex(il, runtime, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
        }, length, to);
        il.MarkLabel(targetDone);

        var hasStart = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasStart);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, from);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(hasStart);
        EmitGenericRelativeArrayIndex(il, runtime, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
        }, length, from);
        il.MarkLabel(startDone);

        var hasEnd = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Stloc, final);
        il.Emit(OpCodes.Br, endDone);
        il.MarkLabel(hasEnd);
        EmitGenericRelativeArrayIndex(il, runtime, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
        }, length, final);
        il.MarkLabel(endDone);

        // count = min(final - from, length - to)
        il.Emit(OpCodes.Ldloc, final);
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Double, _types.Double));
        il.Emit(OpCodes.Stloc, count);

        // Overlapping ranges copy backward; all other ranges copy forward.
        var forward = il.DefineLabel();
        var directionDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Bge, forward);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Bge, forward);
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, from);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, to);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Stloc, direction);
        il.Emit(OpCodes.Br, directionDone);
        il.MarkLabel(forward);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Stloc, direction);
        il.MarkLabel(directionDone);

        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ble, done);
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, fromKey);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, toKey);

        var deleteTarget = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, fromKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(deleteTarget);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, toKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.DeletePropertyStrict);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, from);
        il.Emit(OpCodes.Ldloc, direction);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, from);
        il.Emit(OpCodes.Ldloc, to);
        il.Emit(OpCodes.Ldloc, direction);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, to);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ret);
    }
}

