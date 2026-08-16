using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// <c>primitive:tty</c> runtime support. Emits a single <c>Tty_isatty</c> method
/// that checks whether a file descriptor is a TTY. The user-facing <c>tty</c>
/// module lives in <c>stdlib/node/tty.ts</c> and calls this primitive.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the <c>Tty_isatty</c> runtime method backing <c>primitive:tty.isatty</c>.
    /// </summary>
    internal void EmitTtyPrimitiveMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit: public static object Tty_isatty(object? fd)
        var method = typeBuilder.DefineMethod(
            "Tty_isatty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Coerce fd → int via $Runtime.ToNumber (handles undefined/null/NaN/Infinity gracefully).
        // Use a try/catch around Convert.ToInt32 so non-finite doubles don't propagate as
        // OverflowException (e.g. Debug package passes `process.stderr.fd` which may be NaN
        // when the host has no real FDs).
        var fdInt = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        var dblLocal = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Stloc, dblLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, dblLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, fdInt);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, fdInt);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, fdInt);

        var case1 = il.DefineLabel();
        var case2 = il.DefineLabel();
        var defaultCase = il.DefineLabel();
        var done = il.DefineLabel();

        // fd == 0?
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bne_Un, case1);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Call, typeof(Console).GetProperty("IsInputRedirected")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, done);

        // fd == 1?
        il.MarkLabel(case1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, case2);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Call, typeof(Console).GetProperty("IsOutputRedirected")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, done);

        // fd == 2?
        il.MarkLabel(case2);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, defaultCase);
        il.Emit(OpCodes.Call, typeof(Console).GetProperty("IsErrorRedirected")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, done);

        // default: false
        il.MarkLabel(defaultCase);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        runtime.TtyIsatty = method;
        // No RegisterBuiltInModuleMethod — the `tty` module is now a TS stdlib
        // file (stdlib/node/tty.ts) that calls primitive:tty. CJS require('tty')
        // flows through the standard ESM→CJS namespace-object path.
    }
}
