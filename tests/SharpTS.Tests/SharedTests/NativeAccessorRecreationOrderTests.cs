using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NativeAccessorRecreationOrderTests
{
    public static string Source => """
        for (const source of [new Date(0), new Error('probe')] as any[]) {
            for (const phase of ['fresh', 'recreated']) {
                const state = { trace: '' }, first = { value: 1 }, second = { value: 2 };
                Object.defineProperty(source, '__first', {
                    enumerable: true, configurable: true,
                    get() { state.trace += 'first,'; return first; }
                });
                Object.defineProperty(source, '__second', {
                    enumerable: true, configurable: true,
                    get() { state.trace += 'second,'; return second; }
                });
                console.log(phase, Object.keys(source).filter(key => key === '__first' || key === '__second').join(','),
                    Object.getOwnPropertyNames(source).filter(key => key === '__first' || key === '__second').join(','));
                const target: any = {};
                Object.defineProperties(target, source);
                console.log(state.trace, target.__first, target.__second);
                delete source.__first;
                delete source.__second;
            }
        }
        """.ReplaceLineEndings("\n") + "\n";

    public const string Expected = "fresh __first,__second __first,__second\nfirst,second, 1 2\nrecreated __first,__second __first,__second\nfirst,second, 1 2\nfresh __first,__second __first,__second\nfirst,second, 1 2\nrecreated __first,__second __first,__second\nfirst,second, 1 2\n";

    // The report is compiler-specific; historical Date own-name omission is separate.
    [Fact]
    public void CompiledRecreationPreservesNativeAccessorCreationOrder()
    {
        var source = Source;
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(Expected, output);
    }

    [Fact]
    public void InterpretedRecreationPreservesNativeAccessorCreationOrder()
    {
        var source = Source;
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        Assert.Equal(Expected, TestHarness.RunInterpreted(source));
    }
}
