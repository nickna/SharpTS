using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericStaticFieldInheritanceControlTests
{
    [Theory, ModeData]
    public void ClosedGenericParentRetainsInitializedStaticValue(ExecutionMode mode)
    {
        var source = """
            class Generic<T> { static regular = 5; }
            class Derived extends Generic<number> {}
            const base: any = Generic, derived: any = Derived;
            console.log(Generic.regular, Derived.regular, base.regular, derived.regular);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal("5 5 5 5\n", output);
        }
        else Assert.Equal("5 5 5 5\n", TestHarness.RunInterpreted(source));
    }
}
