using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected void EmitSetSuper(Expr.SetSuper expression)
    {
        var receiver = SpillBoxed(new Expr.This(
            new Token(TokenType.THIS, "this", null, expression.Target.Keyword.Line)));

        // The method's lexical owner selects the base, independently of a
        // borrowed or inherited method's receiver. Read the live link now,
        // before an RHS can mutate the prototype chain or suspend execution.
        if (Ctx.CurrentClassExpr != null && TryEmitOwnedClassDefinition()) { }
        else
        {
            IL.Emit(OpCodes.Ldtoken, Ctx.CurrentClassBuilder!);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        }
        if (!Ctx.IsStaticSuperContext)
        {
            IL.Emit(OpCodes.Ldstr, "prototype");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectRead.Property);
        }
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectPrototypes.GetPrototypeOf);
        var target = _helpers.SpillStoreObject();
        var complete = IL.DefineLabel();
        bool logical = expression.Operator?.Type is TokenType.AND_AND_EQUAL or TokenType.OR_OR_EQUAL or TokenType.QUESTION_QUESTION_EQUAL;
        LocalBuilder? current = null;
        if (expression.Operator is { } op)
        {
            IL.Emit(OpCodes.Ldloc, target);
            IL.Emit(OpCodes.Ldstr, expression.Target.Method!.Lexeme);
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Call, Ctx.Runtime.Reflect.Get);
            current = _helpers.SpillStoreObject();
            if (logical)
            {
                IL.Emit(OpCodes.Ldloc, current);
                IL.Emit(OpCodes.Dup);
                EmitLogicalConditionCheck(op.Type, complete);
                IL.Emit(OpCodes.Pop);
            }
        }
        var value = SpillBoxed(expression.Value);
        if (expression.Operator is { } compound && !logical)
        {
            IL.Emit(OpCodes.Ldloc, current!);
            IL.Emit(OpCodes.Ldloc, value);
            EmitCompoundOperation(compound.Type);
            IL.Emit(OpCodes.Stloc, value);
        }

        IL.Emit(OpCodes.Ldloc, target);
        IL.Emit(OpCodes.Ldstr, expression.Target.Method!.Lexeme);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Call, Ctx.Runtime.Reflect.RequireAssignment().Set);
        var assigned = IL.DefineLabel();
        IL.Emit(OpCodes.Brtrue, assigned);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime,
            $"Cannot assign to super property '{expression.Target.Method.Lexeme}'");
        IL.MarkLabel(assigned);
        IL.Emit(OpCodes.Ldloc, value);
        IL.MarkLabel(complete);
        SetStackUnknown();
    }
}
