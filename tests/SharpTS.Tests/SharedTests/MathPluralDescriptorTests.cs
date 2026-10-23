using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MathPluralDescriptorTests
{
    [Theory, ModeData]
    public void PluralDescriptorsIncludeOwnedIntrinsicMathMethods(ExecutionMode mode)
    {
        var source = """
            const descriptors = Object.getOwnPropertyDescriptors(Math);
            console.log(Object.hasOwn(descriptors, 'max'), descriptors.max?.value === Math.max);
            console.log(Object.hasOwn(Math, 'max'), typeof Math.max);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true true\ntrue function\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
