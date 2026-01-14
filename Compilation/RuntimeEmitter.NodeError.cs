using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns.Modules;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the ConvertToNodeError helper method.
    /// </summary>
    /// <remarks>
    /// Creates a method that converts .NET exceptions to NodeError objects with
    /// proper error codes (ENOENT, EACCES, etc.) for Node.js compatibility.
    /// </remarks>
    private void EmitNodeErrorHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitConvertToNodeError(typeBuilder, runtime);
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

    /// <summary>
    /// Emits: public static object ConvertToNodeError(Exception ex, string syscall, string path)
    /// Returns a SharpTSObject with code, syscall, path, message, and name properties.
    /// </summary>
    private void EmitConvertToNodeError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConvertToNodeError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Exception, _types.String, _types.String]
        );
        runtime.ConvertToNodeError = method;

        var il = method.GetILGenerator();

        // Get the error code from the exception type
        // We'll implement a simple switch on exception type

        var codeLocal = il.DeclareLocal(_types.String);

        // Default to EINVAL
        il.Emit(OpCodes.Ldstr, "EINVAL");
        il.Emit(OpCodes.Stloc, codeLocal);

        // Check for FileNotFoundException -> ENOENT
        var checkDirNotFound = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(FileNotFoundException));
        il.Emit(OpCodes.Brfalse, checkDirNotFound);
        il.Emit(OpCodes.Ldstr, "ENOENT");
        il.Emit(OpCodes.Stloc, codeLocal);
        var createObject = il.DefineLabel();
        il.Emit(OpCodes.Br, createObject);

        // Check for DirectoryNotFoundException -> ENOENT
        il.MarkLabel(checkDirNotFound);
        var checkUnauthorized = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(DirectoryNotFoundException));
        il.Emit(OpCodes.Brfalse, checkUnauthorized);
        il.Emit(OpCodes.Ldstr, "ENOENT");
        il.Emit(OpCodes.Stloc, codeLocal);
        il.Emit(OpCodes.Br, createObject);

        // Check for UnauthorizedAccessException -> EACCES
        il.MarkLabel(checkUnauthorized);
        var checkIO = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(UnauthorizedAccessException));
        il.Emit(OpCodes.Brfalse, checkIO);
        il.Emit(OpCodes.Ldstr, "EACCES");
        il.Emit(OpCodes.Stloc, codeLocal);
        il.Emit(OpCodes.Br, createObject);

        // Check for IOException -> EACCES (default for IO errors)
        il.MarkLabel(checkIO);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(IOException));
        il.Emit(OpCodes.Brfalse, createObject);
        il.Emit(OpCodes.Ldstr, "EACCES");
        il.Emit(OpCodes.Stloc, codeLocal);

        // Create the error object dictionary
        il.MarkLabel(createObject);

        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // name: "Error"
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldstr, "Error");
        il.Emit(OpCodes.Call, addMethod);

        // code: <error code>
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Ldloc, codeLocal);
        il.Emit(OpCodes.Call, addMethod);

        // syscall: <syscall arg>
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "syscall");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, addMethod);

        // path: <path arg>
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "path");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, addMethod);

        // message: format as "CODE: syscall: message 'path'"
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "message");

        // Build message string: code + ": " + syscall + ": " + ex.Message + " '" + path + "'"
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
        il.Emit(OpCodes.Ldstr, " '");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldarg_2); // path
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject and return
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }
}
