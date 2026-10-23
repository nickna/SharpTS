using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NestedBindingPatternDefaultTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-nested-object-binding-default", """
            function run({ nested: { count = 3 } = {} }: any = {}) {
                console.log(count);
            }
            run();
            """, "3\n"];
        yield return ["provided-nested-object-bypasses-default", """
            function run({ nested: { count = 3 } = {} }: any = {}) {
                console.log(count);
            }
            run({ nested: { count: 7 } });
            """, "7\n"];
        yield return ["nested-array-binding-default", """
            function run({ nested: [count = 3] = [] }: any = {}) {
                console.log(count);
            }
            run();
            """, "3\n"];
        yield return ["nested-default-fallback-side-effect", """
            let defaults = 0;
            function fallback(): any {
                defaults++;
                return {};
            }
            function run({ nested: { count = 3 } = fallback() }: any = {}) {
                console.log(count, defaults);
            }
            run();
            run({ nested: { count: 7 } });
            """, "3 1\n7 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NestedBindingPatternsApplyTheirOwnDefaultsOnce(
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
