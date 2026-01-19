using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'util' module.
/// </summary>
public sealed class UtilModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "util";

    private static readonly string[] _exportedMembers =
    [
        "format", "inspect", "types"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "format" => EmitFormat(emitter, arguments),
            "inspect" => EmitInspect(emitter, arguments),
            // Handle util.types.* nested calls
            "types.isArray" => EmitTypesIsArray(emitter, arguments),
            "types.isFunction" => EmitTypesIsFunction(emitter, arguments),
            "types.isNull" => EmitTypesIsNull(emitter, arguments),
            "types.isUndefined" => EmitTypesIsUndefined(emitter, arguments),
            "types.isDate" => EmitTypesIsDate(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // util.types is a nested object - handled via nested call pattern in BuiltInModuleHandler
        return false;
    }

    private static bool EmitFormat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Build array of arguments
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

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilFormat);
        return true;
    }

    private static bool EmitInspect(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit object to inspect
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit options (or null)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilInspect);
        return true;
    }

    private static bool EmitTypesIsArray(IEmitterContext emitter, List<Expr> arguments)
    {
        return EmitTypesCheck(emitter, arguments, nameof(UtilHelpers.IsArray));
    }

    private static bool EmitTypesIsFunction(IEmitterContext emitter, List<Expr> arguments)
    {
        return EmitTypesCheck(emitter, arguments, nameof(UtilHelpers.IsFunction));
    }

    private static bool EmitTypesIsNull(IEmitterContext emitter, List<Expr> arguments)
    {
        return EmitTypesCheck(emitter, arguments, nameof(UtilHelpers.IsNull));
    }

    private static bool EmitTypesIsUndefined(IEmitterContext emitter, List<Expr> arguments)
    {
        return EmitTypesCheck(emitter, arguments, nameof(UtilHelpers.IsUndefined));
    }

    private static bool EmitTypesIsDate(IEmitterContext emitter, List<Expr> arguments)
    {
        return EmitTypesCheck(emitter, arguments, nameof(UtilHelpers.IsDate));
    }

    private static bool EmitTypesCheck(IEmitterContext emitter, List<Expr> arguments, string methodName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the argument (or null if no argument)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call UtilHelpers.IsXxx(value)
        var method = typeof(UtilHelpers).GetMethod(methodName)!;
        il.Emit(OpCodes.Call, method);

        // Box the boolean result
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }
}
