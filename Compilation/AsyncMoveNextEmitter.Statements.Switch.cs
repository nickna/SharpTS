using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
    }

    protected override void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    protected override void EmitSwitch(Stmt.Switch s)
    {
        var endLabel = _il.DefineLabel();
        var defaultLabel = _il.DefineLabel();
        var caseLabels = s.Cases.Select(_ => _il.DefineLabel()).ToList();

        // Evaluate subject once
        EmitExpression(s.Subject);
        EnsureBoxed();
        var subjectLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
            _il.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        // Jump to default or end
        if (s.DefaultBody == null)
        {
            _il.Emit(OpCodes.Br, endLabel);
        }
        else
        {
            _il.Emit(OpCodes.Br, defaultLabel);
        }

        // Emit case bodies
        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break)
                {
                    // Unlabeled break - exits switch only
                    _il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
            // Fall through if no break
        }

        // Default case
        if (s.DefaultBody != null)
        {
            _il.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break)
                {
                    _il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
        }

        _il.MarkLabel(endLabel);
    }
}
