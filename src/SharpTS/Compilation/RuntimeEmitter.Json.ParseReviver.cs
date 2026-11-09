using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct JsonParseWithReviverInputs(
        MethodBuilder DeleteProperty,
        EmittedDescriptorStorageRuntime DescriptorStorage,
        MethodBuilder InvokeMethodUnwrapped,
        MethodBuilder GetKeys,
        MethodBuilder GetProperty,
        Type HasFieldsInterface,
        Type ThrownValueType,
        EmittedNumericCoercionRuntime NumericCoercion,
        MethodBuilder ObjectDefineProperty,
        MethodBuilder SetArrayElement,
        MethodBuilder TSFunctionInvokeWithThis,
        TypeBuilder TSFunctionType,
        Type UndefinedType,
        MethodInfo? NativeProxyRejection
    );

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
    private void EmitJsonParseWithReviver(TypeBuilder typeBuilder, EmittedJsonImplementation json, JsonParseWithReviverInputs inputs)
    {
        var applyReviver = EmitApplyReviverHelper(typeBuilder, inputs);

        var method = typeBuilder.DefineMethod(
            "JsonParseWithReviver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        json.ParseWithReviver = method;

        var il = method.GetILGenerator();
        var noReviverLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (reviver == null) goto noReviverLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noReviverLabel);

        // parsed = JsonParse(unassociatedCopy(text)). A reviver must walk the
        // ordinary Dictionary/List graph. Breaking string identity here keeps
        // the closed-shape fast path exclusive to the no-reviver overload.
        var parsedLocal = il.DeclareLocal(_types.Object);
        var parseNormallyLabel = il.DefineLabel();
        var parsedReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, parseNormallyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "ToCharArray", Type.EmptyTypes));
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.String, _types.MakeArrayType(_types.Char)));
        il.Emit(OpCodes.Call, json.Parse);
        il.Emit(OpCodes.Stloc, parsedLocal);
        il.Emit(OpCodes.Br, parsedReadyLabel);

        il.MarkLabel(parseNormallyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, json.Parse);
        il.Emit(OpCodes.Stloc, parsedLocal);
        il.MarkLabel(parsedReadyLabel);

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
        il.Emit(OpCodes.Call, json.Parse);

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
    private MethodBuilder EmitApplyReviverHelper(TypeBuilder typeBuilder, JsonParseWithReviverInputs inputs)
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
        EmitHolderGet(
            il,
            inputs,
            ldHolder: () => il.Emit(OpCodes.Ldarg_0),
            ldKey: () => il.Emit(OpCodes.Ldarg_1)
        );
        il.Emit(OpCodes.Stloc, valLocal);

        var afterIterLabel = il.DefineLabel();

        // Dispatch on val's runtime type. Order matters: Proxy check is by
        // FullName (object identity), so it doesn't conflict with isinst on
        // List/Dictionary. We do Proxy first because a Proxy value-shaped
        // wrapper around a List/Dict would otherwise route to the iteration
        // path and lose trap dispatch.
        EmitReviverProxyBranch(
            il,
            inputs,
            method,
            valLocal,
            afterIterLabel
        );
        EmitReviverListBranch(
            il,
            inputs,
            method,
            valLocal,
            afterIterLabel
        );
        EmitReviverObjectBranch(il, inputs, method, valLocal, afterIterLabel);

        il.MarkLabel(afterIterLabel);

        // ECMA-262 step 3: Call reviver with `this` = holder, args = (key, val).
        // Function expressions emit a leading `__this` parameter; raw Invoke
        // would shift the args. InvokeWithThis takes (thisArg, args) and
        // handles the prepend correctly (see SharpTSProxy.InvokeTrap and
        // $TSFunction.InvokeWithThis for the parameter-name detection).
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, inputs.TSFunctionType);
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
        il.Emit(OpCodes.Callvirt, inputs.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits the Proxy branch of ApplyReviver: if val is a SharpTSProxy, snapshot
    /// TrapOwnKeys, recurse on each key with val as the new holder, then dispatch
    /// TrapSet (kept) or TrapDeleteProperty (newElement is null/undefined).
    /// Falls through to the next branch if val is not a proxy.
    /// </summary>
    private void EmitReviverProxyBranch(ILGenerator il, JsonParseWithReviverInputs inputs, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
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

        if (inputs.NativeProxyRejection is { } rejection)
        {
            il.Emit(OpCodes.Call, rejection);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, afterIterLabel);
            il.MarkLabel(notProxyLabel);
            return;
        }

        // proxyType = Type.GetType("SharpTS.Runtime.Types.SharpTSProxy, SharpTS")
        var proxyTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSProxy, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, proxyTypeLocal);

        // IsArray(proxy) recursively follows the proxy target. Array proxies
        // use the array branch of InternalizeJSONProperty: capture length and
        // visit only integer indices. Treating them as ordinary proxies would
        // enumerate the non-configurable "length" property and then attempt to
        // redefine it as configurable, producing a spurious TypeError.
        var ordinaryProxyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "HasArrayTarget");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, inputs.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, ordinaryProxyLabel);

        // len = ToLength(Get(proxy, "length")). This must go through [[Get]]:
        // a proxy get trap can observe or abruptly complete the length read.
        var proxyArrayLength = il.DeclareLocal(_types.Int32);
        EmitHolderGet(
            il,
            inputs,
            ldHolder: () => il.Emit(OpCodes.Ldloc, valLocal),
            ldKey: () => il.Emit(OpCodes.Ldstr, "length")
        );
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, inputs.NumericCoercion.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, proxyArrayLength);

        var arrayDeleteMethod = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapDeleteProperty");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, arrayDeleteMethod);

        var arrayIndex = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, arrayIndex);
        var arrayLoopStart = il.DefineLabel();
        var arrayLoopEnd = il.DefineLabel();
        il.MarkLabel(arrayLoopStart);
        il.Emit(OpCodes.Ldloc, arrayIndex);
        il.Emit(OpCodes.Ldloc, proxyArrayLength);
        il.Emit(OpCodes.Bge, arrayLoopEnd);

        var arrayProp = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, arrayIndex);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, arrayProp);

        var arrayElement = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, arrayProp);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, applyReviverMethod);
        il.Emit(OpCodes.Stloc, arrayElement);

        var arrayDeleteLabel = il.DefineLabel();
        var arrayElementDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, arrayElement);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, arrayDeleteLabel);

        var arrayDescriptor = EmitReviverDataDescriptor(il, inputs.DescriptorStorage, arrayElement);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, arrayProp);
        il.Emit(OpCodes.Ldloc, arrayDescriptor);
        il.Emit(OpCodes.Call, inputs.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, arrayElementDone);

        il.MarkLabel(arrayDeleteLabel);
        il.Emit(OpCodes.Ldloc, arrayDeleteMethod);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, arrayProp);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, inputs.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(arrayElementDone);
        il.Emit(OpCodes.Ldloc, arrayIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, arrayIndex);
        il.Emit(OpCodes.Br, arrayLoopStart);

        il.MarkLabel(arrayLoopEnd);
        il.Emit(OpCodes.Br, afterIterLabel);
        il.MarkLabel(ordinaryProxyLabel);

        // keys = (List<string>)proxyType.GetMethod("TrapOwnKeys").Invoke(val, [null])
        var keysLocal = il.DeclareLocal(_types.ListOfString);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapOwnKeys");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, inputs.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Castclass, _types.ListOfString);
        il.Emit(OpCodes.Stloc, keysLocal);

        // Cache TrapDeleteProperty MethodInfo across the loop. The write side
        // uses ObjectDefineProperty below because InternalizeJSONProperty calls
        // CreateDataProperty rather than ordinary [[Set]].
        var trapDelMi = il.DeclareLocal(_types.MethodInfo);
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

        // ECMA-262 step 2.b.iii.3 / 2.c.ii.2: only JavaScript undefined
        // selects Delete. CLR null is the JavaScript null primitive and must
        // be written back as an ordinary value.
        var deleteLabel = il.DefineLabel();
        var setLabel = il.DefineLabel();
        var endIfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, deleteLabel);
        il.Emit(OpCodes.Br, setLabel);

        il.MarkLabel(setLabel);
        // CreateDataProperty uses [[DefineOwnProperty]], not [[Set]]. Route
        // through ObjectDefineProperty so Proxy defineProperty traps observe
        // each revived child. All CreateDataProperty attributes are true.
        var createDescLocal = EmitReviverDataDescriptor(il, inputs.DescriptorStorage, newElemLocal);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldloc, createDescLocal);
        il.Emit(OpCodes.Call, inputs.ObjectDefineProperty);
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
        il.Emit(OpCodes.Call, inputs.InvokeMethodUnwrapped);
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
    /// with val as the new holder, then CreateDataProperty or Delete according
    /// to the reviver result. Falls through if val is not a List&lt;object?&gt;.
    /// </summary>
    private void EmitReviverListBranch(ILGenerator il, JsonParseWithReviverInputs inputs, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
    {
        var notListLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // The reviver can change the array length while traversal is in progress.
        var getCount = _types.GetPropertyGetter(_types.ListOfObject, "Count");
        var lengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, getCount);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // for (int i = 0; i < length; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
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

        var doSetLabel = il.DefineLabel();
        var afterSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, doSetLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Call, inputs.DeleteProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterSetLabel);

        il.MarkLabel(doSetLabel);
        // Direct storage is safe only for a default index on an extensible array.
        var defineLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, defineLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.IsExtensible);
        il.Emit(OpCodes.Brfalse, defineLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Call, inputs.SetArrayElement);
        il.Emit(OpCodes.Br, afterSetLabel);

        il.MarkLabel(defineLabel);
        EmitReviverCreateDataProperty(il, inputs, valLocal, propLocal, newElemLocal);
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

    private void EmitReviverCreateDataProperty(
        ILGenerator il, JsonParseWithReviverInputs inputs, LocalBuilder valLocal,
        LocalBuilder propLocal, LocalBuilder newElemLocal)
    {
        // CreateDataProperty ignores a refusal from an ordinary object.
        // The descriptor has only data fields, so this cannot invoke a setter.
        il.BeginExceptionBlock();
        var descriptor = EmitReviverDataDescriptor(il, inputs.DescriptorStorage, newElemLocal);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, inputs.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);
        il.BeginCatchBlock(inputs.ThrownValueType);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
    }

    private LocalBuilder EmitReviverDataDescriptor(
        ILGenerator il, EmittedDescriptorStorageRuntime descriptors, LocalBuilder value)
    {
        var descriptor = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descriptor);
        // CreateDataProperty has no observable descriptor conversion. Keep
        // omitted fields absent even if a reviver modified Object.prototype.
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, descriptors.SetPrototype);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, setItem);
        foreach (var attribute in new[] { "writable", "enumerable", "configurable" })
        {
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldstr, attribute);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Callvirt, setItem);
        }
        return descriptor;
    }

    /// <summary>Revives the enumerable own keys of parsed objects and replacement instances.</summary>
    private void EmitReviverObjectBranch(ILGenerator il, JsonParseWithReviverInputs inputs, MethodBuilder applyReviverMethod, LocalBuilder valLocal, Label afterIterLabel)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        var objectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brtrue, objectLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, inputs.HasFieldsInterface);
        il.Emit(OpCodes.Brfalse, afterIterLabel);
        il.MarkLabel(objectLabel);

        var keysLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Call, inputs.GetKeys);
        il.Emit(OpCodes.Stloc, keysLocal);

        // for (int i = 0; i < keys.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // prop = keys[i]
        var propLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, propLocal);

        // newElement = ApplyReviver(val, prop, reviver)
        var newElemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, applyReviverMethod);
        il.Emit(OpCodes.Stloc, newElemLocal);

        // Only undefined deletes. JavaScript null remains a data value.
        var deleteLabel = il.DefineLabel();
        var endIfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, deleteLabel);

        // The parsed dictionary fast path is safe only for ordinary data on
        // an extensible receiver. An installed accessor or integrity flag
        // requires CreateDataProperty to consult the actual descriptor.
        var defineLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, defineLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, defineLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.IsExtensible);
        il.Emit(OpCodes.Brfalse, defineLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Ldloc, newElemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));
        il.Emit(OpCodes.Br, endIfLabel);

        il.MarkLabel(defineLabel);
        EmitReviverCreateDataProperty(il, inputs, valLocal, propLocal, newElemLocal);
        il.Emit(OpCodes.Br, endIfLabel);

        il.MarkLabel(deleteLabel);
        // Ordinary [[Delete]] respects non-configurable descriptors and
        // returns false without throwing in this non-strict algorithm.
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldloc, propLocal);
        il.Emit(OpCodes.Call, inputs.DeleteProperty);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endIfLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits the read side of ECMA-262 [[Get]] for an arbitrary holder shape:
    /// <list type="bullet">
    ///   <item>SharpTSProxy → TrapGet via reflection.</item>
    ///   <item>List&lt;object?&gt; with numeric key → ordinary array index lookup.</item>
    ///   <item>Parsed dictionary or replacement class instance → ordinary property lookup.</item>
    /// </list>
    /// Pushes the resulting object? on the stack.
    /// </summary>
    private void EmitHolderGet(ILGenerator il, JsonParseWithReviverInputs inputs, Action ldHolder, Action ldKey)
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
        if (inputs.NativeProxyRejection is { } rejection)
        {
            il.Emit(OpCodes.Call, rejection);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, doneLabel);
        }
        else
        {
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
            il.Emit(OpCodes.Call, inputs.InvokeMethodUnwrapped);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, doneLabel);
        }

        il.MarkLabel(notProxyLabel);

        var ordinaryLabel = il.DefineLabel();
        var otherLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, ordinaryLabel);
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, ordinaryLabel);
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Isinst, inputs.HasFieldsInterface);
        il.Emit(OpCodes.Brfalse, otherLabel);
        il.MarkLabel(ordinaryLabel);
        il.Emit(OpCodes.Ldloc, holderTmp);
        il.Emit(OpCodes.Ldloc, keyTmp);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(otherLabel);

        // Fallthrough: result = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }
}
