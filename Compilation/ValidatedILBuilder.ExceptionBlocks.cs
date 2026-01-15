using System.Reflection.Emit;

namespace SharpTS.Compilation;

public sealed partial class ValidatedILBuilder
{
    #region Exception Block Operations

    /// <summary>
    /// Begins an exception block (try).
    /// </summary>
    /// <returns>Label for the end of the exception block.</returns>
    public Label BeginExceptionBlock()
    {
        var label = _il.BeginExceptionBlock();
        _exceptionBlocks.Push(new ExceptionBlockInfo(ExceptionBlockPhase.Try, _stackDepth, label));
        _exceptionBlockDepth++;
        return label;
    }

    /// <summary>
    /// Begins a catch block for the specified exception type.
    /// </summary>
    /// <param name="exceptionType">The exception type to catch.</param>
    /// <exception cref="ILValidationException">Thrown if not in a valid position for catch.</exception>
    public void BeginCatchBlock(Type exceptionType)
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("BeginCatchBlock without matching BeginExceptionBlock");
            return;
        }

        var block = _exceptionBlocks.Peek();
        if (block.Phase == ExceptionBlockPhase.Finally)
        {
            ThrowOrRecord("Cannot add catch block after finally block");
            return;
        }

        block.Phase = ExceptionBlockPhase.Catch;

        // Catch block starts with exception object on stack
        _stackDepth = 1;
        _typeStack.Clear();
        _typeStack.Push(new StackEntry(StackEntryType.Reference, exceptionType));
        _unreachable = false;

        _il.BeginCatchBlock(exceptionType);
    }

    /// <summary>
    /// Begins a finally block.
    /// </summary>
    /// <exception cref="ILValidationException">Thrown if not in a valid position for finally.</exception>
    public void BeginFinallyBlock()
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("BeginFinallyBlock without matching BeginExceptionBlock");
            return;
        }

        var block = _exceptionBlocks.Peek();
        if (block.Phase == ExceptionBlockPhase.Finally)
        {
            ThrowOrRecord("Cannot have multiple finally blocks");
            return;
        }

        block.Phase = ExceptionBlockPhase.Finally;

        // Finally block starts with empty stack
        _stackDepth = 0;
        _typeStack.Clear();
        _unreachable = false;

        _il.BeginFinallyBlock();
    }

    /// <summary>
    /// Ends the current exception block.
    /// </summary>
    /// <exception cref="ILValidationException">Thrown if no matching BeginExceptionBlock.</exception>
    public void EndExceptionBlock()
    {
        if (_exceptionBlocks.Count == 0)
        {
            ThrowOrRecord("EndExceptionBlock without matching BeginExceptionBlock");
            return;
        }

        _exceptionBlocks.Pop();
        _exceptionBlockDepth--;
        _unreachable = false;

        _il.EndExceptionBlock();
    }

    /// <summary>
    /// Throws an exception.
    /// </summary>
    public void Emit_Throw()
    {

        RequireStackDepth(1, "Throw");
        PopStack();
        _il.Emit(OpCodes.Throw);
        _unreachable = true;
    }

    /// <summary>
    /// Rethrows the current exception (in catch handler).
    /// </summary>
    public void Emit_Rethrow()
    {

        _il.Emit(OpCodes.Rethrow);
        _unreachable = true;
    }

    /// <summary>
    /// Emits endfinally/endfault.
    /// </summary>
    public void Emit_Endfinally()
    {

        _il.Emit(OpCodes.Endfinally);
        _unreachable = true;
    }

    #endregion
}
