using System.IO;
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

        // 1. Emit the awaited expression (should produce Task<object> or $Promise or any value)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> - handle $Promise, Task<object>, or non-Task values
        // If it's a $Promise, extract its Task property
        // If it's already a Task<object>, use it directly
        // Otherwise, wrap in Task.FromResult (for non-promise values like numbers, strings, etc.)
        var taskLocal = _il.DeclareLocal(typeof(Task<object>));
        var isPromiseLabel = _il.DefineLabel();
        var isTaskLabel = _il.DefineLabel();
        var wrapValueLabel = _il.DefineLabel();
        var haveTaskLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.TSPromiseType);
        _il.Emit(OpCodes.Brtrue, isPromiseLabel);

        // Not a $Promise - check if it's a Task<object>
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, typeof(Task<object>));
        _il.Emit(OpCodes.Brtrue, isTaskLabel);

        // Not a Promise or Task - wrap in Task.FromResult
        _il.MarkLabel(wrapValueLabel);
        _il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a Task<object> - use directly
        _il.MarkLabel(isTaskLabel);
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a $Promise - extract its Task property
        _il.MarkLabel(isPromiseLabel);
        _il.Emit(OpCodes.Castclass, _ctx.Runtime.TSPromiseType);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.TSPromiseTaskGetter);
        _il.Emit(OpCodes.Stloc, taskLocal);

        _il.MarkLabel(haveTaskLabel);

        // 2b. Store the task in AwaitedTaskField (needed for continuation if not completed)
        // Stack: []
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

        // Use consolidated binary operator helper
        if (!_helpers.TryEmitBinaryOperator(b.Operator.Type, _ctx!.Runtime!.Add, _ctx!.Runtime!.Equals))
        {
            // Unsupported operator - return null
            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
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
            if (_ctx.ClassRegistry!.TryGetStaticField(resolvedClassName, g.Name.Lexeme, out var staticField))
            {
                _il.Emit(OpCodes.Ldsfld, staticField!);
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

    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (gp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async generator state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.GetStaticPrivateField);
            SetStackUnknown();
            return;
        }

        // Instance private field access - use runtime helper
        // Stack: GetPrivateField(instance, declaringClass, fieldName)
        EmitExpression(gp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetPrivateField);
        SetStackUnknown();
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is static private field access (ClassName.#field)
        if (sp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async generator state machines, we can't directly access private static fields
            // from another class, so always use the runtime helper for static private fields
            EmitExpression(sp.Value);
            EnsureBoxed();
            var valueTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, valueTemp);

            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Ldloc, valueTemp);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.SetStaticPrivateField);

            // Leave value on stack for expression result
            _il.Emit(OpCodes.Ldloc, valueTemp);
            SetStackUnknown();
            return;
        }

        // Instance private field assignment - use runtime helper
        EmitExpression(sp.Value);
        EnsureBoxed();
        var valueLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueLocal);

        // SetPrivateField(instance, declaringClass, fieldName, value)
        EmitExpression(sp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, fieldName);
        _il.Emit(OpCodes.Ldloc, valueLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetPrivateField);

        // Leave value on stack for expression result
        _il.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        // Check if this is static private method call (ClassName.#method())
        if (cp.Object is Expr.Variable classVar &&
            _ctx?.CurrentClassName != null &&
            classVar.Name.Lexeme == _ctx.CurrentClassName.Split('.').Last()?.Split('_').Last())
        {
            // In async generator state machines, we can't directly call private static methods
            // from another class, so always use the runtime helper for static private methods
            EmitDeclaringClassType();
            _il.Emit(OpCodes.Ldstr, methodName);
            EmitArgumentArray(cp.Arguments);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.CallStaticPrivateMethod);
            SetStackUnknown();
            return;
        }

        // Instance private method call - use runtime helper
        // Emit arguments first (may contain await which clears stack)
        var argTemps = new List<LocalBuilder>();
        foreach (var arg in cp.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // CallPrivateMethod(instance, declaringClass, methodName, args[])
        EmitExpression(cp.Object);
        EnsureBoxed();
        EmitDeclaringClassType();
        _il.Emit(OpCodes.Ldstr, methodName);

        // Build args array from temps
        _il.Emit(OpCodes.Ldc_I4, argTemps.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CallPrivateMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits the typeof() for the declaring class containing the private member.
    /// </summary>
    private void EmitDeclaringClassType()
    {
        if (_ctx?.CurrentClassBuilder != null)
        {
            _il.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        }
        else
        {
            // Should not happen if called correctly - throw at runtime
            _il.Emit(OpCodes.Ldstr, "Cannot access private members outside of class context");
            _il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            _il.Emit(OpCodes.Throw);
        }
    }

    /// <summary>
    /// Emits an object[] array from argument expressions.
    /// </summary>
    private void EmitArgumentArray(List<Expr> arguments)
    {
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

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        string name = la.Name.Lexeme;
        var endLabel = _il.DefineLabel();

        EmitVariable(new Expr.Variable(la.Name));
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        switch (la.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, endLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, endLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and keep current
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, endLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        EmitExpression(la.Value);
        EnsureBoxed();
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

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSet(Expr.LogicalSet ls)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(ls.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        _il.Emit(OpCodes.Dup);

        switch (ls.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        EmitExpression(ls.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetProperty);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    protected override void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi)
    {
        var skipLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(lsi.Object);
        EnsureBoxed();
        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        EmitExpression(lsi.Index);
        EnsureBoxed();
        var indexLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        _il.Emit(OpCodes.Dup);

        switch (lsi.Operator.Type)
        {
            case TokenType.AND_AND_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
                _il.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, assignLabel);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
                _il.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Br, skipLabel);
                _il.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                _il.Emit(OpCodes.Pop);
                break;
        }

        _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        EmitExpression(lsi.Value);
        EnsureBoxed();
        var resultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipLabel);
        _il.MarkLabel(endLabel);
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

    /// <summary>
    /// Extracts a qualified class name from a callee expression.
    /// </summary>
    private static (List<string> namespaceParts, string className) ExtractQualifiedNameFromCallee(Expr callee)
    {
        List<string> parts = [];
        CollectGetChainParts(callee, parts);

        if (parts.Count == 0)
            return ([], "");

        var namespaceParts = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : [];
        var className = parts[^1];
        return (namespaceParts, className);
    }

    private static void CollectGetChainParts(Expr expr, List<string> parts)
    {
        switch (expr)
        {
            case Expr.Variable v:
                parts.Add(v.Name.Lexeme);
                break;
            case Expr.Get g:
                CollectGetChainParts(g.Object, parts);
                parts.Add(g.Name.Lexeme);
                break;
        }
    }

    protected override void EmitNew(Expr.New n)
    {
        // Extract qualified name from callee expression
        var (namespaceParts, className) = ExtractQualifiedNameFromCallee(n.Callee);

        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (namespaceParts.Count > 0)
        {
            string nsPath = string.Join("_", namespaceParts);
            resolvedClassName = $"{nsPath}_{className}";
        }
        else
        {
            resolvedClassName = _ctx!.ResolveClassName(className);
        }

        var ctorBuilder = _ctx!.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassRegistry!.GetGenericParams(resolvedClassName) != null)
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

        // Two-phase emission for async generators:
        // Phase 1: Evaluate all expressions to temps first (awaits happen here)
        // This ensures proper stack management when expressions contain await
        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            EmitExpression(tl.Expressions[i]);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            exprTemps.Add(temp);
        }

        // Phase 2: Build string from temps (no awaits, stack safe)
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        for (int i = 0; i < exprTemps.Count; i++)
        {
            _il.Emit(OpCodes.Ldloc, exprTemps[i]);
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

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // Two-phase emission for async generators:
        // Phase 1: Evaluate tag and all expressions to temps first (awaits happen here)
        EmitExpression(ttl.Tag);
        EnsureBoxed();
        var tagTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, tagTemp);

        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            EmitExpression(ttl.Expressions[i]);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            exprTemps.Add(temp);
        }

        // Phase 2: Build arrays and call from temps (no awaits, stack safe)
        _il.Emit(OpCodes.Ldloc, tagTemp);

        _il.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
                _il.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            else
                _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Ldc_I4, exprTemps.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < exprTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, exprTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
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
        string path = _ctx?.CurrentModulePath ?? "";
        string url = path;
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }
        string dirname = string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path) ?? "";

        // Create Dictionary<string, object> and add properties
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

        // Add "url" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Add "filename" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "filename");
        _il.Emit(OpCodes.Ldstr, path);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Add "dirname" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "dirname");
        _il.Emit(OpCodes.Ldstr, dirname);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    #endregion
}
