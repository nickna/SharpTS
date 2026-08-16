using System.IO;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the raw stat record helpers (#977): FsStatTimeMs, FsBuildStatRecord,
    /// and FsStatRaw/FsLstatRaw/FsFstatRaw. These return a flat numeric record that
    /// the TS Stats class shapes — mirroring FsModuleInterpreter.BuildStatRecord so
    /// interpreter and compiled Stats agree. BCL-only (standalone).
    /// </summary>
    private void EmitFsStatRawHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitFsStatTimeMs(typeBuilder, runtime);
        EmitFsBuildStatRecord(typeBuilder, runtime);
        EmitFsStatRaw(typeBuilder, runtime);
        EmitFsLstatRaw(typeBuilder, runtime);
        EmitFsFstatRaw(typeBuilder, runtime);
    }

    /// <summary>double FsStatTimeMs(DateTime local) = unix-ms of local.ToUniversalTime().</summary>
    private void EmitFsStatTimeMs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsStatTimeMs",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Double, [typeof(DateTime)]);
        runtime.FsStatTimeMs = method;

        var il = method.GetILGenerator();
        var utc = il.DeclareLocal(typeof(DateTime));
        il.Emit(OpCodes.Ldarga_S, (byte)0);
        il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToUniversalTime", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, utc);
        var dto = il.DeclareLocal(typeof(DateTimeOffset));
        il.Emit(OpCodes.Ldloc, utc);
        il.Emit(OpCodes.Newobj, typeof(DateTimeOffset).GetConstructor([typeof(DateTime)])!);
        il.Emit(OpCodes.Stloc, dto);
        il.Emit(OpCodes.Ldloca, dto);
        il.Emit(OpCodes.Call, typeof(DateTimeOffset).GetMethod("ToUnixTimeMilliseconds", Type.EmptyTypes)!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object FsBuildStatRecord(bool isDir, bool isSymlink, bool isReadOnly,
    ///   double size, double atimeMs, double mtimeMs, double ctimeMs, double birthtimeMs)
    /// </summary>
    private void EmitFsBuildStatRecord(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsBuildStatRecord",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object,
            [_types.Boolean, _types.Boolean, _types.Boolean, _types.Double, _types.Double, _types.Double, _types.Double, _types.Double]);
        runtime.FsBuildStatRecord = method;

        var il = method.GetILGenerator();

        // typeBits = isSymlink ? 0xA000 : (isDir ? 0x4000 : 0x8000)
        var typeBits = il.DeclareLocal(_types.Int32);
        var symL = il.DefineLabel(); var dirL = il.DefineLabel(); var typeDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brtrue, symL);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brtrue, dirL);
        il.Emit(OpCodes.Ldc_I4, 0x8000); il.Emit(OpCodes.Br, typeDone);
        il.MarkLabel(dirL); il.Emit(OpCodes.Ldc_I4, 0x4000); il.Emit(OpCodes.Br, typeDone);
        il.MarkLabel(symL); il.Emit(OpCodes.Ldc_I4, 0xA000);
        il.MarkLabel(typeDone); il.Emit(OpCodes.Stloc, typeBits);

        // permBits = isDir ? 0x1FF : (isReadOnly ? 0x124 : 0x1B6)
        var permBits = il.DeclareLocal(_types.Int32);
        var permFile = il.DefineLabel(); var permRo = il.DefineLabel(); var permDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, permFile);
        il.Emit(OpCodes.Ldc_I4, 0x1FF); il.Emit(OpCodes.Br, permDone);
        il.MarkLabel(permFile);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brtrue, permRo);
        il.Emit(OpCodes.Ldc_I4, 0x1B6); il.Emit(OpCodes.Br, permDone);
        il.MarkLabel(permRo); il.Emit(OpCodes.Ldc_I4, 0x124);
        il.MarkLabel(permDone); il.Emit(OpCodes.Stloc, permBits);

        // mode = (double)(typeBits | permBits)
        var modeL = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, typeBits); il.Emit(OpCodes.Ldloc, permBits); il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Stloc, modeL);

        // blocks = Math.Floor((size + 511) / 512)
        var blocksL = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldc_R8, 511.0); il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_R8, 512.0); il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, blocksL);

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

        AddEntry("mode", () => il.Emit(OpCodes.Ldloc, modeL));
        AddEntry("size", () => il.Emit(OpCodes.Ldarg_3));
        AddEntry("atimeMs", () => il.Emit(OpCodes.Ldarg_S, (byte)4));
        AddEntry("mtimeMs", () => il.Emit(OpCodes.Ldarg_S, (byte)5));
        AddEntry("ctimeMs", () => il.Emit(OpCodes.Ldarg_S, (byte)6));
        AddEntry("birthtimeMs", () => il.Emit(OpCodes.Ldarg_S, (byte)7));
        AddEntry("dev", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("ino", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("nlink", () => il.Emit(OpCodes.Ldc_R8, 1.0));
        AddEntry("uid", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("gid", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("rdev", () => il.Emit(OpCodes.Ldc_R8, 0.0));
        AddEntry("blksize", () => il.Emit(OpCodes.Ldc_R8, 4096.0));
        AddEntry("blocks", () => il.Emit(OpCodes.Ldloc, blocksL));

        il.Emit(OpCodes.Ldloc, dict);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

    // Loads the four File.Get*Time timestamps (as unix-ms doubles) into the given
    // locals, from the path local. Times work for files and directories alike.
    private void EmitLoadStatTimes(ILGenerator il, EmittedRuntime runtime, LocalBuilder pathLocal,
        LocalBuilder at, LocalBuilder mt, LocalBuilder ct)
    {
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetLastAccessTime", _types.String));
        il.Emit(OpCodes.Call, runtime.FsStatTimeMs); il.Emit(OpCodes.Stloc, at);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetLastWriteTime", _types.String));
        il.Emit(OpCodes.Call, runtime.FsStatTimeMs); il.Emit(OpCodes.Stloc, mt);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetCreationTime", _types.String));
        il.Emit(OpCodes.Call, runtime.FsStatTimeMs); il.Emit(OpCodes.Stloc, ct);
    }

    /// <summary>object FsStatRaw(object path) — follows symlinks.</summary>
    private void EmitFsStatRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsStatRaw",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.FsStatRaw = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String); il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "stat", afterTry =>
        {
            var isDirL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Stloc, isDirL);
            var isFileL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Stloc, isFileL);

            var existsL = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, isDirL); il.Emit(OpCodes.Brtrue, existsL);
            il.Emit(OpCodes.Ldloc, isFileL); il.Emit(OpCodes.Brtrue, existsL);
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);
            il.MarkLabel(existsL);

            var sizeL = EmitStatSize(il, isFileL, pathLocal);
            var roL = EmitStatReadOnly(il, isFileL, pathLocal);
            var at = il.DeclareLocal(_types.Double);
            var mt = il.DeclareLocal(_types.Double);
            var ct = il.DeclareLocal(_types.Double);
            EmitLoadStatTimes(il, runtime, pathLocal, at, mt, ct);

            il.Emit(OpCodes.Ldloc, isDirL);
            il.Emit(OpCodes.Ldc_I4_0);          // isSymlink = false (stat follows links)
            il.Emit(OpCodes.Ldloc, roL);
            il.Emit(OpCodes.Ldloc, sizeL);
            il.Emit(OpCodes.Ldloc, at); il.Emit(OpCodes.Ldloc, mt);
            il.Emit(OpCodes.Ldloc, ct); il.Emit(OpCodes.Ldloc, ct);
            il.Emit(OpCodes.Call, runtime.FsBuildStatRecord);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>object FsLstatRaw(object path) — does not follow symlinks.</summary>
    private void EmitFsLstatRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsLstatRaw",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.FsLstatRaw = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String); il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "lstat", afterTry =>
        {
            var dirRawL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Stloc, dirRawL);
            var isFileL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Stloc, isFileL);

            var existsL = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, dirRawL); il.Emit(OpCodes.Brtrue, existsL);
            il.Emit(OpCodes.Ldloc, isFileL); il.Emit(OpCodes.Brtrue, existsL);
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);
            il.MarkLabel(existsL);

            // isDir = dirRaw && !isFile
            var isDirL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, dirRawL);
            il.Emit(OpCodes.Ldloc, isFileL); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.And); il.Emit(OpCodes.Stloc, isDirL);

            // isSymlink = (File.GetAttributes(path) & ReparsePoint) != 0
            var symL = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetAttributes", _types.String));
            il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
            il.Emit(OpCodes.And); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Stloc, symL);

            var sizeL = EmitStatSize(il, isFileL, pathLocal);
            var roL = EmitStatReadOnly(il, isFileL, pathLocal);
            var at = il.DeclareLocal(_types.Double);
            var mt = il.DeclareLocal(_types.Double);
            var ct = il.DeclareLocal(_types.Double);
            EmitLoadStatTimes(il, runtime, pathLocal, at, mt, ct);

            il.Emit(OpCodes.Ldloc, isDirL);
            il.Emit(OpCodes.Ldloc, symL);
            il.Emit(OpCodes.Ldloc, roL);
            il.Emit(OpCodes.Ldloc, sizeL);
            il.Emit(OpCodes.Ldloc, at); il.Emit(OpCodes.Ldloc, mt);
            il.Emit(OpCodes.Ldloc, ct); il.Emit(OpCodes.Ldloc, ct);
            il.Emit(OpCodes.Call, runtime.FsBuildStatRecord);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>object FsFstatRaw(object fd).</summary>
    private void EmitFsFstatRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("FsFstatRaw",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.FsFstatRaw = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, runtime.ToNumber); il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32); il.Emit(OpCodes.Stloc, fdLocal);

        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String); il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "fstat", afterTry =>
        {
            il.Emit(OpCodes.Ldsfld, runtime.FileDescriptorTableInstance);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, runtime.FileDescriptorTableGet);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            var sizeL = il.DeclareLocal(_types.Double);
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Callvirt, typeof(FileStream).GetProperty("Length")!.GetMethod!);
            il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Stloc, sizeL);

            // path = stream.Name → for time recovery
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Callvirt, typeof(FileStream).GetProperty("Name")!.GetMethod!);
            il.Emit(OpCodes.Stloc, pathLocal);

            var at = il.DeclareLocal(_types.Double);
            var mt = il.DeclareLocal(_types.Double);
            var ct = il.DeclareLocal(_types.Double);
            EmitLoadStatTimes(il, runtime, pathLocal, at, mt, ct);

            il.Emit(OpCodes.Ldc_I4_0);    // isDir = false
            il.Emit(OpCodes.Ldc_I4_0);    // isSymlink = false
            il.Emit(OpCodes.Ldc_I4_0);    // isReadOnly = false
            il.Emit(OpCodes.Ldloc, sizeL);
            il.Emit(OpCodes.Ldloc, at); il.Emit(OpCodes.Ldloc, mt);
            il.Emit(OpCodes.Ldloc, ct); il.Emit(OpCodes.Ldloc, ct);
            il.Emit(OpCodes.Call, runtime.FsBuildStatRecord);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    // size = isFile ? (double)new FileInfo(path).Length : 0
    private LocalBuilder EmitStatSize(ILGenerator il, LocalBuilder isFileL, LocalBuilder pathLocal)
    {
        var sizeL = il.DeclareLocal(_types.Double);
        var notFile = il.DefineLabel(); var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isFileL); il.Emit(OpCodes.Brfalse, notFile);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Length").GetMethod!);
        il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Stloc, sizeL); il.Emit(OpCodes.Br, done);
        il.MarkLabel(notFile); il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Stloc, sizeL);
        il.MarkLabel(done);
        return sizeL;
    }

    // readOnly = isFile && (File.GetAttributes(path) & ReadOnly) != 0
    private LocalBuilder EmitStatReadOnly(ILGenerator il, LocalBuilder isFileL, LocalBuilder pathLocal)
    {
        var roL = il.DeclareLocal(_types.Boolean);
        var notFile = il.DefineLabel(); var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isFileL); il.Emit(OpCodes.Brfalse, notFile);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetAttributes", _types.String));
        il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReadOnly);
        il.Emit(OpCodes.And); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, roL); il.Emit(OpCodes.Br, done);
        il.MarkLabel(notFile); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, roL);
        il.MarkLabel(done);
        return roL;
    }
}
