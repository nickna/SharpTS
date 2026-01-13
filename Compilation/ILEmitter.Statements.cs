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
        // Determine if this local can use unboxed double type
        Type localType = CanUseUnboxedLocal(v) ? _ctx.Types.Double : _ctx.Types.Object;
        var local = _ctx.Locals.DeclareLocal(v.Name.Lexeme, localType);

        if (v.Initializer != null)
        {
            EmitExpression(v.Initializer);

            if (_ctx.Types.IsDouble(localType))
            {
                // Ensure we have an unboxed double on stack
                EnsureDouble();
            }
            else
            {
                // Ensure we have a boxed object on stack
                EmitBoxIfNeeded(v.Initializer);
            }
            IL.Emit(OpCodes.Stloc, local);
        }
        else
        {
            if (_ctx.Types.IsDouble(localType))
            {
                // Initialize to 0.0 for uninitialized number variables
                IL.Emit(OpCodes.Ldc_R8, 0.0);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Conservative check: only use unboxed double for variables with explicit ': number' annotation.
    /// </summary>
    private bool CanUseUnboxedLocal(Stmt.Var v)
    {
        // Must have explicit 'number' type annotation
        if (v.TypeAnnotation != "number")
            return false;

        // If there's an initializer, it must be a known number expression
        if (v.Initializer != null)
        {
            var exprType = _ctx.TypeMap?.Get(v.Initializer);
            if (exprType is not TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return false;
        }

        return true;
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
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(i.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else if (i.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else
        {
            // For other expressions, apply truthy check
            EnsureBoxed();
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
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(w.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else if (w.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else
        {
            // For other expressions, apply truthy check
            EnsureBoxed();
            EmitTruthyCheck();
        }
        IL.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);
        IL.Emit(OpCodes.Br, startLabel);

        IL.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    private void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        _ctx.EnterLoop(endLabel, continueLabel);

        // Body executes at least once
        IL.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        IL.MarkLabel(continueLabel);

        // Evaluate condition
        EmitExpression(dw.Condition);
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(dw.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else if (dw.Condition is Expr.Logical)
        {
            // Logical expressions already leave int on stack
        }
        else
        {
            // For other expressions, apply truthy check
            EnsureBoxed();
            EmitTruthyCheck();
        }
        IL.Emit(OpCodes.Brtrue, startLabel);

        IL.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    private void EmitForOf(Stmt.ForOf f)
    {
        _ctx.Locals.EnterScope();

        // Evaluate iterable
        TypeInfo? iterableType = _ctx.TypeMap?.Get(f.Iterable);
        EmitExpression(f.Iterable);

        // For Map/Set, convert to a List first
        if (iterableType is TypeInfo.Map)
        {
            // Map iteration yields [key, value] entries
            IL.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
        }
        else if (iterableType is TypeInfo.Set)
        {
            // Set iteration yields values
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
        }

        // For generators, use enumerator-based iteration (with its own labels)
        if (iterableType is TypeInfo.Generator)
        {
            var genStartLabel = IL.DefineLabel();
            var genEndLabel = IL.DefineLabel();
            var genContinueLabel = IL.DefineLabel();
            _ctx.EnterLoop(genEndLabel, genContinueLabel);
            EmitForOfEnumerator(f, genStartLabel, genEndLabel, genContinueLabel);
            return;
        }

        // Store the iterable for potential iterator protocol check
        var iterableLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, iterableLocal);

        // Try iterator protocol first: GetIteratorFunction(iterable, Symbol.iterator)
        var iteratorFnLocal = IL.DeclareLocal(_ctx.Types.Object);
        var indexBasedLabel = IL.DefineLabel();
        var afterLoopLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIteratorFunction);
        IL.Emit(OpCodes.Stloc, iteratorFnLocal);

        // If iterator function is null, fall back to index-based iteration
        IL.Emit(OpCodes.Ldloc, iteratorFnLocal);
        IL.Emit(OpCodes.Brfalse, indexBasedLabel);

        // ===== Iterator protocol path =====
        {
            var iterStartLabel = IL.DefineLabel();
            var iterEndLabel = IL.DefineLabel();
            var iterContinueLabel = IL.DefineLabel();
            _ctx.EnterLoop(iterEndLabel, iterContinueLabel);

            // Call the iterator function to get the iterator object
            // Use InvokeMethodValue to properly bind 'this' to the iterable object
            IL.Emit(OpCodes.Ldloc, iterableLocal);       // receiver (this)
            IL.Emit(OpCodes.Ldloc, iteratorFnLocal);     // method
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);  // args
            IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);

            // Store the iterator object
            var iteratorObjLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, iteratorObjLocal);

            // Create $IteratorWrapper: new $IteratorWrapper(iteratorObj, typeof($Runtime))
            IL.Emit(OpCodes.Ldloc, iteratorObjLocal);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.IteratorWrapperCtor);

            // Cast to IEnumerator and store
            var enumLocal = IL.DeclareLocal(_ctx.Types.IEnumerator);
            IL.Emit(OpCodes.Castclass, _ctx.Types.IEnumerator);
            IL.Emit(OpCodes.Stloc, enumLocal);

            // Loop variable
            var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

            // Get MoveNext and Current methods
            var moveNext = _ctx.Types.GetMethod(_ctx.Types.IEnumerator, "MoveNext");
            var current = _ctx.Types.IEnumerator.GetProperty("Current")!.GetGetMethod()!;

            IL.MarkLabel(iterStartLabel);

            // Call MoveNext
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, moveNext);
            IL.Emit(OpCodes.Brfalse, iterEndLabel);

            // Get Current
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, current);
            IL.Emit(OpCodes.Stloc, loopVar);

            // Emit body
            EmitStatement(f.Body);

            IL.MarkLabel(iterContinueLabel);
            IL.Emit(OpCodes.Br, iterStartLabel);

            IL.MarkLabel(iterEndLabel);
            _ctx.ExitLoop();
            IL.Emit(OpCodes.Br, afterLoopLabel); // Skip the index-based path
        }

        // ===== Index-based fallback (for arrays, strings, etc.) =====
        IL.MarkLabel(indexBasedLabel);
        {
            var startLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            var continueLabel = IL.DefineLabel();
            _ctx.EnterLoop(endLabel, continueLabel);

            // Create index variable
            var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, indexLocal);

            // Loop variable
            var indexLoopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

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
            IL.Emit(OpCodes.Stloc, indexLoopVar);

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
            _ctx.ExitLoop();
        }

        // Common exit point for both paths
        IL.MarkLabel(afterLoopLabel);
        _ctx.Locals.ExitScope();
    }

    private void EmitForOfEnumerator(Stmt.ForOf f, Label startLabel, Label endLabel, Label continueLabel)
    {
        // Use IEnumerable.GetEnumerator()/MoveNext()/Current pattern for generators
        var getEnumerator = _ctx.Types.GetMethod(_ctx.Types.IEnumerable, "GetEnumerator");
        var moveNext = _ctx.Types.GetMethod(_ctx.Types.IEnumerator, "MoveNext");
        var current = _ctx.Types.IEnumerator.GetProperty("Current")!.GetGetMethod()!;

        // Stack has the iterable (generator)
        IL.Emit(OpCodes.Castclass, _ctx.Types.IEnumerable);
        IL.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = IL.DeclareLocal(_ctx.Types.IEnumerator);
        IL.Emit(OpCodes.Stloc, enumLocal);

        // Loop variable
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

        IL.MarkLabel(startLabel);

        // Call MoveNext
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, endLabel);

        // Get Current
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, current);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        IL.MarkLabel(continueLabel);
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
        var keysLocal = IL.DeclareLocal(_ctx.Types.ListOfObject);
        IL.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        var loopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

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
                _ctx.ReturnValueLocal = IL.DeclareLocal(_ctx.Types.Object);
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

    private void EmitBreak(string? labelName = null)
    {
        var loop = labelName != null
            ? _ctx.FindLabeledLoop(labelName)
            : _ctx.CurrentLoop;

        if (loop != null)
        {
            // Use Leave instead of Br when inside exception blocks
            if (_ctx.ExceptionBlockDepth > 0)
                IL.Emit(OpCodes.Leave, loop.Value.BreakLabel);
            else
                IL.Emit(OpCodes.Br, loop.Value.BreakLabel);
        }
    }

    private void EmitContinue(string? labelName = null)
    {
        var loop = labelName != null
            ? _ctx.FindLabeledLoop(labelName)
            : _ctx.CurrentLoop;

        if (loop != null)
        {
            // Use Leave instead of Br when inside exception blocks
            if (_ctx.ExceptionBlockDepth > 0)
                IL.Emit(OpCodes.Leave, loop.Value.ContinueLabel);
            else
                IL.Emit(OpCodes.Br, loop.Value.ContinueLabel);
        }
    }

    private void EmitLabeledStatement(Stmt.LabeledStatement labeledStmt)
    {
        string labelName = labeledStmt.Label.Lexeme;
        var breakLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // For labeled statements, we need to handle both loops and non-loop statements.
        // For loops, the inner loop will use its own labels for unlabeled break/continue,
        // but labeled break/continue should use the labels registered here.

        // Mark continue label at the start (for labeled continue, restart from here)
        IL.MarkLabel(continueLabel);

        _ctx.EnterLoop(breakLabel, continueLabel, labelName);
        try
        {
            // If this is directly a loop, the loop itself will handle its own unlabeled labels
            // But for labeled break/continue, it will use the labeled entry we just pushed
            EmitStatement(labeledStmt.Statement);
        }
        finally
        {
            _ctx.ExitLoop();
        }

        // Mark the break label (after the statement, for labeled break)
        IL.MarkLabel(breakLabel);
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
        var subjectLocal = IL.DeclareLocal(_ctx.Types.Object);
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
                if (stmt is Stmt.Break breakStmt)
                {
                    if (breakStmt.Label != null)
                    {
                        // Labeled break - find and jump to the labeled target
                        EmitBreak(breakStmt.Label.Lexeme);
                    }
                    else
                    {
                        // Unlabeled break - exits switch only
                        IL.Emit(OpCodes.Br, endLabel);
                    }
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
                if (stmt is Stmt.Break breakStmt)
                {
                    if (breakStmt.Label != null)
                    {
                        // Labeled break - find and jump to the labeled target
                        EmitBreak(breakStmt.Label.Lexeme);
                    }
                    else
                    {
                        // Unlabeled break - exits switch only
                        IL.Emit(OpCodes.Br, endLabel);
                    }
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
            IL.BeginCatchBlock(_ctx.Types.Exception);

            if (t.CatchParam != null)
            {
                // Store exception
                var exLocal = _ctx.Locals.DeclareLocal(t.CatchParam.Lexeme, _ctx.Types.Object);
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
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Console, "WriteLine", _ctx.Types.Object));
    }
}
