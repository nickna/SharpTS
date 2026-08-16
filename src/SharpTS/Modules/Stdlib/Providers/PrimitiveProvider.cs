namespace SharpTS.Modules.Stdlib.Providers;

/// <summary>
/// Provider for <c>primitive:*</c> specifiers. Resolves them to virtual paths
/// that the runtime and compiler dispatch to C# primitive implementations.
/// </summary>
/// <remarks>
/// This provider claims every specifier in <see cref="PrimitiveRegistry"/> and
/// sits at the top of the stdlib chain. It does <em>not</em> enforce origin
/// restrictions — that happens in <see cref="ModuleResolver.ResolveModulePath"/>
/// before the chain is consulted. Keeping origin policy out of the provider
/// means the provider stays a simple lookup.
/// </remarks>
public sealed class PrimitiveProvider : IModuleProvider
{
    public string Name => "primitive";

    public IReadOnlyCollection<string> ProvidedModules => PrimitiveRegistry.GetPrimitives();

    public bool TryResolve(string specifier, out StdlibModule? module)
    {
        if (PrimitiveRegistry.IsPrimitive(specifier))
        {
            module = new StdlibModule(
                Specifier: specifier,
                Source: CSharpBuiltInSource.Instance,
                Origin: "primitive",
                VirtualPath: specifier);
            return true;
        }
        module = null;
        return false;
    }
}
