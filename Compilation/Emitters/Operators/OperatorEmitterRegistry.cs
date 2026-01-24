using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Operators;

/// <summary>
/// Registry that manages operator emitters and dispatches operations.
/// Emitters are tried in priority order until one handles the operation.
/// </summary>
public sealed class OperatorEmitterRegistry
{
    private readonly List<IOperatorEmitter> _emitters = [];
    private bool _sorted = false;

    /// <summary>
    /// Registers an operator emitter.
    /// </summary>
    /// <param name="emitter">The emitter to register.</param>
    public void Register(IOperatorEmitter emitter)
    {
        _emitters.Add(emitter);
        _sorted = false;
    }

    /// <summary>
    /// Registers multiple operator emitters.
    /// </summary>
    /// <param name="emitters">The emitters to register.</param>
    public void RegisterAll(IEnumerable<IOperatorEmitter> emitters)
    {
        _emitters.AddRange(emitters);
        _sorted = false;
    }

    /// <summary>
    /// Ensures emitters are sorted by priority (lower values first).
    /// </summary>
    private void EnsureSorted()
    {
        if (!_sorted)
        {
            _emitters.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _sorted = true;
        }
    }

    /// <summary>
    /// Attempts to emit a binary expression using the first matching emitter.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="binary">The binary expression.</param>
    /// <returns>True if an emitter handled the operation.</returns>
    public bool TryEmitBinary(IEmitterContext emitter, Expr.Binary binary)
    {
        EnsureSorted();

        foreach (var opEmitter in _emitters)
        {
            if (opEmitter.CanHandleBinary(emitter, binary) && opEmitter.TryEmitBinary(emitter, binary))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit a unary expression using the first matching emitter.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="unary">The unary expression.</param>
    /// <returns>True if an emitter handled the operation.</returns>
    public bool TryEmitUnary(IEmitterContext emitter, Expr.Unary unary)
    {
        EnsureSorted();

        foreach (var opEmitter in _emitters)
        {
            if (opEmitter.CanHandleUnary(emitter, unary) && opEmitter.TryEmitUnary(emitter, unary))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all registered emitters (for testing/debugging).
    /// </summary>
    public IReadOnlyList<IOperatorEmitter> Emitters
    {
        get
        {
            EnsureSorted();
            return _emitters;
        }
    }

    /// <summary>
    /// Clears all registered emitters.
    /// </summary>
    public void Clear()
    {
        _emitters.Clear();
        _sorted = true;
    }
}
