namespace SharpTS.Modules.Stdlib;

/// <summary>
/// Ordered chain of <see cref="IModuleProvider"/> instances. Consulted by
/// <see cref="ModuleResolver"/> when resolving bare specifiers. First matching
/// provider wins.
/// </summary>
/// <remarks>
/// Default precedence (highest first):
/// <list type="number">
///   <item><see cref="Providers.EmbeddedStdlibProvider"/> — TypeScript stdlib baked
///   into SharpTS.dll as embedded resources.</item>
///   <item><see cref="Providers.BuiltInCSharpProvider"/> — the legacy C#/IL
///   module emitters kept as a fallback during migration.</item>
/// </list>
/// </remarks>
public sealed class StdlibProviderChain
{
    private readonly List<IModuleProvider> _providers;

    public StdlibProviderChain(IEnumerable<IModuleProvider> providers)
    {
        _providers = providers.ToList();
    }

    /// <summary>
    /// The providers in precedence order (highest priority first).
    /// </summary>
    public IReadOnlyList<IModuleProvider> Providers => _providers;

    /// <summary>
    /// Attempts to resolve a specifier against the chain. Returns the first match.
    /// </summary>
    public bool TryResolve(string specifier, out StdlibModule? module)
    {
        foreach (var provider in _providers)
        {
            if (provider.TryResolve(specifier, out var resolved) && resolved is not null)
            {
                module = resolved;
                return true;
            }
        }
        module = null;
        return false;
    }

}
