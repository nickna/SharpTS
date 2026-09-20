using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

/// <summary>
/// Owns completed CLR synthesis results and their reference-identity associations.
/// Weak keys allow unused collectible types and checked declarations to be released.
/// </summary>
internal sealed class DotNetSynthesisCache(Func<Type, TypeInfo.Class> build)
{
    private ConditionalWeakTable<Type, TypeInfo.Class> _current = new();
    private readonly ConditionalWeakTable<TypeInfo.Class, Type> _identities = new();

    public TypeInfo.Class GetOrCreate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Volatile.Read(ref _current).GetValue(type, Create);
    }

    private TypeInfo.Class Create(Type type)
    {
        var result = build(type);
        ArgumentNullException.ThrowIfNull(result);
        // Publish the reverse association before making a completed declaration
        // visible to importers, including callers racing with a cache reset.
        _identities.Add(result, type);
        return result;
    }

    public bool TryGetClrType(TypeInfo.Class declaration, out Type type) =>
        _identities.TryGetValue(declaration, out type!);

    // Existing checked declarations retain their associations. Only subsequent
    // synthesis requests move to the new generation; in-flight callers finish
    // against the generation they already captured.
    public void Reset() => Interlocked.Exchange(ref _current, new());
}
