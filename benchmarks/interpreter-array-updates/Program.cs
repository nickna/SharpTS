using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Parsing;

int iterations = args.Length > 0 ? int.Parse(args[0]) : 20_000;
int samples = args.Length > 1 ? int.Parse(args[1]) : 9;
if (iterations < 1 || samples < 3) throw new ArgumentOutOfRangeException(nameof(args));

var cases = new[]
{
    (Name: "dense-number", Setup: "const a=[0,0,0,0];", Body: "a[0]++;++a[1];a[2]--;--a[3];",
        Print: "console.log(a[0],a[1],a[2],a[3]);", Expected: $"{iterations} {iterations} {-iterations} {-iterations}\n"),
    (Name: "fractional-key", Setup: "const a=[];a[0.5]=0;a[1.5]=0;a[2.5]=0;a[3.5]=0;",
        Body: "a[0.5]++;++a[1.5];a[2.5]--;--a[3.5];",
        Print: "console.log(a[0.5],a[1.5],a[2.5],a[3.5]);", Expected: $"{iterations} {iterations} {-iterations} {-iterations}\n"),
    (Name: "accessor", Setup: "const a=[0];let value=0,reads=0,writes=0;Object.defineProperty(a,'0',{get(){reads++;return value},set(v){writes++;value=v}});",
        Body: "a[0]++;", Print: "console.log(value,reads,writes);", Expected: $"{iterations} {iterations} {iterations}\n"),
    (Name: "object-key", Setup: "const a=[0];let conversions=0;const key={[Symbol.toPrimitive](){conversions++;return '0'}};",
        Body: "a[key]++;", Print: "console.log(a[0],conversions);", Expected: $"{iterations} {2 * iterations}\n"),
    (Name: "frozen", Setup: "const a=Object.freeze([0]);", Body: "a[0]++;",
        Print: "console.log(a[0]);", Expected: "0\n"),
};

var results = new List<object>();
foreach (var item in cases)
{
    string source = item.Setup + $"for(let i=0;i<{iterations};i++){{{item.Body}}}" + item.Print;
    var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
    var timings = new List<double>();
    var allocations = new List<long>();
    for (int sample = -3; sample < samples; sample++)
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        interpreter.Interpret(statements);
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (interpreter.LastUncaughtError is { } error) throw new InvalidOperationException(item.Name, error);
        if (output.ToString().Replace("\r\n", "\n") != item.Expected)
            throw new InvalidOperationException($"Unexpected output in {item.Name}: {output}");
        if (sample < 0) continue;
        timings.Add(elapsed);
        allocations.Add(allocated);
    }
    results.Add(new { item.Name, Source = source, Milliseconds = timings, AllocatedBytes = allocations,
        MedianMilliseconds = timings.Order().ElementAt(samples / 2),
        MedianAllocatedBytes = allocations.Order().ElementAt(samples / 2) });
}

string assembly = typeof(Interpreter).Assembly.Location;
Console.WriteLine(JsonSerializer.Serialize(new {
    Assembly = assembly,
    AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))),
    Runtime = Environment.Version.ToString(), ProcessorCount = Environment.ProcessorCount,
    Iterations = iterations, Samples = samples, Warmups = 3,
    UnsupportedBaselineControls = new[] { "Canonical numeric String updates throw; tracked by #3473, excluded from timing." },
    Results = results
}, new JsonSerializerOptions { WriteIndented = true }));
