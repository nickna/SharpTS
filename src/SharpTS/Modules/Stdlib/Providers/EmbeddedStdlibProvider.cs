using System.Reflection;

namespace SharpTS.Modules.Stdlib.Providers;

/// <summary>
/// Provider that serves TypeScript stdlib modules embedded in SharpTS.dll as
/// resources under the <c>stdlib/</c> prefix.
/// </summary>
/// <remarks>
/// <para>
/// Resource naming convention: the file <c>stdlib/node/querystring.ts</c> in the
/// project becomes the manifest resource <c>SharpTS.stdlib.node.querystring.ts</c>.
/// Nested specifiers like <c>fs/promises</c> map to <c>stdlib/node/fs/promises.ts</c>.
/// </para>
/// <para>
/// In Phase 1 of the embedded stdlib rollout, no <c>.ts</c> files are embedded, so
/// <see cref="ProvidedModules"/> is empty and <see cref="TryResolve"/> always returns
/// false. Migrating a module (e.g. <c>querystring</c>) means adding
/// <c>stdlib/node/querystring.ts</c> to the project with <c>&lt;EmbeddedResource&gt;</c>
/// inclusion and removing the corresponding C# emitter from
/// <see cref="BuiltInCSharpProvider"/>'s coverage.
/// </para>
/// </remarks>
public sealed class EmbeddedStdlibProvider : IModuleProvider
{
    /// <summary>
    /// Prefix used by this provider for the <see cref="StdlibModule.VirtualPath"/>
    /// it produces (e.g. <c>stdlib:node/querystring.ts</c>). Callers elsewhere —
    /// notably <see cref="ModuleResolver.LoadModule"/> — match on this prefix to
    /// dispatch to stdlib handling instead of filesystem loading.
    /// </summary>
    public const string VirtualPathPrefix = "stdlib:";

    private const string ResourcePrefix = "SharpTS.stdlib.";
    private const string NodeNamespace = "node";
    private const string NodeVirtualPathPrefix = VirtualPathPrefix + NodeNamespace + "/";
    private const string TypeScriptExtension = ".ts";

    /// <summary>
    /// Extracts the original specifier from a stdlib virtual path. Returns null
    /// if the path does not match the stdlib format.
    /// </summary>
    public static string? TryExtractSpecifier(string virtualPath)
    {
        if (!virtualPath.StartsWith(NodeVirtualPathPrefix, StringComparison.Ordinal)) return null;
        if (!virtualPath.EndsWith(TypeScriptExtension, StringComparison.Ordinal)) return null;
        var middle = virtualPath.Substring(
            NodeVirtualPathPrefix.Length,
            virtualPath.Length - NodeVirtualPathPrefix.Length - TypeScriptExtension.Length);
        return middle.Length == 0 ? null : middle;
    }

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _specifierToResource;

    public EmbeddedStdlibProvider()
        : this(typeof(EmbeddedStdlibProvider).Assembly)
    {
    }

    // Constructor kept internal for tests that inject a fake assembly.
    internal EmbeddedStdlibProvider(Assembly assembly)
    {
        _assembly = assembly;
        _specifierToResource = DiscoverEmbeddedModules(assembly);
    }

    public string Name => "embedded-stdlib";

    public IReadOnlyCollection<string> ProvidedModules => _specifierToResource.Keys;

    public bool TryResolve(string specifier, out StdlibModule? module)
    {
        if (_specifierToResource.TryGetValue(specifier, out var resourceName))
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                // Manifest discovery found the name but the stream is gone. Treat as a miss
                // rather than throwing — the chain will fall through to the C# provider.
                module = null;
                return false;
            }
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            module = new StdlibModule(
                Specifier: specifier,
                Source: new TypeScriptSource(text),
                Origin: "stdlib",
                VirtualPath: NodeVirtualPathPrefix + specifier + TypeScriptExtension);
            return true;
        }

        module = null;
        return false;
    }

    /// <summary>
    /// Scans assembly manifest resources for embedded stdlib files and builds the
    /// specifier → resource-name map.
    /// </summary>
    /// <remarks>
    /// Resource names use '.' as a path separator. We convert <c>SharpTS.stdlib.node.fs.ts</c>
    /// back to the specifier <c>fs</c>, and <c>SharpTS.stdlib.node.fs.promises.ts</c> to
    /// <c>fs/promises</c>. Because '.' is both a path separator AND the extension separator,
    /// we peel off the trailing <c>.ts</c>, then take everything after the <c>node.</c>
    /// namespace and replace remaining '.' with '/'.
    /// </remarks>
    private static Dictionary<string, string> DiscoverEmbeddedModules(Assembly assembly)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodePrefix = ResourcePrefix + NodeNamespace + ".";
        const string extension = ".ts";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(nodePrefix, StringComparison.Ordinal)) continue;
            if (!resourceName.EndsWith(extension, StringComparison.Ordinal)) continue;

            var middle = resourceName.Substring(
                nodePrefix.Length,
                resourceName.Length - nodePrefix.Length - extension.Length);

            if (middle.Length == 0) continue;

            var specifier = middle.Replace('.', '/');
            map[specifier] = resourceName;
        }

        return map;
    }
}
