using Xunit;

namespace SharpTS.TypeScriptConformance;

public sealed class P3CheckerOrderConformanceTests
{
    [Fact]
    public void DiagnosticAndMetadataOrderPreservesEveryPinnedSourceObservation()
    {
        string root = TypeScriptConformancePaths.TryFindRoot()
            ?? throw new DirectoryNotFoundException("The pinned TypeScript corpus is required.");
        string path = Path.Combine(root, "tests/cases/conformance/types/literal/literalTypes1.ts");
        var runner = new TypeScriptConformanceRunner(root);
        var first = Observe();
        Assert.Equal(TypeScriptConformanceOutcome.Pass, runner.RunOne(path).Outcome);
        Assert.Equal(first, Observe());
        Assert.Equal(TypeScriptConformanceOutcome.Pass, runner.RunOne(path).Outcome);
        Assert.Equal(first, Observe());

        IReadOnlyList<ActualTypeObservation> Observe()
        {
            CheckedTypeScriptProgram? program = null;
            var result = runner.RunOne(path, checkedProgram => program = checkedProgram);
            Assert.Equal(TypeScriptConformanceOutcome.Pass, result.Outcome);
            Assert.NotNull(program);
            return TypeScriptTypesConformanceRunner.Collect(program);
        }
    }
}
