using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliSymbolUnionEqualityTests
{
    [Fact]
    public void ContextualOwnKeysFilterRemainsValidInBothCheckedProfiles()
    {
        var row = SymbolUnionEqualityTests.Cases().Single(row => (string)row[0] == "contextual-ownkeys-filter");
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", (string)row[1]);
        foreach (var profile in new[] { "--no-tsconfig", "--no-tsconfig --strict --noLib" })
        {
            var result = CliTestHelper.RunCli($"{profile} \"{sourcePath}\"", directory.Path, TimeSpan.FromSeconds(30));
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
            Assert.Empty(result.StandardError);
            Assert.Equal((string)row[2], result.StandardOutput.Replace("\r\n", "\n"));
        }
    }
}
