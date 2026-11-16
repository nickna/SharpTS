using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private void EmitGlobalCompoundAssignment(Expr.CompoundAssign assignment)
    {
        var reference = CaptureUnresolvedAssignmentReference(assignment.Name.Lexeme);
        // GetValue must fail before RHS evaluation for a missing identifier,
        // and an accessor must be read only once before RHS side effects.
        EmitVariable(new Expr.Variable(assignment.Name));
        EnsureBoxed();
        var current = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, current);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        var right = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, right);
        IL.Emit(OpCodes.Ldloc, current);
        IL.Emit(OpCodes.Ldloc, right);
        _helpers.EmitCompoundOperation(assignment.Operator.Type, _ctx.Runtime!.Operators.Add);
        IL.Emit(OpCodes.Dup);
        EmitUnresolvedAssignmentStore(assignment.Name.Lexeme, reference);
        ReleaseUnresolvedAssignmentReference(reference);
        SetStackUnknown();
    }

    private void EmitGlobalLogicalAssignment(Expr.LogicalAssign assignment)
    {
        var reference = CaptureUnresolvedAssignmentReference(assignment.Name.Lexeme);
        var done = IL.DefineLabel();
        EmitVariable(new Expr.Variable(assignment.Name));
        EnsureBoxed();
        IL.Emit(OpCodes.Dup);
        EmitLogicalConditionCheck(assignment.Operator.Type, done);
        IL.Emit(OpCodes.Pop);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        IL.Emit(OpCodes.Dup);
        EmitUnresolvedAssignmentStore(assignment.Name.Lexeme, reference);
        IL.MarkLabel(done);
        ReleaseUnresolvedAssignmentReference(reference);
        SetStackUnknown();
    }
}
