using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits $FsReadStream and $FsWriteStream types and their factory methods
/// for compiled fs.createReadStream/createWriteStream support.
/// </summary>
public partial class RuntimeEmitter
{
    // Stream type builders (module-level emitted types)
    private TypeBuilder _fsReadStreamType = null!;
    private TypeBuilder _fsWriteStreamType = null!;

    // $FsReadStream fields
    private FieldBuilder _rsPathField = null!;
    private FieldBuilder _rsDataField = null!;
    private FieldBuilder _rsBytesReadField = null!;
    // #980: replay state — open/ready/close are emitted at creation (before user
    // listeners), so OnListenerAdded re-emits them to late listeners (no event loop).
    private FieldBuilder _rsEmitCloseField = null!;
    private FieldBuilder _rsFdNumField = null!;
    private FieldBuilder _rsPendingField = null!;

    // $FsWriteStream fields
    private FieldBuilder _wsPathField = null!;
    private FieldBuilder _wsStreamField = null!;
    private FieldBuilder _wsBytesWrittenField = null!;

    // Method builders for cross-type references
    private MethodBuilder _wsWriteMethod = null!;
    private MethodBuilder _wsEndMethod = null!;

    /// <summary>
    /// Phase 1: Define $FsReadStream/$FsWriteStream types, fields, methods, and create them.
    /// Must be called before EmitRuntimeClass and after TSFunction is defined.
    /// </summary>
    private void EmitFsStreamTypeDefinitions(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        DefineFsReadStreamType(moduleBuilder, runtime);
        DefineFsWriteStreamType(moduleBuilder, runtime);
        EmitFsWriteStreamMethods(runtime);  // Must come before ReadStream methods (Pipe needs _wsWriteMethod)
        EmitFsReadStreamMethods(runtime);
        _fsReadStreamType.CreateType();
        _fsWriteStreamType.CreateType();
    }

    /// <summary>
    /// Phase 2: Emit static factory methods on the runtime type.
    /// Must be called during/after EmitRuntimeClass.
    /// </summary>
    private void EmitFsStreamFactories(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitFsCreateReadStreamFactory(runtimeType, runtime);
        EmitFsCreateWriteStreamFactory(runtimeType, runtime);
    }

    #region $FsReadStream

