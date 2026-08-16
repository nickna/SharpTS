using SharpTS.Runtime.BuiltIns.Modules;

namespace SharpTS.Modules.Stdlib.Providers;

/// <summary>
/// Provider wrapping the legacy C#/IL built-in module path. Claims every specifier
/// known to <see cref="BuiltInModuleRegistry"/> and returns a
/// <see cref="CSharpBuiltInSource"/> so callers fall through to existing dispatch.
/// </summary>
/// <remarks>
/// This provider is always the fallback in the chain. As modules migrate to the
/// embedded TypeScript stdlib, they are removed from the C# registry and this
/// provider stops claiming them — the higher-priority
/// <see cref="EmbeddedStdlibProvider"/> answers first instead.
/// </remarks>
public sealed class BuiltInCSharpProvider : IModuleProvider
{
    public string Name => "builtin-csharp";

    public IReadOnlyCollection<string> ProvidedModules => BuiltInModuleRegistry.GetBuiltInModules();

    public bool TryResolve(string specifier, out StdlibModule? module)
    {
        if (BuiltInModuleRegistry.IsBuiltIn(specifier))
        {
            module = new StdlibModule(
                Specifier: specifier,
                Source: CSharpBuiltInSource.Instance,
                Origin: "builtin",
                VirtualPath: BuiltInModuleRegistry.GetBuiltInPath(specifier));
            return true;
        }
        module = null;
        return false;
    }
}
