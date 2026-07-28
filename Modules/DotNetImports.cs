using SharpTS.Declaration;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;

namespace SharpTS.Modules;

/// <summary>
/// Resolution for <c>dotnet:</c>-scheme import specifiers — the first-class consumption path
/// for .NET interop (#1195): <c>import { StringBuilder } from "dotnet:System.Text.StringBuilder"</c>
/// (single-type form) or <c>import { StringBuilder, Encoding } from "dotnet:System.Text"</c>
/// (namespace form). Type metadata is synthesized directly from reflection with no generated
/// TypeScript text in between.
/// </summary>
/// <remarks>
/// The specifier after the scheme is either a fully-qualified type name or a namespace; each
/// named import resolves individually:
/// <list type="number">
/// <item>If the specifier resolves to a type, an import of that type's simple name binds it,
/// and any other name is tried as a nested type (<c>Specifier+Name</c>).</item>
/// <item>Otherwise the specifier is treated as a namespace and the import resolves
/// <c>Specifier.Name</c>.</item>
/// </list>
/// v1 scope: named imports only (no default, namespace-star, re-export-from, <c>require()</c>,
/// or dynamic <c>import()</c>), and only types in already-loaded assemblies (the BCL plus
/// anything the host has loaded). Every resolved type is vetted by
/// <see cref="DotNetInteropClassifier"/> so this path and the <c>--gen-decl</c> discovery tool
/// never disagree about what is importable.
/// </remarks>
public static class DotNetImports
{
    /// <summary>The scheme prefix for .NET interop import specifiers.</summary>
    public const string Prefix = "dotnet:";

    /// <summary>True if the specifier (or virtual module path) uses the <c>dotnet:</c> scheme.</summary>
    public static bool IsDotNetSpecifier(string specifier) =>
        specifier.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Creates the placeholder module for a <c>dotnet:</c> virtual path. Exports are added
    /// per import statement by <see cref="EnsureImports"/>.
    /// </summary>
    public static ParsedModule CreateModule(string virtualPath) =>
        new(virtualPath, [])
        {
            IsScript = false,
            IsTypeChecked = true,
            DotNetExports = new Dictionary<string, Type>(StringComparer.Ordinal),
        };

    /// <summary>
    /// Validates the import form and resolves every named import against the module's
    /// specifier, populating <see cref="ParsedModule.DotNetExports"/> (CLR types) and
    /// <see cref="ParsedModule.ExportedTypes"/> (synthesized static types).
    /// </summary>
    /// <exception cref="Exception">With a clear <c>Module Error:</c> message when the import
    /// form is unsupported or a name cannot be resolved to a usable .NET type.</exception>
    public static void EnsureImports(ParsedModule module, Stmt.Import import)
    {
        if (import.DefaultImport != null)
        {
            throw new Exception(
                $"Module Error: '{module.Path}' has no default export. " +
                "dotnet: modules support named imports only, e.g. " +
                $"import {{ {SuggestedName(module.Path)} }} from \"{module.Path}\".");
        }

        if (import.NamespaceImport != null)
        {
            throw new Exception(
                $"Module Error: namespace imports (import * as …) are not supported for '{module.Path}'. " +
                "A .NET namespace cannot be enumerated as a module object; import the types you need by name.");
        }

        if (import.NamedImports == null) return; // side-effect import: nothing to bind

        foreach (var spec in import.NamedImports)
        {
            EnsureExport(module, spec.Imported.Lexeme);
        }
    }

    /// <summary>
    /// Resolves one exported name of a <c>dotnet:</c> module, caching the result on the module.
    /// </summary>
    public static Type EnsureExport(ParsedModule module, string name)
    {
        var exports = module.DotNetExports!;
        if (exports.TryGetValue(name, out var cached)) return cached;

        string specifier = module.Path[Prefix.Length..];
        var type = ResolveExportType(specifier, name);

        exports[name] = type;
        module.ExportedTypes[name] = DotNetTypeSynthesizer.Synthesize(type);
        return type;
    }

