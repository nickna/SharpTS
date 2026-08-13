using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits JsonParseWithReviver(text, reviver). Implements ECMA-262 25.5.1.1
    /// JSON.parse(reviver) by parsing into a tree of <c>Dictionary&lt;string,
    /// object?&gt;</c> / <c>List&lt;object?&gt;</c>, synthesizing a root holder
    /// <c>{ "": parsed }</c>, and walking it via the in-place
    /// <see cref="EmitApplyReviverHelper"/> spec walker.
    ///
    /// <para>Why a synthetic root holder: the spec's
    /// <c>InternalizeJSONProperty(holder, name, reviver)</c> always invokes the
    /// reviver with <c>this</c> = holder; the top-level call therefore needs a
    /// synthetic parent so the root reviver call still receives an object as
    /// <c>this</c> rather than <c>null</c>. This also means user revivers can
    /// inspect the wrapper at <c>this[""]</c>.</para>
    /// </summary>
    private void EmitJsonParseWithReviver(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var applyReviver = EmitApplyReviverHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonParseWithReviver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.JsonParseWithReviver = method;

        var il = method.GetILGenerator();
        var noReviverLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (reviver == null) goto noReviverLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noReviverLabel);

        // parsed = JsonParse(text)
        var parsedLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);
        il.Emit(OpCodes.Stloc, parsedLocal);

        // root = new Dictionary<string, object?> { [""] = parsed };
        var rootLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, rootLocal);
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));

        // return ApplyReviver(root, "", reviver)
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, applyReviver);
        il.Emit(OpCodes.Br, endLabel);

        // No reviver - just parse
        il.MarkLabel(noReviverLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the recursive <c>ApplyReviver(holder, key, reviver) -&gt; object?</c>
    /// helper. Implements ECMA-262 25.5.1.1.1 InternalizeJSONProperty:
    /// <list type="number">
    ///   <item>val = Get(holder, key) — Proxy <c>get</c> trap when holder is a Proxy.</item>
    ///   <item>If val is iterable (Dictionary / List / Proxy), iterate own keys
    ///     (Proxy <c>ownKeys</c> trap when val is a Proxy). For each key,
    ///     recurse and either Set or Delete on val (Proxy traps when applicable).</item>
    ///   <item>Call reviver with <c>this</c> = holder, args = (key, val).</item>
    /// </list>
    /// All Proxy interactions go through <see cref="EmitProxyTypeCheck"/> and
    /// reflection on the proxy's runtime type — no compile-time SharpTS.dll
    /// reference is embedded in the emitted assembly.
    /// </summary>
    private MethodBuilder EmitApplyReviverHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ApplyReviver",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // val = HolderGet(holder, key)
        var valLocal = il.DeclareLocal(_types.Object);
        EmitHolderGet(il, runtime, ldHolder: () => il.Emit(OpCodes.Ldarg_0), ldKey: () => il.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Stloc, valLocal);

        var afterIterLabel = il.DefineLabel();

        // Dispatch on val's runtime type. Order matters: Proxy check is by
        // FullName (object identity), so it doesn't conflict with isinst on
        // List/Dictionary. We do Proxy first because a Proxy value-shaped
        // wrapper around a List/Dict would otherwise route to the iteration
        // path and lose trap dispatch.
        EmitReviverProxyBranch(il, runtime, method, valLocal, afterIterLabel);
        EmitReviverListBranch(il, runtime, method, valLocal, afterIterLabel);
        EmitReviverDictBranch(il, runtime, method, valLocal, afterIterLabel);

        il.MarkLabel(afterIterLabel);

        // ECMA-262 step 3: Call reviver with `this` = holder, args = (key, val).
        // Function expressions emit a leading `__this` parameter; raw Invoke
        // would shift the args. InvokeWithThis takes (thisArg, args) and
        // handles the prepend correctly (see SharpTSProxy.InvokeTrap and
        // $TSFunction.InvokeWithThis for the parameter-name detection).
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_0);   // thisArg = holder
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits the Proxy branch of ApplyReviver: if val is a SharpTSProxy, snapshot
    /// TrapOwnKeys, recurse on each key with val as the new holder, then dispatch
    /// TrapSet (kept) or TrapDeleteProperty (newElement is null/undefined).
    /// Falls through to the next branch if val is not a proxy.
    /// </summary>
    private void EmitReviverProxyBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
    {
        var notProxyLabel = il.DefineLabel();
        var proxyLabel = il.DefineLabel();

        // EmitProxyTypeCheck calls GetType() — Callvirt against a null
        // reference throws NullReferenceException. Skip the check entirely
        // for null/primitive vals (which is the common case during the walk).
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Brfalse, notProxyLabel);

        EmitProxyTypeCheck(il, () => il.Emit(OpCodes.Ldloc, valLocal), proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);

        // proxyType = Type.GetType("SharpTS.Runtime.Types.SharpTSProxy, SharpTS")
        var proxyTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSProxy, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, proxyTypeLocal);

        // keys = (List<string>)proxyType.GetMethod("TrapOwnKeys").Invoke(val, [null])
        var keysLocal = il.DeclareLocal(_types.ListOfString);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapOwnKeys");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Castclass, _types.ListOfString);
        il.Emit(OpCodes.Stloc, keysLocal);

        // Cache TrapSet / TrapDeleteProperty MethodInfo across the loop.
        var trapSetMi = il.DeclareLocal(_types.MethodInfo);
        var trapDelMi = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapSet");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, trapSetMi);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapDeleteProperty");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, trapDelMi);

        // for (int i = 0; i < keys.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // prop = keys[i]
        var propLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, propLocal);

        // newElement = ApplyReviver(val, prop, reviver)
        var newElemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, applyReviverMethod);
        il.Emit(OpCodes.Stloc, newElemLocal);

        // ECMA-262 step 2.b.iii.3 / 2.c.ii.2: if newElement is undefined,
        // delete; else set. We treat C# null and the $Undefined singleton
        // both as "undefined" here (parity with the interpreter helper).
        var deleteLabel = il.DefineLabel();
        var setLabel = il.DefineLabel();
        var endIfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Brfalse, deleteLabel);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, deleteLabel);
        il.Emit(OpCodes.Br, setLabel);

        il.MarkLabel(setLabel);
        // TrapSet(val, prop, newElement, null)
        il.Emit(OpCodes.Ldloc, trapSetMi);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Stelem_Ref);
        // [2] = null (Interpreter) — already null from Newarr
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, endIfLabel);

        il.MarkLabel(deleteLabel);
        // TrapDeleteProperty(val, prop, null)
        il.Emit(OpCodes.Ldloc, trapDelMi);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Stelem_Ref);
        // [1] = null (Interpreter)
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endIfLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, afterIterLabel);

        il.MarkLabel(notProxyLabel);
    }

    /// <summary>
    /// Emits the List branch of ApplyReviver: iterate by index 0..Count, recurse
    /// with val as the new holder, then list[i] = newElement (compiled mode does
    /// not model array holes; null is stored when the reviver returns
    /// null/undefined). Falls through if val is not a List&lt;object?&gt;.
    /// </summary>
    private void EmitReviverListBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
    {
        var notListLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // for (int i = 0; i < list.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // prop = i.ToString(InvariantCulture) — spec: Internalize uses ToString(F(I))
        var propLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, propLocal);

        // newElement = ApplyReviver(val, prop, reviver)
        var newElemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, applyReviverMethod);
        il.Emit(OpCodes.Stloc, newElemLocal);

        // Normalize $Undefined to null so the stored list slot reads as null
        // when joined/stringified; keeps parity with the no-reviver path
        // where missing indexes are absent rather than undefined-tagged.
        var stripUndefDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, stripUndefDoneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, newElemLocal);
        il.MarkLabel(stripUndefDoneLabel);

        // ECMA-262 25.5.1.1.1 InternalizeJSONProperty step 2.b.iii.3.a:
        // CreateDataProperty(val, ToString(I), newElement). When the property
        // already exists on the holder with Configurable: false (as set by a
        // reviver-side defineProperty on `this`), [[DefineOwnProperty]] must
        // return false silently and the slot is left untouched. Mirror that
        // here by consulting PDS; without this guard the direct set_Item
        // would punch through the non-configurable lock.
        var doSetLabel = il.DefineLabel();
        var afterSetLabel = il.DefineLabel();
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, doSetLabel);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doSetLabel);
        // Non-configurable: skip set, fall through to the increment.
        il.Emit(OpCodes.Br, afterSetLabel);

        il.MarkLabel(doSetLabel);
        // list[i] = newElement
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "set_Item", [_types.Int32, _types.Object]));
        il.MarkLabel(afterSetLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, afterIterLabel);

        il.MarkLabel(notListLabel);
    }

    /// <summary>
    /// Emits the Dictionary branch of ApplyReviver: snapshot keys (so revivers
    /// that defineProperty on `this` don't shift the iteration), recurse on each
    /// key with val as the new holder, then val[prop] = newElement (or
    /// val.Remove(prop) when newElement is null/undefined). Falls through if val
    /// is not a Dictionary&lt;string, object?&gt;.
    /// </summary>
    private void EmitReviverDictBranch(ILGenerator il, EmittedRuntime runtime, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
    {
        var notDictLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Snapshot keys to a List<string>: spec EnumerableOwnProperties is
        // captured before iteration. Without snapshotting, dict.Keys is a live
        // KeyCollection — adding/removing keys during the loop throws.
        var keysLocal = il.DeclareLocal(_types.ListOfString);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfString));
        il.Emit(OpCodes.Stloc, keysLocal);

        // foreach (var k in dict.Keys) keys.Add(k);
        var keyEnumLocal = il.DeclareLocal(typeof(Dictionary<string, object>.KeyCollection.Enumerator));
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt,
            typeof(Dictionary<string, object>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keyEnumLocal);

        var keyCopyStart = il.DefineLabel();
        var keyCopyEnd = il.DefineLabel();
        il.MarkLabel(keyCopyStart);
        il.Emit(OpCodes.Ldloca, keyEnumLocal);
        il.Emit(OpCodes.Call,
            typeof(Dictionary<string, object>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, keyCopyEnd);

        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloca, keyEnumLocal);
        il.Emit(OpCodes.Call,
            typeof(Dictionary<string, object>.KeyCollection.Enumerator)
                .GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "Add", [_types.String]));
        il.Emit(OpCodes.Br, keyCopyStart);
        il.MarkLabel(keyCopyEnd);

        // for (int i = 0; i < keys.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // prop = keys[i]
        var propLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, propLocal);

        // newElement = ApplyReviver(val, prop, reviver)
        var newElemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, applyReviverMethod);
        il.Emit(OpCodes.Stloc, newElemLocal);

        // if (newElement == null || newElement is $Undefined) Remove else Set
        var setLabel = il.DefineLabel();
        var deleteLabel = il.DefineLabel();
        var endIfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Brfalse, deleteLabel);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, deleteLabel);
        il.Emit(OpCodes.Br, setLabel);

        il.MarkLabel(setLabel);
        // dict[prop] = newElement
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));
        il.Emit(OpCodes.Br, endIfLabel);

        il.MarkLabel(deleteLabel);
        // dict.Remove(prop)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endIfLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, afterIterLabel);

        il.MarkLabel(notDictLabel);
    }

    /// <summary>
    /// Emits the read side of ECMA-262 [[Get]] for an arbitrary holder shape:
    /// <list type="bullet">
    ///   <item>SharpTSProxy → TrapGet via reflection.</item>
    ///   <item>List&lt;object?&gt; with numeric key → indexed access (returns null if out of range).</item>
    ///   <item>Dictionary&lt;string, object?&gt; → TryGetValue.</item>
    ///   <item>Otherwise → null.</item>
    /// </list>
    /// Pushes the resulting object? on the stack.
    /// </summary>
    private void EmitHolderGet(ILGenerator il, EmittedRuntime runtime, Action ldHolder, Action ldKey)
    {
        // Cache holder/key in temporaries so the multiple branches don't
        // re-evaluate ldHolder/ldKey (which may have side effects in
        // pathological caller actions; cheap insurance for callers).
        var holderTmp = il.DeclareLocal(_types.Object);
        var keyTmp = il.DeclareLocal(_types.Object);
        ldHolder();
        il.Emit(OpCodes.Stloc, holderTmp);
        ldKey();
        il.Emit(OpCodes.Stloc, keyTmp);

        var resultLocal = il.DeclareLocal(_types.Object);
        var doneLabel = il.DefineLabel();

        // If holder is a Proxy → TrapGet(key.ToString())
        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, () => il.Emit(OpCodes.Ldloc, holderTmp), proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        // proxyType.GetMethod("TrapGet").Invoke(holder, [key.ToString(), null])
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "TrapGet");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        // Convert key to string (it's always a string in our walk, but be defensive)
        il.Emit(OpCodes.Ldloc, keyTmp);
        var keyNullLabel = il.DefineLabel();
        var keyStrEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, keyNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, keyStrEndLabel);
        il.MarkLabel(keyNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(keyStrEndLabel);
        il.Emit(OpCodes.Stelem_Ref);
        // [1] = null (Interpreter) — already null from Newarr
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notProxyLabel);

        // If holder is a List<object?> and key parses as a non-negative int < Count → list[i]
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        var listHolderLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listHolderLocal);

        // Try int.TryParse(key.ToString(), out idx)
        var idxLocal = il.DeclareLocal(_types.Int32);
        var listFailLabel = il.DefineLabel();
        var listOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyTmp);
        var keyTmpNullLabel = il.DefineLabel();
        var keyTmpStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, keyTmpNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, keyTmpStrLabel);
        il.MarkLabel(keyTmpNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(keyTmpStrLabel);
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, listFailLabel);

        // Bounds check: 0 <= idx < list.Count
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, listFailLabel);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, listHolderLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listFailLabel);

        // list[idx]
        il.Emit(OpCodes.Ldloc, listHolderLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(listFailLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notListLabel);

        // If holder is a Dictionary<string, object?> → TryGetValue
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // dict.TryGetValue(key.ToString(), out result)
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, keyTmp);
        var dKeyNullLabel = il.DefineLabel();
        var dKeyStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, dKeyNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, dKeyStrLabel);
        il.MarkLabel(dKeyNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(dKeyStrLabel);
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Pop);  // discard bool — resultLocal is already null when missing
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notDictLabel);

        // Fallthrough: result = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }
}
