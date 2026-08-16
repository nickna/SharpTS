namespace SharpTS.Modules.Stdlib;

/// <summary>
/// A resolved standard library module returned by an <see cref="IModuleProvider"/>.
/// Carries the source (either TypeScript to compile or a C# built-in marker) and the
/// virtual path used in diagnostics.
/// </summary>
/// <param name="Specifier">The canonical specifier this resolves (e.g. "fs", "fs/promises").</param>
/// <param name="Source">The module source — TypeScript text or C# built-in marker.</param>
/// <param name="Origin">The provider identity for diagnostics ("stdlib" or "builtin").</param>
/// <param name="VirtualPath">The virtual path reported in stack traces and diagnostics
/// (e.g. "stdlib:node/fs.ts" or "builtin:fs").</param>
public sealed record StdlibModule(
    string Specifier,
    StdlibSource Source,
    string Origin,
    string VirtualPath);

/// <summary>
/// Discriminated union representing how a stdlib module is served.
/// </summary>
public abstract record StdlibSource;

/// <summary>
/// TypeScript source that should be compiled through the normal pipeline.
/// </summary>
public sealed record TypeScriptSource(string Text) : StdlibSource;

/// <summary>
/// Marker indicating the module is served by the legacy C# built-in path.
/// Callers fall through to existing <see cref="Runtime.BuiltIns.Modules.BuiltInModuleRegistry"/>
/// dispatch when they see this source kind.
/// </summary>
public sealed record CSharpBuiltInSource : StdlibSource
{
    public static CSharpBuiltInSource Instance { get; } = new();
}
