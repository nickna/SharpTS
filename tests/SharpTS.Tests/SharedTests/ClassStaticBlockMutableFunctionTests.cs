using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassStaticBlockMutableFunctionTests
{
    public static IEnumerable<object[]> CapturedNameCollisionCases()
    {
        foreach (var declaration in new[] { "class Holder", "const Holder = class" })
        foreach (bool capturedFirst in new[] { true, false })
        {
            string capturedImport = "import { readCaptured, bumpCaptured } from './captured';";
            string functionImport = "import { readFunction } from './function';";
            string imports = capturedFirst
                ? capturedImport + "\n" + functionImport
                : functionImport + "\n" + capturedImport;
            yield return [$"{(declaration.StartsWith("class") ? "declaration" : "expression")}-{(capturedFirst ? "captured-first" : "function-first")}",
                new Dictionary<string, string>
                {
                    ["captured.ts"] = """
                        let value = 100;
                        export function readCaptured(): number { return value; }
                        export function bumpCaptured(): void { value++; }
                        """,
                    ["function.ts"] = $$"""
                        function value(): number { return 1; }
                        {{declaration}} {
                            static { console.log(value()); value = () => 2; console.log(value()); }
                        };
                        export function readFunction(): number { return value(); }
                        """,
                    ["main.ts"] = imports + """

                        console.log(readCaptured(), readFunction());
                        bumpCaptured();
                        console.log(readCaptured(), readFunction());
                        """
                }, "1\n2\n100 2\n101 2\n"];
        }
    }

    public static IEnumerable<object[]> CapturedNameCollisionModeCases() => CapturedNameCollisionCases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, SerializableMemberData(nameof(CapturedNameCollisionModeCases))]
    public void CapturesInAnotherModuleDoNotSuppressMutableFunctionStorage(
        string name, Dictionary<string, string> files, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }

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
