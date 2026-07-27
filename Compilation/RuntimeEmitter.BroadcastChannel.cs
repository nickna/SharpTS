using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$BroadcastChannel</c> class for compiled mode.
/// </summary>
/// <remarks>
/// $BroadcastChannel is a pure-IL implementation of the WHATWG / Node.js BroadcastChannel.
/// It uses only BCL types (no SharpTS.dll dependency) and inherits from the emitted
/// $EventEmitter so on/off/once/emit are inherited for free. Cross-thread delivery is
/// dispatched onto the singleton $EventLoop, mirroring how interpreter mode dispatches
/// onto the Interpreter event loop.
///
/// Spec mapping:
///   - new BroadcastChannel(name)         → ctor
///   - bc.postMessage(msg)                → PostMessage
///   - bc.close()                         → Close
///   - bc.name                            → Name property (camelCase 'name' resolved by reflection PascalCase fallback)
///   - bc.onmessage = h / addEventListener('message', h) → AddEventListener('message', h) → On
///   - bc.ref() / bc.unref()              → Ref / Unref (event-loop keep-alive)
/// </remarks>
public partial class RuntimeEmitter
{
    // Field/method handles cached during emission so subsequent helpers (e.g. constructor wiring
    // in ExpressionEmitterBase) can reference them.
    private TypeBuilder _broadcastChannelType = null!;
    private FieldBuilder _broadcastChannelRegistryField = null!;
    private FieldBuilder _broadcastChannelNextIdField = null!;
    // Shared clone-failure sentinel (#1255), mirroring $MessagePort's _cloneError: enqueued
    // in place of a per-subscriber clone when the posted value cannot be structured-cloned.
    // Drain compares by reference and fires 'messageerror' instead of 'message'.
    private FieldBuilder _broadcastChannelCloneErrorField = null!;
    private FieldBuilder _broadcastChannelNameField = null!;
    private FieldBuilder _broadcastChannelIdField = null!;
    private FieldBuilder _broadcastChannelClosedField = null!;
    private FieldBuilder _broadcastChannelRefedField = null!;
    private FieldBuilder _broadcastChannelPendingField = null!;
    // Property-style handlers (bc.onmessage = h / bc.onmessageerror = h).
    private FieldBuilder _broadcastChannelOnMessageField = null!;
    private FieldBuilder _broadcastChannelOnMessageErrorField = null!;
    private ConstructorBuilder _broadcastChannelCtor = null!;
    private MethodBuilder _broadcastChannelPostMessage = null!;
    private MethodBuilder _broadcastChannelClose = null!;
    private MethodBuilder _broadcastChannelRef = null!;
    private MethodBuilder _broadcastChannelUnref = null!;
    private MethodBuilder _broadcastChannelDrain = null!;

    private Type _bcInnerDictType = null!;   // ConcurrentDictionary<long, object>
    private Type _bcRegistryDictType = null!; // ConcurrentDictionary<string, object>

    /// <summary>
    /// Emits the $BroadcastChannel type. Must be called AFTER $EventEmitter is emitted
    /// (uses TSEventEmitterType as base) and AFTER $EventLoop is emitted (Schedule/Ref/Unref).
    /// </summary>
    private void EmitBroadcastChannelClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Inner registry dictionaries are typed as ConcurrentDictionary<TKey, object> so the
        // generic args are concrete CLR types — avoids TypeBuilder.GetConstructor gymnastics.
        _bcInnerDictType = typeof(ConcurrentDictionary<long, object>);
        _bcRegistryDictType = typeof(ConcurrentDictionary<string, object>);

