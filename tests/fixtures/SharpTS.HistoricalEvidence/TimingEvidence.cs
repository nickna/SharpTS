using System.Reflection;
using System.Text.Json;
using SharpTS.Tests.CompilerTests;

internal static class TimingEvidence
{
    public static void Collect(string reportPath)
    {
        // Recover the exact pre-c85a5505 single-sample workload, warmup, and budget.
        const string source = """
            function hot(iterations: number): number {
                const tape = new Uint8Array(1);
                let i: number = 0;
                while (i < iterations) {
                    tape[0] = (tape[0] + 1) & 255;
                    i = i + 1;
                }
                return tape[0];
            }
            """;
        var testType = typeof(NumericBitwiseLoweringTests);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var assembly = (Assembly)testType.GetMethod("Compile", flags)!.Invoke(null, [source])!;
        var method = (MethodInfo)testType.GetMethod("FindFunction", flags)!.Invoke(null, [assembly, "hot"])!;
        var hot = method.CreateDelegate<Func<double, double>>();
        double warmup = hot(1_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = hot(1_000_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            source, runtime = Environment.Version.ToString(),
            warmupIterations = 1_000, expectedWarmup = 232, warmup,
            iterations = 1_000_000, expectedResult = 64, result,
            budgetBytes = 4_096, allocated,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
