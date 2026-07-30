using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _kernel32CreateHardLink;
    private MethodBuilder? _libcLink;

    /// <summary>
    /// Emits P/Invoke methods for hard link creation in standalone DLLs.
    /// </summary>
    private void EmitHardLinkPInvokeMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Define Windows kernel32.dll CreateHardLinkW P/Invoke
        _kernel32CreateHardLink = typeBuilder.DefinePInvokeMethod(
            "CreateHardLinkW",
            "kernel32.dll",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            CallingConventions.Standard,
            _types.Boolean,
            [_types.String, _types.String, _types.IntPtr],
            CallingConvention.Winapi,
            CharSet.Unicode
        );
        _kernel32CreateHardLink.SetImplementationFlags(MethodImplAttributes.PreserveSig);

        // Define Unix libc link P/Invoke
        _libcLink = typeBuilder.DefinePInvokeMethod(
            "link",
            "libc",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            CallingConventions.Standard,
            _types.Int32,
            [_types.String, _types.String],
            CallingConvention.Cdecl,
            CharSet.Ansi
        );
        _libcLink.SetImplementationFlags(MethodImplAttributes.PreserveSig);

        // Emit the cross-platform CreateHardLink helper
        EmitCreateHardLinkPure(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static void CreateHardLinkPure(string existingPath, string newPath)
    /// Cross-platform hard link creation using emitted P/Invoke.
    /// </summary>
    private void EmitCreateHardLinkPure(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateHardLinkPure",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.String, _types.String]
        );
        runtime.CreateHardLinkPure = method;

        var il = method.GetILGenerator();

        var unixPath = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (OperatingSystem.IsWindows()) { ... } else { ... }
        il.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod("IsWindows")!);
        il.Emit(OpCodes.Brfalse, unixPath);

        // Windows path: CreateHardLinkW(newPath, existingPath, IntPtr.Zero)
        // Note: Windows API takes (newPath, existingPath) order
        var windowsSuccessLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); // newPath
        il.Emit(OpCodes.Ldarg_0); // existingPath
        il.Emit(OpCodes.Ldsfld, typeof(IntPtr).GetField("Zero")!);
        il.Emit(OpCodes.Call, _kernel32CreateHardLink!);
        il.Emit(OpCodes.Brtrue, windowsSuccessLabel);

        // Windows failure - throw IOException
        // Get last error and format message
        var errorLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Call, typeof(Marshal).GetMethod("GetLastWin32Error")!);
        il.Emit(OpCodes.Stloc, errorLocal);

        // Build error message: "{code}: {message}, link '{existingPath}' -> '{newPath}'"
        EmitHardLinkErrorMessage(il, errorLocal, true);
        il.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(windowsSuccessLabel);
        il.Emit(OpCodes.Br, endLabel);

        // Unix path: link(existingPath, newPath)
        il.MarkLabel(unixPath);
        var unixSuccessLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); // existingPath
        il.Emit(OpCodes.Ldarg_1); // newPath
        il.Emit(OpCodes.Call, _libcLink!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, unixSuccessLabel);

        // Unix failure - throw IOException
        var errnoLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Call, typeof(Marshal).GetMethod("GetLastPInvokeError")!);
        il.Emit(OpCodes.Stloc, errnoLocal);

        // Build error message
        EmitHardLinkErrorMessage(il, errnoLocal, false);
        il.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(unixSuccessLabel);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits code that builds and pushes a hard link error message string onto the stack.
    /// Format: "{code}: {message}, link '{existingPath}' -> '{newPath}'"
    /// Uses string array concat since we need more than 4 strings.
    /// </summary>
    private void EmitHardLinkErrorMessage(ILGenerator il, LocalBuilder errorLocal, bool isWindows)
    {
        // Store error code string in a local first
        var errorMsgLocal = il.DeclareLocal(_types.String);

        // Get error code string based on platform
        if (isWindows)
        {
            EmitWindowsErrorCodeString(il, errorLocal);
        }
        else
        {
            EmitUnixErrorCodeString(il, errorLocal);
        }
        il.Emit(OpCodes.Stloc, errorMsgLocal);

        // Build 6-element string array: [errorMsg, ", link '", existingPath, "' -> '", newPath, "'"]
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Newarr, _types.String);

        // [0] = errorMsg
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, errorMsgLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // [1] = ", link '"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldstr, ", link '");
        il.Emit(OpCodes.Stelem_Ref);

        // [2] = existingPath (arg0)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // [3] = "' -> '"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldstr, "' -> '");
        il.Emit(OpCodes.Stelem_Ref);

        // [4] = newPath (arg1)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        // [5] = "'"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Stelem_Ref);

        // Call String.Concat(string[])
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [typeof(string[])])!);
    }

    /// <summary>
    /// Emits code that pushes a Windows error message string onto the stack.
    /// Format: "{code}: {message}"
    /// </summary>
    private void EmitWindowsErrorCodeString(ILGenerator il, LocalBuilder errorLocal)
    {
        // Switch on common error codes
        var endLabel = il.DefineLabel();

        var labels = new[]
        {
            (2, "ENOENT", "no such file or directory"),
            (3, "ENOENT", "no such file or directory"),
            (5, "EACCES", "permission denied"),
            (17, "EXDEV", "cross-device link not permitted"),
            (80, "EEXIST", "file already exists"),
            (183, "EEXIST", "file already exists"),
            (1142, "EXDEV", "cross-device link not permitted"),
        };

        foreach (var (code, errCode, message) in labels)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, errorLocal);
            il.Emit(OpCodes.Ldc_I4, code);
            il.Emit(OpCodes.Bne_Un, skipLabel);

            il.Emit(OpCodes.Ldstr, $"{errCode}: {message}");
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(skipLabel);
        }

        // Default case
        il.Emit(OpCodes.Ldstr, "UNKNOWN: unknown error (");
        il.Emit(OpCodes.Ldloca, errorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ")");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits code that pushes a Unix error message string onto the stack.
    /// Format: "{code}: {message}"
    /// </summary>
    private void EmitUnixErrorCodeString(ILGenerator il, LocalBuilder errnoLocal)
    {
        var endLabel = il.DefineLabel();

        var labels = new[]
        {
            (1, "EPERM", "operation not permitted"),
            (2, "ENOENT", "no such file or directory"),
            (13, "EACCES", "permission denied"),
            (17, "EEXIST", "file exists"),
            (18, "EXDEV", "cross-device link not permitted"),
            (20, "ENOTDIR", "not a directory"),
            (21, "EISDIR", "is a directory"),
            (22, "EINVAL", "invalid argument"),
            (40, "ELOOP", "too many symbolic links"),
        };

        foreach (var (errno, errCode, message) in labels)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, errnoLocal);
            il.Emit(OpCodes.Ldc_I4, errno);
            il.Emit(OpCodes.Bne_Un, skipLabel);

            il.Emit(OpCodes.Ldstr, $"{errCode}: {message}");
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(skipLabel);
        }

        // Default case
        il.Emit(OpCodes.Ldstr, "UNKNOWN: unknown error (");
        il.Emit(OpCodes.Ldloca, errnoLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ")");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);

        il.MarkLabel(endLabel);
    }
}
