using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    public override void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // Pop unused result if any
                _il.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.Const c:
                EmitVarDeclaration(new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer));
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.Block b:
                foreach (var s in b.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Print p:
                EmitPrint(p);
                break;

            case Stmt.ForOf f:
                EmitForOf(f);
                break;

            case Stmt.DoWhile dw:
                EmitDoWhile(dw);
                break;

            case Stmt.For f:
                EmitFor(f);
                break;

            case Stmt.ForIn fi:
                EmitForIn(fi);
                break;

            case Stmt.Throw t:
                EmitThrow(t);
                break;

            case Stmt.Switch s:
                EmitSwitch(s);
                break;

            case Stmt.TryCatch t:
                EmitTryCatch(t);
                break;

            case Stmt.Break b:
                EmitBreak(b);
                break;

            case Stmt.Continue c:
                EmitContinue(c);
                break;

            case Stmt.LabeledStatement ls:
                EmitLabeledStatement(ls);
                break;

            // Skip other statements for now
            default:
                break;
        }
    }
}
