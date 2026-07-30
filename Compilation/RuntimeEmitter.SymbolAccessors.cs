using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Slot layout inside each per-(Type,symbol) object[6].
    private const int SymGetterInstanceSlot = 0;
    private const int SymSetterInstanceSlot = 1;
    private const int SymGetterStaticSlot = 2;
    private const int SymSetterStaticSlot = 3;
    private const int SymMethodInstanceSlot = 4;   // #647 computed instance method
    private const int SymMethodStaticSlot = 5;     // #647 computed static method
    private const int SymSlotCount = 6;

    private Type SymInnerDictType => _types.MakeGenericType(_types.DictionaryOpen, _types.Object, _types.ObjectArray);
    private Type SymOuterDictType => _types.MakeGenericType(_types.DictionaryOpen, _types.Type, SymInnerDictType);

    /// <summary>
    /// Forward-declares the symbol-accessor registry field and its three helper
    /// methods (#266). GetIndex/SetIndex — emitted before the bodies — call the
    /// FindSymbol* methods, and class .cctors (emitted after all runtime methods)
    /// call RegisterSymbolAccessor, so the signatures must exist up front.
    /// </summary>
    private void DefineSymbolAccessorRegistry(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.SymbolAccessorRegistryField = typeBuilder.DefineField(
            "_symbolAccessors", SymOuterDictType,
            FieldAttributes.Public | FieldAttributes.Static);

        runtime.RegisterSymbolAccessor = typeBuilder.DefineMethod(
            "RegisterSymbolAccessor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type, _types.Object, _types.Object, _types.Object, _types.Boolean]);

        runtime.FindSymbolGetter = typeBuilder.DefineMethod(
            "FindSymbolGetterFor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        runtime.FindSymbolSetter = typeBuilder.DefineMethod(
            "FindSymbolSetterFor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        // #647 computed symbol-keyed methods.
        runtime.RegisterSymbolMethod = typeBuilder.DefineMethod(
            "RegisterSymbolMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type, _types.Object, _types.Object, _types.Boolean]);

        runtime.FindSymbolMethod = typeBuilder.DefineMethod(
            "FindSymbolMethodFor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        // #351 generic-class helpers (forward-declared; bodies filled alongside
        // the Find* bodies). FindSymbol* calls them in its base-chain walk.
        runtime.SymbolRegistryKey = typeBuilder.DefineMethod(
            "SymbolRegistryKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Type,
            [_types.Type]);

        runtime.SymbolClosedOwner = typeBuilder.DefineMethod(
            "SymbolClosedOwner",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Type,
            [_types.Type]);

        runtime.CloseSymbolAccessor = typeBuilder.DefineMethod(
            "CloseSymbolAccessor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Type]);
    }

    /// <summary>Emits the registry field initialization into the $Runtime cctor.</summary>
    private void InitSymbolAccessorRegistry(ILGenerator cctorIL, EmittedRuntime runtime)
    {
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(SymOuterDictType));
        cctorIL.Emit(OpCodes.Stsfld, runtime.SymbolAccessorRegistryField);
    }

    /// <summary>Fills the bodies of Register/FindGetter/FindSetter (#266).</summary>
    private void EmitSymbolAccessorRegistryBodies(EmittedRuntime runtime)
    {
        var outer = SymOuterDictType;
        var inner = SymInnerDictType;
        var field = runtime.SymbolAccessorRegistryField;

        var outerTryGetValue = _types.GetMethod(outer, "TryGetValue", [_types.Type, inner.MakeByRefType()])!;
        var outerSetItem = _types.GetMethod(outer, "set_Item", [_types.Type, inner])!;
        var innerTryGetValue = _types.GetMethod(inner, "TryGetValue", [_types.Object, _types.ObjectArray.MakeByRefType()])!;
        var innerSetItem = _types.GetMethod(inner, "set_Item", [_types.Object, _types.ObjectArray])!;
        var getBaseType = _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!;
        var getType = _types.GetMethod(_types.Object, "GetType");

        EmitRegisterSymbolAccessorBody(runtime, inner, outerTryGetValue, outerSetItem, innerTryGetValue, innerSetItem);
        EmitRegisterSymbolMethodBody(runtime, inner, outerTryGetValue, outerSetItem, innerTryGetValue, innerSetItem);
        EmitSymbolGenericHelperBodies(runtime);
        EmitFindSymbolAccessorBody(runtime, runtime.FindSymbolGetter, field, inner,
            SymGetterInstanceSlot, SymGetterStaticSlot, outerTryGetValue, innerTryGetValue, getBaseType, getType);
        EmitFindSymbolAccessorBody(runtime, runtime.FindSymbolSetter, field, inner,
            SymSetterInstanceSlot, SymSetterStaticSlot, outerTryGetValue, innerTryGetValue, getBaseType, getType);
        // #647: methods reuse the same base-chain walk, reading the method slots.
        EmitFindSymbolAccessorBody(runtime, runtime.FindSymbolMethod, field, inner,
            SymMethodInstanceSlot, SymMethodStaticSlot, outerTryGetValue, innerTryGetValue, getBaseType, getType);
    }

    /// <summary>
    /// Emits <c>RegisterSymbolMethod(Type owner, object symbol, object method, bool isStatic)</c> (#647):
    /// stores <paramref name="method"/> into the per-(owner,symbol) slot array's method slot (instance
    /// slot 4 / static slot 5). Mirrors <see cref="EmitRegisterSymbolAccessorBody"/>'s get-or-create of
    /// the inner dictionary and slot array (sized <see cref="SymSlotCount"/>).
    /// </summary>
    private void EmitRegisterSymbolMethodBody(
        EmittedRuntime runtime, Type inner,
        MethodInfo outerTryGetValue, MethodInfo outerSetItem,
        MethodInfo innerTryGetValue, MethodInfo innerSetItem)
    {
        var il = runtime.RegisterSymbolMethod.GetILGenerator();
        var field = runtime.SymbolAccessorRegistryField;
        var innerLocal = il.DeclareLocal(inner);
        var slotLocal = il.DeclareLocal(_types.ObjectArray);

        // if (symbol == null) return;  (see RegisterSymbolAccessor — module-local Symbol keys, #282)
        var symbolOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, symbolOkLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolOkLabel);

        // #791: a non-symbol computed method key (string/number, e.g. `["dyn"]()` / `[1]()`) is
        // registered here too. Normalize it to its property-key string so it matches the form the
        // lookup side produces (a named access uses the literal string; an index access uses
        // ToJsString(key)); a numeric key would otherwise store as a boxed double and never match.
        // Symbols pass through unchanged so symbol-keyed methods/accessors still share their slot.
        var keyNormalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, keyNormalizedLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(keyNormalizedLabel);

        // if (!_symbolAccessors.TryGetValue(owner, out inner)) { inner = new(); _symbolAccessors[owner] = inner; }
        var haveInner = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, innerLocal);
        il.Emit(OpCodes.Callvirt, outerTryGetValue);
        il.Emit(OpCodes.Brtrue, haveInner);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(inner));
        il.Emit(OpCodes.Stloc, innerLocal);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Callvirt, outerSetItem);
        il.MarkLabel(haveInner);

        // if (!inner.TryGetValue(symbol, out slot)) { slot = new object[6]; inner[symbol] = slot; }
        var haveSlot = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, slotLocal);
        il.Emit(OpCodes.Callvirt, innerTryGetValue);
        il.Emit(OpCodes.Brtrue, haveSlot);
        il.Emit(OpCodes.Ldc_I4, SymSlotCount);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, slotLocal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, slotLocal);
        il.Emit(OpCodes.Callvirt, innerSetItem);
        il.MarkLabel(haveSlot);

        // if (method != null) slot[isStatic ? 5 : 4] = method;  (isStatic is arg 3 here)
        EmitStoreSlotIfPresent(il, slotLocal, argIndex: 2, staticSlot: SymMethodStaticSlot, instanceSlot: SymMethodInstanceSlot, isStaticArgIndex: 3);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the #351 generic-class helper bodies: SymbolRegistryKey,
    /// SymbolClosedOwner, and CloseSymbolAccessor. The registry is keyed by the
    /// open generic type definition (a class .cctor registers via
    /// <c>typeof(ThisClass)</c>, which for a generic class is the open
    /// definition), while receiver runtime types are closed (e.g. MyP&lt;object&gt;)
    /// and a static class reference is the open definition. These reconcile the
    /// two so the base-chain walk both finds the slot and produces an invokable
    /// (closed) accessor MethodInfo.
    /// </summary>
    private void EmitSymbolGenericHelperBodies(EmittedRuntime runtime)
    {
        var isConstructedGeneric = _types.GetProperty(_types.Type, "IsConstructedGenericType")!.GetGetMethod()!;
        var isGenericTypeDef = _types.GetProperty(_types.Type, "IsGenericTypeDefinition")!.GetGetMethod()!;
        var getGenericTypeDef = _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition");
        var getGenericArgs = _types.GetMethodNoParams(_types.Type, "GetGenericArguments");
        var makeGenericType = _types.GetMethod(_types.Type, "MakeGenericType", _types.MakeArrayType(_types.Type));
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);
        var getTypeHandle = _types.GetProperty(_types.Type, "TypeHandle").GetGetMethod()!;
        var getMethodHandle = _types.GetProperty(_types.MethodBase, "MethodHandle").GetGetMethod()!;
        var getDeclaringType = _types.GetProperty(typeof(System.Reflection.MemberInfo), "DeclaringType").GetGetMethod()!;
        var containsGenericParams = _types.GetProperty(_types.Type, "ContainsGenericParameters")!.GetGetMethod()!;
        var getMethodFromHandle = _types.MethodBaseGetMethodFromHandleWithType;

        // ---- SymbolRegistryKey(Type owner) ----
        // return owner.IsConstructedGenericType ? owner.GetGenericTypeDefinition() : owner;
        {
            var il = runtime.SymbolRegistryKey.GetILGenerator();
            var notConstructed = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, isConstructedGeneric);
            il.Emit(OpCodes.Brfalse, notConstructed);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getGenericTypeDef);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notConstructed);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }

        // ---- SymbolClosedOwner(Type owner) ----
        // An open generic definition (a bare generic class reference, e.g. MyP`1)
        // has no instantiation; close it on `object` for each type parameter so it
        // can drive cctor execution and reflective Invoke. Closed/non-generic
        // owners pass through.
        {
            var il = runtime.SymbolClosedOwner.GetILGenerator();
            var notOpenDef = il.DefineLabel();
            var argsLocal = il.DeclareLocal(_types.MakeArrayType(_types.Type));
            var iLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, isGenericTypeDef);
            il.Emit(OpCodes.Brfalse, notOpenDef);

            // var args = owner.GetGenericArguments();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getGenericArgs);
            il.Emit(OpCodes.Stloc, argsLocal);

            // for (i = 0; i < args.Length; i++) args[i] = typeof(object);
            var loopTop = il.DefineLabel();
            var loopCheck = il.DefineLabel();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loopTop);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Blt, loopTop);

            // return owner.MakeGenericType(args);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, makeGenericType);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notOpenDef);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }

        // ---- CloseSymbolAccessor(object mi, Type closedOwner) ----
        // var m = mi as MethodInfo;
        // if (m != null && m.DeclaringType != null && m.DeclaringType.ContainsGenericParameters)
        //     return MethodBase.GetMethodFromHandle(m.MethodHandle, closedOwner.TypeHandle);
        // return mi;
        {
            var il = runtime.CloseSymbolAccessor.GetILGenerator();
            var passThrough = il.DefineLabel();
            var mLocal = il.DeclareLocal(_types.MethodInfo);
            var dtLocal = il.DeclareLocal(_types.Type);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.MethodInfo);
            il.Emit(OpCodes.Stloc, mLocal);
            il.Emit(OpCodes.Ldloc, mLocal);
            il.Emit(OpCodes.Brfalse, passThrough);

            // dt = m.DeclaringType;
            il.Emit(OpCodes.Ldloc, mLocal);
            il.Emit(OpCodes.Callvirt, getDeclaringType);
            il.Emit(OpCodes.Stloc, dtLocal);
            il.Emit(OpCodes.Ldloc, dtLocal);
            il.Emit(OpCodes.Brfalse, passThrough);

            // if (!dt.ContainsGenericParameters) pass through
            il.Emit(OpCodes.Ldloc, dtLocal);
            il.Emit(OpCodes.Callvirt, containsGenericParams);
            il.Emit(OpCodes.Brfalse, passThrough);

            // return GetMethodFromHandle(m.MethodHandle, closedOwner.TypeHandle);
            il.Emit(OpCodes.Ldloc, mLocal);
            il.Emit(OpCodes.Callvirt, getMethodHandle);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getTypeHandle);
            il.Emit(OpCodes.Call, getMethodFromHandle);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(passThrough);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitRegisterSymbolAccessorBody(
        EmittedRuntime runtime, Type inner,
        MethodInfo outerTryGetValue, MethodInfo outerSetItem,
        MethodInfo innerTryGetValue, MethodInfo innerSetItem)
    {
        // RegisterSymbolAccessor(Type owner, object symbol, object getter, object setter, bool isStatic)
        var il = runtime.RegisterSymbolAccessor.GetILGenerator();
        var field = runtime.SymbolAccessorRegistryField;
        var innerLocal = il.DeclareLocal(inner);
        var slotLocal = il.DeclareLocal(_types.ObjectArray);

        // if (symbol == null) return; — a non-well-known key that couldn't be
        // resolved at .cctor time (e.g. a module-local Symbol whose binding isn't
        // yet visible) must not crash the type initializer. Well-known-symbol keys
        // (the common case: species/toStringTag/toPrimitive/iterator) are global
        // constants and always resolve. Module-local Symbol keys tracked by #282.
        var symbolOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, symbolOkLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolOkLabel);

        // if (!_symbolAccessors.TryGetValue(owner, out inner)) { inner = new(); _symbolAccessors[owner] = inner; }
        var haveInner = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, innerLocal);
        il.Emit(OpCodes.Callvirt, outerTryGetValue);
        il.Emit(OpCodes.Brtrue, haveInner);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(inner));
        il.Emit(OpCodes.Stloc, innerLocal);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Callvirt, outerSetItem);
        il.MarkLabel(haveInner);

        // if (!inner.TryGetValue(symbol, out slot)) { slot = new object[6]; inner[symbol] = slot; }
        var haveSlot = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, slotLocal);
        il.Emit(OpCodes.Callvirt, innerTryGetValue);
        il.Emit(OpCodes.Brtrue, haveSlot);
        il.Emit(OpCodes.Ldc_I4, SymSlotCount);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, slotLocal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, slotLocal);
        il.Emit(OpCodes.Callvirt, innerSetItem);
        il.MarkLabel(haveSlot);

        // if (getter != null) slot[isStatic ? 2 : 0] = getter;  (isStatic is arg 4 here)
        EmitStoreSlotIfPresent(il, slotLocal, argIndex: 2, staticSlot: SymGetterStaticSlot, instanceSlot: SymGetterInstanceSlot, isStaticArgIndex: 4);
        // if (setter != null) slot[isStatic ? 3 : 1] = setter;
        EmitStoreSlotIfPresent(il, slotLocal, argIndex: 3, staticSlot: SymSetterStaticSlot, instanceSlot: SymSetterInstanceSlot, isStaticArgIndex: 4);

        il.Emit(OpCodes.Ret);
    }

    // if (argN != null) slot[arg(isStaticArgIndex) ? staticSlot : instanceSlot] = argN;
    private static void EmitStoreSlotIfPresent(ILGenerator il, LocalBuilder slotLocal, int argIndex, int staticSlot, int instanceSlot, int isStaticArgIndex)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldloc, slotLocal);
        // index = isStatic ? staticSlot : instanceSlot
        var useStatic = il.DefineLabel();
        var haveIdx = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, isStaticArgIndex);
        il.Emit(OpCodes.Brtrue, useStatic);
        il.Emit(OpCodes.Ldc_I4, instanceSlot);
        il.Emit(OpCodes.Br, haveIdx);
        il.MarkLabel(useStatic);
        il.Emit(OpCodes.Ldc_I4, staticSlot);
        il.MarkLabel(haveIdx);
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Stelem_Ref);
        il.MarkLabel(skip);
    }

    // object FindSymbol{Getter,Setter}For(object obj, object symbol):
    //   owner = obj is Type t ? t : obj.GetType();  idx = obj is Type ? staticSlot : instanceSlot;
    //   for (; owner != null; owner = owner.BaseType) {
    //     closed = SymbolClosedOwner(owner);           // #351: a closed type to drive cctor + Invoke
    //     key = SymbolRegistryKey(owner);              // #351: registry is keyed by the open generic def
    //     if (reg.TryGetValue(key, out inner) && inner.TryGetValue(symbol, out slot) && slot[idx] != null)
    //       return CloseSymbolAccessor(slot[idx], closed);  // #351: close a generic-def accessor before Invoke
    //   }
    //   return null;
    private void EmitFindSymbolAccessorBody(
        EmittedRuntime runtime,
        MethodBuilder method, FieldBuilder field, Type inner,
        int instanceSlot, int staticSlot,
        MethodInfo outerTryGetValue, MethodInfo innerTryGetValue, MethodInfo getBaseType, MethodInfo getType)
    {
        var il = method.GetILGenerator();
        var ownerLocal = il.DeclareLocal(_types.Type);
        var closedOwnerLocal = il.DeclareLocal(_types.Type);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var innerLocal = il.DeclareLocal(inner);
        var slotLocal = il.DeclareLocal(_types.ObjectArray);
        var asTypeLocal = il.DeclareLocal(_types.Type);

        var retNull = il.DefineLabel();

        // if (_symbolAccessors == null) return null;
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Brfalse, retNull);

        // owner/idx from whether obj is a Type (static) or an instance.
        var isType = il.DefineLabel();
        var afterOwner = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, asTypeLocal);
        il.Emit(OpCodes.Ldloc, asTypeLocal);
        il.Emit(OpCodes.Brtrue, isType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getType);
        il.Emit(OpCodes.Stloc, ownerLocal);
        il.Emit(OpCodes.Ldc_I4, instanceSlot);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, afterOwner);
        il.MarkLabel(isType);
        il.Emit(OpCodes.Ldloc, asTypeLocal);
        il.Emit(OpCodes.Stloc, ownerLocal);
        il.Emit(OpCodes.Ldc_I4, staticSlot);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.MarkLabel(afterOwner);

        // base-chain walk
        var loopTop = il.DefineLabel();
        var nextBase = il.DefineLabel();
        var retIt = il.DefineLabel();
        var runClassCtor = _types.RuntimeHelpersRunClassConstructor;
        var getTypeHandle = _types.GetProperty(_types.Type, "TypeHandle").GetGetMethod()!;

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Brfalse, retNull);
        // #351: closed = SymbolClosedOwner(owner) — for an open generic-class
        // reference (MyP`1) this is a fully-closed instantiation that can drive
        // both cctor execution and reflective Invoke; closed/non-generic owners
        // pass through unchanged.
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Call, runtime.SymbolClosedOwner);
        il.Emit(OpCodes.Stloc, closedOwnerLocal);
        // Static-side accessors are registered in the owner class's .cctor, which
        // the CLR runs lazily — merely using the class as a Type value (typeof) does
        // not trigger it. Force it so a static `Class[Symbol.x]` access sees the
        // registration. Idempotent and cheap after the first run. (Instance-side
        // accessors are already registered by the time an instance exists.)
        // Run the cctor on the CLOSED owner: an open generic definition has no
        // runnable static initializer (#351).
        {
            var skipCctor = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, asTypeLocal);
            il.Emit(OpCodes.Brfalse, skipCctor);
            il.Emit(OpCodes.Ldloc, closedOwnerLocal);
            il.Emit(OpCodes.Callvirt, getTypeHandle);
            il.Emit(OpCodes.Call, runClassCtor);
            il.MarkLabel(skipCctor);
        }
        // if (!reg.TryGetValue(SymbolRegistryKey(owner), out inner)) goto nextBase;
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Call, runtime.SymbolRegistryKey);
        il.Emit(OpCodes.Ldloca, innerLocal);
        il.Emit(OpCodes.Callvirt, outerTryGetValue);
        il.Emit(OpCodes.Brfalse, nextBase);
        // if (!inner.TryGetValue(symbol, out slot)) goto nextBase;
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, slotLocal);
        il.Emit(OpCodes.Callvirt, innerTryGetValue);
        il.Emit(OpCodes.Brfalse, nextBase);
        // v = slot[idx]; if (v != null) return CloseSymbolAccessor(v, closed);
        il.Emit(OpCodes.Ldloc, slotLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, retIt);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(nextBase);
        il.Emit(OpCodes.Ldloc, ownerLocal);
        il.Emit(OpCodes.Callvirt, getBaseType);
        il.Emit(OpCodes.Stloc, ownerLocal);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(retIt);
        // stack: [v]  →  CloseSymbolAccessor(v, closed)
        il.Emit(OpCodes.Ldloc, closedOwnerLocal);
        il.Emit(OpCodes.Call, runtime.CloseSymbolAccessor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(retNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
