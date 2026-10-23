using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ParameterDefaultCapturedBodyTests
{
    public const string IssueSource = """
        let value = 100;
        function run(initial: number = value) {
            const read = () => value;
            try { read(); } catch (error: any) { console.log(error.name); }
            let value = 42;
            console.log(initial, read());
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-scalar-default", IssueSource, "ReferenceError\n100 42\n"];
        yield return ["without-body-capture-control", """
            let value = 100;
            function run(initial: number = value) {
                let value = 42;
                console.log(initial, value);
            }
            run();
            """, "100 42\n"];
        yield return ["outer-default-before-body-tdz", """
            let value = 100;
            function observe(current: number): number {
                console.log("default", current);
                return current;
            }
            function run(initial: number = observe(value)) {
                const read = () => value;
                try { read(); } catch (error: any) { console.log("body", error.name); }
                let value = 42;
                console.log(initial, read());
            }
            run();
            console.log(value);
            """, "default 100\nbody ReferenceError\n100 42\n100\n"];
        yield return ["earlier-captured-parameter", """
            function run(first: number = 7, second: number = first) {
                const read = () => first;
                console.log(first, second, read());
            }
            run();
            run(11);
            run(undefined, 13);
            """, "7 7 7\n11 11 11\n7 13 7\n"];
        yield return ["supplied-scalar-skips-default", """
            let value = 100;
            let calls = 0;
            function fallback(current: number): number {
                calls++;
                console.log("default", current);
                return current;
            }
            function run(initial: number = fallback(value)) {
                const read = () => value;
                let value = 42;
                console.log(initial, read(), calls);
            }
            run(5);
            run(undefined);
            console.log(value, calls);
            """, "5 42 0\ndefault 100\n100 42 1\n100 1\n"];
        yield return ["default-assignment-writes-outer-binding", """
            let value = 100;
            function run(initial: number = (value = 101)) {
                const read = () => value;
                try { read(); } catch (error: any) { console.log(error.name); }
                let value = 42;
                console.log(initial, read());
            }
            run();
            console.log(value);
            """, "ReferenceError\n101 42\n101\n"];
        yield return ["default-call-resolves-outer-helper", """
            function helper(): number { return 100; }
            function run(initial: number = helper()) {
                const read = () => helper;
                try { read(); } catch (error: any) { console.log(error.name); }
                let helper = 42;
                console.log(initial, read());
            }
            run();
            console.log(helper());
            """, "ReferenceError\n100 42\n100\n"];
        yield return ["factory-parameter-default", """
            function factory(value: number) {
                function run(initial: number = value) {
                    const read = () => value;
                    let value = 42;
                    console.log(initial, read());
                }
                return run;
            }
            const first = factory(100), second = factory(200);
            first();
            second();
            first(5);
            """, "100 42\n200 42\n5 42\n"];
        yield return ["factory-lexical-default", """
            function factory(initial: number) {
                const value = initial;
                function run(start: number = value) {
                    const read = () => value;
                    let value = 42;
                    console.log(start, read());
                }
                return run;
            }
            const first = factory(100), second = factory(200);
            first();
            second();
            first(5);
            """, "100 42\n200 42\n5 42\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ScalarDefaultsResolveBeforeCapturedBodyBindings(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
