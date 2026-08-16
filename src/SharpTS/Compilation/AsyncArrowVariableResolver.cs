using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Variable resolver for async arrow state machine emitters.
/// Handles the complex case of multi-level captures through outer state machines.
/// </summary>
/// <remarks>
/// Resolution order:
/// 1. Arrow's own parameters (hoisted to fields)
/// 2. Arrow's own hoisted locals (hoisted to fields)
/// 3. Captured from outer scope (via boxed outer state machine reference)
/// 4. Non-hoisted IL locals (for variables that don't cross await boundaries)
///
/// Does NOT handle: functions (caller handles these as fallback).
/// </remarks>
public class AsyncArrowVariableResolver : IVariableResolver
{
    private readonly ILGenerator _il;
    private readonly AsyncArrowStateMachineBuilder _builder;
    private readonly Dictionary<string, LocalBuilder> _locals;
    private readonly IReadOnlyDictionary<string, LocalBuilder>? _cellBindingLocals;
    private readonly FieldInfo? _strongBoxValueField;

    /// <summary>
    /// Creates a new resolver for async arrow variable access.
    /// </summary>
    /// <param name="il">The IL generator for emitting instructions</param>
    /// <param name="builder">The async arrow state machine builder</param>
    /// <param name="locals">Dictionary of non-hoisted local variables</param>
    /// <param name="cellBindingLocals">Per-iteration cell locals (#650), shared with the emitter; null disables cells.</param>
    /// <param name="strongBoxValueField">The StrongBox&lt;object&gt;.Value field, for cell dereference.</param>
    public AsyncArrowVariableResolver(
        ILGenerator il,
        AsyncArrowStateMachineBuilder builder,
        Dictionary<string, LocalBuilder> locals,
        IReadOnlyDictionary<string, LocalBuilder>? cellBindingLocals = null,
        FieldInfo? strongBoxValueField = null)
    {
        _il = il;
        _builder = builder;
        _locals = locals;
        _cellBindingLocals = cellBindingLocals;
        _strongBoxValueField = strongBoxValueField;
    }

    /// <inheritdoc />
    public StackType? TryLoadVariable(string name)
    {
        // 0. Per-iteration loop-binding cell (#650): read through the StrongBox.
        if (_cellBindingLocals != null && _cellBindingLocals.TryGetValue(name, out var loadCell))
        {
            _il.Emit(OpCodes.Ldloc, loadCell);
            _il.Emit(OpCodes.Ldfld, _strongBoxValueField!);
            return StackType.Unknown;
        }

        // 1. Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, paramField);
            return StackType.Unknown;
        }

        // 2. Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, localField);
            return StackType.Unknown;
        }

        // 3. Check if it's captured from outer scope
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            EmitCapturedLoad(name, outerField);
            return StackType.Unknown;
        }

        // 4. Check non-hoisted local
        if (_locals.TryGetValue(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            return StackType.Unknown;
        }

        // NOTE: standalone captures (#641) are resolved by the EMITTER (AsyncArrowMoveNextEmitter)
        // at LOWER priority than module-level globals, because a top-level variable a standalone
        // arrow closes over is ALSO registered as a standalone capture but must be read LIVE from
        // its static field, not from the by-value snapshot. Handling it here would shadow that.
        return null; // Not found - caller handles fallback
    }

    /// <inheritdoc />
    public bool HasVariable(string name)
    {
        if (_cellBindingLocals != null && _cellBindingLocals.ContainsKey(name)) return true;
        if (_builder.ParameterFields.ContainsKey(name)) return true;
        if (_builder.LocalFields.ContainsKey(name)) return true;
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.ContainsKey(name)) return true;
        if (_locals.ContainsKey(name)) return true;
        return false;
    }

    /// <inheritdoc />
    public bool TryStoreVariable(string name)
    {
        // 0. Per-iteration loop-binding cell (#650): write through the StrongBox.
        if (_cellBindingLocals != null && _cellBindingLocals.TryGetValue(name, out var storeCell))
        {
            var cellTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, cellTemp);
            _il.Emit(OpCodes.Ldloc, storeCell);
            _il.Emit(OpCodes.Ldloc, cellTemp);
            _il.Emit(OpCodes.Stfld, _strongBoxValueField!);
            return true;
        }

        // 1. Check if it's a parameter of this arrow
        if (_builder.ParameterFields.TryGetValue(name, out var paramField))
        {
            EmitStoreToField(paramField);
            return true;
        }

        // 2. Check if it's a hoisted local of this arrow
        if (_builder.LocalFields.TryGetValue(name, out var localField))
        {
            EmitStoreToField(localField);
            return true;
        }

        // 3. Check if it's captured from outer scope
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var outerField))
        {
            EmitCapturedStore(name, outerField);
            return true;
        }

        // 4. Non-hoisted local - use or create IL local
        if (!_locals.TryGetValue(name, out var local))
        {
            local = _il.DeclareLocal(typeof(object));
            _locals[name] = local;
        }
        _il.Emit(OpCodes.Stloc, local);
        return true;
    }

    /// <inheritdoc />
    public void LoadThis()
    {
        // 'this' in async arrows is captured from outer scope
        if (_builder.IsCaptured("this") && _builder.CapturedFieldMap.TryGetValue("this", out var thisField))
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            _il.Emit(OpCodes.Ldfld, thisField);
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }
    }

    private void EmitCapturedLoad(string name, FieldInfo outerField)
    {
        // Load through outer reference
        // Use Unbox (not Unbox_Any) to get a pointer to the boxed struct, then load field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);

        // Check if this is a transitive capture (needs extra indirection through parent's outer)
        if (_builder.TransitiveCaptures.Contains(name) &&
            _builder.ParentOuterStateMachineField != null &&
            _builder.GrandparentStateMachineType != null)
        {
            // First unbox to parent, then load parent's outer reference
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
            _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
        }
        else
        {
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
        }

        _il.Emit(OpCodes.Ldfld, outerField);
    }

    private void EmitCapturedStore(string name, FieldInfo outerField)
    {
        // Store value to outer state machine's field through the boxed reference
        // Stack has: value
        // We need to: store to temp, get outer ptr, load temp, store to field
        var temp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, temp);

        // Get pointer to the boxed outer state machine
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField!);

        // Check if this is a transitive capture (needs extra indirection through parent's outer)
        if (_builder.TransitiveCaptures.Contains(name) &&
            _builder.ParentOuterStateMachineField != null &&
            _builder.GrandparentStateMachineType != null)
        {
            // First unbox to parent, then load parent's outer reference
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
            _il.Emit(OpCodes.Ldfld, _builder.ParentOuterStateMachineField);
            _il.Emit(OpCodes.Unbox, _builder.GrandparentStateMachineType);
        }
        else
        {
            _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType!);
        }

        // Load value and store to field
        _il.Emit(OpCodes.Ldloc, temp);
        _il.Emit(OpCodes.Stfld, outerField);
    }

    private void EmitStoreToField(FieldInfo field)
    {
        // Stack has: value
        // Store to state machine field: save to temp, ldarg_0, load temp, stfld
        var temp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, temp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, temp);
        _il.Emit(OpCodes.Stfld, field);
    }
}
