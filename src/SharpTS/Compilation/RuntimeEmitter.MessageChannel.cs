using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$MessagePort</c> / <c>$MessageChannel</c> classes
/// for compiled mode (#222).
/// </summary>
/// <remarks>
/// Pure-IL implementations of Node's worker_threads MessageChannel pair —
/// BCL types only (no SharpTS.dll dependency). <c>$MessagePort</c> inherits
/// the emitted <c>$EventEmitter</c> so on/once/off/emit come for free, and
/// overrides the <c>OnListenerAdded</c> virtual hook so registering a
/// 'message' listener implicitly starts the port (matches the interpreter's
/// post-#209 <c>SharpTSMessagePort</c> semantics):
///   - postMessage(v)  → structured-clones v, enqueues to the PARTNER port,
///     schedules its Drain on the $EventLoop when it has started
///   - on('message')   → implicit Start(): Ref the event loop + drain queued
///   - listener receives the cloned value DIRECTLY (not a {data} wrapper)
///   - close()         → stops delivery, Unrefs so the process can exit
/// Must be emitted AFTER EmitRuntimeClass (uses $Runtime.StructuredClone)
/// and after $EventEmitter / $EventLoop.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSMessagePort.
/// </remarks>
public partial class RuntimeEmitter
{
    private void EmitMessageChannelTypes(
        ModuleBuilder moduleBuilder,
        EmittedMessageChannelRuntime channels,
        TypeBuilder runtimeType,
        EmittedEventEmitterRuntime eventEmitter,
        EmittedEventLoopRuntime eventLoop,
        MethodInfo structuredClone,
        Type dataCloneError)
    {
        EmitMessagePortClass(moduleBuilder, channels.Port, eventEmitter, eventLoop, structuredClone, dataCloneError);
        EmitMessageChannelClass(moduleBuilder, channels);
        EmitCreateMessageChannelHelper(runtimeType, channels);
    }

