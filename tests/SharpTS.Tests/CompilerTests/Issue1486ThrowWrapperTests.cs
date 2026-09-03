using System.Reflection;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class Issue1486ThrowWrapperTests
{
    [Fact]
    public void CreateException_UsesDedicatedCarrierWithoutEagerMessageOrMetadata()
    {
        Assembly assembly = Compile("function noop(): void { }");
        MethodInfo createException = assembly.GetType("$Runtime")!
            .GetMethod("CreateException", BindingFlags.Public | BindingFlags.Static)!;
        var probe = new ToStringProbe();

        var exception = Assert.IsAssignableFrom<Exception>(
            createException.Invoke(null, [probe]));

        Assert.Equal("$ThrownValueException", exception.GetType().Name);
        Assert.Same(probe, exception.GetType().GetProperty("Value")!.GetValue(exception));
        Assert.Equal(0, probe.CallCount);
        Assert.Empty(exception.Data);
        Assert.Equal("probe", exception.Message);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public void ReflectionAndManagedHostUnwrapping_PreserveExactGuestValue()
    {
        Assembly assembly = Compile("""
            const marker: any = {};
            function getMarker(): any { return marker; }
            function fail(): void { throw marker; }
            """);
        Type program = assembly.GetType("$Program")!;
        program.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        MethodInfo getMarker = FindFunction(program, "getMarker");
        MethodInfo fail = FindFunction(program, "fail");
        object marker = getMarker.Invoke(null, null)!;
        var reflectionException = Assert.Throws<TargetInvocationException>(
            () => fail.Invoke(null, null));

        MethodInfo emittedWrap = assembly.GetType("$Runtime")!
            .GetMethod("WrapException", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Same(marker, emittedWrap.Invoke(null, [reflectionException]));
        Assert.Same(marker, RuntimeTypes.WrapException(reflectionException));
    }

    [Fact]
    public void LocalPrimitiveThrow_AllocatesOnlyTheCatchRepresentationBox()
    {
        Assembly assembly = Compile("""
            function run(n: number): number {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    try {
                        if ((i & 1023) === 0) throw i;
                        sum = sum + 1;
                    } catch (error: any) {
                        sum = sum + (error === i ? i : -1);
                    }
                }
                return sum;
            }
            """);
        MethodInfo method = FindFunction(assembly.GetType("$Program")!, "run");
        var run = method.CreateDelegate<Func<double, double>>();

        _ = run(100_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = run(100_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result > 0);
        // 98 local throws require one 24-byte boxed double apiece. The former
        // `error === i` path boxed both sides and allocated about 4.7 KiB.
        Assert.InRange(allocated, 2_300, 3_500);
    }

    private static Assembly Compile(string source)
    {
        var result = CompilationService.Compile(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(result.AssemblyBytes!);
    }

    private static MethodInfo FindFunction(Type program, string name) =>
        program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == name ||
                method.Name.EndsWith("_" + name, StringComparison.Ordinal));

    private sealed class ToStringProbe
    {
        public int CallCount { get; private set; }

        public override string ToString()
        {
            CallCount++;
            return "probe";
        }
    }
}
