using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    protected override void EmitYield(Expr.Yield y)
    {
        int stateNumber = _currentYieldState++;
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

        // Mirror live spill temps to fields before returning: IL locals do not survive the
        // MoveNext re-entry, so a value spilled before this yield and used after it would be
        // lost (#400/#414). The state machine is a class, so the fields persist directly.
        _helpers.PersistLiveSpillsBeforeSuspend();

        // 4. Return true (has value)
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // 5. Mark the resume label (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // The suspension has now been consumed. Keep the state completed while the
        // resumed body runs; a later yield installs its own resume state. If evaluation
        // throws before then, subsequent next() calls must observe a closed generator.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Restore spill temps from their fields on the resumed path.
        _helpers.RehydrateLiveSpillsAfterResume();

        // 5a. An external return()/throw() on the suspended generator injects an abrupt completion
        // here, running active try/finally(/catch) (#526). With nothing pending this falls through.
        EmitResumeInjectionCheck();

        // 6. The yield expression evaluates to the value passed to next(v), which
        // next() stashed in SentField before driving MoveNext (ECMA-262 §27.5.3.3, #452).
        // A bare next()/for-of resume leaves it as the caller's sent value (undefined) — so
        // this also subsumes #443's resumed-yield case (`"x:" + (yield 1)` → `x:undefined`).
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.SentField);
        SetStackUnknown();
    }

    private void EmitYieldStar(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // yield* delegates to another iterable
        // We store the enumerator in a field so it survives across MoveNext calls

        var delegatedField = _builder.DelegatedEnumeratorField;
        if (delegatedField == null)
        {
            // Fallback: no field defined, just push null
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // State-machine expression emission evaluates multi-operand expressions through
        // SpillBoxed locals before entering a suspension point. Consequently yield* starts
        // with an empty IL evaluation stack on both the initial fall-through and the
        // state-dispatch re-entry. Keep that source-owned invariant instead of inspecting
        // PersistedAssemblyBuilder's private IL byte and stack-depth fields.

        // Mirror operand spill temps (SpillBoxed locals, e.g. the left side of
        // `"x" + (yield* g())`) to fields up front. These live in locals that the
        // per-element re-entry would wipe. Persisting before the
        // resume label means the field is valid on both the first fall-through and every
        // state-dispatch re-entry, where it is rehydrated (#400/#414).
        _helpers.PersistLiveSpillsBeforeSuspend();

        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;

        var loopEnd = _il.DefineLabel();
        var hasIteratorLabel = _il.DefineLabel();
        var gotEnumeratorLabel = _il.DefineLabel();

        // Locals
        var iterableLocal = _il.DeclareLocal(typeof(object));
        var iterFnLocal = _il.DeclareLocal(typeof(object));
        var iteratorLocal = _il.DeclareLocal(typeof(object));
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));

        // Emit the iterable expression
        EmitExpression(y.Value!);
        EnsureBoxed();
        _il.Emit(OpCodes.Stloc, iterableLocal);

        // A delegated SharpTS generator implements $IGenerator. Store it directly in the
        // delegate field and skip the Symbol.iterator/$IteratorWrapper setup below, which
        // drives via IEnumerator.MoveNext() — a path with no slot to forward the outer's
        // resume value. The per-element loop detects $IGenerator and drives it via
        // next(sent) instead, forwarding next(v) into the delegate (ECMA-262 §14.4.14, #476).
        var generatorInterfaceType = _ctx?.Runtime?.GeneratorInterfaceType;
        if (generatorInterfaceType != null)
        {
            var notDelegatedGeneratorLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Isinst, generatorInterfaceType);
            _il.Emit(OpCodes.Brfalse, notDelegatedGeneratorLabel);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
            _il.Emit(OpCodes.Stfld, delegatedField);
            _il.Emit(OpCodes.Br, resumeLabel);
            _il.MarkLabel(notDelegatedGeneratorLabel);
        }

        // Handle Map/Set specially - convert to List before iteration. Each
        // arm checks the corresponding $Runtime method MethodBuilder for null;
        // when Map/Set emission is gated off, the dispatch arm is skipped.
        var hasMapEntries = _ctx?.Runtime?.MapEntries != null;
        var hasSetValues = _ctx?.Runtime?.SetValues != null;
        if (hasMapEntries || hasSetValues)
        {
            var afterMapSetLabel = _il.DefineLabel();
            var checkSetLabel = _il.DefineLabel();
            var dictionaryType = typeof(Dictionary<object, object?>);
            var hashSetType = typeof(HashSet<object>);

            if (hasMapEntries)
            {
                // Check if iterable is Dictionary<object, object?> (Map)
                _il.Emit(OpCodes.Ldloc, iterableLocal);
                _il.Emit(OpCodes.Isinst, dictionaryType);
                _il.Emit(OpCodes.Brfalse, checkSetLabel);

                // It's a Map - call MapEntries
                _il.Emit(OpCodes.Ldloc, iterableLocal);
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapEntries);
                _il.Emit(OpCodes.Stloc, iterableLocal);
                _il.Emit(OpCodes.Br, afterMapSetLabel);
            }

            if (hasSetValues)
            {
                // Check if iterable is HashSet<object> (Set)
                _il.MarkLabel(checkSetLabel);
                _il.Emit(OpCodes.Ldloc, iterableLocal);
                _il.Emit(OpCodes.Isinst, hashSetType);
                _il.Emit(OpCodes.Brfalse, afterMapSetLabel);

                // It's a Set - call SetValues
                _il.Emit(OpCodes.Ldloc, iterableLocal);
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetValues);
                _il.Emit(OpCodes.Stloc, iterableLocal);
            }
            else
            {
                // Map-only: still need to mark checkSetLabel so the Map arm's
                // Brfalse has a target.
                _il.MarkLabel(checkSetLabel);
            }

            _il.MarkLabel(afterMapSetLabel);
        }

        // Emitted typed arrays and Buffers do not implement IEnumerable. Normalize only
        // those operands through IterateToList before the Symbol.iterator/IEnumerable
        // dispatch so yield* remains standalone-safe (#1289).
        NormalizeYieldStarTypedArrayOrBuffer(iterableLocal);

        // Check for Symbol.iterator on the object (for custom iterables)
        _il.Emit(OpCodes.Ldloc, iterableLocal);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorFunction);
        _il.Emit(OpCodes.Stloc, iterFnLocal);

        // If @@iterator is present, use the iterator protocol. Explicit null
        // is present-but-non-callable and must not take the CLR fallback.
        _il.Emit(OpCodes.Ldloc, iterFnLocal);
        _il.Emit(OpCodes.Isinst, _ctx.Runtime.UndefinedType);
        _il.Emit(OpCodes.Brfalse, hasIteratorLabel);

        // No Symbol.iterator - fall back to IEnumerable cast
        _il.Emit(OpCodes.Ldloc, iterableLocal);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Br, gotEnumeratorLabel);

        // Has Symbol.iterator - use iterator protocol with $IteratorWrapper
        _il.MarkLabel(hasIteratorLabel);
        // Call iterator function: iterator = InvokeMethodValue(iterable, iterFn, new object[0])
        _il.Emit(OpCodes.Ldloc, iterableLocal);     // receiver (this)
        _il.Emit(OpCodes.Ldloc, iterFnLocal);       // function
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Newarr, typeof(object));   // empty args
        _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);
        _il.Emit(OpCodes.Stloc, iteratorLocal);

        // Create $IteratorWrapper: new $IteratorWrapper(iterator, runtimeType)
        _il.Emit(OpCodes.Ldloc, iteratorLocal);
        _il.Emit(OpCodes.Ldtoken, _ctx.Runtime.RuntimeType);
        _il.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime.IteratorWrapperCtor);
        _il.Emit(OpCodes.Stloc, enumTemp);

        // Store enumerator in field
        _il.MarkLabel(gotEnumeratorLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // This label is where we resume from state dispatch (and the per-element loop top).
        _il.MarkLabel(resumeLabel);

        // As with an ordinary yield, consuming the suspension makes -2 the active
        // execution state until the next delegated value is yielded.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Restore operand spill temps. Safe on the first fall-through too: they were persisted
        // before the setup above, so the field already holds the live value (#400/#414).
        _helpers.RehydrateLiveSpillsAfterResume();

        // Drive the delegate one step. Three paths converge on a single value via valueTemp:
        //   - $IGenerator (a delegated SharpTS generator): call next(this.SentField) so the
        //     outer's resume value reaches the inner's suspended yield (#476). The result is
        //     a { value, done } record.
        //   - $IteratorWrapper (custom iterator objects with [Symbol.iterator]): call
        //     MoveNextWithSent(this.SentField) to forward the sent value to next(v) (#503).
        //   - Plain IEnumerator (arrays, Maps, Sets, etc.): MoveNext()/Current with no
        //     resume slot — the sent value is irrelevant to those iterators.
        var valueTemp = _il.DeclareLocal(typeof(object));
        var genResultLocal = _il.DeclareLocal(typeof(object));
        var yieldStarResultLocal = _il.DeclareLocal(typeof(object));
        var haveValueLabel = _il.DefineLabel();
        var doneCleanupLabel = _il.DefineLabel();
        Label driveViaGeneratorLabel = default, genDoneLabel = default;
        if (generatorInterfaceType != null)
        {
            driveViaGeneratorLabel = _il.DefineLabel();
            genDoneLabel = _il.DefineLabel();
        }
        var iteratorWrapperType = _ctx?.Runtime?.IteratorWrapperType;
        var moveNextWithSent = _ctx?.Runtime?.IteratorWrapperMoveNextWithSent;
        Label plainEnumeratorLabel = default;
        if (iteratorWrapperType != null && moveNextWithSent != null)
            plainEnumeratorLabel = _il.DefineLabel();

        // #526: an external return()/throw() injected while suspended at this yield* is forwarded
        // into the delegate (ECMA-262 §14.4.14): return() runs the delegate's finally, then the outer
        // returns (or, if the delegate's finally yields, the outer yields and stays delegating);
        // throw() lets the delegate catch (then the outer continues past yield*) or, uncaught,
        // re-raises here. Emits nothing that transfers control when no injection is pending.
        EmitYieldStarResumeInjectionCheck(
            delegatedField, generatorInterfaceType, genResultLocal, valueTemp,
            yieldStarResultLocal, haveValueLabel, genDoneLabel, doneCleanupLabel);

        if (generatorInterfaceType != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Isinst, generatorInterfaceType);
            _il.Emit(OpCodes.Brtrue, driveViaGeneratorLabel);
        }

        // $IteratorWrapper path: call MoveNextWithSent(SentField) so the outer's resume value
        // is forwarded as the argument to the custom iterator's next(v) (ECMA-262 §14.4.14, #503).
        if (iteratorWrapperType != null && moveNextWithSent != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Isinst, iteratorWrapperType);
            _il.Emit(OpCodes.Brfalse, plainEnumeratorLabel);

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Castclass, iteratorWrapperType);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SentField);
            _il.Emit(OpCodes.Call, moveNextWithSent);
            _il.Emit(OpCodes.Brfalse, loopEnd);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, valueTemp);
            _il.Emit(OpCodes.Br, haveValueLabel);

            _il.MarkLabel(plainEnumeratorLabel);
        }

        // Plain IEnumerator path (arrays, Maps, Sets, etc.): advance, then read Current into valueTemp.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Callvirt, current);
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Br, haveValueLabel);

        // $IGenerator path: result = delegate.next(this.SentField); branch on result.done.
        if (generatorInterfaceType != null)
        {
            _il.MarkLabel(driveViaGeneratorLabel);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Castclass, generatorInterfaceType);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.SentField);
            _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.GeneratorNextMethod);
            _il.Emit(OpCodes.Stloc, genResultLocal);

            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
            _il.Emit(OpCodes.Brtrue, genDoneLabel);

            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
            _il.Emit(OpCodes.Stloc, valueTemp);
            // fall through to haveValue
        }

        // Yield the delegated value: store in <>2__current, set resume state, return true.
        _il.MarkLabel(haveValueLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // End of delegation — reached when the delegate reports done.
        // ECMA-262 §14.4.14: the completion value of `yield* expr` is the delegated
        // iterator's return value. For the $IGenerator path it is the done record's `value`;
        // for the IEnumerator path SharpTS-emitted generators store it in CurrentField (see
        // EmitReturn in Statements.cs), which IEnumerator.Current still reports after MoveNext
        // returned false. Non-SharpTS iterators (e.g. List<T>.Enumerator) throw on Current
        // past the end, so that read is guarded and falls back to null.
        _il.MarkLabel(loopEnd);

        // Capture the delegated iterator's Current as the yield* result. Guard against
        // non-SharpTS iterators (e.g. List<T>.Enumerator) that throw on Current after
        // MoveNext returned false: wrap in try/catch and fall back to `undefined`.
        // A non-generator iterable (array, string, Map/Set) has no return value, so per
        // ECMA-262 14.4.14 the `yield*` completion value is `undefined` — load the emitted
        // `$Undefined` sentinel, not CLR `null` (which would surface as JS `null`, #443). The
        // interpreter's DelegateYieldStar coerces its own null sentinel to `undefined` the same way.
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Stloc, yieldStarResultLocal);
        _il.BeginExceptionBlock();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Callvirt, current);
        _il.Emit(OpCodes.Stloc, yieldStarResultLocal);
        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Pop);
        _il.EndExceptionBlock();
        if (generatorInterfaceType != null)
        {
            _il.Emit(OpCodes.Br, doneCleanupLabel);

            // $IGenerator done: the completion value is the done record's `value`.
            _il.MarkLabel(genDoneLabel);
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIteratorValue);
            _il.Emit(OpCodes.Stloc, yieldStarResultLocal);
            // fall through to doneCleanup
        }

        _il.MarkLabel(doneCleanupLabel);

        // Clear the delegated enumerator field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to the delegated iterator's return value (captured above).
        _il.Emit(OpCodes.Ldloc, yieldStarResultLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// At a <c>yield*</c> resume point, forwards an external return()/throw() injected on the outer
    /// generator into the delegated iterator (ECMA-262 §14.4.14, #526). Only an <c>$IGenerator</c>
    /// delegate carries return/throw; a plain iterable (array, string, Map/Set) has neither, so the
    /// outer simply returns the value / re-raises the error after closing the delegate. Emits nothing
    /// that transfers control when no injection is pending, so the normal per-element drive runs.
    /// </summary>
    private void EmitYieldStarResumeInjectionCheck(
        FieldBuilder delegatedField,
        Type? generatorInterfaceType,
        LocalBuilder genResultLocal,
        LocalBuilder valueTemp,
        LocalBuilder yieldStarResultLocal,
        Label haveValueLabel,
        Label genDoneLabel,
        Label doneCleanupLabel)
    {
        var kindField = _builder.InjectedKindField;
        var valueField = _builder.InjectedValueField;
        if (kindField == null || valueField == null) return;

        void LoadInjectedValue()
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, valueField);
        }

        // ---- return(v) forwarding ----
        var afterReturn = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, kindField);
        _il.Emit(OpCodes.Ldc_I4, GeneratorStateMachineBuilder.InjectKindReturn);
        _il.Emit(OpCodes.Bne_Un, afterReturn);
        ClearInjectedKind();

        if (generatorInterfaceType != null)
        {
            var notGenReturn = _il.DefineLabel();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Isinst, generatorInterfaceType);
            _il.Emit(OpCodes.Brfalse, notGenReturn);

            // result = delegate.return(injectedValue) — runs the delegate's finally(s).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Castclass, generatorInterfaceType);
            LoadInjectedValue();
            _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.GeneratorReturnMethod);
            _il.Emit(OpCodes.Stloc, genResultLocal);

            // result.done → the outer returns result.value (§14.4.14 c.7); else the delegate's
            // finally yielded, so the outer yields result.value and stays delegating.
            var innerReturnNotDone = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
            _il.Emit(OpCodes.Brfalse, innerReturnNotDone);
            ClearDelegateField(delegatedField);
            EmitRoutedReturn(() =>
            {
                _il.Emit(OpCodes.Ldloc, genResultLocal);
                _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
            });

            _il.MarkLabel(innerReturnNotDone);
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
            _il.Emit(OpCodes.Stloc, valueTemp);
            _il.Emit(OpCodes.Br, haveValueLabel);

            _il.MarkLabel(notGenReturn);
        }

        // Plain iterable: no return() to forward — close it and the outer returns the value.
        ClearDelegateField(delegatedField);
        EmitRoutedReturn(LoadInjectedValue);

        _il.MarkLabel(afterReturn);

        // ---- throw(e) forwarding ----
        var afterThrow = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, kindField);
        _il.Emit(OpCodes.Ldc_I4, GeneratorStateMachineBuilder.InjectKindThrow);
        _il.Emit(OpCodes.Bne_Un, afterThrow);
        ClearInjectedKind();

        if (generatorInterfaceType != null)
        {
            var notGenThrow = _il.DefineLabel();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Isinst, generatorInterfaceType);
            _il.Emit(OpCodes.Brfalse, notGenThrow);

            // Drive delegate.throw(injectedValue) in a mini try/catch so an inner rethrow (the
            // delegate has no handler, §14.4.14 b.ii) is captured and re-raised at this point —
            // running the outer's own catch/finally — rather than escaping MoveNext directly.
            var caughtLocal = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, caughtLocal);
            _il.BeginExceptionBlock();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, delegatedField);
            _il.Emit(OpCodes.Castclass, generatorInterfaceType);
            LoadInjectedValue();
            _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.GeneratorThrowMethod);
            _il.Emit(OpCodes.Stloc, genResultLocal);
            _il.BeginCatchBlock(typeof(Exception));
            _il.Emit(OpCodes.Call, _ctx.Runtime.WrapException);
            _il.Emit(OpCodes.Stloc, caughtLocal);
            _il.EndExceptionBlock();

            // Inner rethrew → re-raise at this point (runs the outer's catch/finally).
            var innerHandled = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, caughtLocal);
            _il.Emit(OpCodes.Brfalse, innerHandled);
            ClearDelegateField(delegatedField);
            EmitRoutedThrow(() => _il.Emit(OpCodes.Ldloc, caughtLocal));

            // Inner handled it: done → yield* value as a normal completion, the outer continues past
            // yield* (§14.4.14 b.5); else the delegate yielded again, so the outer yields and stays.
            _il.MarkLabel(innerHandled);
            var innerThrowNotDone = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
            _il.Emit(OpCodes.Brfalse, innerThrowNotDone);
            _il.Emit(OpCodes.Br, genDoneLabel);

            _il.MarkLabel(innerThrowNotDone);
            _il.Emit(OpCodes.Ldloc, genResultLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
            _il.Emit(OpCodes.Stloc, valueTemp);
            _il.Emit(OpCodes.Br, haveValueLabel);

            _il.MarkLabel(notGenThrow);
        }

        // Plain iterable: no throw() to forward — close it and re-raise the error at this point.
        ClearDelegateField(delegatedField);
        EmitRoutedThrow(LoadInjectedValue);

        _il.MarkLabel(afterThrow);
    }

    /// <summary>Clears the delegated-enumerator field once a <c>yield*</c> delegation has ended.</summary>
    private void ClearDelegateField(FieldBuilder delegatedField)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);
    }

}
