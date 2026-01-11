using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for string method calls and property access.
/// Handles all TypeScript string methods like charAt, substring, toUpperCase, etc.
/// </summary>
public sealed class StringEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a string receiver.
    /// </summary>
    public bool TryEmitMethodCall(ILEmitter emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the string object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        il.Emit(OpCodes.Castclass, ctx.Types.String);

        switch (methodName)
        {
            case "charAt":
                EmitCharAt(emitter, arguments);
                return true;

            case "substring":
                EmitSubstring(emitter, arguments);
                return true;

            case "indexOf":
                EmitIndexOf(emitter, arguments);
                return true;

            case "toUpperCase":
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.String, "ToUpper"));
                return true;

            case "toLowerCase":
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.String, "ToLower"));
                return true;

            case "trim":
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.String, "Trim"));
                return true;

            case "replace":
                EmitReplace(emitter, arguments);
                return true;

            case "split":
                EmitSplit(emitter, arguments);
                return true;

            case "match":
                EmitMatch(emitter, arguments);
                return true;

            case "search":
                EmitSearch(emitter, arguments);
                return true;

            case "includes":
                EmitIncludes(emitter, arguments);
                return true;

            case "startsWith":
                EmitStartsWith(emitter, arguments);
                return true;

            case "endsWith":
                EmitEndsWith(emitter, arguments);
                return true;

            case "slice":
                EmitSlice(emitter, arguments);
                return true;

            case "repeat":
                EmitRepeat(emitter, arguments);
                return true;

            case "padStart":
                EmitPadStart(emitter, arguments);
                return true;

            case "padEnd":
                EmitPadEnd(emitter, arguments);
                return true;

            case "charCodeAt":
                EmitCharCodeAt(emitter, arguments);
                return true;

            case "concat":
                EmitConcat(emitter, arguments);
                return true;

            case "lastIndexOf":
                EmitLastIndexOf(emitter, arguments);
                return true;

            case "trimStart":
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.String, "TrimStart"));
                return true;

            case "trimEnd":
                il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.String, "TrimEnd"));
                return true;

            case "replaceAll":
                EmitReplaceAll(emitter, arguments);
                return true;

            case "at":
                EmitAt(emitter, arguments);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a string receiver.
    /// </summary>
    public bool TryEmitPropertyGet(ILEmitter emitter, Expr receiver, string propertyName)
    {
        if (propertyName != "length")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the string object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        il.Emit(OpCodes.Castclass, ctx.Types.String);

        // Get length and convert to double (TypeScript number)
        il.Emit(OpCodes.Call, ctx.Types.GetProperty(ctx.Types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);

        return true;
    }

    #region String Method Implementations

    private static void EmitCharAt(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringCharAt);
    }

    private static void EmitSubstring(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringSubstring);
    }

    private static void EmitIndexOf(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringIndexOf);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitReplace(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            // Don't cast - let runtime handle string or RegExp
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
            il.Emit(OpCodes.Call, ctx.Runtime!.Stringify); // replacement is always string
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringReplaceRegExp);
    }

    private static void EmitSplit(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            // Don't cast - let runtime handle string or RegExp
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringSplitRegExp);
    }

    private static void EmitMatch(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringMatchRegExp);
    }

    private static void EmitSearch(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringSearchRegExp);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitIncludes(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringIncludes);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
    }

    private static void EmitStartsWith(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringStartsWith);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
    }

    private static void EmitEndsWith(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringEndsWith);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
    }

    private static void EmitSlice(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringSlice);
    }

    private static void EmitRepeat(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringRepeat);
    }

    private static void EmitPadStart(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringPadStart);
    }

    private static void EmitPadEnd(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringPadEnd);
    }

    private static void EmitCharCodeAt(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringCharCodeAt);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitConcat(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringConcat);
    }

    private static void EmitLastIndexOf(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringLastIndexOf);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitReplaceAll(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
            il.Emit(OpCodes.Castclass, ctx.Types.String);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldstr, "");
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringReplaceAll);
    }

    private static void EmitAt(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.StringAt);
    }

    #endregion
}
