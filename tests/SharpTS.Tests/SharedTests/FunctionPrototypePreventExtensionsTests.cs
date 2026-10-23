using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionPrototypePreventExtensionsTests
{
    [Theory, ModeData]
    public void NonextensibleFunctionPrototypeRejectsNewOwnProperties(ExecutionMode mode)
    {
        var source = """
            const prototype: any = Function.prototype;
            Object.preventExtensions(prototype);
            prototype.fresh = 1;
            console.log(Object.isExtensible(prototype), prototype.fresh === undefined,
                Object.hasOwn(prototype, 'fresh'));
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "false true false\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
