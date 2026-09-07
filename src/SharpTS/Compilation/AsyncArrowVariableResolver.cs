using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Variable resolver for async arrow state machine emitters.
/// Handles the complex case of multi-level captures through outer state machines.
/// </summary>
/// <remarks>
/// Resolution order:
/// 0. Per-iteration loop-binding cells
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
        var storage = TryResolveVariable(name);
        if (storage == null) return null;
        storage.EmitLoad(_il);
        return StackType.Unknown;
    }

    /// <inheritdoc />
    public bool HasVariable(string name) => TryResolveVariable(name) != null;

    /// <inheritdoc />
    public bool TryStoreVariable(string name)
    {
        (TryResolveVariable(name) ?? GetOrCreateLocal(name)).EmitStore(_il);
        return true;
    }

    internal AsyncArrowStorageAccess? TryResolveCell(string name)
        => _cellBindingLocals != null && _cellBindingLocals.TryGetValue(name, out var cell)
            ? AsyncArrowStorageAccess.Cell(cell, _strongBoxValueField!)
            : null;

    internal AsyncArrowStorageAccess? TryResolveHoistedOrCaptured(string name)
    {
        if (_builder.ParameterFields.TryGetValue(name, out var parameter))
            return AsyncArrowStorageAccess.StateMachineField(parameter);
        if (_builder.LocalFields.TryGetValue(name, out var local))
            return AsyncArrowStorageAccess.StateMachineField(local);
        if (_builder.IsCaptured(name) && _builder.CapturedFieldMap.TryGetValue(name, out var captured))
            return AsyncArrowStorageAccess.CapturedField(_builder, name, captured);
        return null;
    }

    internal AsyncArrowStorageAccess? TryResolveLocal(string name)
        => _locals.TryGetValue(name, out var local) ? AsyncArrowStorageAccess.Local(local) : null;

    internal AsyncArrowStorageAccess GetOrCreateLocal(string name)
    {
        if (!_locals.TryGetValue(name, out var local))
        {
            local = _il.DeclareLocal(typeof(object));
            _locals[name] = local;
        }
        return AsyncArrowStorageAccess.Local(local);
    }

    private AsyncArrowStorageAccess? TryResolveVariable(string name)
    {
        // Standalone captures stay with the emitter, below module globals: their snapshot
        // must not shadow the live static storage of a captured top-level binding (#641).
        return TryResolveCell(name) ?? TryResolveHoistedOrCaptured(name) ?? TryResolveLocal(name);
    }

    /// <inheritdoc />
    public void LoadThis()
    {
        if (_builder.IsCaptured("this") && _builder.CapturedFieldMap.TryGetValue("this", out var thisField))
            AsyncArrowStorageAccess.CapturedField(_builder, "this", thisField).EmitLoad(_il);
        else
            _il.Emit(OpCodes.Ldnull);
    }
}
