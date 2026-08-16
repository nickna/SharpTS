using System.Reflection;

namespace SharpTS.Modules.Stdlib.Providers;

/// <summary>
/// Serves the embedded npm-fallback shim packages (the react family under
/// <c>stdlib/npm/</c>) so bare <c>.tsx</c> programs run without an npm install.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="EmbeddedStdlibProvider"/> (node builtins, which are always stdlib-first),
/// this provider is deliberately NOT part of the primary stdlib chain:
/// <see cref="ModuleResolver"/> consults it only after node_modules resolution misses, so a
/// real installed react always wins over the shim.
/// </para>
/// <para>
/// Resource names are pinned via <c>LogicalName</c> in src/SharpTS/SharpTS.csproj (default manifest
/// naming would mangle the <c>react-dom</c> hyphen):
/// <c>SharpTS.stdlib.npm.react.index.ts</c> → specifier <c>react</c> (<c>index</c> marks the
/// package root), <c>SharpTS.stdlib.npm.react.jsx-runtime.ts</c> → <c>react/jsx-runtime</c>,
/// <c>SharpTS.stdlib.npm.react-dom.server.ts</c> → <c>react-dom/server</c>. Virtual paths are
/// <c>stdlib:npm/&lt;package&gt;/&lt;file&gt;.ts</c>.
/// </para>
/// </remarks>
public sealed class EmbeddedNpmFallbackProvider : IModuleProvider
{
    private const string ResourcePrefix = "SharpTS.stdlib.npm.";
    private const string NpmVirtualPathPrefix = EmbeddedStdlibProvider.VirtualPathPrefix + "npm/";
    private const string TypeScriptExtension = ".ts";
    private const string PackageRootFile = "index";

    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _specifierToResource;

    public EmbeddedNpmFallbackProvider()
        : this(typeof(EmbeddedNpmFallbackProvider).Assembly)
    {
    }

    // Internal for tests that inject a fake assembly.
    internal EmbeddedNpmFallbackProvider(Assembly assembly)
    {
        _assembly = assembly;
        _specifierToResource = DiscoverEmbeddedModules(assembly);
    }

    public string Name => "embedded-npm-fallback";

    public IReadOnlyCollection<string> ProvidedModules => _specifierToResource.Keys;

    /// <summary>
    /// Extracts the npm specifier from a <c>stdlib:npm/…</c> virtual path
    /// (<c>stdlib:npm/react/index.ts</c> → <c>react</c>;
    /// <c>stdlib:npm/react/jsx-runtime.ts</c> → <c>react/jsx-runtime</c>).
    /// Returns null when the path is not an npm-fallback virtual path.
    /// </summary>
    public static string? TryExtractSpecifier(string virtualPath)
    {
        if (!virtualPath.StartsWith(NpmVirtualPathPrefix, StringComparison.Ordinal)) return null;
        if (!virtualPath.EndsWith(TypeScriptExtension, StringComparison.Ordinal)) return null;
        var middle = virtualPath.Substring(
            NpmVirtualPathPrefix.Length,
            virtualPath.Length - NpmVirtualPathPrefix.Length - TypeScriptExtension.Length);
        if (middle.Length == 0) return null;
        return middle.EndsWith("/" + PackageRootFile, StringComparison.Ordinal)
            ? middle[..^(PackageRootFile.Length + 1)]
            : middle;
    }

    public bool TryResolve(string specifier, out StdlibModule? module)
    {
        if (_specifierToResource.TryGetValue(specifier, out var resourceName))
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                module = null;
                return false;
            }
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            // Virtual path keeps the physical file name (incl. index.ts for package roots)
            // so it round-trips through TryExtractSpecifier and stays unique per file.
            string relativeFile = resourceName[ResourcePrefix.Length..^TypeScriptExtension.Length];
            int firstDot = relativeFile.IndexOf('.');
            string packageName = firstDot < 0 ? relativeFile : relativeFile[..firstDot];
            string fileName = firstDot < 0 ? PackageRootFile : relativeFile[(firstDot + 1)..];

            module = new StdlibModule(
                Specifier: specifier,
                Source: new TypeScriptSource(text),
                Origin: "stdlib-npm",
                VirtualPath: NpmVirtualPathPrefix + packageName + "/" + fileName + TypeScriptExtension);
            return true;
        }

        module = null;
        return false;
    }

    private static Dictionary<string, string> DiscoverEmbeddedModules(Assembly assembly)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
            if (!resourceName.EndsWith(TypeScriptExtension, StringComparison.Ordinal)) continue;

            var middle = resourceName.Substring(
                ResourcePrefix.Length,
                resourceName.Length - ResourcePrefix.Length - TypeScriptExtension.Length);
            if (middle.Length == 0) continue;

            // First '.' separates the package directory from the file path inside it
            // (package names never contain dots; file names may contain dashes).
            int firstDot = middle.IndexOf('.');
            if (firstDot <= 0 || firstDot == middle.Length - 1) continue;
            string packageName = middle[..firstDot];
            string filePath = middle[(firstDot + 1)..].Replace('.', '/');

            string specifier = filePath == PackageRootFile
                ? packageName
                : packageName + "/" + filePath;
            map[specifier] = resourceName;
        }
        return map;
    }
}
