using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

[CollectionDefinition("Runtime conformance", DisableParallelization = true)]
public sealed class RuntimeConformanceCollection
{
}

[Trait("Category", "Corpus")]
[Collection("Runtime conformance")]
public sealed partial class RuntimeConformanceTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeConformanceTests(ITestOutputHelper output) => _output = output;

    private void AssertPassInBothModes(string relativePath)
    {
        foreach (var mode in new[]
                 {
                     Test262ExecutionMode.Interpreted,
                     Test262ExecutionMode.Compiled,
                 })
        {
            AssertPass(relativePath, mode);
        }
    }

    private void AssertPromiseKeyedPass(string relativePath)
        => AssertPassInBothModes(relativePath);

    private void AssertPass(
        string relativePath,
        Test262ExecutionMode mode,
        TimeSpan? timeout = null)
    {
        var root = Test262Paths.RequireRoot();

        var testPath = Path.Combine(Test262Paths.TestDir(root), relativePath);
        Assert.True(File.Exists(testPath), $"Expected Test262 file at {testPath}");

        var runner = new Test262Runner(
            root,
            timeout ?? TimeSpan.FromSeconds(15),
            useNonCollectibleLoad: true);
        var result = runner.RunOne(testPath, mode);

        _output.WriteLine($"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
        Assert.True(
            result.Outcome == Test262Outcome.Pass,
            $"{mode} {relativePath} -> {result.Outcome}: {result.Message}");
    }
}
