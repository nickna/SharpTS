using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Closed set of CLR types whose metadata and native code are available to the
/// Native AOT SharpTS process. Managed builds continue to use the open-world
/// reflection provider and do not consult this catalog.
/// </summary>
public interface INativeDotNetCatalog
{
    internal const DynamicallyAccessedMemberTypes InteropMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicEvents |
        DynamicallyAccessedMemberTypes.PublicNestedTypes;

    /// <summary>Resolves a CLR or SharpTS-friendly type name from the closed catalog.</summary>
    bool TryResolveType(
        string typeName,
        [NotNullWhen(true)] out Type? type);

    /// <summary>Returns whether the exact runtime type belongs to the catalog.</summary>
    bool Contains(Type type);

    /// <summary>Finds a registered constructed generic type without creating one at runtime.</summary>
    bool TryGetConstructedGeneric(
        Type genericDefinition,
        IReadOnlyList<Type> arguments,
        [NotNullWhen(true)] out Type? type);

    /// <summary>Finds a registered array type without creating one at runtime.</summary>
    bool TryGetArrayType(
        Type elementType,
        [NotNullWhen(true)] out Type? type);

    /// <summary>
    /// Extracts the managed assembly payload closure selected by a custom host.
    /// The official catalog has no payloads and returns an empty list.
    /// </summary>
    IReadOnlyList<string> ExtractAssemblyPayloads(string destinationDirectory);
}

/// <summary>
/// Builder used by generated Native AOT host code. Calling <see cref="Add{T}"/>
/// is the build-time contract that roots the selected type and all public member
/// metadata required by the existing SharpTS binder.
/// </summary>
public sealed class NativeDotNetCatalogBuilder
{
    private readonly Dictionary<string, Type> _names = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _types = [];
    private readonly Dictionary<string, string> _assemblyPayloads =
        new(StringComparer.OrdinalIgnoreCase);

    public NativeDotNetCatalogBuilder Add<
        [DynamicallyAccessedMembers(INativeDotNetCatalog.InteropMembers)] T>(
        params string[] aliases)
    {
        AddCore(typeof(T), aliases);
        return this;
    }

    public NativeDotNetCatalogBuilder Add(
        [DynamicallyAccessedMembers(INativeDotNetCatalog.InteropMembers)] Type type,
        params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(type);
        AddCore(type, aliases);
        return this;
    }

    public NativeDotNetCatalogBuilder AddEnum<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        params string[] aliases) where T : struct, Enum
    {
        AddCore(typeof(T), aliases);
        return this;
    }

    public NativeDotNetCatalogBuilder AddDelegate<T>(params string[] aliases)
        where T : Delegate
    {
        AddCore(typeof(T), aliases);
        return this;
    }

    public NativeDotNetCatalogBuilder AddStaticEventSurface<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.PublicEvents)] T>(
        params string[] aliases)
    {
        AddCore(typeof(T), aliases);
        return this;
    }

    /// <summary>
    /// Registers a managed DLL embedded in the executable host. Generated host
    /// code uses this for the selected interop assemblies and their dependencies.
    /// </summary>
    public NativeDotNetCatalogBuilder AddAssemblyPayload(
        string fileName,
        string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        _assemblyPayloads[Path.GetFileName(fileName)] = resourceName;
        return this;
    }

    /// <summary>Adds the curated BCL profile shipped by the official native binary.</summary>
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The only enum shapes rooted here are compile-time-known closed enum types; Native AOT generates their value arrays.")]
    public NativeDotNetCatalogBuilder AddDefaultTypes() =>
        Add<string>("System.String")
            .Add<System.Text.StringBuilder>()
            .Add<Guid>()
            .Add<DateTime>()
            .Add<TimeSpan>()
            .Add(typeof(Convert))
            .Add(typeof(Math))
            .Add(typeof(Environment))
            .Add<Uri>()
            .Add(typeof(Console))
            .AddStaticEventSurface<AppDomain>()
            .Add<System.Threading.Tasks.Task>()
            .AddEnum<DayOfWeek>()
            .AddEnum<Environment.SpecialFolder>()
            .AddDelegate<EventHandler>()
            .AddDelegate<UnhandledExceptionEventHandler>()
            .AddDelegate<Action<double>>()
            .AddDelegate<Predicate<double>>()
            .AddDelegate<Comparison<double>>()
            .Add<List<double>>("System.Collections.Generic.List<number>")
            .Add<Dictionary<string, double>>("System.Collections.Generic.Dictionary<string, number>");

    public INativeDotNetCatalog Build() =>
        new NativeDotNetCatalog(
            new Dictionary<string, Type>(_names, StringComparer.Ordinal),
            [.. _types],
            new Dictionary<string, string>(_assemblyPayloads, StringComparer.OrdinalIgnoreCase));

    private void AddCore(
        Type type,
        IReadOnlyList<string> aliases)
    {
        _types.Add(type);
        if (type.FullName is { } fullName)
            _names[fullName] = type;
        _names[type.Name] = type;
        foreach (string alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                _names[alias.Trim()] = type;
        }
    }
}

