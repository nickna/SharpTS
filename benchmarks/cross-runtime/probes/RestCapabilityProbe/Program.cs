using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

// Input: NumericRest.ts compiled by a frozen compiler. No SharpTS reference is
// needed here, so the same probe measures each emitted runtime independently.
string path = Path.GetFullPath(args.Single());
var assembly = Assembly.LoadFrom(path);
const BindingFlags methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
var program = assembly.GetType("$Program")!;
program.GetMethod("Main", methods)!.Invoke(null, null);
MethodInfo Find(string name) => program.GetMethods(methods).Single(m => m.Name == name);
var wrapper = assembly.GetType("$TSFunction")!;
var constructor = wrapper.GetConstructor([typeof(object), typeof(MethodInfo)])!;
var run = Find("restUnknownTarget").CreateDelegate<Func<double, object, double>>();
var wrappers = new List<object>();
var calls = new List<object>();

foreach (string name in new[] { "restAdd4", "restMutatingExtra" })
{
    var target = Find(name);
    var create = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor,
        Expression.Constant(null, typeof(object)), Expression.Constant(target, typeof(MethodInfo))),
        typeof(object))).Compile();
    object callable = create();
    for (int i = 0; i < 1000; i++) callable = create();
    long before = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    for (int i = 0; i < 10000; i++) callable = create();
    double setupMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    long setupBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    wrappers.Add(new
    {
        target = name,
        hasCapability = wrapper.GetField("_numericRest4", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(callable) != null,
        bytesPerConstruction = setupBytes / 10000d,
        microsecondsPerConstruction = setupMs / 10d
    });

    foreach (int n in new[] { 10000, 100000 })
    {
        double expected = 0.5 + (double)n * (n - 1) / 2 + (name == "restAdd4" ? 6 : 7) * n;
        var warmup = Stopwatch.StartNew();
        do { Check(run(n, callable)); } while (warmup.ElapsedMilliseconds < 500);
        started = Stopwatch.GetTimestamp();
        Check(run(n, callable));
        int repeats = Math.Clamp((int)Math.Ceiling(2 / Stopwatch.GetElapsedTime(started).TotalMilliseconds), 3, 10000);
        var samples = new double[7];
        long allocated = 0;
        double guard = 0;
        for (int sample = 0; sample < samples.Length; sample++)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            for (int repeat = 0; repeat < repeats; repeat++) guard += run(n, callable);
            samples[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / repeats;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(run(n, callable));
        GC.KeepAlive(guard);
        calls.Add(new
        {
            target = name, n, innerRepeats = repeats,
            meanMs = samples.Average(), minMs = samples.Min(), maxMs = samples.Max(),
            measurementsMs = samples,
            bytesPerInvocation = allocated / ((double)samples.Length * repeats),
            bytesPerInnerCall = allocated / ((double)samples.Length * repeats * n)
        });
        void Check(double actual)
        {
            if (actual != expected) throw new InvalidOperationException($"{name}/{n}: {actual} != {expected}");
        }
    }
    GC.KeepAlive(callable);
}

int IlSize(MethodBase method) => method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0;
var allMethods = assembly.GetTypes().SelectMany(type =>
    type.GetMethods(methods | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
        .Concat(type.GetConstructors(methods | BindingFlags.Instance | BindingFlags.DeclaredOnly))).ToArray();
var result = new
{
    schemaVersion = 1,
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
    assemblyBytes = new FileInfo(path).Length,
    emittedIlBytes = allMethods.Sum(IlSize),
    programMethods = program.GetMethods(methods).Select(method => new { method.Name, ilBytes = IlSize(method) }),
    wrapperConstruction = wrappers,
    calls
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
