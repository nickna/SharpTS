using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NativePromiseExpressionParentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-parent", """
            const Parent = class extends Promise<number> {
                constructor(executor: any) { super(executor); }
            };
            class Child extends Parent {}
            console.log(typeof Child);
            """.ReplaceLineEndings("\n") + "\n", "function\n"];
        yield return ["ordinary-expression-parent-control", """
            const Parent = class {
                constructor(executor: any) {}
            };
            class Child extends Parent {}
            console.log(typeof Child);
            """.ReplaceLineEndings("\n") + "\n", "function\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeclaredChildrenRetainEvaluatedNativeParents(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }
}
