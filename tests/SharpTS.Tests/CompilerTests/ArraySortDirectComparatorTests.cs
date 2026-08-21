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
