using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Statement emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        // If we're in a nested scope, always create a local variable to support shadowing.
        // This allows inner scopes to declare variables with the same name as outer/top-level vars.
        if (!_ctx.Locals.IsInNestedScope)
        {
            // Check if this is a captured top-level variable - use entry-point display class
            if (_ctx.CapturedTopLevelVars?.Contains(v.Name.Lexeme) == true &&
                _ctx.EntryPointDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var displayField) == true)
            {
                // Load the display class instance
                if (_ctx.EntryPointDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
                }
                else if (_ctx.EntryPointDisplayClassStaticField != null)
                {
                    IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
                }
                else
                {
                    // No access to display class - fall through to static field path
                    goto checkStaticField;
                }

                if (v.Initializer != null)
                {
                    EmitExpression(v.Initializer);
                    EmitBoxIfNeeded(v.Initializer);
                }
                else if (v.TypeAnnotation == "number")
                {
                    // Typed number without initializer defaults to 0
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Stfld, displayField);
                return;
            }

            checkStaticField:

            // Check if this is a top-level variable - use static fields so all functions can access them
            if (_ctx.TopLevelStaticVars?.TryGetValue(v.Name.Lexeme, out var staticField) == true)
            {
                if (v.Initializer != null)
                {
                    EmitExpression(v.Initializer);
                    EmitBoxIfNeeded(v.Initializer);
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else if (v.TypeAnnotation == "number")
                {
                    // Typed number without initializer defaults to 0
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                return;
            }
        }

        // Check if this is a function-level captured variable - use function display class
        if (_ctx.CapturedFunctionLocals?.Contains(v.Name.Lexeme) == true &&
            _ctx.FunctionDisplayClassFields?.TryGetValue(v.Name.Lexeme, out var funcDisplayField) == true &&
            _ctx.FunctionDisplayClassLocal != null)
        {
            // Store initializer (or default) in function display class field
            IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EmitBoxIfNeeded(v.Initializer);
            }
            else if (v.TypeAnnotation == "number")
            {
                // Typed number without initializer defaults to 0
                IL.Emit(OpCodes.Ldc_R8, 0.0);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Stfld, funcDisplayField);
            return;
        }

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
    /// Tracks the loop counter variable name for optimized for loops.
    /// When set, the variable can use unboxed double even without explicit type annotation.
    /// </summary>
    private string? _optimizedLoopCounterName;

    /// <summary>
    /// Conservative check: use unboxed double for variables with explicit ': number' annotation
    /// or for optimized for loop counters.
    /// </summary>
    private bool CanUseUnboxedLocal(Stmt.Var v)
    {
        // Check if this is an optimized for loop counter
        if (_optimizedLoopCounterName != null && v.Name.Lexeme == _optimizedLoopCounterName)
            return true;

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

    /// <summary>
    /// Emits a for loop with unboxed counter optimization when applicable.
    /// </summary>
    protected override void EmitFor(Stmt.For f)
    {
        // Analyze the loop to see if we can use an unboxed counter
        var analysis = ForLoopAnalyzer.Analyze(f, _ctx.ClosureAnalyzer);

        if (analysis.CanUseUnboxedCounter && analysis.VariableName != null)
        {
            // Set the flag so CanUseUnboxedLocal will return true for this variable
            _optimizedLoopCounterName = analysis.VariableName;
            try
            {
                // Emit the loop with the optimization enabled
                base.EmitFor(f);
            }
            finally
            {
                // Clear the flag
                _optimizedLoopCounterName = null;
            }
        }
        else
        {
            // No optimization - emit normally
            base.EmitFor(f);
        }
    }

    protected override void EmitIf(Stmt.If i)
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
        var builder = _ctx.ILBuilder;
        var elseLabel = builder.DefineLabel("if_else");
        var endLabel = builder.DefineLabel("if_end");

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
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brfalse
            EnsureBoxed();
            EmitTruthyCheck();
        }
        builder.Emit_Brfalse(elseLabel);

        EmitStatement(i.ThenBranch);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
        {
            EmitStatement(i.ElseBranch);
        }

        builder.MarkLabel(endLabel);
    }

    protected override void EmitWhile(Stmt.While w)
    {
        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("while_start");
        var endLabel = builder.DefineLabel("while_end");

        _ctx.EnterLoop(endLabel, startLabel);

        builder.MarkLabel(startLabel);
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
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brfalse
            EnsureBoxed();
            EmitTruthyCheck();
        }
        builder.Emit_Brfalse(endLabel);

        EmitStatement(w.Body);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    protected override void EmitDoWhile(Stmt.DoWhile dw)
    {
        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("dowhile_start");
        var endLabel = builder.DefineLabel("dowhile_end");
        var continueLabel = builder.DefineLabel("dowhile_continue");

        _ctx.EnterLoop(endLabel, continueLabel);

        // Body executes at least once
        builder.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        builder.MarkLabel(continueLabel);

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
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brtrue
            EnsureBoxed();
            EmitTruthyCheck();
        }
        builder.Emit_Brtrue(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.ExitLoop();
    }

    protected override void EmitForOf(Stmt.ForOf f)
    {
        _ctx.Locals.EnterScope();
        var builder = _ctx.ILBuilder;

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
            var genStartLabel = builder.DefineLabel("forof_gen_start");
            var genEndLabel = builder.DefineLabel("forof_gen_end");
            var genContinueLabel = builder.DefineLabel("forof_gen_continue");
            _ctx.EnterLoop(genEndLabel, genContinueLabel);
            EmitForOfEnumerator(f, genStartLabel, genEndLabel, genContinueLabel);
            return;
        }

        // Store the iterable for potential iterator protocol check
        var iterableLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, iterableLocal);

        // Try iterator protocol first: GetIteratorFunction(iterable, Symbol.iterator)
        var iteratorFnLocal = IL.DeclareLocal(_ctx.Types.Object);
        var indexBasedLabel = builder.DefineLabel("forof_index_based");
        var afterLoopLabel = builder.DefineLabel("forof_after");

        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIteratorFunction);
        IL.Emit(OpCodes.Stloc, iteratorFnLocal);

        // If iterator function is null, fall back to index-based iteration
        IL.Emit(OpCodes.Ldloc, iteratorFnLocal);
        builder.Emit_Brfalse(indexBasedLabel);

        // ===== Iterator protocol path =====
        {
            var iterStartLabel = builder.DefineLabel("forof_iter_start");
            var iterEndLabel = builder.DefineLabel("forof_iter_end");
            var iterContinueLabel = builder.DefineLabel("forof_iter_continue");
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

            builder.MarkLabel(iterStartLabel);

            // Call MoveNext
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, moveNext);
            builder.Emit_Brfalse(iterEndLabel);

            // Get Current
            IL.Emit(OpCodes.Ldloc, enumLocal);
            IL.Emit(OpCodes.Callvirt, current);
            IL.Emit(OpCodes.Stloc, loopVar);

            // Emit body
            EmitStatement(f.Body);

            builder.MarkLabel(iterContinueLabel);
            builder.Emit_Br(iterStartLabel);

            builder.MarkLabel(iterEndLabel);
            _ctx.ExitLoop();
            builder.Emit_Br(afterLoopLabel); // Skip the index-based path
        }

        // ===== Index-based fallback (for arrays, strings, etc.) =====
        builder.MarkLabel(indexBasedLabel);
        {
            var startLabel = builder.DefineLabel("forof_idx_start");
            var endLabel = builder.DefineLabel("forof_idx_end");
            var continueLabel = builder.DefineLabel("forof_idx_continue");
            _ctx.EnterLoop(endLabel, continueLabel);

            // Create index variable
            var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, indexLocal);

            // Loop variable
            var indexLoopVar = _ctx.Locals.DeclareLocal(f.Variable.Lexeme, _ctx.Types.Object);

            builder.MarkLabel(startLabel);

            // Check if index < length
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
            IL.Emit(OpCodes.Clt);
            builder.Emit_Brfalse(endLabel);

            // Get current element
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
            IL.Emit(OpCodes.Stloc, indexLoopVar);

            // Emit body
            EmitStatement(f.Body);

            builder.MarkLabel(continueLabel);

            // Increment index
            IL.Emit(OpCodes.Ldloc, indexLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Stloc, indexLocal);

            builder.Emit_Br(startLabel);

            builder.MarkLabel(endLabel);
            _ctx.ExitLoop();
        }

        // Common exit point for both paths
        builder.MarkLabel(afterLoopLabel);
        _ctx.Locals.ExitScope();
    }

    private void EmitForOfEnumerator(Stmt.ForOf f, Label startLabel, Label endLabel, Label continueLabel)
    {
        var builder = _ctx.ILBuilder;

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

        builder.MarkLabel(startLabel);

        // Call MoveNext
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        builder.Emit_Brfalse(endLabel);

        // Get Current
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, current);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        builder.MarkLabel(continueLabel);
        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    protected override void EmitForIn(Stmt.ForIn f)
    {
        var builder = _ctx.ILBuilder;
        var startLabel = builder.DefineLabel("forin_start");
        var endLabel = builder.DefineLabel("forin_end");
        var continueLabel = builder.DefineLabel("forin_continue");

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

        builder.MarkLabel(startLabel);

        // Check if index < keys.Count
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
        IL.Emit(OpCodes.Clt);
        builder.Emit_Brfalse(endLabel);

        // Get current key: keys[index]
        IL.Emit(OpCodes.Ldloc, keysLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
        IL.Emit(OpCodes.Stloc, loopVar);

        // Emit body
        EmitStatement(f.Body);

        builder.MarkLabel(continueLabel);

        // Increment index
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, indexLocal);

        builder.Emit_Br(startLabel);

        builder.MarkLabel(endLabel);
        _ctx.Locals.ExitScope();
        _ctx.ExitLoop();
    }

    protected override void EmitBlock(Stmt.Block b)
    {
        _ctx.Locals.EnterScope();
        foreach (var stmt in b.Statements)
        {
            EmitStatement(stmt);
        }
        _ctx.Locals.ExitScope();
    }

    protected override void EmitReturn(Stmt.Return r)
    {
        // Get the current method's return type (defaults to object if not set)
        var returnType = _ctx.CurrentMethodReturnType ?? _ctx.Types.Object;

        if (r.Value != null)
        {
            EmitExpression(r.Value);
            // Only box if return type is object; otherwise use typed value directly
            if (returnType == _ctx.Types.Object)
            {
                EmitBoxIfNeeded(r.Value);
            }
            else if (_ctx.Types.IsDouble(returnType))
            {
                // Ensure we have an unboxed double for : number return type
                if (_stackType != StackType.Double)
                {
                    EmitUnboxToDouble();
                }
            }
            else if (_ctx.Types.IsBoolean(returnType))
            {
                // Ensure we have an unboxed bool for : boolean return type
                if (_stackType != StackType.Boolean)
                {
                    // Convert to boolean: double -> i4, or object -> unbox to double -> i4
                    if (_stackType == StackType.Double)
                    {
                        IL.Emit(OpCodes.Conv_I4);
                    }
                    else
                    {
                        EmitUnboxToDouble();
                        IL.Emit(OpCodes.Conv_I4);
                    }
                }
            }
            // For other types (string, etc.), the value should already be correct
        }
        else
        {
            // Return undefined (null) or appropriate default
            if (returnType == _ctx.Types.Object || !returnType.IsValueType)
            {
                IL.Emit(OpCodes.Ldnull);
            }
            else if (_ctx.Types.IsDouble(returnType))
            {
                IL.Emit(OpCodes.Ldc_R8, 0.0);
            }
            else if (_ctx.Types.IsBoolean(returnType))
            {
                IL.Emit(OpCodes.Ldc_I4_0);
            }
            else
            {
                // For other value types, emit default
                var local = IL.DeclareLocal(returnType);
                IL.Emit(OpCodes.Ldloca, local);
                IL.Emit(OpCodes.Initobj, returnType);
                IL.Emit(OpCodes.Ldloc, local);
            }
        }

        if (_ctx.ExceptionBlockDepth > 0)
        {
            // Inside exception block: store value and leave
            // Use builder for Leave validation (ensures we're inside exception block)
            var builder = _ctx.ILBuilder;
            if (_ctx.ReturnValueLocal == null)
            {
                // Use the appropriate type for the return value local
                _ctx.ReturnValueLocal = IL.DeclareLocal(returnType);
                _ctx.ReturnLabel = builder.DefineLabel("deferred_return");
            }
            IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal);
            builder.Emit_Leave(_ctx.ReturnLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ret);
        }
    }

    protected override void EmitBreak(Stmt.Break b)
    {
        var loop = b.Label != null
            ? FindLabeledLoop(b.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.BreakLabel);
    }

    protected override void EmitContinue(Stmt.Continue c)
    {
        var loop = c.Label != null
            ? FindLabeledLoop(c.Label.Lexeme)
            : CurrentLoop;

        if (loop != null)
            EmitBranchToLabel(loop.Value.ContinueLabel);
    }

    protected override void EmitLabeledStatement(Stmt.LabeledStatement labeledStmt)
    {
        string labelName = labeledStmt.Label.Lexeme;
        var builder = _ctx.ILBuilder;
        var breakLabel = builder.DefineLabel($"labeled_{labelName}_break");
        var continueLabel = builder.DefineLabel($"labeled_{labelName}_continue");

        // For labeled statements, we need to handle both loops and non-loop statements.
        // For loops, the inner loop will use its own labels for unlabeled break/continue,
        // but labeled break/continue should use the labels registered here.

        // Mark continue label at the start (for labeled continue, restart from here)
        builder.MarkLabel(continueLabel);

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
        builder.MarkLabel(breakLabel);
    }

    protected override void EmitSwitch(Stmt.Switch s)
    {
        // Check for exhaustive switch optimization
        var switchAnalysis = _ctx.DeadCode?.GetSwitchResult(s);
        bool skipDefault = switchAnalysis?.DefaultIsUnreachable == true;

        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("switch_end");
        var defaultLabel = builder.DefineLabel("switch_default");
        var caseLabels = s.Cases.Select((_, i) => builder.DefineLabel($"switch_case_{i}")).ToList();

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
            builder.Emit_Brtrue(caseLabels[i]);
        }

        // Jump to default or end (skip default if unreachable)
        if (skipDefault || s.DefaultBody == null)
        {
            builder.Emit_Br(endLabel);
        }
        else
        {
            builder.Emit_Br(defaultLabel);
        }

        // Emit case bodies
        for (int i = 0; i < s.Cases.Count; i++)
        {
            builder.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break breakStmt)
                {
                    if (breakStmt.Label != null)
                    {
                        // Labeled break - find and jump to the labeled target
                        EmitBreak(breakStmt);
                    }
                    else
                    {
                        // Unlabeled break - exits switch only
                        builder.Emit_Br(endLabel);
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
            builder.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break breakStmt)
                {
                    if (breakStmt.Label != null)
                    {
                        // Labeled break - find and jump to the labeled target
                        EmitBreak(breakStmt);
                    }
                    else
                    {
                        // Unlabeled break - exits switch only
                        builder.Emit_Br(endLabel);
                    }
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
        }

        builder.MarkLabel(endLabel);
    }

    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        // Use ValidatedILBuilder for exception block operations - it tracks depth automatically
        // and validates proper Begin/End pairing
        var builder = _ctx.ILBuilder;

        _ctx.ExceptionBlockDepth++;
        builder.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
        {
            EmitStatement(stmt);
        }

        if (t.CatchBlock != null)
        {
            builder.BeginCatchBlock(_ctx.Types.Exception);

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
            builder.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
            {
                EmitStatement(stmt);
            }
        }

        builder.EndExceptionBlock();
        _ctx.ExceptionBlockDepth--;
    }

    protected override void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EmitBoxIfNeeded(t.Value);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateException);
        IL.Emit(OpCodes.Throw);
    }

    protected override void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EmitBoxIfNeeded(p.Expr);
        // Call Console.WriteLine(object) directly
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Console, "WriteLine", _ctx.Types.Object));
    }
}
