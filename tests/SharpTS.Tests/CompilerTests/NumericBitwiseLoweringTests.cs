using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Structural regression coverage for the allocation-free #1421 bitwise path.</summary>
public sealed class NumericBitwiseLoweringTests
{
    private readonly ITestOutputHelper _output;

    public NumericBitwiseLoweringTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void NumericTypedArrayUpdate_StaysUnboxedBetweenAccessors()
    {
        Assembly assembly = Compile("""
            function update(index: number): number {
                const tape = new Uint8Array(1);
                tape[index] = (tape[index] + 1) & 255;
                return tape[index];
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "update")).ToArray();
        int get = Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetUnboxed" });
        int set = Array.FindIndex(instructions, get + 1, instruction =>
            instruction.Operand is MethodBase { Name: "SetUnboxed" });

        Assert.True(get >= 0,
            "Expected typed-array GetUnboxed call. Calls: " +
            string.Join(", ", instructions
                .Where(instruction => instruction.Operand is MethodBase)
                .Select(instruction => ((MethodBase)instruction.Operand!).Name)));
        Assert.True(set > get, "Expected typed-array SetUnboxed call after GetUnboxed.");

        var hotSlice = instructions[(get + 1)..set];
        Assert.Contains(hotSlice, instruction =>
            instruction.Operand is MethodBase { Name: "JsNumberToInt32" });
        Assert.DoesNotContain(hotSlice, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
        Assert.DoesNotContain(hotSlice, instruction =>
            instruction.Operand is MethodBase { Name: "JsToInt32" or "ConvertToNumber" });
    }

    [Fact]
    public void DynamicOperands_RetainObjectCoercionPath()
    {
        Assembly assembly = Compile("""
            function combine(left: any, right: any): number {
                return left & right;
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "combine")).ToArray();
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Operand is MethodBase { Name: "JsToInt32" }));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "JsNumberToInt32" });
    }

    [Fact]
    public void NumericBitwise_PassesIlVerification()
    {
        const string source = """
            function update(index: number): number {
                const tape = new Uint8Array(1);
                tape[index] = (tape[index] + 1) & 255;
                return tape[index];
            }
            console.log(update(0));
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void NumericTypedArrayBitwiseLoop_HasNoPerIterationAllocation()
    {
        Assembly assembly = Compile("""
            function hot(iterations: number): number {
                const tape = new Uint8Array(1);
                let i: number = 0;
                while (i < iterations) {
                    tape[0] = (tape[0] + 1) & 255;
                    i = i + 1;
                }
                return tape[0];
            }
            """);

        var hot = FindFunction(assembly, "hot").CreateDelegate<Func<double, double>>();
        Assert.Equal(232, hot(1_000)); // Warm JIT and emitted runtime state.

        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = hot(1_000_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(64, result);
        _output.WriteLine($"One million bitwise iterations allocated {allocated:N0} bytes.");
        Assert.InRange(allocated, 0, 4_096);
    }

    [Fact]
    public void BrainfuckN5000_MeetsAllocationGate()
    {
        Assembly assembly = Compile("""
            function buildProgram(reps: number): string {
                let s: string = "";
                for (let i: number = 0; i < reps; i++) {
                    s = s + "+++++[>+<-]>[<+>-]<";
                }
                return s;
            }

            function buildJumps(program: string): number[] {
                const len: number = program.length;
                const jumps: number[] = [];
                for (let i: number = 0; i < len; i++) jumps.push(0);
                const stack: number[] = [];
                for (let i: number = 0; i < len; i++) {
                    const c: number = program.charCodeAt(i);
                    if (c === 91) {
                        stack.push(i);
                    } else if (c === 93) {
                        const open: number = stack[stack.length - 1];
                        stack.pop();
                        jumps[open] = i;
                        jumps[i] = open;
                    }
                }
                return jumps;
            }

            function runBF(program: string, jumps: number[]): number {
                const TAPE: number = 4096;
                const tape = new Uint8Array(TAPE);
                let ptr: number = 0;
                let ip: number = 0;
                const len: number = program.length;
                while (ip < len) {
                    const c: number = program.charCodeAt(ip);
                    if (c === 43) {
                        tape[ptr] = (tape[ptr] + 1) & 255;
                    } else if (c === 45) {
                        tape[ptr] = (tape[ptr] - 1) & 255;
                    } else if (c === 62) {
                        ptr = ptr + 1;
                    } else if (c === 60) {
                        ptr = ptr - 1;
                    } else if (c === 91) {
                        if (tape[ptr] === 0) ip = jumps[ip];
                    } else if (c === 93) {
                        if (tape[ptr] !== 0) ip = jumps[ip];
                    }
                    ip = ip + 1;
                }
                let sum: number = 0;
                for (let i: number = 0; i < TAPE; i++) sum = sum + tape[i];
                return sum;
            }
            """);

        var run = FindFunction(assembly, "runBF")
            .CreateDelegate<BrainfuckRunner>();

        string program = (string)FindFunction(assembly, "buildProgram")
            .Invoke(null, [5_000d])!;
        object jumps = FindFunction(assembly, "buildJumps")
            .Invoke(null, [program])!;
        Assert.Equal(168, run(program, jumps)); // Warm JIT and emitted runtime state.

        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = run(program, jumps);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(168, result);
        _output.WriteLine($"Brainfuck N=5,000 runBF allocated {allocated:N0} bytes.");
        Assert.InRange(allocated, 0, 35_000_000);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1421_bitwise_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
        MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Method '{method.Name}' has no IL body.");
        Module module = method.Module;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];
            MemberInfo? operand = null;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType == OperandType.InlineMethod
                    ? module.ResolveMethod(token)
                    : module.ResolveType(token);
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
            yield return (opCode, operand);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

    private delegate double BrainfuckRunner(string program, object jumps);
}
