namespace SharpTS.Modules.Stdlib;

/// <summary>
/// A source of standard library module implementations. Consulted by
/// <see cref="StdlibProviderChain"/> in priority order when resolving imports.
/// </summary>
public interface IModuleProvider
{
    /// <summary>
    /// Identifier for this provider, used in diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Attempts to resolve a module specifier to a <see cref="StdlibModule"/>.
    /// The specifier has already had any "node:" prefix stripped by the caller.
    /// </summary>
    /// <param name="specifier">The bare specifier (e.g. "fs", "fs/promises").</param>
    /// <param name="module">The resolved module when this provider handles the specifier.</param>
    /// <returns>True when this provider handles the specifier; false otherwise.</returns>
    bool TryResolve(string specifier, out StdlibModule? module);

    /// <summary>
    /// All module specifiers this provider can handle. Used for diagnostics,
    /// conflict detection, and future tooling (e.g. <c>--why-shim</c>).
    /// </summary>
    IReadOnlyCollection<string> ProvidedModules { get; }
}
