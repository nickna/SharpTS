using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static object FsMkdtempSync(object prefix)
    /// Creates a unique temporary directory.
    /// </summary>
    private void EmitFsMkdtempSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsMkdtempSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsMkdtempSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);

        // Convert prefix to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var prefixLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, prefixLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "mkdtemp", afterTry =>
        {
            // random = Path.GetRandomFileName().Replace(".", "")
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetRandomFileName"));
            il.Emit(OpCodes.Ldstr, ".");
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [typeof(string), typeof(string)])!);
            var randomLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, randomLocal);

            // suffix = prefix + random  (Node appends the random suffix to the prefix)
            il.Emit(OpCodes.Ldloc, prefixLocal);
            il.Emit(OpCodes.Ldloc, randomLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [typeof(string), typeof(string)])!);
            var suffixLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, suffixLocal);

            // result = Path.Combine(Path.GetTempPath(), suffix). Path.Combine returns
            // `suffix` verbatim when it is rooted, so an absolute prefix (the canonical
            // mkdtempSync(path.join(os.tmpdir(),'foo-'))) is NOT doubled — matches the
            // interpreter and Node. A bare relative prefix lands under the temp dir.
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetTempPath"));
            il.Emit(OpCodes.Ldloc, suffixLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
            il.Emit(OpCodes.Stloc, resultLocal);

            // Directory.CreateDirectory(result)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "CreateDirectory", _types.String));
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsOpendirSync(object path)
    /// Opens a directory for iteration.
    /// </summary>
    private void EmitFsOpendirSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsOpendirSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsOpendirSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "opendir", afterTry =>
        {
            // Check if directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw DirectoryNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory, opendir '");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldstr, "'");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [typeof(string), typeof(string), typeof(string)])!);
            il.Emit(OpCodes.Newobj, typeof(DirectoryNotFoundException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Create new $Dir(path) using emitted type
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, runtime.DirCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsLinkSync(object existingPath, object newPath)
    /// Creates a hard link.
    /// </summary>
    private void EmitFsLinkSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLinkSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsLinkSync = method;

        var il = method.GetILGenerator();

        // Convert paths to strings
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var existingPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, existingPathLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var newPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, newPathLocal);

        EmitWithFsErrorHandling(il, runtime, newPathLocal, "link", afterTry =>
        {
            // Check if source exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Check if destination already exists
            var notExistsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, notExistsLabel);

            // Throw IOException for EEXIST
            // Use Concat overload that takes string array
            il.Emit(OpCodes.Ldc_I4_5);
            il.Emit(OpCodes.Newarr, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldstr, "EEXIST: file already exists, link '");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldstr, "' -> '");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Ldstr, "'");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [typeof(string[])])!);
            il.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notExistsLabel);

            // CreateHardLinkPure(existingPath, newPath) via P/Invoke (Phase 21)
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, runtime.CreateHardLinkPure);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }
}
