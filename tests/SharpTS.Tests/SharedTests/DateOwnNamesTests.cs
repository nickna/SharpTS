using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DateOwnNamesTests
{
    [Theory, ModeData]
    public void OwnPropertyNamesIncludeAccessorsWithoutInvokingThem(ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            const target: any = new Date(0);
            Object.defineProperty(target, 'probe', { get() { reads++; return 7; }, configurable: true });
            console.log(Object.getOwnPropertyNames(target).includes('probe'), Object.hasOwn(target, 'probe'), reads);
            console.log(target.probe, reads);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true true 0\n7 1\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
