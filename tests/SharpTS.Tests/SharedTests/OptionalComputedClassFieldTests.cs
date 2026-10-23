using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class OptionalComputedClassFieldTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["optional-literal", """
            class C { ["field"]?: number; }
            const instance: any = new C();
            console.log(instance.field === undefined);
            """ + "\n", "true\n"];
        yield return ["optional-expression", """
            const C = class { ["field"]?: number; };
            console.log(typeof C);
            """ + "\n", "function\n"];
        yield return ["optional-parse-control", """
            class C { field?: number; }
            console.log(typeof C);
            """ + "\n", "function\n"];
        yield return ["initialized-literal-control", """
            class C { ["field"] = 1; }
            const instance: any = new C();
            console.log(instance.field);
            """ + "\n", "1\n"];
        yield return ["initialized-string-control", """
            const key = "field";
            class C { [key] = 1; }
            const instance: any = new C();
            console.log(instance[key]);
            """ + "\n", "1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedFieldsRetainOptionalSyntaxAndInitialization(
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
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
