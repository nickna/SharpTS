using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static double FsOpenSync(object path, object flags, object mode)
    /// Opens a file and returns a file descriptor.
    /// </summary>
    private void EmitFsOpenSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsOpenSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsOpenSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        var resultLocal = il.DeclareLocal(_types.Double);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // Parse flags using pure-IL FsFlagsParsePure helper (no reflection, standalone-compatible)
            il.Emit(OpCodes.Ldarg_1); // flags
            il.Emit(OpCodes.Call, runtime.FsFlagsParsePure);

            // The result is a ValueTuple<FileMode, FileAccess, FileShare> (not boxed)
            var tupleType = typeof(ValueTuple<FileMode, FileAccess, FileShare>);
            var tupleLocal = il.DeclareLocal(tupleType);
            il.Emit(OpCodes.Stloc, tupleLocal);

            // Get $FileDescriptorTable.Instance (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);

            // Call instance.Open(path, mode, access, share)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item1")!);
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item2")!);
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item3")!);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableOpen);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsCloseSync(object fd)
    /// Closes a file descriptor.
    /// </summary>
    private void EmitFsCloseSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsCloseSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.FsCloseSync = method;

        var il = method.GetILGenerator();

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create a null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "close", afterTry =>
        {
            // Get $FileDescriptorTable.Instance (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);

            // Call instance.Close(fd)
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableClose);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static double FsReadSync(object fd, object buffer, object offset, object length, object position)
    /// Reads from a file descriptor into a buffer.
    /// </summary>
    private void EmitFsReadSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.FsReadSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Convert offset to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert length to int
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "read", afterTry =>
        {
            var fileStreamReadMethod = typeof(FileStream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!;
            var fileStreamSeekMethod = typeof(FileStream).GetMethod("Seek")!;

            // Get FileStream from $FileDescriptorTable (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Handle position (if not null, seek to it)
            var skipSeekLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Brfalse, skipSeekLabel);
            // Check for undefined (use emitted $Undefined type for standalone DLLs)
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, skipSeekLabel);

            // Seek to position
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldc_I4_0); // SeekOrigin.Begin
            il.Emit(OpCodes.Callvirt, fileStreamSeekMethod);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(skipSeekLabel);

            // Get buffer data - cast to compiled $Buffer type and call GetData()
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            var dataLocal = il.DeclareLocal(typeof(byte[]));
            il.Emit(OpCodes.Stloc, dataLocal);

            // Read from stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Callvirt, fileStreamReadMethod);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static double FsWriteSync(object fd, object data, object offset, object length, object position)
    /// Writes to a file descriptor from a buffer or string.
    /// </summary>
    private void EmitFsWriteSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.FsWriteSyncBuffer = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "write", afterTry =>
        {
            var fileStreamWriteMethod = typeof(FileStream).GetMethod("Write", [typeof(byte[]), typeof(int), typeof(int)])!;
            var fileStreamSeekMethod = typeof(FileStream).GetMethod("Seek")!;

            // Get FileStream from $FileDescriptorTable (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Handle position (if not null and not undefined, seek to it)
            var skipSeekLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Brfalse, skipSeekLabel);
            // Use emitted $Undefined type for standalone DLLs
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, skipSeekLabel);

            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, fileStreamSeekMethod);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(skipSeekLabel);

            // Check if data is a buffer or string
            var isBufferLabel = il.DefineLabel();
            var afterDataLabel = il.DefineLabel();
            var dataLocal = il.DeclareLocal(typeof(byte[]));
            var offsetLocal = il.DeclareLocal(_types.Int32);
            var lengthLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, isBufferLabel);

            // String case - convert to UTF8 bytes
            // Stack: [] -> [Encoding] -> [Encoding, string] -> [byte[]]
            il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetMethod!);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Callvirt, _types.EncodingGetBytesFromString);
            il.Emit(OpCodes.Stloc, dataLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.Emit(OpCodes.Br, afterDataLabel);

            // Buffer case - use compiled $Buffer type
            il.MarkLabel(isBufferLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            il.Emit(OpCodes.Stloc, dataLocal);

            // Offset: use arg2 if provided, else 0
            var useDefaultOffsetLabel = il.DefineLabel();
            var afterOffsetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, useDefaultOffsetLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.Emit(OpCodes.Br, afterOffsetLabel);
            il.MarkLabel(useDefaultOffsetLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.MarkLabel(afterOffsetLabel);

            // Length: use arg3 if provided, else buffer.Length (via data array length)
            var useDefaultLengthLabel = il.DefineLabel();
            var afterLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, useDefaultLengthLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.Emit(OpCodes.Br, afterLengthLabel);
            il.MarkLabel(useDefaultLengthLabel);
            // Use data array length instead of calling buffer.Length
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.MarkLabel(afterLengthLabel);

            il.MarkLabel(afterDataLabel);

            // Write to stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Callvirt, fileStreamWriteMethod);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsFstatSync(object fd)
    /// Returns stats for an open file descriptor.
    /// </summary>
    private void EmitFsFstatSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsFstatSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsFstatSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "fstat", afterTry =>
        {
            var lengthGetter = typeof(FileStream).GetProperty("Length")!.GetMethod!;

            // Get FileStream from $FileDescriptorTable (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Get file size from stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Callvirt, lengthGetter);
            il.Emit(OpCodes.Conv_R8);
            var sizeLocal = il.DeclareLocal(_types.Double);
            il.Emit(OpCodes.Stloc, sizeLocal);

            // Create $Stats instance:
            // new $Stats(isFile, isDirectory, isSymbolicLink, size, mode, atimeMs, mtimeMs, ctimeMs, birthtimeMs)
            il.Emit(OpCodes.Ldc_I4_1);        // arg1: isFile = true (fds are always files)
            il.Emit(OpCodes.Ldc_I4_0);        // arg2: isDirectory = false
            il.Emit(OpCodes.Ldc_I4_0);        // arg3: isSymbolicLink = false
            il.Emit(OpCodes.Ldloc, sizeLocal); // arg4: size
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg5: mode (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg6: atimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg7: mtimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg8: ctimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg9: birthtimeMs (placeholder)
            il.Emit(OpCodes.Newobj, runtime.StatsCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsFtruncateSync(object fd, object len)
    /// Truncates an open file descriptor to the specified length.
    /// </summary>
    private void EmitFsFtruncateSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsFtruncateSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsFtruncateSync = method;

        var il = method.GetILGenerator();

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Convert len to long (default 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        var lenLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, lenLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "ftruncate", afterTry =>
        {
            var setLengthMethod = typeof(FileStream).GetMethod("SetLength")!;

            // Get FileStream from $FileDescriptorTable (pure-IL, no reflection)
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // SetLength
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Callvirt, setLengthMethod);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }
}
