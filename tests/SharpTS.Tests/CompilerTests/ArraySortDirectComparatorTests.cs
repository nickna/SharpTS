using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Structural coverage for #1388: only comparators whose arrow identity is
/// statically proven lower through the direct sort helper. Dynamic comparator
/// values retain the general callable path.
/// </summary>
public sealed class ArraySortDirectComparatorTests
{
    [Fact]
    public void BenchmarkRecordPush_PreservesTypedCompactCarrierAndComparatorLoads()
    {
        Assembly assembly = Compile("""
            function makeRecords(n: number): { key: number; tag: string }[] {
                const out: { key: number; tag: string }[] = [];
                let state: number = 987654321;
                for (let i: number = 0; i < n; i++) {
                    state = (state * 48271) % 2147483647;
                    out.push({ key: state, tag: "t" + (state % 1000) });
                }
                return out;
            }

            function sortRecords(src: { key: number; tag: string }[]): number {
                const c = src.slice();
                c.sort((a: { key: number; tag: string }, b: { key: number; tag: string }): number => a.key - b.key);
                return c[0].key;
            }
            """);

        MethodInfo makeRecords = FindFunction(assembly, "makeRecords");
        var records = Assert.IsAssignableFrom<List<object>>(
            makeRecords.Invoke(null, [32.0]));
        Type carrier = records[0].GetType();
        MethodInfo sortRecords = FindFunction(assembly, "sortRecords");
        MethodInfo arrow = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.StartsWith("<>Arrow_", StringComparison.Ordinal) &&
                !method.Name.Contains('$'));

