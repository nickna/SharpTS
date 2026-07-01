using System.Reflection;

namespace SharpTS.Declaration;

/// <summary>
/// Builds a <see cref="DiscoveryReport"/> for a .NET type, namespace, or assembly — the engine
/// behind <c>--gen-decl</c>'s discovery/inspection mode (issue #1194). Unlike the older
/// TypeScript-source generator, it never has to produce valid TypeScript: it reports real CLR
/// signatures faithfully (<c>ReadOnlySpan&lt;char&gt;</c>, <c>out Guid</c>, …) and marks each
/// member usable or unsupported using <see cref="DotNetInteropClassifier"/> — the same rules the
/// runtime interop marshaller enforces, so the tool and the runtime can never disagree.
/// </summary>
public class DiscoveryGenerator
{
    private readonly TypeInspector _inspector = new();

    /// <summary>
    /// Resolves <paramref name="input"/> as a type, then a namespace, then an assembly path, and
    /// builds the matching report. Throws <see cref="ArgumentException"/> if nothing resolves.
    /// </summary>
    public DiscoveryReport Generate(string input)
    {
        // Assembly file path → table of contents for the whole assembly.
        if (input.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenerateForAssembly(input);
        }

        // A specific type → full member detail.
        Type? type = ResolveType(input);
        if (type != null)
        {
            return GenerateForType(type);
        }

        // Otherwise treat it as a namespace → table of contents.
        var toc = GenerateForNamespace(input);
        if (toc != null)
        {
            return toc;
        }

        throw new ArgumentException(
            $"'{input}' did not resolve to a .NET type, namespace, or assembly. " +
            "Check the name is fully qualified (e.g. System.Text.StringBuilder) and its assembly is loadable.");
    }

    /// <summary>Builds a full detail report for a single resolved type.</summary>
    public DiscoveryReport GenerateForType(Type type)
    {
        var metadata = _inspector.Inspect(type);
        var typeReport = BuildTypeReport(metadata, type);
        return new DiscoveryReport(DiscoveryKind.TypeDetail, type.FullName ?? type.Name, Type: typeReport);
    }

