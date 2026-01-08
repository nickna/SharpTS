using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    private void LoadVariable(string name)
    {
        // Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            SetStackUnknown();
            return;
        }

        // Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            SetStackUnknown();
            return;
        }

        // Check if it's captured from outer scope
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            // Load through outer reference
            // Use Unbox (not Unbox_Any) to get a pointer to the boxed struct, then load field
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture (needs extra indirection through parent's outer)
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                // First unbox to parent, then load parent's outer reference
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            _il.Emit(OpCodes.Ldfld, outerField);
            SetStackUnknown();
            return;
        }

        // Check for non-hoisted local variable
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            SetStackUnknown();
            return;
        }

        // Check if it's a global function
        if (_ctx?.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod) == true)
        {
            // Load function reference
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Fallback: null
        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

    private void StoreVariable(string name)
    {
        // Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, paramField);
            return;
        }

        // Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, localField);
            return;
        }

        // Check if it's captured from outer scope - store back to outer
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            // Store value to outer state machine's field through the boxed reference
            // Stack has: value
            // We need to: store to temp, get outer ptr, load temp, store to field
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);

            // Get pointer to the boxed outer state machine
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture (needs extra indirection through parent's outer)
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                // First unbox to parent, then load parent's outer reference
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            // Load value and store to field
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, outerField);
            return;
        }

        // Non-hoisted local variable - use IL local
        // Create or get the local
        if (!_locals.TryGetValue(name, out var local))
        {
            local = _il.DeclareLocal(typeof(object));
            _locals[name] = local;
        }
        _il.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Loads a variable value for populating a capture in a non-async arrow's display class.
    /// This is similar to LoadVariable but designed for capture population.
    /// </summary>
    private void LoadVariableForCapture(string name)
    {
        // Check if it's a parameter of this async arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            SetStackUnknown();
            return;
        }

        // Check if it's a hoisted local of this async arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            SetStackUnknown();
            return;
        }

        // Check if it's captured from outer scope (parent async function/arrow)
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);

            // Check if this is a transitive capture
            if (_builder.TransitiveCaptures.Contains(name) &&
                _builder.ParentOuterStateMachineField != null &&
                _builder.GrandparentStateMachineType != null)
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
            }
            else
            {
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            }

            _il.Emit(OpCodes.Ldfld, outerField);
            SetStackUnknown();
            return;
        }

        // Check for non-hoisted local variable
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            SetStackUnknown();
            return;
        }

        // Handle 'this' capture - in async arrows, 'this' is captured from outer scope
        if (name == "this" && _builder.IsCaptured("this") && _builder.CapturedFieldMap.TryGetValue("this", out var thisField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
            _il.Emit(OpCodes.Ldfld, thisField);
            SetStackUnknown();
            return;
        }

        // Fallback: null
        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }
}
