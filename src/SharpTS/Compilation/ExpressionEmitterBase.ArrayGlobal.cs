using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private bool IsLiveArrayConstructor(Expr expression) =>
        Ctx.RuntimeFeatures?.UsesArrayConstructorMutation == true &&
        expression is Expr.Variable { Name.Lexeme: "Array" } &&
        !Resolver.HasVariable("Array") && !Ctx.HasVisibleValueBinding("Array");

    protected bool TryEmitArrayGlobalAssignment(Expr.Assign assignment)
    {
        if (!IsLiveArrayConstructor(new Expr.Variable(assignment.Name))) return false;
        var value = SpillBoxed(assignment.Value);
        if (Ctx.IsStrictMode)
        {
            // Strict PutValue checks presence without reading an accessor value.
            var present = IL.DefineLabel();
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.GlobalObject.SingletonField);
            IL.Emit(OpCodes.Ldstr, "Array");
            IL.Emit(OpCodes.Call, Ctx.Runtime.ObjectOwnProperties.HasOwnProperty);
            IL.Emit(OpCodes.Brtrue, present);
            IL.Emit(OpCodes.Ldstr, "Array");
            IL.Emit(OpCodes.Call, Ctx.Runtime.Errors.ThrowUndefinedVariable);
            IL.MarkLabel(present);
        }
        IL.Emit(OpCodes.Ldstr, "Array");
        IL.Emit(OpCodes.Ldloc, value);
        EmitGlobalPropertyStore();
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitLiveArrayConstruction(Expr.New expression)
    {
        if (!IsLiveArrayConstructor(expression.Callee)) return false;
        EmitDynamicConstruction(expression);
        return true;
    }
}
