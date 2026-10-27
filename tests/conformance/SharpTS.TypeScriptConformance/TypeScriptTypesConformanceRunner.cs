using SharpTS.TypeSystem;

namespace SharpTS.TypeScriptConformance;

public enum TypeScriptTypesOutcome { Pass, Mismatch, Unsupported, NoBaseline, HarnessError }
public enum TypeObservationDifferenceKind
{
    TypeTextMismatch, MissingSharpTSObservation, ExtraSharpTSObservation, UnsupportedSharpTSType, HarnessError
}
public sealed record TypeObservationKey(string File, int Line, string SourceText, int Ordinal);
public sealed record ActualTypeObservation(TypeObservationKey Key, string? TypeText, UnsupportedTypeDisplay? Unsupported = null);
public sealed record TypeObservationDifference(TypeObservationDifferenceKind Kind, TypeObservationKey Key,
    string? Expected, string? Actual, string? Detail = null);
public sealed record TypeScriptTypesResult(TypeScriptTypesOutcome Outcome, int Matches,
    IReadOnlyList<TypeObservationDifference> Differences, string? Message = null);

/// <summary>Compares checked source types against the pinned corpus without running TypeScript or guest code.</summary>
public sealed class TypeScriptTypesConformanceRunner(string typescriptRoot)
{
    public TypeScriptTypesResult RunOne(string testFilePath)
    {
        try
        {
            var metadata = TypeScriptConformanceMetadataParser.Parse(testFilePath, File.ReadAllText(testFilePath));
            var baseline = TypeScriptBaselineResolver.Resolve(typescriptRoot, testFilePath, metadata, TypeScriptBaselineKind.Types);
            if (baseline.Status == TypeScriptBaselineResolutionStatus.NoBaseline)
                return new(TypeScriptTypesOutcome.NoBaseline, 0, []);
            if (baseline.Status != TypeScriptBaselineResolutionStatus.Found)
                return new(TypeScriptTypesOutcome.HarnessError, 0, [], $"Ambiguous type baseline: {string.Join(", ", baseline.Candidates)}");
            var expected = TypesBaselineParser.Parse(File.ReadAllText(baseline.Path!), metadata.Files).Observations;
            CheckedTypeScriptProgram? program = null;
            var diagnostics = new TypeScriptConformanceRunner(typescriptRoot).RunOne(testFilePath, value => program = value);
            if (program is null || diagnostics.Outcome != TypeScriptConformanceOutcome.Pass)
                return new(TypeScriptTypesOutcome.HarnessError, 0, [], $"Pilot requires a diagnostic pass: {diagnostics.Outcome}: {diagnostics.Message}");
            var actual = Collect(program);
            return Compare(expected, actual);
        }
        catch (Exception exception)
        {
            return new(TypeScriptTypesOutcome.HarnessError, 0, [], exception.Message);
        }
    }

    internal static IReadOnlyList<ActualTypeObservation> Collect(CheckedTypeScriptProgram program)
    {
        var result = new List<ActualTypeObservation>();
        var sourceModules = program.Modules.Where(module => !module.IsDefaultLibrary && !module.IsDeclarationFile && module.Document is not null);
        foreach (var module in sourceModules)
        {
            var document = module.Document!;
            string relative = Path.GetRelativePath(program.VirtualRoot, module.Path).Replace('\\', '/');
            var sourceFile = program.Metadata.Files.SingleOrDefault(file => NormalizeFile(file.Name) == NormalizeFile(relative));
            if (sourceFile is null) continue; // Dependencies are not TypeWriterWalker roots.
            var ordinals = new Dictionary<(int Line, string Text), int>();
            foreach (var fact in program.Observations[document])
            {
                string text = document.Text[fact.Span.Start..fact.Span.End].Replace("\r\n", "\n", StringComparison.Ordinal);
                int line = document.Lines.ToPosition(fact.Span.Start).Line;
                var key = (line, text);
                int ordinal = ordinals.GetValueOrDefault(key) + 1;
                ordinals[key] = ordinal;
                result.Add(new(new(NormalizeFile(sourceFile.Name), line, text, ordinal), fact.Display.Text, fact.Display.Unsupported));
            }
        }
        return result;
    }

    public static TypeScriptTypesResult Compare(IReadOnlyList<TypesBaselineObservation> expected,
        IReadOnlyList<ActualTypeObservation> actual)
    {
        var differences = new List<TypeObservationDifference>();
        var actualByKey = new Dictionary<TypeObservationKey, ActualTypeObservation>();
        foreach (var observation in actual)
        {
            if (!actualByKey.TryAdd(observation.Key, observation))
                return new(TypeScriptTypesOutcome.HarnessError, 0,
                    [new(TypeObservationDifferenceKind.HarnessError, observation.Key, null, null, "Ambiguous actual observation key.")]);
        }
        int matches = 0;
        var expectedKeys = new HashSet<TypeObservationKey>();
        foreach (var observation in expected)
        {
            var key = new TypeObservationKey(NormalizeFile(observation.VirtualFileName), observation.SourceLine,
                observation.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal), observation.OccurrenceOrdinal);
            if (!expectedKeys.Add(key))
                return new(TypeScriptTypesOutcome.HarnessError, matches,
                    [new(TypeObservationDifferenceKind.HarnessError, key, null, null, "Ambiguous expected observation key.")]);
            if (!actualByKey.Remove(key, out var found))
                differences.Add(new(TypeObservationDifferenceKind.MissingSharpTSObservation, key, observation.ExpectedTypeText, null));
            else if (found.Unsupported is { } unsupported)
                differences.Add(new(TypeObservationDifferenceKind.UnsupportedSharpTSType, key, observation.ExpectedTypeText, null,
                    $"{unsupported.TypeKind}: {unsupported.Reason}"));
            else if (observation.ExpectedTypeText != found.TypeText)
                differences.Add(new(TypeObservationDifferenceKind.TypeTextMismatch, key, observation.ExpectedTypeText, found.TypeText));
            else matches++;
        }
        differences.AddRange(actualByKey.Values.OrderBy(value => value.Key.File, StringComparer.Ordinal)
            .ThenBy(value => value.Key.Line).ThenBy(value => value.Key.Ordinal)
            .Select(value => new TypeObservationDifference(TypeObservationDifferenceKind.ExtraSharpTSObservation,
                value.Key, null, value.TypeText, value.Unsupported?.Reason)));
        return new(differences.Count == 0 ? TypeScriptTypesOutcome.Pass :
            differences.All(difference => difference.Kind == TypeObservationDifferenceKind.UnsupportedSharpTSType)
                ? TypeScriptTypesOutcome.Unsupported : TypeScriptTypesOutcome.Mismatch,
            matches, differences);
    }

    private static string NormalizeFile(string path) => path.Replace('\\', '/').TrimStart('/');
}
