using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GenericStaticFieldModuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNamedModuleFieldsStayIndependent(bool reverse)
    {
        const string left = "import { read as l, write } from './left';";
        const string right = "import { read as r } from './right';";
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = """
                const Box = class Same<T> { static count: number = 1; };
                export function read(): number { return (Box as any).count; }
                export function write(): void { (Box as any).count = 7; }
                """,
            ["right.ts"] = """
                const Box = class Same<T> { static count: number = 2; };
                export function read(): number { return (Box as any).count; }
                """,
            ["main.ts"] = (reverse ? right + left : left + right)
                + "console.log(l()); console.log(r()); write(); console.log(l()); console.log(r());"
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("1\n2\n7\n2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
    }

    [Theory]
    [InlineData("class Box<T> { static count: number = 1; } Box.count = 'bad';")]
    [InlineData("class Box<T> { private static count: number = 1; } Box.count = 2;")]
    [InlineData("class Base<T> { protected static count: number = 1; } class Box<T> extends Base<T> {} Box.count = 2;")]
    public void StaticAssignmentPreservesTypeAndAccessChecks(string source)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Throws<TypeCheckDiagnosticException>(() => TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }
}
