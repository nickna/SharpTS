using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Template literal type pattern matching for type compatibility.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Checks if a string literal matches a template literal pattern.
    /// </summary>
    private bool MatchesTemplateLiteralPattern(TypeInfo.TemplateLiteralType pattern, string value)
    {
        int pos = 0;

        for (int i = 0; i < pattern.InterpolatedTypes.Count; i++)
        {
            string prefix = pattern.Strings[i];

            // Check prefix
            if (pos + prefix.Length > value.Length || !value[pos..].StartsWith(prefix))
                return false;
            pos += prefix.Length;

            // Find where the next static string starts (or end of string)
            string nextStatic = pattern.Strings[i + 1];
            int nextPos;

            if (string.IsNullOrEmpty(nextStatic))
            {
                // No more static parts after this interpolation
                if (i == pattern.InterpolatedTypes.Count - 1)
                {
                    // Last interpolation - capture rest of string
                    nextPos = value.Length;
                }
                else
                {
                    // Find next non-empty static part
                    int foundIdx = -1;
                    for (int j = i + 2; j < pattern.Strings.Count; j++)
                    {
                        if (!string.IsNullOrEmpty(pattern.Strings[j]))
                        {
                            foundIdx = value.IndexOf(pattern.Strings[j], pos);
                            break;
                        }
                    }
                    nextPos = foundIdx >= pos ? foundIdx : value.Length;
                }
            }
            else
            {
                nextPos = value.IndexOf(nextStatic, pos);
            }

            if (nextPos < pos) return false;

            // Extract the matched portion and check against interpolated type
            string matched = value[pos..nextPos];
            TypeInfo interpolatedType = pattern.InterpolatedTypes[i];

            if (!MatchesInterpolatedType(matched, interpolatedType))
                return false;

            pos = nextPos;
        }

        // Check final suffix
        return value[pos..] == pattern.Strings[^1];
    }

    /// <summary>
    /// Checks if a string matches an interpolated type slot in a template literal.
    /// </summary>
    private bool MatchesInterpolatedType(string value, TypeInfo type) => type switch
    {
        TypeInfo.String => true,  // 'string' matches any string
        TypeInfo.StringLiteral sl => sl.Value == value,
        TypeInfo.NumberLiteral nl => double.TryParse(value, out var d) && d == nl.Value,
        TypeInfo.BooleanLiteral bl => (bl.Value ? "true" : "false") == value,
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => double.TryParse(value, out _),
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => value is "true" or "false",
        TypeInfo.Union u => u.FlattenedTypes.Any(t => MatchesInterpolatedType(value, t)),
        _ => false
    };

    /// <summary>
    /// Checks if two template literal types are structurally compatible.
    /// </summary>
    private bool TemplatePatternStructurallyCompatible(TypeInfo.TemplateLiteralType expected, TypeInfo.TemplateLiteralType actual)
    {
        // Must have same structure
        if (expected.Strings.Count != actual.Strings.Count ||
            expected.InterpolatedTypes.Count != actual.InterpolatedTypes.Count)
            return false;

        // All static strings must match
        for (int i = 0; i < expected.Strings.Count; i++)
        {
            if (expected.Strings[i] != actual.Strings[i])
                return false;
        }

        // All interpolated types must be compatible
        for (int i = 0; i < expected.InterpolatedTypes.Count; i++)
        {
            if (!IsCompatible(expected.InterpolatedTypes[i], actual.InterpolatedTypes[i]))
                return false;
        }

        return true;
    }
}
