namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Registry that maps built-in module names to their emitter implementations.
/// </summary>
public sealed class BuiltInModuleEmitterRegistry
{
    private readonly Dictionary<string, IBuiltInModuleEmitter> _emitters = new();

    /// <summary>
    /// Registers an emitter for a built-in module.
    /// </summary>
    /// <param name="emitter">The emitter to register.</param>
    public void Register(IBuiltInModuleEmitter emitter)
    {
        _emitters[emitter.ModuleName] = emitter;
    }

    /// <summary>
    /// Gets the emitter for a built-in module.
    /// </summary>
    /// <param name="moduleName">The module name (e.g., "fs", "path").</param>
    /// <returns>The emitter, or null if not found.</returns>
    public IBuiltInModuleEmitter? GetEmitter(string moduleName)
    {
        return _emitters.GetValueOrDefault(moduleName);
    }

    /// <summary>
    /// Checks if an emitter is registered for the given module.
    /// </summary>
    public bool HasEmitter(string moduleName)
    {
        return _emitters.ContainsKey(moduleName);
    }

    /// <summary>
    /// Gets all registered module names.
    /// </summary>
    public IEnumerable<string> GetRegisteredModules()
    {
        return _emitters.Keys;
    }
}
