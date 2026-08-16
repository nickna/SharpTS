using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Shared operator emission (LogicalSet, LogicalSetIndex, CompoundSet, CompoundSetIndex,
/// DynamicImport, ImportMeta) previously duplicated across all 4 state machine emitters.
/// ILEmitter keeps its own overrides since it uses ValidatedILBuilder and EmitBoxIfNeeded.
/// </summary>
public abstract partial class ExpressionEmitterBase
{
    /// <summary>
    /// Emits the logical condition check for &&=, ||=, ??= operators.
    /// After this, the stack has the current value and control flows to either
    /// skipLabel (keep current) or falls through (assign new value).
    /// </summary>
    private void EmitLogicalConditionCheck(TokenType operatorType, Label skipLabel)
    {
        switch (operatorType)
        {
            case TokenType.AND_AND_EQUAL:
                IL.Emit(OpCodes.Call, Ctx.Runtime!.IsTruthy);
                IL.Emit(OpCodes.Brfalse, skipLabel);
                break;
            case TokenType.OR_OR_EQUAL:
                IL.Emit(OpCodes.Call, Ctx.Runtime!.IsTruthy);
                IL.Emit(OpCodes.Brtrue, skipLabel);
                break;
            case TokenType.QUESTION_QUESTION_EQUAL:
                var assignLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Brfalse, assignLabel);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
                IL.Emit(OpCodes.Brtrue, assignLabel);
                // Not nullish - pop extra value and skip assignment
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Br, skipLabel);
                IL.MarkLabel(assignLabel);
                // At assignLabel we have [value, value], pop one to match other cases
                IL.Emit(OpCodes.Pop);
                break;
        }
    }

    /// <summary>
    /// Emits a compound operation (+= -= *= /= %=, etc.) given left and right values on stack.
    /// Delegates to StateMachineEmitHelpers.EmitCompoundOperation.
    /// </summary>
    protected void EmitCompoundOperation(TokenType opType)
    {
        _helpers.EmitCompoundOperation(opType, Ctx.Runtime!.Add);
    }

    /// <summary>
    /// Stores the value on top of the stack into the named variable.
    /// Checks hoisted fields, locals, CapturedTopLevelVars, and TopLevelStaticVars.
    /// AsyncArrowMoveNextEmitter overrides this with its richer variable resolution
    /// (parameters, captured fields, transitive captures).
    /// </summary>
    /// <remarks>
    /// Expects the value to store on top of the evaluation stack.
    /// Consumes the value from the stack.
    /// </remarks>
    protected virtual void EmitStoreVariable(string name)
    {
        // Per-iteration loop-binding cell (#650): write through the StrongBox. This base
        // helper (used by state-machine increments/compound/logical assignment) hand-rolls
        // the store instead of routing through the resolver, so the cell case is handled here.
        if (Ctx.CellBindingLocals.TryGetValue(name, out var cell))
        {
            var cellTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, cellTemp);
            IL.Emit(OpCodes.Ldloc, cell);
            IL.Emit(OpCodes.Ldloc, cellTemp);
            IL.Emit(OpCodes.Stfld, Ctx.Types.StrongBoxOfObjectValueField);
            return;
        }

        var field = GetHoistedVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store via state machine field (this.field = value)
            var temp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);  // Load state machine 'this'
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, field);
        }
        else if (Ctx.Locals.TryGetLocal(name, out var local))
        {
            IL.Emit(OpCodes.Stloc, local);
        }
        else if (Ctx.CapturedTopLevelVars?.Contains(name) == true &&
                 Ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryPointField) == true &&
                 Ctx.EntryPointDisplayClassStaticField != null)
        {
            var temp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, entryPointField);
        }
        else if (Ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            IL.Emit(OpCodes.Stsfld, topLevelField);
        }
    }

    /// <summary>
    /// variable += value / variable -= value / etc.
    /// Emits value first (safe for await/yield), loads current, applies op, stores result.
    /// </summary>
    protected virtual void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        // Emit value first — it may contain await/yield which clears the stack
        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, valueTemp);

        // Load current variable value
        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();

        // Load the RHS value back
        IL.Emit(OpCodes.Ldloc, valueTemp);

        // Apply compound operation (stack: [current, value] → [result])
        EmitCompoundOperation(ca.Operator.Type);

        // Dup result for expression value, then store
        IL.Emit(OpCodes.Dup);
        EmitStoreVariable(name);

        SetStackUnknown();
    }

    /// <summary>
    /// variable &&= value / variable ||= value / variable ??= value
    /// </summary>
    protected virtual void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        string name = la.Name.Lexeme;
        var endLabel = IL.DefineLabel();

        // Load current value
        EmitVariable(new Expr.Variable(la.Name));
        EnsureBoxed();
        IL.Emit(OpCodes.Dup);

        // Check condition — may skip to endLabel (keeping current value on stack)
        EmitLogicalConditionCheck(la.Operator.Type, endLabel);

        // Condition says: assign new value
        IL.Emit(OpCodes.Pop); // Pop current value

        EmitExpression(la.Value);
        EnsureBoxed();
        IL.Emit(OpCodes.Dup);
        EmitStoreVariable(name);

        IL.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// ++variable / --variable (returns new value)
    /// </summary>
    protected virtual void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load current value, convert to double, add delta, box.
            // ConvertToNumber implements ECMA-262 ToNumber (undefined→NaN);
            // Convert.ToDouble throws InvalidCastException on $Undefined (#190).
            EmitVariable(v);
            EnsureBoxed();
            IL.Emit(OpCodes.Call, Ctx.Runtime?.ConvertToNumber ?? Types.ConvertToDoubleFromObject);
            IL.Emit(OpCodes.Ldc_R8, delta);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Box, typeof(double));

            // Dup new value (expression result), then store
            IL.Emit(OpCodes.Dup);
            EmitStoreVariable(name);
            SetStackUnknown();
            return;
        }

        // ++obj.prop — read via GetProperty, write via SetProperty. The synchronous ILEmitter
        // overrides handle Get/GetIndex operands; without this arm the base (used by every
        // state-machine MoveNext) emitted nothing and the surrounding statement underflowed the
        // stack (#357).
        if (pi.Operand is Expr.Get get)
        {
            // Class.field++ on a static data field needs the own/inherited shadow handling so the
            // write lands on the storage the static-typed read consults (#339) — the generic
            // GetProperty/SetProperty path below would desync with the Ldsfld read.
            if (TryEmitStaticFieldIncrement(get, isPrefix: true, pi.Operator.Type))
                return;

            var objLocal = SpillBoxed(get.Object);
            EmitMemberAccessIncrement(
                isPrefix: true, pi.Operator.Type, objLocal,
                emitKey: () => IL.Emit(OpCodes.Ldstr, get.Name.Lexeme),
                Ctx.Runtime!.GetProperty, Ctx.Runtime!.SetProperty);
            return;
        }

        // ++arr[i] — read via GetIndex, write via SetIndex.
        if (pi.Operand is Expr.GetIndex gi)
        {
            var objLocal = SpillBoxed(gi.Object);
            var indexLocal = SpillBoxed(gi.Index);
            EmitMemberAccessIncrement(
                isPrefix: true, pi.Operator.Type, objLocal,
                emitKey: () => IL.Emit(OpCodes.Ldloc, indexLocal),
                Ctx.Runtime!.GetIndex, Ctx.Runtime!.SetIndex);
            return;
        }

        // Unknown operand — keep the stack state defined for the verifier.
        SetStackUnknown();
    }

    /// <summary>
    /// variable++ / variable-- (returns original value)
    /// </summary>
    protected virtual void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load current value (this is the expression result — original value)
            EmitVariable(v);
            EnsureBoxed();
            IL.Emit(OpCodes.Dup);  // Dup original for expression result

            // Convert to double, add delta, box (ToNumber semantics — see EmitPrefixIncrement)
            IL.Emit(OpCodes.Call, Ctx.Runtime?.ConvertToNumber ?? Types.ConvertToDoubleFromObject);
            IL.Emit(OpCodes.Ldc_R8, delta);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Box, typeof(double));

            // Store incremented value (original remains on stack)
            EmitStoreVariable(name);
            SetStackUnknown();
            return;
        }

        // obj.prop++ — read via GetProperty, write via SetProperty (mirrors ++obj.prop; see #357).
        if (poi.Operand is Expr.Get get)
        {
            // Class.field++ on a static data field — own/inherited shadow handling (#339); see prefix.
            if (TryEmitStaticFieldIncrement(get, isPrefix: false, poi.Operator.Type))
                return;

            var objLocal = SpillBoxed(get.Object);
            EmitMemberAccessIncrement(
                isPrefix: false, poi.Operator.Type, objLocal,
                emitKey: () => IL.Emit(OpCodes.Ldstr, get.Name.Lexeme),
                Ctx.Runtime!.GetProperty, Ctx.Runtime!.SetProperty);
            return;
        }

        // arr[i]++ — read via GetIndex, write via SetIndex.
        if (poi.Operand is Expr.GetIndex gi)
        {
            var objLocal = SpillBoxed(gi.Object);
            var indexLocal = SpillBoxed(gi.Index);
            EmitMemberAccessIncrement(
                isPrefix: false, poi.Operator.Type, objLocal,
                emitKey: () => IL.Emit(OpCodes.Ldloc, indexLocal),
                Ctx.Runtime!.GetIndex, Ctx.Runtime!.SetIndex);
            return;
        }

        // Unknown operand — keep the stack state defined for the verifier.
        SetStackUnknown();
    }

    /// <summary>
    /// Emits <c>++</c>/<c>--</c> for a member-access operand (<c>obj.prop</c> or <c>arr[i]</c>) in
    /// a state-machine body. The receiver (and index) must already be spilled into <paramref name="objLocal"/>
    /// (and captured by <paramref name="emitKey"/>) via <see cref="SpillBoxed"/>, so any await/yield inside
    /// them has already suspended with an empty stack and is evaluated exactly once; from here the
    /// read → ToNumber → write sequence is straight-line with no suspension point, so plain locals suffice.
    /// <paramref name="emitKey"/> pushes the property name (Ldstr) or index local (Ldloc) and is invoked
    /// once for the read and once for the write. <paramref name="isPrefix"/> selects the result: prefix
    /// returns the new value, postfix the ToNumber-coerced original (ECMA-262 §13.4).
    /// </summary>
    private void EmitMemberAccessIncrement(
        bool isPrefix, TokenType op, LocalBuilder objLocal, Action emitKey,
        MethodBuilder getMethod, MethodBuilder setMethod)
    {
        double delta = op == TokenType.PLUS_PLUS ? 1.0 : -1.0;

        // Read current value and coerce to a number (undefined→NaN, never throws — #190).
        IL.Emit(OpCodes.Ldloc, objLocal);
        emitKey();
        IL.Emit(OpCodes.Call, getMethod);
        IL.Emit(OpCodes.Call, Ctx.Runtime?.ConvertToNumber ?? Types.ConvertToDoubleFromObject);

        var newValue = IL.DeclareLocal(typeof(object));
        var resultValue = IL.DeclareLocal(typeof(object));
        EmitIncrementComputeStoreResult(isPrefix, delta, newValue, resultValue);

        // Write the incremented value back to the same member.
        IL.Emit(OpCodes.Ldloc, objLocal);
        emitKey();
        IL.Emit(OpCodes.Ldloc, newValue);
        IL.Emit(OpCodes.Call, setMethod);

        IL.Emit(OpCodes.Ldloc, resultValue);
        SetStackUnknown();
    }

    /// <summary>
    /// Given the current numeric value on the stack as an unboxed <c>double</c>, computes the
    /// incremented value and stores two boxed locals: <paramref name="newValue"/> (to write back)
    /// and <paramref name="resultValue"/> (what the expression evaluates to — the new value for
    /// prefix, the ToNumber-coerced original for postfix). Consumes the stack value.
    /// </summary>
    private void EmitIncrementComputeStoreResult(
        bool isPrefix, double delta, LocalBuilder newValue, LocalBuilder resultValue)
    {
        if (isPrefix)
        {
            // Stack: [current:double] → compute new, return it.
            IL.Emit(OpCodes.Ldc_R8, delta);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Box, typeof(double));
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, newValue);
            IL.Emit(OpCodes.Stloc, resultValue);
        }
        else
        {
            // Stack: [current:double] → stash the coerced original as the result, then compute new.
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Box, typeof(double));
            IL.Emit(OpCodes.Stloc, resultValue);
            IL.Emit(OpCodes.Ldc_R8, delta);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Box, typeof(double));
            IL.Emit(OpCodes.Stloc, newValue);
        }
    }

    /// <summary>
    /// Emits <c>++</c>/<c>--</c> on a class's static data field accessed as <c>Class.field</c>.
    /// Returns false (emitting nothing) when <paramref name="get"/> is not a known class's static
    /// data field, so callers fall through to the generic property path. Mirrors the own/inherited
    /// shadow handling of <see cref="EmitCompoundSet"/> (#339): an own field is read and written
    /// directly; an inherited field is read shadow-or-base and the result written as a per-subclass
    /// own shadow via SetProperty's Type arm (→ PDS). Binding here keeps the read and write on the
    /// same storage as the static-typed <c>Ldsfld</c> read, which the generic dynamic path desyncs with.
    /// </summary>
    private bool TryEmitStaticFieldIncrement(Expr.Get get, bool isPrefix, TokenType op)
    {
        if (get.Object is not Expr.Variable classVar)
            return false;
        string resolvedClassName = Ctx.ResolveClassName(classVar.Name.Lexeme);
        if (!Ctx.Classes.TryGetValue(resolvedClassName, out var classBuilder))
            return false;

        double delta = op == TokenType.PLUS_PLUS ? 1.0 : -1.0;
        var newValue = IL.DeclareLocal(typeof(object));
        var resultValue = IL.DeclareLocal(typeof(object));

        // Own static data field — read+write the field directly (write side is own-only).
        if (Ctx.ClassRegistry!.TryGetOwnCallableStaticField(resolvedClassName, get.Name.Lexeme, classBuilder, out var ownField))
        {
            IL.Emit(OpCodes.Ldsfld, ownField!);
            IL.Emit(OpCodes.Call, Ctx.Runtime?.ConvertToNumber ?? Types.ConvertToDoubleFromObject);
            EmitIncrementComputeStoreResult(isPrefix, delta, newValue, resultValue);

            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Stsfld, ownField!);

            IL.Emit(OpCodes.Ldloc, resultValue);
            SetStackUnknown();
            return true;
        }

        // Inherited static data field — read shadow-or-base, write a per-subclass own shadow (→ PDS).
        if (Ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, get.Name.Lexeme, classBuilder, out var inheritedField))
        {
            EmitStaticFieldLoadWithShadow(resolvedClassName, classBuilder, get.Name.Lexeme, inheritedField!);
            IL.Emit(OpCodes.Call, Ctx.Runtime?.ConvertToNumber ?? Types.ConvertToDoubleFromObject);
            EmitIncrementComputeStoreResult(isPrefix, delta, newValue, resultValue);

            IL.Emit(OpCodes.Ldtoken, classBuilder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, newValue);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.SetProperty);

            IL.Emit(OpCodes.Ldloc, resultValue);
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// obj.prop &&= value / obj.prop ||= value / obj.prop ??= value
    /// </summary>
    protected virtual void EmitLogicalSet(Expr.LogicalSet ls)
    {
        var skipLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Store object (registered so an await inside ls.Value persists it — #400)
        var objLocal = SpillBoxed(ls.Object);

        // Logical assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before the short-circuit check (#733).
        if (!IsNullPlaceholderGlobal(ls.Object))
            EmitThrowIfReceiverUndefined(objLocal, ls.Name.Lexeme, isWrite: false);

        // Get current value
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
        IL.Emit(OpCodes.Dup);

        EmitLogicalConditionCheck(ls.Operator.Type, skipLabel);

        IL.Emit(OpCodes.Pop);
        // Spill the value so an await inside it suspends with an empty stack.
        var resultLocal = SpillBoxed(ls.Value);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, ls.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetProperty);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(skipLabel);
        IL.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// obj[index] &&= value / obj[index] ||= value / obj[index] ??= value
    /// </summary>
    protected virtual void EmitLogicalSetIndex(Expr.LogicalSetIndex lsi)
    {
        var skipLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Store object and index (registered so an await inside lsi.Value persists them — #400)
        var objLocal = SpillBoxed(lsi.Object);
        var indexLocal = SpillBoxed(lsi.Index);

        // Logical assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before the short-circuit check (#733).
        if (!IsNullPlaceholderGlobal(lsi.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocal, indexLocal, isWrite: false);

        // Get current value
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetIndex);
        IL.Emit(OpCodes.Dup);

        EmitLogicalConditionCheck(lsi.Operator.Type, skipLabel);

        IL.Emit(OpCodes.Pop);
        // Spill the value so an await inside it suspends with an empty stack.
        var resultLocal = SpillBoxed(lsi.Value);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetIndex);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(skipLabel);
        IL.MarkLabel(endLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// obj.prop += value (compound assignment on property)
    /// </summary>
    protected virtual void EmitCompoundSet(Expr.CompoundSet cs)
    {
        // Handle static field compound assignment: Class.field += x
        if (cs.Object is Expr.Variable classVar &&
            Ctx.Classes.TryGetValue(Ctx.ResolveClassName(classVar.Name.Lexeme), out var compoundClassBuilder))
        {
            string resolvedClassName = Ctx.ResolveClassName(classVar.Name.Lexeme);
            // Compound assignment binds the data field own-only (write side); see TryGetOwnCallableStaticField.
            if (Ctx.ClassRegistry!.TryGetOwnCallableStaticField(resolvedClassName, cs.Name.Lexeme, compoundClassBuilder, out var staticField))
            {
                EmitExpression(cs.Value);
                EnsureBoxed();
                var valueTemp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, valueTemp);

                IL.Emit(OpCodes.Ldsfld, staticField!);
                IL.Emit(OpCodes.Ldloc, valueTemp);
                EmitCompoundOperation(cs.Operator.Type);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }

            // Inherited static data field: read the current value (shadow-or-base), compute, then
            // write the result as a per-subclass own shadow via SetProperty's Type arm (→ PDS) rather
            // than mutating the base field. Matches JS own-shadow semantics and the plain-write path (#339).
            if (Ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, cs.Name.Lexeme, compoundClassBuilder, out var inheritedField))
            {
                EmitStaticFieldLoadWithShadow(resolvedClassName, compoundClassBuilder, cs.Name.Lexeme, inheritedField!);
                // Registered so an await inside cs.Value persists it across the suspension (#400).
                var inheritedCurrentTemp = _helpers.SpillStoreObject();

                // Spill the RHS so an await inside it suspends with an empty stack.
                var inheritedRhsTemp = SpillBoxed(cs.Value);

                IL.Emit(OpCodes.Ldloc, inheritedCurrentTemp);
                IL.Emit(OpCodes.Ldloc, inheritedRhsTemp);
                EmitCompoundOperation(cs.Operator.Type);
                var inheritedResultTemp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, inheritedResultTemp);

                IL.Emit(OpCodes.Ldtoken, compoundClassBuilder);
                IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
                IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
                IL.Emit(OpCodes.Ldloc, inheritedResultTemp);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.SetProperty);

                IL.Emit(OpCodes.Ldloc, inheritedResultTemp);
                SetStackUnknown();
                return;
            }
        }

        // Dynamic property compound assignment. objTemp and currentTemp are registered so
        // an await inside cs.Value persists them across the suspension (#400).
        var objTemp = SpillBoxed(cs.Object);

        // Compound assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before GetProperty (which would otherwise no-op) (#733).
        if (!IsNullPlaceholderGlobal(cs.Object))
            EmitThrowIfReceiverUndefined(objTemp, cs.Name.Lexeme, isWrite: false);

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
        var currentTemp = _helpers.SpillStoreObject();

        // Spill the value so an await inside it suspends with an empty stack
        // (the current property value must not stay on the stack across it).
        var rhsTemp = SpillBoxed(cs.Value);

        IL.Emit(OpCodes.Ldloc, currentTemp);
        IL.Emit(OpCodes.Ldloc, rhsTemp);
        EmitCompoundOperation(cs.Operator.Type);

        var resultLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, resultLocal);

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Ldstr, cs.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetProperty);

        IL.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// obj[index] += value (compound assignment on index)
    /// </summary>
    protected virtual void EmitCompoundSetIndex(Expr.CompoundSetIndex csi)
    {
        // objTemp, indexTemp and currentTemp are registered so an await inside csi.Value
        // persists them across the suspension (#400).
        var objTemp = SpillBoxed(csi.Object);
        var indexTemp = SpillBoxed(csi.Index);

        // Compound assignment reads first → a nullish base throws the *read*-worded
        // guest TypeError before GetIndex (which would otherwise no-op) (#733).
        if (!IsNullPlaceholderGlobal(csi.Object))
            EmitThrowIfUndefinedIndexReceiver(objTemp, indexTemp, isWrite: false);

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Ldloc, indexTemp);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetIndex);
        var currentTemp = _helpers.SpillStoreObject();

        // Spill the value so an await inside it suspends with an empty stack
        // (the current element value must not stay on the stack across it).
        var valueTemp = SpillBoxed(csi.Value);

        IL.Emit(OpCodes.Ldloc, currentTemp);
        IL.Emit(OpCodes.Ldloc, valueTemp);
        EmitCompoundOperation(csi.Operator.Type);

        var resultLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, resultLocal);

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Ldloc, indexTemp);
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetIndex);

        IL.Emit(OpCodes.Ldloc, resultLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// import('path') - dynamic import expression.
    /// Generator emitters override this to return null (dynamic import not supported in generators).
    /// </summary>
    protected virtual void EmitDynamicImport(Expr.DynamicImport di)
    {
        EmitExpression(di.PathExpression);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Types.ConvertToStringFromObject);
        IL.Emit(OpCodes.Ldstr, Ctx.CurrentModulePath ?? "");
        IL.Emit(OpCodes.Call, Ctx.Runtime!.DynamicImportModule);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.WrapTaskAsPromise);
        SetStackUnknown();
    }

    /// <summary>
    /// import.meta - returns object with url, filename, dirname properties.
    /// </summary>
    protected virtual void EmitImportMeta(Expr.ImportMeta im)
    {
        string path = Ctx.CurrentModulePath ?? "";
        string url = path;
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }
        string dirname = string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetDirectoryName(path) ?? "";

        IL.Emit(OpCodes.Newobj, Types.DictionaryStringObjectCtor);

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "url");
        IL.Emit(OpCodes.Ldstr, url);
        IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "filename");
        IL.Emit(OpCodes.Ldstr, path);
        IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "dirname");
        IL.Emit(OpCodes.Ldstr, dirname);
        IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateObject);
        SetStackUnknown();
    }
}
