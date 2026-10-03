using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitConfigureWindowsErrorReporting(ILGenerator il)
    {
        // Native imports are separate from guest program functions and their IL bodies.
        var controls = EmitTypeDefinitions.DefineType(_moduleBuilder,
            "$WindowsErrorReporting", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed,
            _types.Object);
        MethodBuilder DefineControl(string name, string entryPoint, Type[] parameters)
        {
            var method = EmitTypeDefinitions.DefinePInvokeMethod(controls, name, "kernel32.dll",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
                CallingConventions.Standard, _types.UInt32, parameters, CallingConvention.Winapi, CharSet.Unicode, entryPoint);
            method.SetImplementationFlags(MethodImplAttributes.PreserveSig);
            return method;
        }

        var getMode = DefineControl("$GetWindowsErrorMode", "GetErrorMode", []);
        var setMode = DefineControl("$SetWindowsErrorMode", "SetErrorMode", [_types.UInt32]);
        controls.CreateType();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Resolve("System.OperatingSystem"), "IsWindows"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Assembly, "GetEntryAssembly"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Assembly, "GetExecutingAssembly"));
        il.Emit(OpCodes.Bne_Un, done);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "IsErrorRedirected"));
        il.Emit(OpCodes.Brfalse, done);

        // Preserve inherited error-mode flags. Unattended executable errors must
        // complete rather than wait in the Windows fatal-error reporting path.
        // This leaves the CLR's exception text, stack and nonzero status intact.
        il.Emit(OpCodes.Call, getMode);
        il.Emit(OpCodes.Ldc_I4, 2); // SEM_NOGPFAULTERRORBOX
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Call, setMode);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(done);
    }
}
