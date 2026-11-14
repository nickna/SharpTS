using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class MutableFunctionClosureTests
{
    public const string NestedBlockShadowSource = """
            function value(): number { return 1; }
            const moduleRead = () => value();
            value = () => 2;
            console.log(moduleRead());
            {
                const value = () => 10;
                const read = () => () => value();
                console.log(read()());
                const deepRead = () => () => () => value();
                console.log(deepRead()()());
            }
            console.log(moduleRead());
            """;

    [Theory, ModeData]
    public void NestedClosuresKeepTopLevelBlockShadows(ExecutionMode mode)
    {
        Assert.Equal("2\n10\n10\n2\n", TestHarness.Run(NestedBlockShadowSource, mode));
    }

    [Theory, ModeData]
    public void ClosuresReadAndWriteTheLiveSourceFunction(ExecutionMode mode)
    {
        const string source = """
            function value(): number { return 1; }
            const read = () => value();
            const readValue = function () { return value; };
            const nested = () => () => value();
            const write = () => { value = () => 3; };
            const Holder = class { static { value = () => 2; } };
            console.log(value(), read(), readValue()(), nested()());
            write();
            console.log(value(), read(), readValue()(), nested()());
            function shadow(value: () => number) { return () => value(); }
            console.log(shadow(() => 10)());
            """;
        Assert.Equal("2 2 2 2\n3 3 3 3\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuleClosuresKeepTheirOwnFunctionBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["first.ts"] = """
                function value(): number { return 1; }
                export const read = () => value();
                export const readAsync = async () => value();
                export function change() { value = () => 2; }
                """,
            ["second.ts"] = """
                function value(): number { return 10; }
                export const read = () => value();
                value = () => 20;
                """,
            ["main.ts"] = """
                import { read, readAsync, change } from './first';
                import { read as second } from './second';
                async function main() {
                    console.log(read(), await readAsync(), second());
                    change();
                    console.log(read(), await readAsync(), second());
                }
                main();
                """
        };
        Assert.Equal("1 1 20\n2 2 20\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
