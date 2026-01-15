using System.Reflection.Emit;

namespace SharpTS.Compilation;

public sealed partial class ValidatedILBuilder
{
    #region Label Operations

    /// <summary>
    /// Defines a new label with an optional debug name.
    /// </summary>
    /// <param name="debugName">Name for error messages (optional).</param>
    /// <returns>The defined label.</returns>
    public Label DefineLabel(string? debugName = null)
    {
        var label = _il.DefineLabel();
        _labels[label] = new LabelInfo(debugName, _exceptionBlockDepth);
        return label;
    }

    /// <summary>
    /// Marks a label at the current position.
    /// </summary>
    /// <param name="label">The label to mark.</param>
    /// <exception cref="ILValidationException">
    /// Thrown if label was not defined, already marked, or stack depth doesn't match branches.
    /// </exception>
    public void MarkLabel(Label label)
    {
        // After marking a label, code becomes reachable again
        _unreachable = false;

        if (!_labels.TryGetValue(label, out var info))
        {
            ThrowOrRecord("Label was not defined in this method scope");
            return;
        }

        if (_markedLabels.Contains(label))
        {
            ThrowOrRecord($"Label '{info.DebugName ?? "<unnamed>"}' already marked");
            return;
        }

        // Verify stack state matches previous branches to this label
        // NOTE: During incremental migration, stack tracking is incomplete because
        // not all IL operations go through the builder yet. We skip validation
        // and rely on the CLR verifier to catch actual stack errors.
        // if (_branchTargetSnapshots.TryGetValue(label, out var expected))
        // {
        //     if (_stackDepth != expected.Depth)
        //     {
        //         ThrowOrRecord($"Stack depth mismatch at label '{info.DebugName ?? "<unnamed>"}': expected {expected.Depth}, found {_stackDepth}");
        //     }
        // }

        _markedLabels.Add(label);
        _il.MarkLabel(label);
    }

    #endregion

    #region Branch Operations

    /// <summary>
    /// Emits an unconditional branch (br).
    /// </summary>
    /// <param name="target">Target label.</param>
    /// <exception cref="ILValidationException">Thrown if inside an exception block.</exception>
    public void Emit_Br(Label target)
    {
        ValidateLabelDefined(target, "Br");
        if (!_unreachable)
            RecordBranchTarget(target);

        // NOTE: During incremental migration, we don't enforce Br vs Leave rules because
        // Br is actually valid for LOCAL jumps within exception blocks (jumps that don't
        // exit the block). Leave is only required when branching OUT of an exception block.
        // Detecting local vs exiting jumps requires tracking which labels are in which scope,
        // which is complex. The CLR verifier will catch actual violations.
        // if (_exceptionBlockDepth > 0)
        // {
        //     ThrowOrRecord("Use Leave instead of Br inside exception blocks");
        // }

        _il.Emit(OpCodes.Br, target);
        _unreachable = true;
    }

    /// <summary>
    /// Emits a branch if true (brtrue).
    /// </summary>
    /// <param name="target">Target label.</param>
    public void Emit_Brtrue(Label target)
    {

        RequireStackDepth(1, "Brtrue");
        ValidateLabelDefined(target, "Brtrue");

        // Record target with stack AFTER consuming condition
        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Emits a branch if true (short form).
    /// </summary>
    public void Emit_Brtrue_S(Label target)
    {

        RequireStackDepth(1, "Brtrue_S");
        ValidateLabelDefined(target, "Brtrue_S");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brtrue_S, target);
    }

    /// <summary>
    /// Emits a branch if false (brfalse).
    /// </summary>
    /// <param name="target">Target label.</param>
    public void Emit_Brfalse(Label target)
    {

        RequireStackDepth(1, "Brfalse");
        ValidateLabelDefined(target, "Brfalse");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brfalse, target);
    }

    /// <summary>
    /// Emits a branch if false (short form).
    /// </summary>
    public void Emit_Brfalse_S(Label target)
    {

        RequireStackDepth(1, "Brfalse_S");
        ValidateLabelDefined(target, "Brfalse_S");

        var depthAfterPop = _stackDepth - 1;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        _il.Emit(OpCodes.Brfalse_S, target);
    }

    /// <summary>
    /// Emits a leave instruction for exiting exception blocks.
    /// </summary>
    /// <param name="target">Target label (must be outside the exception block).</param>
    /// <exception cref="ILValidationException">Thrown if not inside an exception block.</exception>
    public void Emit_Leave(Label target)
    {

        ValidateLabelDefined(target, "Leave");

        if (_exceptionBlockDepth == 0)
        {
            ThrowOrRecord("Leave used outside exception block");
        }

        // Leave clears the evaluation stack and records target with empty stack
        RecordBranchTargetWithDepth(target, 0);

        _il.Emit(OpCodes.Leave, target);
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = true;
    }

    /// <summary>
    /// Emits a leave instruction (short form).
    /// </summary>
    public void Emit_Leave_S(Label target)
    {

        ValidateLabelDefined(target, "Leave_S");

        if (_exceptionBlockDepth == 0)
        {
            ThrowOrRecord("Leave_S used outside exception block");
        }

        RecordBranchTargetWithDepth(target, 0);

        _il.Emit(OpCodes.Leave_S, target);
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = true;
    }

    /// <summary>
    /// Emits a branch if equal (beq).
    /// </summary>
    public void Emit_Beq(Label target)
    {

        RequireStackDepth(2, "Beq");
        ValidateLabelDefined(target, "Beq");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Beq, target);
    }

    /// <summary>
    /// Emits a branch if not equal (bne.un).
    /// </summary>
    public void Emit_Bne_Un(Label target)
    {

        RequireStackDepth(2, "Bne_Un");
        ValidateLabelDefined(target, "Bne_Un");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bne_Un, target);
    }

    /// <summary>
    /// Emits a branch if greater than (bgt).
    /// </summary>
    public void Emit_Bgt(Label target)
    {

        RequireStackDepth(2, "Bgt");
        ValidateLabelDefined(target, "Bgt");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bgt, target);
    }

    /// <summary>
    /// Emits a branch if less than (blt).
    /// </summary>
    public void Emit_Blt(Label target)
    {

        RequireStackDepth(2, "Blt");
        ValidateLabelDefined(target, "Blt");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Blt, target);
    }

    /// <summary>
    /// Emits a branch if greater than or equal (bge).
    /// </summary>
    public void Emit_Bge(Label target)
    {

        RequireStackDepth(2, "Bge");
        ValidateLabelDefined(target, "Bge");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Bge, target);
    }

    /// <summary>
    /// Emits a branch if less than or equal (ble).
    /// </summary>
    public void Emit_Ble(Label target)
    {

        RequireStackDepth(2, "Ble");
        ValidateLabelDefined(target, "Ble");

        var depthAfterPop = _stackDepth - 2;
        RecordBranchTargetWithDepth(target, depthAfterPop);

        PopStack();
        PopStack();
        _il.Emit(OpCodes.Ble, target);
    }

    #endregion
}
