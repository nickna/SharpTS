using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    #region Abstract Method Implementations from StatementEmitterBase

    protected override FieldBuilder? GetHoistedVariableField(string name) => _builder.GetVariableField(name);

    protected override void EmitReturn(Stmt.Return r)
    {
        // Evaluate the return value for its side effects — the expression may contain
        // `yield` or `yield*` sub-expressions whose state labels are registered by the
        // analyzer at the top of MoveNext. Skipping evaluation (the prior behaviour)
        // left those labels defined but never marked, which makes the metadata writer
        // fail with "Label N has not been marked" (surfaced via yaml's lexer.parseNext,
        // where every `case '…': return yield* this.parseFoo()` contributed one
        // orphaned state label).
        //
        // Per ECMA-262 27.3 / 14.5, the completed value of a generator is the return
        // expression's result; we store it in the Current field so callers can read
        // it via IEnumerator.Current after MoveNext returns false.
        if (r.Value != null)
        {
            EmitExpression(r.Value);
            EnsureBoxed();
            var returnValueTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, returnValueTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, returnValueTemp);
            _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        }
        else
        {
            // Bare `return;` completes the generator with `undefined`. Store the `$Undefined`
            // sentinel into Current so the completion value read by `gen.next().value` after
            // done — and by a delegating `yield* thisGenerator()` — is `undefined` rather than
            // the stale last-yielded value still sitting in Current (#443).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
            _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        }

        // Generator return - set state to completed. The direct `ret` path below returns immediately;
        // a routed or deferred return re-asserts this at its terminal / landing pad, since a yielding
        // finally between here and there would overwrite it with the finally's resume state.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // A non-local `return` must run any enclosing finally(s) before the generator completes. See
        // the exit-routing machinery in GeneratorMoveNextEmitter.Statements.TryCatch.cs.
        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            // Flag-based finally(s) are open. Stash the completion value: a finally that yields
            // overwrites Current with its yielded value, and the return terminal restores it from here
            // after the finally has run (#555). From inside a real IL block the route uses `Leave`
            // (running that block's no-yield finally) to reach the innermost flag cleanup label (#554).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.CurrentField);
            _il.Emit(OpCodes.Stfld, GetPendingReturnValueField());

            RegisterReturnTerminal();
            RouteThroughFinallys(chain, ExitCodeReturn, _protectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
            return;
        }

        if (_protectedRegionDepth > 0)
        {
            // Inside a real IL try/finally with no enclosing flag finally: a bare `ret` here is illegal
            // (it leaves a protected region). Current/state are set above; `Leave` the deferred-return
            // landing pad, which runs the enclosing no-yield finally(s) and rets false (#554).
            _deferredReturnUsed = true;
            _il.Emit(OpCodes.Leave, _deferredReturnLabel);
            return;
        }

        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
    }

    // EmitTryCatch lives in GeneratorMoveNextEmitter.Statements.TryCatch.cs — it needs
    // suspension-aware (flag-based) handling when a yield crosses the protected region.

    #endregion

    #region For...Of Loop Override (Hoisted Enumerator Support)

    /// <summary>
    /// Emits a for...of loop with hoisted enumerator support.
    /// When the loop contains yield statements, the enumerator is stored in a state machine field
    /// so it persists across yield boundaries.
    /// </summary>
    protected override void EmitForOf(Stmt.ForOf f)
    {
        // Check if this loop needs a hoisted enumerator
        var enumeratorField = _builder.GetEnumeratorField(f);

        if (enumeratorField == null)
        {
            // No yield inside this loop - use base implementation with local enumerator
            base.EmitForOf(f);
            return;
        }

        // Loop contains yield - use hoisted enumerator field
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Get compile-time type info for the iterable expression
        TypeInfo? iterableType = _ctx!.TypeMap?.Get(f.Iterable);

        // Emit iterable and get enumerator
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Handle Map/Set specially - convert to List before iteration
        // This matches the behavior in ILEmitter.Statements.cs EmitForOf
        if (iterableType is TypeInfo.Map)
        {
            // Map iteration yields [key, value] entries (compile-time known)
            _il.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
        }
        else if (iterableType is TypeInfo.Set)
        {
            // Set iteration yields values (compile-time known)
            _il.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
        }
        else
        {
            // Fallback: runtime type checking for Map/Set when compile-time
            // type isn't available. Each arm gates on whether the corresponding
            // $Runtime helper was emitted (null MethodBuilder ⇒ feature off).
            var hasMapEntries = _ctx.Runtime?.MapEntries != null;
            var hasSetValues = _ctx.Runtime?.SetValues != null;
            var iterableLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, iterableLocal);

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

                    // It's a Map - call MapEntries to get List<object?>
                    _il.Emit(OpCodes.Ldloc, iterableLocal);
                    _il.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
                    _il.Emit(OpCodes.Stloc, iterableLocal);
                    _il.Emit(OpCodes.Br, afterMapSetLabel);
                }

                if (hasSetValues)
                {
                    _il.MarkLabel(checkSetLabel);
                    _il.Emit(OpCodes.Ldloc, iterableLocal);
                    _il.Emit(OpCodes.Isinst, hashSetType);
                    _il.Emit(OpCodes.Brfalse, afterMapSetLabel);

                    // It's a Set - call SetValues to get List<object?>
                    _il.Emit(OpCodes.Ldloc, iterableLocal);
                    _il.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
                    _il.Emit(OpCodes.Stloc, iterableLocal);
                }
                else
                {
                    // Map-only: still need checkSetLabel as the Map arm's Brfalse target.
                    _il.MarkLabel(checkSetLabel);
                }

                _il.MarkLabel(afterMapSetLabel);
            }
            _il.Emit(OpCodes.Ldloc, iterableLocal);
        }

        // Get the enumerator from the (possibly converted) iterable
        _il.Emit(OpCodes.Castclass, _types.IEnumerable);
        _il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerable, "GetEnumerator"));

        // Store enumerator to hoisted field (need temp local for the stack swap)
        var tempLocal = _il.DeclareLocal(_types.IEnumerator);
        _il.Emit(OpCodes.Stloc, tempLocal);
        _il.Emit(OpCodes.Ldarg_0);  // this
        _il.Emit(OpCodes.Ldloc, tempLocal);
        _il.Emit(OpCodes.Stfld, enumeratorField);

        EnterLoop(endLabel, continueLabel);

        // Declare loop variable (may be hoisted or local)
        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        _il.MarkLabel(startLabel);

        // Check MoveNext - load enumerator from hoisted field
        _il.Emit(OpCodes.Ldarg_0);  // this
        _il.Emit(OpCodes.Ldfld, enumeratorField);
        _il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from Current
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            _il.Emit(OpCodes.Ldarg_0);  // this
            _il.Emit(OpCodes.Ldfld, enumeratorField);
            _il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumerator, "Current"));
        });

        // Emit body
        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        ExitLoop();
    }

    #endregion

    #region For...In Loop Override (Hoisted Key-List/Index Support)

    /// <summary>
    /// Emits a for...in loop with hoisted key-list and index when the body contains a yield.
    /// The base emitter keeps the enumerated key list and current index in IL locals, which a
    /// MoveNext re-entry across a yield wipes — so the loop would restart from (or stop after) the
    /// first key (#547). Storing both in state-machine fields makes the iteration position survive
    /// the suspension, mirroring the hoisted-enumerator treatment <see cref="EmitForOf"/> gives
    /// for...of.
    /// </summary>
    protected override void EmitForIn(Stmt.ForIn f)
    {
        var keysField = _builder.GetForInKeysField(f);
        if (keysField == null)
        {
            // No yield inside this loop - use base implementation with local key list/index.
            base.EmitForIn(f);
            return;
        }
        var indexField = _builder.GetForInIndexField(f)!;

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Get keys from the object and stash them in the hoisted field (need a temp for the swap).
        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysTemp = _il.DeclareLocal(_types.ListOfObject);
        _il.Emit(OpCodes.Stloc, keysTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, keysTemp);
        _il.Emit(OpCodes.Stfld, keysField);

        // index = 0
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stfld, indexField);

        EnterLoop(endLabel, continueLabel);

        var loopVarLocal = DeclareLoopVariable(f.Variable.Lexeme);

        _il.MarkLabel(startLabel);
        EmitCancellationCheck();

        // if (index < keys.Count) else goto end — both read from hoisted fields.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, indexField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, keysField);
        _il.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // loopVar = keys[index]
        EmitStoreLoopVariable(loopVarLocal, f.Variable.Lexeme, () =>
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, keysField);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, indexField);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.GetElement);
        });

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        // index = index + 1 (store back to the hoisted field)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, indexField);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stfld, indexField);

        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        ExitLoop();
    }

    #endregion

    // Note: The following methods are inherited from StatementEmitterBase:
    // - EmitStatement (dispatch)
    // - EmitIf, EmitWhile, EmitDoWhile (control flow)
    // - EmitBlock, EmitLabeledStatement, EmitSwitch, EmitPrint
    //
    // EmitBreak, EmitContinue and EmitThrow are overridden in
    // GeneratorMoveNextEmitter.Statements.TryCatch.cs so a non-local exit runs any enclosing
    // flag-based finally before transferring control (#500); the loop-scope methods
    // (EnterLoop/ExitLoop/CurrentLoop/FindLabeledLoop) are overridden there for the same reason.
}
