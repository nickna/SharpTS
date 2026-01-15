using System.Reflection.Emit;

namespace SharpTS.Compilation;

public sealed partial class ValidatedILBuilder
{
    #region Stack Operations

    /// <summary>
    /// Pops the top value from the stack.
    /// </summary>
    public void Emit_Pop()
    {

        RequireStackDepth(1, "Pop");
        PopStack();
        _il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Duplicates the top value on the stack.
    /// </summary>
    public void Emit_Dup()
    {

        RequireStackDepth(1, "Dup");
        var top = PeekStack();
        _il.Emit(OpCodes.Dup);
        PushStack(top.Type, top.ClrType);
    }

    #endregion

    #region Arithmetic Operations

    /// <summary>
    /// Adds two values.
    /// </summary>
    public void Emit_Add()
    {

        RequireStackDepth(2, "Add");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Add);
        PushStack(StackEntryType.Unknown); // Result type depends on operands
    }

    /// <summary>
    /// Subtracts two values.
    /// </summary>
    public void Emit_Sub()
    {

        RequireStackDepth(2, "Sub");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Sub);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Multiplies two values.
    /// </summary>
    public void Emit_Mul()
    {

        RequireStackDepth(2, "Mul");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Mul);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Divides two values.
    /// </summary>
    public void Emit_Div()
    {

        RequireStackDepth(2, "Div");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Div);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Computes remainder.
    /// </summary>
    public void Emit_Rem()
    {

        RequireStackDepth(2, "Rem");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Rem);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Negates a value.
    /// </summary>
    public void Emit_Neg()
    {

        RequireStackDepth(1, "Neg");
        // Top stays, type may change
        _il.Emit(OpCodes.Neg);
    }

    #endregion

    #region Comparison Operations

    /// <summary>
    /// Compares equal.
    /// </summary>
    public void Emit_Ceq()
    {

        RequireStackDepth(2, "Ceq");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Ceq);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Compares greater than.
    /// </summary>
    public void Emit_Cgt()
    {

        RequireStackDepth(2, "Cgt");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Cgt);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Compares less than.
    /// </summary>
    public void Emit_Clt()
    {

        RequireStackDepth(2, "Clt");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Clt);
        PushStack(StackEntryType.Int32);
    }

    #endregion

    #region Conversion Operations

    /// <summary>
    /// Converts to float64 (double).
    /// </summary>
    public void Emit_Conv_R8()
    {

        RequireStackDepth(1, "Conv_R8");
        PopStack();
        _il.Emit(OpCodes.Conv_R8);
        PushStack(StackEntryType.Double);
    }

    /// <summary>
    /// Converts to int32.
    /// </summary>
    public void Emit_Conv_I4()
    {

        RequireStackDepth(1, "Conv_I4");
        PopStack();
        _il.Emit(OpCodes.Conv_I4);
        PushStack(StackEntryType.Int32);
    }

    /// <summary>
    /// Converts to int64.
    /// </summary>
    public void Emit_Conv_I8()
    {

        RequireStackDepth(1, "Conv_I8");
        PopStack();
        _il.Emit(OpCodes.Conv_I8);
        PushStack(StackEntryType.Int64);
    }

    /// <summary>
    /// Converts to unsigned int64.
    /// </summary>
    public void Emit_Conv_U8()
    {

        RequireStackDepth(1, "Conv_U8");
        PopStack();
        _il.Emit(OpCodes.Conv_U8);
        PushStack(StackEntryType.Int64);
    }

    #endregion

    #region Bitwise Operations

    /// <summary>
    /// Bitwise AND.
    /// </summary>
    public void Emit_And()
    {

        RequireStackDepth(2, "And");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.And);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise OR.
    /// </summary>
    public void Emit_Or()
    {

        RequireStackDepth(2, "Or");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Or);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise XOR.
    /// </summary>
    public void Emit_Xor()
    {

        RequireStackDepth(2, "Xor");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Xor);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Bitwise NOT.
    /// </summary>
    public void Emit_Not()
    {

        RequireStackDepth(1, "Not");
        // Type unchanged
        _il.Emit(OpCodes.Not);
    }

    /// <summary>
    /// Shift left.
    /// </summary>
    public void Emit_Shl()
    {

        RequireStackDepth(2, "Shl");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shl);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Shift right (signed).
    /// </summary>
    public void Emit_Shr()
    {

        RequireStackDepth(2, "Shr");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shr);
        PushStack(StackEntryType.Unknown);
    }

    /// <summary>
    /// Shift right (unsigned).
    /// </summary>
    public void Emit_Shr_Un()
    {

        RequireStackDepth(2, "Shr_Un");
        PopStack();
        PopStack();
        _il.Emit(OpCodes.Shr_Un);
        PushStack(StackEntryType.Unknown);
    }

    #endregion

    #region Return and Method End

    /// <summary>
    /// Emits a return instruction.
    /// </summary>
    /// <remarks>
    /// Also validates that all defined labels have been marked.
    /// </remarks>
    public void Emit_Ret()
    {

        ValidateAllLabelsMarked();
        _il.Emit(OpCodes.Ret);
        _unreachable = true;
    }

    /// <summary>
    /// Validates that all defined labels have been marked.
    /// Call this at method end if not using Emit_Ret().
    /// </summary>
    public void ValidateAllLabelsMarked()
    {
        foreach (var (label, info) in _labels)
        {
            if (!_markedLabels.Contains(label))
            {
                ThrowOrRecord($"Label '{info.DebugName ?? "<unnamed>"}' was defined but never marked");
            }
        }
    }

    /// <summary>
    /// Resets the builder state for a new method.
    /// Call this when starting to emit a new method.
    /// </summary>
    public void Reset()
    {
        _labels.Clear();
        _markedLabels.Clear();
        _stackDepth = 0;
        _typeStack.Clear();
        _branchTargetSnapshots.Clear();
        _exceptionBlocks.Clear();
        _exceptionBlockDepth = 0;
        _unreachable = false;
        _collectedErrors.Clear();
    }

    #endregion
}
