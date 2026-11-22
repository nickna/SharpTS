using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvalAsyncDeclarationTests
{
    [Theory, ModeData]
    public void DirectAndIndirectEvalDeclarationsUseAsyncStateMachines(ExecutionMode mode)
    {
        const string source = """
            eval('async function direct(){ await 0; console.log(7); } direct();');
            (0, eval)('async function indirect(){ await 0; console.log(8); } indirect();');
            """;
        Assert.Equal("7\n8\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncDeclarationIsHoistedAndRetainsFunctionMetadata(ExecutionMode mode)
    {
        const string source = """
            eval('console.log(run.name, run.length); run(4).then(value => console.log(value)); async function run(value){ await 0; return value + 1; }');
            """;
        Assert.Equal("run 1\n5\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncDeclarationCapturesExistingCallerBindingsAndCanEscape(ExecutionMode mode)
    {
        const string source = """
            function make(value) {
                return eval('async function run(extra = 2){ await 0; return value + extra; } run;');
            }
            const a = make(3), b = make(10);
            console.log(a === b);
            a().then(value => console.log(value));
            b(4).then(value => console.log(value));
            """;
        Assert.Equal("false\n5\n14\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncDeclarationRejectsThroughTheReturnedPromise(ExecutionMode mode)
    {
        const string source = """
            eval('async function fail(){ await 0; throw new Error("boom"); } fail().catch(error => console.log(error.message));');
            """;
        Assert.Equal("boom\n", TestHarness.RunUnchecked(source, mode));
    }
}
