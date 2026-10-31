using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Compilation;

/// <summary>Core identity translations owned by one metadata-only reference loader.</summary>
internal sealed class ExternalMetadataTypes
{
    private readonly MetadataLoadContext _context;
    private readonly Type? _nullableDefinition;
    private readonly Dictionary<Type, Type> _runtime = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, Type> _metadata = new(ReferenceEqualityComparer.Instance);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only actual core types in this MetadataLoadContext are inspected; no executable library is loaded or rooted.")]
    internal ExternalMetadataTypes(MetadataLoadContext context)
    {
        _context = context;
        var core = context.CoreAssembly ?? throw new InvalidOperationException("External metadata requires its actual core assembly.");
        _nullableDefinition = core.GetType(typeof(Nullable<>).FullName!, throwOnError: false);
        foreach (var runtime in new[] { typeof(void), typeof(object), typeof(bool), typeof(char), typeof(sbyte), typeof(byte),
                     typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float),
                     typeof(double), typeof(string), typeof(nint), typeof(nuint), typeof(TypedReference), typeof(ValueType),
                     typeof(Delegate), typeof(MulticastDelegate) })
            if (core.GetType(runtime.FullName!, throwOnError: false) is { } metadata)
            {
                _runtime.Add(metadata, runtime);
                _metadata.Add(runtime, metadata);
            }
    }

    // Context membership proves metadata ownership without probing AssemblyBuilder's
    // unsupported ReflectionOnly property or inspecting a foreign assembly.
    internal bool Owns(Assembly assembly) => _context.GetAssemblies().Any(candidate => ReferenceEquals(candidate, assembly));
    private bool Owns(Type value) => value.GetType().Assembly == typeof(MetadataLoadContext).Assembly && Owns(value.Assembly);
    internal bool IsNullable(Type value) => value.IsGenericType && ReferenceEquals(value.GetGenericTypeDefinition(), _nullableDefinition);
    internal Type RuntimeAtom(Type value) => _runtime.GetValueOrDefault(value.UnderlyingSystemType) ?? value;
    internal bool TryRuntimeAtom(Type value, out Type atom) => _runtime.TryGetValue(value, out atom!);

    internal bool TrySelectionType(Type value, out Type selected)
    {
        if (Owns(value)) { selected = value; return true; }
        if (_metadata.TryGetValue(value, out selected!)) return true;
        // Structural translations retain only already-proved element identities.
        // A user name, another context or a compiler-generated owner cannot supply one.
        if (value.GetType() == typeof(string).GetType() && (value.IsArray || value.IsByRef) &&
            TrySelectionType(value.GetElementType()!, out var element))
        {
            selected = value.IsByRef ? element.MakeByRefType() : value.IsSZArray ? ArrayShape(element, true, 1) : ArrayShape(element, false, value.GetArrayRank());
            return true;
        }
        selected = null!;
        return false;
    }
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The positive implementation-and-context guard admits only actual MetadataLoadContext element types. Their MakeArrayType override constructs metadata signature shapes and never generates runtime code or guest roots.")]
    internal Type ArrayShape(Type element, bool vector, int rank)
    {
        if (!Owns(element)) throw new ArgumentException("Array selection requires an actual type owned by this metadata context.", nameof(element));
        return vector ? element.MakeArrayType() : element.MakeArrayType(rank);
    }
    internal CoreSnapshot CaptureCoreAliases() => CoreSnapshot.Capture(this);

    /// <summary>Immutable exact identities from this loader's already-owned metadata core.</summary>
    internal sealed class CoreSnapshot
    {
        private readonly Dictionary<Type, Type> _atoms;
        private CoreSnapshot(ExternalMetadataTypes owner) => _atoms = new(owner._runtime, ReferenceEqualityComparer.Instance);
        internal static CoreSnapshot Capture(ExternalMetadataTypes owner) => new(owner);
        // No names, virtual equality or UnderlyingSystemType can authorize an alias.
        internal Type Normalize(Type value) => _atoms.GetValueOrDefault(value) ?? value;
    }
}
