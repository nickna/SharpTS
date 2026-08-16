using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits strict mode helper methods:
    /// - ThrowStrictSyntaxError(string message): throws SyntaxError in strict mode
    /// - WarnSloppyDeleteVariable(string varName): warns in sloppy mode for delete variable
    /// </summary>
    private void EmitStrictModeHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitThrowStrictSyntaxError(typeBuilder, runtime);
        EmitWarnSloppyDeleteVariable(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits ThrowStrictSyntaxError(string message) -> void
    /// Throws an exception with SyntaxError prefix.
    /// </summary>
    private void EmitThrowStrictSyntaxError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ThrowStrictSyntaxError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.String]
        );
        runtime.ThrowStrictSyntaxError = method;

        var il = method.GetILGenerator();

        // throw new Exception("SyntaxError: " + message)
        il.Emit(OpCodes.Ldstr, "SyntaxError: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits WarnSloppyDeleteVariable(string varName) -> bool
    /// Logs warning and returns false (sloppy mode delete variable behavior).
    /// </summary>
    private void EmitWarnSloppyDeleteVariable(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WarnSloppyDeleteVariable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String]
        );
        runtime.WarnSloppyDeleteVariable = method;

        var il = method.GetILGenerator();

        // Console.Error.WriteLine("[Warning] Silent failure: delete variable - delete {varName} returns false in sloppy mode")
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "get_Error"));
        il.Emit(OpCodes.Ldstr, "[Warning] Silent failure: delete variable - delete ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, " returns false in sloppy mode");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));

        // return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
