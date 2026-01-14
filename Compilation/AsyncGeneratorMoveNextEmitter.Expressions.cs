using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    #region Yield Expressions

    protected override void EmitYield(Expr.Yield y)
    {
        int stateNumber = _currentSuspensionState++;
        var resumeLabel = _stateLabels[stateNumber];

        // Handle yield* delegation
        if (y.IsDelegating && y.Value != null)
        {
            EmitYieldStar(y, stateNumber, resumeLabel);
            return;
        }

        // 1. Emit the yield value (or null if no value)
        if (y.Value != null)
        {
            EmitExpression(y.Value);
            EnsureBoxed();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        // 2. Store value in <>2__current field
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // 3. Set state to the resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 4. Return ValueTask<bool>(true) - has value
        EmitReturnValueTaskBool(true);

        // 5. Mark the resume label (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // 6. Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 7. yield expression evaluates to undefined (null) when resumed
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitYieldStar(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // yield* delegates to another iterable (sync or async)
        var delegatedField = _builder.DelegatedAsyncEnumeratorField;

        if (delegatedField == null)
        {
            EmitYieldStarSync(y, stateNumber, resumeLabel);
            return;
        }

        // Check the type of the yield* expression to determine sync vs async
        // For now, try async first (check if it's IAsyncEnumerator), fall back to sync
        EmitYieldStarWithTypeCheck(y, stateNumber, resumeLabel, delegatedField);
    }

    private void EmitYieldStarWithTypeCheck(Expr.Yield y, int stateNumber, Label resumeLabel, FieldBuilder delegatedField)
    {
        // Structure:
        // 1. First-entry path: evaluate expression, check type, set up iteration, goto appropriate loop
        // 2. Resume path: check field type, dispatch to appropriate loop
        // 3. Sync loop
        // 4. Async loop
        // 5. End/cleanup

        var syncLoopLabel = _il.DefineLabel();
        var asyncLoopLabel = _il.DefineLabel();
        var syncSetupLabel = _il.DefineLabel();
        var asyncSetupLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        // === First entry path: evaluate and check type ===
        EmitExpression(y.Value!);
        EnsureBoxed();

        var iterableTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, iterableTemp);

        // Check if it's IAsyncEnumerator<object> (async generators implement this)
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Isinst, _types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Brtrue, asyncSetupLabel);

        // Sync setup
        _il.MarkLabel(syncSetupLabel);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        _il.Emit(OpCodes.Callvirt, getEnumerator);
        // Store in field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, delegatedField); // load field address for swap
        _il.Emit(OpCodes.Pop); // pop address
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);
        _il.Emit(OpCodes.Br, syncLoopLabel);

        // Async setup
        _il.MarkLabel(asyncSetupLabel);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);
        var asyncEnumTemp = _il.DeclareLocal(_types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Stloc, asyncEnumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, asyncEnumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);
        _il.Emit(OpCodes.Br, asyncLoopLabel);

        // === Resume path ===
        _il.MarkLabel(resumeLabel);
        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        // Check field type to determine sync vs async
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Isinst, _types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Brtrue, asyncLoopLabel);
        _il.Emit(OpCodes.Br, syncLoopLabel);

        // === Sync loop ===
        var syncLoopEnd = _il.DefineLabel();
        _il.MarkLabel(syncLoopLabel);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, syncLoopEnd);

        // Get current
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, current);

        // Store in current field
        var syncValueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, syncValueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, syncValueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        _il.MarkLabel(syncLoopEnd);
        _il.Emit(OpCodes.Br, endLabel);

        // === Async loop ===
        var asyncLoopEnd = _il.DefineLabel();
        _il.MarkLabel(asyncLoopLabel);

        // Call MoveNextAsync
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);
        var moveNextAsync = _types.GetMethodNoParams(_types.IAsyncEnumeratorOfObject, "MoveNextAsync");
        _il.Emit(OpCodes.Callvirt, moveNextAsync);

        // Get result synchronously (simplified - full impl would suspend here)
        var valueTaskLocal = _il.DeclareLocal(_types.ValueTaskOfBool);
        _il.Emit(OpCodes.Stloc, valueTaskLocal);
        _il.Emit(OpCodes.Ldloca, valueTaskLocal);
        var getAwaiter = _types.GetMethodNoParams(_types.ValueTaskOfBool, "GetAwaiter");
        _il.Emit(OpCodes.Call, getAwaiter);
        var awaiterLocal = _il.DeclareLocal(_types.ValueTaskAwaiterOfBool);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResult = _types.GetMethodNoParams(_types.ValueTaskAwaiterOfBool, "GetResult");
        _il.Emit(OpCodes.Call, getResult);

        _il.Emit(OpCodes.Brfalse, asyncLoopEnd);

        // Get Current
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);
        var currentGetter = _types.GetPropertyGetter(_types.IAsyncEnumeratorOfObject, "Current");
        _il.Emit(OpCodes.Callvirt, currentGetter);

        // Store in current field
        var asyncValueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, asyncValueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, asyncValueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        _il.MarkLabel(asyncLoopEnd);

        // === End/cleanup ===
        _il.MarkLabel(endLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitYieldStarSyncFromStack(int stateNumber, Label resumeLabel, FieldBuilder delegatedField)
    {
        // Stack has: [iterable]
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        var loopStart = _il.DefineLabel();
        var loopEnd = _il.DefineLabel();

        // Cast to IEnumerable and get enumerator
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        // Store enumerator in field
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // Fall through to loop start
        _il.Emit(OpCodes.Br, loopStart);

        // Resume label
        _il.MarkLabel(resumeLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Loop start
        _il.MarkLabel(loopStart);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, current);

        // Store in current field
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        // Loop end
        _il.MarkLabel(loopEnd);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitYieldStarSync(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // Sync yield* delegation using IEnumerable
        // The enumerator must be stored in a FIELD (not local) to persist across suspensions

        var delegatedField = _builder.DelegatedAsyncEnumeratorField;
        if (delegatedField == null)
        {
            // No field available - shouldn't happen if HasYieldStar was detected
            EmitExpression(y.Value!);
            EnsureBoxed();
            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        var loopStart = _il.DefineLabel();
        var loopEnd = _il.DefineLabel();

        // Emit the iterable expression and get its enumerator
        EmitExpression(y.Value!);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        // Store enumerator in field (persists across suspensions)
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // Fall through to loop start
        _il.Emit(OpCodes.Br, loopStart);

        // Resume label - jumped to from state switch when resuming after yield
        _il.MarkLabel(resumeLabel);
        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Loop start - check if more elements
        _il.MarkLabel(loopStart);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current value
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, current);

        // Store in <>2__current
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state to resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Return true (has value)
        EmitReturnValueTaskBool(true);

        // Loop end - delegation finished
        _il.MarkLabel(loopEnd);

        // Clear the delegated field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #endregion

    #region Await Expressions

    protected override void EmitAwait(Expr.Await a)
    {
        int stateNumber = _currentSuspensionState++;
        var resumeLabel = _stateLabels[stateNumber];
        var continueLabel = _il.DefineLabel();

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));

        // 2b. Store the task in AwaitedTaskField (needed for continuation if not completed)
        // Stack: [task]
        var taskLocal = _il.DeclareLocal(typeof(Task<object>));
        _il.Emit(OpCodes.Stloc, taskLocal);       // Stack: []
        _il.Emit(OpCodes.Ldarg_0);                // Stack: [this]
        _il.Emit(OpCodes.Ldloc, taskLocal);       // Stack: [this, task]
        _il.Emit(OpCodes.Stfld, _builder.AwaitedTaskField); // Stack: []
        _il.Emit(OpCodes.Ldloc, taskLocal);       // Stack: [task]

        // 3. Get awaiter: task.GetAwaiter()
        var getAwaiterMethod = typeof(Task<object>).GetMethod("GetAwaiter")!;
        _il.Emit(OpCodes.Call, getAwaiterMethod);

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_types.TaskAwaiterOfObject);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, _builder.AwaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
        var isCompletedGetter = _types.TaskAwaiterOfObject.GetProperty("IsCompleted")!.GetGetMethod()!;
        _il.Emit(OpCodes.Call, isCompletedGetter);
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend and return a pending ValueTask<bool>
        // Set state to resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // For async generators, we need to return a ValueTask<bool> that will complete when the await completes
        // The simplest approach: wrap the continuing task
        // Create a continuation that resumes MoveNextAsync
        EmitAwaitSuspensionReturn();

        // 7. Resume point (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
        var getResultMethod = _types.TaskAwaiterOfObject.GetMethod("GetResult")!;
        _il.Emit(OpCodes.Call, getResultMethod);

        // Result is now on stack
        SetStackUnknown();
    }

    private void EmitAwaitSuspensionReturn()
    {
        // Call emitted AsyncGeneratorAwaitContinue(task, generator)
        // This creates a proper continuation that calls MoveNextAsync after the await completes

        // Load the awaited task from field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.AwaitedTaskField);

        // Load this (the generator) as IAsyncEnumerator<object>
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);

        // Call AsyncGeneratorAwaitContinue(task, generator) - use emitted method for standalone support
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.AsyncGeneratorAwaitContinue);

        // Returns ValueTask<bool>, return it
        _il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Literal Expressions

    protected override void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                EmitNullConstant();
                break;
            case double d:
                EmitDoubleConstant(d);
                break;
            case bool b:
                EmitBoolConstant(b);
                break;
            case string s:
                EmitStringConstant(s);
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    #endregion

    #region Variable Expressions

    protected override void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        // Try resolver first (hoisted fields and non-hoisted locals)
        var stackType = _resolver!.TryLoadVariable(name);
        if (stackType != null)
        {
            SetStackType(stackType.Value);
            return;
        }

        // Fallback: Check if it's a function
        if (_ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod))
        {
            // Create TSFunction wrapper
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a namespace - load the static field
        if (_ctx.NamespaceFields?.TryGetValue(name, out var nsField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        // Not found - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();

        // Duplicate for return value
        _il.Emit(OpCodes.Dup);

        // Use resolver to store (consumes one copy, leaves one on stack as return value)
        _resolver!.TryStoreVariable(name);

        SetStackUnknown();
    }

    #endregion

    #region Binary/Logical Expressions

    protected override void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _helpers.EmitCallUnknown(_ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                _helpers.EmitArithmeticBinary(OpCodes.Sub);
                break;
            case TokenType.STAR:
                _helpers.EmitArithmeticBinary(OpCodes.Mul);
                break;
            case TokenType.SLASH:
                _helpers.EmitArithmeticBinary(OpCodes.Div);
                break;
            case TokenType.PERCENT:
                _helpers.EmitArithmeticBinary(OpCodes.Rem);
                break;
            case TokenType.LESS:
                _helpers.EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                _helpers.EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                _helpers.EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                _helpers.EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _helpers.EmitRuntimeEquals(_ctx!.Runtime!.Equals);
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _helpers.EmitRuntimeNotEquals(_ctx!.Runtime!.Equals);
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    #endregion

    #region Call Expressions

    protected override void EmitCall(Expr.Call c)
    {
        // Handle console.log specially (handles both Variable and Get patterns)
        if (_helpers.TryEmitConsoleLog(c,
            arg => { EmitExpression(arg); EnsureBoxed(); },
            _ctx!.Runtime!.ConsoleLog))
        {
            return;
        }

        // Check if it's a direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(funcVar.Name.Lexeme), out var funcMethod))
        {
            var parameters = funcMethod.GetParameters();
            List<LocalBuilder> argTemps = [];

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
                argTemps.Add(temp);
            }

            foreach (var temp in argTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }
            _il.Emit(OpCodes.Call, funcMethod);
            SetStackUnknown();
            return;
        }

        // Generic call through TSFunction
        EmitExpression(c.Callee);
        EnsureBoxed();
        var calleeTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, calleeTemp);

        List<LocalBuilder> genericArgTemps = [];
        foreach (var arg in c.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            genericArgTemps.Add(temp);
        }

        _il.Emit(OpCodes.Ldloc, calleeTemp);

        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < genericArgTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, genericArgTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        SetStackUnknown();
    }

    #endregion

    #region Member Access Expressions

    protected override void EmitGet(Expr.Get g)
    {
        // Special case: Symbol well-known symbols
        if (g.Object is Expr.Variable symV && symV.Name.Lexeme == "Symbol")
        {
            switch (g.Name.Lexeme)
            {
                case "iterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
                    SetStackUnknown();
                    return;
                case "asyncIterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
                    SetStackUnknown();
                    return;
                case "toStringTag":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToStringTag);
                    SetStackUnknown();
                    return;
                case "hasInstance":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolHasInstance);
                    SetStackUnknown();
                    return;
                case "isConcatSpreadable":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIsConcatSpreadable);
                    SetStackUnknown();
                    return;
                case "toPrimitive":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToPrimitive);
                    SetStackUnknown();
                    return;
                case "species":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolSpecies);
                    SetStackUnknown();
                    return;
                case "unscopables":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolUnscopables);
                    SetStackUnknown();
                    return;
            }
        }

        // Handle static field access
        if (g.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(g.Name.Lexeme, out var staticField))
            {
                _il.Emit(OpCodes.Ldsfld, staticField);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property access
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    protected override void EmitSet(Expr.Set s)
    {
        EmitExpression(s.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(s.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);

        SetStackUnknown();
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        SetStackUnknown();
    }

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        EmitExpression(si.Object);
        EnsureBoxed();
        EmitExpression(si.Index);
        EnsureBoxed();
        EmitExpression(si.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        SetStackUnknown();
    }

    #endregion


    #region Increment/Decrement Expressions

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();

        _il.Emit(OpCodes.Ldloc, valueTemp);

        var op = ca.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            default:
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

        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

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
        SetStackUnknown();
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

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
        }
        SetStackUnknown();
    }

    protected override void EmitCompoundSet(Expr.CompoundSet cs)
    {
        EmitExpression(cs.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);

        EmitExpression(cs.Value);
        EnsureBoxed();

        switch (cs.Operator.Type)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx.Runtime.Add);
                break;
            default:
                var rightLocal = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Ldloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

                switch (cs.Operator.Type)
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

        _il.Emit(OpCodes.Dup);
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Call, _ctx.Runtime.SetProperty);

        SetStackUnknown();
    }

    protected override void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        EmitExpression(csi.Object);
        EnsureBoxed();
        var objTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objTemp);

        EmitExpression(csi.Index);
        EnsureBoxed();
        var indexTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);

        EmitExpression(csi.Value);
        EnsureBoxed();

        switch (csi.Operator.Type)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx.Runtime.Add);
                break;
            default:
                var rightLocal = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Ldloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

                switch (csi.Operator.Type)
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

        _il.Emit(OpCodes.Dup);
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        _il.Emit(OpCodes.Ldloc, objTemp);
        _il.Emit(OpCodes.Ldloc, indexTemp);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Call, _ctx.Runtime.SetIndex);

        SetStackUnknown();
    }

    #endregion

    #region Object/Array Expressions

    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: create array directly
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

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
            _il.Emit(OpCodes.Ldtoken, _ctx!.Runtime!.RuntimeType);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread or computed key
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);

        if (!hasSpreads && !hasComputedKeys)
        {
            // Simple case: no spreads, no computed keys
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys
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
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    EmitExpression(ck.Expression);
                    EnsureBoxed();
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
                }
                else
                {
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a static property key (identifier, string literal, or number literal) as a string.
    /// </summary>
    private void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                _il.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                _il.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                _il.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new Exception($"Internal Error: Unexpected static property key type: {key.GetType().Name}");
        }
    }

    protected override void EmitNew(Expr.New n)
    {
        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (n.NamespacePath != null && n.NamespacePath.Count > 0)
        {
            string nsPath = string.Join("_", n.NamespacePath.Select(t => t.Lexeme));
            resolvedClassName = $"{nsPath}_{n.ClassName.Lexeme}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(n.ClassName.Lexeme);
        }

        if (_ctx!.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(resolvedClassName, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(resolvedClassName, out var _) == true)
            {
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();
                targetType = typeBuilder.MakeGenericType(typeArgs);
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            int expectedParamCount = ctorBuilder.GetParameters().Length;

            // Emit all arguments first and store to temps
            List<LocalBuilder> argTemps = [];
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

            _il.Emit(OpCodes.Newobj, targetCtor);
            SetStackUnknown();
        }
        else
        {
            // Fallback: push null
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
    }

    private Type ResolveTypeArg(string typeArg)
    {
        return typeArg switch
        {
            "number" => typeof(object),
            "string" => typeof(object),
            "boolean" => typeof(object),
            "any" => typeof(object),
            _ => typeof(object)
        };
    }

    protected override void EmitThis()
    {
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }
        SetStackUnknown();
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // TemplateLiteral has Strings (literal parts) and Expressions (interpolated parts)
        // Structure: strings[0] + expressions[0] + strings[1] + expressions[1] + ... + strings[n]
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackString();
            return;
        }

        // Start with first string
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        // Interleave expressions and remaining strings
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Stringify);
            _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);

            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
            }
        }
        SetStackString();
    }

    protected override void EmitArrowFunction(Expr.ArrowFunction af)
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
            SetStackUnknown();
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            EmitNonCapturingArrowFunction(method);
        }
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af)
    {
        // For now, fallback to null for async arrow functions in async generators
        // Full implementation would need AsyncArrowBuilder support
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Newobj, displayCtor);

        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            _il.Emit(OpCodes.Ldtoken, method);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Populate captured fields
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup);

            var hoistedField = _builder.GetVariableField(capturedVar);
            if (hoistedField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, hoistedField);
            }
            else if (capturedVar == "this" && _builder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            }
            else if (_ctx.Locals.TryGetLocal(capturedVar, out var local))
            {
                _il.Emit(OpCodes.Ldloc, local);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Stfld, field);
        }

        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowFunction(MethodBuilder method)
    {
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    #endregion

    #region Missing Abstract Implementations

    protected override void EmitSuper(Expr.Super s)
    {
        // Super not supported in async generators - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Dynamic imports not yet implemented - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string url = _ctx?.CurrentModulePath ?? "";
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }

        // Create Dictionary<string, object> and add "url" property
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    protected override void EmitUnknownExpression(Expr expr)
    {
        // Unknown expression - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #endregion
}