        var typeBuilder = moduleBuilder.DefineType(
            "$BroadcastChannel",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        _broadcastChannelType = typeBuilder;

        // ---- Static fields ----
        _broadcastChannelRegistryField = typeBuilder.DefineField(
            "_registry", _bcRegistryDictType,
            FieldAttributes.Private | FieldAttributes.Static);

        _broadcastChannelNextIdField = typeBuilder.DefineField(
            "_nextId", _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static);

        _broadcastChannelCloneErrorField = typeBuilder.DefineField(
            "_cloneError", _types.Object,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);

        // Static constructor: initialize _registry + _cloneError
        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        {
            var il = cctor.GetILGenerator();
            // _registry = new ConcurrentDictionary<string, object>(StringComparer.Ordinal)
            var ordinal = typeof(StringComparer).GetProperty("Ordinal")!.GetGetMethod()!;
            il.Emit(OpCodes.Call, ordinal);
            var bcRegistryCtor = _bcRegistryDictType.GetConstructor([typeof(IEqualityComparer<string>)])!;
            il.Emit(OpCodes.Newobj, bcRegistryCtor);
            il.Emit(OpCodes.Stsfld, _broadcastChannelRegistryField);
            // _cloneError = new object()
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Stsfld, _broadcastChannelCloneErrorField);
            il.Emit(OpCodes.Ret);
        }

