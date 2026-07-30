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
    private const string ManagedBuildRequiredMessage =
        "Declaration discovery is not available in the native SharpTS build — use the managed build.";

    private readonly TypeInspector _inspector = new();

    /// <summary>
    /// Resolves <paramref name="input"/> as a type, then a namespace, then an assembly path, and
    /// builds the matching report. Throws <see cref="ArgumentException"/> if nothing resolves.
    /// </summary>
    public DiscoveryReport Generate(string input)
    {
        EnsureManagedBuild();

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
        EnsureManagedBuild();

        var metadata = _inspector.Inspect(type);
        var typeReport = BuildTypeReport(metadata, type);
        return new DiscoveryReport(DiscoveryKind.TypeDetail, type.FullName ?? type.Name, Type: typeReport);
    }

    /// <summary>Builds a table-of-contents report for every public type in an assembly file.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Declaration discovery rejects Native AOT before loading or enumerating runtime assemblies.")]
    public DiscoveryReport GenerateForAssembly(string assemblyPath)
    {
        EnsureManagedBuild();

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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Declaration discovery rejects Native AOT before enumerating runtime assemblies.")]
    public DiscoveryReport? GenerateForNamespace(string ns)
    {
        EnsureManagedBuild();

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
            ? $"import {{ {Runtime.DotNet.DotNetTypeRegistry.GetFriendlySimpleName(type)} }} from " +
              $"\"dotnet:{Runtime.DotNet.DotNetTypeRegistry.GetFriendlyFullName(type)}\";"
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
                members.Add(BuildCallableReport(
                    "Constructors", "constructor", ctor.Parameters,
                    returnType: null, isConstructor: true));

            foreach (var prop in metadata.StaticProperties)
                members.Add(BuildPropertyReport("Static properties", prop, isStatic: true));

            foreach (var prop in metadata.Properties)
                members.Add(BuildPropertyReport("Instance properties", prop, isStatic: false));

            foreach (var method in metadata.StaticMethods)
                members.Add(BuildCallableReport(
                    "Static methods", method.TypeScriptName, method.Parameters,
                    method.ReturnType, isStatic: true,
                    genericParameters: method.GenericParameters));

            foreach (var method in metadata.Methods)
                members.Add(BuildCallableReport(
                    "Instance methods", method.TypeScriptName, method.Parameters,
                    method.ReturnType,
                    genericParameters: method.GenericParameters));
        }

        return new TypeReport(metadata.FullName, metadata.SimpleName, kind, importLine, typeReason, members);
    }

    /// <summary>Builds a report line for a constructor or method (a callable with parameters).</summary>
    private static MemberReport BuildCallableReport(
        string category,
        string name,
        List<ParameterMetadata> parameters,
        Type? returnType,
        bool isStatic = false,
        bool isConstructor = false,
        List<Type>? genericParameters = null)
    {
        string prefix = isStatic ? "static " : "";
        string typeParameters = genericParameters is { Count: > 0 }
            ? $"<{string.Join(", ", genericParameters.Select(p => p.Name))}>"
            : "";
        bool lowerByRef = returnType != null;
        string paramText = string.Join(", ", parameters
            .Where(p => !lowerByRef || !(p.IsByRef && p.IsOut))
            .Select(DescribeInteropInput));
        string returnText = returnType == null ? "" : $": {DescribeInteropReturn(returnType, parameters)}";
        string signature = $"{prefix}{name}{typeParameters}({paramText}){returnText}";

        // A member is usable only if every parameter slot and the return slot are marshalable.
        string? reason = null;
        foreach (var p in parameters)
        {
            if (isConstructor && p.ParameterType.IsByRef)
            {
                reason = DotNetInteropClassifier.ReasonByRefConstructor;
            }
            else if (genericParameters is { Count: > 0 })
            {
                reason = DotNetInteropClassifier.UnsupportedGenericMethodSlotReason(
                    p.ParameterType, genericParameters, isParameter: true);
            }
            else
            {
                reason = lowerByRef
                    ? DotNetInteropClassifier.UnsupportedParameterReason(p.ParameterType)
                    : DotNetInteropClassifier.UnsupportedSlotReason(p.ParameterType);
            }
            if (reason != null) break;
        }
        if (reason == null && returnType != null)
        {
            reason = genericParameters is { Count: > 0 }
                ? DotNetInteropClassifier.UnsupportedGenericMethodSlotReason(
                    returnType, genericParameters, isParameter: false)
                : DotNetInteropClassifier.UnsupportedSlotReason(returnType);
        }

        return new MemberReport(category, signature, reason == null, reason);
    }

    private static MemberReport BuildPropertyReport(string category, PropertyMetadata prop, bool isStatic)
    {
        string prefix = isStatic ? "static " : "";
        string accessors = prop.CanWrite ? "{ get; set; }" : "{ get; }";
        string member = prop.IsIndexer
            ? $"[{string.Join(", ", prop.IndexParameters.Select(DotNetTypeMapper.DescribeParameter))}]"
            : prop.TypeScriptName;
        string signature = $"{prefix}{member}: {DotNetTypeMapper.Describe(prop.PropertyType)}   {accessors}";
        string? reason = prop.IsIndexer && prop.IndexParameters.Count != 1
            ? DotNetInteropClassifier.ReasonMultiParameterIndexer
            : DotNetInteropClassifier.UnsupportedSlotReason(prop.PropertyType);
        if (reason == null)
        {
            foreach (var parameter in prop.IndexParameters)
            {
                reason = DotNetInteropClassifier.UnsupportedSlotReason(parameter.ParameterType);
                if (reason != null) break;
            }
        }
        return new MemberReport(category, signature, reason == null, reason);
    }

    private static string DescribeInteropInput(ParameterMetadata parameter)
    {
        Type type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;
        string name = DotNetTypeMapper.ToTypeScriptPropertyName(parameter.Name);
        string optional = parameter.IsOptional ? "?" : "";
        return $"{name}{optional}: {DotNetTypeMapper.Describe(type)}";
    }

    private static string DescribeInteropReturn(Type returnType, List<ParameterMetadata> parameters)
    {
        var outputs = parameters
            .Where(p => p.IsByRef && !p.IsIn)
            .Select(p => DotNetTypeMapper.Describe(p.ParameterType.GetElementType()!))
            .ToList();
        if (outputs.Count == 0)
            return DotNetTypeMapper.Describe(returnType);
        if (returnType != typeof(void))
            outputs.Insert(0, DotNetTypeMapper.Describe(returnType));
        return $"[{string.Join(", ", outputs)}]";
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "Declaration discovery rejects Native AOT before resolving user-supplied runtime type names.")]
    private static Type? ResolveType(string typeName)
    {
        Type? type = Runtime.DotNet.DotNetTypeRegistry.ResolveFriendly(typeName);
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

    private static void EnsureManagedBuild()
    {
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException(ManagedBuildRequiredMessage);
    }
}
