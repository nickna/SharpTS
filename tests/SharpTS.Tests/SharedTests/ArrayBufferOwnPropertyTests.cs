using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayBufferOwnPropertyTests
{
    [Theory, ModeData]
    public void IsViewOwnMembershipMatchesItsDataDescriptor(ExecutionMode mode)
    {
        var source = """
            const target: any = ArrayBuffer;
            const descriptor = Object.getOwnPropertyDescriptor(target, "isView")!;
            console.log(typeof target.isView, typeof descriptor.value, descriptor.value === target.isView);
            console.log(Object.hasOwn(target, "isView"), descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "function function true\ntrue true false true\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
