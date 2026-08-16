using System.IO;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the long-tail fd primitives (#976): FsFsyncSync (flush an fd),
    /// FsFdPath (resolve an fd to its path so the TS facade can route fchmod/
    /// fchown/futimes through the path ops), and FsStatfsRaw (filesystem stats
    /// from DriveInfo). Mirrors FsModuleInterpreter so both modes agree. BCL-only
    /// (standalone): FileStream/DriveInfo/Path/Math are all in the BCL.
    /// </summary>
    private void EmitFsLongTail(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitFsFsyncSync(typeBuilder, runtime);
        EmitFsFdPath(typeBuilder, runtime);
        EmitFsStatfsRaw(typeBuilder, runtime);
    }

    /// <summary>void FsFsyncSync(object fd) — flush the fd's buffered writes to disk.</summary>
    private void EmitFsFsyncSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsFsyncSync",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Void, [_types.Object]);
        runtime.FsFsyncSync = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "fsync", afterTry =>
        {
            var flushMethod = typeof(FileStream).GetMethod("Flush", [typeof(bool)])!;

            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            il.Emit(OpCodes.Ldc_I4_1); // flushToDisk = true
            il.Emit(OpCodes.Callvirt, flushMethod);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>object FsFdPath(object fd) — the open fd's file path (FileStream.Name).</summary>
    private void EmitFsFdPath(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsFdPath",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.FsFdPath = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "fstat", afterTry =>
        {
            var nameGetter = typeof(FileStream).GetProperty("Name")!.GetMethod!;

            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            il.Emit(OpCodes.Callvirt, nameGetter); // string is already an object
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object FsStatfsRaw(object path) — flat statfs record {type,bsize,blocks,
    /// bfree,bavail,files,ffree}. Mirrors FsModuleInterpreter.BuildStatfsRecord:
    /// bsize fixed at 4096; block counts from DriveInfo; inode counts reported 0;
    /// any DriveInfo failure (virtual/unknown filesystem) yields zeros, never throws.
    /// </summary>
    private void EmitFsStatfsRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsStatfsRaw",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.FsStatfsRaw = method;

        var il = method.GetILGenerator();

        // path = Stringify(arg0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // blocks / bfree / bavail default to 0.0
        var blocksL = il.DeclareLocal(_types.Double);
        var bfreeL = il.DeclareLocal(_types.Double);
        var bavailL = il.DeclareLocal(_types.Double);

        var getFullPath = typeof(Path).GetMethod("GetFullPath", [typeof(string)])!;
        var getPathRoot = typeof(Path).GetMethod("GetPathRoot", [typeof(string)])!;
        var isNullOrEmpty = typeof(string).GetMethod("IsNullOrEmpty", [typeof(string)])!;
        var driveCtor = typeof(DriveInfo).GetConstructor([typeof(string)])!;
        var totalSizeGet = typeof(DriveInfo).GetProperty("TotalSize")!.GetMethod!;
        var totalFreeGet = typeof(DriveInfo).GetProperty("TotalFreeSpace")!.GetMethod!;
        var availFreeGet = typeof(DriveInfo).GetProperty("AvailableFreeSpace")!.GetMethod!;
        var floor = typeof(Math).GetMethod("Floor", [typeof(double)])!;

        var driveLocal = il.DeclareLocal(typeof(DriveInfo));

        // try { root = GetPathRoot(GetFullPath(path)); if (!IsNullOrEmpty(root)) { drive=...; blocks/bfree/bavail } } catch { /* zeros */ }
        il.BeginExceptionBlock();
        {
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, getFullPath);
            il.Emit(OpCodes.Call, getPathRoot);
            var rootLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, rootLocal);

            var skipDrive = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Call, isNullOrEmpty);
            il.Emit(OpCodes.Brtrue, skipDrive);

            il.Emit(OpCodes.Ldloc, rootLocal);
            il.Emit(OpCodes.Newobj, driveCtor);
            il.Emit(OpCodes.Stloc, driveLocal);

            void Blocks(System.Reflection.MethodInfo getter, LocalBuilder dest)
            {
                il.Emit(OpCodes.Ldloc, driveLocal);
                il.Emit(OpCodes.Callvirt, getter);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Ldc_R8, 4096.0);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Call, floor);
                il.Emit(OpCodes.Stloc, dest);
            }
            Blocks(totalSizeGet, blocksL);
            Blocks(totalFreeGet, bfreeL);
            Blocks(availFreeGet, bavailL);

            il.MarkLabel(skipDrive);
            // No manual Leave: BeginCatchBlock emits the try's exit leave, and
            // EndExceptionBlock emits the catch's — both to the block's end.
        }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // swallow → zeros
        il.EndExceptionBlock();

        // Build the record dictionary.
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);
        il.Emit(OpCodes.Newobj, dictCtor);
        var dict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dict);

        void AddEntry(string key, Action loadDouble)
        {
            il.Emit(OpCodes.Ldloc, dict);
            il.Emit(OpCodes.Ldstr, key);
            loadDouble();
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, addMethod);
        }

        AddEntry("type", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("bsize", () => il.Emit(OpCodes.Ldc_R8, 4096.0));
        AddEntry("blocks", () => il.Emit(OpCodes.Ldloc, blocksL));
        AddEntry("bfree", () => il.Emit(OpCodes.Ldloc, bfreeL));
        AddEntry("bavail", () => il.Emit(OpCodes.Ldloc, bavailL));
        AddEntry("files", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("ffree", () => il.Emit(OpCodes.Ldc_R8, 0.0));

        il.Emit(OpCodes.Ldloc, dict);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }
}
