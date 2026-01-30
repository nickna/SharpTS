using System.Text;
using System.Text.RegularExpressions;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript RegExp objects.
/// </summary>
/// <remarks>
/// Wraps .NET System.Text.RegularExpressions.Regex with JavaScript semantics.
/// Supports global (g), ignoreCase (i), and multiline (m) flags.
/// Maintains lastIndex for global matching.
/// </remarks>
public class SharpTSRegExp : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.RegExp;

    private readonly Regex _regex;
    private readonly string _source;
    private readonly string _flags;
    private readonly bool _global;
    private readonly bool _ignoreCase;
    private readonly bool _multiline;

    /// <summary>
    /// The index at which to start the next match (used with global flag).
    /// </summary>
    public int LastIndex { get; set; }

    /// <summary>
    /// The pattern string of the regular expression.
    /// </summary>
    public string Source => _source;

    /// <summary>
    /// The flags string of the regular expression.
    /// </summary>
    public string Flags => _flags;

    /// <summary>
    /// Whether the global (g) flag is set.
    /// </summary>
    public bool Global => _global;

    /// <summary>
    /// Whether the ignoreCase (i) flag is set.
    /// </summary>
    public bool IgnoreCase => _ignoreCase;

    /// <summary>
    /// Whether the multiline (m) flag is set.
    /// </summary>
    public bool Multiline => _multiline;

    /// <summary>
    /// Creates a RegExp with the specified pattern and optional flags.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    /// <param name="flags">The flags string (g, i, m).</param>
    public SharpTSRegExp(string pattern, string flags = "")
    {
        _source = pattern;
        _flags = NormalizeFlags(flags);
        _global = _flags.Contains('g');
        _ignoreCase = _flags.Contains('i');
        _multiline = _flags.Contains('m');
        LastIndex = 0;

        var options = RegexOptions.ECMAScript; // Use ECMAScript mode for JS compatibility
        if (_ignoreCase) options |= RegexOptions.IgnoreCase;
        if (_multiline) options |= RegexOptions.Multiline;

        try
        {
            _regex = new Regex(pattern, options);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regular expression: {ex.Message}");
        }
    }

    /// <summary>
    /// Normalize and deduplicate flags, preserving only valid flags.
    /// </summary>
    private static string NormalizeFlags(string flags)
    {
        var sb = new StringBuilder();
        if (flags.Contains('g') && !sb.ToString().Contains('g')) sb.Append('g');
        if (flags.Contains('i') && !sb.ToString().Contains('i')) sb.Append('i');
        if (flags.Contains('m') && !sb.ToString().Contains('m')) sb.Append('m');
        return sb.ToString();
    }

    /// <summary>
    /// Tests if the pattern matches the string.
    /// For global regexes, starts from lastIndex and updates it.
    /// </summary>
    /// <param name="input">The string to test against.</param>
    /// <returns>True if the pattern matches, false otherwise.</returns>
    public bool Test(string input)
    {
        if (_global)
        {
            if (LastIndex > input.Length)
            {
                LastIndex = 0;
                return false;
            }

            var match = _regex.Match(input, Math.Min(LastIndex, input.Length));
            if (match.Success)
            {
                LastIndex = match.Index + match.Length;
                return true;
            }
            else
            {
                LastIndex = 0;
                return false;
            }
        }

        return _regex.IsMatch(input);
    }

    /// <summary>
    /// Executes the regex on the string and returns match info.
    /// For global regexes, maintains state via lastIndex.
    /// </summary>
    /// <param name="input">The string to match against.</param>
    /// <returns>An array with the match and capture groups, or null if no match.</returns>
    public SharpTSArray? Exec(string input)
    {
        Match match;

        if (_global)
        {
            if (LastIndex > input.Length)
            {
                LastIndex = 0;
                return null;
            }
            match = _regex.Match(input, Math.Min(LastIndex, input.Length));
        }
        else
        {
            match = _regex.Match(input);
        }

        if (!match.Success)
        {
            if (_global) LastIndex = 0;
            return null;
        }

        if (_global)
        {
            LastIndex = match.Index + match.Length;
        }

        // Build result array: [fullMatch, ...groups]
        List<object?> elements = [];
        elements.Add(match.Value);

        // Add capture groups (skip group 0 which is the full match)
        for (int i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            elements.Add(group.Success ? group.Value : null);
        }

        var result = new SharpTSArray(elements);

        // Note: JavaScript's exec() also adds 'index' and 'input' properties to the result array.
        // SharpTSArray currently doesn't support arbitrary properties, so we skip this for now.
        // The array elements themselves are the primary result.

        return result;
    }

    /// <summary>
    /// Returns the string representation of the regex: /pattern/flags
    /// </summary>
    public override string ToString() => $"/{_source}/{_flags}";

    /// <summary>
    /// Internal regex accessor for string methods.
    /// </summary>
    internal Regex InternalRegex => _regex;

    /// <summary>
    /// Match all occurrences in the string (used by String.match with global flag).
    /// </summary>
    internal List<string> MatchAll(string input)
    {
        var matches = _regex.Matches(input);
        List<string> result = [];
        foreach (Match m in matches)
        {
            result.Add(m.Value);
        }
        return result;
    }

    /// <summary>
    /// Replace occurrences in the string.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="replacement">The replacement string.</param>
    /// <returns>The string with replacements made.</returns>
    internal string Replace(string input, string replacement)
    {
        if (_global)
        {
            return _regex.Replace(input, replacement);
        }
        else
        {
            // Non-global: replace first match only
            return _regex.Replace(input, replacement, 1);
        }
    }

    /// <summary>
    /// Search for the first match in the string.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The index of the first match, or -1 if not found.</returns>
    internal int Search(string input)
    {
        var match = _regex.Match(input);
        return match.Success ? match.Index : -1;
    }

    /// <summary>
    /// Split the string by the regex pattern.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>An array of substrings.</returns>
    internal string[] Split(string input)
    {
        return _regex.Split(input);
    }
}
