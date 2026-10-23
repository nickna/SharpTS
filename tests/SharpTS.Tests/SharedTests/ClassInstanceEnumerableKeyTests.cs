using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstanceEnumerableKeyTests
{
    [Theory, ModeData]
    public void ObjectKeysExcludesNonEnumerableDataProperties(ExecutionMode mode)
    {
        var source = """
            class C {}
            const target: any = new C();
            Object.defineProperty(target, 'hidden', { value: 7, enumerable: false, configurable: true });
            console.log(Object.keys(target).length, Object.getOwnPropertyDescriptor(target, 'hidden')!.enumerable);
            """.ReplaceLineEndings("\n") + "\n";

        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        Assert.Equal("0 false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectKeysIncludesEnumerableAccessorsWithoutInvokingGetters(ExecutionMode mode)
    {
        var source = """
            class C { visible = 1; }
            const target: any = new C();
            let reads = 0;
            Object.defineProperty(target, 'hidden', { value: 7, enumerable: false });
            Object.defineProperty(target, 'silent', { get() { reads++; return 8; }, enumerable: false });
            Object.defineProperty(target, 'shown', { get() { reads++; return 9; }, enumerable: true });
            console.log(Object.keys(target).join(','), reads, Object.getOwnPropertyNames(target).includes('hidden'), Object.getOwnPropertyNames(target).includes('silent'));
            """.ReplaceLineEndings("\n") + "\n";

        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        Assert.Equal("visible,shown 0 true true\n", TestHarness.Run(source, mode));
    }
}
