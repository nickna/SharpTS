using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

internal static class ProcessHostEmitter
{
    internal static void EmitPlatform(ILGenerator il)
    {
        var done = il.DefineLabel();
        foreach (var (method, name) in new[] { ("IsWindows", "win32"), ("IsLinux", "linux"), ("IsMacOS", "darwin") })
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod(method, Type.EmptyTypes)!);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "unknown");
        il.MarkLabel(done);
    }

    internal static void EmitArchitecture(ILGenerator il)
    {
        var architecture = il.DeclareLocal(typeof(Architecture));
        var done = il.DefineLabel();
        il.Emit(OpCodes.Call, typeof(RuntimeInformation).GetProperty(nameof(RuntimeInformation.ProcessArchitecture))!.GetMethod!);
        il.Emit(OpCodes.Stloc, architecture);
        foreach (var (value, name) in new[] { (Architecture.X64, "x64"), (Architecture.X86, "ia32"), (Architecture.Arm64, "arm64"), (Architecture.Arm, "arm") })
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, architecture);
            il.Emit(OpCodes.Ldc_I4, (int)value);
            il.Emit(OpCodes.Bne_Un, next);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "unknown");
        il.MarkLabel(done);
    }
}
