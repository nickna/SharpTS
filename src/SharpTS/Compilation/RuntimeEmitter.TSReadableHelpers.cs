using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the async-iterator helpers on the standalone <c>$Readable</c> class (#1025):
/// <c>reduce</c>/<c>some</c>/<c>every</c>/<c>find</c> (consuming → resolved $Promise) and
/// <c>drop</c>/<c>take</c>/<c>flatMap</c>/<c>asIndexedPairs</c> (lazy → a new $Readable carrying
/// the transformed chunks). Mirrors <see cref="SharpTS.Runtime.Types.SharpTSReadable"/>'s
/// buffer-based interpreter helpers. All BCL-only / emitted-runtime — standalone preserved.
///
/// Emitted in Phase 2b (after Push/Pipe + SetObjectMode), so it can use TSReadablePush /
/// TSReadableSetObjectMode / TSReadableCtor; the consuming helpers use TSPromiseResolve and
/// IsTruthy; asIndexedPairs/flatMap use TSArrayCtor / TSArrayElementsGetter.
/// </summary>
public partial class RuntimeEmitter
{
    private MethodBuilder _tsReadableDrainToList = null!;

    private void EmitTSReadableIterHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        EmitTSReadableDrainToList(typeBuilder, runtime, queueType);
        EmitTSReadableReduce(typeBuilder, runtime);
        EmitTSReadablePredicate(typeBuilder, runtime, "Some");
        EmitTSReadablePredicate(typeBuilder, runtime, "Every");
        EmitTSReadablePredicate(typeBuilder, runtime, "Find");
        EmitTSReadableDropTake(typeBuilder, runtime, "Drop");
        EmitTSReadableDropTake(typeBuilder, runtime, "Take");
        EmitTSReadableFlatMap(typeBuilder, runtime);
        EmitTSReadableAsIndexedPairs(typeBuilder, runtime);
    }

    /// <summary>private List&lt;object&gt; DrainToList() — drains _readBuffer into a fresh list.</summary>
    private void EmitTSReadableDrainToList(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var method = typeBuilder.DefineMethod("DrainToList", MethodAttributes.Private, _types.ListOfObject, Type.EmptyTypes);
        _tsReadableDrainToList = method;

        var il = method.GetILGenerator();
        var countGetter = _types.GetProperty(queueType, "Count")!.GetGetMethod()!;
        var dequeue = _types.GetMethod(queueType, "Dequeue")!;
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var loop = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, listLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, done);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeue);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    // Helpers shared across the methods below.
    private MethodInfo ListCountGetter => _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
    private MethodInfo ListItemGetter => _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32);

    /// <summary>Emits a $TSFunction.Invoke(new object[]{ ...args }) call; the function ref and args
    /// are produced by the supplied delegates. Leaves the (object) result on the stack.</summary>
    private void EmitInvokeUserFn(ILGenerator il, EmittedRuntime runtime, LocalBuilder fnLocal, Action<ILGenerator> loadArgsArray)
    {
        il.Emit(OpCodes.Ldloc, fnLocal);
        loadArgsArray(il);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
    }

    /// <summary>public object Reduce(object fn, object initial)</summary>
    private void EmitTSReadableReduce(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Reduce", MethodAttributes.Public, _types.Object, [_types.Object, _types.Object]);
        var il = method.GetILGenerator();

        var fnLocal = il.DeclareLocal(runtime.TSFunctionType);
        var itemsLocal = il.DeclareLocal(_types.ListOfObject);
        var accLocal = il.DeclareLocal(_types.Object);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsReadableDrainToList);
        il.Emit(OpCodes.Stloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        // bool hasInitial = !(initial is UndefinedType);
        var noInitial = il.DefineLabel();
        var afterSeed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noInitial);

        // acc = initial; i = 0;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, afterSeed);

        il.MarkLabel(noInitial);
        // if (count == 0) return TSPromiseResolve(undefined);
        var haveItems = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Brtrue, haveItems);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(haveItems);
        // acc = items[0]; i = 1;
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, ListItemGetter);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(afterSeed);
        // for (; i < count; i++) acc = fn.Invoke([acc, items[i]]);
        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, endLoop);

        EmitInvokeUserFn(il, runtime, fnLocal, gen =>
        {
            gen.Emit(OpCodes.Ldc_I4_2);
            gen.Emit(OpCodes.Newarr, _types.Object);
            gen.Emit(OpCodes.Dup);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ldloc, accLocal);
            gen.Emit(OpCodes.Stelem_Ref);
            gen.Emit(OpCodes.Dup);
            gen.Emit(OpCodes.Ldc_I4_1);
            gen.Emit(OpCodes.Ldloc, itemsLocal);
            gen.Emit(OpCodes.Ldloc, iLocal);
            gen.Emit(OpCodes.Callvirt, ListItemGetter);
            gen.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Stloc, accLocal);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(endLoop);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>public object Some/Every/Find(object fn) — drains, predicates, returns resolved $Promise.</summary>
    private void EmitTSReadablePredicate(TypeBuilder typeBuilder, EmittedRuntime runtime, string kind)
    {
        var method = typeBuilder.DefineMethod(kind, MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var fnLocal = il.DeclareLocal(runtime.TSFunctionType);
        var itemsLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsReadableDrainToList);
        il.Emit(OpCodes.Stloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        var hitLabel = il.DefineLabel();   // the per-item "decisive" branch (early return)
        var continueLabel = il.DefineLabel();

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, endLoop);

        // bool t = IsTruthy(fn.Invoke([items[i]]));
        EmitInvokeUserFn(il, runtime, fnLocal, gen =>
        {
            gen.Emit(OpCodes.Ldc_I4_1);
            gen.Emit(OpCodes.Newarr, _types.Object);
            gen.Emit(OpCodes.Dup);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ldloc, itemsLocal);
            gen.Emit(OpCodes.Ldloc, iLocal);
            gen.Emit(OpCodes.Callvirt, ListItemGetter);
            gen.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        // Every short-circuits on a FALSE result; Some/Find short-circuit on a TRUE result.
        if (kind == "Every")
            il.Emit(OpCodes.Brfalse, hitLabel);
        else
            il.Emit(OpCodes.Brtrue, hitLabel);

        // not decisive → i++; continue
        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        // decisive hit → early return
        il.MarkLabel(hitLabel);
        if (kind == "Some")
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
        }
        else if (kind == "Every")
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, _types.Boolean);
        }
        else // Find — return the matching item
        {
            il.Emit(OpCodes.Ldloc, itemsLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Callvirt, ListItemGetter);
        }
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(endLoop);
        // Default result: Some->false, Every->true, Find->undefined
        if (kind == "Some")
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, _types.Boolean);
        }
        else if (kind == "Every")
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
        }
        else
        {
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        }
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>public object Drop/Take(object n) — returns a new $Readable with the subset.</summary>
    private void EmitTSReadableDropTake(TypeBuilder typeBuilder, EmittedRuntime runtime, string kind)
    {
        var method = typeBuilder.DefineMethod(kind, MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var nLocal = il.DeclareLocal(_types.Int32);
        var itemsLocal = il.DeclareLocal(_types.ListOfObject);
        var rLocal = il.DeclareLocal(runtime.TSReadableType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);

        // n = (int)ToNumber(arg1)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, nLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsReadableDrainToList);
        il.Emit(OpCodes.Stloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        EmitNewHelperReadable(il, runtime, rLocal, objectModeFromThis: true);

        // start index: Drop -> n, Take -> 0;  end: Drop -> count, Take -> min(n,count)
        if (kind == "Drop")
        {
            il.Emit(OpCodes.Ldloc, nLocal);
            il.Emit(OpCodes.Stloc, iLocal);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            // count = min(n, count)
            var keepCount = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, nLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Bge, keepCount);
            il.Emit(OpCodes.Ldloc, nLocal);
            il.Emit(OpCodes.Stloc, countLocal);
            il.MarkLabel(keepCount);
        }

        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, endLoop);
        // r.Push(items[i]);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, ListItemGetter);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(endLoop);

        EmitPushNullAndReturn(il, runtime, rLocal);
    }

    /// <summary>public object FlatMap(object fn) — maps each chunk; flattens $Array results.</summary>
    private void EmitTSReadableFlatMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FlatMap", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var fnLocal = il.DeclareLocal(runtime.TSFunctionType);
        var itemsLocal = il.DeclareLocal(_types.ListOfObject);
        var rLocal = il.DeclareLocal(runtime.TSReadableType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var mappedLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsReadableDrainToList);
        il.Emit(OpCodes.Stloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        EmitNewHelperReadable(il, runtime, rLocal, objectModeFromThis: false, forceObjectMode: true);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, endLoop);

        // mapped = fn.Invoke([items[i]]);
        EmitInvokeUserFn(il, runtime, fnLocal, gen =>
        {
            gen.Emit(OpCodes.Ldc_I4_1);
            gen.Emit(OpCodes.Newarr, _types.Object);
            gen.Emit(OpCodes.Dup);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ldloc, itemsLocal);
            gen.Emit(OpCodes.Ldloc, iLocal);
            gen.Emit(OpCodes.Callvirt, ListItemGetter);
            gen.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Stloc, mappedLocal);

        // if (mapped is $Array) push each element; else push mapped.
        var notArray = il.DefineLabel();
        var afterPush = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, mappedLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notArray);

        // var elems = ((TSArray)mapped).Elements; for j: r.Push(elems[j]);
        var elemsLocal = il.DeclareLocal(_types.ListOfObject);
        var jLocal = il.DeclareLocal(_types.Int32);
        var elemCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, mappedLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Stloc, elemsLocal);
        il.Emit(OpCodes.Ldloc, elemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, elemCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var innerLoop = il.DefineLabel();
        var innerEnd = il.DefineLabel();
        il.MarkLabel(innerLoop);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, elemCountLocal);
        il.Emit(OpCodes.Bge, innerEnd);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldloc, elemsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, ListItemGetter);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, innerLoop);
        il.MarkLabel(innerEnd);
        il.Emit(OpCodes.Br, afterPush);

        il.MarkLabel(notArray);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldloc, mappedLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(afterPush);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(endLoop);

        EmitPushNullAndReturn(il, runtime, rLocal);
    }

    /// <summary>public object AsIndexedPairs() — emits [index, value] pairs into a new $Readable.</summary>
    private void EmitTSReadableAsIndexedPairs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("AsIndexedPairs", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        var itemsLocal = il.DeclareLocal(_types.ListOfObject);
        var rLocal = il.DeclareLocal(runtime.TSReadableType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsReadableDrainToList);
        il.Emit(OpCodes.Stloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Callvirt, ListCountGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        EmitNewHelperReadable(il, runtime, rLocal, objectModeFromThis: false, forceObjectMode: true);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, endLoop);

        // var pair = new List<object>(); pair.Add((double)i boxed); pair.Add(items[i]);
        var pairLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, pairLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloc, itemsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, ListItemGetter);
        il.Emit(OpCodes.Callvirt, listAdd);

        // r.Push(new $Array(pair));
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(endLoop);

        EmitPushNullAndReturn(il, runtime, rLocal);
    }

    /// <summary>Emits: rLocal = new $Readable(); rLocal.SetObjectMode(forceObjectMode ? true : this._objectMode);</summary>
    private void EmitNewHelperReadable(ILGenerator il, EmittedRuntime runtime, LocalBuilder rLocal, bool objectModeFromThis, bool forceObjectMode = false)
    {
        il.Emit(OpCodes.Newobj, runtime.TSReadableCtor);
        il.Emit(OpCodes.Stloc, rLocal);
        il.Emit(OpCodes.Ldloc, rLocal);
        if (forceObjectMode)
        {
            il.Emit(OpCodes.Ldc_I4_1);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsReadableObjectModeField);
        }
        il.Emit(OpCodes.Callvirt, runtime.TSReadableSetObjectMode);
    }

    /// <summary>Emits: rLocal.Push(null); return rLocal;</summary>
    private void EmitPushNullAndReturn(ILGenerator il, EmittedRuntime runtime, LocalBuilder rLocal)
    {
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ret);
    }
}
