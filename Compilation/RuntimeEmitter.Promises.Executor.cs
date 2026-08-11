using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Promise executor constructor support: new Promise((resolve, reject) => { ... })
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $PromiseResolveCallback and $PromiseRejectCallback types.
    /// Must be called before EmitInvokeValue so the callback types are available for dispatch.
    /// </summary>
    private void EmitPromiseCallbackTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitPromiseResolveCallbackType(moduleBuilder, runtime);
        EmitPromiseRejectCallbackType(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits the PromiseFromExecutor method that creates promises from executor functions.
    /// Must be called after EmitInvokeValue since it depends on runtime.InvokeValue.
    /// </summary>
    private void EmitPromiseExecutorSupport(TypeBuilder runtimeType, EmittedRuntime runtime, ModuleBuilder moduleBuilder)
    {
        // Emit the PromiseFromExecutor method
        EmitPromiseFromExecutorMethod(runtimeType, runtime, runtime.PromiseResolveCallbackType, runtime.PromiseRejectCallbackType);

        // Promise-subclass support (#242): receiver unwrapping + derived-result wrapping
        EmitUnwrapPromiseReceiverMethod(runtimeType, runtime);

        // Pre-declare the general NewPromiseCapability helper (#349) so
        // WrapDerivedPromiseResult can call it; the body and the $PromiseCapability
        // type are emitted later (EmitPromiseCapabilitySupport, after
        // ConstructDynamicValue) when all of its dependencies are available. The
        // species is typed `object` (not `Type`): a class species arrives as a Type
        // token, but a function-valued species or a non-constructor arrives as its
        // raw value, and ConstructDynamicValue dispatches all three (Type →
        // Activator, function → NewOnFunction, non-constructor → TypeError, #390).
        runtime.NewPromiseCapabilityResultMethod = runtimeType.DefineMethod(
            "NewPromiseCapabilityResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.TaskOfObject]);

        EmitWrapDerivedPromiseResultMethod(runtimeType, runtime);
    }

    /// <summary>
    /// Emits NormalizePromiseList(object iterable) -> object: when the arg is
    /// a List&lt;object?&gt;, returns a copy normalized through the base Promise
    /// constructor's current <c>resolve</c> method. The built-in path unwraps
    /// $Promise elements (including #242 subclasses) to their backing Task;
    /// when user code has replaced <c>Promise.resolve</c>, that callable is
    /// invoked once for each value with Promise as its receiver, as required by
    /// PerformPromiseAll/Race/AllSettled/Any. Non-list args pass through.
    /// </summary>
    internal void EmitNormalizePromiseList(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "NormalizePromiseList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NormalizePromiseListMethod = method;

        var il = method.GetILGenerator();
        var listType = _types.ListOfObject;
        var passThroughLabel = il.DefineLabel();

        var listLocal = il.DeclareLocal(listType);
        var resultLocal = il.DeclareLocal(listType);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var elementLocal = il.DeclareLocal(_types.Object);
        var resolvedElementLocal = il.DeclareLocal(_types.Object);
        var resolveDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var resolveFunctionLocal = il.DeclareLocal(runtime.TSFunctionType);

        // if (iterable is not List<object?>) return iterable;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brfalse, passThroughLabel);

        // Capture Promise.resolve once before iteration. Direct assignments to
        // a compiled built-in constructor are represented by an own descriptor
        // in PDS. With no override, keep the existing built-in fast path below.
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldstr, "resolve");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, resolveDescriptorLocal);
        var noResolveOverrideLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resolveDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noResolveOverrideLabel);
        il.Emit(OpCodes.Ldloc, resolveDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, resolveFunctionLocal);
        il.MarkLabel(noResolveOverrideLabel);

        // var result = new List<object?>(); for each element: $Promise → .Task
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var addRawLabel = il.DefineLabel();
        var nextLabel = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // A user-installed Promise.resolve is observable and must be called
        // for every iterated value, with the constructor as `this`.
        var useBuiltInResolveLabel = il.DefineLabel();
        var haveResolvedElementLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resolveFunctionLocal);
        il.Emit(OpCodes.Brfalse, useBuiltInResolveLabel);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldloc, resolveFunctionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.Emit(OpCodes.Br, haveResolvedElementLabel);

        il.MarkLabel(useBuiltInResolveLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.MarkLabel(haveResolvedElementLabel);

        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, addRawLabel);

        // result.Add(((​$Promise)element).Task)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));
        il.Emit(OpCodes.Br, nextLabel);

        // result.Add(resolvedElement)
        il.MarkLabel(addRawLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));

        il.MarkLabel(nextLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(passThroughLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits UnwrapPromiseReceiver(object receiver) -> Task&lt;object?&gt;:
    /// $Promise instances (including #242 Promise subclasses) yield their
    /// wrapped task; anything else is cast to Task&lt;object?&gt; (matching the
    /// previous inline Castclass that broke for $Promise receivers).
    /// </summary>
    private void EmitUnwrapPromiseReceiverMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "UnwrapPromiseReceiver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.UnwrapPromiseReceiverMethod = method;

        var il = method.GetILGenerator();
        var notPromiseObjLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseObjLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPromiseObjLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits WrapDerivedPromiseResult(Task&lt;object?&gt; result, object receiver) -> object:
    /// the then/catch/finally result construction through
    /// SpeciesConstructor(receiver, %Promise%) (ECMA-262 §27.2.5.4, §7.3.22).
    /// When the receiver is a $Promise SUBCLASS instance (#242), reads
    /// <c>receiver.constructor[Symbol.species]</c> — the subclass's static
    /// <c>@@species</c> getter, or, absent that, the expando assigned via
    /// <c>(C as any)[Symbol.species] = …</c> (#262/#349), defaulting to the
    /// subclass itself when neither is present — and constructs the result
    /// through it: <c>%Promise%</c> (or a
    /// <c>@@species</c> yielding <c>Promise</c>/<c>undefined</c>/<c>null</c>)
    /// returns the raw task; a guest Promise SUBCLASS is built by invoking its
    /// single-object (executor) constructor reflectively (PromiseFromExecutor
    /// adopts a raw task, so the new instance wraps <c>result</c>); a general
    /// non-Promise species is built through NewPromiseCapabilityResult (#349, see
    /// below).
    /// </summary>
    /// <remarks>
    /// Generic Promise subclasses (#351): the static <c>@@species</c> accessor is
    /// registered under the open generic definition (MyP`1) while the receiver's
    /// runtime type is closed (MyP&lt;object&gt;); FindSymbolGetterFor reconciles
    /// the two (SymbolRegistryKey/CloseSymbolAccessor) and a species naming a
    /// generic subclass is closed via SymbolClosedOwner before construction.
    /// A species that is NOT a $Promise subclass (a general guest constructor)
    /// is routed to <see cref="EmittedRuntime.NewPromiseCapabilityResultMethod"/>
    /// (#349): the (object)→PromiseFromExecutor task-adoption path below only
    /// works for $Promise subclasses, so a general class is constructed with a
    /// real capturing executor and the result task adopted into its capability.
    /// </remarks>
    private void EmitWrapDerivedPromiseResultMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "WrapDerivedPromiseResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.TaskOfObject, _types.Object]
        );
        runtime.WrapDerivedPromiseResultMethod = method;

        var il = method.GetILGenerator();
        var returnResultLabel = il.DefineLabel();
        var haveSpeciesLabel = il.DefineLabel();
        var expandoLookupLabel = il.DefineLabel();
        // #390: a resolved @@species value that is NOT a Type (a function-valued
        // species, or a non-constructor like a number) but is also not
        // undefined/null is routed here with its raw value preserved.
        var generalFromValueLabel = il.DefineLabel();
        var typeLocal = il.DeclareLocal(_types.Type);          // receiver's runtime type (= C)
        var speciesTypeLocal = il.DeclareLocal(_types.Type);   // resolved SpeciesConstructor
        var speciesValLocal = il.DeclareLocal(_types.Object);  // raw @@species value (#390)
        var getterLocal = il.DeclareLocal(_types.Object);
        var ctorLocal = il.DeclareLocal(typeof(ConstructorInfo));
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);

        // #350: SpeciesConstructor(promise, %Promise%) step 1 = Get(promise,
        // "constructor"). An own `constructor` getter installed via
        // Object.defineProperty (a poisoned getter, test262 then/ctor-poisoned)
        // is stored in $PropertyDescriptorStore keyed on the receiver. Invoke it
        // for its side effect FIRST — this runs synchronously right after the
        // then/catch/finally state machine returns its task, so a throw
        // propagates synchronously out of the `.then()` expression (a
        // ReturnIfAbrupt before PerformPromiseThen) rather than rejecting the
        // result. Applies to plain promises (raw Task / base $Promise) too, so
        // it precedes the $Promise-subclass narrowing below. The getter's RETURN
        // value does not redirect species (the receiver's own class still drives
        // the result) — own-constructor-returns-a-value is the #349/#350 remainder.
        var poisonGetterLocal = il.DeclareLocal(_types.Object);
        var noPoisonGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noPoisonGetterLabel);   // null receiver → skip
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldloca, poisonGetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
        il.Emit(OpCodes.Brfalse, noPoisonGetterLabel);
        il.Emit(OpCodes.Ldarg_1);                        // receiver
        il.Emit(OpCodes.Ldloc, poisonGetterLocal);       // getter
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);          // empty args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);                            // discard; only the throw matters
        il.MarkLabel(noPoisonGetterLabel);

        // if (receiver is not $Promise) return result;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // var recvType = receiver.GetType();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);
        // if (recvType == typeof($Promise)) return result;
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldtoken, runtime.TSPromiseType);
        il.Emit(OpCodes.Call, getTypeFromHandle);
        il.Emit(OpCodes.Beq, returnResultLabel);

        // SpeciesConstructor(receiver, %Promise%): C = recvType; species default = C.
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Stloc, speciesTypeLocal); // speciesType = recvType (default)
        // var getter = FindSymbolGetter(recvType, Symbol.species);  // static-slot lookup
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolSpecies);
        il.Emit(OpCodes.Call, runtime.FindSymbolGetter);
        il.Emit(OpCodes.Stloc, getterLocal);
        // if (getter == null) consult the dynamically-assigned static @@species
        // expando before defaulting (#349).
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Brfalse, expandoLookupLabel);

        // var speciesVal = ((MethodBase)getter).Invoke(recvType, Array.Empty<object>());
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Castclass, _types.MethodBase);
        il.Emit(OpCodes.Ldloc, typeLocal);  // receiver arg (ignored for a static getter)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        // speciesVal = the raw return; speciesType = speciesVal as Type;
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, speciesValLocal);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, speciesTypeLocal);
        // A Type species (class / %Promise%) flows to the shared tail.
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Brtrue, haveSpeciesLabel);
        // Non-Type: undefined/null → %Promise% (§7.3.22 steps 6-7); any other
        // value (a function-valued species, or a non-constructor) → general path,
        // which constructs it or throws TypeError (§7.3.22 step 5, #390).
        EmitSpeciesValueRouting(il, runtime, speciesValLocal, returnResultLabel, generalFromValueLabel);

        // #349: no declared static @@species accessor — consult the expando
        // assigned via `(C as any)[Symbol.species] = …` (#262), which the
        // interpreter (ResolveSpeciesConstructor → TryGetStaticBySymbol) reads but
        // compiled mode previously ignored. The expando is stored in the per-Type
        // symbol dict (GetSymbolDict), so walk the receiver's runtime type and its
        // base-type chain (inherited expando statics are visible on subclasses,
        // #265), keying each level through SymbolRegistryKey so a generic
        // subclass's closed runtime type (MyP&lt;object&gt;) reaches the expando
        // stored under its open generic definition (MyP`1, #351). A found value
        // has the same representation as a getter's return
        // (a Type token; `Promise` → typeof(Task<object?>)), so it flows through
        // the shared tail below. None found → the default species (= recvType,
        // already in speciesType) — the inherited Promise[@@species] returns `this`.
        il.MarkLabel(expandoLookupLabel);
        var expandoOwnerLocal = il.DeclareLocal(_types.Type);
        var expandoDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var expandoValLocal = il.DeclareLocal(_types.Object);
        var expandoLoopLabel = il.DefineLabel();
        var haveExpandoLabel = il.DefineLabel();
        var getBaseType = _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!;
        var dictTryGetValue = _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue");

        // owner = recvType;
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Stloc, expandoOwnerLocal);
        il.MarkLabel(expandoLoopLabel);
        // if (owner == null) goto haveSpecies;  // none found → default species
        il.Emit(OpCodes.Ldloc, expandoOwnerLocal);
        il.Emit(OpCodes.Brfalse, haveSpeciesLabel);
        // if (GetSymbolDict(SymbolRegistryKey(owner)).TryGetValue(Symbol.species, out expandoVal)) goto haveExpando;
        il.Emit(OpCodes.Ldloc, expandoOwnerLocal);
        il.Emit(OpCodes.Call, runtime.SymbolRegistryKey);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, expandoDictLocal);
        il.Emit(OpCodes.Ldloc, expandoDictLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolSpecies);
        il.Emit(OpCodes.Ldloca, expandoValLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, haveExpandoLabel);
        // owner = owner.BaseType; continue;
        il.Emit(OpCodes.Ldloc, expandoOwnerLocal);
        il.Emit(OpCodes.Callvirt, getBaseType);
        il.Emit(OpCodes.Stloc, expandoOwnerLocal);
        il.Emit(OpCodes.Br, expandoLoopLabel);

        il.MarkLabel(haveExpandoLabel);
        // speciesVal = expandoVal; speciesType = expandoVal as Type;
        il.Emit(OpCodes.Ldloc, expandoValLocal);
        il.Emit(OpCodes.Stloc, speciesValLocal);
        il.Emit(OpCodes.Ldloc, expandoValLocal);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, speciesTypeLocal);
        // A Type species flows to the shared tail; a non-Type expando is routed
        // exactly like a non-Type getter return (#390).
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Brtrue, haveSpeciesLabel);
        EmitSpeciesValueRouting(il, runtime, speciesValLocal, returnResultLabel, generalFromValueLabel);

        il.MarkLabel(haveSpeciesLabel);
        // #351: a species naming a generic Promise subclass resolves to the OPEN
        // generic definition (MyP`1) — uninstantiable. Close it on `object` so the
        // GetConstructor/Invoke below target a constructable type. Closed and
        // non-generic species (incl. the default species = receiver's closed type)
        // pass through unchanged.
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Call, runtime.SymbolClosedOwner);
        il.Emit(OpCodes.Stloc, speciesTypeLocal);
        // if (speciesType == typeof(Task<object?>)) return result;  // %Promise%
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, getTypeFromHandle);
        il.Emit(OpCodes.Beq, returnResultLabel);

        // #349 general NewPromiseCapability: a species that is NOT a $Promise
        // subclass cannot be settled by the (object)→PromiseFromExecutor task-
        // adoption path below — its (object) executor ctor would receive the raw
        // task and throw "object is not a function". Route it through
        // NewPromiseCapabilityResult, which constructs new S(executor) with a
        // capturing capability and adopts the result task into it.
        // if (!typeof($Promise).IsAssignableFrom(speciesType))
        //     return NewPromiseCapabilityResult(speciesType, result);
        var promiseSubclassLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldtoken, runtime.TSPromiseType);
        il.Emit(OpCodes.Call, getTypeFromHandle);
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Brtrue, promiseSubclassLabel);
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewPromiseCapabilityResultMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(promiseSubclassLabel);

        // var ctor = speciesType.GetConstructor(new[] { typeof(object) });
        il.Emit(OpCodes.Ldloc, speciesTypeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Type);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, getTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetConstructor", [typeof(Type[])])!);
        il.Emit(OpCodes.Stloc, ctorLocal);

        // if (ctor == null) return result;
        il.Emit(OpCodes.Ldloc, ctorLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // return ctor.Invoke(new object[] { result });
        il.Emit(OpCodes.Ldloc, ctorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(ConstructorInfo).GetMethod("Invoke", [typeof(object[])])!);
        il.Emit(OpCodes.Ret);

        // #390 general path for a non-Type species value (a function, or a
        // non-constructor): NewPromiseCapabilityResult → ConstructDynamicValue,
        // which constructs new S(executor) for a callable or throws TypeError
        // (§7.3.22 step 5) for a non-constructor.
        il.MarkLabel(generalFromValueLabel);
        il.Emit(OpCodes.Ldloc, speciesValLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NewPromiseCapabilityResultMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the §7.3.22 steps 5-7 branch for a resolved <c>@@species</c> value
    /// already known not to be a <see cref="Type"/> (so it is neither a class nor
    /// <c>%Promise%</c>): <c>undefined</c>/<c>null</c> falls back to <c>%Promise%</c>
    /// (<paramref name="promiseFallbackLabel"/>); any other value is routed with
    /// its raw form preserved to the general NewPromiseCapability path
    /// (<paramref name="generalFromValueLabel"/>), which constructs a callable
    /// species or throws <c>TypeError</c> for a non-constructor (#390).
    /// </summary>
    private void EmitSpeciesValueRouting(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder speciesValLocal,
        Label promiseFallbackLabel, Label generalFromValueLabel)
    {
        il.Emit(OpCodes.Ldloc, speciesValLocal);
        il.Emit(OpCodes.Brfalse, promiseFallbackLabel);    // null → %Promise%
        il.Emit(OpCodes.Ldloc, speciesValLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, promiseFallbackLabel);     // undefined → %Promise%
        il.Emit(OpCodes.Br, generalFromValueLabel);
    }

    /// <summary>
    /// Emits the $PromiseResolveCallback type with:
    /// - TaskCompletionSource field
    /// - SettledFlag field (object for locking + bool tracking)
    /// - Constructor(TaskCompletionSource, object settledLock, ref bool settledFlag)
    /// - Invoke(object?[] args) method
    /// </summary>
    private TypeBuilder EmitPromiseResolveCallbackType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PromiseResolveCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);

        // Constructor: (TaskCompletionSource<object?> tcs, object lockObj)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TaskCompletionSource<object?>), _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            // Call base constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            // this._tcs = tcs
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            // this._lock = lockObj
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, lockField);
            // this._settled = false
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(object?[] args) method - compatible with TSFunction invocation
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        {
            var il = invokeMethod.GetILGenerator();
            var alreadySettledLabel = il.DefineLabel();
            var endLockLabel = il.DefineLabel();
            var notTaskLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            var valueLocal = il.DeclareLocal(_types.Object);
            var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
            var innerTaskLocal = il.DeclareLocal(_types.TaskOfObject);

            // value = args.Length > 0 ? args[0] : null
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, notTaskLabel);  // if args.Length <= 0, jump (using notTaskLabel temporarily)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, valueLocal);
            var afterValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Br, afterValueLabel);
            il.MarkLabel(notTaskLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.MarkLabel(afterValueLabel);

            // Load _tcs for later use
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Stloc, tcsLocal);

            // lock (_lock) { if (_settled) return; _settled = true; }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Enter", _types.Object));

            // try { if (_settled) goto alreadySettled; _settled = true; } finally { Monitor.Exit(_lock); }
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, settledField);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // Just call TrySetResult(value) - no flattening for now (simplification)
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            var trySetResult = typeof(TaskCompletionSource<object?>).GetMethod("TrySetResult")!;
            il.Emit(OpCodes.Callvirt, trySetResult);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.PromiseResolveCallbackType = typeBuilder;
        runtime.PromiseResolveCallbackCtor = ctor;
        runtime.PromiseResolveCallbackInvoke = invokeMethod;
        return typeBuilder;
    }

    /// <summary>
    /// Emits the $PromiseRejectCallback type with similar structure to resolve callback.
    /// </summary>
    private TypeBuilder EmitPromiseRejectCallbackType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PromiseRejectCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TaskCompletionSource<object?>), _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, lockField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke method
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        {
            var il = invokeMethod.GetILGenerator();
            var alreadySettledLabel = il.DefineLabel();
            var endLockLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            var reasonLocal = il.DeclareLocal(_types.Object);
            var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));

            // reason = args.Length > 0 ? args[0] : null
            var noReasonLabel = il.DefineLabel();
            var afterReasonLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, noReasonLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.Emit(OpCodes.Br, afterReasonLabel);
            il.MarkLabel(noReasonLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.MarkLabel(afterReasonLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Stloc, tcsLocal);

            // lock (_lock) { if (_settled) return; _settled = true; }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Enter", _types.Object));

            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, settledField);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // tcs.TrySetException(new $PromiseRejectedException(reason)) —
            // the carrier whose Reason the then/catch state machines extract,
            // so `new Promise((res, rej) => rej(err)).catch(e => ...)` hands
            // the guest value through unchanged (mirrors the interpreter's
            // SharpTSPromiseRejectedException; #232 reason-preservation).
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, reasonLocal);
            il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
            var trySetException = typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!;
            il.Emit(OpCodes.Callvirt, trySetException);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.PromiseRejectCallbackType = typeBuilder;
        runtime.PromiseRejectCallbackCtor = ctor;
        runtime.PromiseRejectCallbackInvoke = invokeMethod;
        return typeBuilder;
    }

    /// <summary>
    /// Emits the PromiseFromExecutor(object executor) -> Task<object?> method.
    /// </summary>
    private void EmitPromiseFromExecutorMethod(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        TypeBuilder resolveCallbackType,
        TypeBuilder rejectCallbackType)
    {
        var method = runtimeType.DefineMethod(
            "PromiseFromExecutor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.PromiseFromExecutor = method;

        var il = method.GetILGenerator();

        // Local variables
        var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
        var lockLocal = il.DeclareLocal(_types.Object);
        var resolveLocal = il.DeclareLocal(resolveCallbackType);
        var rejectLocal = il.DeclareLocal(rejectCallbackType);
        var argsLocal = il.DeclareLocal(typeof(object[]));

        // Task adoption (#242): a raw Task<object?> in place of an executor is
        // adopted as the promise's task directly. Promise-subclass constructors
        // chain through here (super(executor) → PromiseFromExecutor → base
        // $Promise ctor), so passing a task to that same constructor is the
        // derived-promise construction path used by inherited statics
        // (MyPromise.resolve) and subclass-typed then/catch/finally results.
        var notTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTaskLabel);

        // TaskCompletionSource<object?> tcs = new TaskCompletionSource<object?>();
        var tcsCtor = typeof(TaskCompletionSource<object?>).GetConstructor([])!;
        il.Emit(OpCodes.Newobj, tcsCtor);
        il.Emit(OpCodes.Stloc, tcsLocal);

        // object lockObj = new object();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, lockLocal);

        // var resolveCallback = new $PromiseResolveCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseResolveCallbackCtor);
        il.Emit(OpCodes.Stloc, resolveLocal);

        // var rejectCallback = new $PromiseRejectCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseRejectCallbackCtor);
        il.Emit(OpCodes.Stloc, rejectLocal);

        // Create args array [resolveCallback, rejectCallback]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resolveLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // try { InvokeValue(executor, args); }
        // catch (Exception ex) { tcs.TrySetException(ex); }
        var exLocal = il.DeclareLocal(_types.Exception);
        var endTryLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // Call the executor: InvokeValue(executor, args)
        // This invokes the executor function with (resolve, reject) arguments
        il.Emit(OpCodes.Ldarg_0);  // executor
        il.Emit(OpCodes.Ldloc, argsLocal);  // args
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);  // Discard executor return value

        il.Emit(OpCodes.Leave, endTryLabel);

        // catch (Exception ex)
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);

        // tcs.TrySetException(ex)
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        var trySetException = typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!;
        il.Emit(OpCodes.Callvirt, trySetException);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Leave, endTryLabel);

        il.EndExceptionBlock();
        il.MarkLabel(endTryLabel);

        // return tcs.Task;
        il.Emit(OpCodes.Ldloc, tcsLocal);
        var taskProperty = typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, taskProperty);
        il.Emit(OpCodes.Ret);
    }
}
