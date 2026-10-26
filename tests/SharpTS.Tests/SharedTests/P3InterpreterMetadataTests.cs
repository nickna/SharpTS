using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class P3InterpreterMetadataTests
{
    [Theory, ModeData]
    public void BoundFunctionDeletedMetadataAgreesWithOwnDescriptors(ExecutionMode mode)
    {
        AssertOutput("""
            function f() {}
            const bound: any = f.bind(null);
            for (const key of ["length", "name"]) {
                console.log(Object.hasOwn(bound, key));
                console.log(delete bound[key], Object.hasOwn(bound, key),
                    Object.prototype.hasOwnProperty.call(bound, key),
                    Object.getOwnPropertyDescriptor(bound, key) === undefined);
                Object.defineProperty(bound, key, { value: 7, configurable: true });
                console.log(Object.hasOwn(bound, key), bound[key]);
            }
            """, "true\ntrue false false true\ntrue 7\ntrue\ntrue false false true\ntrue 7\n", mode);
    }

    private static void AssertOutput(string source, string expected, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
