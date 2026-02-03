using System.Collections.Immutable;

namespace SharpTS.TypeSystem.Narrowing;

/// <summary>
/// Tracks type narrowings for paths in the current control flow scope.
/// Immutable - all operations return new contexts.
/// </summary>
/// <remarks>
/// This replaces the simple variable-name-based narrowing in TypeEnvironment
/// with a path-based approach that supports property access narrowing.
///
/// Example:
/// <code>
/// if (obj.prop !== null) {
///     // Context contains: obj.prop -> T (without null)
///     use(obj.prop);  // Narrowed type is used
/// }
/// </code>
/// </remarks>
public sealed class NarrowingContext
{
    /// <summary>
    /// Empty context with no narrowings.
    /// </summary>
    public static readonly NarrowingContext Empty = new(ImmutableDictionary<NarrowingPath, TypeInfo>.Empty);

    private readonly ImmutableDictionary<NarrowingPath, TypeInfo> _narrowings;

    private NarrowingContext(ImmutableDictionary<NarrowingPath, TypeInfo> narrowings)
    {
        _narrowings = narrowings;
    }

    /// <summary>
    /// Gets the narrowed type for a path, or null if the path is not narrowed.
    /// </summary>
    public TypeInfo? GetNarrowing(NarrowingPath path)
    {
        return _narrowings.TryGetValue(path, out var type) ? type : null;
    }

    /// <summary>
    /// Checks if this context has any narrowings.
    /// </summary>
    public bool IsEmpty => _narrowings.IsEmpty;

    /// <summary>
    /// Gets all narrowings in this context.
    /// </summary>
    public IEnumerable<KeyValuePair<NarrowingPath, TypeInfo>> Narrowings => _narrowings;

    /// <summary>
    /// Returns a new context with the given narrowing applied.
    /// If a narrowing already exists for the path, it is replaced.
    /// </summary>
    public NarrowingContext WithNarrowing(NarrowingPath path, TypeInfo narrowedType)
    {
        return new NarrowingContext(_narrowings.SetItem(path, narrowedType));
    }

    /// <summary>
    /// Returns a new context with narrowings applied from another context.
    /// Existing narrowings are replaced if the other context has narrowings for the same paths.
    /// </summary>
    public NarrowingContext WithNarrowings(NarrowingContext other)
    {
        if (other.IsEmpty) return this;
        if (this.IsEmpty) return other;

        var builder = _narrowings.ToBuilder();
        foreach (var (path, type) in other._narrowings)
        {
            builder[path] = type;
        }
        return new NarrowingContext(builder.ToImmutable());
    }

    /// <summary>
    /// Returns a new context with all narrowings for the given path and its descendants removed.
    /// Used when a path is assigned to.
    /// </summary>
    /// <param name="assignedPath">The path that was assigned.</param>
    public NarrowingContext Invalidate(NarrowingPath assignedPath)
    {
        var builder = _narrowings.ToBuilder();
        var toRemove = new List<NarrowingPath>();

        foreach (var path in _narrowings.Keys)
        {
            if (path.IsAffectedByAssignmentTo(assignedPath))
            {
                toRemove.Add(path);
            }
        }

        foreach (var path in toRemove)
        {
            builder.Remove(path);
        }

        return builder.Count == _narrowings.Count
            ? this  // No changes
            : new NarrowingContext(builder.ToImmutable());
    }

    /// <summary>
    /// Returns a new context with the specified path removed (but not descendants).
    /// Used for precise invalidation when only a specific narrowing should be removed.
    /// </summary>
    public NarrowingContext Remove(NarrowingPath path)
    {
        return _narrowings.ContainsKey(path)
            ? new NarrowingContext(_narrowings.Remove(path))
            : this;
    }

    /// <summary>
    /// Merges two contexts at a control flow join point.
    /// A narrowing is preserved only if it exists in both contexts with compatible types.
    /// </summary>
    /// <remarks>
    /// At join points (e.g., after if/else), we need to compute the intersection
    /// of narrowings from both branches. If a path is narrowed differently in each
    /// branch, we keep the union of the types (widening).
    /// </remarks>
    public static NarrowingContext Merge(NarrowingContext a, NarrowingContext b)
    {
        if (a.IsEmpty) return Empty;
        if (b.IsEmpty) return Empty;

        var result = ImmutableDictionary.CreateBuilder<NarrowingPath, TypeInfo>();

        foreach (var (path, typeA) in a._narrowings)
        {
            if (b._narrowings.TryGetValue(path, out var typeB))
            {
                // Path is narrowed in both branches
                if (typeA.Equals(typeB))
                {
                    // Same type - keep it
                    result[path] = typeA;
                }
                else
                {
                    // Different types - create union (widening)
                    // This handles cases like:
                    // if (x) { narrow to A } else { narrow to B }
                    // After: x is A | B
                    result[path] = new TypeInfo.Union([typeA, typeB]);
                }
            }
            // Path only narrowed in 'a' - don't include (not narrowed in 'b' path)
        }

        return result.Count == 0 ? Empty : new NarrowingContext(result.ToImmutable());
    }

    /// <summary>
    /// Creates a context that represents the narrowings from one branch
    /// when the other branch always terminates (return/throw).
    /// </summary>
    /// <param name="survivingBranch">The narrowings from the branch that continues.</param>
    /// <param name="terminatingBranchNarrowings">The narrowings that were in effect when termination occurred.</param>
    /// <remarks>
    /// After: if (x === null) return;
    /// The narrowings from the "then" branch don't matter since it terminates,
    /// so we use the narrowings from the implicit "else" (the excluded types).
    /// </remarks>
    public static NarrowingContext AfterTerminatingBranch(
        NarrowingContext survivingBranch,
        NarrowingContext? excludedNarrowings)
    {
        // If there are excluded narrowings, apply them to the surviving context
        if (excludedNarrowings != null && !excludedNarrowings.IsEmpty)
        {
            return survivingBranch.WithNarrowings(excludedNarrowings);
        }
        return survivingBranch;
    }

    public override string ToString()
    {
        if (IsEmpty) return "NarrowingContext.Empty";
        var entries = string.Join(", ", _narrowings.Select(kv => $"{kv.Key}: {kv.Value}"));
        return $"NarrowingContext {{ {entries} }}";
    }
}
