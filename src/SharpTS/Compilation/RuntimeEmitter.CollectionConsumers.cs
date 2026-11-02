using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct CollectionConsumerInputs(
        EmittedErrorRuntime Errors,
        EmittedObjectReadRuntime ObjectRead,
        EmittedObjectPrototypeRuntime Prototypes,
        EmittedInvocationRuntime Invocation,
        EmittedIteratorRecordRuntime Records,
        EmittedIteratorProtocolRuntime Protocol,
        EmittedSymbolRuntime Symbols,
        Type UndefinedType,
        ConstructorInfo? MapIteratorConstructor,
        ConstructorInfo? SetIteratorConstructor,
        EmittedTypedArrayImplementation? TypedArrays,
        EmittedBufferRuntime? Buffer);

    private static CollectionConsumerInputs GetCollectionConsumerInputs(EmittedRuntime runtime) =>
        new(runtime.Errors, runtime.ObjectRead, runtime.ObjectPrototypes, runtime.Invocation, runtime.IteratorRecords,
            runtime.IteratorProtocol, runtime.Symbols, runtime.Sentinels.UndefinedType,
            runtime.Map?.IteratorConstructor, runtime.Set?.IteratorConstructor,
            runtime.TypedArrays.Implementation, runtime.Buffer);

    /// <summary>
    /// Emits a collection consumer's incremental iterator loop. Acquisition and
    /// stepping remain outside the consumer's catch; only processing an obtained
    /// value owns IteratorClose. This is compiler construction code, not a guest
    /// callback or a new emitted runtime metadata owner.
    /// </summary>
    private void EmitCollectionConsumerLoop(ILGenerator il, CollectionConsumerInputs inputs,
        Action emitSource, Action<LocalBuilder, LocalBuilder> emitConsumer,
        Label? arrayLikeFallback = null, Action<LocalBuilder>? convertValue = null)
    {
        var source = il.DeclareLocal(_types.Object);
        var iteratorMethod = il.DeclareLocal(_types.Object);
        var iterator = il.DeclareLocal(_types.Object);
        var next = il.DeclareLocal(_types.Object);
        var result = il.DeclareLocal(_types.Object);
        var enumerator = il.DeclareLocal(_types.IEnumerator);
        var indexedLength = il.DeclareLocal(_types.Int32);
        var indexed = il.DeclareLocal(_types.Boolean);
        var value = il.DeclareLocal(_types.Object);
        var index = il.DeclareLocal(_types.Int32);
        var absentMethod = il.DefineLabel();
        var invalidSource = il.DefineLabel();
        var nativeIterator = il.DefineLabel();
        var prepareIterator = il.DefineLabel();
        var loop = il.DefineLabel();
        var nativeStep = il.DefineLabel();
        var indexedStep = il.DefineLabel();
        var consume = il.DefineLabel();
        var done = il.DefineLabel();

        emitSource();
        il.Emit(OpCodes.Stloc, source);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Brfalse, invalidSource);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, invalidSource);

        // Ordinary symbol Get observes inherited accessors and array-prototype
        // overrides. Keep the selected method so acquisition cannot read it twice.
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldsfld, inputs.Symbols.Iterator);
        il.Emit(OpCodes.Call, inputs.ObjectRead.Index);
        il.Emit(OpCodes.Stloc, iteratorMethod);
        il.Emit(OpCodes.Ldloc, iteratorMethod);
        il.Emit(OpCodes.Brfalse, invalidSource);
        il.Emit(OpCodes.Ldloc, iteratorMethod);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, absentMethod);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, iteratorMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, inputs.Invocation.Method);
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Br, prepareIterator);

        il.MarkLabel(absentMethod);
        // A native carrier's intrinsic iterator has no ordinary symbol slot.
        // An explicitly shadowing slot, including a getterless accessor or an
        // inherited undefined value, must not restore that intrinsic fallback.
        // Inspect storage without invoking an already-acquired getter again.
        var symbolOwner = il.DeclareLocal(_types.Object);
        var symbolStorage = il.DeclareLocal(_types.DictionaryObjectObject);
        var inspectSymbolOwner = il.DefineLabel();
        var nextSymbolOwner = il.DefineLabel();
        var noSymbolClaim = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Stloc, symbolOwner);
        il.MarkLabel(inspectSymbolOwner);
        il.Emit(OpCodes.Ldloc, symbolOwner);
        il.Emit(OpCodes.Brfalse, noSymbolClaim);
        il.Emit(OpCodes.Ldloc, symbolOwner);
        il.Emit(OpCodes.Call, inputs.Symbols.TryGetStorage);
        il.Emit(OpCodes.Stloc, symbolStorage);
        il.Emit(OpCodes.Ldloc, symbolStorage);
        il.Emit(OpCodes.Brfalse, nextSymbolOwner);
        il.Emit(OpCodes.Ldloc, symbolStorage);
        il.Emit(OpCodes.Ldsfld, inputs.Symbols.Iterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brtrue, invalidSource);
        il.MarkLabel(nextSymbolOwner);
        il.Emit(OpCodes.Ldloc, symbolOwner);
        il.Emit(OpCodes.Call, inputs.Prototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, symbolOwner);
        il.Emit(OpCodes.Br, inspectSymbolOwner);
        il.MarkLabel(noSymbolClaim);

        // Native carriers have intrinsic iteration without an ordinary guest
        // symbol slot. Arrays/strings have real prototypes, so an absent method
        // on those values must not silently restore the deleted intrinsic.
        foreach (var type in new[] { _types.ListOfObject, _types.String, _types.DictionaryStringObject })
        {
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Brtrue, invalidSource);
        }

        if (inputs.MapIteratorConstructor is { } mapIterator)
        {
            var notMap = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Brfalse, notMap);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryObjectObject, "Keys")!.GetGetMethod()!);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject)!);
            il.Emit(OpCodes.Ldc_I4_2); // Map default iteration produces entry arrays.
            il.Emit(OpCodes.Newobj, mapIterator);
            il.Emit(OpCodes.Stloc, iterator);
            il.Emit(OpCodes.Br, nativeIterator);
            il.MarkLabel(notMap);
        }
        if (inputs.SetIteratorConstructor is { } setIterator)
        {
            var notSet = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
            il.Emit(OpCodes.Brfalse, notSet);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject)!);
            il.Emit(OpCodes.Newobj, setIterator);
            il.Emit(OpCodes.Stloc, iterator);
            il.Emit(OpCodes.Br, nativeIterator);
            il.MarkLabel(notSet);
        }
        if (inputs.TypedArrays is { } typedArrays)
            EmitIndexedCarrier(typedArrays.BaseType, typedArrays.LengthGetter);
        if (inputs.Buffer is { } buffer)
            EmitIndexedCarrier(buffer.Type, buffer.LengthGetter);

        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, _types.IEnumerator);
        il.Emit(OpCodes.Stloc, enumerator);
        il.Emit(OpCodes.Ldloc, enumerator);
        var tryEnumerable = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tryEnumerable);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Br, prepareIterator);
        il.MarkLabel(tryEnumerable);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, _types.IEnumerable);
        il.Emit(OpCodes.Brfalse, invalidSource);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Castclass, _types.IEnumerable);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerable, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Br, nativeIterator);

        il.MarkLabel(invalidSource);
        if (arrayLikeFallback is { } fallback)
            il.Emit(OpCodes.Br, fallback);
        else
            GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException,
                inputs.Errors.TypeErrorConstructor, "Value is not iterable");

        il.MarkLabel(prepareIterator);
        // The captured-next helper distinguishes a native adapter with no
        // guest slot from an explicit undefined/getterless replacement.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, enumerator);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Call, inputs.Records.NextMethod);
        il.Emit(OpCodes.Stloc, next);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(nativeIterator);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Castclass, _types.IEnumerator);
        il.Emit(OpCodes.Stloc, enumerator);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexed);
        il.Emit(OpCodes.Brtrue, indexedStep);
        il.Emit(OpCodes.Ldloc, enumerator);
        il.Emit(OpCodes.Brtrue, nativeStep);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Call, inputs.Records.InvokeNext);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Call, inputs.Protocol.Done);
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Call, inputs.Protocol.Value);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, consume);

        il.MarkLabel(nativeStep);
        il.Emit(OpCodes.Ldloc, enumerator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, enumerator);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumerator, "Current"));
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, consume);

        il.MarkLabel(indexedStep);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, indexedLength);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, inputs.ObjectRead.Index);
        il.Emit(OpCodes.Stloc, value);

        il.MarkLabel(consume);
        convertValue?.Invoke(value);
        il.BeginExceptionBlock();
        emitConsumer(value, index);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        var noCloseTarget = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Brfalse, noCloseTarget);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, inputs.Protocol.Close);
        il.MarkLabel(noCloseTarget);
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);

        void EmitIndexedCarrier(Type type, MethodInfo length)
        {
            var nextCarrier = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Brfalse, nextCarrier);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Callvirt, length);
            il.Emit(OpCodes.Stloc, indexedLength);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, indexed);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(nextCarrier);
        }
    }
}