    /// <summary>
    /// The single resolution algorithm for a named import against a <c>dotnet:</c> specifier.
    /// Shared by module loading and the language server (which injects its project-reference
    /// resolver) so editor diagnostics and actual resolution never disagree.
    /// </summary>
    /// <param name="specifier">The part after the <c>dotnet:</c> scheme.</param>
    /// <param name="name">The imported name.</param>
    /// <param name="resolve">CLR type-name resolver; defaults to
    /// <see cref="DotNetTypeRegistry.Resolve"/> (all loaded assemblies).</param>
    /// <exception cref="Exception">With a <c>Module Error:</c> message when the name cannot be
    /// resolved to a usable public .NET type.</exception>
    public static Type ResolveExportType(string specifier, string name, Func<string, Type?>? resolve = null)
    {
        resolve ??= DotNetTypeRegistry.Resolve;

        if (specifier.Contains('/') || specifier.Contains('\\') || specifier.Contains('#') ||
            specifier.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            specifier.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception(
                $"Module Error: cannot import 'dotnet:{specifier}': dotnet: specifiers name types or " +
                "namespaces, not assembly paths. Add the assembly to a sharpts.json manifest " +
                "(\"references\": [\"./libs/MyLib.dll\"]) or pass -r ./libs/MyLib.dll, then import the " +
                "type by name: import { Widget } from \"dotnet:MyLib.Widget\".");
        }

        if (specifier.Contains('`'))
        {
            throw new Exception(
                $"Module Error: cannot import 'dotnet:{specifier}': {DotNetInteropClassifier.ReasonOpenGeneric}. " +
                "Use a constructed friendly name such as List<number>, not CLR backtick syntax.");
        }

        Type type;
        try
        {
            type = ResolveExportCandidate(specifier, name, resolve);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Module Error: cannot import 'dotnet:{specifier}': {ex.Message}", ex);
        }

        string? unsupported = DotNetInteropClassifier.UnsupportedTypeReason(type);
        if (unsupported != null)
        {
            throw new Exception(
                $"Module Error: .NET type '{type.FullName}' cannot be imported: {unsupported}.");
        }

        return type;
    }

    private static Type ResolveExportCandidate(string specifier, string name, Func<string, Type?> resolve)
    {
        // Single-type form: the whole specifier is a type.
        var specType = ResolvePublic(specifier, resolve);
        if (specType != null)
        {
            if (name == DotNetTypeRegistry.GetFriendlySimpleName(specType)) return specType;

            // A different name against a type specifier can only mean a nested type.
            var nested = ResolvePublic($"{specifier}+{name}", resolve);
            if (nested != null) return nested;

            throw new Exception(
                $"Module Error: 'dotnet:{specifier}' resolves to .NET type '{specType.FullName}', " +
                $"which exports only '{specType.Name}' (and public nested types). '{name}' was not found.");
        }

        // Namespace form: resolve each named import as Namespace.Name.
        var type = ResolvePublic($"{specifier}.{name}", resolve);
        if (type != null) return type;

        throw new Exception(
            $"Module Error: cannot resolve '{name}' from 'dotnet:{specifier}': neither a type " +
            $"'{specifier}' nor a type '{specifier}.{name}' was found in any loaded assembly. " +
            "Check the fully-qualified name with: sharpts --gen-decl " + specifier);
    }

    /// <summary>
    /// Resolves a CLR type name, ignoring non-public types — reflection can find internal
    /// types, but the interop surface must not expose them.
    /// </summary>
    private static Type? ResolvePublic(string clrName, Func<string, Type?> resolve)
    {
        var type = DotNetTypeRegistry.ResolveFriendly(clrName, resolve);
        return type != null && (type.IsPublic || type.IsNestedPublic) ? type : null;
    }

    /// <summary>Best-effort example name for error messages: last dotted segment.</summary>
    private static string SuggestedName(string virtualPath)
    {
        string spec = virtualPath[Prefix.Length..];
        int lastDot = spec.LastIndexOf('.');
        return lastDot >= 0 && lastDot < spec.Length - 1 ? spec[(lastDot + 1)..] : spec;
    }
}
