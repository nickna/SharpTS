using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ForInAccessorDeletionGeneratorTests
{
    [Theory, ModeData]
    public void GeneratorRechecksDeletedAccessorsAfterYieldWithoutReevaluatingReceiver(ExecutionMode mode)
    {
        var source = """
            class C { stable = 10; }
            const target: any = new C();
            let calls = 0;
            let reads = 0;
            Object.defineProperty(target, 'first', { get() { reads++; return 1; }, enumerable: true, configurable: true });
            Object.defineProperty(target, 'second', { get() { reads++; return 2; }, enumerable: true, configurable: true });
            function receiver(): any { calls++; return target; }
            function* keys() {
                for (const key in receiver()) {
                    yield key;
                    if (key === 'stable') { delete target.first; delete target.second; }
                }
            }
            const iterator = keys();
            console.log(iterator.next().value, calls, reads);
            console.log(iterator.next().done, calls, reads, Object.keys(target).join(','));
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "stable 1 0\ntrue 1 0 stable\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
