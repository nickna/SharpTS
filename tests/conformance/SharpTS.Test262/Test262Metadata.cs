using System.Text.RegularExpressions;

namespace SharpTS.Test262;

/// <summary>
/// Frontmatter extracted from a Test262 test file.
///
/// Test262 files begin with a YAML block bracketed by <c>/*---</c> / <c>---*/</c>.
/// We parse a deliberately small subset — enough for the fields the runner
/// actually keys on. Full YAML is not required: upstream enforces a strict
/// shape via its own linter.
/// </summary>
public sealed record Test262Metadata(
    string? Description,
    string? Esid,
    string? Info,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Includes,
    Test262Negative? Negative)
{
    public bool IsNegative => Negative is not null;
    public bool IsRaw => Flags.Contains("raw");
    public bool IsModule => Flags.Contains("module");
    public bool IsAsync => Flags.Contains("async");
    public bool OnlyStrict => Flags.Contains("onlyStrict");
    public bool NoStrict => Flags.Contains("noStrict");
}

/// <summary>Declares that a test is expected to fail at a given phase with a given error type.</summary>
public sealed record Test262Negative(string Phase, string Type);

public static class Test262MetadataParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"/\*---\s*\r?\n(?<body>.*?)\r?\n---\*/",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parses the <c>/*--- ... ---*/</c> frontmatter block. Returns an empty
    /// metadata record (no description, empty lists) when no frontmatter is
    /// found — upstream guarantees it but we tolerate its absence.
    /// </summary>
    public static Test262Metadata Parse(string source)
    {
        var match = FrontmatterRegex.Match(source);
        if (!match.Success)
        {
            return new Test262Metadata(null, null, null,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null);
        }

        var lines = match.Groups["body"].Value.Split('\n');

        string? description = null;
        string? esid = null;
        string? info = null;
        var flags = new List<string>();
        var features = new List<string>();
        var includes = new List<string>();
        Test262Negative? negative = null;

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 || IsIndented(line))
            {
                // Continuation lines are consumed by the key-branch that owns them.
                i++;
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                i++;
                continue;
            }

            var key = line[..colon].Trim();
            var rest = line[(colon + 1)..].TrimStart();

            switch (key)
            {
                case "description":
                    description = ReadScalarOrBlock(lines, ref i, rest);
                    break;
                case "esid":
                    esid = ReadScalarOrBlock(lines, ref i, rest);
                    break;
                case "info":
                    info = ReadScalarOrBlock(lines, ref i, rest);
                    break;
                case "flags":
                    AddAll(flags, ParseInlineList(rest));
                    i++;
                    break;
                case "features":
                    AddAll(features, ParseInlineList(rest));
                    i++;
                    break;
                case "includes":
                    AddAll(includes, ParseInlineList(rest));
                    i++;
                    break;
                case "negative":
                    negative = ReadNegative(lines, ref i, rest);
                    break;
                default:
                    i++;
                    break;
            }
        }

        return new Test262Metadata(description, esid, info, flags, features, includes, negative);
    }

    private static bool IsIndented(string line) =>
        line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

    private static void AddAll(List<string> dest, IEnumerable<string> src)
    {
        foreach (var item in src) dest.Add(item);
    }

    /// <summary>
    /// Parses a value that's either on the same line as the key (scalar), or
    /// introduced by a YAML block marker (<c>|</c> / <c>&gt;</c>) with the body
    /// on subsequent indented lines.
    /// </summary>
    private static string ReadScalarOrBlock(string[] lines, ref int i, string rest)
    {
        if (rest == ">" || rest == "|" || rest == ">-" || rest == "|-")
        {
            var fold = rest[0] == '>';
            i++;
            var body = new List<string>();
            while (i < lines.Length)
            {
                var cur = lines[i].TrimEnd('\r');
                if (cur.Length == 0)
                {
                    body.Add("");
                    i++;
                    continue;
                }
                if (!IsIndented(cur)) break;
                body.Add(cur.TrimStart());
                i++;
            }
            return fold ? string.Join(' ', body.Where(s => s.Length > 0)) : string.Join('\n', body);
        }

        // Single-line scalar. Strip matching surrounding quotes for convenience.
        i++;
        return Unquote(rest);
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2)
        {
            if ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))
                return s[1..^1];
        }
        return s;
    }

    private static IEnumerable<string> ParseInlineList(string rest)
    {
        // Accept either `[a, b, c]` or a comma-separated bare value.
        rest = rest.Trim();
        if (rest.StartsWith('[') && rest.EndsWith(']'))
            rest = rest[1..^1];
        if (rest.Length == 0) yield break;
        foreach (var raw in rest.Split(','))
        {
            var item = raw.Trim();
            if (item.Length == 0) continue;
            yield return Unquote(item);
        }
    }

    private static Test262Negative? ReadNegative(string[] lines, ref int i, string rest)
    {
        // `negative:` is always a nested object with `phase:` and `type:` children.
        // Tolerate (but ignore) the rare single-line form.
        i++;
        string? phase = null;
        string? type = null;
        while (i < lines.Length)
        {
            var cur = lines[i].TrimEnd('\r');
            if (cur.Length == 0) { i++; continue; }
            if (!IsIndented(cur)) break;

            var t = cur.TrimStart();
            var colon = t.IndexOf(':');
            if (colon < 0) { i++; continue; }
            var k = t[..colon].Trim();
            var v = Unquote(t[(colon + 1)..].Trim());
            if (k == "phase") phase = v;
            else if (k == "type") type = v;
            i++;
        }
        if (phase is null || type is null) return null;
        return new Test262Negative(phase, type);
    }
}
