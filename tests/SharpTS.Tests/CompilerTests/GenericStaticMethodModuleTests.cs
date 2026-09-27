using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericStaticMethodModuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNamedBindingsKeepTheirStaticOwners(bool reverseImports)
    {
        string leftImport = "import { read as left } from './left';";
        string rightImport = "import { read as right } from './right';";
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = """
                const Box = class Same<T> { static read(): number { return 1; } };
                export function read(): number { return Box.read(); }
                """,
            ["right.ts"] = """
                const Box = class Same<T> { static read(): number { return 2; } };
                export function read(): number { return Box.read(); }
                """,
            ["main.ts"] = (reverseImports ? rightImport + leftImport : leftImport + rightImport)
                + "console.log(left()); console.log(right());"
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }
}
