using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits IL to throw a $NodeError with the specified error details.
    /// Uses the emitted $NodeError type for standalone DLL support.
    /// </summary>
    /// <param name="il">The IL generator.</param>
    /// <param name="runtime">The emitted runtime containing NodeErrorCtor.</param>
    /// <param name="code">Error code (e.g., "ENOSYS", "EINVAL").</param>
    /// <param name="message">Error message.</param>
    /// <param name="syscall">System call name.</param>
    /// <param name="pathLocal">Local variable containing the path string.</param>
    private void EmitNodeErrorThrow(ILGenerator il, EmittedRuntime runtime, string code, string message, string syscall, LocalBuilder pathLocal)
    {
        // Load constructor arguments: code, message, syscall, path, errno (null)
        il.Emit(OpCodes.Ldstr, code);
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Ldstr, syscall);
        il.Emit(OpCodes.Ldloc, pathLocal);

        // For int? errno = null, we need to load a default Nullable<int>
        var nullableInt32 = _types.MakeNullable(_types.Int32);
        var errnoLocal = il.DeclareLocal(nullableInt32);
        il.Emit(OpCodes.Ldloca, errnoLocal);
        il.Emit(OpCodes.Initobj, nullableInt32);
        il.Emit(OpCodes.Ldloc, errnoLocal);

        // new $NodeError(code, message, syscall, path, errno)
        il.Emit(OpCodes.Newobj, runtime.NodeErrorCtor);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits fs module helper methods.
    /// </summary>
    private void EmitFsModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Low-level helpers using reflection for standalone DLLs (must be first)
        EmitFsLowLevelHelpers(typeBuilder, runtime);

        // Encoding helpers — must precede read/write/append which call them.
        EmitFsEncodingHelpers(typeBuilder, runtime);

        EmitFsExistsSync(typeBuilder, runtime);
        EmitFsReadFileSync(typeBuilder, runtime);
        EmitFsWriteFileSync(typeBuilder, runtime);
        EmitFsAppendFileSync(typeBuilder, runtime);
        EmitFsUnlinkSync(typeBuilder, runtime);
        EmitFsMkdirSync(typeBuilder, runtime);
        EmitFsRmdirSync(typeBuilder, runtime);
        EmitFsCreateDirent(typeBuilder, runtime); // Must be before ReaddirSync which uses it
        EmitFsReaddirSync(typeBuilder, runtime);
        EmitFsStatSync(typeBuilder, runtime);
        EmitFsLstatSync(typeBuilder, runtime);
        EmitFsStatRawHelpers(typeBuilder, runtime); // #977: statRaw/lstatRaw/fstatRaw
        EmitFsRenameSync(typeBuilder, runtime);
        EmitFsCopyFileSync(typeBuilder, runtime);
        EmitFsAccessSync(typeBuilder, runtime);
        EmitFsChmodSync(typeBuilder, runtime);
        EmitFsChownSync(typeBuilder, runtime);
        EmitFsLchownSync(typeBuilder, runtime);
        EmitFsTruncateSync(typeBuilder, runtime);
        EmitFsSymlinkSync(typeBuilder, runtime);
        EmitFsReadlinkSync(typeBuilder, runtime);
        EmitFsRealpathSync(typeBuilder, runtime);
        EmitFsUtimesSync(typeBuilder, runtime);
        EmitFsGetConstants(typeBuilder, runtime);

        // File descriptor APIs
        EmitFsOpenSync(typeBuilder, runtime);
        EmitFsCloseSync(typeBuilder, runtime);
        EmitFsReadSync(typeBuilder, runtime);
        EmitFsWriteSync(typeBuilder, runtime);
        EmitFsFstatSync(typeBuilder, runtime);
        EmitFsFtruncateSync(typeBuilder, runtime);

        // Long-tail fd primitives (#976): fsync, fd→path, statfs
        EmitFsLongTail(typeBuilder, runtime);

        // Directory utilities
        EmitFsMkdtempSync(typeBuilder, runtime);
        EmitFsOpendirSync(typeBuilder, runtime);

        // Hard links
        EmitFsLinkSync(typeBuilder, runtime);

        // Stream factory methods (types already defined in Phase 1)
        EmitFsStreamFactories(typeBuilder, runtime);

        // Async fs methods (fs.promises and fs/promises)
        EmitFsAsyncMethods(typeBuilder, runtime);
        EmitFsGetPromisesNamespace(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits low-level helpers that use reflection to access FileDescriptorTable, FsFlags, SharpTSDir, and LibC.
    /// These helpers avoid compile-time dependencies on SharpTS.dll for standalone compiled assemblies.
    /// </summary>
    private void EmitFsLowLevelHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Pure-IL helpers for standalone DLLs (no reflection)
        EmitFsFlagsParsePureHelper(typeBuilder, runtime);

        // Pure P/Invoke hard link implementation (Phase 21)
        EmitHardLinkPInvokeMethods(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a try-catch block that converts exceptions to Node.js-style errors.
    /// The emitTryBody action receives the afterTry label for the Leave instruction.
    /// </summary>
    private void EmitWithFsErrorHandling(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder pathLocal,
        string syscall,
        Action<Label> emitTryBody)
    {
        var caughtExLocal = il.DeclareLocal(_types.Exception);
        var afterTry = il.DefineLabel();

        il.BeginExceptionBlock();
        emitTryBody(afterTry);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, caughtExLocal);
        il.Emit(OpCodes.Ldloc, caughtExLocal);
        il.Emit(OpCodes.Ldstr, syscall);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, runtime.ThrowNodeError);
        il.Emit(OpCodes.Rethrow);

        il.EndExceptionBlock();
        il.MarkLabel(afterTry);
    }
}
