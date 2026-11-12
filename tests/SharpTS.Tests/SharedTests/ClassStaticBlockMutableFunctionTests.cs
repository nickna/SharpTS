using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassStaticBlockMutableFunctionTests
{
    [Theory, ModeData]
    public void MutableSourceFunctionsRemainScopedToTheirOwnModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["first.ts"] = """
                function value(): number { return 1; }
                class Holder { static { value = () => 2; console.log(value()); } }
                export function readFirst(): number { return value(); }
                """,
            ["second.ts"] = """
                function value(): number { return 10; }
                const Holder = class { static { value = () => 20; console.log(value()); } };
                export function readSecond(): number { return value(); }
                """,
            ["main.ts"] = """
                import { readFirst } from './first';
                import { readSecond } from './second';
                console.log(readFirst(), readSecond());
                """
        };
        Assert.Equal("2\n20\n2 20\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
