using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits path module helper methods.
    /// </summary>
    private void EmitPathModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitPathFormat(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static string PathFormat(object pathObject)
    /// Implements path.format() which reconstructs a path from a parsed path object.
    /// </summary>
    private void EmitPathFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.PathFormat = method;

        var il = method.GetILGenerator();

        // Local variables
        var dirLocal = il.DeclareLocal(_types.String);      // 0: dir
        var rootLocal = il.DeclareLocal(_types.String);     // 1: root
        var baseLocal = il.DeclareLocal(_types.String);     // 2: base
        var nameLocal = il.DeclareLocal(_types.String);     // 3: name
        var extLocal = il.DeclareLocal(_types.String);      // 4: ext
        var resultLocal = il.DeclareLocal(_types.String);   // 5: result

        // Get the GetProperty method for extracting properties from the object
        var getPropertyMethod = runtime.GetProperty;

        // Helper to emit: string prop = GetProperty(pathObject, "propName")?.ToString() ?? ""
        void EmitGetStringProperty(string propName, LocalBuilder local)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, propName);
            il.Emit(OpCodes.Call, getPropertyMethod);

            // Convert to string
            var notNull = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notNull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
            il.MarkLabel(done);
            il.Emit(OpCodes.Stloc, local);
        }

        // Extract all properties
        EmitGetStringProperty("dir", dirLocal);
        EmitGetStringProperty("root", rootLocal);
        EmitGetStringProperty("base", baseLocal);
        EmitGetStringProperty("name", nameLocal);
        EmitGetStringProperty("ext", extLocal);

        // Algorithm from Node.js path.format():
        // 1. If base is provided, use dir + sep + base
        // 2. Otherwise, use dir + sep + name + ext
        // 3. If dir is empty but root is set, prepend root

        var hasBase = il.DefineLabel();
        var buildFromParts = il.DefineLabel();
        var checkDir = il.DefineLabel();
        var addSepAndBase = il.DefineLabel();
        var done2 = il.DefineLabel();

        // Check if base has a value
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasBase);

        // base is empty, build from name + ext
        il.MarkLabel(buildFromParts);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, baseLocal);
        il.Emit(OpCodes.Br, checkDir);

        il.MarkLabel(hasBase);

        // Check if dir has a value
        il.MarkLabel(checkDir);
        il.Emit(OpCodes.Ldloc, dirLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, addSepAndBase);

        // dir is empty, check if root is set
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, done2);

        // Return root + base
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // dir has a value, return dir + sep + base
        il.MarkLabel(addSepAndBase);
        il.Emit(OpCodes.Ldloc, dirLocal);

        // Get separator as string using static Char.ToString(char)
        il.Emit(OpCodes.Ldsfld, _types.GetField(_types.Path, "DirectorySeparatorChar"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));

        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // Return just base (no dir, no root)
        il.MarkLabel(done2);
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Ret);
    }
}
