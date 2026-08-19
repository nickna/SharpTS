using Xunit;

namespace SharpTS.TypeScriptConformance;

public class TypeScriptConformanceConfigTests
{
    [Fact]
    public void Load_ParsesExplicitCoverageFiles()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"sharpts-tsconfig-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "folders": ["tests/cases/conformance/types/keyof"],
                  "files": [
                    "tests/cases/conformance/types/typeAliases/typeAliases.ts",
                    "tests/cases/conformance/enums/enumBasics.ts"
                  ],
                  "timeoutSeconds": 5,
                  "skipDirectivesFile": null,
                  "skipTestsFile": null
                }
                """);

            var config = TypeScriptConformanceConfig.Load(path);

            Assert.Equal(2, config.Files?.Count);
            Assert.Contains(
                "tests/cases/conformance/enums/enumBasics.ts",
                config.Files ?? []);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
