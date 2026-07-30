using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $FileDescriptorTable type for standalone DLLs.
    /// This replaces the reflection-based FileDescriptorTable access.
    /// </summary>
    /// <remarks>
    /// The emitted type provides:
    /// - Static Instance field (singleton)
    /// - Open(path, mode, access, share) -> int fd
    /// - Get(fd) -> FileStream
    /// - Close(fd) -> void
    /// - IsValid(fd) -> bool
    /// </remarks>
    private void EmitFileDescriptorTableType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$FileDescriptorTable",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        _ = typeBuilder;

        // Fields
        var nextFdField = typeBuilder.DefineField(
            "_nextFd",
            _types.Int32,
            FieldAttributes.Private
        );

        var streamsType = typeof(ConcurrentDictionary<int, FileStream>);
        var streamsField = typeBuilder.DefineField(
            "_streams",
            streamsType,
            FieldAttributes.Private | FieldAttributes.InitOnly
        );

        // Static Instance field
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.FileDescriptorTableInstance = instanceField;

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        _ = ctor;

        var ctorIl = ctor.GetILGenerator();

        // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _nextFd = 3 (0-2 reserved for stdin/stdout/stderr)
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_3);
        ctorIl.Emit(OpCodes.Stfld, nextFdField);

        // _streams = new ConcurrentDictionary<int, FileStream>()
        ctorIl.Emit(OpCodes.Ldarg_0);
        var streamsDictCtor = streamsType.GetConstructor(Type.EmptyTypes)!;
        ctorIl.Emit(OpCodes.Newobj, streamsDictCtor);
        ctorIl.Emit(OpCodes.Stfld, streamsField);

        ctorIl.Emit(OpCodes.Ret);

        // Static constructor to initialize Instance
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIl = cctor.GetILGenerator();

        // Instance = new $FileDescriptorTable()
        cctorIl.Emit(OpCodes.Newobj, ctor);
        cctorIl.Emit(OpCodes.Stsfld, instanceField);
        cctorIl.Emit(OpCodes.Ret);

        // Open method
        EmitFileDescriptorTableOpen(typeBuilder, runtime, nextFdField, streamsField, streamsType);

        // Get method
        EmitFileDescriptorTableGet(typeBuilder, runtime, streamsField, streamsType);

        // Close method
        EmitFileDescriptorTableClose(typeBuilder, runtime, streamsField, streamsType);

        // IsValid method
        EmitFileDescriptorTableIsValid(typeBuilder, runtime, streamsField, streamsType);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public int Open(string path, FileMode mode, FileAccess access, FileShare share)
    /// </summary>
    private void EmitFileDescriptorTableOpen(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder nextFdField,
        FieldBuilder streamsField,
        Type streamsType)
    {
        var method = typeBuilder.DefineMethod(
            "Open",
            MethodAttributes.Public,
            _types.Int32,
            [_types.String, typeof(FileMode), typeof(FileAccess), typeof(FileShare)]
        );
        runtime.FileDescriptorTableOpen = method;

        var il = method.GetILGenerator();
        var fdLocal = il.DeclareLocal(_types.Int32);
        var streamLocal = il.DeclareLocal(typeof(FileStream));

        // var fd = Interlocked.Increment(ref _nextFd) - 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, nextFdField);
        var interlockedIncrement = typeof(Interlocked).GetMethod(
            "Increment",
            [typeof(int).MakeByRefType()])!;
        il.Emit(OpCodes.Call, interlockedIncrement);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, fdLocal);

        // var stream = new FileStream(path, mode, access, share)
        var fileStreamCtor = typeof(FileStream).GetConstructor(
            [typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!;
        il.Emit(OpCodes.Ldarg_1); // path
        il.Emit(OpCodes.Ldarg_2); // mode
        il.Emit(OpCodes.Ldarg_3); // access
        il.Emit(OpCodes.Ldarg, 4); // share (5th arg, 0-based index 4)
        il.Emit(OpCodes.Newobj, fileStreamCtor);
        il.Emit(OpCodes.Stloc, streamLocal);

        // _streams[fd] = stream
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, streamsField);
        il.Emit(OpCodes.Ldloc, fdLocal);
        il.Emit(OpCodes.Ldloc, streamLocal);
        var dictIndexerSet = streamsType.GetMethod("set_Item")!;
        il.Emit(OpCodes.Callvirt, dictIndexerSet);

        // return fd
        il.Emit(OpCodes.Ldloc, fdLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public FileStream Get(int fd)
    /// Throws $NodeError if fd is invalid.
    /// </summary>
    private void EmitFileDescriptorTableGet(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder streamsField,
        Type streamsType)
    {
        var method = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public,
            typeof(FileStream),
            [_types.Int32]
        );
        runtime.FileDescriptorTableGet = method;

        var il = method.GetILGenerator();
        var streamLocal = il.DeclareLocal(typeof(FileStream));
        var successLabel = il.DefineLabel();

        // if (_streams.TryGetValue(fd, out var stream)) return stream
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, streamsField);
        il.Emit(OpCodes.Ldarg_1); // fd
        il.Emit(OpCodes.Ldloca, streamLocal);
        var tryGetValue = streamsType.GetMethod("TryGetValue")!;
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brtrue, successLabel);

        // throw new $NodeError("EBADF", "bad file descriptor", "fstat", null, 9)
        il.Emit(OpCodes.Ldstr, "EBADF");
        il.Emit(OpCodes.Ldstr, "bad file descriptor");
        il.Emit(OpCodes.Ldstr, "fstat");
        il.Emit(OpCodes.Ldnull);
        var nullableInt = _types.MakeNullable(_types.Int32);
        var errnoLocal = il.DeclareLocal(nullableInt);
        il.Emit(OpCodes.Ldloca, errnoLocal);
        il.Emit(OpCodes.Ldc_I4, 9);
        var nullableCtor = nullableInt.GetConstructor([_types.Int32])!;
        il.Emit(OpCodes.Call, nullableCtor);
        il.Emit(OpCodes.Ldloc, errnoLocal);
        il.Emit(OpCodes.Newobj, runtime.NodeErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(successLabel);
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void Close(int fd)
    /// Throws $NodeError if fd is invalid.
    /// </summary>
    private void EmitFileDescriptorTableClose(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder streamsField,
        Type streamsType)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32]
        );
        runtime.FileDescriptorTableClose = method;

        var il = method.GetILGenerator();
        var streamLocal = il.DeclareLocal(typeof(FileStream));
        var successLabel = il.DefineLabel();

        // if (_streams.TryRemove(fd, out var stream)) { stream.Dispose(); return; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, streamsField);
        il.Emit(OpCodes.Ldarg_1); // fd
        il.Emit(OpCodes.Ldloca, streamLocal);
        var tryRemove = streamsType.GetMethod("TryRemove", [typeof(int), typeof(FileStream).MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, tryRemove);
        il.Emit(OpCodes.Brtrue, successLabel);

        // throw new $NodeError("EBADF", "bad file descriptor", "close", null, 9)
        il.Emit(OpCodes.Ldstr, "EBADF");
        il.Emit(OpCodes.Ldstr, "bad file descriptor");
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldnull);
        var nullableInt = _types.MakeNullable(_types.Int32);
        var errnoLocal = il.DeclareLocal(nullableInt);
        il.Emit(OpCodes.Ldloca, errnoLocal);
        il.Emit(OpCodes.Ldc_I4, 9);
        var nullableCtor = nullableInt.GetConstructor([_types.Int32])!;
        il.Emit(OpCodes.Call, nullableCtor);
        il.Emit(OpCodes.Ldloc, errnoLocal);
        il.Emit(OpCodes.Newobj, runtime.NodeErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(successLabel);
        // stream.Dispose()
        il.Emit(OpCodes.Ldloc, streamLocal);
        var dispose = typeof(FileStream).GetMethod("Dispose", Type.EmptyTypes)!;
        il.Emit(OpCodes.Callvirt, dispose);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool IsValid(int fd)
    /// </summary>
    private void EmitFileDescriptorTableIsValid(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder streamsField,
        Type streamsType)
    {
        var method = typeBuilder.DefineMethod(
            "IsValid",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Int32]
        );
        _ = method;

        var il = method.GetILGenerator();

        // return _streams.ContainsKey(fd)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, streamsField);
        il.Emit(OpCodes.Ldarg_1); // fd
        var containsKey = streamsType.GetMethod("ContainsKey")!;
        il.Emit(OpCodes.Callvirt, containsKey);
        il.Emit(OpCodes.Ret);
    }
}
