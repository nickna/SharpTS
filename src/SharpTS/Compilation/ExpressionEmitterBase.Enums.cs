using System.Reflection.Emit;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitEnumVariable(string name)
    {
        if (Ctx.EnumValueFields == null || !Ctx.EnumValueFields.TryGetValue(Ctx.ResolveEnumName(name), out var field))
            return false;

        // Ordinary enums have a hoisted value binding, initialized at their declaration.
        var initialized = IL.DefineLabel();
        IL.Emit(OpCodes.Ldsfld, field);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brtrue, initialized);
        IL.Emit(OpCodes.Pop);
        EmitUndefinedConstant();
        IL.MarkLabel(initialized);
        SetStackUnknown();
        return true;
    }
}
