using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData("issue")]
    [InlineData("metadata")]
    [InlineData("receivers")]
    [InlineData("cache-keys")]
    public void Isolated_Issue2017StaticSymbolMethods_PreserveCanonicalValues(string name)
    {
        var (source, expected) = name switch
        {
            "issue" => (StaticSymbolMethodIdentityTests.IssueSource, "true\ntrue\n"),
            "metadata" => (StaticSymbolMethodIdentityTests.MetadataSource,
                string.Concat(Enumerable.Repeat("true true 1 1\n", 4))),
            "cache-keys" => (StaticSymbolMethodIdentityTests.CacheKeySource, StaticSymbolMethodIdentityTests.CacheKeyExpected),
            _ => (StaticSymbolMethodIdentityTests.ReceiverSource, StaticSymbolMethodIdentityTests.ReceiverExpected)
        };
        AssertStaticSymbolMethodSource(name, source, expected);
    }

    [Theory]
    [DeploymentData(nameof(StaticSymbolMethodIdentityTests.ClosedGenericCases), MemberType = typeof(StaticSymbolMethodIdentityTests))]
    public void Isolated_Issue2122ClosedGenericStaticSymbolMethods_PreserveCanonicalValues(
        string name, string source, string expected)
        => AssertStaticSymbolMethodSource("closed-generic-" + name, source, expected);

    [Theory]
    [DeploymentData(nameof(StaticSymbolMethodAssignmentTests.DescriptorCases), MemberType = typeof(StaticSymbolMethodAssignmentTests))]
    public void Isolated_Issue2213StaticSymbolMethods_HaveLiveOwnDescriptors(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        AssertStaticSymbolMethodSource("descriptors-" + name, source, expected);
    }

    private static void AssertStaticSymbolMethodSource(string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("static-symbol-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.All(GetAssemblyReferences(output), reference => Assert.True(
            reference is "mscorlib" or "netstandard" || reference.StartsWith("System", StringComparison.Ordinal),
            "Unexpected standalone reference: " + reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
