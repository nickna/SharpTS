using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class LazyErrorStackTests
{
    private const string SparseThrowSource = """
        function throwErrorSparse(n: number): number {
            let sum: number = 0;
            for (let i: number = 0; i < n; i++) {
                try {
                    if ((i & 1023) === 0) {
                        throw new Error("sparse");
                    }
                    sum = sum + 1;
                } catch (error: any) {
                    sum = sum + (error instanceof Error ? i : -1);
                }
            }
            return sum;
        }
        """;

    [Fact]
    public void SparseThrowWithoutStackRead_AvoidsEagerFormattingAllocation()
    {
        var run = FindFunction(Compile(SparseThrowSource), "throwErrorSparse")
            .CreateDelegate<Func<double, double>>();

        _ = run(10_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(4_966_974, run(100_000));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The eager baseline allocated 1,318,715 bytes for this exact shape.
        // Keep at least the issue's 70% reduction as a permanent gate.
        Assert.True(allocated <= 395_615,
            $"Sparse Error throws allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void FirstReadUsesCreationSiteAndRepeatedReadIsStable()
    {
        const string source = """
            function createError(): Error { return new Error("boom"); }
            function readLater(error: Error): string { return error.stack!; }
            const error = createError();
            const first = readLater(error);
            const second = error.stack!;
            console.log(first.includes("createError"));
            console.log(first.includes("readLater"));
            console.log(first === second);
            """;

        Assert.Equal("true\nfalse\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void SubtypeCauseAndExplicitStackAssignmentRemainObservable()
    {
        const string source = """
            const cause = new Error("root");
            const error = new TypeError("outer", { cause });
            console.log(error.name, error.message, error.cause === cause);
            console.log(typeof error.stack, error.stack === error.stack);
            error.stack = "manual stack";
            console.log(error.stack);
            """;

        Assert.Equal(
            "TypeError outer true\nstring true\nmanual stack\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void UncaughtErrorRetainsItsCreationSiteForHostReporting()
    {
        Assembly assembly = Compile("""
            function explode(): void { throw new Error("uncaught"); }
            """);
        MethodInfo explode = FindFunction(assembly, "explode");
        var reflectionException = Assert.Throws<TargetInvocationException>(
            () => explode.Invoke(null, null));
        MethodInfo wrapException = assembly.GetType("$Runtime")!
            .GetMethod("WrapException", BindingFlags.Public | BindingFlags.Static)!;

        object guestError = wrapException.Invoke(null, [reflectionException])!;
        string stack = Assert.IsType<string>(guestError.GetType()
            .GetProperty("Stack")!.GetValue(guestError));

        Assert.Contains("explode", stack, StringComparison.Ordinal);
    }

    [Fact]
    public void LazyStackAssemblyPassesIlVerification()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(SparseThrowSource));
    }

    private static Assembly Compile(string source)
    {
        var result = CompilationService.Compile(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(result.AssemblyBytes!);
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == name ||
                method.Name.EndsWith("_" + name, StringComparison.Ordinal));
}
