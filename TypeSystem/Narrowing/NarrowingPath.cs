namespace SharpTS.TypeSystem.Narrowing;

/// <summary>
/// Represents a narrowable location in code - a variable or property access path.
/// Used for tracking type narrowings through control flow analysis.
/// </summary>
/// <remarks>
/// Examples:
/// - Variable "x" -> Variable("x")
/// - Property "x.prop" -> PropertyAccess(Variable("x"), "prop")
/// - Nested "x.a.b.c" -> PropertyAccess(PropertyAccess(PropertyAccess(Variable("x"), "a"), "b"), "c")
/// - Tuple element "x[0]" -> ElementAccess(Variable("x"), 0)
/// </remarks>
public abstract record NarrowingPath
{
    /// <summary>
    /// Gets the root variable name of this path.
    /// For "obj.prop.value", returns "obj".
    /// </summary>
    public abstract string RootVariable { get; }

    /// <summary>
    /// Gets the depth of this path (number of access steps from root).
    /// Variable has depth 0, obj.prop has depth 1, obj.a.b has depth 2.
    /// </summary>
    public abstract int Depth { get; }

    /// <summary>
    /// Checks if this path is a prefix of another path.
    /// Used to determine if an assignment invalidates a narrowing.
    /// e.g., "obj" is a prefix of "obj.prop.value"
    /// </summary>
    public bool IsPrefixOf(NarrowingPath other)
    {
        if (this.Equals(other)) return true;

        return other switch
        {
            PropertyAccess pa => IsPrefixOf(pa.Base),
            ElementAccess ea => IsPrefixOf(ea.Base),
            _ => false
        };
    }

    /// <summary>
    /// Checks if this path is affected by an assignment to the given path.
    /// A path is affected if the assigned path is a prefix (assigning parent invalidates children)
    /// or if this path is a prefix (assigning child may affect parent in discriminated unions).
    /// </summary>
    public bool IsAffectedByAssignmentTo(NarrowingPath assignedPath)
    {
        // If assigned path is a prefix of this, this is invalidated
        // e.g., assigning "obj" invalidates "obj.prop"
        if (assignedPath.IsPrefixOf(this)) return true;

        // If this is a prefix of assigned path, this may be affected
        // e.g., assigning "obj.kind" may affect narrowing on "obj" (discriminated unions)
        // For now, we're conservative and invalidate in this case too
        if (this.IsPrefixOf(assignedPath)) return true;

        return false;
    }

    /// <summary>
    /// Creates a string representation for debugging and error messages.
    /// </summary>
    public abstract override string ToString();

    /// <summary>
    /// Simple variable reference: x
    /// </summary>
    public sealed record Variable(string Name) : NarrowingPath
    {
        public override string RootVariable => Name;
        public override int Depth => 0;
        public override string ToString() => Name;
    }

    /// <summary>
    /// Property access: base.property
    /// </summary>
    public sealed record PropertyAccess(NarrowingPath Base, string Property) : NarrowingPath
    {
        public override string RootVariable => Base.RootVariable;
        public override int Depth => Base.Depth + 1;
        public override string ToString() => $"{Base}.{Property}";
    }

    /// <summary>
    /// Element access with literal numeric index: base[index]
    /// Used primarily for tuple narrowing.
    /// </summary>
    public sealed record ElementAccess(NarrowingPath Base, int Index) : NarrowingPath
    {
        public override string RootVariable => Base.RootVariable;
        public override int Depth => Base.Depth + 1;
        public override string ToString() => $"{Base}[{Index}]";
    }
}

/// <summary>
/// Comparer for NarrowingPath that uses structural equality.
/// Records already provide this, but this makes it explicit for dictionary usage.
/// </summary>
public sealed class NarrowingPathComparer : IEqualityComparer<NarrowingPath>
{
    public static readonly NarrowingPathComparer Instance = new();

    public bool Equals(NarrowingPath? x, NarrowingPath? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.Equals(y);
    }

    public int GetHashCode(NarrowingPath obj) => obj.GetHashCode();
}
