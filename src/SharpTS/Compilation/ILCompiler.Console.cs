using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitInitializeConsoleOutput(ILGenerator il)
    {
        // Windows hidden processes have no inherited console code page and can
        // default to OEM encoding even when stdout is a redirected pipe. Define
        // the executable's pipe output contract before the first guest write.
        var done = il.DefineLabel();
        // Main can also be invoked by an embedding host. Preserve that host's
        // Console writers and encoding rather than resetting its redirected capture.
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Assembly, "GetEntryAssembly"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Assembly, "GetExecutingAssembly"));
        il.Emit(OpCodes.Bne_Un, done);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "IsOutputRedirected"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Encoding, "UTF8"));
        il.Emit(OpCodes.Call, _types.GetPropertySetter(_types.Console, "OutputEncoding"));
        il.MarkLabel(done);
    }
}
