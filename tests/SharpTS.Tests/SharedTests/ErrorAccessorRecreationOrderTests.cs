using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ErrorAccessorRecreationOrderTests
{
    [Theory, ModeData]
    public void RecreatedAccessorsKeepTheirCreationOrder(ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            const target: any = new Error('probe');
            for (let phase = 0; phase < 2; phase++) {
                Object.defineProperty(target, 'first', { get() { reads++; return 1; }, enumerable: true, configurable: true });
                Object.defineProperty(target, 'second', { get() { reads++; return 2; }, enumerable: true, configurable: true });
                console.log(Object.keys(target).filter(key => key === 'first' || key === 'second').join(','), Object.getOwnPropertyNames(target).filter(key => key === 'first' || key === 'second').join(','), reads);
                delete target.first;
                delete target.second;
            }
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "first,second first,second 0\nfirst,second first,second 0\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory, ModeData]
    public void DeletingAccessorsDuringEnumerationDoesNotYieldStaleKeys(ExecutionMode mode)
    {
        var source = """
            class C { stable = 10; }
            const target: any = new C();
            let reads = 0;
            Object.defineProperty(target, 'first', { get() { reads++; return 1; }, enumerable: true, configurable: true });
            Object.defineProperty(target, 'second', { get() { reads++; return 2; }, enumerable: true, configurable: true });
            const seen: string[] = [];
            for (const key in target) {
                seen.push(key);
                if (key === 'stable') { delete target.first; delete target.second; }
            }
            console.log(seen.join(','), Object.keys(target).join(','), reads);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "stable stable 0\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
