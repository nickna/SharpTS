using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Shared emit helpers for raising a guest error from emitted runtime code:
/// <c>throw CreateException(new $XxxError(message))</c>. This tail was previously open-coded at
/// every throw site (~170x across the emitter files); any change to the raise idiom (e.g. the
/// CreateException wrapping) now lands once. Usable from every emitter class — the RuntimeEmitter
/// partials pass their local <c>il</c>/<c>runtime</c>, the expression emitters pass
/// <c>IL</c>/<c>Ctx.Runtime!</c>.
/// </summary>
internal static class GuestErrorEmitter
{
    /// <summary>Emits <c>throw CreateException(new errorCtor(message))</c>.</summary>
    public static void ThrowError(ILGenerator il, EmittedRuntime runtime, ConstructorInfo errorCtor, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        ThrowErrorFromStack(il, runtime, errorCtor);
    }

    /// <summary>
    /// Emits <c>throw CreateException(new errorCtor(message))</c> with the message string
    /// already on the evaluation stack (for computed messages).
    /// </summary>
    public static void ThrowErrorFromStack(ILGenerator il, EmittedRuntime runtime, ConstructorInfo errorCtor)
    {
        il.Emit(OpCodes.Newobj, errorCtor);
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);
    }

    public static void ThrowTypeError(ILGenerator il, EmittedRuntime runtime, string message) =>
        ThrowError(il, runtime, runtime.TSTypeErrorCtor, message);

    public static void ThrowRangeError(ILGenerator il, EmittedRuntime runtime, string message) =>
        ThrowError(il, runtime, runtime.TSRangeErrorCtor, message);

    public static void ThrowSyntaxError(ILGenerator il, EmittedRuntime runtime, string message) =>
        ThrowError(il, runtime, runtime.TSSyntaxErrorCtor, message);
}
