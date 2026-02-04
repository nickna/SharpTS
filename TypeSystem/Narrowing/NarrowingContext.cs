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
    public static readonly NarrowingContext Empty = new(
        ImmutableDictionary<NarrowingPath, TypeInfo>.Empty,
        ImmutableHashSet<NarrowingPath>.Empty);

    private readonly ImmutableDictionary<NarrowingPath, TypeInfo> _narrowings;

    /// <summary>
    /// Paths that have been explicitly invalidated and should not be looked up in parent scopes.
    /// This is used when an assignment inside a loop should invalidate a narrowing from an outer scope.
    /// </summary>
    private readonly ImmutableHashSet<NarrowingPath> _invalidatedPaths;

    private NarrowingContext(
        ImmutableDictionary<NarrowingPath, TypeInfo> narrowings,
        ImmutableHashSet<NarrowingPath> invalidatedPaths)
    {
        _narrowings = narrowings;
        _invalidatedPaths = invalidatedPaths;
    }

    private NarrowingContext(ImmutableDictionary<NarrowingPath, TypeInfo> narrowings)
        : this(narrowings, ImmutableHashSet<NarrowingPath>.Empty)
    {
    }

    /// <summary>
    /// Gets the narrowed type for a path, or null if the path is not narrowed.
    /// </summary>
    public TypeInfo? GetNarrowing(NarrowingPath path)
    {
        return _narrowings.TryGetValue(path, out var type) ? type : null;
    }

    /// <summary>
    /// Checks if a path has been explicitly invalidated in this context.
    /// Used to stop upward lookup in the narrowing context stack.
    /// </summary>
    public bool IsInvalidated(NarrowingPath path)
    {
        foreach (var invalidatedPath in _invalidatedPaths)
        {
            if (path.IsAffectedByAssignmentTo(invalidatedPath))
                return true;
        }
        return false;
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
        // Adding a narrowing clears the invalidation for that path (if any)
        var newInvalidated = _invalidatedPaths.Remove(path);
        return new NarrowingContext(_narrowings.SetItem(path, narrowedType), newInvalidated);
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
        // Merge invalidated paths from both contexts
        var mergedInvalidated = _invalidatedPaths.Union(other._invalidatedPaths);
        return new NarrowingContext(builder.ToImmutable(), mergedInvalidated);
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

        // Also add to invalidated paths so parent scope lookups will be blocked
        var newInvalidatedPaths = _invalidatedPaths.Add(assignedPath);

        return new NarrowingContext(builder.ToImmutable(), newInvalidatedPaths);
    }

    /// <summary>
    /// Returns a new context with the specified path removed (but not descendants).
    /// Used for precise invalidation when only a specific narrowing should be removed.
    /// </summary>
    public NarrowingContext Remove(NarrowingPath path)
    {
        return _narrowings.ContainsKey(path)
            ? new NarrowingContext(_narrowings.Remove(path), _invalidatedPaths)
            : this;
    }

    /// <summary>
    /// Returns a new context with all property narrowings invalidated for paths
    /// that have the given base path as their root.
    /// Used when an object is passed to a function that might mutate it.
    /// </summary>
    /// <param name="basePath">The base path whose property narrowings should be invalidated.</param>
    public NarrowingContext InvalidatePropertiesOf(NarrowingPath basePath)
        => InvalidatePropertiesOf(basePath, shouldSkip: null);

    /// <summary>
    /// Returns a new context with property narrowings invalidated for paths
    /// that have the given base path as their root, except for properties
    /// where the predicate returns true (e.g., readonly properties).
    /// </summary>
    /// <param name="basePath">The base path whose property narrowings should be invalidated.</param>
    /// <param name="shouldSkip">A predicate that returns true for property names that should NOT be invalidated.</param>
    public NarrowingContext InvalidatePropertiesOf(NarrowingPath basePath, Func<NarrowingPath, string, bool>? shouldSkip)
    {
        var builder = _narrowings.ToBuilder();
        var toRemove = new List<NarrowingPath>();

        foreach (var path in _narrowings.Keys)
        {
            // Only invalidate property narrowings (depth > 0) that have this base
            // Don't invalidate narrowings on the variable itself
            if (path.Depth > 0 && basePath.IsPrefixOf(path))
            {
                // Check if this is a direct property access that should be skipped
                if (path is NarrowingPath.PropertyAccess propAccess &&
                    propAccess.Base.Equals(basePath) &&
                    shouldSkip != null &&
                    shouldSkip(basePath, propAccess.Property))
                {
                    // Skip this readonly property
                    continue;
                }

                // For nested property paths (e.g., obj.inner.value), check if any
                // property in the chain is readonly - if so, we can keep it
                if (shouldSkip != null && IsProtectedByReadonlyAncestor(basePath, path, shouldSkip))
                {
                    continue;
                }

                toRemove.Add(path);
            }
        }

        foreach (var path in toRemove)
        {
            builder.Remove(path);
        }

        // Also add the removed paths to invalidatedPaths to block parent scope lookups
        var newInvalidatedPaths = _invalidatedPaths;
        foreach (var path in toRemove)
        {
            newInvalidatedPaths = newInvalidatedPaths.Add(path);
        }

        return builder.Count == _narrowings.Count && toRemove.Count == 0
            ? this  // No changes
            : new NarrowingContext(builder.ToImmutable(), newInvalidatedPaths);
    }

    /// <summary>
    /// Checks if a path is protected from invalidation by having a readonly property in its chain.
    /// For example, if obj.inner is readonly, then obj.inner.value is also protected.
    /// </summary>
    private static bool IsProtectedByReadonlyAncestor(
        NarrowingPath basePath,
        NarrowingPath targetPath,
        Func<NarrowingPath, string, bool> shouldSkip)
    {
        // Walk from basePath towards targetPath, checking each property access
        var current = targetPath;
        var ancestors = new List<(NarrowingPath parent, string property)>();

        // Build the chain from target back to base
        while (current is NarrowingPath.PropertyAccess propAccess && !current.Equals(basePath))
        {
            ancestors.Add((propAccess.Base, propAccess.Property));
            current = propAccess.Base;
        }

        // Check each ancestor - if any is readonly, the target is protected
        // Check from base towards target (reverse order)
        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            var (parent, property) = ancestors[i];
            if (shouldSkip(parent, property))
            {
                return true;
            }
        }

        return false;
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
        if (a.IsEmpty && a._invalidatedPaths.IsEmpty) return Empty;
        if (b.IsEmpty && b._invalidatedPaths.IsEmpty) return Empty;

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

        // Merge invalidated paths - a path is invalidated if it's invalidated in either branch
        var mergedInvalidated = a._invalidatedPaths.Union(b._invalidatedPaths);

        return result.Count == 0 && mergedInvalidated.IsEmpty
            ? Empty
            : new NarrowingContext(result.ToImmutable(), mergedInvalidated);
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