internal sealed class NativeDotNetCatalog(
    Dictionary<string, Type> names,
    HashSet<Type> types,
    Dictionary<string, string> assemblyPayloads) : INativeDotNetCatalog
{
    public bool TryResolveType(
        string typeName,
        [NotNullWhen(true)] out Type? type) => names.TryGetValue(typeName.Trim(), out type);

    public bool Contains(Type type) => types.Contains(type);

    public bool TryGetConstructedGeneric(
        Type genericDefinition,
        IReadOnlyList<Type> arguments,
        [NotNullWhen(true)] out Type? type)
    {
        foreach (Type candidate in types)
        {
            if (!candidate.IsGenericType ||
                candidate.GetGenericTypeDefinition() != genericDefinition)
            {
                continue;
            }

            Type[] candidateArguments = candidate.GetGenericArguments();
            if (candidateArguments.Length == arguments.Count &&
                candidateArguments.AsSpan().SequenceEqual(arguments.ToArray()))
            {
                type = candidate;
                return true;
            }
        }

        type = null;
        return false;
    }

    public bool TryGetArrayType(
        Type elementType,
        [NotNullWhen(true)] out Type? type)
    {
        type = types.FirstOrDefault(candidate =>
            candidate.IsArray && candidate.GetArrayRank() == 1 &&
            candidate.GetElementType() == elementType);
        return type != null;
    }

    public IReadOnlyList<string> ExtractAssemblyPayloads(string destinationDirectory)
    {
        if (assemblyPayloads.Count == 0)
            return [];

        string fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);
        Assembly hostAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "The Native .NET interop host entry assembly could not be resolved.");
        var extracted = new List<string>(assemblyPayloads.Count);

        foreach ((string fileName, string resourceName) in assemblyPayloads)
        {
            using Stream payload = hostAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Native .NET interop payload resource '{resourceName}' is missing.");
            string destinationPath = Path.Combine(fullDestination, fileName);
            if (!global::SharpTS.Runtime.EmbeddedManagedRuntime.TryExtractTo(
                    payload, destinationPath, out string? extractionError))
            {
                throw new IOException(
                    $"Could not extract native .NET interop payload '{fileName}': {extractionError}");
            }
            extracted.Add(destinationPath);
        }

        return extracted;
    }
}

/// <summary>Process-wide Native AOT catalog selected by the executable host.</summary>
public static class NativeDotNetInterop
{
    private static INativeDotNetCatalog? _catalog;

    public static INativeDotNetCatalog? Catalog => Volatile.Read(ref _catalog);

    /// <summary>
    /// Installs the immutable catalog before parsing or executing TypeScript.
    /// Reinstalling the same instance is harmless; replacing it after binding has
    /// started is rejected so process-wide caches cannot mix catalog identities.
    /// </summary>
    public static void Configure(INativeDotNetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        INativeDotNetCatalog? existing = Interlocked.CompareExchange(ref _catalog, catalog, null);
        if (existing != null && !ReferenceEquals(existing, catalog))
        {
            throw new InvalidOperationException(
                "The Native .NET interop catalog has already been configured for this process.");
        }
    }

    internal static bool IsAllowed(Type type) =>
        Catalog is { } catalog &&
        (catalog.Contains(type) || IsIntrinsic(type));

    internal static bool IsIntrinsic(Type type) =>
        type.IsGenericParameter ||
        (type.ContainsGenericParameters && !type.IsGenericTypeDefinition) ||
        type == typeof(void) || type == typeof(object) || type == typeof(string) ||
        type == typeof(bool) || type == typeof(char) ||
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);
}

/// <summary>Curated catalog embedded in the official Native AOT release binary.</summary>
public static class DefaultNativeDotNetCatalog
{
    public static INativeDotNetCatalog Instance { get; } = Create();

    private static INativeDotNetCatalog Create() =>
        new NativeDotNetCatalogBuilder()
            .AddDefaultTypes()
            .Build();
}
