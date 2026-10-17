using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MethodLocalInnerFunctionCaptureTests
{
    [Theory, ModeData]
    public void PublicMethodsShareTypedCapturedParameterBindings(ExecutionMode mode)
    {
        const string source = """
            class C {
                run(value: number, flag: boolean, text: string) {
                    function update() { value = value + 1; flag = !flag; text = text + "!"; }
                    function shadow(value: number) { return value; }
                    update();
                    return value + ":" + flag + ":" + text + ":" + shadow(99);
                }
                static runStatic(value: number = 2, flag: boolean = true, text: string = "S") {
                    function update() { value = value + 1; flag = !flag; text = text + "!"; }
                    function shadow(value: number) { return value; }
                    update();
                    return value + ":" + flag + ":" + text + ":" + shadow(99);
                }
            }
            console.log(new C().run(2, true, "I"), C.runStatic());
            """;
        Assert.Equal("3:false:I!:99 3:false:S!:99\n", TestHarness.Run(source, mode));
    }

    public const string IssueSource = """
        class C {
            static run() {
                let count = 0;
                function next() { count++; return count; }
                console.log(next(), next(), count);
            }
        }
        C.run();
        """;

    private const string FactoryMethod = """
        make(initial: number) {
            let count = initial;
            function next() { count = count + 1; return count; }
            function read() { return count; }
            const arrow = () => ++count;
            count = 10;
            return { next, read, arrow };
        }
        """;

    private const string FactoryCalls = """
        const instance = new C();
        const first: any = instance.make(0), second: any = instance.make(100);
        console.log(first.read(), first.next(), first.arrow(), first.read());
        console.log(second.read(), second.next(), first.read());
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue-static-increment", IssueSource, "1 2 2\n"];
        yield return ["instance-assignment", """
            class C {
                run() {
                    let count = 0;
                    function next() { count = count + 1; return count; }
                    console.log(next(), next(), count);
                }
            }
            new C().run();
            """, "1 2 2\n"];
        yield return ["same-named-instance-static", """
            class C {
                run() {
                    let count = 20;
                    function next() { return ++count; }
                    console.log(next(), count);
                }
                static run() {
                    let count = 40;
                    function next() { return count++; }
                    console.log(next(), count);
                }
            }
            new C().run(); C.run();
            """, "21 21\n40 41\n"];
        yield return ["declaration-live-siblings", $$"""
            class C { {{FactoryMethod}} }
            {{FactoryCalls}}
            """, "10 11 12 12\n10 11 12\n"];
        yield return ["expression-live-siblings", $$"""
            const C = class Named { {{FactoryMethod}} };
            {{FactoryCalls}}
            """, "10 11 12 12\n10 11 12\n"];
        yield return ["nested-declaration-forwarding", """
            class C {
                static run() {
                    let count = 1;
                    function outer() {
                        count = count + 1;
                        function next() { count++; }
                        next();
                    }
                    count = 5;
                    outer(); outer();
                    console.log(count);
                }
            }
            C.run();
            """, "9\n"];
        yield return ["shadowed-inner-binding", """
            class C {
                run() {
                    let count = 1;
                    const read = () => count;
                    function outer() {
                        let count = 10;
                        function next() { count = count + 1; return count; }
                        console.log(next(), count);
                    }
                    outer();
                    console.log(read(), count);
                }
            }
            new C().run();
            """, "11 11\n1 1\n"];
    }

    public static IEnumerable<object[]> ParityCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void OrdinaryNestedFunctionsShareTheirMethodLocalBindings(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    public static Dictionary<string, string> ModuleFiles() => new()
    {
        ["left.ts"] = """
            export class C { static run() {
                let count = 1;
                function next() { count++; return count; }
                console.log(next(), count);
            } }
            """,
        ["right.ts"] = """
            export class C { static run() {
                let count = 10;
                function next() { count = count + 1; return count; }
                console.log(next(), count);
            } }
            """,
        ["main.ts"] = """
            import { C as Left } from './left';
            import { C as Right } from './right';
            Left.run(); Right.run(); Left.run();
            """
    };

    [Theory, ModeData]
    public void SameNamedMethodsInDifferentModulesUseTheirOwnCaptureStorage(ExecutionMode mode)
        => Assert.Equal("2 2\n11 11\n2 2\n", TestHarness.RunModules(ModuleFiles(), "main.ts", mode));
}
