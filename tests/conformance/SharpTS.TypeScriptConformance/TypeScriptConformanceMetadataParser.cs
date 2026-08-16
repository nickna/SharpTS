using System.Text.RegularExpressions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Parses the <c>// @&lt;key&gt;: &lt;value&gt;</c> directive header used by
/// TypeScript conformance tests, and splits the source at <c>@filename:</c>
/// boundaries.
///
/// Behavior:
/// <list type="bullet">
/// <item>Every <c>// @key: value</c> line anywhere in the source is recorded
///       as a global directive (TS's test harness behaves the same way).</item>
/// <item><c>@filename:</c> additionally marks a virtual-file boundary. The
///       directive line itself is dropped from the resulting body.</item>
/// <item>Other directive lines remain in the body as ordinary comments — this
///       preserves source line numbers, which baselines reference.</item>
/// <item>If no <c>@filename:</c> is present, the whole source becomes one
///       virtual file named after the test's basename.</item>
/// </list>
/// </summary>
public static class TypeScriptConformanceMetadataParser
{
    // Matches:  // @key: value   (allows missing space after //, around colon, trailing whitespace)
    private static readonly Regex DirectiveRegex = new(
        @"^\s*//\s*@([A-Za-z][A-Za-z0-9_]*)\s*:\s*(.*?)\s*$",
        RegexOptions.Compiled);

    public static TypeScriptConformanceMetadata Parse(string testPath, string source)
    {
        // Strip BOM if present so the first line matches cleanly.
        if (source.Length > 0 && source[0] == '﻿') source = source[1..];

        var lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 && lines[i][^1] == '\r') lines[i] = lines[i][..^1];
        }

        var directives = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<TypeScriptConformanceFile>();

        // First pass: locate every @filename: line and use them to slice bodies.
        // Second pass interleaves with this — we capture directives along the way.
        var filenameLineIndices = new List<int>();
        var filenameNames = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var m = DirectiveRegex.Match(lines[i]);
            if (!m.Success) continue;

            var key = m.Groups[1].Value.ToLowerInvariant();
            var value = m.Groups[2].Value;

            if (key == "filename")
            {
                filenameLineIndices.Add(i);
                filenameNames.Add(value);
            }

            // Record every directive (including filename, in case anything wants to count them).
            // Last-wins on duplicate keys — matches TS harness behavior.
            directives[key] = value;
        }

        if (filenameLineIndices.Count == 0)
        {
            // Single-file test. Body = the source with directive lines removed and leading blank
            // lines dropped — matching tsc's TestCaseParser, whose *.errors.txt line numbers are
            // in this stripped coordinate space. Keeping the directives (they parse as comments)
            // shifts every diagnostic line on directive-headed tests.
            var basename = Path.GetFileName(testPath);
            var bodyLines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (DirectiveRegex.IsMatch(line)) continue;
                if (bodyLines.Count == 0 && line.Trim().Length == 0) continue;
                bodyLines.Add(line);
            }
            files.Add(new TypeScriptConformanceFile(basename, string.Join('\n', bodyLines)));
        }
        else
        {
            // Multi-file. Each file's body is the lines AFTER its @filename:
            // marker, up to (but not including) the next @filename: line —
            // or end of file for the last one.
            for (int f = 0; f < filenameLineIndices.Count; f++)
            {
                var startLine = filenameLineIndices[f] + 1;
                var endLine = f + 1 < filenameLineIndices.Count
                    ? filenameLineIndices[f + 1]
                    : lines.Length;

                var body = endLine > startLine
                    ? string.Join('\n', lines, startLine, endLine - startLine)
                    : string.Empty;
                files.Add(new TypeScriptConformanceFile(filenameNames[f], body));
            }
        }

        // The filename directive is a structural marker, not a config knob —
        // drop it from RawDirectives so consumers iterating over directives
        // don't have to special-case it.
        directives.Remove("filename");

        return new TypeScriptConformanceMetadata(
            TestPath: testPath,
            Target: GetString(directives, "target"),
            Module: GetString(directives, "module"),
            Jsx: GetString(directives, "jsx"),
            Strict: GetBool(directives, "strict") ?? false,
            NoImplicitAny: GetBool(directives, "noimplicitany"),
            StrictNullChecks: GetBool(directives, "strictnullchecks"),
            NoEmit: GetBool(directives, "noemit") ?? false,
            Lib: GetList(directives, "lib"),
            RawDirectives: directives,
            Files: files);
    }

    private static string? GetString(IReadOnlyDictionary<string, string> directives, string key) =>
        directives.TryGetValue(key, out var v) ? v : null;

    private static bool? GetBool(IReadOnlyDictionary<string, string> directives, string key)
    {
        if (!directives.TryGetValue(key, out var v)) return null;
        if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static IReadOnlyList<string> GetList(IReadOnlyDictionary<string, string> directives, string key)
    {
        if (!directives.TryGetValue(key, out var v) || v.Length == 0) return Array.Empty<string>();
        var parts = v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts;
    }
}
