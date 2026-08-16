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
    /// Registers an emitter under an additional key (alias). Used when a single
    /// emitter serves both a user-facing specifier and the internal primitive
    /// specifier — e.g. <c>process</c> and <c>primitive:process</c> share
    /// <see cref="ProcessModuleEmitter"/>.
    /// </summary>
    public void RegisterAlias(string alias, IBuiltInModuleEmitter emitter)
    {
        _emitters[alias] = emitter;
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
}
