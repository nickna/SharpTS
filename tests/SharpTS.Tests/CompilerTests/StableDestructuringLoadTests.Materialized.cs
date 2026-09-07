using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed partial class StableDestructuringLoadTests
{
    private static string MaterializedLoopSource(bool carrier, string x = "1", string y = "2") => $$"""
        type Point = { x: number; y: number };
        function reduce(n: number): number {
            {{(carrier ? $"const points: Point[] = []; points.push({{ x: {x}, y: {y} }}); const point: Point = points[0];"
                : $"const point: Point = {{ x: {x}, y: {y} }};")}}
            const dynamicPoint: any = point;
            dynamicPoint.extra = true;
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                const { x, y } = point;
                total = total + x + y;
            }
            return total;
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializedNumericLoops_HoistReadsAndRetainFixedAllocations(bool carrier)
    {
        string source = MaterializedLoopSource(carrier);
        Assembly assembly = Compile(source);
        MethodInfo method = FindFunction(assembly, "reduce");
        var instructions = ReadInstructions(method).ToArray();
        Assert.Contains(instructions, i => i.OpCode == OpCodes.Newobj &&
            (carrier ? i.Member?.DeclaringType?.Name.StartsWith("$CompactObjectRecord", StringComparison.Ordinal) == true
                : i.Member?.DeclaringType == typeof(Dictionary<string, object>)));
        Assert.Contains(instructions, i => i.Member?.Name == "HasPropertyDescriptors");
        if (carrier)
            Assert.Contains(instructions, i => i.Member?.Name == "TryGetMaterializedDictionary");

        // Both numeric loops must be free of property operations; the generic
        // fallback remains in the method and is not a reason to fail this check.
        var numericLoops = instructions.Where(i => i.BranchTarget is { } target && target < i.Offset)
            .Select(edge => instructions.Where(i => i.Offset >= edge.BranchTarget && i.Offset <= edge.Offset).ToArray())
            .Where(loop => loop.Any(i => i.OpCode == OpCodes.Add) &&
                loop.All(i => i.Member?.Name is not ("GetProperty" or "ConvertToNumber" or
                    "TryGetValue" or "HasPropertyDescriptors" or "TryGetMaterializedDictionary")))
            .ToArray();
        Assert.True(numericLoops.Length >= 2, "Expected guarded integer and double loops without property reads.");
        Assert.All(numericLoops, loop => Assert.Contains(loop, i => i.Member?.Name == "_cancelRequested"));
        Assert.Contains(instructions, i => i.Member?.Name == "GetProperty");

        var reduce = method.CreateDelegate<Func<double, double>>();
        Assert.Equal(300_000, reduce(100_000));
        reduce(1_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double small = reduce(1_000);
        long smallBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double large = reduce(100_000);
        long largeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(3_000, small);
        Assert.Equal(300_000, large);
        Assert.True(largeBytes <= smallBytes + 1_024, $"Allocations scaled: {smallBytes} -> {largeBytes}");
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

        var cancel = assembly.GetType("$Runtime")!.GetField("_cancelRequested", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        cancel.SetValue(null, true);
        Assert.ThrowsAny<Exception>(() => reduce(100));
        cancel.SetValue(null, false);
        Assert.Equal(300, reduce(100));
    }

    [Fact]
    public void DictionaryShapeWithoutCompactCarrier_StillHasNumericLoops()
    {
        const string source = """
            type Wide = { a: number; b: number; c: number; d: number; e: number };
            function reduce(n: number): number {
                const point: Wide = { a: 1, b: 2, c: 3, d: 4, e: 5 };
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { a, e } = point;
                    total = total + a + e;
                }
                return total;
            }
            """;
        Assembly assembly = Compile(source);
        var method = FindFunction(assembly, "reduce");
        Assert.Equal(600, method.CreateDelegate<Func<double, double>>()(100));
        Assert.Contains(ReadInstructions(method), i => i.Member?.Name == "HasPropertyDescriptors");
        Assert.Contains(ReadInstructions(method), i => i.OpCode == OpCodes.Div);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Theory, ModeData]
    public void MaterializedValues_PreserveNumberSemanticsAndZeroTripReads(ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };
            function reduce(n: number, start: number, arg: Point): number {
                const point: Point = arg;
                let total: number = start;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }
            const point: Point = { x: 1, y: 2 };
            const dynamicPoint: any = point;
            dynamicPoint.extra = true;
            dynamicPoint.x = 0.5;
            dynamicPoint.y = 0.25;
            console.log(reduce(3, 0.5, point), reduce(2.5, 0.5, point));
            console.log(Object.is(reduce(0, -0, null as any), -0));
            console.log(reduce(-1, 7, null as any), reduce(NaN, 8, null as any));
            dynamicPoint.x = 1;
            dynamicPoint.y = 2;
            console.log(reduce(2, 9007199254740991, point));
            dynamicPoint.x = -0;
            dynamicPoint.y = -0;
            console.log(Object.is(reduce(2, -0, point), -0));
            dynamicPoint.x = Infinity;
            dynamicPoint.y = -Infinity;
            console.log(Number.isNaN(reduce(2, 0, point)));
            dynamicPoint.x = NaN;
            console.log(Number.isNaN(reduce(1, 0, point)));
            """;
        Assert.Equal("2.75 2.75\ntrue\n7 8\n9007199254740998\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericSnapshots_PreserveAdditionAssociation(ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };
            function reduce(n: number): number {
                const point: Point = { x: -10000000000000000, y: 1 };
                const dynamicPoint: any = point;
                dynamicPoint.extra = true;
                let total: number = 10000000000000000;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }
            console.log(reduce(1));
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MaterializedCarrier_ReadsCanonicalValuesAndSeesMutationInsideLoop(ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };
            function reduce(n: number): number {
                const points: Point[] = [];
                points.push({ x: 1, y: 2 });
                points.push({ x: 3, y: 4 });
                const point: Point = points[0];
                const dynamicPoint: any = point;
                dynamicPoint.x = 10;
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                for (let i: number = 0; i < n; i++) {
                    dynamicPoint.x = i;
                    const { x, y } = point;
                    total = total + x + y;
                }
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = points[i % 2];
                    total = total + x + y;
                }
                return total;
            }
            console.log(reduce(4));
            """;
        Assert.Equal("86\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DictionaryFallbacks_PreserveGettersMissingKeysAndProxies(ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };
            function reduce(arg: Point, n: number): number {
                const point: Point = arg;
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }
            let trace: string = "";
            const point: any = { x: 1, y: 2 };
            Object.defineProperty(point, "x", { get: () => { trace = trace + "x"; point.y++; return 1; }, configurable: true });
            console.log(reduce(point, 2), trace);
            Object.defineProperty(point, "x", { get: undefined, set: (v: number) => {}, configurable: true });
            console.log(Number.isNaN(reduce(point, 1)));
            delete point.x;
            Object.setPrototypeOf(point, { get x() { trace = trace + "p"; return 5; } });
            console.log(reduce(point, 1), trace);
            delete point.y;
            console.log(Number.isNaN(reduce(point, 1)));
            const proxy = new Proxy({ x: 1, y: 2 }, { get: (target: any, key: string) => { trace = trace + key; return target[key]; } });
            console.log(reduce(proxy, 2), trace);
            """;
        Assert.Equal("9 xx\ntrue\n9 xxp\ntrue\n6 xxppxyxy\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void MaterializedProbe_DoesNotMaterializeUntouchedCarrier()
    {
        Assembly assembly = Compile(MaterializedLoopSource(true));
        Type carrier = assembly.GetTypes().Single(t => t.Name.StartsWith("$CompactObjectRecord", StringComparison.Ordinal));
        object point = Activator.CreateInstance(carrier, 1d, 2d)!;
        var probe = carrier.GetMethod("TryGetMaterializedDictionary", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { null };
        Assert.Equal(false, probe.Invoke(point, args));
        Assert.Null(args[0]);
        carrier.GetMethod("SetProperty")!.Invoke(point, ["x", 9d]);
        Assert.Equal(true, probe.Invoke(point, args));
        var dictionary = Assert.IsType<Dictionary<string, object>>(args[0]);
        Assert.Equal(9d, dictionary["x"]);
        object sibling = Activator.CreateInstance(carrier, 3d, 4d)!;
        Assert.Equal(false, probe.Invoke(sibling, args));
        Assert.Null(args[0]);
        var isMaterialized = carrier.GetMethod("get_IsMaterialized", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(false, isMaterialized.Invoke(sibling, null));
    }

    [Fact]
    public void MaterializedSnapshot_ChecksDescriptorsOnTheOriginalReceiver()
    {
        string source = MaterializedLoopSource(true) + """
            export function sum(n: number, arg: Point): number {
                const point: Point = arg;
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }
            """;
        Assembly assembly = Compile(source);
        Type carrier = assembly.GetTypes().Single(t => t.Name.StartsWith("$CompactObjectRecord", StringComparison.Ordinal));
        object point = Activator.CreateInstance(carrier, 1d, 2d)!;
        carrier.GetMethod("SetProperty")!.Invoke(point, ["extra", true]);
        var sum = FindFunction(assembly, "sum").CreateDelegate<Func<double, object, double>>();
        Assert.Equal(6, sum(2, point));

        // Attach an accessor with an undefined getter through the emitted runtime, keeping
        // the compiler's numeric optimization enabled. The raw dictionary still
        // contains x=1, but ordinary [[Get]] on the carrier returns undefined.
        var descriptorType = assembly.GetType("$CompiledPropertyDescriptor")!;
        var descriptor = Activator.CreateInstance(descriptorType)!;
        object undefined = assembly.GetType("$Undefined")!.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        descriptorType.GetProperty("Getter")!.SetValue(descriptor, undefined);
        descriptorType.GetProperty("Value")!.SetValue(descriptor, undefined);
        assembly.GetType("$PropertyDescriptorStore")!.GetMethod("DefineProperty")!
            .Invoke(null, [point, "x", descriptor]);
        Assert.True(double.IsNaN(sum(2, point)));
    }

    [Theory, ModeData]
    public void NumericSnapshots_RejectInterveningEffectsAndRepeatCoercions(ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };
            let calls: number = 0;
            function reduce(n: number): number {
                const point: Point = { x: 1, y: 2 };
                const alias: any = point;
                alias.x = { valueOf(): number { calls++; return 1; } };
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }
            console.log(reduce(3), calls);
            const point: any = { x: 1, y: 2 };
            const alias: any = point;
            alias.x = undefined;
            function defaultX(): number { point.y = 9; return 1; }
            const { x = defaultX(), y } = point;
            console.log(x, y);
            point.x = 1;
            function mutate(): void { point.x = point.x! + 1; }
            for (let i: number = 0; i < 2; i++) {
                mutate();
                const { x, y } = point;
                console.log(x, y);
            }
            """;
        Assert.Equal("9 3\n1 9\n2 9\n3 9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericReductions_DoNotConfuseShadowedBindingsWithLoopControl(ExecutionMode mode)
    {
        const string source = """
            function reduce(n: number): number {
                const point = { n: 1, y: 2 };
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const { n, y } = point;
                    total = total + n + y;
                }
                return total;
            }
            console.log(reduce(3));
            """;
        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }
}
