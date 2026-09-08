namespace SharpTS.Conformance;

internal static class ConformanceInputs
{
    public static string RequireRoot(string? root, string corpus) => root
        ?? throw new InvalidOperationException(
            $"Missing external/{corpus}. Run git submodule update --init external/{corpus}. " +
            "Use --filter Category!=Corpus for harness-only tests.");

    public static void RequireCases(int count)
    {
        if (count <= 0) throw new InvalidOperationException("Conformance selection enumerated zero cases.");
    }

    public static void RequireBaseline(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Missing committed conformance baseline; updates must be explicit.", path);
    }

    public static IReadOnlyList<string> ReadSmokeCases(string projectDirectory)
    {
        var cases = File.ReadAllLines(Path.Combine(projectDirectory, "config", "ci-smoke.txt"))
            .Select(line => line.Trim()).Where(line => line.Length != 0 && !line.StartsWith('#')).ToArray();
        RequireCases(cases.Length);
        if (cases.Distinct(StringComparer.Ordinal).Count() != cases.Length)
            throw new InvalidOperationException("Duplicate CI conformance cases.");
        if (cases.Any(path => Path.IsPathRooted(path) || path.Split('/').Contains("..")))
            throw new InvalidOperationException("CI conformance paths must stay within their corpus.");
        return cases;
    }
}
