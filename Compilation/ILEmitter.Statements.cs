using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Statement emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitVarDeclaration(Stmt.Var v)
    {
        var local = _ctx.Locals.DeclareLocal(v.Name.Lexeme, typeof(object));

        if (v.Initializer != null)
        {
            EmitExpression(v.Initializer);
            EmitBoxIfNeeded(v.Initializer);
            IL.Emit(OpCodes.Stloc, local);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    private void EmitIf(Stmt.If i)
    {
        // Check for dead code elimination optimization
        var branchResult = _ctx.DeadCode?.GetIfResult(i) ?? IfBranchResult.BothReachable;

        switch (branchResult)
        {
            case IfBranchResult.OnlyThenReachable:
                // Condition is always true - emit only then branch
                EmitStatement(i.ThenBranch);
                return;

            case IfBranchResult.OnlyElseReachable:
                // Condition is always false - emit only else branch (or nothing)
                if (i.ElseBranch != null)
                {
                    EmitStatement(i.ElseBranch);
                }
                return;
        }

        // BothReachable: emit both branches with condition check
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        EmitExpression(i.Condition);
        // For comparisons, unbox the bool result; for others, apply truthy check
        if (IsComparisonExpr(i.Condition))
        {
            IL.Emit(OpCodes.Unbox_Any, typeof(bool));
        }
        else if (i.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else if (i.Condition is Expr.Literal { Value: bool })
        {
            // Boolean literals push int directly, no boxing needed
        }
        else
        {
            EmitTruthyCheck();
        }
        IL.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
        {
            EmitStatement(i.ElseBranch);
        }

        IL.MarkLabel(endLabel);
    }

    private void EmitWhile(Stmt.While w)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        _ctx.EnterLoop(endLabel, startLabel);

        IL.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        // For comparisons, unbox the bool result; for others, apply truthy check
        if (IsComparisonExpr(w.Condition))
        {
            IL.Emit(OpCodes.Unbox_Any, typeof(bool));
        }
        else if (w.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else if (w.Condition is Expr.Literal { Value: bool })
        {
            // Boolean literals push int directly, no boxing needed
        }
        else
        {
            EmitTruthyCheck();
        }
        IL.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    private void EmitForOf(Stmt.ForOf f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        _ctx.EnterLoop(endLabel, continueLabel);
        _ctx.Locals.EnterScope();

        // Evaluate iterable and get enumerator
        EmitExpression(f.Iterable);
        var iterableLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, iterableLocal);

        // Create index variable
        var indexLocal = IL.DeclareLocal(typeof(int));
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, typeof(object));

        IL.MarkLabel(startLabel);

        // Check if index < length
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Get current element
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    private void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        _ctx.EnterLoop(endLabel, continueLabel);
        _ctx.Locals.EnterScope();

        // Evaluate object and get keys
        EmitExpression(f.Object);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetKeys);
        var keysLocal = IL.DeclareLocal(typeof(List<object>));
        IL.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = IL.DeclareLocal(typeof(int));
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, typeof(object));

        IL.MarkLabel(startLabel);

        // Check if index < keys.Count
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Get current key: keys[index]
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    private void EmitBlock(Stmt.Block b)
    {
        _ctx.Locals.EnterScope();
        foreach (var stmt in b.Statements)
        {
            EmitStatement(stmt);
        }
        _ctx.Locals.ExitScope();
    }

    private void EmitReturn(Stmt.Return r)
    {
        if (r.Value != null)
        {
            EmitExpression(r.Value);
            EmitBoxIfNeeded(r.Value);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        if (_ctx.ExceptionBlockDepth > 0)
        {
            // Inside exception block: store value and leave
            if (_ctx.ReturnValueLocal == null)
            {
                _ctx.ReturnValueLocal = IL.DeclareLocal(typeof(object));
                _ctx.ReturnLabel = IL.DefineLabel();
            }
            IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal);
            IL.Emit(OpCodes.Leave, _ctx.ReturnLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ret);
        }
    }

    private void EmitBreak()
    {
        var loop = _ctx.CurrentLoop;
        if (loop != null)
        {
            IL.Emit(OpCodes.Br, loop.Value.BreakLabel);
        }
    }

    private void EmitContinue()
    {
        var loop = _ctx.CurrentLoop;
        if (loop != null)
        {
            IL.Emit(OpCodes.Br, loop.Value.ContinueLabel);
        }
    }

    private void EmitSwitch(Stmt.Switch s)
    {
        // Check for exhaustive switch optimization
        var switchAnalysis = _ctx.DeadCode?.GetSwitchResult(s);
        bool skipDefault = switchAnalysis?.DefaultIsUnreachable == true;

        var endLabel = IL.DefineLabel();
        var defaultLabel = IL.DefineLabel();
        var caseLabels = s.Cases.Select(_ => IL.DefineLabel()).ToList();

        // Evaluate subject once
        EmitExpression(s.Subject);
        var subjectLocal = IL.DeclareLocal(typeof(object));
        EmitBoxIfNeeded(s.Subject);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EmitBoxIfNeeded(s.Cases[i].Value);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Equals);
            IL.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        // Jump to default or end (skip default if unreachable)
        if (skipDefault || s.DefaultBody == null)
        {
            IL.Emit(OpCodes.Br, endLabel);
        }
        else
        {
            IL.Emit(OpCodes.Br, defaultLabel);
        }

        // Emit case bodies
        for (int i = 0; i < s.Cases.Count; i++)
        {
            IL.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break)
                {
                    IL.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
            // Fall through if no break
        }

        // Default case (skip if unreachable)
        if (s.DefaultBody != null && !skipDefault)
        {
            IL.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break)
                {
                    IL.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
        }

        IL.MarkLabel(endLabel);
    }

    private void EmitTryCatch(Stmt.TryCatch t)
    {
        _ctx.ExceptionBlockDepth++;

        IL.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
        {
            EmitStatement(stmt);
        }

        if (t.CatchBlock != null)
        {
            IL.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                // Store exception
                var exLocal = _ctx.Locals.DeclareLocal(t.CatchParam.Lexeme, typeof(object));
                IL.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                IL.Emit(OpCodes.Stloc, exLocal);
            }
            else
            {
                IL.Emit(OpCodes.Pop);
            }

            foreach (var stmt in t.CatchBlock)
            {
                EmitStatement(stmt);
            }
        }

        if (t.FinallyBlock != null)
        {
            IL.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
            {
                EmitStatement(stmt);
            }
        }

        IL.EndExceptionBlock();

        _ctx.ExceptionBlockDepth--;
    }

    private void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EmitBoxIfNeeded(t.Value);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateException);
        IL.Emit(OpCodes.Throw);
    }

    private void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EmitBoxIfNeeded(p.Expr);
        // Call Console.WriteLine(object) directly
        IL.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(object)])!);
    }
}
