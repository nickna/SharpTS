using System.Text.RegularExpressions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Parses TypeScript's committed <c>*.types</c> baseline format into source-backed ordered
/// observations. The virtual source bodies are authoritative for line coordinates because the
/// upstream writer inserts presentation-only blank lines between some observation groups.
/// </summary>
public static class TypesBaselineParser
{
    private const string Delimiter = " : ";
    private const string PerformanceHeader = "=== Performance Stats ===";
    private static readonly Regex TestPathPrefixRegex = new(
        @"(?:(file:/{3})|/)\.(?:ts|lib|src)/",
        RegexOptions.Compiled);

    public static TypesBaselineDocument Parse(
        string content,
        IReadOnlyList<TypeScriptConformanceFile> sourceFiles)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sourceFiles);

        string[] baselineLines = SplitBaselineLines(content);
        SourceLookup sourceLookup = new(sourceFiles);
        var parsedFiles = new List<TypesBaselineFile>();
        SectionState? section = null;
        bool inPerformanceSection = false;
        bool sawFileSection = false;

        for (int index = 0; index < baselineLines.Length; index++)
        {
            string line = baselineLines[index];
            int baselineLine = index + 1;

            if (TryReadSectionHeader(line, out string? sectionName))
            {
                FinishSection(section, baselineLine);
                section = null;
                if (line == PerformanceHeader)
                {
                    inPerformanceSection = true;
                    continue;
                }

                inPerformanceSection = false;
                TypeScriptConformanceFile source = sourceLookup.Resolve(sectionName!, baselineLine);
                section = new SectionState(source);
                parsedFiles.Add(new TypesBaselineFile(
                    section.VirtualFileName,
                    section.Observations));
                sawFileSection = true;
                continue;
            }

            if (inPerformanceSection)
                continue;

            if (section is null)
            {
                if (line.Length == 0 || IsPreamble(line))
                    continue;
                if (line.StartsWith('>'))
                    throw Error("Type observation appears outside a virtual-file section.", baselineLine);
                throw Error($"Unexpected content before a virtual-file section: '{line}'.", baselineLine);
            }

            // Source echo lines take precedence, including a legitimate source line beginning
            // with '>'. A blank line that does not match the next source line is a writer-added
            // presentation separator and is ignored.
            if (section.TryConsumeSourceLine(line))
                continue;
            if (line.Length == 0)
                continue;

            if (!line.StartsWith('>'))
            {
                throw Error(
                    $"Source echo for '{section.VirtualFileName}' does not match line {section.NextSourceLine}: '{line}'.",
                    baselineLine);
            }

            if (TryReadUnderline(line, out _, out _))
                throw Error("Underline appears without a preceding type observation.", baselineLine);
            if (section.CurrentSourceLine == 0)
                throw Error("Type observation appears before its source line.", baselineLine);

            int delimiterIndex;
            string? underline = null;
            if (index + 1 < baselineLines.Length &&
                TryReadUnderline(baselineLines[index + 1], out int sourceTextLength, out string? parsedUnderline))
            {
                delimiterIndex = sourceTextLength;
                underline = parsedUnderline;
                index++;
            }
            else
            {
                // The upstream writer omits underlines only for intrinsic any/error displays.
                // Use the final delimiter so source expressions containing ` : ` remain intact.
                delimiterIndex = line[1..].LastIndexOf(Delimiter, StringComparison.Ordinal);
            }

            string observationBody = line[1..];
            if (delimiterIndex < 0 ||
                delimiterIndex + Delimiter.Length > observationBody.Length ||
                !observationBody.AsSpan(delimiterIndex, Delimiter.Length).SequenceEqual(Delimiter))
            {
                throw Error("Malformed type observation delimiter.", baselineLine);
            }

            string sourceText = observationBody[..delimiterIndex];
            string expectedType = observationBody[(delimiterIndex + Delimiter.Length)..];
            if (expectedType.Length == 0)
                throw Error("Type observation has an empty expected type.", baselineLine);

            int occurrence = section.NextOccurrence(sourceText);
            section.Observations.Add(new TypesBaselineObservation(
                section.VirtualFileName,
                section.CurrentSourceLine,
                section.CurrentSourceLineText,
                sourceText,
                occurrence,
                expectedType,
                underline));
        }

        FinishSection(section, baselineLines.Length + 1);
        if (!sawFileSection)
            throw Error("Baseline contains no virtual-file sections.", 1);

        return new TypesBaselineDocument(parsedFiles);
    }

    private static void FinishSection(SectionState? section, int baselineLine)
    {
        if (section is null || section.HasConsumedAllSource)
            return;
        throw Error(
            $"Baseline section '{section.VirtualFileName}' ended before source line {section.NextSourceLine} was echoed.",
            baselineLine);
    }

    private static bool TryReadSectionHeader(string line, out string? name)
    {
        const string prefix = "=== ";
        const string suffix = " ===";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) ||
            !line.EndsWith(suffix, StringComparison.Ordinal) ||
            line.Length <= prefix.Length + suffix.Length)
        {
            name = null;
            return false;
        }
        name = line[prefix.Length..^suffix.Length];
        return true;
    }

    private static bool TryReadUnderline(
        string line,
        out int sourceTextLength,
        out string? underline)
    {
        sourceTextLength = -1;
        underline = null;
        if (!line.StartsWith('>'))
            return false;

        string body = line[1..];
        int delimiter = body.IndexOf(Delimiter, StringComparison.Ordinal);
        if (delimiter < 0 || body[..delimiter].Any(character => character != ' '))
            return false;
        string value = body[(delimiter + Delimiter.Length)..];
        if (value.Any(character => character is not (' ' or '^')))
            return false;

        sourceTextLength = delimiter;
        underline = value;
        return true;
    }

    private static bool IsPreamble(string line) =>
        line.StartsWith("//// [", StringComparison.Ordinal) &&
        line.EndsWith("] ////", StringComparison.Ordinal);

    private static string[] SplitBaselineLines(string text)
    {
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private static string[] SplitSourceLines(string text) =>
        SplitBaselineLines(text)
            .SelectMany(line => line.Split(['\r', '\u2028', '\u2029']))
            .ToArray();

    private static TypesBaselineParseException Error(string message, int baselineLine) =>
        new(message, baselineLine);

    private sealed class SectionState
    {
        private readonly string[] _sourceLines;
        private readonly Dictionary<(int Line, string Text), int> _occurrences = [];
        private int _nextSourceIndex;

        public SectionState(TypeScriptConformanceFile source)
        {
            VirtualFileName = NormalizeName(source.Name);
            _sourceLines = SplitSourceLines(source.Body);
        }

        public string VirtualFileName { get; }
        public List<TypesBaselineObservation> Observations { get; } = [];
        public int CurrentSourceLine { get; private set; }
        public string CurrentSourceLineText => CurrentSourceLine == 0
            ? string.Empty
            : _sourceLines[CurrentSourceLine - 1];
        public int NextSourceLine => _nextSourceIndex + 1;
        public bool HasConsumedAllSource => _nextSourceIndex == _sourceLines.Length;

        public bool TryConsumeSourceLine(string baselineLine)
        {
            if (_nextSourceIndex >= _sourceLines.Length ||
                !string.Equals(
                    RemoveTestPathPrefixes(_sourceLines[_nextSourceIndex]),
                    baselineLine,
                    StringComparison.Ordinal))
            {
                return false;
            }

            CurrentSourceLine = ++_nextSourceIndex;
            return true;
        }

        public int NextOccurrence(string sourceText)
        {
            var key = (CurrentSourceLine, sourceText);
            int occurrence = _occurrences.GetValueOrDefault(key) + 1;
            _occurrences[key] = occurrence;
            return occurrence;
        }
    }

    private sealed class SourceLookup
    {
        private readonly IReadOnlyList<TypeScriptConformanceFile> _files;

        public SourceLookup(IReadOnlyList<TypeScriptConformanceFile> files) => _files = files;

        public TypeScriptConformanceFile Resolve(string baselineName, int baselineLine)
        {
            string normalized = NormalizeName(baselineName);
            TypeScriptConformanceFile[] exact = _files
                .Where(file => string.Equals(
                    NormalizeName(file.Name),
                    normalized,
                    StringComparison.Ordinal))
                .ToArray();
            if (exact.Length == 1)
                return exact[0];
            if (exact.Length > 1)
                throw Error($"Virtual filename '{baselineName}' is ambiguous.", baselineLine);

            string basename = normalized.Split('/').Last();
            TypeScriptConformanceFile[] byBasename = _files
                .Where(file => string.Equals(
                    NormalizeName(file.Name).Split('/').Last(),
                    basename,
                    StringComparison.Ordinal))
                .ToArray();
            return byBasename.Length switch
            {
                1 => byBasename[0],
                0 => throw Error($"Baseline references unknown virtual file '{baselineName}'.", baselineLine),
                _ => throw Error($"Virtual filename '{baselineName}' is ambiguous by basename.", baselineLine),
            };
        }
    }

    private static string NormalizeName(string name)
    {
        string normalized = RemoveTestPathPrefixes(name.Replace('\\', '/'));
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private static string RemoveTestPathPrefixes(string text) =>
        TestPathPrefixRegex.Replace(text, match => match.Groups[1].Success ? match.Groups[1].Value : string.Empty);
}