        // ---- Instance fields ----
        _broadcastChannelNameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);
        _broadcastChannelIdField = typeBuilder.DefineField("_id", _types.Int64, FieldAttributes.Private);
        _broadcastChannelClosedField = typeBuilder.DefineField("_closed", _types.Boolean, FieldAttributes.Private);
        _broadcastChannelRefedField = typeBuilder.DefineField("_refed", _types.Boolean, FieldAttributes.Private);
        _broadcastChannelPendingField = typeBuilder.DefineField("_pending", _types.ConcurrentQueueOfObject, FieldAttributes.Private);
        _broadcastChannelOnMessageField = typeBuilder.DefineField("_onmessage", _types.Object, FieldAttributes.Private);
        _broadcastChannelOnMessageErrorField = typeBuilder.DefineField("_onmessageerror", _types.Object, FieldAttributes.Private);

        // ---- Constructor: $BroadcastChannel(string name) ----
        EmitBroadcastChannelConstructor(typeBuilder, runtime);

        // ---- Instance methods ----
        EmitBroadcastChannelDrain(typeBuilder, runtime);
        EmitBroadcastChannelPostMessage(typeBuilder, runtime);
        EmitBroadcastChannelClose(typeBuilder, runtime);
        EmitBroadcastChannelRef(typeBuilder, runtime);
        EmitBroadcastChannelUnref(typeBuilder, runtime);
        EmitBroadcastChannelGetName(typeBuilder, runtime);
        EmitBroadcastChannelOnMessageAccessors(typeBuilder, runtime);
        EmitBroadcastChannelSetMember(typeBuilder, runtime);
        EmitBroadcastChannelAddEventListener(typeBuilder, runtime);
        EmitBroadcastChannelRemoveEventListener(typeBuilder, runtime);

        typeBuilder.CreateType();

        // Publish handles for use by ExpressionEmitterBase.TryEmitBuiltInConstructor.
        runtime.BroadcastChannelType = _broadcastChannelType;
        runtime.BroadcastChannelCtor = _broadcastChannelCtor;
        _ = _broadcastChannelPostMessage;
        _ = _broadcastChannelClose;
        _ = _broadcastChannelRef;
        _ = _broadcastChannelUnref;
    }

    private void EmitBroadcastChannelConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        _broadcastChannelCtor = ctor;

        var il = ctor.GetILGenerator();

        // base()  — $EventEmitter parameterless ctor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _name = name
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _broadcastChannelNameField);

        // _id = Interlocked.Increment(ref _nextId)
        var interlockedIncLong = typeof(Interlocked).GetMethod("Increment", [typeof(long).MakeByRefType()])!;
        var idLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldsflda, _broadcastChannelNextIdField);
        il.Emit(OpCodes.Call, interlockedIncLong);
        il.Emit(OpCodes.Stloc, idLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idLocal);
        il.Emit(OpCodes.Stfld, _broadcastChannelIdField);

        // _pending = new ConcurrentQueue<object>()
        il.Emit(OpCodes.Ldarg_0);
        var queueCtor = _types.ConcurrentQueueOfObject.GetConstructor(Type.EmptyTypes)!;
        il.Emit(OpCodes.Newobj, queueCtor);
        il.Emit(OpCodes.Stfld, _broadcastChannelPendingField);

        // _refed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _broadcastChannelRefedField);

        // bucket = (ConcurrentDictionary<long, object>)_registry.GetOrAdd(name, new ConcurrentDictionary<long, object>())
        // Use the simple value-overload of GetOrAdd. It allocates an unused dict on the cold path,
        // which is fine for the rare ctor case.
        var bucketLocal = il.DeclareLocal(_bcInnerDictType);
        il.Emit(OpCodes.Ldsfld, _broadcastChannelRegistryField);
        il.Emit(OpCodes.Ldarg_1);  // name
        var innerCtor = _bcInnerDictType.GetConstructor(Type.EmptyTypes)!;
        il.Emit(OpCodes.Newobj, innerCtor);
        var getOrAddValue = _bcRegistryDictType.GetMethod("GetOrAdd", [_types.String, _types.Object])!;
        il.Emit(OpCodes.Callvirt, getOrAddValue);
        il.Emit(OpCodes.Castclass, _bcInnerDictType);
        il.Emit(OpCodes.Stloc, bucketLocal);

        // bucket[_id] = this
        il.Emit(OpCodes.Ldloc, bucketLocal);
        il.Emit(OpCodes.Ldloc, idLocal);
        il.Emit(OpCodes.Ldarg_0);
        var setItem = _bcInnerDictType.GetProperty("Item")!.GetSetMethod()!;
        il.Emit(OpCodes.Callvirt, setItem);

        // $EventLoop.GetInstance().Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Drain() — dequeues pending messages and emits them as 'message' events.
    /// </summary>
    private void EmitBroadcastChannelDrain(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Drain",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        _broadcastChannelDrain = method;

        var il = method.GetILGenerator();

        var loopTop = il.DefineLabel();
        var exitLabel = il.DefineLabel();
        var msgLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var eventDataLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.MarkLabel(loopTop);

        // Drain pending messages even if _closed — Close() just removes us from the
        // registry and prevents future PostMessage; in-flight deliveries that were
        // queued before Close() still complete to match Node semantics.

        // if (!_pending.TryDequeue(out msg)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelPendingField);
        il.Emit(OpCodes.Ldloca, msgLocal);
        var tryDequeue = _types.ConcurrentQueueOfObject.GetMethod("TryDequeue", [_types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, tryDequeue);
        il.Emit(OpCodes.Brfalse, exitLabel);

        // A clone-failure sentinel (from postMessage of an uncloneable value, #1255) fires
        // 'messageerror' instead of 'message' — WHATWG receiver-side model, mirroring
        // $MessagePort. ReferenceEquals(msg, _cloneError).
        var notCloneErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Ldsfld, _broadcastChannelCloneErrorField);
        il.Emit(OpCodes.Bne_Un, notCloneErrorLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "messageerror");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Also invoke the property-style onmessageerror handler if set.
        var skipOnMessageErrorLabel = il.DefineLabel();
        var onMsgErrLocal = il.DeclareLocal(runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelOnMessageErrorField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, onMsgErrLocal);
        il.Emit(OpCodes.Ldloc, onMsgErrLocal);
        il.Emit(OpCodes.Brfalse, skipOnMessageErrorLabel);
        il.Emit(OpCodes.Ldloc, onMsgErrLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipOnMessageErrorLabel);

        il.Emit(OpCodes.Br, loopTop);
        il.MarkLabel(notCloneErrorLabel);

        // eventData = new Dictionary<string, object>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, eventDataLocal);

        var setItemDict = _types.DictionaryStringObject.GetProperty("Item")!.GetSetMethod()!;
        // eventData["data"] = msg
        il.Emit(OpCodes.Ldloc, eventDataLocal);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Callvirt, setItemDict);
        // eventData["type"] = "message"
        il.Emit(OpCodes.Ldloc, eventDataLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Callvirt, setItemDict);
        // eventData["target"] = this
        il.Emit(OpCodes.Ldloc, eventDataLocal);
        il.Emit(OpCodes.Ldstr, "target");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, setItemDict);

        // args = new object[1] { eventData }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, eventDataLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // this.Emit("message", args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Also invoke the property-style onmessage handler if set (WHATWG spec):
        // if (_onmessage is $TSFunction tf) tf.Invoke(args);
        var skipOnMessageLabel = il.DefineLabel();
        var onMsgLocal = il.DeclareLocal(runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelOnMessageField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, onMsgLocal);
        il.Emit(OpCodes.Ldloc, onMsgLocal);
        il.Emit(OpCodes.Brfalse, skipOnMessageLabel);
        il.Emit(OpCodes.Ldloc, onMsgLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipOnMessageLabel);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits PostMessage(object msg).
    /// </summary>
    private void EmitBroadcastChannelPostMessage(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PostMessage",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        _broadcastChannelPostMessage = method;

        var il = method.GetILGenerator();

        var notClosedLabel = il.DefineLabel();
        var noBucketLabel = il.DefineLabel();
        var loopTop = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipSelfLabel = il.DefineLabel();
        var exitLabel = il.DefineLabel();

        // if (_closed) throw new InvalidOperationException("InvalidStateError: BroadcastChannel is closed")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);
        il.Emit(OpCodes.Ldstr, "InvalidStateError: BroadcastChannel is closed");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notClosedLabel);

        // bucket = _registry.TryGetValue(_name, out bucketObj) ? bucketObj : null
        var bucketObjLocal = il.DeclareLocal(_types.Object);
        var bucketLocal = il.DeclareLocal(_bcInnerDictType);
        il.Emit(OpCodes.Ldsfld, _broadcastChannelRegistryField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelNameField);
        il.Emit(OpCodes.Ldloca, bucketObjLocal);
        var registryTryGet = _bcRegistryDictType.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, registryTryGet);
        il.Emit(OpCodes.Brfalse, exitLabel);

        il.Emit(OpCodes.Ldloc, bucketObjLocal);
        il.Emit(OpCodes.Castclass, _bcInnerDictType);
        il.Emit(OpCodes.Stloc, bucketLocal);

        // Snapshot subscribers: object[] snapshot = bucket.Values.ToArray()? — ConcurrentDictionary<long,object>.Values
        // Use Linq's ToArray? Not available without linq. Manually iterate via GetEnumerator.
        // Simpler: copy to a List<object> via the Values collection, then iterate.
        var valuesCollection = _bcInnerDictType.GetProperty("Values")!.GetGetMethod()!; // ICollection<object>
        var snapshotLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, bucketLocal);
        il.Emit(OpCodes.Callvirt, valuesCollection);
        var listCtorFromEnumerable = _types.ListOfObject.GetConstructor([_types.IEnumerableOfObject])!;
        il.Emit(OpCodes.Newobj, listCtorFromEnumerable);
        il.Emit(OpCodes.Stloc, snapshotLocal);

        // for (int i = 0; i < snapshot.Count; i++) { var sub = snapshot[i]; if (sub == this) continue; deliver(sub, msg); }
        var indexLocal = il.DeclareLocal(_types.Int32);
        var subLocal = il.DeclareLocal(_broadcastChannelType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, snapshotLocal);
        var listCount = _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Bge, loopEnd);

        // sub = (BroadcastChannel)snapshot[i]
        il.Emit(OpCodes.Ldloc, snapshotLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        var listGetItem = _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!;
        il.Emit(OpCodes.Callvirt, listGetItem);
        il.Emit(OpCodes.Castclass, _broadcastChannelType);
        il.Emit(OpCodes.Stloc, subLocal);

        // if (sub == this) goto skipSelf
        il.Emit(OpCodes.Ldloc, subLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Beq, skipSelfLabel);

        // if (sub._closed) goto skipSelf
        il.Emit(OpCodes.Ldloc, subLocal);
        il.Emit(OpCodes.Ldfld, _broadcastChannelClosedField);
        il.Emit(OpCodes.Brtrue, skipSelfLabel);

        // Deep-clone the message for this subscriber via $Runtime.StructuredClone(msg, null).
        // Per-subscriber clone matches the WHATWG spec and prevents mutation aliasing across
        // receivers. An uncloneable value (#1255) is NOT thrown back on the sender — the
        // WHATWG model fires 'messageerror' on the receiver instead (matches the
        // interpreter's catch around StructuredClone.Clone in SharpTSBroadcastChannel.
        // PostMessage) — so catch $DataCloneError here and enqueue the shared _cloneError
        // sentinel for THIS subscriber instead of a clone, then continue to the next one.
        var clonedLocal = il.DeclareLocal(_types.Object);
        var haveClonedLabel = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);  // transferList
        il.Emit(OpCodes.Call, runtime.StructuredCloneClone);
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.Emit(OpCodes.Leave, haveClonedLabel);

        il.BeginCatchBlock(runtime.TSDataCloneErrorType);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, _broadcastChannelCloneErrorField);
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.EndExceptionBlock();

        il.MarkLabel(haveClonedLabel);

        // sub._pending.Enqueue(cloned)
        il.Emit(OpCodes.Ldloc, subLocal);
        il.Emit(OpCodes.Ldfld, _broadcastChannelPendingField);
        il.Emit(OpCodes.Ldloc, clonedLocal);
        var enqueue = _types.ConcurrentQueueOfObject.GetMethod("Enqueue", [_types.Object])!;
        il.Emit(OpCodes.Callvirt, enqueue);

        // $EventLoop.GetInstance().Schedule(new Action(sub.Drain))
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldloc, subLocal);
        il.Emit(OpCodes.Ldftn, _broadcastChannelDrain);
        var actionCtor = typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!;
        il.Emit(OpCodes.Newobj, actionCtor);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);

        il.MarkLabel(skipSelfLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(loopEnd);
        il.MarkLabel(exitLabel);
        il.MarkLabel(noBucketLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Close().
    /// </summary>
    private void EmitBroadcastChannelClose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        _broadcastChannelClose = method;

        var il = method.GetILGenerator();
        var alreadyClosedLabel = il.DefineLabel();
        var noBucketLabel = il.DefineLabel();
        var notRefedLabel = il.DefineLabel();

        // if (_closed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelClosedField);
        il.Emit(OpCodes.Brtrue, alreadyClosedLabel);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _broadcastChannelClosedField);

        // if (_registry.TryGetValue(_name, out bucketObj)) bucket.TryRemove(_id, out _)
        var bucketObjLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, _broadcastChannelRegistryField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelNameField);
        il.Emit(OpCodes.Ldloca, bucketObjLocal);
        var registryTryGet = _bcRegistryDictType.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, registryTryGet);
        il.Emit(OpCodes.Brfalse, noBucketLabel);

        // bucket = (ConcurrentDictionary<long,object>)bucketObj
        // bucket.TryRemove(_id, out _)
        var dummyOutLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, bucketObjLocal);
        il.Emit(OpCodes.Castclass, _bcInnerDictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelIdField);
        il.Emit(OpCodes.Ldloca, dummyOutLocal);
        var innerTryRemove = _bcInnerDictType.GetMethod("TryRemove", [_types.Int64, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, innerTryRemove);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noBucketLabel);

        // if (_refed) { $EventLoop.GetInstance().Unref(); _refed = false; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelRefedField);
        il.Emit(OpCodes.Brfalse, notRefedLabel);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _broadcastChannelRefedField);
        il.MarkLabel(notRefedLabel);

        // this.Emit("close", new object[0])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyClosedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Ref().
    /// </summary>
    private void EmitBroadcastChannelRef(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Ref",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        _broadcastChannelRef = method;

        var il = method.GetILGenerator();
        var alreadyRefedLabel = il.DefineLabel();

        // if (_refed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelRefedField);
        il.Emit(OpCodes.Brtrue, alreadyRefedLabel);

        // _refed = true; loop.Ref()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _broadcastChannelRefedField);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

        il.MarkLabel(alreadyRefedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Unref().
    /// </summary>
    private void EmitBroadcastChannelUnref(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Unref",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        _broadcastChannelUnref = method;

        var il = method.GetILGenerator();
        var notRefedLabel = il.DefineLabel();

        // if (!_refed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelRefedField);
        il.Emit(OpCodes.Brfalse, notRefedLabel);

        // _refed = false; loop.Unref()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _broadcastChannelRefedField);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);

        il.MarkLabel(notRefedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Name property as a public getter method ("Name") plus a CLR PropertyBuilder.
    /// The reflection PascalCase fallback in <c>GetFieldsProperty</c> resolves <c>bc.name</c>
    /// to <c>get_Name</c> via case-insensitive property lookup.
    /// </summary>
    private void EmitBroadcastChannelGetName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var getter = typeBuilder.DefineMethod(
            "get_Name",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _broadcastChannelNameField);
        il.Emit(OpCodes.Ret);

        var prop = typeBuilder.DefineProperty("Name", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        prop.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits the <c>Onmessage</c> and <c>Onmessageerror</c> property accessors backed
    /// by private fields, so <c>bc.onmessage = h</c> maps through the PascalCase reflection
    /// fallback in <c>GetFieldsProperty</c> / <c>SetFieldsProperty</c>.
    /// </summary>
    private void EmitBroadcastChannelOnMessageAccessors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitSimpleObjectProperty(typeBuilder, "Onmessage", _broadcastChannelOnMessageField);
        EmitSimpleObjectProperty(typeBuilder, "Onmessageerror", _broadcastChannelOnMessageErrorField);
    }

    /// <summary>
    /// Defines a CLR property with getter + setter backed by the given object-typed field.
    /// </summary>
    private void EmitSimpleObjectProperty(TypeBuilder typeBuilder, string propertyName, FieldBuilder backingField)
    {
        var getter = typeBuilder.DefineMethod(
            "get_" + propertyName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        {
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, backingField);
            il.Emit(OpCodes.Ret);
        }

        var setter = typeBuilder.DefineMethod(
            "set_" + propertyName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Object]
        );
        {
            var il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, backingField);
            il.Emit(OpCodes.Ret);
        }

        var prop = typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        prop.SetGetMethod(getter);
        prop.SetSetMethod(setter);
    }

    /// <summary>
    /// Emits SetMember(string name, object value) — the write-side dispatch picked up by
    /// <c>$Runtime.SetFieldsProperty</c>'s reflection fallback when user code does
    /// <c>bc.onmessage = h</c>. Without this, property-style writes have nowhere to land
    /// because SetFieldsProperty has no PascalCase-property reflection path (only GetFieldsProperty does).
    /// </summary>
    private void EmitBroadcastChannelSetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetMember",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );

        var il = method.GetILGenerator();
        var tryOnMessageErrorLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (name == "onmessage") { _onmessage = value; return; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "onmessage");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, tryOnMessageErrorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _broadcastChannelOnMessageField);
        il.Emit(OpCodes.Ret);

        // if (name == "onmessageerror") { _onmessageerror = value; return; }
        il.MarkLabel(tryOnMessageErrorLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "onmessageerror");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _broadcastChannelOnMessageErrorField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits AddEventListener(string type, object listener) which delegates to base.On.
    /// Lets <c>bc.addEventListener('message', h)</c> resolve via the reflection method-name
    /// fallback in compiled mode.
    /// </summary>
    private void EmitBroadcastChannelAddEventListener(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AddEventListener",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );

        var il = method.GetILGenerator();
        // this.On(type, listener); discard return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits RemoveEventListener(string type, object listener) which delegates to base.Off.
    /// </summary>
    private void EmitBroadcastChannelRemoveEventListener(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RemoveEventListener",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOff);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }
}
