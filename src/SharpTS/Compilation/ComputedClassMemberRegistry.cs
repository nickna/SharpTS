using System.Collections.ObjectModel;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Owns computed class member declarations until their bodies have been emitted.
/// Type identity separates classes with equal names; ordered read-only views retain
/// source order for definition-time registration and expose forward references.
/// </summary>
internal sealed class ComputedClassMemberRegistry
{
    private readonly Dictionary<TypeBuilder, IReadOnlyList<(Stmt.Function Method, Expr Key, MethodBuilder Builder)>> _methods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeBuilder, List<(Stmt.Accessor Accessor, MethodBuilder Method)>> _accessors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeBuilder, ReadOnlyCollection<(Stmt.Accessor Accessor, MethodBuilder Method)>> _accessorViews = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MethodBuilder> _builders = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MethodBuilder> _emitted = new(ReferenceEqualityComparer.Instance);

    internal bool IsComplete { get; private set; }

    internal bool HasMethods(TypeBuilder owner) => _methods.ContainsKey(owner);

    internal IReadOnlyList<(Stmt.Function Method, Expr Key, MethodBuilder Builder)> GetMethods(TypeBuilder owner)
        => _methods.TryGetValue(owner, out var methods) ? methods : [];

    internal IReadOnlyList<(Stmt.Accessor Accessor, MethodBuilder Method)> GetAccessors(TypeBuilder owner)
        => _accessorViews.TryGetValue(owner, out var accessors) ? accessors : [];

    internal void DeclareMethods(TypeBuilder owner, IEnumerable<(Stmt.Function Method, Expr Key, MethodBuilder Builder)> methods)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(methods);
        var snapshot = methods.ToArray();
        var sources = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        var builders = new HashSet<MethodBuilder>(ReferenceEqualityComparer.Instance);
        if (_methods.ContainsKey(owner))
            throw new InvalidOperationException("Computed class methods have already been declared.");
        foreach (var (method, key, builder) in snapshot)
        {
            if (method is null || key is null || builder is null || method.Body is null
                || !ReferenceEquals(method.ComputedKey, key) || method.IsStatic != builder.IsStatic
                || builder.DeclaringType != owner || _builders.Contains(builder)
                || !sources.Add(method) || !builders.Add(builder))
                throw new InvalidOperationException("Invalid or duplicate computed class method declaration.");
        }
        _methods.Add(owner, Array.AsReadOnly(snapshot));
        _builders.UnionWith(builders);
    }

    internal void DeclareAccessor(TypeBuilder owner, Stmt.Accessor accessor, MethodBuilder builder)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(builder);
        if (accessor.ComputedKey is null || accessor.Kind.Type is not (TokenType.GET or TokenType.SET)
            || builder.DeclaringType != owner || accessor.IsStatic != builder.IsStatic
            || _builders.Contains(builder)
            || GetAccessors(owner).Any(entry => ReferenceEquals(entry.Accessor, accessor)))
            throw new InvalidOperationException("Invalid or duplicate computed class accessor declaration.");
        if (!_accessors.TryGetValue(owner, out var accessors))
        {
            accessors = [];
            _accessors.Add(owner, accessors);
            _accessorViews.Add(owner, accessors.AsReadOnly());
        }
        accessors.Add((accessor, builder));
        _builders.Add(builder);
    }

    internal void MarkBodyEmitted(MethodBuilder builder)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(builder);
        if (!_builders.Contains(builder) || builder.GetILGenerator().ILOffset == 0 || !_emitted.Add(builder))
            throw new InvalidOperationException("The computed class member is foreign, empty, or already emitted.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        if (_emitted.Count != _builders.Count)
            throw new InvalidOperationException("Not all computed class member bodies have been emitted.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Computed class member metadata is complete.");
    }
}
