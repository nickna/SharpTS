using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ThenableAwaitDeferralTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["pending-native-await-with-stored-result", """
            const log: string[] = [];
            const value: any = { then(resolve: any) {
                log.push('then');
                resolve(7);
                log.push('after');
            } };
            async function run() {
                const result = await Promise.resolve(value);
                log.push('await:' + result);
            }
            const pending = run();
            queueMicrotask(() => log.push('queued'));
            setTimeout(() => console.log(log.join(',')), 0);
            """, "then,after,queued,await:7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PendingNativeAwaitResumesInItsOwnPromiseJob(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
