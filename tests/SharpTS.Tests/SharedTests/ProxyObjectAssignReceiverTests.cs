using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProxyObjectAssignReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-string-and-symbol-getter-receivers", """
            const key = Symbol('value');
            const target: any = {};
            let stringReceiver: any, symbolReceiver: any;
            Object.defineProperty(target, 'value', { get() { stringReceiver = this; return 3; }, enumerable: true });
            Object.defineProperty(target, key, { get() { symbolReceiver = this; return 5; }, enumerable: true });
            const proxy: any = new Proxy(target, {});
            const copy: any = Object.assign({}, proxy);
            console.log(copy.value, copy[key], stringReceiver === proxy, symbolReceiver === proxy);
            """.ReplaceLineEndings("\n") + "\n", "3 5 true true\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [row[0], row[1], row[2], mode];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ObjectAssignReadsStringAndSymbolGettersWithTheProxyReceiver(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
