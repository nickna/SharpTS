using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private readonly record struct PrivateAccessorReference(string Owner, string Name,
        PrivateMethodValue? Getter, PrivateMethodValue? Setter);

    private PrivateAccessorReference? FindPrivateAccessor(Token token)
    {
        if (Ctx.CurrentClassName is not { } current) return null;
        string name = token.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var registry = Ctx.ClassRegistry;
        var getter = registry?.GetPrivateMethodValue(owner, PrivateAccessorNames.Getter(name));
        var setter = registry?.GetPrivateMethodValue(owner, PrivateAccessorNames.Setter(name));
        return getter is null && setter is null ? null : new(owner, name, getter, setter);
    }

    protected bool TryEmitPrivateAccessor(Expr receiverExpression, Token token, Expr? assignedExpression = null)
    {
        if (FindPrivateAccessor(token) is not { } accessor) return false;
        var receiver = SpillBoxed(receiverExpression);
        var assignedValue = assignedExpression is null ? null : SpillBoxed(assignedExpression);
        EmitPrivateAccessorFacet(accessor, receiver, assignedValue);
        if (assignedValue is not null)
        {
            IL.Emit(OpCodes.Ldloc, assignedValue);
            SetStackUnknown();
        }
        return true;
    }

    private void EmitPrivateAccessorFacet(PrivateAccessorReference accessor, LocalBuilder receiver, LocalBuilder? assignedValue = null)
    {
        bool isWrite = assignedValue is not null;
        var facet = isWrite ? accessor.Setter : accessor.Getter;
        // A wrong-brand access fails before checking whether the facet exists.
        var brand = facet ?? accessor.Getter ?? accessor.Setter!;
        EmitPrivateMethodValue(brand, accessor.Owner, brand.Source.Name.Lexeme.TrimStart('#'), receiver, accessor.Name);
        if (facet is null)
        {
            IL.Emit(OpCodes.Pop);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!,
                $"Private accessor #{accessor.Name} does not have a {(isWrite ? "setter" : "getter")}");
            return;
        }
        if (!isWrite)
        {
            EmitGuestMethodInvocation(receiver, []);
            return;
        }

        var callable = _helpers.SpillStoreObject();
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Ldloc, callable);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Newarr, Types.Object);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ldloc, assignedValue!);
        IL.Emit(OpCodes.Stelem_Ref);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Invocation.Method);
        IL.Emit(OpCodes.Pop);
    }
}
