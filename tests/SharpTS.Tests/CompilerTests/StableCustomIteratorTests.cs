using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableCustomIteratorTests
{
    private const string StableSource = """
        function iterate(n: number): number {
            let current: number = 0;
            const iterable = {
                [Symbol.iterator]() { return this; },
                next() {
                    if (current < n) {
                        const value: number = current;
                        current = current + 1;
                        return { value, done: false };
                    }
                    return { value: 0, done: true };
                }
            };
            let total: number = 0;
            for (const value of iterable) total = total + value;
            return total;
        }
        """;

    [Theory, ModeData]
    public void StableCustomIterator_ReturnsExpectedValues(ExecutionMode mode)
    {
        Assert.Equal("499500\n", TestHarness.Run(
            StableSource + "\nconsole.log(iterate(1000));", mode));
    }

    [Fact]
    public void StableCustomIterator_UsesDirectNextAndCompactResultSlots()
    {
        Assembly assembly = Compile(StableSource);
        MethodInfo iterate = FindFunction(assembly, "iterate");
        var instructions = ReadInstructions(iterate).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode is var op && (op == OpCodes.Call || op == OpCodes.Callvirt) &&
            instruction.Operand is MethodBase { Name: "Invoke" } method &&
            method.DeclaringType?.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal) == true);
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand?.DeclaringType?.Name.StartsWith(
                "$StableNumberIteratorResult", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "InvokeIteratorNext" or "GetIteratorDone" or "GetIteratorValue"
            });
        Assert.Contains(assembly.GetTypes().SelectMany(type => type.GetFields()), field =>
            field.Name == "current" && field.FieldType == typeof(double));
        MethodInfo next = assembly.GetTypes().SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static))
            .Single(method => method.ReturnType.Name == "$StableNumberIteratorResult");
        Assert.DoesNotContain(ReadInstructions(next), instruction =>
            instruction.OpCode == OpCodes.Box);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(StableSource));
    }

    [Fact]
    public void StableCustomIterator_DoesNotAllocatePerValue()
    {
        MethodInfo iterate = FindFunction(Compile(StableSource), "iterate");
        double Invoke(double n) => Convert.ToDouble(iterate.Invoke(null, [n]));
        Assert.Equal(4_999_950_000, Invoke(100_000));

        long before = GC.GetAllocatedBytesForCurrentThread();
        double smallResult = Invoke(1_000);
        long smallAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double largeResult = Invoke(100_000);
        long largeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(499_500, smallResult);
        Assert.Equal(4_999_950_000, largeResult);
        Assert.True(largeAllocated <= smallAllocated + 2_048,
            $"Stable iterator allocations scaled: {smallAllocated} vs {largeAllocated} bytes.");
    }

    [Theory, ModeData]
    public void EscapedAndReassignedNext_RetainsGenericProtocol(ExecutionMode mode)
    {
        const string source = """
            let current: number = 0;
            const iterable: any = {
                [Symbol.iterator]() { return this; },
                next() { return { value: current++, done: current > 3 }; }
            };
            const alias: any = iterable;
            alias.next = () => ({ value: 9, done: true });
            let count: number = 0;
            for (const value of iterable) count = count + value;
            console.log(count);
            """;

        Assert.Equal("0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturedIteratorBinding_RetainsGenericProtocol(ExecutionMode mode)
    {
        const string source = """
            function run(): number {
                let current: number = 0;
                const iterable = {
                    [Symbol.iterator]() { return this; },
                    next() {
                        const value: number = current++;
                        return { value, done: current > 3 };
                    }
                };
                const retain = () => iterable;
                let total: number = 0;
                for (const value of iterable) total = total + value;
                if (retain() !== iterable) return -1;
                return total;
            }
            console.log(run());
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Break_ClosesStableIteratorExactlyOnce(ExecutionMode mode)
    {
        const string source = """
            let current: number = 0;
            let closes: number = 0;
            const iterable = {
                [Symbol.iterator]() { return this; },
                next() {
                    const value: number = current++;
                    return { value, done: false };
                },
                return() { closes = closes + 1; return { value: 0, done: true }; }
            };
            for (const value of iterable) { console.log(value); break; }
            console.log("closes=" + closes);
            """;

        Assert.Equal("0\ncloses=1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ThrowFromStableNext_DoesNotCallReturn()
    {
        const string source = """
            function run(): string {
                let current: number = 0;
                let closes: number = 0;
                const iterable = {
                    [Symbol.iterator]() { return this; },
                    next() {
                        current++;
                        if (current === 2) throw new Error("next failed");
                        return { value: current, done: false };
                    },
                    return() {
                        closes++;
                        return { value: 0, done: true };
                    }
                };
                let message: string = "none";
                try {
                    for (const value of iterable) { }
                } catch (error: any) {
                    message = error.message;
                }
                return message + ":" + closes;
            }
            console.log(run());
            """;

        Assert.Equal("next failed:0\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_custom_iterator_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    [Fact]
    public void DynamicIterator_UsesCapturedProtocolAndTypedClosureFields()
    {
        string source = StableSource.Replace("let total: number = 0;", """
            const alias: any = iterable;
            alias.next = alias.next;
            let total: number = 0;
            """);
        Assembly assembly = Compile(source);
        MethodInfo iterate = FindFunction(assembly, "iterate");
        Assert.Contains(ReadInstructions(iterate), instruction =>
            instruction.Operand is MethodBase { Name: "InvokeCapturedIteratorNext" });
        Assert.Contains(ReadInstructions(iterate), instruction =>
            instruction.Operand is MethodBase { Name: "GetIteratorDone" });
        Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == "$StableNumberIteratorResult");
        Assert.Contains(assembly.GetTypes().SelectMany(type => type.GetFields()), field =>
            field.Name == "current" && field.FieldType == typeof(double));
        Assert.Contains(assembly.GetTypes().SelectMany(type => type.GetFields()), field =>
            field.Name == "n" && field.FieldType == typeof(double));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

        var invoke = iterate.CreateDelegate<Func<double, object>>();
        Assert.Equal(4_999_950_000d, invoke(100_000));
        // Warm the generated call adapters before measuring managed allocation.
        for (int i = 0; i < 5; i++) invoke(10_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        object small = invoke(1_000);
        long smallBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        object large = invoke(100_000);
        long largeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(499_500d, small);
        Assert.Equal(4_999_950_000d, large);
        Assert.True(largeBytes - smallBytes < 128L * 99_000,
            $"Generic iterator allocation grew by {largeBytes - smallBytes} bytes.");
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
