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

        // 4. Return true (has value)
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // 5. Mark the resume label (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // 6. yield expression evaluates to undefined (null) when resumed
        _il.Emit(OpCodes.Ldnull);
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

        // Handle Map/Set specially - convert to List before iteration
        {
            var afterMapSetLabel = _il.DefineLabel();
            var checkSetLabel = _il.DefineLabel();
            var dictionaryType = typeof(Dictionary<object, object?>);
            var hashSetType = typeof(HashSet<object>);

            // Check if iterable is Dictionary<object, object?> (Map)
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Isinst, dictionaryType);
            _il.Emit(OpCodes.Brfalse, checkSetLabel);

            // It's a Map - call MapEntries
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.MapEntries);
            _il.Emit(OpCodes.Stloc, iterableLocal);
            _il.Emit(OpCodes.Br, afterMapSetLabel);

            // Check if iterable is HashSet<object> (Set)
            _il.MarkLabel(checkSetLabel);
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Isinst, hashSetType);
            _il.Emit(OpCodes.Brfalse, afterMapSetLabel);

            // It's a Set - call SetValues
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetValues);
            _il.Emit(OpCodes.Stloc, iterableLocal);

            _il.MarkLabel(afterMapSetLabel);
        }

        // Check for Symbol.iterator on the object (for custom iterables)
        _il.Emit(OpCodes.Ldloc, iterableLocal);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorFunction);
        _il.Emit(OpCodes.Stloc, iterFnLocal);

        // If iterFn != null, use iterator protocol
        _il.Emit(OpCodes.Ldloc, iterFnLocal);
        _il.Emit(OpCodes.Brtrue, hasIteratorLabel);

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

        // This label is where we resume from state dispatch
        _il.MarkLabel(resumeLabel);

        // Load the delegated enumerator from field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);

        // Check if there are more elements
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current value from delegated enumerator
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Callvirt, current);

        // Store in <>2__current
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return true
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Ret);

        // End of delegation
        _il.MarkLabel(loopEnd);

        // Clear the delegated enumerator field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }
}