        Assert.StartsWith("$CompactObjectRecord", carrier.Name, StringComparison.Ordinal);
        Assert.Equal(
            [typeof(double), typeof(string)],
            carrier.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => field.Name.StartsWith("_v", StringComparison.Ordinal))
                .Select(field => field.FieldType)
                .ToArray());
        Assert.Contains(CalledMethods(sortRecords), method =>
            method.Name == "ArraySortDirectNumber");
        Assert.Contains(Instructions(arrow), instruction =>
            instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand is FieldInfo { FieldType: { } fieldType } &&
            fieldType == typeof(double));
        Assert.Equal(2, CalledMethods(arrow).Count(method =>
            method.Name == "ConvertToNumber"));

        double first = Assert.IsType<double>(sortRecords.Invoke(null, [records]));
        Assert.True(first > 0);
    }

    [Fact]
    public void ObservedPushResult_RetainsOrdinaryRecordLiteral()
    {
        Assembly assembly = Compile("""
            type Item = { key: number; tag: string };
            function makeRecord(): Item {
                const items: Item[] = [];
                const length = items.push({ key: 1, tag: "x" });
                return items[length - 1];
            }
            """);

        object record = FindFunction(assembly, "makeRecord").Invoke(null, [])!;
        Assert.IsType<Dictionary<string, object>>(record);
    }

    [Fact]
    public void StableNumericArrow_UsesUnboxedHelper_WhileObjectAndDynamicComparatorsDoNot()
    {
        Assembly assembly = Compile("""
            function sortStable(values: number[]): number[] {
                values.sort((a: number, b: number): number => a - b);
                return values;
            }

            const stableCompare = (a: number, b: number): number => a - b;
            function sortConst(values: number[]): number[] {
                values.sort(stableCompare);
                return values;
            }

            function sortObject(values: any[]): any[] {
                values.sort((a: any, b: any): any => a.rank - b.rank);
                return values;
            }

            function sortDynamic(
                values: number[],
                compare: (a: number, b: number) => number
            ): number[] {
                values.sort(compare);
                return values;
            }
            """);

        Assembly recordAssembly = Compile("""
            interface Item { rank: number; label: string; }
            const sample: Item = { rank: 1, label: "sample" };

            function sortRecords(values: Item[]): Item[] {
                values.sort((a: Item, b: Item): number => a.rank - b.rank);
                return values;
            }
            """);

        MethodInfo stable = FindFunction(assembly, "sortStable");
        MethodInfo constBound = FindFunction(assembly, "sortConst");
        MethodInfo records = FindFunction(recordAssembly, "sortRecords");
        MethodInfo objectReturning = FindFunction(assembly, "sortObject");
        MethodInfo dynamic = FindFunction(assembly, "sortDynamic");

        Assert.Contains(CalledMethods(stable), method => method.Name == "ArraySortDirectNumber");
        Assert.DoesNotContain(CalledMethods(stable), method => method.Name == "ArraySortDirect");
        Assert.DoesNotContain(CalledMethods(stable), method => method.Name == "ArraySort");
        Assert.Contains(CalledMethods(constBound), method => method.Name == "ArraySortDirectNumber");
        Assert.Contains(CalledMethods(records), method => method.Name == "ArraySortDirectNumber");
        Assert.Contains(CalledMethods(objectReturning), method => method.Name == "ArraySortDirect");
        Assert.DoesNotContain(CalledMethods(objectReturning), method => method.Name == "ArraySortDirectNumber");
        Assert.Contains(CalledMethods(dynamic), method => method.Name == "ArraySort");
        Assert.DoesNotContain(CalledMethods(dynamic), method => method.Name == "ArraySortDirect");
        Assert.DoesNotContain(CalledMethods(dynamic), method => method.Name == "ArraySortDirectNumber");

        MethodInfo[] adapters = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.Contains("$nbox2", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(adapters);
        Assert.All(adapters, adapter =>
        {
            Assert.Equal(typeof(double), adapter.ReturnType);
            Assert.DoesNotContain(Instructions(adapter), instruction =>
                instruction.OpCode == OpCodes.Box &&
                instruction.Operand is Type type && type == typeof(double));
        });

        var programMethods = recordAssembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .ToArray();
        MethodInfo[] recordArrows = programMethods
            .Where(method => Instructions(method).Any(instruction =>
                instruction.OpCode == OpCodes.Ldfld &&
                instruction.Operand is FieldInfo field &&
                field.DeclaringType?.Name.StartsWith(
                    "$CompactObjectRecord", StringComparison.Ordinal) == true))
            .ToArray();
        Assert.True(recordArrows.Length == 1,
            "Expected one compact-record arrow; types were: " +
            string.Join(", ", recordAssembly.GetTypes().Select(type => type.Name)) +
            "; fields were: " +
            string.Join(", ", programMethods.SelectMany(method =>
                Instructions(method)
                    .Where(instruction => instruction.Operand is FieldInfo)
                    .Select(instruction =>
                        $"{method.Name}:{((FieldInfo)instruction.Operand!).DeclaringType?.Name}.{((FieldInfo)instruction.Operand!).Name}"))));
        Assert.Equal(typeof(double), recordArrows[0].ReturnType);
    }

    [Fact]
    public void StableNumericArrow_DoesNotAllocateABoxPerComparison()
    {
        Assembly assembly = Compile("""
            function sortStable(values: number[]): number {
                values.sort((a: number, b: number): number => a - b);
                return values[0];
            }
            """);
        var sort = FindFunction(assembly, "sortStable")
            .CreateDelegate<Func<List<object>, double>>();

        sort([2.0, 1.0]);
        var values = new List<object>(10_000);
        long state = 123456789;
        for (int i = 0; i < 10_000; i++)
        {
            state = state * 48271 % 2147483647;
            values.Add((double)state);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        double first = sort(values);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(first > 0);
        Assert.True(
            allocated <= 750_000,
            $"Stable numeric sort allocated {allocated:N0} bytes; comparator results may be boxed.");
    }

    [Fact]
    public void FreshNumericSliceSort_UsesTypedStableKernel()
    {
        Assembly assembly = Compile("""
            function makeNumbers(n: number): number[] {
                const values: number[] = [];
                let state: number = 123456789;
                for (let i: number = 0; i < n; i++) {
                    state = (state * 48271) % 2147483647;
                    values.push(state);
                }
                return values;
            }

            function sortNumbers(source: number[]): number {
                const copy: number[] = source.slice();
                copy.sort((left: number, right: number): number => left - right);
                return copy[0] + copy[copy.length - 1];
            }

            function stableSignedZeros(): boolean {
                const source: number[] = [2, 0, -0, 1];
                const copy: number[] = source.slice();
                copy.sort((left: number, right: number): number => left - right);
                return 1 / copy[0] === Infinity
                    && 1 / copy[1] === -Infinity
                    && copy[2] === 1
                    && copy[3] === 2
                    && source[0] === 2;
            }
            """);

        MethodInfo sortNumbers = FindFunction(assembly, "sortNumbers");
        MethodBase[] calls = CalledMethods(sortNumbers).ToArray();
        Assert.Contains(calls, method => method.Name == "ArraySliceNumber");
        Assert.Contains(calls, method => method.Name == "ArraySortNumeric");
        Assert.DoesNotContain(calls, method => method.Name == "ArraySortDirectNumber");
        Assert.DoesNotContain(calls, method => method.Name == "EnsureBoxed");

        MethodInfo kernel = assembly.GetType("$Array")!.GetMethod(
            "SortNumeric",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing packed numeric sort kernel.");
        var kernelInstructions = Instructions(kernel).ToArray();
        Assert.Contains(kernelInstructions, instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand is Type type
            && type == typeof(double));
        Assert.DoesNotContain(kernelInstructions, instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand is Type type
            && type == typeof(object));
        Assert.DoesNotContain(kernelInstructions, instruction =>
            instruction.OpCode == OpCodes.Box
            && instruction.Operand is Type type
            && type == typeof(double));
        Assert.Contains(CalledMethods(kernel), method =>
            method.DeclaringType == typeof(Func<double, double, double>)
            && method.Name == "Invoke");
        Assert.DoesNotContain(
            kernel.GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(object[]));

        var numbers = Assert.IsAssignableFrom<List<object>>(
            FindFunction(assembly, "makeNumbers").Invoke(null, [1_000.0]));
        double checksum = Assert.IsType<double>(sortNumbers.Invoke(null, [numbers]));
        Assert.True(checksum > 0);
        MethodInfo stableSignedZeros = FindFunction(assembly, "stableSignedZeros");
        Assert.Contains(CalledMethods(stableSignedZeros), method =>
            method.Name == "ArraySortNumeric");
        Assert.True(Assert.IsType<bool>(stableSignedZeros.Invoke(null, [])));
    }

    [Fact]
    public void FreshNumericSliceSort_HolesAndUndefinedUseRuntimeFallback()
    {
        Assembly assembly = Compile("""
            function makeHoley(): number[] {
                const values: number[] = [];
                values[0] = 3;
                values[2] = 1;
                return values;
            }

            function makeUndefined(): number[] {
                return [3, undefined as any, 1];
            }

            function sortFirst(source: number[]): number {
                const copy: number[] = source.slice();
                copy.sort((left: number, right: number): number => left - right);
                return copy[0];
            }
            """);

        MethodInfo sortFirst = FindFunction(assembly, "sortFirst");
        Assert.Contains(CalledMethods(sortFirst), method => method.Name == "ArraySortNumeric");

        var holey = Assert.IsAssignableFrom<List<object>>(
            FindFunction(assembly, "makeHoley").Invoke(null, []));
        var undefined = Assert.IsAssignableFrom<List<object>>(
            FindFunction(assembly, "makeUndefined").Invoke(null, []));
        Assert.Equal(1.0, Assert.IsType<double>(sortFirst.Invoke(null, [holey])));
        Assert.Equal(1.0, Assert.IsType<double>(sortFirst.Invoke(null, [undefined])));
    }

    [Theory]
    [InlineData("const alias: number[] = copy; copy.sort((a: number, b: number): number => a - b); return alias[0];")]
    [InlineData("copy.sort((a: number, b: number): number => a - b); return copy.sort((a: number, b: number): number => a - b)[0];")]
    [InlineData("Object.freeze(copy); copy.sort((a: number, b: number): number => a - b); return copy[0];")]
    [InlineData("Object.defineProperty(copy, '0', { value: 4 }); copy.sort((a: number, b: number): number => a - b); return copy[0];")]
    [InlineData("Object.setPrototypeOf(copy, Array.prototype); copy.sort((a: number, b: number): number => a - b); return copy[0];")]
    [InlineData("copy.sort((a: number, b: number): number => { copy.push(0); return a - b; }); return copy[0];")]
    [InlineData("copy.sort((a: number, b: number): number => { throw new Error('stop'); }); return copy[0];")]
    public void ObservableNumericSliceSortShapes_RetainGeneralPath(string body)
    {
        Assembly assembly = Compile($$"""
            function sortObserved(source: number[]): number {
                const copy: number[] = source.slice();
                {{body}}
            }
            """);

        MethodInfo method = FindFunction(assembly, "sortObserved");
        Assert.DoesNotContain(CalledMethods(method), called =>
            called.Name == "ArraySortNumeric");
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1388_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name)
        => assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
        => Instructions(method)
            .Where(instruction => instruction.Operand is MethodBase)
            .Select(instruction => (MethodBase)instruction.Operand!);

    private static IEnumerable<(OpCode OpCode, object? Operand)> Instructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException(
                $"Method '{method.Name}' has no IL body.");
        Module module = method.Module;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, offset);
                yield return (opCode, module.ResolveMethod(token)
                    ?? throw new InvalidOperationException(
                        $"Could not resolve method token {token}."));
            }
            else if (opCode.OperandType == OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                yield return (opCode, module.ResolveType(token));
            }
            else if (opCode.OperandType == OperandType.InlineField)
            {
                int token = BitConverter.ToInt32(il, offset);
                yield return (opCode, module.ResolveField(token));
            }
            else
            {
                yield return (opCode, null);
            }

            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or
                    OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or
                    OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch =>
                    4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException(
                    $"Unsupported IL operand type {opCode.OperandType}.")
            };

            offset += operandSize;
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
