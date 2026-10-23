using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericNativeArrayConstructorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["generic-native-array-declaration-construction", """
            class Values<T> extends Array<T> {}
            const numbers = new Values<number>(1, 2);
            console.log(numbers.length);
            """.ReplaceLineEndings("\n") + "\n", "2\n"];
        yield return ["concrete-native-array-declaration-construction", """
            class Values extends Array<number> {}
            const numbers = new Values(1, 2);
            console.log(numbers.length);
            """.ReplaceLineEndings("\n") + "\n", "2\n"];
        yield return ["generic-native-array-expression-construction", """
            const Values = class<T> extends Array<T> {};
            const numbers = new Values<number>(1, 2);
            console.log(numbers.length);
            """.ReplaceLineEndings("\n") + "\n", "2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NativeArrayConstructionPreservesAllArguments(
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
