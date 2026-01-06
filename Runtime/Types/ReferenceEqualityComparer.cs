using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Equality comparer that implements JavaScript-style equality semantics for Map/Set keys.
/// </summary>
/// <remarks>
/// - Primitives (string, double, bool, null): use value equality
/// - Objects (SharpTSObject, SharpTSArray, SharpTSInstance, etc.): use reference equality
/// This matches JavaScript's behavior where object keys in Map/Set are compared by reference.
/// </remarks>
public class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    private ReferenceEqualityComparer() { }

    public new bool Equals(object? x, object? y)
    {
        // Handle null cases
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        // Primitives use value equality (string, number, boolean)
        if (x is string || x is double || x is bool)
            return object.Equals(x, y);

        // BigInt uses value equality
        if (x is SharpTSBigInt bigX && y is SharpTSBigInt bigY)
            return bigX.Value == bigY.Value;

        // Symbol uses identity (reference equality)
        if (x is SharpTSSymbol || y is SharpTSSymbol)
            return ReferenceEquals(x, y);

        // All other objects (SharpTSObject, SharpTSArray, SharpTSInstance, etc.) use reference equality
        return ReferenceEquals(x, y);
    }

    public int GetHashCode(object obj)
    {
        if (obj is null) return 0;

        // Primitives use value-based hash
        if (obj is string || obj is double || obj is bool)
            return obj.GetHashCode();

        // BigInt uses value-based hash
        if (obj is SharpTSBigInt bigInt)
            return bigInt.Value.GetHashCode();

        // All other objects use identity hash (reference-based)
        return RuntimeHelpers.GetHashCode(obj);
    }
}