    private void EmitMessagePortClass(
        ModuleBuilder moduleBuilder,
        EmittedMessagePortRuntime port,
        EmittedEventEmitterRuntime eventEmitter,
        EmittedEventLoopRuntime eventLoop,
        MethodInfo structuredClone,
        Type dataCloneError)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$MessagePort",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            eventEmitter.Type
        );
        port.Type = typeBuilder;

        // Assembly-visible so $MessageChannel's ctor can pair the ports.
        port.Partner = typeBuilder.DefineField("_partner", _types.Object, FieldAttributes.Assembly);
        port.Pending = typeBuilder.DefineField("_pending", _types.ConcurrentQueueOfObject, FieldAttributes.Assembly);
        port.Started = typeBuilder.DefineField("_started", _types.Boolean, FieldAttributes.Assembly);
        port.Closed = typeBuilder.DefineField("_closed", _types.Boolean, FieldAttributes.Assembly);
        port.Refed = typeBuilder.DefineField("_refed", _types.Boolean, FieldAttributes.Assembly);
        port.CrossThread = typeBuilder.DefineField("_crossThread", _types.Boolean, FieldAttributes.Assembly);
        // Optional on-enqueue notification. Null for ordinary in-process ports; set
        // (reflectively) by CompiledMessagePortBridge when this port has been
        // transferred to an interpreter worker, so a parent post wakes the worker loop
        // to drain _pending event-driven instead of the worker polling (#465).
        port.OnEnqueue = typeBuilder.DefineField("_onEnqueue", typeof(Action), FieldAttributes.Assembly);

        // Static clone-failure sentinel (#1077). A single shared instance is enqueued in
        // place of a message whose value cannot be structured-cloned; Drain compares by
        // reference and emits 'messageerror' instead of 'message'.
        port.CloneError = typeBuilder.DefineField(
            "_cloneError",
            _types.Object,
            FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly);
        var cctorIl = typeBuilder.DefineTypeInitializer().GetILGenerator();
        cctorIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        cctorIl.Emit(OpCodes.Stsfld, port.CloneError);
        cctorIl.Emit(OpCodes.Ret);

        EmitMessagePortConstructorIl(typeBuilder, port, eventEmitter);
        EmitMessagePortDrain(typeBuilder, port, eventEmitter);
        EmitMessagePortRef(typeBuilder, port, eventLoop);
        EmitMessagePortUnref(typeBuilder, port, eventLoop);
        EmitMessagePortMarkTransferredAcrossThreads(typeBuilder, port);
        EmitMessagePortStart(typeBuilder, port, eventLoop);
        EmitMessagePortPostMessage(typeBuilder, port, eventLoop, structuredClone, dataCloneError);
        EmitMessagePortClose(typeBuilder, port, eventEmitter);
        EmitMessagePortOnListenerAdded(typeBuilder, port, eventEmitter);

        typeBuilder.CreateType();
    }

    private void EmitMessagePortConstructorIl(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventEmitterRuntime eventEmitter)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        port.Ctor = ctor;

        var il = ctor.GetILGenerator();

        // base() — $EventEmitter parameterless ctor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, eventEmitter.Ctor);

        // _pending = new ConcurrentQueue<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ConcurrentQueueOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, port.Pending);

        // Not refed at construction — an unstarted port must not keep the
        // process alive (Node: only a started port with pending work does).
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Drain() — dequeues pending messages and emits each as a 'message'
    /// event with the cloned value directly (Node worker_threads semantics,
    /// not a DOM-style {data} wrapper).
    /// </summary>
    private void EmitMessagePortDrain(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod(
            "Drain",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        port.Drain = method;

        var il = method.GetILGenerator();
        var loopTop = il.DefineLabel();
        var exitLabel = il.DefineLabel();
        var msgLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // if (!_started || _closed) return — messages stay queued until Start().
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Started);
        il.Emit(OpCodes.Brfalse, exitLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, exitLabel);

        il.MarkLabel(loopTop);

        // if (!_pending.TryDequeue(out msg)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Pending);
        il.Emit(OpCodes.Ldloca, msgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConcurrentQueueOfObject, "TryDequeue", [_types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, exitLabel);

        // A clone-failure sentinel (from postMessage of an uncloneable value) is
        // delivered as 'messageerror' (no args), not 'message' — Node's receiver-side
        // model (#1077). ReferenceEquals(msg, _cloneError).
        var notCloneErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Ldsfld, port.CloneError);
        il.Emit(OpCodes.Bne_Un, notCloneErrorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "messageerror");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopTop);
        il.MarkLabel(notCloneErrorLabel);

        // this.Emit("message", new object[1] { msg })
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMessagePortRef(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventLoopRuntime eventLoop)
    {
        var method = typeBuilder.DefineMethod("Ref", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        port.Ref = method;

        var il = method.GetILGenerator();
        var alreadyRefedLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Refed);
        il.Emit(OpCodes.Brtrue, alreadyRefedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, port.Refed);
        il.Emit(OpCodes.Call, eventLoop.GetInstance);
        il.Emit(OpCodes.Callvirt, eventLoop.Ref);
        il.MarkLabel(alreadyRefedLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMessagePortUnref(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventLoopRuntime eventLoop)
    {
        var method = typeBuilder.DefineMethod("Unref", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        port.Unref = method;

        var il = method.GetILGenerator();
        var notRefedLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Refed);
        il.Emit(OpCodes.Brfalse, notRefedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, port.Refed);
        il.Emit(OpCodes.Call, eventLoop.GetInstance);
        il.Emit(OpCodes.Callvirt, eventLoop.Unref);
        il.MarkLabel(notRefedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// MarkTransferredAcrossThreads() — flags this port (and, recursively, its
    /// partner) as cross-thread once either end has been transferred to a worker.
    /// Mirrors <c>SharpTSMessagePort.MarkTransferredAcrossThreads</c> (#406): a plain
    /// in-process channel never sets this, so its started ports don't Ref the loop
    /// (#1254); once one end is handed to a worker, BOTH ends must Ref while
    /// listening — the worker end via <see cref="CompiledMessagePortBridge"/>'s own
    /// keep-alive, and this (compiled) end via <c>Start()</c>'s gated <c>Ref()</c>.
    /// If this port is already started when the transfer is recorded, Ref it now
    /// since <c>Start()</c> won't run again.
    /// </summary>
    private void EmitMessagePortMarkTransferredAcrossThreads(TypeBuilder typeBuilder, EmittedMessagePortRuntime port)
    {
        var method = typeBuilder.DefineMethod("MarkTransferredAcrossThreads", MethodAttributes.Public, _types.Void, Type.EmptyTypes);

        var il = method.GetILGenerator();
        var exitLabel = il.DefineLabel();
        var skipRefLabel = il.DefineLabel();
        var noPartnerLabel = il.DefineLabel();

        // if (_crossThread) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.CrossThread);
        il.Emit(OpCodes.Brtrue, exitLabel);

        // _crossThread = true;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, port.CrossThread);

        // if (_started && !_closed && !_refed) this.Ref();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Started);
        il.Emit(OpCodes.Brfalse, skipRefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, skipRefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Refed);
        il.Emit(OpCodes.Brtrue, skipRefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, port.Ref);
        il.MarkLabel(skipRefLabel);

        // var partner = _partner as $MessagePort; partner?.MarkTransferredAcrossThreads();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Partner);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, noPartnerLabel);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Br, exitLabel);
        il.MarkLabel(noPartnerLabel);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Start() — begins message delivery: Refs the event loop when this port is
    /// cross-thread (a started, unclosed cross-thread port keeps the process alive,
    /// #406) and schedules a Drain for anything queued before the port started. A
    /// plain in-process port (never transferred, #1254) does not Ref — its queue
    /// drains synchronously and shouldn't keep the process running forever.
    /// </summary>
    private void EmitMessagePortStart(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventLoopRuntime eventLoop)
    {
        var method = typeBuilder.DefineMethod("Start", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        port.Start = method;

        var il = method.GetILGenerator();
        var exitLabel = il.DefineLabel();
        var skipRefLabel = il.DefineLabel();

        // if (_started || _closed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Started);
        il.Emit(OpCodes.Brtrue, exitLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, exitLabel);

        // _started = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, port.Started);

        // if (_crossThread) this.Ref();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.CrossThread);
        il.Emit(OpCodes.Brfalse, skipRefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, port.Ref);
        il.MarkLabel(skipRefLabel);

        // $EventLoop.GetInstance().Schedule(new Action(this.Drain))
        il.Emit(OpCodes.Call, eventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, port.Drain);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, eventLoop.Schedule);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// PostMessage(object msg) — structured-clones the value and enqueues it
    /// to the partner port; delivery is scheduled (async) once the partner
    /// has started.
    /// </summary>
    private void EmitMessagePortPostMessage(
        TypeBuilder typeBuilder,
        EmittedMessagePortRuntime port,
        EmittedEventLoopRuntime eventLoop,
        MethodInfo structuredClone,
        Type dataCloneError)
    {
        var method = typeBuilder.DefineMethod(
            "PostMessage",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var exitLabel = il.DefineLabel();
        var partnerLocal = il.DeclareLocal(typeBuilder);
        var clonedLocal = il.DeclareLocal(_types.Object);

        // if (_closed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, exitLabel);

        // partner = _partner as $MessagePort; if (partner == null || partner._closed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Partner);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, partnerLocal);
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Brfalse, exitLabel);
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, exitLabel);

        // Node model: an uncloneable value is NOT thrown back on the sender — the receiver
        // fires 'messageerror' instead (#1077). $Runtime.StructuredClone throws
        // $DataCloneError for any uncloneable value at ANY nesting depth (#1255); catch it
        // here and enqueue the shared _cloneError sentinel instead of a clone. Drain
        // converts it to 'messageerror'. Mirrors the try/catch around StructuredClone.Clone
        // in SharpTSMessagePort.PostMessage.
        var haveValueLabel = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, structuredClone);
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.Emit(OpCodes.Leave, haveValueLabel);

        il.BeginCatchBlock(dataCloneError);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, port.CloneError);
        il.Emit(OpCodes.Stloc, clonedLocal);
        il.EndExceptionBlock();

        il.MarkLabel(haveValueLabel);

        // partner._pending.Enqueue(cloned)
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Ldfld, port.Pending);
        il.Emit(OpCodes.Ldloc, clonedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConcurrentQueueOfObject, "Enqueue", [_types.Object])!);

        // var cb = partner._onEnqueue; if (cb != null) cb();
        // Wakes a bridge-driven partner (an interpreter worker that adopted this port
        // via CompiledMessagePortBridge) so it drains _pending event-driven rather than
        // polling (#465). The volatile read pairs with the Thread.MemoryBarrier the
        // bridge issues after installing the callback, so a cross-thread post on the
        // parent loop reliably observes it. Null (skipped) for ordinary in-process ports.
        var onEnqueueLocal = il.DeclareLocal(typeof(Action));
        var afterOnEnqueue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, port.OnEnqueue);
        il.Emit(OpCodes.Stloc, onEnqueueLocal);
        il.Emit(OpCodes.Ldloc, onEnqueueLocal);
        il.Emit(OpCodes.Brfalse, afterOnEnqueue);
        il.Emit(OpCodes.Ldloc, onEnqueueLocal);
        il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke", Type.EmptyTypes)!);
        il.MarkLabel(afterOnEnqueue);

        // if (partner._started) $EventLoop.GetInstance().Schedule(new Action(partner.Drain))
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Ldfld, port.Started);
        il.Emit(OpCodes.Brfalse, exitLabel);
        il.Emit(OpCodes.Call, eventLoop.GetInstance);
        il.Emit(OpCodes.Ldloc, partnerLocal);
        il.Emit(OpCodes.Ldftn, port.Drain);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, eventLoop.Schedule);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMessagePortClose(TypeBuilder typeBuilder, EmittedMessagePortRuntime port, EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod("Close", MethodAttributes.Public, _types.Void, Type.EmptyTypes);

        var il = method.GetILGenerator();
        var alreadyClosedLabel = il.DefineLabel();

        // if (_closed) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, port.Closed);
        il.Emit(OpCodes.Brtrue, alreadyClosedLabel);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, port.Closed);

        // this.Unref()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, port.Unref);

        // this.Emit("close", new object[0])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, eventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyClosedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Overrides $EventEmitter.OnListenerAdded: registering a 'message'
    /// listener implicitly starts the port (Node worker_threads semantics).
    /// Covers on/once/addListener/prepend* — they all funnel through
    /// AddListenerInternal, which Callvirts this hook.
    /// </summary>
    private void EmitMessagePortOnListenerAdded(
        TypeBuilder typeBuilder,
        EmittedMessagePortRuntime port,
        EmittedEventEmitterRuntime eventEmitter)
    {
        var method = typeBuilder.DefineMethod(
            "OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );
        typeBuilder.DefineMethodOverride(method, eventEmitter.OnListenerAdded);

        var il = method.GetILGenerator();
        var exitLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, exitLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, port.Start);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMessageChannelClass(ModuleBuilder moduleBuilder, EmittedMessageChannelRuntime channels)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$MessageChannel",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit
        );

        var port1Field = typeBuilder.DefineField("_port1", _types.Object, FieldAttributes.Private);
        var port2Field = typeBuilder.DefineField("_port2", _types.Object, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        {
            var il = ctor.GetILGenerator();
            var p1Local = il.DeclareLocal(channels.Port.Type);
            var p2Local = il.DeclareLocal(channels.Port.Type);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

            // p1 = new $MessagePort(); p2 = new $MessagePort()
            il.Emit(OpCodes.Newobj, channels.Port.Ctor);
            il.Emit(OpCodes.Stloc, p1Local);
            il.Emit(OpCodes.Newobj, channels.Port.Ctor);
            il.Emit(OpCodes.Stloc, p2Local);

            // p1._partner = p2; p2._partner = p1
            il.Emit(OpCodes.Ldloc, p1Local);
            il.Emit(OpCodes.Ldloc, p2Local);
            il.Emit(OpCodes.Stfld, channels.Port.Partner);
            il.Emit(OpCodes.Ldloc, p2Local);
            il.Emit(OpCodes.Ldloc, p1Local);
            il.Emit(OpCodes.Stfld, channels.Port.Partner);

            // _port1 = p1; _port2 = p2
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, p1Local);
            il.Emit(OpCodes.Stfld, port1Field);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, p2Local);
            il.Emit(OpCodes.Stfld, port2Field);

            il.Emit(OpCodes.Ret);
        }

        // Port1/Port2 getter properties — `channel.port1` resolves via the
        // case-insensitive PascalCase reflection fallback in GetFieldsProperty
        // (same mechanism as $BroadcastChannel's `bc.name` → get_Name).
        EmitReadOnlyObjectProperty(typeBuilder, "Port1", port1Field);
        EmitReadOnlyObjectProperty(typeBuilder, "Port2", port2Field);

        typeBuilder.CreateType();

        channels.Type = typeBuilder;
        channels.Ctor = ctor;
    }


    private void EmitReadOnlyObjectProperty(TypeBuilder typeBuilder, string propertyName, FieldBuilder backingField)
    {
        var getter = typeBuilder.DefineMethod(
            "get_" + propertyName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, backingField);
        il.Emit(OpCodes.Ret);

        var prop = typeBuilder.DefineProperty(propertyName, PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        prop.SetGetMethod(getter);
    }

    /// <summary>
    /// $Runtime.CreateMessageChannel() — kept as the public construction entry
    /// (TryEmitBuiltInConstructor calls it for `new MessageChannel()`).
    /// </summary>
    private void EmitCreateMessageChannelHelper(TypeBuilder runtimeType, EmittedMessageChannelRuntime channels)
    {
        var method = runtimeType.DefineMethod(
            "CreateMessageChannel",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, channels.Ctor);
        il.Emit(OpCodes.Ret);

        channels.Create = method;
    }
}
