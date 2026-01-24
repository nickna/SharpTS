using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Properties;

/// <summary>
/// Registry that manages property emitter strategies and dispatches get/set operations.
/// Strategies are tried in priority order until one handles the operation.
/// </summary>
public sealed class PropertyEmitterRegistry
{
    private readonly List<IPropertyEmitterStrategy> _strategies = [];
    private bool _sorted = false;

    /// <summary>
    /// Registers a property emitter strategy.
    /// </summary>
    /// <param name="strategy">The strategy to register.</param>
    public void Register(IPropertyEmitterStrategy strategy)
    {
        _strategies.Add(strategy);
        _sorted = false;
    }

    /// <summary>
    /// Registers multiple property emitter strategies.
    /// </summary>
    /// <param name="strategies">The strategies to register.</param>
    public void RegisterAll(IEnumerable<IPropertyEmitterStrategy> strategies)
    {
        _strategies.AddRange(strategies);
        _sorted = false;
    }

    /// <summary>
    /// Ensures strategies are sorted by priority (lower values first).
    /// </summary>
    private void EnsureSorted()
    {
        if (!_sorted)
        {
            _strategies.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            _sorted = true;
        }
    }

    /// <summary>
    /// Attempts to emit a property get operation using the first matching strategy.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="get">The property get expression.</param>
    /// <returns>True if a strategy handled the get operation.</returns>
    public bool TryEmitGet(IEmitterContext emitter, Expr.Get get)
    {
        EnsureSorted();

        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandleGet(emitter, get) && strategy.TryEmitGet(emitter, get))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit a property set operation using the first matching strategy.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="set">The property set expression.</param>
    /// <returns>True if a strategy handled the set operation.</returns>
    public bool TryEmitSet(IEmitterContext emitter, Expr.Set set)
    {
        EnsureSorted();

        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandleSet(emitter, set) && strategy.TryEmitSet(emitter, set))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all registered strategies (for testing/debugging).
    /// </summary>
    public IReadOnlyList<IPropertyEmitterStrategy> Strategies
    {
        get
        {
            EnsureSorted();
            return _strategies;
        }
    }

    /// <summary>
    /// Clears all registered strategies.
    /// </summary>
    public void Clear()
    {
        _strategies.Clear();
        _sorted = true;
    }
}
