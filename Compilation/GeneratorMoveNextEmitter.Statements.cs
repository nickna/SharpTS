using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    #region Abstract Method Implementations from StatementEmitterBase

    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        // Check if this variable is hoisted to state machine
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store to field
            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
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

    protected override void EmitReturn(Stmt.Return r)
    {
        // Generator return - set state to completed and return false
        // Note: In generators, return value is available via iterator result's done=true
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
    }

    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        _il.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        if (t.CatchBlock != null)
        {
            _il.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);
                _il.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, exLocal);
            }
            else
            {
                _il.Emit(OpCodes.Pop);
            }

            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        if (t.FinallyBlock != null)
        {
            _il.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        _il.EndExceptionBlock();
    }

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
            // Fallback: runtime type checking for Map/Set when compile-time type isn't available
            var afterMapSetLabel = _il.DefineLabel();
            var checkSetLabel = _il.DefineLabel();
            var dictionaryType = typeof(Dictionary<object, object?>);
            var hashSetType = typeof(HashSet<object>);
            var iterableLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, iterableLocal);

            // Check if iterable is Dictionary<object, object?> (Map)
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Isinst, dictionaryType);
            _il.Emit(OpCodes.Brfalse, checkSetLabel);

            // It's a Map - call MapEntries to get List<object?>
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.MapEntries);
            _il.Emit(OpCodes.Stloc, iterableLocal);
            _il.Emit(OpCodes.Br, afterMapSetLabel);

            // Check if iterable is HashSet<object> (Set)
            _il.MarkLabel(checkSetLabel);
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Isinst, hashSetType);
            _il.Emit(OpCodes.Brfalse, afterMapSetLabel);

            // It's a Set - call SetValues to get List<object?>
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime!.SetValues);
            _il.Emit(OpCodes.Stloc, iterableLocal);

            _il.MarkLabel(afterMapSetLabel);
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

    // Note: The following methods are inherited from StatementEmitterBase:
    // - EmitStatement (dispatch)
    // - EmitIf, EmitWhile, EmitDoWhile (control flow)
    // - EmitForIn (loops with DeclareLoopVariable/EmitStoreLoopVariable overrides)
    // - EmitBlock, EmitBreak, EmitContinue, EmitLabeledStatement
    // - EmitSwitch, EmitThrow, EmitPrint
}
