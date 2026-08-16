using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles compile-time lowering of <c>fetch.cookieJar.{getCookies,setCookie,clear}(...)</c>
/// into direct calls on the emitted runtime's cookie-jar helpers.
/// </summary>
/// <remarks>
/// Why a compile-time lowering instead of a runtime <c>$Fetch</c> object with
/// properties: in compiled mode <c>fetch</c> resolves to a <c>$TSFunction</c> wrapping
/// the static <c>Fetch</c> method. <c>$TSFunction</c> has no concept of static
/// properties, so <c>fetch.cookieJar</c> would otherwise resolve to undefined at
/// runtime. Detecting the chain syntactically and emitting a direct call to the
/// corresponding <c>CookieJar*</c> static helper avoids needing a per-fetch wrapper
/// object and keeps zero-overhead pure-IL execution.
///
/// Only the inline form <c>fetch.cookieJar.METHOD(args)</c> is supported in compiled
/// mode. Aliasing <c>const cj = fetch.cookieJar;</c> first will not work — the user
/// would have to inline the chain. The interpreter mode (via
/// <see cref="Runtime.Types.SharpTSFetchGlobal"/>) supports both forms.
/// </remarks>
public class CookieJarHandler : ICallHandler
{
    public int Priority => 44; // Before FetchHandler (46) and GlobalFunctionHandler (50)

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        // Match: fetch.cookieJar.METHOD(args)
        if (call.Callee is not Expr.Get outerGet) return false;
        if (outerGet.Object is not Expr.Get innerGet) return false;
        if (innerGet.Object is not Expr.Variable v) return false;
        if (v.Name.Lexeme != "fetch") return false;
        if (innerGet.Name.Lexeme != "cookieJar") return false;

        var method = outerGet.Name.Lexeme;
        var runtime = emitter.Context.Runtime!;
        var il = emitter.IL;

        switch (method)
        {
            case "getCookies":
            {
                // CookieJarGetCookies(string url) -> string
                if (call.Arguments.Count >= 1)
                {
                    emitter.EmitExpression(call.Arguments[0]);
                    EmitToString(emitter);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "");
                }
                il.Emit(OpCodes.Call, runtime.CookieJarGetCookies);
                emitter.SetStackUnknown();
                return true;
            }
            case "setCookie":
            {
                // CookieJarSetCookie(string cookie, string url) -> void
                if (call.Arguments.Count >= 1)
                {
                    emitter.EmitExpression(call.Arguments[0]);
                    EmitToString(emitter);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "");
                }
                if (call.Arguments.Count >= 2)
                {
                    emitter.EmitExpression(call.Arguments[1]);
                    EmitToString(emitter);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "");
                }
                il.Emit(OpCodes.Call, runtime.CookieJarSetCookie);
                // setCookie returns undefined in JS land — push the undefined sentinel.
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                emitter.SetStackUnknown();
                return true;
            }
            case "clear":
            {
                il.Emit(OpCodes.Call, runtime.CookieJarClear);
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                emitter.SetStackUnknown();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Coerces the value on the stack to a string. Uses Object.ToString() with a null guard.
    /// </summary>
    private static void EmitToString(IEmitterContext emitter)
    {
        var il = emitter.IL;
        var notNullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString", Type.EmptyTypes)!);
        il.MarkLabel(doneLabel);
    }
}
