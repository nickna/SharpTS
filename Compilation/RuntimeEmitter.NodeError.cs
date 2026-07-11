using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // $NodeError fields
    private FieldBuilder _nodeErrorCodeField = null!;
    private FieldBuilder _nodeErrorSyscallField = null!;
    private FieldBuilder _nodeErrorPathField = null!;
    private FieldBuilder _nodeErrorErrnoField = null!;

    /// <summary>
    /// Emits the $NodeError class for standalone FS module support.
    /// NOTE: Must stay in sync with NodeError in Runtime/BuiltIns/Modules/NodeError.cs
    /// </summary>
    private void EmitNodeErrorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $NodeError : Exception
        var typeBuilder = moduleBuilder.DefineType(
            "$NodeError",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            _types.Exception
        );

        // Fields
        var nullableInt32 = _types.MakeNullable(_types.Int32);
        _nodeErrorCodeField = typeBuilder.DefineField("_code", _types.String, FieldAttributes.Private);
        _nodeErrorSyscallField = typeBuilder.DefineField("_syscall", _types.String, FieldAttributes.Private);
        _nodeErrorPathField = typeBuilder.DefineField("_path", _types.String, FieldAttributes.Private);
        _nodeErrorErrnoField = typeBuilder.DefineField("_errno", nullableInt32, FieldAttributes.Private);

        // Constructor
        EmitNodeErrorCtor(typeBuilder, runtime);

        // Property getters
        EmitNodeErrorCodeGetter(typeBuilder, runtime);
        EmitNodeErrorSyscallGetter(typeBuilder, runtime);
        EmitNodeErrorPathGetter(typeBuilder, runtime);
        EmitNodeErrorErrnoGetter(typeBuilder, runtime);

        runtime.NodeErrorType = typeBuilder.CreateType()!;
    }

    private void EmitNodeErrorCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $NodeError(string code, string message, string? syscall, string? path, int? errno)
        var nullableInt32 = _types.MakeNullable(_types.Int32);
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.String, _types.String, _types.String, nullableInt32]
        );
        runtime.NodeErrorCtor = ctor;

        var il = ctor.GetILGenerator();

        // Format message: "{code}: {syscall}: {message} '{path}'" or similar
        var messageLocal = il.DeclareLocal(_types.String);

        // Start with code
        il.Emit(OpCodes.Ldarg_1); // code

        // Check if syscall != null
        var noSyscallLabel = il.DefineLabel();
        var afterSyscallLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3); // syscall
        il.Emit(OpCodes.Brfalse, noSyscallLabel);

        // syscall is not null: code + ": " + syscall + ": "
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldarg_3); // syscall
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Br, afterSyscallLabel);

        il.MarkLabel(noSyscallLabel);
        // syscall is null: just code + ": "
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);

        il.MarkLabel(afterSyscallLabel);
        // Now add message
        il.Emit(OpCodes.Ldarg_2); // message
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);

        // Check if path != null
        var noPathLabel = il.DefineLabel();
        var afterPathLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_S, (byte)4); // path
        il.Emit(OpCodes.Brfalse, noPathLabel);

        // path is not null: + " '" + path + "'"
        il.Emit(OpCodes.Ldstr, " '");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldarg_S, (byte)4); // path
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Br, afterPathLabel);

        il.MarkLabel(noPathLabel);
        // path is null: nothing to add

        il.MarkLabel(afterPathLabel);
        il.Emit(OpCodes.Stloc, messageLocal);

        // Call base(message): Exception(string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, messageLocal);
        il.Emit(OpCodes.Call, _types.Exception.GetConstructor([_types.String])!);

        // _code = code
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _nodeErrorCodeField);

        // _syscall = syscall
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _nodeErrorSyscallField);

        // _path = path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, _nodeErrorPathField);

        // _errno = errno
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stfld, _nodeErrorErrnoField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitNodeErrorCodeGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Code",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.NodeErrorCodeGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _nodeErrorCodeField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNodeErrorSyscallGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Syscall",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.NodeErrorSyscallGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _nodeErrorSyscallField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNodeErrorPathGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Path",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.NodeErrorPathGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _nodeErrorPathField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNodeErrorErrnoGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var nullableInt32 = _types.MakeNullable(_types.Int32);
        var method = typeBuilder.DefineMethod(
            "get_Errno",
            MethodAttributes.Public,
            nullableInt32,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _nodeErrorErrnoField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits helpers that surface .NET exceptions as Node.js-style errors with
    /// proper error codes (ENOENT, EACCES, etc.).
    /// </summary>
    private void EmitNodeErrorHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitThrowNodeError(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static void ThrowNodeError(Exception ex, string syscall, string path)
    /// Creates a regular Exception with Node.js error metadata in Data dictionary and throws it.
    /// </summary>
    /// <remarks>
    /// We can't use NodeError directly because it's in SharpTS.dll which isn't available
    /// at runtime for compiled assemblies. Instead, we store metadata in Exception.Data
    /// and WrapException extracts it to create the proper error object.
    /// </remarks>
    private void EmitThrowNodeError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ThrowNodeError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Exception, _types.String, _types.String]
        );
        runtime.ThrowNodeError = method;

        var il = method.GetILGenerator();

        // Preserve an already-formed $NodeError verbatim (e.g. EBADF from the fd table's
        // Get/Close, or ENOSYS from chown/lchown on Windows). These are thrown by emitted
        // code *inside* the EmitWithFsErrorHandling try block; without this branch the
        // type-based mapping below would clobber their deliberate codes to the EINVAL
        // default. Mirrors the interpreter's WrapFsOperation, which rethrows a NodeError
        // as-is (keeping its own code/syscall/path and pre-formatted message). (#986)
        var notNodeError = il.DefineLabel();
        var nodeErrLocal = il.DeclareLocal(runtime.NodeErrorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.NodeErrorType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, nodeErrLocal);
        il.Emit(OpCodes.Brfalse, notNodeError);

        // var preserved = new Exception(ne.Message);  // ne.Message is already the fully
        // formatted "CODE: syscall: message 'path'" string the interpreter produces.
        var preservedLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Ldloc, nodeErrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, preservedLocal);

        // preserved.Data["__nodeError"] = true;
        il.Emit(OpCodes.Ldloc, preservedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__nodeError");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // preserved.Data["__code"] = ne.Code;
        il.Emit(OpCodes.Ldloc, preservedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__code");
        il.Emit(OpCodes.Ldloc, nodeErrLocal);
        il.Emit(OpCodes.Callvirt, runtime.NodeErrorCodeGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // preserved.Data["__syscall"] = ne.Syscall;
        il.Emit(OpCodes.Ldloc, preservedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__syscall");
        il.Emit(OpCodes.Ldloc, nodeErrLocal);
        il.Emit(OpCodes.Callvirt, runtime.NodeErrorSyscallGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // preserved.Data["__path"] = ne.Path;
        il.Emit(OpCodes.Ldloc, preservedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__path");
        il.Emit(OpCodes.Ldloc, nodeErrLocal);
        il.Emit(OpCodes.Callvirt, runtime.NodeErrorPathGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // throw preserved;
        il.Emit(OpCodes.Ldloc, preservedLocal);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notNodeError);

        // Local for the error code string
        var codeLocal = il.DeclareLocal(_types.String);

        // Default to EINVAL
        il.Emit(OpCodes.Ldstr, "EINVAL");
        il.Emit(OpCodes.Stloc, codeLocal);

        // Check for FileNotFoundException -> ENOENT
        var checkDirNotFound = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.FileNotFoundException);
        il.Emit(OpCodes.Brfalse, checkDirNotFound);
        il.Emit(OpCodes.Ldstr, "ENOENT");
        il.Emit(OpCodes.Stloc, codeLocal);
        var createError = il.DefineLabel();
        il.Emit(OpCodes.Br, createError);

        // Check for DirectoryNotFoundException -> ENOENT
        il.MarkLabel(checkDirNotFound);
        var checkUnauthorized = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DirectoryNotFoundException);
        il.Emit(OpCodes.Brfalse, checkUnauthorized);
        il.Emit(OpCodes.Ldstr, "ENOENT");
        il.Emit(OpCodes.Stloc, codeLocal);
        il.Emit(OpCodes.Br, createError);

        // Check for UnauthorizedAccessException -> EACCES
        il.MarkLabel(checkUnauthorized);
        var checkIO = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(UnauthorizedAccessException));
        il.Emit(OpCodes.Brfalse, checkIO);
        il.Emit(OpCodes.Ldstr, "EACCES");
        il.Emit(OpCodes.Stloc, codeLocal);
        il.Emit(OpCodes.Br, createError);

        // Check for IOException -> EACCES (default for IO errors)
        il.MarkLabel(checkIO);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IOException);
        il.Emit(OpCodes.Brfalse, createError);
        il.Emit(OpCodes.Ldstr, "EACCES");
        il.Emit(OpCodes.Stloc, codeLocal);

        // Create a new Exception with metadata in Data dictionary
        il.MarkLabel(createError);

        // Build message: "CODE: syscall: original_message, path 'path'"
        // var message = code + ": " + syscall + ": " + ex.Message + ", path '" + path + "'";
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldarg_1); // syscall
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldarg_0); // ex
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetMethod!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, ", path '");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldarg_2); // path
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));

        // var newEx = new Exception(message);
        var newExLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, newExLocal);

        // newEx.Data["__nodeError"] = true;  (marker)
        il.Emit(OpCodes.Ldloc, newExLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__nodeError");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // newEx.Data["__code"] = code;
        il.Emit(OpCodes.Ldloc, newExLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__code");
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // newEx.Data["__syscall"] = syscall;
        il.Emit(OpCodes.Ldloc, newExLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__syscall");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // newEx.Data["__path"] = path;
        il.Emit(OpCodes.Ldloc, newExLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__path");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));

        // throw newEx;
        il.Emit(OpCodes.Ldloc, newExLocal);
        il.Emit(OpCodes.Throw);
    }
}
