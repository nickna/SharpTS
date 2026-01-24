using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Properties;

/// <summary>
/// Property emitter strategy for enum member access.
/// Handles expressions like Direction.Up -> 0 or Status.Success -> "SUCCESS".
/// </summary>
public sealed class EnumPropertyEmitter : IPropertyEmitterStrategy
{
    /// <summary>
    /// Enum property access has high priority (10) since it's a specific pattern.
    /// </summary>
    public int Priority => 10;

    /// <inheritdoc/>
    public bool CanHandleGet(IEmitterContext emitter, Expr.Get get)
    {
        if (get.Object is not Expr.Variable enumVar)
            return false;

        var ctx = emitter.Context;
        var resolvedName = ctx.ResolveEnumName(enumVar.Name.Lexeme);

        return ctx.EnumMembers?.TryGetValue(resolvedName, out var members) == true
            && members.ContainsKey(get.Name.Lexeme);
    }

    /// <inheritdoc/>
    public bool TryEmitGet(IEmitterContext emitter, Expr.Get get)
    {
        if (get.Object is not Expr.Variable enumVar)
            return false;

        var ctx = emitter.Context;
        var resolvedName = ctx.ResolveEnumName(enumVar.Name.Lexeme);

        if (ctx.EnumMembers?.TryGetValue(resolvedName, out var members) != true)
            return false;

        if (!members!.TryGetValue(get.Name.Lexeme, out var value))
            return false;

        var il = emitter.IL;
        var types = ctx.Types;

        if (value is double d)
        {
            il.Emit(OpCodes.Ldc_R8, d);
            il.Emit(OpCodes.Box, types.Double);
            emitter.SetStackUnknown();
        }
        else if (value is string s)
        {
            il.Emit(OpCodes.Ldstr, s);
            emitter.SetStackType(StackType.String);
        }

        return true;
    }

    /// <inheritdoc/>
    public bool CanHandleSet(IEmitterContext emitter, Expr.Set set)
    {
        // Enum members are read-only, cannot be set
        return false;
    }

    /// <inheritdoc/>
    public bool TryEmitSet(IEmitterContext emitter, Expr.Set set)
    {
        // Enum members are read-only, cannot be set
        return false;
    }
}
