using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrowCallbackOwnershipTests
{
    public static TheoryData<string, string> Shadows => new()
    {
        { "function apply(callback: (x: number) => number): void { console.log([1, 2].map(callback).join(',')); } apply((x: number): number => x + 100);", "101,102\n" },
        { "{ const callback = (x: number): number => x + 100; console.log([1, 2].map(callback).join(',')); } console.log([1, 2].map(callback).join(','));", "101,102\n2,3\n" },
        { "function apply(): void { const callback = (x: number): number => x + 100; console.log([1, 2].map(callback).join(',')); } apply();", "101,102\n" },
        { "for (const callback of [(x: number): number => x + 100]) { console.log([1, 2].map(callback).join(',')); }", "101,102\n" },
        { "try { throw (x: number): number => x + 100; } catch (callback) { console.log([1, 2].map(callback).join(',')); }", "101,102\n" },
        { "function apply({ callback }: { callback: (x: number) => number }): void { console.log([1, 2].map(callback).join(',')); } apply({ callback: (x: number): number => x + 100 });", "101,102\n" },
        { "class Holder { static { const callback = (x: number): number => x + 100; console.log([1, 2].map(callback).join(',')); } }", "101,102\n" },
        { "const Holder = class { static { const callback = (x: number): number => x + 100; console.log([1, 2].map(callback).join(',')); } };", "101,102\n" },
        { "new (class { run(callback: (x: number) => number): void { console.log([1, 2].map(callback).join(',')); } })().run((x: number): number => x + 100);", "101,102\n" }
    };

    [Theory, MemberData(nameof(Shadows))]
    public void SameNamedBindingKeepsItsCallable(string consumer, string expected)
    {
        string source = "const callback = (x: number): number => x + 1;\n" + consumer;
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNamedModuleCallbacksKeepTheirOwners(bool reverseImports)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "const callback = (x: number): number => x + 1; export function run(): string { return [1, 2].map(callback).join(','); }",
            ["right.ts"] = "const callback = (x: number): number => x + 100; export function run(): string { return [1, 2].map(callback).join(','); }",
            ["main.ts"] = (reverseImports
                ? "import { run as right } from './right'; import { run as left } from './left';"
                : "import { run as left } from './left'; import { run as right } from './right';")
                + "console.log(left()); console.log(right());"
        };
        Assert.Equal("2,3\n101,102\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Equal("2,3\n101,102\n", TestHarness.RunModulesCompiled(files, "main.ts"));
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }
}
