using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
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
        var completionValue = _il.DeclareLocal(typeof(object));

        // === First entry path: evaluate and check type ===
        EmitExpression(y.Value!);
        EnsureBoxed();

        var iterableTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, iterableTemp);

        // Reserve the suspension state for the delegated iterator's next() await (consumed by the async
        // arm below, #688). Allocated AFTER the value expression so any awaits inside it take earlier
        // states — matching AsyncGeneratorStateAnalyzer.VisitYield, which records this synthetic await
        // point after visiting the yield value. Unused by the sync arm, but its resume label is always
        // marked (the async arm is always emitted), so the state switch stays valid either way.
        int awaitState = _currentSuspensionState++;

        // Check if it's IAsyncEnumerator<object> (async generators implement this)
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Isinst, _types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Brtrue, asyncSetupLabel);

        // Sync setup
        _il.MarkLabel(syncSetupLabel);
        // Emitted typed arrays and Buffers are synchronous iterables but do not implement
        // IEnumerable. Materialize them before the existing sync setup cast (#1289).
        NormalizeYieldStarTypedArrayOrBuffer(iterableTemp);
        // Keep a generator's public protocol so next(v) carries sent values
        // and its completed result retains the return value.
        var plainSyncSetupLabel = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.Generators.Type);
        _il.Emit(OpCodes.Brfalse, plainSyncSetupLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);
        _il.Emit(OpCodes.Br, syncLoopLabel);
        _il.MarkLabel(plainSyncSetupLabel);
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

        var plainSyncLoopLabel = _il.DefineLabel();
        var syncHaveValueLabel = _il.DefineLabel();
        var syncGeneratorDoneLabel = _il.DefineLabel();
        var syncResult = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Isinst, _ctx.Runtime.Generators.Type);
        _il.Emit(OpCodes.Brfalse, plainSyncLoopLabel);
        EmitCaptureTryOperation(() =>
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Castclass, _ctx.Runtime.Generators.Type);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SentField);
            _il.Emit(OpCodes.Callvirt, _ctx.Runtime.Generators.Next);
            _il.Emit(OpCodes.Stloc, syncResult);
        });
        _il.Emit(OpCodes.Ldloc, syncResult);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Done);
        _il.Emit(OpCodes.Brtrue, syncGeneratorDoneLabel);
        _il.Emit(OpCodes.Ldloc, syncResult);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Value);
        _il.Emit(OpCodes.Br, syncHaveValueLabel);

        _il.MarkLabel(plainSyncLoopLabel);

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
        _il.MarkLabel(syncHaveValueLabel);
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
        _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.Sentinels.UndefinedInstance);
        _il.Emit(OpCodes.Stloc, completionValue);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(syncGeneratorDoneLabel);
        _il.Emit(OpCodes.Ldloc, syncResult);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Value);
        _il.Emit(OpCodes.Stloc, completionValue);
        _il.Emit(OpCodes.Br, endLabel);

        // === Async loop ===
        // Drive the delegated async iterator via its $IAsyncGenerator.next() — a Task<object> holding the
        // { value, done } iterator result — and SUSPEND the enclosing async generator on it, rather than
        // blocking on a synchronous ValueTask GetResult (which deadlocks a genuinely-async delegate the
        // same way next() did before #631). next() maps directly onto the async-gen await mechanism; the
        // reserved `awaitState` backs that suspension. Everything that reaches this arm
        // (Isinst IAsyncEnumerator<object> succeeded) is an emitted async generator, which implements
        // $IAsyncGenerator (#688).
        var asyncLoopEnd = _il.DefineLabel();
        _il.MarkLabel(asyncLoopLabel);

        // result = await delegated.next(SentField) — forward the outer sent value to the inner generator
        // (#473). The inner generator ignores the argument on its first call (per spec), so passing
        // SentField on initial entry (which holds undefined or the outer first-next value) is safe.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, _ctx!.Runtime!.RequireAsyncGenerators().Type);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.SentField);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.RequireAsyncGenerators().Next);
        SetStackUnknown();
        EmitAwaitFromValueOnStack(awaitState);
        var asyncResultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, asyncResultLocal);

        // if (result.done) the delegation is finished.
        _il.Emit(OpCodes.Ldloc, asyncResultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Done);
        _il.Emit(OpCodes.Brtrue, asyncLoopEnd);

        // <>2__current = result.value
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, asyncResultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Value);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Re-yield the delegated value to our own consumer: suspend at the re-yield state and return true.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        _il.MarkLabel(asyncLoopEnd);
        _il.Emit(OpCodes.Ldloc, asyncResultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.IteratorProtocol.Value);
        _il.Emit(OpCodes.Stloc, completionValue);

        // === End/cleanup ===
        _il.MarkLabel(endLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        _il.Emit(OpCodes.Ldloc, completionValue);
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
        var iterableTemp = _il.DeclareLocal(typeof(object));
        EmitExpression(y.Value!);
        EnsureBoxed();
        _il.Emit(OpCodes.Stloc, iterableTemp);
        NormalizeYieldStarTypedArrayOrBuffer(iterableTemp);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
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

        // yield* evaluates to undefined — load the `$Undefined` sentinel, not CLR null (#481).
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.Sentinels.UndefinedInstance);
        SetStackUnknown();
    }

}