    /// <summary>Builds a table-of-contents report for every public type in an assembly file.</summary>
    public DiscoveryReport GenerateForAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load assembly: {ex.Message}", ex);
        }

        var entries = assembly.GetExportedTypes()
            .Where(t => !t.Name.StartsWith('<') && !t.IsNested)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(ToTocEntry)
            .ToList();

        return new DiscoveryReport(
            DiscoveryKind.TableOfContents,
            assemblyPath,
            Scope: assembly.GetName().Name ?? assemblyPath,
            Types: entries);
    }

    /// <summary>
    /// Builds a table-of-contents report for a namespace by scanning all loaded assemblies, or
    /// null if no loaded type lives in that namespace.
    /// </summary>
    public DiscoveryReport? GenerateForNamespace(string ns)
    {
        var entries = new List<TocEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch
            {
                continue; // Skip assemblies that can't be reflected over.
            }

            foreach (var type in types)
            {
                if (type.Namespace != ns || type.IsNested || type.Name.StartsWith('<'))
                    continue;
                if (!seen.Add(type.FullName ?? type.Name))
                    continue;
                entries.Add(ToTocEntry(type));
            }
        }

        if (entries.Count == 0)
            return null;

        entries.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return new DiscoveryReport(DiscoveryKind.TableOfContents, ns, Scope: ns, Types: entries);
    }

    // ── Report building ──────────────────────────────────────────────

    private static TocEntry ToTocEntry(Type type)
    {
        string? reason = DotNetInteropClassifier.UnsupportedTypeReason(type);
        return new TocEntry(type.FullName ?? type.Name, KindOf(type), reason == null, reason);
    }

    private static TypeReport BuildTypeReport(TypeMetadata metadata, Type type)
    {
        // KindOf reads the reflected type so structs read as "struct", static classes as
        // "static class", etc. — the same label the table-of-contents uses.
        string kind = KindOf(type);

        string? typeReason = DotNetInteropClassifier.UnsupportedTypeReason(type);
        string? importLine = typeReason == null
            ? $"import {{ {metadata.SimpleName} }} from \"dotnet:{metadata.FullName}\";"
            : null;

        var members = new List<MemberReport>();

        if (metadata.IsEnum)
        {
            foreach (var m in metadata.EnumMembers)
                members.Add(new MemberReport("Values", $"{m.Name} = {m.Value}", true, null));
        }
        else
        {
            foreach (var ctor in metadata.Constructors)
                members.Add(BuildCallableReport("Constructors", "constructor", ctor.Parameters, returnType: null));

            foreach (var prop in metadata.StaticProperties)
                members.Add(BuildPropertyReport("Static properties", prop, isStatic: true));

            foreach (var prop in metadata.Properties)
                members.Add(BuildPropertyReport("Instance properties", prop, isStatic: false));

            foreach (var method in metadata.StaticMethods)
                members.Add(BuildCallableReport("Static methods", method.TypeScriptName, method.Parameters, method.ReturnType, isStatic: true));

            foreach (var method in metadata.Methods)
                members.Add(BuildCallableReport("Instance methods", method.TypeScriptName, method.Parameters, method.ReturnType));
        }

        return new TypeReport(metadata.FullName, metadata.SimpleName, kind, importLine, typeReason, members);
    }

    /// <summary>Builds a report line for a constructor or method (a callable with parameters).</summary>
    private static MemberReport BuildCallableReport(
        string category, string name, List<ParameterMetadata> parameters, Type? returnType, bool isStatic = false)
    {
        string prefix = isStatic ? "static " : "";
        string paramText = string.Join(", ", parameters.Select(DotNetTypeMapper.DescribeParameter));
        string returnText = returnType == null ? "" : $": {DotNetTypeMapper.Describe(returnType)}";
        string signature = $"{prefix}{name}({paramText}){returnText}";

        // A member is usable only if every parameter slot and the return slot are marshalable.
        string? reason = null;
        foreach (var p in parameters)
        {
            reason = DotNetInteropClassifier.UnsupportedSlotReason(p.ParameterType);
            if (reason != null) break;
        }
        if (reason == null && returnType != null)
            reason = DotNetInteropClassifier.UnsupportedSlotReason(returnType);

        return new MemberReport(category, signature, reason == null, reason);
    }

    private static MemberReport BuildPropertyReport(string category, PropertyMetadata prop, bool isStatic)
    {
        string prefix = isStatic ? "static " : "";
        string accessors = prop.CanWrite ? "{ get; set; }" : "{ get; }";
        string signature = $"{prefix}{prop.TypeScriptName}: {DotNetTypeMapper.Describe(prop.PropertyType)}   {accessors}";
        string? reason = DotNetInteropClassifier.UnsupportedSlotReason(prop.PropertyType);
        return new MemberReport(category, signature, reason == null, reason);
    }

    private static string KindOf(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsAbstract && type.IsSealed) return "static class";
        if (type.IsValueType) return "struct";
        if (type.IsAbstract) return "abstract class";
        return "class";
    }

    // ── Type resolution ──────────────────────────────────────────────

    /// <summary>
    /// Resolves a type name the same way the runtime interop does (<see cref="Runtime.DotNet.DotNetTypeRegistry"/>),
    /// falling back to a handful of common BCL assembly qualifiers for convenience.
    /// </summary>
    private static Type? ResolveType(string typeName)
    {
        Type? type = Runtime.DotNet.DotNetTypeRegistry.Resolve(typeName);
        if (type != null)
            return type;

        string[] commonAssemblies =
        [
            "System.Runtime", "System.Console", "System.Collections",
            "System.Linq", "System.Private.CoreLib", "mscorlib", "System.Net.Http"
        ];

        foreach (var asmName in commonAssemblies)
        {
            try
            {
                type = Type.GetType($"{typeName}, {asmName}", throwOnError: false);
                if (type != null)
                    return type;
            }
            catch
            {
                // Continue trying other assemblies.
            }
        }

        return null;
    }
}