    private void DefineFsReadStreamType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _fsReadStreamType = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FsReadStream",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSReadableType  // Extends $Readable instead of object
        );

        // Fields. _data holds the whole content (for the Pipe override); the factory
        // pushes highWaterMark chunks into $Readable's buffer for 'data' events.
        _rsPathField = _fsReadStreamType.DefineField("_path", _types.String, FieldAttributes.Private);
        _rsDataField = _fsReadStreamType.DefineField("_data", _types.Object, FieldAttributes.Private);
        _rsBytesReadField = _fsReadStreamType.DefineField("_bytesRead", _types.Double, FieldAttributes.Private);
        _rsEmitCloseField = _fsReadStreamType.DefineField("_emitClose", _types.Boolean, FieldAttributes.Private);
        _rsFdNumField = _fsReadStreamType.DefineField("_fdNum", _types.Double, FieldAttributes.Private);
        _rsPendingField = _fsReadStreamType.DefineField("_pending", _types.Boolean, FieldAttributes.Private);

        // Constructor: $FsReadStream(string path, object data, bool emitClose, double fdNum, double bytesRead).
        // Stores fields (no push — the factory pushes chunks then null). The read is
        // eager, so open/ready/close are "ready" immediately; OnListenerAdded replays
        // them to late listeners (#980).
        var ctor = _fsReadStreamType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.Object, _types.Boolean, _types.Double, _types.Double]
        );
        var il = ctor.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSReadableCtor);
        void Store(int arg, FieldBuilder f) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg, arg); il.Emit(OpCodes.Stfld, f); }
        Store(1, _rsPathField);
        Store(2, _rsDataField);
        Store(3, _rsEmitCloseField);
        Store(4, _rsFdNumField);
        Store(5, _rsBytesReadField);
        // _pending = false (eager read => already ready)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _rsPendingField);
        il.Emit(OpCodes.Ret);

        // OnListenerAdded override: base handles data/end replay; we replay open/ready/close.
        EmitFsReadStreamOnListenerAdded(runtime);

        // Property: Path (string)
        var pathProp = _fsReadStreamType.DefineProperty("Path", PropertyAttributes.None, _types.String, null);
        var pathGetter = _fsReadStreamType.DefineMethod("get_Path",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.String, Type.EmptyTypes);
        var pil = pathGetter.GetILGenerator();
        pil.Emit(OpCodes.Ldarg_0);
        pil.Emit(OpCodes.Ldfld, _rsPathField);
        pil.Emit(OpCodes.Ret);
        pathProp.SetGetMethod(pathGetter);

        // Property: BytesRead (object - returns boxed double)
        var brProp = _fsReadStreamType.DefineProperty("BytesRead", PropertyAttributes.None, _types.Object, null);
        var brGetter = _fsReadStreamType.DefineMethod("get_BytesRead",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.Object, Type.EmptyTypes);
        var bril = brGetter.GetILGenerator();
        bril.Emit(OpCodes.Ldarg_0);
        bril.Emit(OpCodes.Ldfld, _rsBytesReadField);
        bril.Emit(OpCodes.Box, _types.Double);
        bril.Emit(OpCodes.Ret);
        brProp.SetGetMethod(brGetter);

        // Property: Pending (object - boxed bool)
        var pendProp = _fsReadStreamType.DefineProperty("Pending", PropertyAttributes.None, _types.Object, null);
        var pendGetter = _fsReadStreamType.DefineMethod("get_Pending",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.Object, Type.EmptyTypes);
        var pendil = pendGetter.GetILGenerator();
        pendil.Emit(OpCodes.Ldarg_0);
        pendil.Emit(OpCodes.Ldfld, _rsPendingField);
        pendil.Emit(OpCodes.Box, _types.Boolean);
        pendil.Emit(OpCodes.Ret);
        pendProp.SetGetMethod(pendGetter);
    }

    /// <summary>
    /// Overrides OnListenerAdded: delegates to base $Readable (data/end replay), then
    /// re-emits open/ready/close to a late listener (the read is eager so these are
    /// always ready). 'close' honors emitClose. No event loop — pure replay (#980).
    /// </summary>
    private void EmitFsReadStreamOnListenerAdded(EmittedRuntime runtime)
    {
        var method = _fsReadStreamType.DefineMethod("OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual, _types.Void, [_types.String]);
        var il = method.GetILGenerator();
        var strEquals = _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!;
        var baseOLA = runtime.TSReadableType.GetMethod("OnListenerAdded", [_types.String])!;

        // base.OnListenerAdded(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, baseOLA);

        void EmitEvent(Action loadArgs)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1); // reuse the matched name string
            loadArgs();
            il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);
        }

        var ret = il.DefineLabel();
        var notOpen = il.DefineLabel();
        var notReady = il.DefineLabel();

        // if (name == "open") emit("open", [fdNum])
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "open"); il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brfalse, notOpen);
        EmitEvent(() =>
        {
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _rsFdNumField); il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Br, ret);
        il.MarkLabel(notOpen);

        // if (name == "ready") emit("ready", [])
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "ready"); il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brfalse, notReady);
        EmitEvent(() => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); });
        il.Emit(OpCodes.Br, ret);
        il.MarkLabel(notReady);

        // if (name == "close" && _emitClose) emit("close", [])
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "close"); il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _rsEmitCloseField);
        il.Emit(OpCodes.Brfalse, ret);
        EmitEvent(() => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); });

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFsReadStreamMethods(EmittedRuntime runtime)
    {
        // Override Pipe to handle $FsWriteStream (which doesn't extend $Writable)
        EmitFsReadStreamPipeOverride(runtime);
    }

    /// <summary>
    /// Override Pipe on $FsReadStream: first tries the base $Readable.Pipe behavior (for $Writable/$Duplex),
    /// then falls back to calling Write/End directly on $FsWriteStream.
    /// </summary>
    private void EmitFsReadStreamPipeOverride(EmittedRuntime runtime)
    {
        var method = _fsReadStreamType.DefineMethod("Pipe",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
            _types.Object, [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var basePathLabel = il.DefineLabel();

        // Check if dest is $FsWriteStream
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _fsWriteStreamType);
        il.Emit(OpCodes.Brfalse, basePathLabel);

        // $FsWriteStream path: write _data directly, then end
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _fsWriteStreamType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _rsDataField);
        il.Emit(OpCodes.Callvirt, _wsWriteMethod);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _fsWriteStreamType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _wsEndMethod);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        // For all other destinations: delegate to base $Readable.Pipe
        il.MarkLabel(basePathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSReadablePipe);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region $FsWriteStream

    // #980: $FsWriteStream fields (reparented onto $EventEmitter)
    private FieldBuilder _wsAutoCloseField = null!;
    private FieldBuilder _wsEmitCloseField = null!;
    private FieldBuilder _wsFdNumField = null!;
    private FieldBuilder _wsPendingField = null!;
    private FieldBuilder _wsClosedField = null!;

    private void DefineFsWriteStreamType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Reparent onto $EventEmitter so it's a real emitter (on/once/emit) with
        // open/ready/finish/close events (#980).
        _fsWriteStreamType = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FsWriteStream",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );

        // Fields
        _wsPathField = _fsWriteStreamType.DefineField("_path", _types.String, FieldAttributes.Private);
        _wsStreamField = _fsWriteStreamType.DefineField("_stream", typeof(FileStream), FieldAttributes.Private);
        _wsBytesWrittenField = _fsWriteStreamType.DefineField("_bytesWritten", _types.Double, FieldAttributes.Private);
        _wsAutoCloseField = _fsWriteStreamType.DefineField("_autoClose", _types.Boolean, FieldAttributes.Private);
        _wsEmitCloseField = _fsWriteStreamType.DefineField("_emitClose", _types.Boolean, FieldAttributes.Private);
        _wsFdNumField = _fsWriteStreamType.DefineField("_fdNum", _types.Double, FieldAttributes.Private);
        _wsPendingField = _fsWriteStreamType.DefineField("_pending", _types.Boolean, FieldAttributes.Private);
        _wsClosedField = _fsWriteStreamType.DefineField("_closed", _types.Boolean, FieldAttributes.Private);

        // Constructor: $FsWriteStream(path, flags, autoClose, emitClose, fd, start)
        var ctor = _fsWriteStreamType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.String, _types.Boolean, _types.Boolean, _types.Object, _types.Double]
        );
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, _wsPathField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stfld, _wsAutoCloseField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg, 4); il.Emit(OpCodes.Stfld, _wsEmitCloseField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Stfld, _wsBytesWrittenField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, _wsPendingField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, _wsClosedField);

        // Open: fd ? table.Get + fdNum : new FileStream(path, mode-from-flags, …); seek start
        var fdLabel = il.DefineLabel(); var afterOpen = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 5); il.Emit(OpCodes.Brfalse, fdLabel); // arg5 = fd (object); brfalse if null
        // fd path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
        il.Emit(OpCodes.Ldarg, 5); il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
        il.Emit(OpCodes.Stfld, _wsStreamField);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg, 5); il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!); il.Emit(OpCodes.Stfld, _wsFdNumField);
        il.Emit(OpCodes.Br, afterOpen);
        il.MarkLabel(fdLabel);
        // path path: mode = flags=="a"/"ax"/"a+" ? Append : flags=="wx" ? CreateNew : flags=="r+" ? Open : Create
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1); // path
        EmitFlagsToFileMode(il); // pushes FileMode int from flags (arg2)
        il.Emit(OpCodes.Ldc_I4, (int)FileAccess.Write);
        il.Emit(OpCodes.Ldc_I4, (int)FileShare.ReadWrite);
        il.Emit(OpCodes.Newobj, typeof(FileStream).GetConstructor([typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!);
        il.Emit(OpCodes.Stfld, _wsStreamField);
        // fdNum placeholder for path-opened streams (the exact fd isn't observable here).
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Stfld, _wsFdNumField);
        il.MarkLabel(afterOpen);
        // seek start if > 0 (arg6)
        var noSeek = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Ble, noSeek);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsStreamField);
        il.Emit(OpCodes.Ldarg, 6); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(FileStream).GetMethod("Seek")!); il.Emit(OpCodes.Pop);
        il.MarkLabel(noSeek);
        il.Emit(OpCodes.Ret);

        // Property: Path (string)
        var pathProp = _fsWriteStreamType.DefineProperty("Path", PropertyAttributes.None, _types.String, null);
        var pathGetter = _fsWriteStreamType.DefineMethod("get_Path",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.String, Type.EmptyTypes);
        var pil = pathGetter.GetILGenerator();
        pil.Emit(OpCodes.Ldarg_0);
        pil.Emit(OpCodes.Ldfld, _wsPathField);
        pil.Emit(OpCodes.Ret);
        pathProp.SetGetMethod(pathGetter);

        // Property: BytesWritten (object - returns boxed double)
        var bwProp = _fsWriteStreamType.DefineProperty("BytesWritten", PropertyAttributes.None, _types.Object, null);
        var bwGetter = _fsWriteStreamType.DefineMethod("get_BytesWritten",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.Object, Type.EmptyTypes);
        var bwil = bwGetter.GetILGenerator();
        bwil.Emit(OpCodes.Ldarg_0);
        bwil.Emit(OpCodes.Ldfld, _wsBytesWrittenField);
        bwil.Emit(OpCodes.Box, _types.Double);
        bwil.Emit(OpCodes.Ret);
        bwProp.SetGetMethod(bwGetter);

        // Property: Pending (object - boxed bool)
        var wpProp = _fsWriteStreamType.DefineProperty("Pending", PropertyAttributes.None, _types.Object, null);
        var wpGetter = _fsWriteStreamType.DefineMethod("get_Pending",
            MethodAttributes.Public | MethodAttributes.SpecialName, _types.Object, Type.EmptyTypes);
        var wpil = wpGetter.GetILGenerator();
        wpil.Emit(OpCodes.Ldarg_0);
        wpil.Emit(OpCodes.Ldfld, _wsPendingField);
        wpil.Emit(OpCodes.Box, _types.Boolean);
        wpil.Emit(OpCodes.Ret);
        wpProp.SetGetMethod(wpGetter);

        // OnListenerAdded override: replay open/ready (always ready since opened at ctor)
        // and close (once finished). finish/close fire synchronously in End.
        EmitFsWriteStreamOnListenerAdded(runtime);

        // Define method stubs (bodies emitted later for cross-type references)
        _wsWriteMethod = _fsWriteStreamType.DefineMethod("Write",
            MethodAttributes.Public, _types.Object, [_types.Object]);
        _wsEndMethod = _fsWriteStreamType.DefineMethod("End",
            MethodAttributes.Public, _types.Object, [_types.Object]);
    }

    /// <summary>Pushes a FileMode int derived from the flags string in arg2.</summary>
    private static void EmitFlagsToFileMode(ILGenerator il)
    {
        var strEq = typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!;
        var append = il.DefineLabel(); var createNew = il.DefineLabel(); var open = il.DefineLabel(); var done = il.DefineLabel();
        void Cmp(string s, System.Reflection.Emit.Label target) { il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldstr, s); il.Emit(OpCodes.Call, strEq); il.Emit(OpCodes.Brtrue, target); }
        Cmp("a", append); Cmp("ax", append); Cmp("a+", append);
        Cmp("wx", createNew); Cmp("r+", open);
        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Create); il.Emit(OpCodes.Br, done);
        il.MarkLabel(append); il.Emit(OpCodes.Ldc_I4, (int)FileMode.Append); il.Emit(OpCodes.Br, done);
        il.MarkLabel(createNew); il.Emit(OpCodes.Ldc_I4, (int)FileMode.CreateNew); il.Emit(OpCodes.Br, done);
        il.MarkLabel(open); il.Emit(OpCodes.Ldc_I4, (int)FileMode.Open);
        il.MarkLabel(done);
    }

    /// <summary>OnListenerAdded override for $FsWriteStream: replay open/ready always,
    /// close once finished (emitClose-gated). finish/close fire in End().</summary>
    private void EmitFsWriteStreamOnListenerAdded(EmittedRuntime runtime)
    {
        var method = _fsWriteStreamType.DefineMethod("OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual, _types.Void, [_types.String]);
        var il = method.GetILGenerator();
        var strEq = typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!;

        void EmitEvent(Action loadArgs) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); loadArgs(); il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit); il.Emit(OpCodes.Pop); }
        var ret = il.DefineLabel(); var notOpen = il.DefineLabel(); var notReady = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "open"); il.Emit(OpCodes.Call, strEq); il.Emit(OpCodes.Brfalse, notOpen);
        EmitEvent(() => { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Newarr, _types.Object); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsFdNumField); il.Emit(OpCodes.Box, _types.Double); il.Emit(OpCodes.Stelem_Ref); });
        il.Emit(OpCodes.Br, ret);
        il.MarkLabel(notOpen);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "ready"); il.Emit(OpCodes.Call, strEq); il.Emit(OpCodes.Brfalse, notReady);
        EmitEvent(() => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); });
        il.Emit(OpCodes.Br, ret);
        il.MarkLabel(notReady);
        // close: only if already closed && emitClose
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "close"); il.Emit(OpCodes.Call, strEq); il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsClosedField); il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsEmitCloseField); il.Emit(OpCodes.Brfalse, ret);
        EmitEvent(() => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object); });
        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFsWriteStreamMethods(EmittedRuntime runtime)
    {
        EmitFsWriteStreamWriteBody(runtime);
        EmitFsWriteStreamEndBody(runtime);
        // On/once/emit are inherited from $EventEmitter now (no stub).
    }

    private void EmitFsWriteStreamWriteBody(EmittedRuntime runtime)
    {
        // public object Write(object data) - writes byte[]/$Buffer/string to FileStream
        var il = _wsWriteMethod.GetILGenerator();

        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var afterConvertLabel = il.DefineLabel();

        // if (data is byte[]) bytes = (byte[])data
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(byte[]));
        var notBytesLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBytesLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(byte[]));
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        // else if (data is $Buffer) bytes = data.GetData()
        il.MarkLabel(notBytesLabel);
        var notBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, notBufferLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        // else: bytes = Encoding.UTF8.GetBytes(data.ToString())
        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        il.MarkLabel(afterConvertLabel);

        // _stream.Write(bytes, 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _wsStreamField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Write", [typeof(byte[]), typeof(int), typeof(int)])!);

        // _bytesWritten += bytes.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _wsBytesWrittenField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _wsBytesWrittenField);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFsWriteStreamEndBody(EmittedRuntime runtime)
    {
        // public object End(object? data): write final data, flush/close, emit finish→close.
        var il = _wsEndMethod.GetILGenerator();
        var skipWriteLabel = il.DefineLabel();

        // if (data != null) Write(data)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skipWriteLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _wsWriteMethod);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipWriteLabel);

        // _stream.Flush(); if (_autoClose) _stream.Close();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _wsStreamField);
        il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Flush", Type.EmptyTypes)!);
        var noCloseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsAutoCloseField); il.Emit(OpCodes.Brfalse, noCloseLabel);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsStreamField);
        il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Close", Type.EmptyTypes)!);
        il.MarkLabel(noCloseLabel);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, _wsClosedField);

        void Emit(string name)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit); il.Emit(OpCodes.Pop);
        }
        // emit 'finish'; if (_emitClose) emit 'close'
        Emit("finish");
        var noEmitClose = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _wsEmitCloseField); il.Emit(OpCodes.Brfalse, noEmitClose);
        Emit("close");
        il.MarkLabel(noEmitClose);

        // return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Factory Methods

    private void EmitFsCreateReadStreamFactory(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // public static object FsCreateReadStream(object path, object? options)
        var method = runtimeType.DefineMethod("FsCreateReadStream",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.FsCreateReadStream = method;

        var il = method.GetILGenerator();
        var utf8Get = typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!;
        var utf8GetString = typeof(System.Text.Encoding).GetMethod("GetString", [typeof(byte[])])!;
        var arrayCopy = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!;
        var fsRead = typeof(FileStream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!;

        var pathStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, pathStrLocal);

        // --- option parsing (defaults when options is null) ---
        LocalBuilder RawOpt(string key)
        {
            var loc = il.DeclareLocal(_types.Object);
            var noOpt = il.DefineLabel(); var done = il.DefineLabel(); var notUndef = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, noOpt);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, key); il.Emit(OpCodes.Call, runtime.GetProperty);
            // GetProperty returns $Undefined (not null) for an absent key — normalize to null.
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Isinst, runtime.UndefinedType); il.Emit(OpCodes.Brfalse, notUndef);
            il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldnull);
            il.MarkLabel(notUndef);
            il.Emit(OpCodes.Stloc, loc);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(noOpt); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, loc);
            il.MarkLabel(done); return loc;
        }
        LocalBuilder NumOpt(LocalBuilder raw, double dflt)
        {
            var loc = il.DeclareLocal(_types.Double);
            var useDflt = il.DefineLabel(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Call, runtime.ToNumber); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDflt); il.Emit(OpCodes.Ldc_R8, dflt);
            il.MarkLabel(done); il.Emit(OpCodes.Stloc, loc); return loc;
        }
        LocalBuilder BoolOpt(LocalBuilder raw, bool dflt)
        {
            var loc = il.DeclareLocal(_types.Boolean);
            var useDflt = il.DefineLabel(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Isinst, _types.Boolean); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Unbox_Any, _types.Boolean); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDflt); il.Emit(dflt ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.MarkLabel(done); il.Emit(OpCodes.Stloc, loc); return loc;
        }

        var encodingLocal = RawOpt("encoding");
        var fdLocal = RawOpt("fd");
        var startLocal = NumOpt(RawOpt("start"), 0.0);
        var endLocal = NumOpt(RawOpt("end"), -1.0);
        var chunkLocal = NumOpt(RawOpt("highWaterMark"), 65536.0);
        var emitCloseLocal = BoolOpt(RawOpt("emitClose"), true);
        var autoCloseLocal = BoolOpt(RawOpt("autoClose"), true);

        // --- open: fd ? table.Get : new FileStream(path) ---
        var fsLocal = il.DeclareLocal(typeof(FileStream));
        var fdNumLocal = il.DeclareLocal(_types.Double);
        var pathOpen = il.DefineLabel(); var afterOpen = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fdLocal); il.Emit(OpCodes.Brfalse, pathOpen);
        il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
        il.Emit(OpCodes.Ldloc, fdLocal); il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
        il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet); il.Emit(OpCodes.Stloc, fsLocal);
        il.Emit(OpCodes.Ldloc, fdLocal); il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!); il.Emit(OpCodes.Stloc, fdNumLocal);
        il.Emit(OpCodes.Br, afterOpen);
        il.MarkLabel(pathOpen);
        il.Emit(OpCodes.Ldloc, pathStrLocal);
        il.Emit(OpCodes.Ldc_I4, (int)FileMode.Open); il.Emit(OpCodes.Ldc_I4, (int)FileAccess.Read); il.Emit(OpCodes.Ldc_I4, (int)FileShare.ReadWrite);
        il.Emit(OpCodes.Newobj, typeof(FileStream).GetConstructor([typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!);
        il.Emit(OpCodes.Stloc, fsLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Stloc, fdNumLocal);
        il.MarkLabel(afterOpen);

        // seek start if > 0
        var noSeek = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Ble, noSeek);
        il.Emit(OpCodes.Ldloc, fsLocal); il.Emit(OpCodes.Ldloc, startLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(FileStream).GetMethod("Seek")!); il.Emit(OpCodes.Pop);
        il.MarkLabel(noSeek);

        // remaining = end<0 ? Length-start : end-start+1; clamp >= 0
        var remLocal = il.DeclareLocal(_types.Int64);
        var endSet = il.DefineLabel(); var afterRem = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Bge, endSet);
        il.Emit(OpCodes.Ldloc, fsLocal); il.Emit(OpCodes.Callvirt, typeof(FileStream).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, remLocal); il.Emit(OpCodes.Br, afterRem);
        il.MarkLabel(endSet);
        il.Emit(OpCodes.Ldloc, endLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ldloc, startLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, remLocal);
        il.MarkLabel(afterRem);
        var remOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Bge, remOk);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Stloc, remLocal);
        il.MarkLabel(remOk);

        // bytes = new byte[(int)remaining]; read loop
        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var totalLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, totalLocal);
        var rl = il.DefineLabel(); var rd = il.DefineLabel();
        il.MarkLabel(rl);
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Bge, rd);
        il.Emit(OpCodes.Ldloc, fsLocal); il.Emit(OpCodes.Ldloc, bytesLocal); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, fsRead);
        var nLocal = il.DeclareLocal(_types.Int32); il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Brfalse, rd);
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldloc, nLocal); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, totalLocal);
        il.Emit(OpCodes.Br, rl);
        il.MarkLabel(rd);
        // trim bytes to total
        var noTrim = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ldloc, remLocal); il.Emit(OpCodes.Beq, noTrim);
        var trimmed = il.DeclareLocal(typeof(byte[]));
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, trimmed);
        il.Emit(OpCodes.Ldloc, bytesLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, trimmed); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Call, arrayCopy);
        il.Emit(OpCodes.Ldloc, trimmed); il.Emit(OpCodes.Stloc, bytesLocal);
        il.MarkLabel(noTrim);

        // if (autoClose && fd == null) fs.Dispose()
        var noClose = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, autoCloseLocal); il.Emit(OpCodes.Brfalse, noClose);
        il.Emit(OpCodes.Ldloc, fdLocal); il.Emit(OpCodes.Brtrue, noClose);
        il.Emit(OpCodes.Ldloc, fsLocal); il.Emit(OpCodes.Callvirt, typeof(FileStream).GetMethod("Dispose", Type.EmptyTypes)!);
        il.MarkLabel(noClose);

        // whole = encoding != null ? UTF8.GetString(bytes) : new $Buffer(bytes)
        var wholeLocal = il.DeclareLocal(_types.Object);
        var wbin = il.DefineLabel(); var wdone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, encodingLocal); il.Emit(OpCodes.Brfalse, wbin);
        il.Emit(OpCodes.Call, utf8Get); il.Emit(OpCodes.Ldloc, bytesLocal); il.Emit(OpCodes.Callvirt, utf8GetString); il.Emit(OpCodes.Br, wdone);
        il.MarkLabel(wbin); il.Emit(OpCodes.Ldloc, bytesLocal); il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.MarkLabel(wdone); il.Emit(OpCodes.Stloc, wholeLocal);

        // stream = new $FsReadStream(path, whole, emitClose, fdNum, (double)total)
        var streamLocal = il.DeclareLocal(_fsReadStreamType);
        var rsCtor = _fsReadStreamType.GetConstructors()[0];
        il.Emit(OpCodes.Ldloc, pathStrLocal); il.Emit(OpCodes.Ldloc, wholeLocal); il.Emit(OpCodes.Ldloc, emitCloseLocal); il.Emit(OpCodes.Ldloc, fdNumLocal); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Newobj, rsCtor); il.Emit(OpCodes.Stloc, streamLocal);

        // push highWaterMark chunks: off=0; while (off<total) { sl=min(chunk,total-off); slice; stream.Push(chunk); off+=sl }
        var offLocal = il.DeclareLocal(_types.Int32);
        var chunkSzLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, chunkLocal); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stloc, chunkSzLocal);
        // guard chunkSz <= 0 -> total (single chunk)
        var chunkOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, chunkSzLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Bgt, chunkOk);
        il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!); il.Emit(OpCodes.Stloc, chunkSzLocal);
        il.MarkLabel(chunkOk);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, offLocal);
        var pl = il.DefineLabel(); var pd = il.DefineLabel();
        il.MarkLabel(pl);
        il.Emit(OpCodes.Ldloc, offLocal); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Bge, pd);
        var slLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, chunkSzLocal); il.Emit(OpCodes.Ldloc, totalLocal); il.Emit(OpCodes.Ldloc, offLocal); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!); il.Emit(OpCodes.Stloc, slLocal);
        var sliceLocal = il.DeclareLocal(typeof(byte[]));
        il.Emit(OpCodes.Ldloc, slLocal); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, sliceLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal); il.Emit(OpCodes.Ldloc, offLocal); il.Emit(OpCodes.Ldloc, sliceLocal); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, slLocal); il.Emit(OpCodes.Call, arrayCopy);
        // stream.Push(encoding ? UTF8.GetString(slice) : new $Buffer(slice))
        il.Emit(OpCodes.Ldloc, streamLocal);
        var cbin = il.DefineLabel(); var cdone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, encodingLocal); il.Emit(OpCodes.Brfalse, cbin);
        il.Emit(OpCodes.Call, utf8Get); il.Emit(OpCodes.Ldloc, sliceLocal); il.Emit(OpCodes.Callvirt, utf8GetString); il.Emit(OpCodes.Br, cdone);
        il.MarkLabel(cbin); il.Emit(OpCodes.Ldloc, sliceLocal); il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.MarkLabel(cdone);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, offLocal); il.Emit(OpCodes.Ldloc, slLocal); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, offLocal);
        il.Emit(OpCodes.Br, pl);
        il.MarkLabel(pd);
        // stream.Push(null) -> end
        il.Emit(OpCodes.Ldloc, streamLocal); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, runtime.TSReadablePush); il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFsCreateWriteStreamFactory(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // public static object FsCreateWriteStream(object path, object? options)
        var method = runtimeType.DefineMethod("FsCreateWriteStream",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.FsCreateWriteStream = method;

        var il = method.GetILGenerator();

        var pathStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, pathStrLocal);

        // --- option parsing (defaults when options null) ---
        LocalBuilder RawOpt(string key)
        {
            var loc = il.DeclareLocal(_types.Object);
            var noOpt = il.DefineLabel(); var done = il.DefineLabel(); var notUndef = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brfalse, noOpt);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, key); il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Isinst, runtime.UndefinedType); il.Emit(OpCodes.Brfalse, notUndef);
            il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldnull);
            il.MarkLabel(notUndef); il.Emit(OpCodes.Stloc, loc); il.Emit(OpCodes.Br, done);
            il.MarkLabel(noOpt); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stloc, loc);
            il.MarkLabel(done); return loc;
        }
        LocalBuilder StrOpt(LocalBuilder raw, string dflt)
        {
            var loc = il.DeclareLocal(_types.String);
            var useDflt = il.DefineLabel(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Stloc, loc); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDflt); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldstr, dflt); il.Emit(OpCodes.Stloc, loc);
            il.MarkLabel(done); return loc;
        }
        LocalBuilder NumOpt(LocalBuilder raw, double dflt)
        {
            var loc = il.DeclareLocal(_types.Double);
            var useDflt = il.DefineLabel(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Call, runtime.ToNumber); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDflt); il.Emit(OpCodes.Ldc_R8, dflt);
            il.MarkLabel(done); il.Emit(OpCodes.Stloc, loc); return loc;
        }
        LocalBuilder BoolOpt(LocalBuilder raw, bool dflt)
        {
            var loc = il.DeclareLocal(_types.Boolean);
            var useDflt = il.DefineLabel(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Isinst, _types.Boolean); il.Emit(OpCodes.Brfalse, useDflt);
            il.Emit(OpCodes.Ldloc, raw); il.Emit(OpCodes.Unbox_Any, _types.Boolean); il.Emit(OpCodes.Br, done);
            il.MarkLabel(useDflt); il.Emit(dflt ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.MarkLabel(done); il.Emit(OpCodes.Stloc, loc); return loc;
        }

        var flagsLocal = StrOpt(RawOpt("flags"), "w");
        var fdLocal = RawOpt("fd");
        var autoCloseLocal = BoolOpt(RawOpt("autoClose"), true);
        var emitCloseLocal = BoolOpt(RawOpt("emitClose"), true);
        var startLocal = NumOpt(RawOpt("start"), 0.0);

        // return new $FsWriteStream(path, flags, autoClose, emitClose, fd, start)
        var wsCtor = _fsWriteStreamType.GetConstructors()[0];
        il.Emit(OpCodes.Ldloc, pathStrLocal);
        il.Emit(OpCodes.Ldloc, flagsLocal);
        il.Emit(OpCodes.Ldloc, autoCloseLocal);
        il.Emit(OpCodes.Ldloc, emitCloseLocal);
        il.Emit(OpCodes.Ldloc, fdLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Newobj, wsCtor);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
