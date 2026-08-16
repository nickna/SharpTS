namespace SharpTS.TypeSystem;

/// <summary>
/// Tuple and array compatibility checking.
/// </summary>
public partial class TypeChecker
{
    private bool IsTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        // If either has unresolved spreads, use variadic comparison
        if (expected.HasSpread || actual.HasSpread)
            return IsVariadicTupleCompatible(expected, actual);

        // Get element types for comparison
        var expectedTypes = expected.Elements.Select(e => e.Type).ToList();
        var actualTypes = actual.Elements.Select(e => e.Type).ToList();

        // Actual must have at least the required elements
        if (actualTypes.Count < expected.RequiredCount) return false;

        // If expected has fixed length (no rest), actual cannot be longer
        if (expected.MaxLength != null && actualTypes.Count > expected.MaxLength) return false;

        // Check element type compatibility for overlapping positions
        int minLen = Math.Min(expectedTypes.Count, actualTypes.Count);
        for (int i = 0; i < minLen; i++)
        {
            if (!IsCompatible(expectedTypes[i], actualTypes[i]))
                return false;
        }

        // If expected has rest, check remaining actual elements against rest type
        if (expected.RestElementType != null)
        {
            for (int i = expectedTypes.Count; i < actualTypes.Count; i++)
            {
                if (!IsCompatible(expected.RestElementType, actualTypes[i]))
                    return false;
            }
            // If actual also has rest, check rest compatibility
            if (actual.RestElementType != null &&
                !IsCompatible(expected.RestElementType, actual.RestElementType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks compatibility for variadic tuple types (tuples with spread elements).
    /// </summary>
    private bool IsVariadicTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Tuple actual)
    {
        // For variadic tuples, we compare concrete elements and check spread compatibility
        // This is a simplified implementation - full TypeScript semantics are complex

        // Get leading elements before first spread
        var expectedLeading = expected.Elements.TakeWhile(e => e.Kind != TupleElementKind.Spread).ToList();
        var actualLeading = actual.Elements.TakeWhile(e => e.Kind != TupleElementKind.Spread).ToList();

        // Check leading element compatibility
        int leadingMin = Math.Min(expectedLeading.Count, actualLeading.Count);
        for (int i = 0; i < leadingMin; i++)
        {
            if (!IsCompatible(expectedLeading[i].Type, actualLeading[i].Type))
                return false;
        }

        // Get trailing elements after last spread
        var expectedTrailing = expected.Elements.AsEnumerable().Reverse().TakeWhile(e => e.Kind != TupleElementKind.Spread).Reverse().ToList();
        var actualTrailing = actual.Elements.AsEnumerable().Reverse().TakeWhile(e => e.Kind != TupleElementKind.Spread).Reverse().ToList();

        // Check trailing element compatibility
        int trailingMin = Math.Min(expectedTrailing.Count, actualTrailing.Count);
        for (int i = 0; i < trailingMin; i++)
        {
            int expectedIdx = expectedTrailing.Count - 1 - i;
            int actualIdx = actualTrailing.Count - 1 - i;
            if (!IsCompatible(expectedTrailing[expectedIdx].Type, actualTrailing[actualIdx].Type))
                return false;
        }

        // For spreads in between, check type compatibility
        var expectedSpreads = expected.Elements.Where(e => e.Kind == TupleElementKind.Spread).Select(e => e.Type).ToList();
        var actualSpreads = actual.Elements.Where(e => e.Kind == TupleElementKind.Spread).Select(e => e.Type).ToList();

        // Simple check: if both have spreads, they should be compatible
        if (expectedSpreads.Count > 0 && actualSpreads.Count > 0)
        {
            // Each actual spread should be compatible with corresponding expected spread
            for (int i = 0; i < expectedSpreads.Count && i < actualSpreads.Count; i++)
            {
                if (!IsCompatible(expectedSpreads[i], actualSpreads[i]))
                    return false;
            }
        }

        return true;
    }

    private bool IsTupleToArrayCompatible(TypeInfo.Array expected, TypeInfo.Tuple actual)
    {
        // All tuple element types must be compatible with array element type
        foreach (var elem in actual.Elements)
        {
            // For spread elements, check the inner type
            var elemType = elem.IsSpread && elem.Type is TypeInfo.Array arrType
                ? arrType.ElementType
                : elem.Type;
            if (!IsCompatible(expected.ElementType, elemType))
                return false;
        }
        if (actual.RestElementType != null &&
            !IsCompatible(expected.ElementType, actual.RestElementType))
            return false;
        return true;
    }

    private bool IsArrayToTupleCompatible(TypeInfo.Tuple expected, TypeInfo.Array actual)
    {
        // Array can match tuple only if tuple has rest, spread, or is all-optional
        if (expected.RequiredCount > 0 && expected.RestElementType == null && !expected.HasSpread)
            return false;

        // All expected element types must be compatible with actual's element type
        foreach (var elem in expected.Elements)
        {
            // For spread elements, check the inner type
            var elemType = elem.IsSpread && elem.Type is TypeInfo.Array arrType
                ? arrType.ElementType
                : elem.Type;
            if (!IsCompatible(elemType, actual.ElementType))
                return false;
        }
        if (expected.RestElementType != null &&
            !IsCompatible(expected.RestElementType, actual.ElementType))
            return false;
        return true;
    }
}
