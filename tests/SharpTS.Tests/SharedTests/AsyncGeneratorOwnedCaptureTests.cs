using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorOwnedCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["scalar-default", """
            async function* run(value: any, read: any = () => value) {
                yield read();
                value = 2;
                yield read();
            }
            const iterator = run(1);
            iterator.next().then(first => {
                console.log("first", first.value);
                return iterator.next();
            }).then(second => console.log("second", second.value));
            """, "first 1\nsecond 2\n"];
        yield return ["body-pattern", """
            async function* run([value]: any[]) {
                const read = () => value;
                yield read();
                value = 2;
                yield read();
            }
            const iterator = run([1]);
            iterator.next().then(first => {
                console.log("first", first.value);
                return iterator.next();
            }).then(second => console.log("second", second.value));
            """, "first 1\nsecond 2\n"];
        yield return ["body-scalar-read-closure", """
            async function* run(value: any) {
                const read = () => value;
                yield read();
                value = 2;
                yield read();
            }
            const iterator = run(1);
            iterator.next().then(first => {
                console.log("first", first.value);
                return iterator.next();
            }).then(second => console.log("second", second.value));
            """, "first 1\nsecond 2\n"];
        yield return ["child-write-capture-control", """
            async function* run(value: any, read: any = () => value, write: any = (next: any) => value = next) {
                yield read();
                write(2);
                yield read();
            }
            const iterator = run(1);
            iterator.next().then(first => {
                console.log("first", first.value);
                return iterator.next();
            }).then(second => console.log("second", second.value));
            """, "first 1\nsecond 2\n"];
        yield return ["block-shadow-preserves-own-parameter", """
            async function* run(value: any, read: any = () => value) {
                yield read();
                {
                    let value: any = 9;
                    value = 10;
                    const shadow = () => value;
                    yield shadow();
                }
                value = 2;
                yield read();
            }
            const iterator = run(1);
            iterator.next().then(first => {
                console.log("first", first.value);
                return iterator.next();
            }).then(shadow => {
                console.log("shadow", shadow.value);
                return iterator.next();
            }).then(second => console.log("second", second.value));
            """, "first 1\nshadow 10\nsecond 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CapturedParametersStayLiveAcrossYields(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
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
