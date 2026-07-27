using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private FieldBuilder _finRegEntryHeldValueField = null!;
    private FieldBuilder _finRegEntryQueueField = null!;
    private FieldBuilder _finRegEntrySuppressedField = null!;

    /// <summary>
    /// Phase 1: Defines the $FinRegEntry type with fields, constructor, Suppress(), and Finalize().
    /// Must be called before EmitRuntimeClass.
    /// </summary>
    private void EmitFinRegEntryTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$FinRegEntry",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        _finRegEntryHeldValueField = typeBuilder.DefineField("_heldValue", _types.Object, FieldAttributes.Private);
        _finRegEntryQueueField = typeBuilder.DefineField("_queue", _types.ConcurrentQueueOfObject, FieldAttributes.Private);
        _finRegEntrySuppressedField = typeBuilder.DefineField("_suppressed", _types.Boolean,
            FieldAttributes.Private); // volatile semantics via Volatile.Read/Write in IL

        // Constructor: public $FinRegEntry(object heldValue, ConcurrentQueue<object?> queue)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.ConcurrentQueueOfObject]
        );
        runtime.FinRegEntryCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        // base()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);
        // _heldValue = heldValue
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, _finRegEntryHeldValueField);
        // _queue = queue
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, _finRegEntryQueueField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: public void Suppress()
        var suppress = typeBuilder.DefineMethod(
            "Suppress",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.FinRegEntrySuppress = suppress;

        var sil = suppress.GetILGenerator();
        // Volatile.Write(ref _suppressed, true)
        sil.Emit(OpCodes.Ldarg_0);
        sil.Emit(OpCodes.Ldflda, _finRegEntrySuppressedField);
        sil.Emit(OpCodes.Ldc_I4_1);
        sil.Emit(OpCodes.Volatile);
        sil.Emit(OpCodes.Stind_I1);
        sil.Emit(OpCodes.Ret);

        // Override Finalize(): protected override void Finalize()
        var finalize = typeBuilder.DefineMethod(
            "Finalize",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            Type.EmptyTypes
        );
        var fil = finalize.GetILGenerator();

        // try { if (!_suppressed) _queue.Enqueue(_heldValue); }
        // finally { base.Finalize(); }
        fil.BeginExceptionBlock();

        // Volatile.Read(ref _suppressed)
        fil.Emit(OpCodes.Ldarg_0);
        fil.Emit(OpCodes.Ldflda, _finRegEntrySuppressedField);
        fil.Emit(OpCodes.Volatile);
        fil.Emit(OpCodes.Ldind_I1);
        var skipEnqueue = fil.DefineLabel();
        fil.Emit(OpCodes.Brtrue, skipEnqueue);

        // _queue.Enqueue(_heldValue)
        fil.Emit(OpCodes.Ldarg_0);
        fil.Emit(OpCodes.Ldfld, _finRegEntryQueueField);
        fil.Emit(OpCodes.Ldarg_0);
        fil.Emit(OpCodes.Ldfld, _finRegEntryHeldValueField);
        var enqueueMethod = _types.ConcurrentQueueOfObject.GetMethod("Enqueue")!;
        fil.Emit(OpCodes.Callvirt, enqueueMethod);

        fil.MarkLabel(skipEnqueue);

        fil.BeginFinallyBlock();

        // base.Finalize()
        fil.Emit(OpCodes.Ldarg_0);
        fil.Emit(OpCodes.Call, _types.Object.GetMethod("Finalize",
            BindingFlags.NonPublic | BindingFlags.Instance)!);

        fil.EndExceptionBlock();

        fil.Emit(OpCodes.Ret);

        // Finalize type
        runtime.FinRegEntryType = typeBuilder.CreateType()!;
    }

    private void EmitFinalizationRegistryMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Skip when the program doesn't construct a FinalizationRegistry — the
        // Register/Unregister methods reference $FinRegEntry, which we also gate.
        if (!_features.UsesFinalizationRegistry) return;

        // Note: _finRegPokeTable field is defined in EmitRuntimeClass before the static constructor
        EmitCreateFinalizationRegistry(typeBuilder, runtime);
        EmitFinalizationRegistryRegister(typeBuilder, runtime);
        EmitFinalizationRegistryUnregister(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits static constructor initialization for _finRegPokeTable.
    /// Called from within the $Runtime static constructor emission.
    /// </summary>
    internal void EmitFinRegPokeTableInit(ILGenerator il, EmittedRuntime runtime)
    {
        // _finRegPokeTable = new ConditionalWeakTable<object, object>()
        il.Emit(OpCodes.Newobj, _types.ConditionalWeakTableObjectObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stsfld, runtime.FinRegPokeTableField);
    }

    /// <summary>
    /// Emits: public static object CreateFinalizationRegistry(object callback)
    /// Returns new object[] { callback, new ConcurrentQueue&lt;object?&gt;(), new List&lt;object?[]&gt;(), new object() }
    /// Pure IL, no reflection to SharpTS.dll.
    /// </summary>
    private void EmitCreateFinalizationRegistry(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateFinalizationRegistry",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateFinalizationRegistry = method;

        var il = method.GetILGenerator();

        // if (callback == null) throw
        var notNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Ldstr, "Runtime Error: FinalizationRegistry constructor requires a callback function.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notNull);

        // new object[4]
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, _types.Object);

        // [0] = callback
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // [1] = new ConcurrentQueue<object?>()
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, _types.ConcurrentQueueOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        // [2] = new List<object?[]>() — use List<object?> since object?[] IS object
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Stelem_Ref);

        // [3] = new object()
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newobj, _types.Object.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FinalizationRegistryRegister(object registry, object target, object heldValue, object token)
    /// Pure IL emission — creates $FinRegEntry, adds to ConditionalWeakTable, stores in entries list.
    /// </summary>
    private void EmitFinalizationRegistryRegister(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FinalizationRegistryRegister",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.FinalizationRegistryRegister = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // Validate target is not null
        var targetNotNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, targetNotNull);
        il.Emit(OpCodes.Ldstr, "Runtime Error: FinalizationRegistry target cannot be null or undefined.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(targetNotNull);

        // Validate target is not a primitive (string, double, bool)
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldstr, "Runtime Error: FinalizationRegistry target must be an object.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notString);

        var notDouble = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDouble);
        il.Emit(OpCodes.Ldstr, "Runtime Error: FinalizationRegistry target must be an object.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notDouble);

        var notBool = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBool);
        il.Emit(OpCodes.Ldstr, "Runtime Error: FinalizationRegistry target must be an object.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notBool);

        // Cast registry to object[]
        var regLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(object?[]));
        il.Emit(OpCodes.Stloc, regLocal);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Brfalse, returnLabel);

        // Extract queue = (ConcurrentQueue<object?>)reg[1]
        var queueLocal = il.DeclareLocal(_types.ConcurrentQueueOfObject);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ConcurrentQueueOfObject);
        il.Emit(OpCodes.Stloc, queueLocal);

        // Extract entries = (List<object?>)reg[2]
        var entriesLocal = il.DeclareLocal(_types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, entriesLocal);

        // Extract lockObj = reg[3]
        var lockLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, lockLocal);

        // var entry = new $FinRegEntry(heldValue, queue)
        var entryLocal = il.DeclareLocal(runtime.FinRegEntryType);
        il.Emit(OpCodes.Ldarg_2); // heldValue
        il.Emit(OpCodes.Ldloc, queueLocal);
        il.Emit(OpCodes.Newobj, runtime.FinRegEntryCtor);
        il.Emit(OpCodes.Stloc, entryLocal);

        // _finRegPokeTable.AddOrUpdate(target, entry)
        il.Emit(OpCodes.Ldsfld, runtime.FinRegPokeTableField);
        il.Emit(OpCodes.Ldarg_1); // target
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.ConditionalWeakTableObjectObject.GetMethod("AddOrUpdate")!);

        // Monitor.Enter(lockObj, ref lockTaken)
        var lockTakenLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Ldloca, lockTakenLocal);
        il.Emit(OpCodes.Call, _types.MonitorEnter);

        il.BeginExceptionBlock();

        // entries.Add(new object[] { target, heldValue, token, entry })
        il.Emit(OpCodes.Ldloc, entriesLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // target
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // heldValue
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_3); // token
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // Call Add on List<object?> — entries are stored as object (the object[] array)
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add")!);

        il.BeginFinallyBlock();

        // Monitor.Exit(lockObj) if lockTaken
        var skipExit = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lockTakenLocal);
        il.Emit(OpCodes.Brfalse, skipExit);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Call, _types.MonitorExit);
        il.MarkLabel(skipExit);

        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FinalizationRegistryUnregister(object registry, object token)
    /// Iterates entries backwards, suppresses matching entries, removes them, returns boxed bool.
    /// Pure IL emission.
    /// </summary>
    private void EmitFinalizationRegistryUnregister(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FinalizationRegistryUnregister",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.FinalizationRegistryUnregister = method;

        var il = method.GetILGenerator();

        // if (token == null) return false
        var tokenNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, tokenNull);

        // Cast registry to object[]
        var regLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(object?[]));
        il.Emit(OpCodes.Stloc, regLocal);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Brfalse, tokenNull);

        // Extract entries = (List<object?>)reg[2]
        var entriesLocal = il.DeclareLocal(_types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, entriesLocal);

        // Extract lockObj = reg[3]
        var lockLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, regLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, lockLocal);

        // bool removed = false
        var removedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, removedLocal);

        // Monitor.Enter(lockObj, ref lockTaken)
        var lockTakenLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Ldloca, lockTakenLocal);
        il.Emit(OpCodes.Call, _types.MonitorEnter);

        il.BeginExceptionBlock();

        // int i = entries.Count - 1
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, entriesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop: while (i >= 0)
        var loopCheck = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCheck);

        il.MarkLabel(loopBody);

        // var entry = (object?[])entries[i]
        var entryArrLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Ldloc, entriesLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(object?[]));
        il.Emit(OpCodes.Stloc, entryArrLocal);

        // if (entry[2] != null && ReferenceEquals(entry[2], token))
        var skipEntry = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, entryArrLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, skipEntry);

        il.Emit(OpCodes.Ldloc, entryArrLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_1); // token
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, skipEntry);

        // ((FinRegEntry)entry[3]).Suppress()
        il.Emit(OpCodes.Ldloc, entryArrLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.FinRegEntryType);
        il.Emit(OpCodes.Callvirt, runtime.FinRegEntrySuppress);

        // entries.RemoveAt(i)
        il.Emit(OpCodes.Ldloc, entriesLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("RemoveAt")!);

        // removed = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, removedLocal);

        il.MarkLabel(skipEntry);

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCheck);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, loopBody);

        il.BeginFinallyBlock();

        // Monitor.Exit(lockObj) if lockTaken
        var skipExit2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lockTakenLocal);
        il.Emit(OpCodes.Brfalse, skipExit2);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Call, _types.MonitorExit);
        il.MarkLabel(skipExit2);

        il.EndExceptionBlock();

        // return (object)removed
        il.Emit(OpCodes.Ldloc, removedLocal);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // return false path
        il.MarkLabel(tokenNull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }
}
