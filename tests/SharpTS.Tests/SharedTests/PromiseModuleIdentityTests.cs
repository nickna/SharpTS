using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseModuleIdentityTests
{
    public static IEnumerable<object[]> ImportOrders =>
    [
        new object[] { "import * as filesystem from 'node:fs/promises'; import * as dns from 'node:dns/promises'; import * as timers from 'node:timers/promises';" },
        new object[] { "import * as timers from 'node:timers/promises'; import * as dns from 'node:dns/promises'; import * as filesystem from 'node:fs/promises';" }
    ];

    [Theory, MemberData(nameof(ImportOrders))]
    public void PromiseModuleImportsKeepDistinctExports(string imports)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = imports + "\nconsole.log(typeof filesystem.readFile, typeof dns.lookup, typeof timers.setTimeout); timers.setTimeout(1, 'ready').then(value => console.log(value));"
        };
        const string expected = "function function function\nready\n";
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void SameFilenameModulesKeepIndependentExportFields(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["left/promises.ts"] = "export const value = 'left';",
            ["right/promises.ts"] = "export const value = 'right';",
            ["main.ts"] = "import { value as left } from './left/promises'; import { value as right } from './right/promises'; console.log(left, right);"
        };
        Assert.Equal("left right\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
