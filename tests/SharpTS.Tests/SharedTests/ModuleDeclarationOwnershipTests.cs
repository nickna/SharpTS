using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ModuleDeclarationOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameBasenameExportAssignmentsKeepTheirOwners(bool reverseImports)
    {
        var files = new Dictionary<string, string>
        {
            ["left/index.ts"] = "class Counter { read(): number { return 1; } } export = Counter;",
            ["right/index.ts"] = "class Counter { read(): number { return 2; } } export = Counter;",
            ["main.ts"] = (reverseImports
                ? "import R = require('./right/index'); import L = require('./left/index');"
                : "import L = require('./left/index'); import R = require('./right/index');")
                + "console.log(new L().read(), new R().read());"
        };
        Assert.Equal("1 2\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Equal("1 2\n", TestHarness.RunModulesCompiled(files, "main.ts"));
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameBasenameModulesKeepTheirDeclarations(bool reverseImports)
    {
        var files = new Dictionary<string, string>
        {
            ["left/index.ts"] = "export class Counter { value: number = 1; read(): number { return this.value; } } export function read(): number { return new Counter().read(); } export enum Tag { Value = 10 }",
            ["right/index.ts"] = "export class Counter { value: number = 2; read(): number { return this.value; } } export function read(): number { return new Counter().read(); } export enum Tag { Value = 20 }",
            ["main.ts"] = (reverseImports
                ? "import * as right from './right/index'; import * as left from './left/index';"
                : "import * as left from './left/index'; import * as right from './right/index';")
                + "console.log(left.read(), right.read(), new left.Counter().read(), new right.Counter().read(), left.Tag.Value, right.Tag.Value);"
        };
        const string expected = "1 2 1 2 10 20\n";
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.RunModulesCompiled(files, "main.ts"));
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }
}
