using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for an async state machine.
/// This is the heart of async IL generation, handling state dispatch,
/// await points, and result/exception completion.
/// </summary>
public class AsyncMoveNextEmitter
{
    private readonly AsyncStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private readonly ILGenerator _il;

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _endLabel;
    private Label _setResultLabel;

    // Current await point being processed
    private int _currentAwaitState = 0;

    // Stack type tracking (mirrors ILEmitter)
    private StackType _stackType = StackType.Unknown;

    // Exception handling
    private LocalBuilder? _exceptionLocal;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Return value storage
    private LocalBuilder? _returnValueLocal;
    private bool _hasReturnValue;

    // Loop label tracking for break/continue
    private readonly Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> _loopLabels = new();

    // For try/catch with awaits: track where to store caught exceptions
    private LocalBuilder? _currentTryCatchExceptionLocal = null;
    private Label? _currentTryCatchSkipLabel = null;

    // For return inside try with finally-with-awaits: track pending return
    private LocalBuilder? _pendingReturnFlagLocal = null;
    private Label? _afterFinallyLabel = null;

    public AsyncMoveNextEmitter(AsyncStateMachineBuilder builder, AsyncStateAnalyzer.AsyncFunctionAnalysis analysis)
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx, Type returnType)
    {
        if (body == null) return;

        _ctx = ctx;
        _hasReturnValue = returnType != typeof(void);

        // Declare exception local for catch block
        _exceptionLocal = _il.DeclareLocal(typeof(Exception));

        // Declare return value local if needed
        if (_hasReturnValue)
        {
            _returnValueLocal = _il.DeclareLocal(typeof(object));
        }

        // Define labels for each await resume point
        foreach (var awaitPoint in _analysis.AwaitPoints)
        {
            _stateLabels[awaitPoint.StateNumber] = _il.DefineLabel();
        }
        _endLabel = _il.DefineLabel();
        _setResultLabel = _il.DefineLabel();

        // Begin outer try block
        _il.BeginExceptionBlock();

        // Emit state dispatch switch
        EmitStateSwitch();

        // Emit the function body (will emit await points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Jump to set result
        _il.Emit(OpCodes.Br, _setResultLabel);

        // Set result label
        _il.MarkLabel(_setResultLabel);

        // this.<>1__state = -2
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetResult(returnValue)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        if (_hasReturnValue && _returnValueLocal != null)
            _il.Emit(OpCodes.Ldloc, _returnValueLocal);
        else
            _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetResultMethod());
        _il.Emit(OpCodes.Leave, _endLabel);

        // Begin catch block
        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Stloc, _exceptionLocal);

        // this.<>1__state = -2
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetException(exception)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _exceptionLocal);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetExceptionMethod());
        _il.Emit(OpCodes.Leave, _endLabel);

        // End exception block
        _il.EndExceptionBlock();

        // End label
        _il.MarkLabel(_endLabel);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitStateSwitch()
    {
        if (_analysis.AwaitPointCount == 0) return;

        // Load state field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Create labels array for switch
        var labels = new Label[_analysis.AwaitPointCount];
        for (int i = 0; i < _analysis.AwaitPointCount; i++)
        {
            labels[i] = _stateLabels[i];
        }

        // switch (state) { case 0: goto State0; case 1: goto State1; ... }
        _il.Emit(OpCodes.Switch, labels);

        // Fall through for state -1 (initial execution)
    }

    #region Statement Emission

    private void EmitStatement(Stmt stmt)
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

    private void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        // Check if this variable is hoisted to state machine
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store to field
            if (v.Initializer != null)
            {
                // Emit expression first (important: may contain await which clears stack)
                EmitExpression(v.Initializer);
                EnsureBoxed();
                // Now store to field - load 'this' after await completes
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else
            {
                // Initialize to null
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stfld, field);
            }
        }
        else
        {
            // Not hoisted - use local variable
            var local = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(name, local);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                _il.Emit(OpCodes.Stloc, local);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, local);
            }
        }
    }

    private void EmitReturn(Stmt.Return r)
    {
        if (r.Value != null)
        {
            EmitExpression(r.Value);
            EnsureBoxed();
            if (_returnValueLocal != null)
                _il.Emit(OpCodes.Stloc, _returnValueLocal);
        }

        // If we're inside a try with finally-with-awaits, set pending return flag
        // and jump to after-finally label (which will then complete the return)
        if (_pendingReturnFlagLocal != null && _afterFinallyLabel != null)
        {
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, _pendingReturnFlagLocal);
            // Use Leave to exit the protected region to the after-finally label
            _il.Emit(OpCodes.Leave, _afterFinallyLabel.Value);
            return;
        }

        // Jump to set result
        _il.Emit(OpCodes.Leave, _setResultLabel);
    }

    private void EmitIf(Stmt.If i)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(i.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        _il.MarkLabel(endLabel);
    }

    private void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitForOf(Stmt.ForOf f)
    {
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Emit iterable
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Cast to IEnumerable and get enumerator
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumLocal);

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable
        if (varField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
    }

    private void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        // Body executes at least once
        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        _il.MarkLabel(continueLabel);

        // Evaluate condition
        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Evaluate object and get keys
        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysLocal = _il.DeclareLocal(typeof(List<object>));
        _il.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        LocalBuilder? loopVar = null;
        if (varField == null)
        {
            loopVar = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, loopVar);
        }

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);

        // Check if index < keys.Count
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Get current key: keys[index]
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetElement);

        // Store to loop variable
        if (varField != null)
        {
            var keyTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, keyTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, keyTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            _il.Emit(OpCodes.Stloc, loopVar!);
        }

        // Emit body
        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        // Increment index
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    private void EmitSwitch(Stmt.Switch s)
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

    private void EmitTryCatch(Stmt.TryCatch t)
    {
        // Check if this try block contains any await points
        bool hasAwaitsInTry = ContainsAwait(t.TryBlock);
        bool hasAwaitsInCatch = t.CatchBlock != null && ContainsAwait(t.CatchBlock);
        bool hasAwaitsInFinally = t.FinallyBlock != null && ContainsAwait(t.FinallyBlock);

        if (hasAwaitsInTry || hasAwaitsInCatch || hasAwaitsInFinally)
        {
            // Complex case: await inside protected region
            EmitTryCatchWithAwaits(t, hasAwaitsInTry, hasAwaitsInCatch, hasAwaitsInFinally);
        }
        else
        {
            // Simple case: no awaits in protected regions
            EmitSimpleTryCatch(t);
        }
    }

    private void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        _il.BeginExceptionBlock();

        // Emit try block statements
        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        // Emit catch block if present
        if (t.CatchBlock != null)
        {
            _il.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                // Create local for the exception parameter
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);

                // Wrap the .NET exception to TypeScript exception object
                _il.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, exLocal);
            }
            else
            {
                // No catch parameter - just pop the exception
                _il.Emit(OpCodes.Pop);
            }

            // Emit catch block statements
            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        // Emit finally block if present
        if (t.FinallyBlock != null)
        {
            _il.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        _il.EndExceptionBlock();
    }

    private void EmitTryCatchWithAwaits(Stmt.TryCatch t, bool hasAwaitsInTry, bool hasAwaitsInCatch, bool hasAwaitsInFinally)
    {
        // For try blocks with awaits, we need to use a flag-based approach:
        // 1. Use a local to track if an exception was caught
        // 2. Emit try body with special handling around each await
        // 3. Check exception flag after try/await completion

        // Create locals for exception tracking
        var caughtExceptionLocal = _il.DeclareLocal(typeof(object));
        var skipCatchLabel = _il.DefineLabel();
        var afterTryCatchLabel = _il.DefineLabel();

        // Initialize caught exception to null
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

        // If there are awaits in finally, set up pending return tracking
        // This allows return statements inside try to defer to after finally runs
        LocalBuilder? pendingReturnLocal = null;
        Label? afterFinallyLabel = null;
        var previousPendingReturnLocal = _pendingReturnFlagLocal;
        var previousAfterFinallyLabel = _afterFinallyLabel;

        if (hasAwaitsInFinally && t.FinallyBlock != null)
        {
            pendingReturnLocal = _il.DeclareLocal(typeof(bool));
            afterFinallyLabel = _il.DefineLabel();

            // Initialize pending return flag to false
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, pendingReturnLocal);

            // Set context for return statements to use
            _pendingReturnFlagLocal = pendingReturnLocal;
            _afterFinallyLabel = afterFinallyLabel;
        }

        if (hasAwaitsInTry)
        {
            // Emit try body with segmented exception handling
            EmitTryBodyWithAwaits(t.TryBlock, caughtExceptionLocal);
        }
        else if (hasAwaitsInFinally)
        {
            // No awaits in try but awaits in finally - need to capture exception from try
            // so we can run the finally with awaits before rethrowing
            _il.BeginExceptionBlock();
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);

            // Always catch to capture exception for finally handling
            _il.BeginCatchBlock(typeof(Exception));
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

            _il.EndExceptionBlock();
        }
        else
        {
            // No awaits in try or finally - use normal try block
            _il.BeginExceptionBlock();
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);

            if (t.CatchBlock != null)
            {
                _il.BeginCatchBlock(typeof(Exception));
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, caughtExceptionLocal);
            }
            _il.EndExceptionBlock();
        }

        // Check if we need to run catch block
        if (t.CatchBlock != null)
        {
            _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
            _il.Emit(OpCodes.Brfalse, skipCatchLabel);

            // Store exception in catch param if needed
            if (t.CatchParam != null)
            {
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Stloc, exLocal);
            }

            // Clear the exception local since catch handled it
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

            // Emit catch block (may contain awaits)
            // If we're inside an outer try context, wrap catch statements in try/catch
            // so that throws propagate to the outer try's exception handling
            var outerExceptionLocal = _currentTryCatchExceptionLocal;
            if (outerExceptionLocal != null && outerExceptionLocal != caughtExceptionLocal)
            {
                // We're nested inside another try-with-awaits
                // Wrap catch block in try/catch to propagate exceptions outward
                _il.BeginExceptionBlock();
                foreach (var stmt in t.CatchBlock)
                    EmitStatement(stmt);
                _il.BeginCatchBlock(typeof(Exception));
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, outerExceptionLocal);
                _il.EndExceptionBlock();
            }
            else
            {
                foreach (var stmt in t.CatchBlock)
                    EmitStatement(stmt);
            }

            // Fall through to finally (don't skip it)
            _il.MarkLabel(skipCatchLabel);
        }

        // Finally block - must always execute
        if (t.FinallyBlock != null)
        {
            // Mark label for return statements inside try to jump to finally
            if (afterFinallyLabel != null)
                _il.MarkLabel(afterFinallyLabel.Value);

            if (hasAwaitsInFinally)
            {
                // Finally with awaits - need special handling
                // The finally must run regardless of exception, so we emit it
                // and track if there's a pending exception to rethrow after
                EmitFinallyBodyWithAwaits(t.FinallyBlock, caughtExceptionLocal);
            }
            else
            {
                // No awaits in finally - emit directly
                foreach (var stmt in t.FinallyBlock)
                    EmitStatement(stmt);
            }

            // After finally, check if we need to rethrow a pending exception
            // (but only if there was no catch block that handled it)
            if (t.CatchBlock == null)
            {
                var noExceptionLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Brfalse, noExceptionLabel);

                // Rethrow the exception
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
                _il.Emit(OpCodes.Throw);

                _il.MarkLabel(noExceptionLabel);
            }

            // After finally, check if there was a pending return
            if (pendingReturnLocal != null)
            {
                var noPendingReturnLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, pendingReturnLocal);
                _il.Emit(OpCodes.Brfalse, noPendingReturnLabel);

                // Pending return - jump to set result
                _il.Emit(OpCodes.Leave, _setResultLabel);

                _il.MarkLabel(noPendingReturnLabel);
            }
        }

        // Restore previous context
        _pendingReturnFlagLocal = previousPendingReturnLocal;
        _afterFinallyLabel = previousAfterFinallyLabel;

        _il.MarkLabel(afterTryCatchLabel);
    }

    private void EmitFinallyBodyWithAwaits(List<Stmt> finallyBody, LocalBuilder caughtExceptionLocal)
    {
        // For finally with awaits, we use a similar strategy to try with awaits:
        // - Segment the code around awaits
        // - Each segment runs regardless of exception state
        // - After all awaits complete, we can rethrow if needed

        // Unlike try, we don't wrap in try/catch - finally must run even if
        // statements throw, but if finally itself throws, that replaces the original exception

        foreach (var stmt in finallyBody)
        {
            EmitStatement(stmt);
        }
    }

    private void EmitTryBodyWithAwaits(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal)
    {
        // Strategy:
        // 1. Wrap synchronous segments in try blocks
        // 2. For awaits, wrap the GetResult call in try/catch using context fields
        // 3. After each await, check if an exception was caught

        // Set context for await exception handling
        var previousExceptionLocal = _currentTryCatchExceptionLocal;
        var afterTryLabel = _il.DefineLabel();
        _currentTryCatchExceptionLocal = caughtExceptionLocal;
        _currentTryCatchSkipLabel = afterTryLabel;

        var stmtsBeforeAwait = new List<Stmt>();

        foreach (var stmt in tryBody)
        {
            if (ContainsAwait([stmt]))
            {
                // Emit accumulated statements in a try block
                if (stmtsBeforeAwait.Count > 0)
                {
                    // Check if exception was already caught
                    var skipSegmentLabel = _il.DefineLabel();
                    _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                    _il.Emit(OpCodes.Brtrue, skipSegmentLabel);

                    EmitSegmentInTry(stmtsBeforeAwait, caughtExceptionLocal);
                    _il.MarkLabel(skipSegmentLabel);
                    stmtsBeforeAwait.Clear();
                }

                // Check if exception was caught before continuing with await
                var skipAwaitLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Brtrue, skipAwaitLabel);

                // Emit the statement containing await
                // EmitAwait will check _currentTryCatchExceptionLocal and wrap GetResult in try/catch
                EmitStatement(stmt);

                _il.MarkLabel(skipAwaitLabel);
            }
            else
            {
                stmtsBeforeAwait.Add(stmt);
            }
        }

        // Emit remaining statements in a try block
        if (stmtsBeforeAwait.Count > 0)
        {
            // Check if exception was already caught
            var skipLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
            _il.Emit(OpCodes.Brtrue, skipLabel);

            EmitSegmentInTry(stmtsBeforeAwait, caughtExceptionLocal);
            _il.MarkLabel(skipLabel);
        }

        _il.MarkLabel(afterTryLabel);

        // Restore context
        _currentTryCatchExceptionLocal = previousExceptionLocal;
        _currentTryCatchSkipLabel = null;
    }

    private void EmitSegmentInTry(List<Stmt> statements, LocalBuilder caughtExceptionLocal)
    {
        _il.BeginExceptionBlock();
        foreach (var stmt in statements)
            EmitStatement(stmt);

        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

        _il.EndExceptionBlock();
    }

    private bool ContainsAwait(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (ContainsAwaitInStmt(stmt))
                return true;
        }
        return false;
    }

    private bool ContainsAwaitInStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                return ContainsAwaitInExpr(e.Expr);
            case Stmt.Var v:
                return v.Initializer != null && ContainsAwaitInExpr(v.Initializer);
            case Stmt.Return r:
                return r.Value != null && ContainsAwaitInExpr(r.Value);
            case Stmt.If i:
                return ContainsAwaitInExpr(i.Condition) ||
                       ContainsAwaitInStmt(i.ThenBranch) ||
                       (i.ElseBranch != null && ContainsAwaitInStmt(i.ElseBranch));
            case Stmt.While w:
                return ContainsAwaitInExpr(w.Condition) || ContainsAwaitInStmt(w.Body);
            case Stmt.Block b:
                return ContainsAwait(b.Statements);
            case Stmt.Sequence seq:
                return ContainsAwait(seq.Statements);
            case Stmt.TryCatch t:
                return ContainsAwait(t.TryBlock) ||
                       (t.CatchBlock != null && ContainsAwait(t.CatchBlock)) ||
                       (t.FinallyBlock != null && ContainsAwait(t.FinallyBlock));
            default:
                return false;
        }
    }

    private bool ContainsAwaitInExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await:
                return true;
            case Expr.Binary b:
                return ContainsAwaitInExpr(b.Left) || ContainsAwaitInExpr(b.Right);
            case Expr.Logical l:
                return ContainsAwaitInExpr(l.Left) || ContainsAwaitInExpr(l.Right);
            case Expr.Unary u:
                return ContainsAwaitInExpr(u.Right);
            case Expr.Grouping g:
                return ContainsAwaitInExpr(g.Expression);
            case Expr.Call c:
                if (ContainsAwaitInExpr(c.Callee)) return true;
                foreach (var arg in c.Arguments)
                    if (ContainsAwaitInExpr(arg)) return true;
                return false;
            case Expr.Assign a:
                return ContainsAwaitInExpr(a.Value);
            case Expr.Ternary t:
                return ContainsAwaitInExpr(t.Condition) ||
                       ContainsAwaitInExpr(t.ThenBranch) ||
                       ContainsAwaitInExpr(t.ElseBranch);
            case Expr.Get g:
                return ContainsAwaitInExpr(g.Object);
            case Expr.Set s:
                return ContainsAwaitInExpr(s.Object) || ContainsAwaitInExpr(s.Value);
            default:
                return false;
        }
    }

    private void EmitBreak(Stmt.Break b)
    {
        if (b.Label != null)
        {
            // Labeled break - search for matching label in loop stack
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == b.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.BreakLabel);
                    return;
                }
            }
            // Label not found - should have been caught by type checker
        }
        else if (_loopLabels.Count > 0)
        {
            // Unlabeled break - jump to innermost loop's break label
            _il.Emit(OpCodes.Br, _loopLabels.Peek().BreakLabel);
        }
    }

    private void EmitContinue(Stmt.Continue c)
    {
        if (c.Label != null)
        {
            // Labeled continue - search for matching label in loop stack
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == c.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.ContinueLabel);
                    return;
                }
            }
            // Label not found - should have been caught by type checker
        }
        else if (_loopLabels.Count > 0)
        {
            // Unlabeled continue - jump to innermost loop's continue label
            _il.Emit(OpCodes.Br, _loopLabels.Peek().ContinueLabel);
        }
    }

    private void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        // For a labeled statement, we need to:
        // 1. If the inner statement is a loop, add the label name to the loop's entry
        // 2. If the inner statement is not a loop, just emit it (label is for break only, not loops)

        // Create labels for the labeled statement block
        var breakLabel = _il.DefineLabel();

        // Check if the inner statement is a loop - if so, we handle it specially
        if (ls.Statement is Stmt.While or Stmt.ForOf or Stmt.DoWhile or Stmt.ForIn)
        {
            // Emit the loop with the label name in the loop label stack
            EmitLabeledLoop(ls.Label.Lexeme, ls.Statement, breakLabel);
        }
        else
        {
            // Non-loop labeled statement - just push a break-only label
            // (continue doesn't make sense for non-loops)
            _loopLabels.Push((breakLabel, breakLabel, ls.Label.Lexeme));
            EmitStatement(ls.Statement);
            _loopLabels.Pop();
        }

        _il.MarkLabel(breakLabel);
    }

    private void EmitLabeledLoop(string labelName, Stmt loopStmt, Label outerBreakLabel)
    {
        // Emit a loop with a specific label name attached
        switch (loopStmt)
        {
            case Stmt.While w:
                EmitLabeledWhile(labelName, w, outerBreakLabel);
                break;
            case Stmt.ForOf f:
                EmitLabeledForOf(labelName, f, outerBreakLabel);
                break;
            case Stmt.DoWhile dw:
                EmitLabeledDoWhile(labelName, dw, outerBreakLabel);
                break;
            case Stmt.ForIn fi:
                EmitLabeledForIn(labelName, fi, outerBreakLabel);
                break;
        }
    }

    private void EmitLabeledWhile(string labelName, Stmt.While w, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels with the label name
        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledForOf(string labelName, Stmt.ForOf f, Label outerBreakLabel)
    {
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        EmitExpression(f.Iterable);
        EnsureBoxed();

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumLocal);

        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        if (varField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledDoWhile(string labelName, Stmt.DoWhile dw, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        _il.MarkLabel(continueLabel);

        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledForIn(string labelName, Stmt.ForIn f, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysLocal = _il.DeclareLocal(typeof(List<object>));
        _il.Emit(OpCodes.Stloc, keysLocal);

        var indexLocal = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, indexLocal);

        LocalBuilder? loopVar = null;
        if (varField == null)
        {
            loopVar = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, loopVar);
        }

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);

        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetElement);

        if (varField != null)
        {
            var keyTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, keyTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, keyTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            _il.Emit(OpCodes.Stloc, loopVar!);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }

    #endregion

    #region Expression Emission

    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await a:
                EmitAwait(a);
                break;

            case Expr.Literal l:
                EmitLiteral(l);
                break;

            case Expr.Variable v:
                EmitVariable(v);
                break;

            case Expr.Assign a:
                EmitAssign(a);
                break;

            case Expr.Binary b:
                EmitBinary(b);
                break;

            case Expr.Logical l:
                EmitLogical(l);
                break;

            case Expr.Unary u:
                EmitUnary(u);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Grouping g:
                EmitExpression(g.Expression);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
                break;

            case Expr.CompoundAssign ca:
                EmitCompoundAssign(ca);
                break;

            case Expr.PrefixIncrement pi:
                EmitPrefixIncrement(pi);
                break;

            case Expr.PostfixIncrement poi:
                EmitPostfixIncrement(poi);
                break;

            case Expr.ArrayLiteral a:
                EmitArrayLiteral(a);
                break;

            case Expr.ObjectLiteral o:
                EmitObjectLiteral(o);
                break;

            case Expr.GetIndex gi:
                EmitGetIndex(gi);
                break;

            case Expr.SetIndex si:
                EmitSetIndex(si);
                break;

            case Expr.New n:
                EmitNew(n);
                break;

            case Expr.This:
                EmitThis();
                break;

            case Expr.NullishCoalescing nc:
                EmitNullishCoalescing(nc);
                break;

            case Expr.TemplateLiteral tl:
                EmitTemplateLiteral(tl);
                break;

            case Expr.Set s:
                EmitSet(s);
                break;

            case Expr.TypeAssertion ta:
                // Type assertions are compile-time only, just emit the inner expression
                EmitExpression(ta.Expression);
                break;

            case Expr.Spread sp:
                // Spread expressions are handled in context (arrays, objects, calls)
                // If we get here directly, just emit the inner expression
                EmitExpression(sp.Expression);
                break;

            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;

            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.Super s:
                EmitSuper(s);
                break;

            default:
                // Unsupported expression - push null
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
                break;
        }
    }

    private void EmitAwait(Expr.Await a)
    {
        int stateNumber = _currentAwaitState++;
        var resumeLabel = _stateLabels[stateNumber];
        var continueLabel = _il.DefineLabel();
        var awaiterField = _builder.AwaiterFields[stateNumber];

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));

        // 3. Get awaiter: task.GetAwaiter()
        _il.Emit(OpCodes.Call, _builder.GetTaskGetAwaiterMethod());

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_builder.AwaiterType);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, awaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterIsCompletedGetter());
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend
        // this.<>1__state = stateNumber
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethod());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _endLabel);

        // 7. Resume point (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        // If we're inside a try block with awaits, wrap GetResult in try/catch
        if (_currentTryCatchExceptionLocal != null)
        {
            var getResultDoneLabel = _il.DefineLabel();

            _il.BeginExceptionBlock();

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());

            // Store result temporarily
            var resultTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.BeginCatchBlock(typeof(Exception));
            // Wrap and store exception
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, _currentTryCatchExceptionLocal);
            // Push null as result
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.EndExceptionBlock();

            _il.MarkLabel(getResultDoneLabel);
            _il.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());
        }

        // Result is now on stack
        _stackType = StackType.Unknown;
    }

    private void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Null;
                break;
            case double d:
                _il.Emit(OpCodes.Ldc_R8, d);
                _stackType = StackType.Double;
                break;
            case bool b:
                _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _stackType = StackType.Boolean;
                break;
            case string s:
                _il.Emit(OpCodes.Ldstr, s);
                _stackType = StackType.String;
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
                break;
        }
    }

    private void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        // Check if hoisted to state machine field
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a local variable
        if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if it's a function
        if (_ctx.Functions.TryGetValue(name, out var funcMethod))
        {
            // Create TSFunction wrapper
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Not found - push null
        _il.Emit(OpCodes.Ldnull);
        _stackType = StackType.Unknown;
    }

    private void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();

        // Duplicate for return value
        _il.Emit(OpCodes.Dup);

        // Check if hoisted
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        _stackType = StackType.Unknown;
    }

    private void EmitSuper(Expr.Super s)
    {
        // Load hoisted 'this' from state machine field
        // In async methods, 'this' is hoisted to the state machine struct
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);  // State machine ref
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
        }
        else
        {
            // Fallback - shouldn't happen if 'this' hoisting is working properly
            _il.Emit(OpCodes.Ldnull);
        }

        // Load the method name
        _il.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");

        // Call GetSuperMethod(instance, methodName) to get a callable wrapper (TSFunction)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetSuperMethod);
        _stackType = StackType.Unknown;
    }

    private void EmitBinary(Expr.Binary b)
    {
        // Emit left and right operands
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        // Use runtime Add method for + since it handles string concatenation
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                // Convert to double and subtract
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Sub);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.STAR:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Mul);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.SLASH:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Div);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.PERCENT:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Rem);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.LESS:
                EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                // Equals returns unboxed bool, need to box it
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                // Negate the result (Equals returns unboxed bool)
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitToDouble()
    {
        // Stack has: left, right (both boxed)
        // Need to convert left to double, then convert right to double
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
    }

    private void EmitNumericComparison(OpCode compareOp)
    {
        // Convert both to double and compare
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(compareOp);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonLe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        // a <= b is equivalent to !(a > b)
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonGe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        // a >= b is equivalent to !(a < b)
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitLogical(Expr.Logical l)
    {
        var endLabel = _il.DefineLabel();

        EmitExpression(l.Left);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        EmitTruthyCheck();

        if (l.Operator.Type == TokenType.AND_AND)
        {
            // Short-circuit: if left is falsy, return left
            _il.Emit(OpCodes.Brfalse, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }
        else // OR
        {
            // Short-circuit: if left is truthy, return left
            _il.Emit(OpCodes.Brtrue, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }

        _il.MarkLabel(endLabel);
        _stackType = StackType.Unknown;
    }

    private void EmitUnary(Expr.Unary u)
    {
        EmitExpression(u.Right);

        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Neg);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.BANG:
                EnsureBoxed();
                EmitTruthyCheck();
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.TYPEOF:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
                _il.Emit(OpCodes.Not);
                _il.Emit(OpCodes.Conv_R8);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            default:
                EnsureBoxed();
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitCall(Expr.Call c)
    {
        // Handle console.log specially (parser converts console.log to Expr.Variable("console.log"))
        if (c.Callee is Expr.Variable consoleVar && consoleVar.Name.Lexeme == "console.log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Also handle the Get pattern in case it's used differently
        if (c.Callee is Expr.Get g && g.Object is Expr.Variable v && v.Name.Lexeme == "console" && g.Name.Lexeme == "log")
        {
            if (c.Arguments.Count > 0)
            {
                EmitExpression(c.Arguments[0]);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
            }
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Handle Promise.xxx() static calls - returns Task<object?> without synchronously awaiting
        if (c.Callee is Expr.Get promiseGet &&
            promiseGet.Object is Expr.Variable promiseVar &&
            promiseVar.Name.Lexeme == "Promise")
        {
            EmitPromiseStaticCall(promiseGet.Name.Lexeme, c.Arguments);
            return;
        }

        // Handle Promise instance methods: promise.then(onFulfilled?, onRejected?)
        // promise.catch(onRejected), promise.finally(onFinally)
        if (c.Callee is Expr.Get methodGet)
        {
            string methodName = methodGet.Name.Lexeme;
            if (methodName is "then" or "catch" or "finally")
            {
                EmitPromiseInstanceMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle string-only methods
            if (methodName is "charAt" or "substring" or "toUpperCase" or "toLowerCase"
                or "trim" or "replace" or "split" or "startsWith" or "endsWith"
                or "repeat" or "padStart" or "padEnd" or "charCodeAt" or "lastIndexOf"
                or "trimStart" or "trimEnd" or "replaceAll" or "at")
            {
                EmitStringMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle array-only methods
            if (methodName is "pop" or "shift" or "unshift" or "map" or "filter" or "forEach"
                or "push" or "find" or "findIndex" or "some" or "every" or "reduce" or "join"
                or "reverse" or "concat")
            {
                EmitArrayMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Handle ambiguous methods (slice, concat, includes, indexOf) that exist on both string and array
            if (methodName is "slice" or "concat" or "includes" or "indexOf")
            {
                // Try to get type info for better dispatch
                var objType = _ctx?.TypeMap?.Get(methodGet.Object);
                if (objType is TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING })
                {
                    EmitStringMethodCall(methodGet.Object, methodName, c.Arguments);
                    return;
                }
                if (objType is TypeSystem.TypeInfo.Array)
                {
                    EmitArrayMethodCall(methodGet.Object, methodName, c.Arguments);
                    return;
                }
                // Fallback: runtime dispatch for any/unknown types
                EmitAmbiguousMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Try direct dispatch for known class instance methods
            if (TryEmitDirectMethodCall(methodGet.Object, methodName, c.Arguments))
                return;
        }

        // Check if it's a direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(funcVar.Name.Lexeme, out var funcMethod))
        {
            // Direct call to known function
            // IMPORTANT: In async context, await can happen in arguments
            // Emit all arguments first and store to temps
            var parameters = funcMethod.GetParameters();
            var directArgTemps = new List<LocalBuilder>();

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < c.Arguments.Count)
                {
                    EmitExpression(c.Arguments[i]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                directArgTemps.Add(temp);
            }

            // Now load all args from temps and call
            foreach (var temp in directArgTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }
            _il.Emit(OpCodes.Call, funcMethod);
            _stackType = StackType.Unknown;
            return;
        }

        // Generic call through TSFunction
        // IMPORTANT: In async context, await can happen in callee or arguments
        // Emit all parts that may contain await first and store to temps

        // Emit callee first and save to temp
        EmitExpression(c.Callee);
        EnsureBoxed();
        var calleeTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, calleeTemp);

        // Emit all arguments and save to temps
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in c.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Now build the call with saved values (no awaits can happen here)
        _il.Emit(OpCodes.Ldloc, calleeTemp);

        // Build arguments array from temps
        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        _stackType = StackType.Unknown;
    }

    private void EmitGet(Expr.Get g)
    {
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        _stackType = StackType.Unknown;
    }

    private void EmitTernary(Expr.Ternary t)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(t.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitExpression(t.ThenBranch);
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        _stackType = StackType.Unknown;
    }

    private void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        // IMPORTANT: Emit the new value first (may contain await which clears stack)
        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Now load current value (after await if any)
        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();

        // Load the value back
        _il.Emit(OpCodes.Ldloc, valueTemp);

        // Apply operation
        var op = ca.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            default:
                // For other compound ops, convert to double
                var rightLocal = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Ldloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

                switch (op)
                {
                    case TokenType.MINUS_EQUAL:
                        _il.Emit(OpCodes.Sub);
                        break;
                    case TokenType.STAR_EQUAL:
                        _il.Emit(OpCodes.Mul);
                        break;
                    case TokenType.SLASH_EQUAL:
                        _il.Emit(OpCodes.Div);
                        break;
                    default:
                        _il.Emit(OpCodes.Add);
                        break;
                }
                _il.Emit(OpCodes.Box, typeof(double));
                break;
        }

        // Store result
        _il.Emit(OpCodes.Dup);
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        _stackType = StackType.Unknown;
    }

    private void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load, increment, store, return new value
            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            _il.Emit(OpCodes.Dup);
            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        _stackType = StackType.Unknown;
    }

    private void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load original value
            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            // Increment and store
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }

            // Original value is on stack
        }
        _stackType = StackType.Unknown;
    }

    #endregion

    #region Helpers

    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case StackType.Boolean:
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
        }
        _stackType = StackType.Unknown;
    }

    private void EmitTruthyCheck()
    {
        // Call runtime IsTruthy method
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _stackType = StackType.Boolean;
    }

    /// <summary>
    /// Emits a Promise static method call (resolve, reject, all, race, allSettled, any).
    /// Returns Task&lt;object?&gt; on the stack - does NOT synchronously await.
    /// The await is handled by the async state machine's EmitAwait.
    /// </summary>
    private void EmitPromiseStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "resolve":
                // Promise.resolve(value?)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseResolve);
                _stackType = StackType.Unknown;
                return;

            case "reject":
                // Promise.reject(reason)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseReject);
                _stackType = StackType.Unknown;
                return;

            case "all":
                // Promise.all(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAll);
                _stackType = StackType.Unknown;
                return;

            case "race":
                // Promise.race(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseRace);
                _stackType = StackType.Unknown;
                return;

            case "allSettled":
                // Promise.allSettled(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAllSettled);
                _stackType = StackType.Unknown;
                return;

            case "any":
                // Promise.any(iterable)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseAny);
                _stackType = StackType.Unknown;
                return;

            default:
                _il.Emit(OpCodes.Ldnull);
                _stackType = StackType.Unknown;
                return;
        }
    }

    /// <summary>
    /// Emits a Promise instance method call (.then, .catch, .finally).
    /// These methods take callbacks and return a new Promise (Task).
    /// </summary>
    private void EmitPromiseInstanceMethodCall(Expr promise, string methodName, List<Expr> arguments)
    {
        // Emit the promise (should be Task<object?>)
        EmitExpression(promise);
        EnsureBoxed();

        // Cast to Task<object?> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object?>));

        switch (methodName)
        {
            case "then":
                // promise.then(onFulfilled?, onRejected?)
                // PromiseThen(Task<object?> promise, object? onFulfilled, object? onRejected)

                // onFulfilled callback (optional)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                // onRejected callback (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseThen);
                break;

            case "catch":
                // promise.catch(onRejected)
                // PromiseCatch(Task<object?> promise, object? onRejected)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseCatch);
                break;

            case "finally":
                // promise.finally(onFinally)
                // PromiseFinally(Task<object?> promise, object? onFinally)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseFinally);
                break;

            default:
                // Unknown method - just return the promise unchanged
                break;
        }

        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a string method call.
    /// </summary>
    private void EmitStringMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the string object
        EmitExpression(obj);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "charAt":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringCharAt);
                break;

            case "substring":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSubstring);
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "toUpperCase":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToUpper", Type.EmptyTypes)!);
                break;

            case "toLowerCase":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                break;

            case "trim":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Trim", Type.EmptyTypes)!);
                break;

            case "replace":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringReplace);
                break;

            case "split":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSplit);
                break;

            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "startsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringStartsWith);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "endsWith":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringEndsWith);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSlice);
                break;

            case "repeat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringRepeat);
                break;

            case "padStart":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringPadStart);
                break;

            case "padEnd":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringPadEnd);
                break;

            case "charCodeAt":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringCharCodeAt);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "concat":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringConcat);
                break;

            case "lastIndexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringLastIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "trimStart":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimStart", Type.EmptyTypes)!);
                break;

            case "trimEnd":
                _il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("TrimEnd", Type.EmptyTypes)!);
                break;

            case "replaceAll":
                if (arguments.Count >= 2)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringReplaceAll);
                break;

            case "at":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Unbox_Any, typeof(double));
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringAt);
                break;
        }

        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits an array method call.
    /// </summary>
    private void EmitArrayMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the array object
        EmitExpression(obj);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(List<object>));

        switch (methodName)
        {
            case "pop":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayPop);
                break;

            case "shift":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayShift);
                break;

            case "unshift":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayUnshift);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "push":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayPush);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArraySlice);
                break;

            case "map":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayMap);
                break;

            case "filter":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayFilter);
                break;

            case "forEach":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayForEach);
                _il.Emit(OpCodes.Ldnull); // forEach returns undefined
                break;

            case "find":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayFind);
                break;

            case "findIndex":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayFindIndex);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "some":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArraySome);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "every":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayEvery);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "reduce":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayReduce);
                break;

            case "join":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayJoin);
                break;

            case "concat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayConcat);
                break;

            case "reverse":
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayReverse);
                break;

            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            default:
                // Unknown method - return null
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }

        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a method call for methods that could be on either string or array (slice, concat, includes, indexOf).
    /// Uses runtime type checking to dispatch to the correct implementation.
    /// </summary>
    private void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object
        EmitExpression(obj);
        EnsureBoxed();

        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        // Check if it's a string
        var isStringLabel = _il.DefineLabel();
        var isListLabel = _il.DefineLabel();
        var doneLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Isinst, typeof(string));
        _il.Emit(OpCodes.Brtrue, isStringLabel);

        // Assume it's a list if not a string
        _il.Emit(OpCodes.Br, isListLabel);

        // String path
        _il.MarkLabel(isStringLabel);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSlice);
                break;

            case "concat":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringConcat);
                break;
        }

        _il.Emit(OpCodes.Br, doneLabel);

        // List path
        _il.MarkLabel(isListLabel);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Castclass, typeof(List<object>));

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArraySlice);
                break;

            case "concat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayConcat);
                break;
        }

        _il.MarkLabel(doneLabel);
        _stackType = StackType.Unknown;
    }

    private void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(a.Elements[i]);
                EnsureBoxed();
                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    EmitExpression(spread.Expression);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_I4, 1);
                    _il.Emit(OpCodes.Newarr, typeof(object));
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4_0);
                    EmitExpression(a.Elements[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
                }

                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
        _stackType = StackType.Unknown;
    }

    private void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);

        if (!hasSpreads)
        {
            // Simple case: no spreads
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldstr, prop.Name!.Lexeme);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads, use MergeIntoObject
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MergeIntoObject);
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, prop.Name!.Lexeme);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        _stackType = StackType.Unknown;
    }

    private void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        _stackType = StackType.Unknown;
    }

    private void EmitSetIndex(Expr.SetIndex si)
    {
        EmitExpression(si.Object);
        EnsureBoxed();
        EmitExpression(si.Index);
        EnsureBoxed();
        EmitExpression(si.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        _stackType = StackType.Unknown;
    }

    private void EmitNew(Expr.New n)
    {
        if (_ctx!.Classes.TryGetValue(n.ClassName.Lexeme, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(n.ClassName.Lexeme, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation (e.g., new Box<number>(42))
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(n.ClassName.Lexeme, out var _) == true)
            {
                // Resolve type arguments
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();

                // Create the constructed generic type
                targetType = typeBuilder.MakeGenericType(typeArgs);

                // Get the constructor on the constructed type
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            // Get expected parameter count from constructor definition
            int expectedParamCount = ctorBuilder.GetParameters().Length;

            // IMPORTANT: In async, await can happen in arguments
            // Emit all arguments first and store to temps
            var argTemps = new List<LocalBuilder>();
            foreach (var arg in n.Arguments)
            {
                EmitExpression(arg);
                EnsureBoxed();
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                argTemps.Add(temp);
            }

            // Now load all arguments onto stack
            foreach (var temp in argTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }

            // Pad missing optional arguments with null
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                _il.Emit(OpCodes.Ldnull);
            }

            // Call the constructor directly using newobj
            _il.Emit(OpCodes.Newobj, targetCtor);
            _stackType = StackType.Unknown;
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Null;
        }
    }

    private Type ResolveTypeArg(string typeArg)
    {
        // Simple type argument resolution - similar to ILEmitter
        return typeArg switch
        {
            "number" => typeof(object),
            "string" => typeof(object),
            "boolean" => typeof(object),
            "any" => typeof(object),
            _ => typeof(object)
        };
    }

    private void EmitThis()
    {
        // 'this' in async methods - load from hoisted field if available
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);  // Load state machine ref
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            _stackType = StackType.Unknown;
        }
        else
        {
            // Not an instance method or 'this' not hoisted - emit null
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Null;
        }
    }

    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var rightLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        // Emit left side
        EmitExpression(nc.Left);
        EnsureBoxed();

        // Check for null/undefined
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);

        // Left is not null - use it
        _il.Emit(OpCodes.Br, endLabel);

        // Left was null - pop and emit right
        _il.MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        _stackType = StackType.Unknown;
    }

    private void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // TemplateLiteral has Strings (literal parts) and Expressions (interpolated parts)
        // Structure: strings[0] + expressions[0] + strings[1] + expressions[1] + ... + strings[n]
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            _stackType = StackType.String;
            return;
        }

        // Start with first string
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        // Interleave expressions and remaining strings
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            // Emit expression, convert to string
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);

            // Emit next string part
            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
            }
        }
        _stackType = StackType.String;
    }

    private void EmitSet(Expr.Set s)
    {
        // Build stack for SetProperty(obj, name, value)
        EmitExpression(s.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        EmitExpression(s.Value);
        EnsureBoxed();

        // Dup value for expression result (assignment returns the value)
        _il.Emit(OpCodes.Dup);
        var resultTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultTemp);

        // Call SetProperty (returns void)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        // Put result back on stack
        _il.Emit(OpCodes.Ldloc, resultTemp);
        _stackType = StackType.Unknown;
    }

    private void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Compound assignment on object property: obj.prop += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // IMPORTANT: Emit value first (may contain await which clears stack)
        EmitExpression(cs.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Get current value: GetProperty(obj, name)
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        // Load value and apply operation
        _il.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(cs.Operator.Type);

        // Store result: SetProperty(obj, name, value)
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        // Leave result on stack
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _stackType = StackType.Unknown;
    }

    private void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        // Compound assignment on array element: arr[i] += x
        // 1. Get current value
        // 2. Apply operation
        // 3. Store back

        // IMPORTANT: Emit value first (may contain await which clears stack)
        EmitExpression(csi.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Emit object and index, store them
        EmitExpression(csi.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(csi.Index);
        EnsureBoxed();
        var indexTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexTemp);

        // Get current value: GetIndex(obj, index)
        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);

        // Load value and apply operation
        _il.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(csi.Operator.Type);

        // Store result: SetIndex(obj, index, value)
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, resultLocal);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);

        // Leave result on stack
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _stackType = StackType.Unknown;
    }

    private void EmitCompoundOperation(TokenType opType)
    {
        // Stack has: currentValue (object), operandValue (object)
        // Apply the compound operation

        if (opType == TokenType.PLUS_EQUAL)
        {
            // Use runtime Add for string concatenation support
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
            return;
        }

        // For other operations, convert to double
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (opType)
        {
            case TokenType.MINUS_EQUAL:
                _il.Emit(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                _il.Emit(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                _il.Emit(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                _il.Emit(OpCodes.Rem);
                break;
            default:
                _il.Emit(OpCodes.Add);
                break;
        }

        _il.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check for async arrow functions first
        if (af.IsAsync)
        {
            EmitAsyncArrowFunction(af);
            return;
        }

        // Get the method for this arrow function (pre-compiled)
        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // Capturing arrow: create display class instance and populate fields
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            // Non-capturing arrow: create TSFunction wrapping static method
            EmitNonCapturingArrowFunction(af, method);
        }
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af)
    {
        // Get the async arrow state machine builder
        if (_ctx?.AsyncArrowBuilders == null ||
            !_ctx.AsyncArrowBuilders.TryGetValue(af, out var arrowBuilder))
        {
            throw new InvalidOperationException(
                "Async arrow function not registered with state machine builder.");
        }

        // Create a TSFunction that wraps the stub method
        // The stub takes (outer state machine boxed, params...) and returns Task<object>
        // We pass the SelfBoxed reference (not a new box) to share state with the arrow

        // Load the SelfBoxed field - this is the same boxed instance the runtime is using
        if (_builder.SelfBoxedField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SelfBoxedField);
        }
        else
        {
            // Fallback: box current state machine (won't share mutations correctly)
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldobj, _builder.StateMachineType);
            _il.Emit(OpCodes.Box, _builder.StateMachineType);
        }

        // Load the stub method
        _il.Emit(OpCodes.Ldtoken, arrowBuilder.StubMethod);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Create TSFunction(target: boxed outer SM, method: stub)
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);

        _stackType = StackType.Unknown;
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        // Get the pre-tracked constructor
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            _stackType = StackType.Unknown;
            return;
        }

        // Create display class instance
        _il.Emit(OpCodes.Newobj, displayCtor);

        // Get captured variables field mapping
        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            // No fields to populate, just create TSFunction
            _il.Emit(OpCodes.Ldtoken, method);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            _stackType = StackType.Unknown;
            return;
        }

        // Populate captured fields from async state machine context
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup); // Keep display class instance on stack

            // Try to load the captured variable from various sources:
            // 1. Hoisted local/parameter in state machine
            var hoistedField = _builder.GetVariableField(capturedVar);
            if (hoistedField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);  // State machine ref
                _il.Emit(OpCodes.Ldfld, hoistedField);
            }
            // 2. Hoisted 'this' reference
            else if (capturedVar == "this" && _builder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);  // State machine ref
                _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            }
            // 3. Regular local variable (not hoisted)
            else if (_ctx.Locals.TryGetLocal(capturedVar, out var local))
            {
                _il.Emit(OpCodes.Ldloc, local);
            }
            // 4. Fallback: null
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Stfld, field);
        }

        // Create TSFunction: new TSFunction(displayInstance, method)
        // Stack has: displayInstance
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        _stackType = StackType.Unknown;
    }

    private void EmitNonCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method)
    {
        // Create TSFunction for static method:
        // new TSFunction(null, method)

        // Push null (no target)
        _il.Emit(OpCodes.Ldnull);

        // Load the method as a runtime handle and convert to MethodInfo
        _il.Emit(OpCodes.Ldtoken, method);

        // For static methods on a non-generic type:
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(
            "GetMethodFromHandle",
            [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));

        // Call TSFunction constructor
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Try to emit a direct method call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        // Try to get type information for the receiver
        var receiverType = _ctx?.TypeMap?.Get(receiver);

        // Only handle Instance types (e.g., let f: Foo = new Foo())
        if (receiverType is not TypeSystem.TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? className = instance.ClassType switch
        {
            TypeSystem.TypeInfo.Class c => c.Name,
            _ => null
        };
        if (className == null)
            return false;

        // Look up the method in the class hierarchy
        var methodBuilder = _ctx!.ResolveInstanceMethod(className, methodName);
        if (methodBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Get expected parameter count from method definition
        int expectedParamCount = methodBuilder.GetParameters().Length;

        // IMPORTANT: In async context, await can happen in arguments
        // Emit all arguments first and store to temps before emitting receiver
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Now emit receiver and cast
        EmitExpression(receiver);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, classType);

        // Load all arguments back onto stack
        foreach (var temp in argTemps)
        {
            _il.Emit(OpCodes.Ldloc, temp);
        }

        // Pad missing optional arguments with null
        for (int i = arguments.Count; i < expectedParamCount; i++)
        {
            _il.Emit(OpCodes.Ldnull);
        }

        // Emit the virtual call
        _il.Emit(OpCodes.Callvirt, methodBuilder);
        _stackType = StackType.Unknown;
        return true;
    }

    #endregion
}
