using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Variable resolver for standard state machine emitters (Async, Generator, AsyncGenerator).
/// Resolves hoisted fields and non-hoisted locals.
/// </summary>
/// <remarks>
/// Resolution order:
/// 1. Hoisted state machine fields (via GetVariableField delegate)
/// 2. Non-hoisted IL locals (via LocalsManager)
///
/// Does NOT handle: functions, namespaces, classes, Math (caller handles these as fallback).
/// </remarks>
public class StateMachineVariableResolver : IVariableResolver
{
    private readonly ILGenerator _il;
    private readonly Func<string, FieldInfo?> _getVariableField;
    private readonly LocalsManager _locals;
    private readonly FieldInfo? _thisField;
    private readonly IReadOnlyDictionary<string, LocalBuilder>? _cellBindingLocals;
    private readonly FieldInfo? _strongBoxValueField;

    /// <summary>
    /// Creates a new resolver for state machine variable access.
    /// </summary>
    /// <param name="il">The IL generator for emitting instructions</param>
    /// <param name="getVariableField">Delegate to get hoisted field from state machine builder</param>
    /// <param name="locals">LocalsManager for non-hoisted locals</param>
    /// <param name="thisField">The <>4__this field, or null if not an instance method</param>
    /// <param name="cellBindingLocals">Per-iteration cell locals (#650), shared with the emitter; null disables cells.</param>
    /// <param name="strongBoxValueField">The StrongBox&lt;object&gt;.Value field, for cell dereference.</param>
    public StateMachineVariableResolver(
        ILGenerator il,
        Func<string, FieldInfo?> getVariableField,
        LocalsManager locals,
        FieldInfo? thisField,
        IReadOnlyDictionary<string, LocalBuilder>? cellBindingLocals = null,
        FieldInfo? strongBoxValueField = null)
    {
        _il = il;
        _getVariableField = getVariableField;
        _locals = locals;
        _thisField = thisField;
        _cellBindingLocals = cellBindingLocals;
        _strongBoxValueField = strongBoxValueField;
    }

    /// <inheritdoc />
    public StackType? TryLoadVariable(string name)
    {
        // 0. Per-iteration loop-binding cell (#650): read through the StrongBox so closures
        //    that captured the cell observe end-of-iteration mutations.
        if (_cellBindingLocals != null && _cellBindingLocals.TryGetValue(name, out var loadCell))
        {
            _il.Emit(OpCodes.Ldloc, loadCell);
            _il.Emit(OpCodes.Ldfld, _strongBoxValueField!);
            return StackType.Unknown;
        }

        // 1. Check if hoisted to state machine field
        var field = _getVariableField(name);
        if (field != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            return StackType.Unknown;
        }

        // 2. Check non-hoisted local
        if (_locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            return StackType.Unknown;
        }

        return null; // Not found - caller handles fallback
    }

    /// <inheritdoc />
    public bool HasVariable(string name)
    {
        if (_cellBindingLocals != null && _cellBindingLocals.ContainsKey(name)) return true;
        if (_getVariableField(name) != null) return true;
        if (_locals.HasLocal(name)) return true;
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

        // 1. Check if hoisted to state machine field
        var field = _getVariableField(name);
        if (field != null)
        {
            // Stack has: value
            // Need to: save to temp, ldarg_0, load temp, stfld
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
            return true;
        }

        // 2. Check non-hoisted local
        if (_locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
            return true;
        }

        return false; // Not found
    }

    /// <inheritdoc />
    public void LoadThis()
    {
        if (_thisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _thisField);
        }
        else
        {
            // No this field - push null
            _il.Emit(OpCodes.Ldnull);
        }
    }
}
