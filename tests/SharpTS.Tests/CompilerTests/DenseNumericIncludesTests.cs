using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class DenseNumericIncludesTests
{
    private const string StableSource = """
        function scan(size: number, passes: number): number {
            const values: number[] = [];
            for (let i: number = 0; i < size; i++) values.push(i);
            let found: number = 0;
            for (let pass: number = 0; pass < passes; pass++) {
                if (values.includes(-1)) found = found + 1;
            }
            return found;
        }
        """;

    [Theory, ModeData]
    public void DenseNumericIncludes_PreservesSameValueZeroAndFromIndex(ExecutionMode mode)
    {
        const string source = """
            const values: number[] = [];
            values.push(-0, 1, NaN, 3);
            console.log(values.includes(1), values.includes(2));
            console.log(values.includes(NaN), values.includes(+0));
            console.log(values.includes(1, 1), values.includes(1, 2));
            console.log(values.includes(1, -3), values.includes(1, -2));
            console.log(values.includes(3, 3.9), values.includes(1, -3.9));
            console.log(values.includes(0, Infinity), values.includes(0, -Infinity));
            const empty: number[] = [];
            console.log(empty.includes(0));
            """;

        Assert.Equal(
            "true false\ntrue true\ntrue false\ntrue false\ntrue true\nfalse true\nfalse\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void DenseNumericIncludes_UsesTypedHelperWithoutBoxing()
    {
        Assembly assembly = Compile(StableSource);
        MethodInfo scan = FindFunction(assembly, "scan");
        var calls = ReadInstructions(scan)
            .Where(instruction => instruction.Operand is MethodBase)
            .Select(instruction => (MethodBase)instruction.Operand!)
            .ToArray();

        Assert.Contains(calls, method => method.Name == "ArrayIncludesDouble");
        Assert.DoesNotContain(calls, method => method.Name == "ArrayIncludes");
        MethodInfo helper = assembly.GetType("$Runtime")!.GetMethod("ArrayIncludesDouble")!;
        Assert.DoesNotContain(ReadInstructions(helper), instruction => instruction.OpCode == OpCodes.Box);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(StableSource));
    }

    [Fact]
    public void DenseNumericIncludes_DoesNotAllocatePerScannedElement()
    {
        MethodInfo scan = FindFunction(Compile(StableSource), "scan");
        double Invoke(double passes) => Convert.ToDouble(scan.Invoke(null, [10_000.0, passes]));
        Assert.Equal(0, Invoke(2));

        long before = GC.GetAllocatedBytesForCurrentThread();
        double small = Invoke(1);
        long smallAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double large = Invoke(100);
        long largeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, small);
        Assert.Equal(0, large);
        Assert.True(largeAllocated <= smallAllocated + 2_048,
            $"Dense includes allocations scaled: {smallAllocated} vs {largeAllocated} bytes.");
        Assert.True(largeAllocated < 1_544_500,
            $"Dense includes allocated {largeAllocated} bytes; the 95% reduction gate is 1,544,500 bytes.");
    }

    [Fact]
    public void ObservableIncludesCases_RetainGenericSemantics()
    {
        const string source = """
            const holey: any[] = [,];
            console.log(holey.includes(undefined));

            Object.defineProperty(Array.prototype, "0", {
                configurable: true,
                get(): number { return 7; }
            });
            const inherited: any[] = [];
            inherited.length = 1;
            console.log(inherited.includes(7));
            delete (Array.prototype as any)[0];

            const omitted: any[] = [undefined];
            console.log(omitted.includes());

            let coercions: number = 0;
            const fromIndex: any = {
                valueOf(): number { coercions = coercions + 1; return 0; }
            };
            console.log(([3] as number[]).includes(3, fromIndex), coercions);
            """;

        Assert.Equal("true\ntrue\ntrue\ntrue 1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ReplacedAndDeletedIncludes_RemainObservable()
    {
        const string replaced = """
            const original: any = Array.prototype.includes;
            Array.prototype.includes = function(value: any): boolean { return false; };
            const values: number[] = [];
            values.push(1);
            console.log(values.includes(1));
            Array.prototype.includes = original;
            """;
        Assert.Equal("false\n", TestHarness.RunCompiled(replaced));

        const string deleted = """
            const original: any = Array.prototype.includes;
            delete (Array.prototype as any).includes;
            const values: number[] = [];
            values.push(1);
            try { values.includes(1); } catch (error) {
                console.log(error instanceof TypeError);
            }
            Array.prototype.includes = original;
            """;
        Assert.Equal("true\n", TestHarness.RunCompiled(deleted));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"dense_numeric_includes_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Method '{method.Name}' has no IL body.");
        Module module = method.Module;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            MemberInfo? operand = null;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineField)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType switch
                {
                    OperandType.InlineMethod => module.ResolveMethod(token),
                    OperandType.InlineField => module.ResolveField(token),
                    _ => module.ResolveType(token)
                };
            }
            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException($"Unsupported IL operand type {opCode.OperandType}.")
            };
            offset += operandSize;
            yield return (opCode, operand);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
