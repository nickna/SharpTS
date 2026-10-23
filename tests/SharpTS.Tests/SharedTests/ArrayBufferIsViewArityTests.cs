using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayBufferIsViewArityTests
{
    [Theory, ModeData]
    public void SurplusArgumentsAreEvaluatedAndIgnored(ExecutionMode mode)
    {
        var source = """
            const state = { calls: 0 };
            function sideEffect() { state.calls++; return 2; }
            console.log((ArrayBuffer.isView as any)(new Uint8Array(1), sideEffect()), state.calls);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true 1\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
